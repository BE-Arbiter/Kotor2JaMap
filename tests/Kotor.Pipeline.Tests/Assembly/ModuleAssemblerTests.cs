using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class ModuleAssemblerTests
{
    private static readonly KotorTexture Tex = new(2, 2, new byte[16], false, TxiInfo.Empty);

    /// <summary>Partie synthetique : un triangle de texture <paramref name="texture"/> et un sol carre x0..x0+10, y -5..5.</summary>
    private static AssemblyPart Part(string module, string area, string texture, float x0, IReadOnlyDictionary<string, KotorTexture?>? textures = null,
        Vector3? ambient = null, params ModuleEntity[] entities)
    {
        var mesh = new SceneMesh("r_" + module, "n", texture, null, TxiInfo.Empty, 0,
            [new(x0, 0, 0), new(x0 + 1, 0, 0), new(x0, 1, 0)], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [new(0, 0), new(1, 0), new(0, 1)], null, [0, 1, 2]);
        var walk = new SceneWalkmesh("r_" + module, [new(x0, -5, 0), new(x0 + 10, -5, 0), new(x0 + 10, 5, 0), new(x0, 5, 0)], [0, 1, 2, 0, 2, 3], [true, true]);
        var info = new ModuleInfo(module, module, area, new ModuleEntry(new Vector3(x0 + 1, 0, 0), Vector2.UnitY));
        var scene = new ModuleScene(info, [mesh], [walk], textures ?? new Dictionary<string, KotorTexture?> { [texture] = Tex })
        {
            AmbientColor = ambient ?? ModuleScene.DefaultAmbient,
        };
        return new AssemblyPart(scene, entities);
    }

    private static ModuleEntity Door(string tag, Vector3 pos, float yaw = 0f, string toModule = "", string to = "") =>
        new(EntityKind.Door, tag, "", pos, yaw, null, LinkedToModule: toModule, LinkedTo: to);

    private static DoorFrame? Frames(AssemblyPart p, ModuleEntity d) => DoorFrames.DefaultFrame(d);

    private static int Count(ImportLog log, string code) => log.Warnings.Count(w => w.Code == code);

    /// <summary>A (reference, identite) : sol 0..10, porte d1 en x=10 vers m_b/d2, porte d3 vers un module non selectionne.
    /// B : sol -5..5 dans son repere, porte d2 en x=-5, waypoint ; translation (bx, 0, 0).</summary>
    private static (AssemblyPart A, AssemblyPart B, DoorLink Link) TwoParts()
    {
        var d1 = Door("d1", new(10, 0, 0), 270f, "m_b", "d2");
        var d3 = Door("d3", new(0, 0, 0), 90f, "m_c", "x");
        var a = Part("m_a", "Zone A", "ta", 0f, null, new Vector3(0.5f, 0.4f, 0.3f), d1, d3);
        var d2 = Door("d2", new(-5, 0, 0), 90f, "m_a", "d1");
        var wp = new ModuleEntity(EntityKind.Waypoint, "wp", "", new(1, 2, 0), 0f, null);
        var trig = new ModuleEntity(EntityKind.Trigger, "tr", "", new(0, 0, 0), 0f, [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)]);
        var b = Part("m_b", "Zone B", "tb", -5f, null, new Vector3(0.9f, 0.9f, 0.9f), d2, wp, trig);
        return (a, b, new DoorLink(0, d1, 1, d2));
    }

    [Fact]
    public void Junction_WithGap_MergesBothScenesAndAddsACorridor()
    {
        var log = new ImportLog();
        var (a, b, link) = TwoParts();
        var tb = new RigidTransform(0f, new Vector3(20, 0, 0)); // d2 en x=15 : ecart de 5 m
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, tb], [link], Frames, log);

        Assert.Equal(3, r.Scene.Meshes.Count);
        Assert.Equal(3, r.Scene.Walkmeshes.Count);
        Assert.Contains(r.Scene.Meshes, m => m.Texture == "ta" && m.Positions[0] == new Vector3(0, 0, 0));
        Assert.Contains(r.Scene.Meshes, m => m.Texture == "tb" && m.Positions[0] == new Vector3(15, 0, 0));
        var corridor = Assert.Single(r.Scene.Meshes, m => m.Room.StartsWith("junction", StringComparison.Ordinal));
        Assert.Equal("ta", corridor.Texture); // texture du premier maillage de la partie source
        Assert.Equal(10f, corridor.Positions.Min(p => p.X), 3);
        Assert.Equal(15f, corridor.Positions.Max(p => p.X), 3);

        Assert.Equal("m_a", r.Scene.Info.Module);
        Assert.Equal("Zone A + Zone B", r.Scene.Info.AreaName);
        Assert.Equal(new Vector3(1, 0, 0), r.Scene.Info.Entry.Position);
        Assert.Equal(new Vector3(0.5f, 0.4f, 0.3f), r.Scene.AmbientColor);
        Assert.NotNull(r.Scene.Parts);
        Assert.Equal(2, r.Scene.Parts!.Count);
        Assert.Equal("m_a", r.Scene.Parts[0].Module);
        Assert.Equal(tb, r.Scene.Parts[1].Transform);
        Assert.Same(b.Scene.Info, r.Scene.Parts[1].Info);

        Assert.Equal(1, Count(log, "ASSEMBLY_JUNCTION"));
        Assert.Contains(log.Warnings, w => w.Code == "ASSEMBLY_JUNCTION" && w.Message.Contains("5.00 m", StringComparison.Ordinal));
        Assert.Empty(r.Overlaps);
        Assert.Equal(0, Count(log, "ASSEMBLY_OVERLAP"));
    }

    [Fact]
    public void Junction_FiltersDoors_SourceKeptUnlocked_DestinationRemoved_OthersUnchanged()
    {
        var (a, b, link) = TwoParts();
        var tb = new RigidTransform(90f, new Vector3(20, 0, 0));
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, tb], [link], Frames, new ImportLog());

        var ea = r.EntitiesByPart[0];
        var d1 = Assert.Single(ea, e => e.Name == "d1");
        Assert.Equal("", d1.LinkedToModule);
        Assert.False(d1.Locked);
        var d3 = Assert.Single(ea, e => e.Name == "d3");
        Assert.Equal("m_c", d3.LinkedToModule);

        var eb = r.EntitiesByPart[1];
        Assert.DoesNotContain(eb, e => e.Name == "d2");
        var wp = Assert.Single(eb, e => e.Name == "wp");
        Assert.Equal(tb.Apply(new Vector3(1, 2, 0)), wp.Position); // repere assemble
        var tr = Assert.Single(eb, e => e.Name == "tr");
        Assert.Equal(90f, tr.Yaw);
        Assert.Equal(tb.Apply(new Vector3(1, 0, 0)), tr.Polygon![1]);
    }

    [Fact]
    public void ApplyPart_FiltersObjectsReadAgainFromTheGit()
    {
        var (a, b, link) = TwoParts();
        var tb = new RigidTransform(0f, new Vector3(20, 0, 0));
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, tb], [link], Frames, new ImportLog());

        // Objets relus du GIT (instances distinctes) : la porte est reconnue par son tag et sa position.
        var placeable = new ModuleEntity(EntityKind.Placeable, "crate", "crate", new(2, 0, 0), 30f, null);
        var objectsB = new[] { Door("d2", new(-5, 0, 0), 90f, "m_a", "d1"), placeable };
        var outB = ModuleAssembler.ApplyPart(objectsB, r.Scene.Parts![1]);
        var only = Assert.Single(outB);
        Assert.Equal(new Vector3(22, 0, 0), only.Position);
        Assert.Equal(30f, only.Yaw);

        var objectsA = new[] { Door("d1", new(10, 0, 0), 270f, "m_b", "d2") with { Locked = true }, Door("d3", new(0, 0, 0), 90f, "m_c", "x") };
        var outA = ModuleAssembler.ApplyPart(objectsA, r.Scene.Parts[0]);
        Assert.Equal("", outA.Single(e => e.Name == "d1").LinkedToModule);
        Assert.False(outA.Single(e => e.Name == "d1").Locked);
        Assert.Equal("m_c", outA.Single(e => e.Name == "d3").LinkedToModule);
    }

    [Fact]
    public void Junction_ZeroGap_NoCorridor()
    {
        var log = new ImportLog();
        var (a, b, link) = TwoParts();
        var tb = new RigidTransform(0f, new Vector3(15, 0, 0)); // d2 en x=10, sur d1
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, tb], [link], Frames, log);
        Assert.Equal(2, r.Scene.Meshes.Count);
        Assert.Equal(2, r.Scene.Walkmeshes.Count);
        Assert.Equal(1, Count(log, "ASSEMBLY_JUNCTION"));
        Assert.Contains(log.Warnings, w => w.Code == "ASSEMBLY_JUNCTION" && w.Message.Contains("0.00 m", StringComparison.Ordinal));
    }

    [Fact]
    public void ReferencePart_GivesInfoAndAmbient()
    {
        var (a, b, link) = TwoParts();
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 1, [new RigidTransform(0f, new Vector3(-15, 0, 0)), RigidTransform.Identity], [link], Frames, new ImportLog());
        Assert.Equal("m_b", r.Scene.Info.Module);
        Assert.Equal(new Vector3(0.9f, 0.9f, 0.9f), r.Scene.AmbientColor);
        Assert.Equal("Zone A + Zone B", r.Scene.Info.AreaName);
    }

    [Fact]
    public void Textures_NullNeverOverwritesAKnownTexture()
    {
        var a = Part("m_a", "A", "ta", 0f, new Dictionary<string, KotorTexture?> { ["ta"] = null, ["s"] = Tex, ["tb2"] = Tex });
        var b = Part("m_b", "B", "tb", 30f, new Dictionary<string, KotorTexture?> { ["tb"] = Tex, ["s"] = null, ["ta"] = Tex });
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, RigidTransform.Identity], [], Frames, new ImportLog());
        Assert.NotNull(r.Scene.Textures["ta"]);
        Assert.NotNull(r.Scene.Textures["s"]);
        Assert.NotNull(r.Scene.Textures["tb"]);
        Assert.Equal(4, r.Scene.Textures.Count);
    }

    [Fact]
    public void CorridorTexture_SkipsMeshesWhoseTextureIsMissing()
    {
        var d1 = Door("d1", new(10, 0, 0), 270f, "m_b", "d2");
        var a = Part("m_a", "A", "missing", 0f, new Dictionary<string, KotorTexture?> { ["missing"] = null, ["wall"] = Tex }, null, d1);
        a = a with
        {
            Scene = a.Scene with
            {
                Meshes = [a.Scene.Meshes[0], a.Scene.Meshes[0] with { Texture = "wall" }],
            },
        };
        var d2 = Door("d2", new(-5, 0, 0), 90f, "m_a", "d1");
        var b = Part("m_b", "B", "tb", -5f, null, null, d2);
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, new RigidTransform(0f, new Vector3(20, 0, 0))],
            [new DoorLink(0, d1, 1, d2)], Frames, new ImportLog());
        Assert.Equal("wall", Assert.Single(r.Scene.Meshes, m => m.Room.StartsWith("junction", StringComparison.Ordinal)).Texture);
    }

    [Fact]
    public void WaypointTarget_RemovesTheNearestDoorOfTheDestination()
    {
        var d1 = Door("d1", new(10, 0, 0), 270f, "m_b", "wp");
        var a = Part("m_a", "A", "ta", 0f, null, null, d1);
        var near = Door("near", new(-5, 0, 0), 90f, "m_a", "d1");
        var far = Door("far", new(4, 0, 0), 90f);
        var wp = new ModuleEntity(EntityKind.Waypoint, "wp", "", new(-4, 0, 0), 0f, null);
        var b = Part("m_b", "B", "tb", -5f, null, null, near, far, wp);
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, new RigidTransform(0f, new Vector3(15, 0, 0))],
            [new DoorLink(0, d1, 1, wp)], Frames, new ImportLog());
        Assert.DoesNotContain(r.EntitiesByPart[1], e => e.Name == "near");
        Assert.Contains(r.EntitiesByPart[1], e => e.Name == "far");
        Assert.Contains(r.EntitiesByPart[1], e => e.Name == "wp");
    }

    [Fact]
    public void OverlappingParts_AreReported()
    {
        var log = new ImportLog();
        var a = Part("m_a", "A", "ta", 0f);
        var b = Part("m_b", "B", "tb", 0f);
        AssemblyResult r = ModuleAssembler.Assemble([a, b], 0, [RigidTransform.Identity, RigidTransform.Identity], [], Frames, log);
        var o = Assert.Single(r.Overlaps);
        Assert.Equal((0, 1), (o.A, o.B));
        Assert.Equal(1, Count(log, "ASSEMBLY_OVERLAP"));
    }
}

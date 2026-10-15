using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class AutoPlacerTests
{
    private static AssemblyPart Part(string module, params ModuleEntity[] entities)
    {
        var info = new ModuleInfo(module, module, module, new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        var scene = new ModuleScene(info, [], [], new Dictionary<string, Kotor.Core.Textures.KotorTexture?>());
        return new AssemblyPart(scene, entities);
    }

    /// <summary>Partie dont le sol marchable est le rectangle [x0, x1] x [y0, y1] a z = 0 (repere du module).</summary>
    private static AssemblyPart FloorPart(string module, (float X0, float Y0, float X1, float Y1) floor, params ModuleEntity[] entities)
    {
        var (x0, y0, x1, y1) = floor;
        var walk = new SceneWalkmesh("room", [new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0)], [0, 1, 2, 0, 2, 3], [true, true]);
        var info = new ModuleInfo(module, module, module, new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        var scene = new ModuleScene(info, [], [walk], new Dictionary<string, Kotor.Core.Textures.KotorTexture?>());
        return new AssemblyPart(scene, entities);
    }

    private static ModuleEntity Door(string tag, Vector3 pos, float yaw = 0f, string toModule = "", string to = "") =>
        new(EntityKind.Door, tag, "", pos, yaw, null, LinkedToModule: toModule, LinkedTo: to);

    private static ModuleEntity Waypoint(string tag, Vector3 pos) => new(EntityKind.Waypoint, tag, "", pos, 0f, null);

    private static int Count(ImportLog log, string code) => log.Warnings.Count(w => w.Code == code);

    [Fact]
    public void TwoParts_BIsAlignedOnADoor()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "d2"));
        var b = Part("m_b", Door("d2", new(0, 5, 0), 0));
        var links = TransitionLinks.Find([a, b], log);
        var p = AutoPlacer.Place([a, b], 0, links, log);
        Assert.Single(links);
        Assert.Equal(RigidTransform.Identity, p.Transforms[0]);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(0, 5, 0), 0), p.Transforms[1]);
        Assert.Empty(p.UnlinkedGroups);
    }

    [Fact]
    public void Triangle_LogsOneExtraEdgeAndNoLoop()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("ab", new(10, 0, 0), 0, "m_b", "ba"), Door("ac", new(0, 10, 0), 90, "m_c", "ca"));
        var b = Part("m_b", Door("ba", new(0, 0, 0), 180, "m_a", "ab"), Door("bc", new(5, 0, 0), 0, "m_c", "cb"));
        var c = Part("m_c", Door("cb", new(0, 0, 0), 0, "m_b", "bc"), Door("ca", new(3, 3, 0), 90, "m_a", "ac"));
        var links = TransitionLinks.Find([a, b, c], log);
        Assert.Equal(3, links.Count);
        var p = AutoPlacer.Place([a, b, c], 0, links, log);
        Assert.Equal(2, p.Used.Count);
        Assert.Equal(1, Count(log, "LINK_EXTRA_EDGE"));
        Assert.Empty(p.UnlinkedGroups);
    }

    [Fact]
    public void NoTransition_PartIsUnlinked()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", Vector3.Zero));
        var b = Part("m_b", Door("d2", Vector3.Zero));
        var p = AutoPlacer.Place([a, b], 0, TransitionLinks.Find([a, b], log), log);
        Assert.Single(p.UnlinkedGroups);
        Assert.Equal([1], p.UnlinkedGroups[0]);
        Assert.Equal(RigidTransform.Identity, p.Transforms[1]);
    }

    [Fact]
    public void WaypointTarget_UsesNearestDoorAsAnchor()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "wp"));
        var b = Part("m_b", Waypoint("wp", new(1, 1, 0)), Door("near", new(0, 5, 0), 0), Door("far", new(50, 50, 0), 0));
        var links = TransitionLinks.Find([a, b], log);
        var p = AutoPlacer.Place([a, b], 0, links, log);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(0, 5, 0), 0), p.Transforms[1]);
        Assert.Equal(0, Count(log, "LINK_NO_TARGET_DOOR"));
    }

    [Fact]
    public void WaypointTarget_WithoutDoor_WarnsAndUsesWaypoint()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "wp"));
        var b = Part("m_b", Waypoint("wp", new(1, 1, 0)));
        var p = AutoPlacer.Place([a, b], 0, TransitionLinks.Find([a, b], log), log);
        Assert.Equal(1, Count(log, "LINK_NO_TARGET_DOOR"));
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(1, 1, 0), 0), p.Transforms[1]);
    }

    [Fact]
    public void UnknownTag_IsLoggedAndGroupUnlinked()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", Vector3.Zero, 0, "m_b", "nope"));
        var b = Part("m_b", Door("d2", Vector3.Zero));
        var links = TransitionLinks.Find([a, b], log);
        Assert.Empty(links);
        Assert.Equal(1, Count(log, "LINK_TARGET_NOT_FOUND"));
        var p = AutoPlacer.Place([a, b], 0, links, log);
        Assert.Equal([1], p.UnlinkedGroups[0]);
    }

    [Fact]
    public void TransitionToUnselectedModule_IsSilentlyIgnored()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", Vector3.Zero, 0, "m_zzz", "x"));
        Assert.Empty(TransitionLinks.Find([a], log));
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void LinkTraversedBackwards_PlacesTheSourcePart()
    {
        // La reference est B ; la porte source est dans A : A doit etre placee par rapport a B.
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "d2"));
        var b = Part("m_b", Door("d2", new(0, 5, 0), 0));
        var p = AutoPlacer.Place([a, b], 1, TransitionLinks.Find([a, b], log), log);
        Assert.Equal(RigidTransform.Identity, p.Transforms[1]);
        Assert.Equal(RigidTransform.AligningDoors(new(0, 5, 0), 0, new(10, 0, 0), 0), p.Transforms[0]);
    }

    [Fact]
    public void SameDoorPoseAndYaw_BackToBackWouldOverlap_FlipsAndLogs()
    {
        // Portes confondues, meme yaw : theta = 180 rabat le sol de B (cote +Y de sa porte) sur celui de A (cote -Y).
        var log = new ImportLog();
        var a = FloorPart("m_a", (-5, -10, 5, 0), Door("d1", Vector3.Zero, 0, "m_b", "d2"));
        var b = FloorPart("m_b", (-5, 0, 5, 10), Door("d2", Vector3.Zero, 0));
        var p = AutoPlacer.Place([a, b], 0, TransitionLinks.Find([a, b], log), log);
        Assert.Equal(RigidTransform.AligningDoors(Vector3.Zero, 180, Vector3.Zero, 0), p.Transforms[1]);
        Assert.Equal(0f, p.Transforms[1].YawDegrees);
        Assert.Equal(1, Count(log, "ASSEMBLY_FLIPPED"));
        Assert.Contains(log.Warnings, w => w.Code == "ASSEMBLY_FLIPPED" && w.Message.Contains("m_a") && w.Message.Contains("m_b"));
        Assert.Empty(OverlapChecker.Check([a.Scene, b.Scene.Transformed(p.Transforms[1])]));
    }

    [Fact]
    public void BackToBackIsRight_NoFlipAndNoLog()
    {
        var log = new ImportLog();
        var a = FloorPart("m_a", (-5, -10, 5, 0), Door("d1", new(3, 2, 0), 0, "m_b", "d2"));
        var b = FloorPart("m_b", (-5, -10, 5, 0), Door("d2", new(3, 2, 0), 0));
        var p = AutoPlacer.Place([a, b], 0, TransitionLinks.Find([a, b], log), log);
        Assert.Equal(RigidTransform.AligningDoors(new(3, 2, 0), 0, new(3, 2, 0), 0), p.Transforms[1]);
        Assert.Equal(0, Count(log, "ASSEMBLY_FLIPPED"));
    }

    [Fact]
    public void EqualOverlap_KeepsBackToBack()
    {
        // Sol de B centre sur sa porte : les deux poses recouvrent A de la meme surface.
        var log = new ImportLog();
        var a = FloorPart("m_a", (-5, -10, 5, 0), Door("d1", Vector3.Zero, 0, "m_b", "d2"));
        var b = FloorPart("m_b", (-5, -5, 5, 5), Door("d2", Vector3.Zero, 0));
        var p = AutoPlacer.Place([a, b], 0, TransitionLinks.Find([a, b], log), log);
        Assert.Equal(RigidTransform.AligningDoors(Vector3.Zero, 0, Vector3.Zero, 0), p.Transforms[1]);
        Assert.Equal(0, Count(log, "ASSEMBLY_FLIPPED"));
    }

    [Fact]
    public void Chain_ThirdPartIsCheckedAgainstAllPlacedParts()
    {
        // A-B-C : la pose dos a dos de C ne touche pas B mais recouvre A ; C doit etre retournee.
        var log = new ImportLog();
        var a = FloorPart("m_a", (0, -10, 10, 0), Door("ab", new(20, 0, 0), 0, "m_b", "ba"));
        var b = FloorPart("m_b", (10, 20, 14, 24), Door("ba", Vector3.Zero, 180), Door("bc", new(-10, 0, 0), 0, "m_c", "cb"));
        var c = FloorPart("m_c", (-3, 1, 3, 9), Door("cb", Vector3.Zero, 0));
        var p = AutoPlacer.Place([a, b, c], 0, TransitionLinks.Find([a, b, c], log), log);
        Assert.Equal(RigidTransform.AligningDoors(new(20, 0, 0), 0, Vector3.Zero, 180), p.Transforms[1]);
        var doorBc = p.Transforms[1].Apply(new(-10, 0, 0));
        Assert.Equal(RigidTransform.AligningDoors(doorBc, p.Transforms[1].ApplyYaw(0) + 180, Vector3.Zero, 0), p.Transforms[2]);
        Assert.Equal(1, Count(log, "ASSEMBLY_FLIPPED"));
        Assert.Contains(log.Warnings, w => w.Code == "ASSEMBLY_FLIPPED" && w.Message.Contains("m_b") && w.Message.Contains("m_c"));
        Assert.Empty(OverlapChecker.Check([a.Scene, b.Scene.Transformed(p.Transforms[1]), c.Scene.Transformed(p.Transforms[2])]));
    }

    [Fact]
    public void ReturnDoorTargetingAWaypoint_IsNotAnExtraEdge()
    {
        // Chaque porte vise un waypoint de l'autre module, pres de la porte jumelle (cas de tar_m02aa / tar_m02af).
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "wp_b"), Waypoint("wp_a", new(11, 1, 0)), Door("other", new(40, 0, 0), 0));
        var b = Part("m_b", Door("d2", new(0, 5, 0), 0, "m_a", "wp_a"), Waypoint("wp_b", new(1, 6, 0)));
        var links = TransitionLinks.Find([a, b], log);
        Assert.Single(links);
        Assert.Equal(0, Count(log, "LINK_EXTRA_EDGE"));
    }

    [Fact]
    public void SecondDoorTargetingAWaypointNearAnotherDoor_IsStillAnExtraEdge()
    {
        var log = new ImportLog();
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "wp_b"), Waypoint("wp_far", new(41, 1, 0)), Door("other", new(40, 0, 0), 0));
        var b = Part("m_b", Door("d2", new(0, 5, 0), 0), Door("d3", new(30, 5, 0), 0, "m_a", "wp_far"), Waypoint("wp_b", new(1, 6, 0)));
        var links = TransitionLinks.Find([a, b], log);
        Assert.Single(links);
        Assert.Equal(1, Count(log, "LINK_EXTRA_EDGE"));
    }

    [SkippableFact]
    public void Taris_upper_city_and_apartment_keep_their_shared_frame()
    {
        // Portes tar02_aprtdoor / tar02_aptdoor : meme position, meme yaw (45). Dos a dos, l'appartement tombe dans l'anneau (theta = 180) ;
        // retourne, il garde le repere commun des deux modules (identite a quelques mm pres). Le sas, present dans les deux
        // modules, se recouvre encore : on verifie seulement que le recouvrement baisse nettement.
        Skip.If(TestEnv.KotorDir is null, "KOTOR not installed");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.GetTempPath(), Path.GetTempPath());
        var log = new ImportLog();
        var (parts, links) = AssemblyLoader.LoadParts(["tar_m02aa", "tar_m02af"], opt, log);
        Assert.Equal(0, Count(log, "LINK_EXTRA_EDGE"));
        var plog = new ImportLog();
        var p = AutoPlacer.Place(parts, 0, links, plog);
        Assert.Equal(1, Count(plog, "ASSEMBLY_FLIPPED"));
        Assert.Equal(0f, p.Transforms[1].YawDegrees, 0.01f);
        Assert.True(p.Transforms[1].Translation.Length() < 0.05f, $"translation {p.Transforms[1].Translation}");
        var link = Assert.Single(links);
        var anchorB = parts[1].Entities.Where(e => e.Kind == EntityKind.Door)
            .MinBy(e => Vector3.DistanceSquared(e.Position, link.ToTarget.Position))!;
        var backToBack = RigidTransform.AligningDoors(link.FromDoor.Position, link.FromDoor.Yaw, anchorB.Position, anchorB.Yaw);
        float chosen = OverlapChecker.OverlapArea(parts[0].Scene, parts[1].Scene.Transformed(p.Transforms[1]));
        float rejected = OverlapChecker.OverlapArea(parts[0].Scene, parts[1].Scene.Transformed(backToBack));
        Assert.True(chosen < rejected / 2, $"overlap {chosen} m2 (chosen) vs {rejected} m2 (back to back)");
    }

    [SkippableFact]
    public void Endar_spire_placement_is_unchanged()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR not installed");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.GetTempPath(), Path.GetTempPath());
        var log = new ImportLog();
        var (parts, links) = AssemblyLoader.LoadParts(["end_m01aa", "end_m01ab"], opt, log);
        var plog = new ImportLog();
        var p = AutoPlacer.Place(parts, 0, links, plog);
        Assert.Equal(0, Count(plog, "ASSEMBLY_FLIPPED"));
        // Porte end_door10 de m01aa contre la porte de m01ab la plus proche du waypoint from_01a (end_door07), dos a dos.
        var link = Assert.Single(links);
        var door10 = link.FromDoor;
        var door07 = parts[1].Entities.Where(e => e.Kind == EntityKind.Door)
            .MinBy(e => Vector3.DistanceSquared(e.Position, link.ToTarget.Position))!;
        Assert.Equal("end_door07", door07.Name);
        Assert.Equal(RigidTransform.AligningDoors(door10.Position, door10.Yaw, door07.Position, door07.Yaw), p.Transforms[1]);
        Assert.InRange(p.Transforms[1].YawDegrees, -0.01f, 0.01f);
    }
}

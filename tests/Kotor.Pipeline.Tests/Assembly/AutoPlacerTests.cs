using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
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
}

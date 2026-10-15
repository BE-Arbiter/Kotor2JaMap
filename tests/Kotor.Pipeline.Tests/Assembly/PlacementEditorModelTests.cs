using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class PlacementEditorModelTests
{
    /// <summary>Partie avec un sol carre marchable x0..x0+size, y0..y0+size (repere du module) et des portes.</summary>
    private static AssemblyPart Part(string module, float x0, float y0, float size, params ModuleEntity[] doors)
    {
        var walk = new SceneWalkmesh("room", [new(x0, y0, 0), new(x0 + size, y0, 0), new(x0 + size, y0 + size, 0), new(x0, y0 + size, 0)],
            [0, 1, 2, 0, 2, 3], [true, true]);
        var info = new ModuleInfo(module, module, module, new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        var scene = new ModuleScene(info, [], [walk], new Dictionary<string, KotorTexture?>());
        return new AssemblyPart(scene, doors);
    }

    private static ModuleEntity Door(string tag, Vector3 pos, float yaw = 0f, string toModule = "", string to = "") =>
        new(EntityKind.Door, tag, "", pos, yaw, null, LinkedToModule: toModule, LinkedTo: to);

    private static AssemblySession Session(params AssemblyPart[] parts)
    {
        var links = TransitionLinks.Find(parts, new ImportLog());
        return new AssemblySession(parts.Select(p => p.Scene.Info.Module).ToList(), parts, links, (_, d) => DoorFrames.DefaultFrame(d));
    }

    [Fact]
    public void Nudge_ChangesTheTranslationOfThePart()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        RigidTransform before = s.Transforms[1];
        m.Nudge(1, new Vector3(3, -4, 0.5f));
        Assert.Equal(before.Translation + new Vector3(3, -4, 0.5f), s.Transforms[1].Translation);
        Assert.Equal(before.YawDegrees, s.Transforms[1].YawDegrees);
        Assert.Equal(RigidTransform.Identity, s.Transforms[0]);
    }

    [Fact]
    public void Rotate90_TurnsAroundTheCentreOfThePart()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 20, 4, 10));
        var m = new PlacementEditorModel(s);
        m.SetNumeric(1, new Vector3(7, -3, 2), 30f);
        Vector3 centre = m.Centre(1);
        Assert.Equal(new RigidTransform(30f, new Vector3(7, -3, 2)).Apply(new Vector3(25, 9, 0)), centre);

        m.Rotate(1, 90f);
        Assert.Equal(120f, s.Transforms[1].YawDegrees, 3);
        Assert.Equal(0f, Vector3.Distance(centre, m.Centre(1)), 3);
        Assert.Equal(0f, Vector3.Distance(centre, s.Transforms[1].Apply(new Vector3(25, 9, 0))), 3);
    }

    [Fact]
    public void SetNumeric_SetsTheTransform()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        m.SetNumeric(1, new Vector3(1, 2, 3), -45f);
        Assert.Equal(new RigidTransform(-45f, new Vector3(1, 2, 3)), s.Transforms[1]);
    }

    [Fact]
    public void SnapDoors_GivesTheSameResultAsAligningDoors()
    {
        var d1 = Door("d1", new(10, 0, 0), 0f);
        var d2 = Door("d2", new(0, 5, 0), 30f);
        var s = Session(Part("m_a", 0, 0, 10, d1), Part("m_b", 0, 0, 10, d2));
        Assert.False(s.IsComplete);
        var m = new PlacementEditorModel(s);
        m.SnapDoors(0, d1, 1, d2);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0f, new(0, 5, 0), 30f), s.Transforms[1]);
        Assert.True(s.IsComplete);
        Assert.Single(s.ManualLinks);
    }

    [Fact]
    public void SnapDoors_AgainAfterMovingTheReference_RealignsAndKeepsOneJunction()
    {
        var d1 = Door("d1", new(10, 0, 0), 0f);
        var d2 = Door("d2", new(0, 5, 0), 30f);
        var s = Session(Part("m_a", 0, 0, 10, d1), Part("m_b", 0, 0, 10, d2));
        var m = new PlacementEditorModel(s);
        m.SnapDoors(0, d1, 1, d2);
        m.Nudge(1, new Vector3(5, 5, 0));
        // Les deux parties sont deja reliees : la partie non reference (B) est realignee sur A.
        m.SnapDoors(0, d1, 1, d2);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0f, new(0, 5, 0), 30f), s.Transforms[1]);
        Assert.Single(s.ManualLinks);
    }

    [Fact]
    public void Overlaps_AppearWhenPartsAreMovedOntoEachOther_AndDisappearWhenMovedApart()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        m.SetNumeric(1, new Vector3(50, 0, 0), 0f);
        Assert.Empty(m.Overlaps);

        m.SetNumeric(1, new Vector3(2, 2, 0), 0f);
        var o = Assert.Single(m.Overlaps);
        Assert.Equal((0, 1), (o.A, o.B));
        Assert.Contains("m_a", o.Detail);

        m.Nudge(1, new Vector3(48, 0, 0));
        Assert.Empty(m.Overlaps);
    }

    [Fact]
    public void Overlaps_FollowChangesMadeDirectlyOnTheSession()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        s.SetTransform(1, new RigidTransform(0f, new Vector3(100, 0, 0)));
        Assert.Empty(m.Overlaps);
        s.SetTransform(1, RigidTransform.Identity);
        Assert.Single(m.Overlaps);
    }

    [Fact]
    public void Cancel_RestoresTheSessionAsItWasWhenTheEditorOpened()
    {
        var d1 = Door("d1", new(10, 0, 0), 0f);
        var d2 = Door("d2", new(0, 5, 0), 30f);
        var s = Session(Part("m_a", 0, 0, 10, d1), Part("m_b", 0, 0, 10, d2));
        RigidTransform[] before = [.. s.Transforms];
        int revision = s.Revision;
        var m = new PlacementEditorModel(s);
        m.SnapDoors(0, d1, 1, d2);
        m.Nudge(0, new Vector3(1, 1, 1));
        Assert.True(m.HasChanges);

        m.Cancel();
        Assert.Equal(before, s.Transforms);
        Assert.False(s.IsComplete);
        Assert.Empty(s.ManualLinks);
        Assert.Equal(revision, s.Revision);
        Assert.False(m.HasChanges);
    }

    [Fact]
    public void PartAt_FindsThePartUnderAPointOfTheTopView()
    {
        var s = Session(Part("m_a", 0, 0, 10), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        m.SetNumeric(1, new Vector3(50, 0, 0), 0f);
        Assert.Equal(0, m.PartAt(new Vector2(5, 5)));
        Assert.Equal(1, m.PartAt(new Vector2(55, 5)));
        Assert.Null(m.PartAt(new Vector2(30, 5)));
    }

    [Fact]
    public void Doors_ListsTheDoorsOfAPart()
    {
        var d1 = Door("d1", new(10, 0, 0));
        var wp = new ModuleEntity(EntityKind.Waypoint, "wp", "", Vector3.Zero, 0f, null);
        var s = Session(Part("m_a", 0, 0, 10, d1, wp), Part("m_b", 0, 0, 10));
        var m = new PlacementEditorModel(s);
        Assert.Equal([d1], m.Doors(0));
        Assert.Empty(m.Doors(1));
    }
}

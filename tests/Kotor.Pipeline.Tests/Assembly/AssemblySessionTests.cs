using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class AssemblySessionTests
{
    private static AssemblyPart Part(string module, params ModuleEntity[] entities)
    {
        var info = new ModuleInfo(module, module, module, new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        var scene = new ModuleScene(info, [], [], new Dictionary<string, Kotor.Core.Textures.KotorTexture?>());
        return new AssemblyPart(scene, entities);
    }

    private static ModuleEntity Door(string tag, Vector3 pos, float yaw = 0f, string toModule = "", string to = "") =>
        new(EntityKind.Door, tag, "", pos, yaw, null, LinkedToModule: toModule, LinkedTo: to);

    private static DoorFrame? Frames(AssemblyPart p, ModuleEntity d) => DoorFrames.DefaultFrame(d);

    private static AssemblySession Session(IReadOnlyList<AssemblyPart> parts)
    {
        var links = TransitionLinks.Find(parts, new ImportLog());
        return new AssemblySession(parts.Select(p => p.Scene.Info.Module).ToList(), parts, links, Frames);
    }

    [Fact]
    public void AutoLink_MakesTheSessionComplete()
    {
        var a = Part("m_a", Door("d1", new(10, 0, 0), 0, "m_b", "d2"));
        var b = Part("m_b", Door("d2", new(0, 5, 0), 0));
        var s = Session([a, b]);
        Assert.True(s.IsComplete);
        Assert.Empty(s.UnlinkedGroups);
        Assert.Single(s.AutoLinks);
        Assert.Equal(["m_a", "m_b"], s.Modules);
        Assert.Equal(RigidTransform.Identity, s.Transforms[0]);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(0, 5, 0), 0), s.Transforms[1]);
    }

    [Fact]
    public void TwoGroups_ManualJunctionCompletesTheSession()
    {
        var d1 = Door("d1", new(10, 0, 0), 0);
        var d2 = Door("d2", new(0, 5, 0), 0, "m_c", "d3");
        var d3 = Door("d3", new(4, 0, 0), 90f, "m_b", "d2");
        var a = Part("m_a", d1);
        var b = Part("m_b", d2);
        var c = Part("m_c", d3);
        var s = Session([a, b, c]);
        Assert.False(s.IsComplete);
        var group = Assert.Single(s.UnlinkedGroups);
        Assert.Equal([1, 2], group);

        s.SetManualJunction(0, d1, 1, d2);
        Assert.True(s.IsComplete);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(0, 5, 0), 0), s.Transforms[1]);
        // C suit B par sa liaison interne : sa porte d3 coincide avec d2 dos a dos.
        Vector3 d3World = s.Transforms[2].Apply(d3.Position);
        Vector3 d2World = s.Transforms[1].Apply(d2.Position);
        Assert.Equal(0f, Vector3.Distance(d3World, d2World), 3);
    }

    [Fact]
    public void ManualJunction_GivenInReverseOrder_AlignsTheUnlinkedSide()
    {
        var d1 = Door("d1", new(10, 0, 0), 0);
        var d2 = Door("d2", new(0, 5, 0), 0);
        var s = Session([Part("m_a", d1), Part("m_b", d2)]);
        s.SetManualJunction(1, d2, 0, d1);
        Assert.True(s.IsComplete);
        Assert.Equal(RigidTransform.Identity, s.Transforms[0]);
        Assert.Equal(RigidTransform.AligningDoors(new(10, 0, 0), 0, new(0, 5, 0), 0), s.Transforms[1]);
    }

    [Fact]
    public void ReferenceIndex_ReplacesAndForgetsManualWork()
    {
        var d1 = Door("d1", new(10, 0, 0), 0, "m_b", "d2");
        var d2 = Door("d2", new(0, 5, 0), 0);
        var s = Session([Part("m_a", d1), Part("m_b", d2)]);
        s.ReferenceIndex = 1;
        Assert.Equal(RigidTransform.Identity, s.Transforms[1]);
        Assert.NotEqual(RigidTransform.Identity, s.Transforms[0]);
        Assert.True(s.IsComplete);
    }

    [Fact]
    public void Revision_changes_on_SetTransform_SetManualJunction_and_ReferenceIndex()
    {
        var d1 = Door("d1", new(10, 0, 0));
        var d2 = Door("d2", new(0, 5, 0));
        var s = Session([Part("m_a", d1), Part("m_b", d2)]);
        int r = s.Revision;
        s.SetManualJunction(0, d1, 1, d2);
        Assert.NotEqual(r, s.Revision);
        r = s.Revision;
        s.SetTransform(1, RigidTransform.Identity);
        Assert.NotEqual(r, s.Revision);
        r = s.Revision;
        s.ReferenceIndex = 1;
        Assert.NotEqual(r, s.Revision);
    }

    [Fact]
    public void SetTransform_ChangesTheTransform()
    {
        var s = Session([Part("m_a"), Part("m_b")]);
        var t = new RigidTransform(90f, new Vector3(1, 2, 3));
        s.SetTransform(1, t);
        Assert.Equal(t, s.Transforms[1]);
    }

    [Fact]
    public void Build_CallsTheAssembler()
    {
        var d1 = Door("d1", new(10, 0, 0), 0, "m_b", "d2");
        var d2 = Door("d2", new(0, 5, 0), 0);
        var s = Session([Part("m_a", d1), Part("m_b", d2)]);
        var log = new ImportLog();
        AssemblyResult r = s.Build(log);
        Assert.Equal("m_a + m_b", r.Scene.Info.AreaName);
        Assert.Equal(2, r.Scene.Parts!.Count);
        Assert.Contains(log.Warnings, w => w.Code == "ASSEMBLY_JUNCTION");
    }
}

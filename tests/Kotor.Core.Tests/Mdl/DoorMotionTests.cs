using System.Numerics;
using Kotor.Core.Mdl;
using Xunit;

namespace Kotor.Core.Tests.Mdl;

public sealed class DoorMotionTests
{
    private static KotorMesh Quad() =>
        new("tex", null, true, false, 0, [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], null, [new(0, 0), new(1, 0), new(1, 1)], null, [0, 1, 2]);

    /// <summary>Racine (0) + deux panneaux (1, 2) et un cadre (3), comme dor_lts02.</summary>
    private static KotorModel Model(params KotorAnimation[] animations)
    {
        var root = new KotorNode("door", NodeKind.Header, Vector3.Zero, Quaternion.Identity, null) { Number = 0 };
        var left = new KotorNode("l", NodeKind.Header | NodeKind.Mesh, new Vector3(-3, 0, 0), Quaternion.Identity, Quad()) { Number = 1 };
        var right = new KotorNode("r", NodeKind.Header | NodeKind.Mesh, new Vector3(3, 0, 0), Quaternion.Identity, Quad()) { Number = 2 };
        var frame = new KotorNode("f", NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, Quad()) { Number = 3 };
        root.AddChildForTest(left); root.AddChildForTest(right); root.AddChildForTest(frame);
        return new KotorModel("door", null, root, Vector3.Zero, Vector3.One) { Animations = animations };
    }

    private static AnimationTrack Slide(int node, Vector3 to) =>
        new("n", node, [new PositionKey(0, Vector3.Zero), new PositionKey(0.5f, to)], []);

    [Fact]
    public void Sliding_panels_become_pieces_and_the_frame_stays_fixed()
    {
        var model = Model(new KotorAnimation("opening1", 0.5f, [Slide(1, new(-3, 0, 0)), Slide(2, new(3, 0.2f, 0))]));
        DoorMotion m = DoorMotion.Analyze(model)!;
        Assert.Equal(0.5f, m.Duration);
        Assert.Equal(2, m.Moving.Count);
        Assert.Equal(new Vector3(-3, 0, 0), m.Moving[0].Offset);
        Assert.True(Vector3.Distance(new Vector3(3, 0.2f, 0), m.Moving[1].Offset) < 1e-5f);
        Assert.Single(m.Fixed);
        Assert.Equal("f", m.Fixed[0].Name);
    }

    [Fact]
    public void Panels_with_the_same_offset_are_grouped()
    {
        var model = Model(new KotorAnimation("opening1", 1f, [Slide(1, new(0, 0, 2)), Slide(2, new(0, 0, 2))]));
        DoorMotion m = DoorMotion.Analyze(model)!;
        Assert.Single(m.Moving);
        Assert.Equal(2, m.Moving[0].Nodes.Count);
    }

    [Fact]
    public void Opening2_is_used_when_opening1_is_missing()
    {
        Assert.NotNull(DoorMotion.Analyze(Model(new KotorAnimation("opening2", 1f, [Slide(1, new(0, 0, 2))]))));
    }

    [Fact]
    public void No_opening_animation_or_no_movement_gives_null()
    {
        Assert.Null(DoorMotion.Analyze(Model()));
        Assert.Null(DoorMotion.Analyze(Model(new KotorAnimation("closing1", 1f, [Slide(1, new(0, 0, 2))]))));
        Assert.Null(DoorMotion.Analyze(Model(new KotorAnimation("opening1", 1f, [Slide(1, new(0.001f, 0, 0))]))));
    }

    [Fact]
    public void A_swinging_panel_is_reduced_to_the_translation_of_its_center()
    {
        // Panneau en (-3, 0, 0) dont le centre local est (0.5, 0.5, 0) ; tourne de 90 degres autour de Z : le centre passe de (-2.5, 0.5) a (-3.5, 0.5).
        var turn = new AnimationTrack("n", 1, [], [new OrientationKey(0, Quaternion.Identity), new OrientationKey(1, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2))]);
        DoorMotion m = DoorMotion.Analyze(Model(new KotorAnimation("opening1", 1f, [turn])))!;
        Assert.Single(m.Moving);
        Assert.True(Vector3.Distance(m.Moving[0].Offset, new Vector3(-1f, 0f, 0f)) < 1e-4f);
        Assert.Equal(2, m.Fixed.Count);
    }

    [Fact]
    public void Too_many_groups_give_null()
    {
        var root = new KotorNode("door", NodeKind.Header, Vector3.Zero, Quaternion.Identity, null) { Number = 0 };
        var tracks = new List<AnimationTrack>();
        for (int i = 1; i <= DoorMotion.MaxPieces + 1; i++)
        {
            root.AddChildForTest(new KotorNode("p", NodeKind.Header | NodeKind.Mesh, Vector3.Zero, Quaternion.Identity, Quad()) { Number = i });
            tracks.Add(Slide(i, new Vector3(i, 0, 0)));
        }
        var model = new KotorModel("door", null, root, Vector3.Zero, Vector3.One) { Animations = [new KotorAnimation("opening1", 1f, tracks)] };
        Assert.Null(DoorMotion.Analyze(model));
    }
}

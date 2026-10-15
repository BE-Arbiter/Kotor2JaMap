using System.Numerics;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Scene;

public class RigidTransformTests
{
    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 1e-5f, $"attendu {expected}, obtenu {actual}");
    }

    [Fact]
    public void Apply_rotates_around_Z_then_translates()
    {
        Near(new Vector3(0, 1, 0), new RigidTransform(90, Vector3.Zero).Apply(new Vector3(1, 0, 0)));
        Near(new Vector3(5, 1, 3), new RigidTransform(90, new Vector3(5, 0, 3)).Apply(new Vector3(1, 0, 0)));
    }

    [Fact]
    public void ApplyDirection_ignores_translation()
    {
        Near(new Vector3(0, 1, 0), new RigidTransform(90, new Vector3(9, 9, 9)).ApplyDirection(new Vector3(1, 0, 0)));
    }

    [Fact]
    public void ApplyYaw_is_normalized()
    {
        Assert.Equal(-170f, new RigidTransform(20, Vector3.Zero).ApplyYaw(170), 3);
        Assert.Equal(180f, new RigidTransform(180, Vector3.Zero).ApplyYaw(0), 3);
    }

    [Fact]
    public void AligningDoors_puts_B_door_on_A_door_back_to_back()
    {
        var t = RigidTransform.AligningDoors(new Vector3(10, 0, 0), 0, new Vector3(0, 5, 0), 0);
        Near(new Vector3(10, 0, 0), t.Apply(new Vector3(0, 5, 0)));
        Assert.Equal(180f, t.ApplyYaw(0), 3);
    }

    [Fact]
    public void Then_composes_in_order()
    {
        var a = new RigidTransform(90, new Vector3(1, 0, 0));
        var b = new RigidTransform(90, new Vector3(0, 2, 0));
        var p = new Vector3(1, 1, 0);
        Near(b.Apply(a.Apply(p)), a.Then(b).Apply(p));
        Assert.Equal(180f, a.Then(b).YawDegrees, 3);
    }

    [Fact]
    public void Transformed_moves_vertices_rotates_normals_and_keeps_properties()
    {
        var mesh = new SceneMesh("r", "n", "tex", null, TxiInfo.Empty, 0,
            new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) },
            new[] { new Vector3(1, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 0) },
            new Vector2[3], null, new[] { 0, 1, 2 });
        var wm = new SceneWalkmesh("r", new[] { new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(0, 0, 1) }, new[] { 0, 1, 2 }, new[] { true });
        var tex = new Dictionary<string, KotorTexture?> { ["tex"] = null };
        var scene = new ModuleScene(new ModuleInfo("m", "a", "A", new ModuleEntry(new Vector3(1, 0, 0), new Vector2(1, 0))),
            new[] { mesh }, new[] { wm }, tex)
        {
            Lights = new[] { new SceneLight("l", new Vector3(1, 0, 0), Vector3.One, 5, 1) },
            AmbientColor = new Vector3(0.3f, 0.4f, 0.5f),
            SunDiffuseColor = new Vector3(0.1f, 0.2f, 0.3f),
            AreaLightingKnown = true,
        };
        var t = new RigidTransform(90, new Vector3(10, 0, 0));
        var r = scene.Transformed(t);

        Near(new Vector3(10, 1, 0), r.Meshes[0].Positions[0]);
        Near(new Vector3(0, 1, 0), r.Meshes[0].Normals[0]);
        Near(new Vector3(10, 1, 0), r.Walkmeshes[0].Positions[0]);
        Near(new Vector3(10, 1, 0), r.Lights[0].Position);
        Near(new Vector3(10, 1, 0), r.Info.Entry.Position);
        Assert.True(Vector2.Distance(new Vector2(0, 1), r.Info.Entry.Direction) < 1e-5f);
        Assert.Same(scene.Textures, r.Textures);
        Assert.Equal(new Vector3(0.3f, 0.4f, 0.5f), r.AmbientColor);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), r.SunDiffuseColor);
        Assert.True(r.AreaLightingKnown);
        Assert.Null(r.Parts);
        // l'original n'est pas modifie
        Near(new Vector3(1, 0, 0), mesh.Positions[0]);
    }
}

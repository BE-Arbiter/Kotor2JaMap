using System.Numerics;
using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Tests;

public sealed class PreviewPickerTests
{
    // Carre de cote 100 centre sur l'origine, a la hauteur z.
    private static PreviewBatch Quad(float z, PreviewBlend blend = PreviewBlend.Opaque, bool sky = false)
    {
        PreviewVertex V(float x, float y) => new(new Vector3(x, y, z), Vector3.UnitZ, Vector2.Zero, Vector2.Zero);
        return new PreviewBatch("q", "t", null, blend, false, [V(-50, -50), V(50, -50), V(50, 50), V(-50, 50)], [0, 1, 2, 0, 2, 3], sky);
    }

    private static PreviewModel Model(params PreviewBatch[] batches) =>
        new(batches, new Dictionary<string, PreviewTexture>(), [], [], null, 0f, Vector3.Zero, Vector3.Zero);

    [Fact]
    public void Hits_the_floor_below_the_camera()
    {
        Vector3? hit = PreviewPicker.Raycast(Model(Quad(0f)), new Vector3(10, 20, 200), new Vector3(0, 0, -1));
        Assert.Equal(new Vector3(10, 20, 0), hit);
    }

    [Fact]
    public void Takes_the_nearest_surface()
    {
        Vector3? hit = PreviewPicker.Raycast(Model(Quad(0f), Quad(80f)), new Vector3(0, 0, 200), new Vector3(0, 0, -1));
        Assert.Equal(80f, hit!.Value.Z, 3);
    }

    [Fact]
    public void Misses_outside_the_surface_and_ignores_sky_and_transparent_batches()
    {
        Assert.Null(PreviewPicker.Raycast(Model(Quad(0f)), new Vector3(500, 0, 200), new Vector3(0, 0, -1)));
        Assert.Null(PreviewPicker.Raycast(Model(Quad(0f, sky: true), Quad(50f, PreviewBlend.Alpha)), new Vector3(0, 0, 200), new Vector3(0, 0, -1)));
    }

    [Fact]
    public void Hits_the_back_face_too()
    {
        Vector3? hit = PreviewPicker.Raycast(Model(Quad(0f)), new Vector3(0, 0, -200), new Vector3(0, 0, 1));
        Assert.Equal(0f, hit!.Value.Z, 3);
    }
}

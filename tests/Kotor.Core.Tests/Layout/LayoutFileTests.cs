using System.Numerics;
using Kotor.Core.Archives;
using Kotor.Core.Layout;

namespace Kotor.Core.Tests.Layout;

public class LayoutFileTests
{
    private const string Sample = """
        beginlayout
          roomcount 2
            M02AA_01A 0.0 0.0 0.0
            m02aa_02b 10.5 -20.25 3.0
          trackcount 1
            track_a 1 2 3
          obstaclecount 0
          doorhookcount 1
            M02aa_01a Door_01 0 5.0 6.0 7.0 0.1 0.2 0.3 0.9
        donelayout
        """;

    [Fact]
    public void Lyt_parses_rooms_and_doorhooks()
    {
        var lyt = LayoutFile.Parse(Sample);
        Assert.Equal(2, lyt.Rooms.Count);
        Assert.Equal("m02aa_01a", lyt.Rooms[0].Model);
        Assert.Equal(new Vector3(10.5f, -20.25f, 3f), lyt.Rooms[1].Position);
        var d = Assert.Single(lyt.DoorHooks);
        Assert.Equal("door_01", d.Name);
        Assert.Equal("m02aa_01a", d.Room);
        Assert.Equal(new Vector3(5, 6, 7), d.Position);
        Assert.Equal(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f), d.Orientation);
    }

    [SkippableFact]
    public void Real_lyt_tar_m02aa()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var bytes = r.Find("m02aa", ResourceType.Lyt);
        Assert.NotNull(bytes);
        var lyt = LayoutFile.Parse(System.Text.Encoding.ASCII.GetString(bytes!));
        Assert.True(lyt.Rooms.Count >= 10);
        Assert.Contains(lyt.Rooms, x => x.Model == "m02aa_01a");
    }
}

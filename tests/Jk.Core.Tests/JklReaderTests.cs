using System.Numerics;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;

namespace Jk.Core.Tests;

public class JklReaderTests
{
    private const string RealDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    private static string Minimal() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "minimal.jkl"));

    [Fact]
    public void Read_MinimalLevel()
    {
        JklLevel l = JklReader.Read(Minimal());

        Assert.Equal(1, l.Header.Version);
        Assert.Equal(20f, l.Header.CeilingSkyZ);
        Assert.Equal(200f, l.Header.HorizonDistance);
        Assert.Equal(new Vector2(0.1f, 0.2f), l.Header.HorizonSkyOffset);
        Assert.Equal(new[] { 1f, 2f, 3f, 4f }, l.Header.MipMapDistances);

        Assert.Equal(new[] { "floor.mat" }, l.Materials);
        Assert.Equal(new[] { "dflt.cmp" }, l.Colormaps);
        Assert.Equal(new[] { "walkmetaldry01.wav" }, l.Sounds);
        Assert.Equal(4, l.Vertices.Length);
        Assert.Equal(new Vector3(0, 1, 0.5f), l.Vertices[3]);
        Assert.Equal(new Vector2(64, 64), l.TexVertices[2]);

        JklSurface s = Assert.Single(l.Surfaces);
        Assert.Equal(0, s.Material);
        Assert.Equal(0x4u, s.SurfaceFlags);
        Assert.Equal(0x1u, s.FaceFlags);
        Assert.Equal(4, s.Geo);
        Assert.Equal(3, s.Light);
        Assert.Equal(1, s.Tex);
        Assert.Equal(-1, s.Adjoin);
        Assert.Equal(0.25f, s.ExtraLight);
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.VertexIndices);
        Assert.Equal(new[] { 0, 1, 2, 3 }, s.TexIndices);
        Assert.Equal(new[] { 0.5f, 0.6f, 0.7f, 0.8f }, s.Intensities);
        Assert.Equal(new Vector3(0, 0, 1), s.Normal);

        JklSector sec = Assert.Single(l.Sectors);
        Assert.Equal(0, sec.Index);
        Assert.Equal(0x1000u, sec.Flags);
        Assert.Equal(0.37f, sec.AmbientLight);
        Assert.Equal(0.10f, sec.ExtraLight);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), sec.Tint);
        Assert.Equal("wind.wav", sec.Sound);
        Assert.Equal(0.6f, sec.SoundVolume);
        Assert.Equal(0, sec.FirstSurface);
        Assert.Equal(1, sec.SurfaceCount);

        Assert.Equal(2, l.Things.Count);
        JklThing t = l.Things[0];
        Assert.Equal("walkplayer", t.Template);
        Assert.Equal(new Vector3(0.5f, 0.25f, 0.1f), t.Position);
        Assert.Equal(new Vector3(1f, -59.5f, 0f), t.PitchYawRoll);
        Assert.Equal(0, t.Sector);
        Assert.Equal("0x20000401", t.Params["thingflags"]);

        JklCogInstance cog = Assert.Single(l.Cogs);
        Assert.Equal("00_door.cog", cog.CogFile);
        Assert.Equal(new[] { "4", "-1", "8.000000" }, cog.Values);

        Assert.Equal(2, l.Templates.Count);
        Assert.Equal("_actor", l.Templates["walkplayer"].Parent);
        Assert.Contains("UNKNOWNSTUFF", l.SkippedSections);
    }

    [Fact]
    public void Read_ThingFrames_AreKeptAndParsed()
    {
        JklLevel l = JklReader.Read(Minimal());
        var frames = JklReader.ReadFrames(l.Things[1].Params["frame"]);
        Assert.Equal(2, frames.Count);
        Assert.Equal(new Vector3(1, 2, 4), frames[1].Pos);
    }

    [Fact]
    public void Read_IgnoresCommentsAndBlankLines()
    {
        string noisy = "# comment\n\n   \nSECTION: HEADER\n# x\nWorld Gravity 4.00   # trailing\n\n#another\nSECTION: SOUNDS\nWorld sounds 1\n# c\na.wav\n\nend\n";
        JklLevel l = JklReader.Read(noisy);
        Assert.Equal(4f, l.Header.Gravity);
        Assert.Equal(new[] { "a.wav" }, l.Sounds);
    }

    [Fact]
    public void ReadFrames_ParsesPositionsAndAngles()
    {
        var frames = JklReader.ReadFrames("frame=(1.0/2.0/3.0:0.0/90.0/0.0)");
        var f = Assert.Single(frames);
        Assert.Equal(new Vector3(1, 2, 3), f.Pos);
        Assert.Equal(new Vector3(0, 90, 0), f.Pyr);
        Assert.Single(JklReader.ReadFrames("(1.0/2.0/3.0:0.0/90.0/0.0)"));
    }

    [Fact]
    public void Read_MalformedSurface_ThrowsWithLineNumber()
    {
        string bad = "SECTION: GEORESOURCE\nWorld surfaces 1\n0: 0 0x4 0x0 4 3 1 -1 0.0 4 0,0 1,1\n0: 0 0 1\n";
        var ex = Assert.Throws<InvalidDataException>(() => JklReader.Read(bad));
        Assert.Contains("line 3", ex.Message);
    }

    [SkippableFact]
    public void Read_All34Levels()
    {
        Skip.IfNot(File.Exists(Path.Combine(RealDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var levels = JkInstall.ListLevels(RealDir).Where(l => l.Name != "static").ToList();
        Assert.Equal(34, levels.Count);
        foreach (JkLevelRef lv in levels)
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(RealDir, lv.EpisodeGob);
            byte[] data = fs.TryRead($@"jkl\{lv.Name}.jkl")!;
            JklLevel level = JklReader.Read(System.Text.Encoding.Latin1.GetString(data));
            Assert.True(level.Surfaces.Count > 0, lv.Name + ": no surfaces");
            Assert.True(level.Sectors.Count > 0, lv.Name + ": no sectors");
            Assert.DoesNotContain("", level.Materials); // index contigus : un trou decalerait les materiaux des surfaces
            Assert.All(level.Surfaces, s => Assert.True(s.Material < level.Materials.Count, lv.Name + ": material out of range"));
            Assert.All(level.Surfaces, s => Assert.All(s.VertexIndices, v => Assert.InRange(v, 0, level.Vertices.Length - 1)));
            var templates = JkTemplates.FromLevels(null, level);
            Assert.Contains(level.Things, t => templates.Resolve(t.Template).TryGetValue("type", out string? ty) && ty == "player");
        }
    }
}

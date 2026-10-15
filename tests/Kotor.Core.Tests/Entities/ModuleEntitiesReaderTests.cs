using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Layout;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Tests.Archives;
using Kotor.Core.Tests.Gff;

namespace Kotor.Core.Tests.Entities;

public class ModuleEntitiesReaderTests
{
    private sealed class FakeKotor : IDisposable
    {
        public string Dir { get; } = Path.Combine(Path.GetTempPath(), "k2j_ent_" + Guid.NewGuid().ToString("N"));
        public ResourceResolver Res { get; }
        public ModuleInfo Info { get; } = new("tst_m99zz", "m99zz", "Zone", new ModuleEntry(Vector3.Zero, Vector2.UnitY));
        public LayoutFile Layout { get; } = LayoutFile.Parse("beginlayout\n roomcount 0\n trackcount 0\n obstaclecount 0\n doorhookcount 0\ndonelayout\n");

        public FakeKotor(byte[]? git)
        {
            Directory.CreateDirectory(Path.Combine(Dir, "modules"));
            var entries = new List<(string, uint, byte[])> { ("dummy", (uint)ResourceType.Lyt, new byte[1]) };
            if (git is not null) entries.Add(("m99zz", (uint)ResourceType.Git, git));
            File.WriteAllBytes(Path.Combine(Dir, "modules", "tst_m99zz.rim"), ArchiveBuilders.Rim(entries.ToArray()));
            Res = ResourceResolver.ForModule(Dir, "tst_m99zz");
        }

        public void Dispose() { Res.Dispose(); Directory.Delete(Dir, true); }
    }

    private static byte[] SyntheticGit(bool withBadEntry = false)
    {
        var g = new GffBuilder();
        int plc = g.AddStruct(9);
        g.Add(plc, "TemplateResRef", 11, "plc_box").Add(plc, "Tag", 10, "Caisse")
         .Add(plc, "X", 8, 1f).Add(plc, "Y", 8, 2f).Add(plc, "Z", 8, 3f).Add(plc, "Bearing", 8, MathF.PI / 2);
        int door = g.AddStruct(8);
        g.Add(door, "TemplateResRef", 11, "door_a")
         .Add(door, "X", 8, 10f).Add(door, "Y", 8, 20f).Add(door, "Z", 8, 0f).Add(door, "Bearing", 8, 0f);
        int wp = g.AddStruct(5);
        g.Add(wp, "Tag", 10, "wp_01")
         .Add(wp, "XPosition", 8, 5f).Add(wp, "YPosition", 8, 6f).Add(wp, "ZPosition", 8, 7f)
         .Add(wp, "XOrientation", 8, -1f).Add(wp, "YOrientation", 8, 0f);
        int p0 = g.AddStruct(0), p1 = g.AddStruct(0), p2 = g.AddStruct(0);
        foreach (var (p, x, y, z) in new[] { (p0, 0f, 0f, 0f), (p1, 4f, 0f, 0f), (p2, 0f, 4f, 1f) })
            g.Add(p, "PointX", 8, x).Add(p, "PointY", 8, y).Add(p, "PointZ", 8, z);
        int trg = g.AddStruct(1);
        g.Add(trg, "TemplateResRef", 11, "trg_a")
         .Add(trg, "XPosition", 8, 100f).Add(trg, "YPosition", 8, 200f).Add(trg, "ZPosition", 8, 300f)
         .Add(trg, "Geometry", 15, new[] { p0, p1, p2 });
        g.Add(g.Root, "Placeable List", 15, new[] { plc });
        g.Add(g.Root, "Door List", 15, new[] { door });
        g.Add(g.Root, "WaypointList", 15, new[] { wp });
        var trigs = new List<int> { trg };
        if (withBadEntry)
        {
            int bad = g.AddStruct(1);
            g.Add(bad, "TemplateResRef", 11, "trg_bad");
            trigs.Add(bad);
        }
        g.Add(g.Root, "TriggerList", 15, trigs.ToArray());
        return g.Build("GIT ");
    }

    [Fact]
    public void Reads_synthetic_git()
    {
        using var k = new FakeKotor(SyntheticGit());
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Equal(4, ents.Count);

        var plc = Assert.Single(ents, e => e.Kind == EntityKind.Placeable);
        Assert.Equal("Caisse", plc.Name);
        Assert.Equal("plc_box", plc.Template);
        Assert.Equal(new Vector3(1, 2, 3), plc.Position);
        Assert.Equal(90f, plc.Yaw, 3);
        Assert.Null(plc.Polygon);

        var door = Assert.Single(ents, e => e.Kind == EntityKind.Door);
        Assert.Equal("door_a", door.Name);
        Assert.Equal(new Vector3(10, 20, 0), door.Position);
        Assert.Equal(0f, door.Yaw, 3);

        var wp = Assert.Single(ents, e => e.Kind == EntityKind.Waypoint);
        Assert.Equal("wp_01", wp.Name);
        Assert.Equal(new Vector3(5, 6, 7), wp.Position);
        Assert.Equal(90f, wp.Yaw, 3); // orientation (-1,0) = meme angle que Bearing pi/2

        var trg = Assert.Single(ents, e => e.Kind == EntityKind.Trigger);
        Assert.Equal(new[] { new Vector3(100, 200, 300), new Vector3(104, 200, 300), new Vector3(100, 204, 301) }, trg.Polygon);

        var bilan = Assert.Single(log.Warnings, w => w.Code == "ENTITIES");
        Assert.Contains("Placeable=1", bilan.Message);
        Assert.Contains("Light=0", bilan.Message);
    }

    [Fact]
    public void Invalid_entry_is_skipped_and_logged()
    {
        using var k = new FakeKotor(SyntheticGit(withBadEntry: true));
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Equal(4, ents.Count);
        Assert.Single(log.Warnings, w => w.Code == "GIT_ENTRY_INVALID");
    }

    [Fact]
    public void Git_without_lists_logs_one_entry_per_list()
    {
        var g = new GffBuilder();
        g.Add(g.Root, "Tag", 10, "x");
        using var k = new FakeKotor(g.Build("GIT "));
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Empty(ents);
        Assert.Equal(9, log.Warnings.Count(w => w.Code == "GIT_LIST_MISSING"));
        Assert.Contains(log.Warnings, w => w.Code == "GIT_LIST_MISSING" && w.Message.Contains("Placeable List"));
    }

    [Fact]
    public void Non_list_field_logs_list_missing()
    {
        var g = new GffBuilder();
        g.Add(g.Root, "Placeable List", 10, "pas une liste");
        using var k = new FakeKotor(g.Build("GIT "));
        var log = new ImportLog();
        ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Contains(log.Warnings, w => w.Code == "GIT_LIST_MISSING" && w.Message.Contains("n'est pas une liste"));
    }

    [Fact]
    public void Position_of_wrong_type_is_invalid_not_zero()
    {
        var g = new GffBuilder();
        int plc = g.AddStruct(9);
        g.Add(plc, "X", 5, 1).Add(plc, "Y", 8, 2f).Add(plc, "Z", 8, 3f).Add(plc, "Bearing", 8, 0f);
        g.Add(g.Root, "Placeable List", 15, new[] { plc });
        using var k = new FakeKotor(g.Build("GIT "));
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Empty(ents);
        Assert.Contains(log.Warnings, w => w.Code == "GIT_ENTRY_INVALID" && w.Message.Contains("type inattendu"));
    }

    [Fact]
    public void Missing_orientation_is_logged_and_yaw_zero()
    {
        var g = new GffBuilder();
        int plc = g.AddStruct(9);
        g.Add(plc, "X", 8, 1f).Add(plc, "Y", 8, 2f).Add(plc, "Z", 8, 3f);
        int wp = g.AddStruct(5);
        g.Add(wp, "XPosition", 8, 1f).Add(wp, "YPosition", 8, 2f).Add(wp, "ZPosition", 8, 3f);
        g.Add(g.Root, "Placeable List", 15, new[] { plc });
        g.Add(g.Root, "WaypointList", 15, new[] { wp });
        using var k = new FakeKotor(g.Build("GIT "));
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Equal(2, ents.Count);
        Assert.All(ents, e => Assert.Equal(0f, e.Yaw));
        Assert.Equal(2, log.Warnings.Count(w => w.Code == "GIT_ORIENTATION_MISSING"));
    }

    [Fact]
    public void Truncated_git_is_logged_not_thrown()
    {
        var full = SyntheticGit();
        foreach (int len in new[] { 8, 20, 56, full.Length / 2, full.Length - 5 })
        {
            using var k = new FakeKotor(full.AsSpan(0, len).ToArray());
            var log = new ImportLog();
            var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
            Assert.Empty(ents);
            Assert.Contains(log.Warnings, w => w.Code == "GIT_UNREADABLE");
        }
    }

    [Fact]
    public void Missing_git_is_logged()
    {
        using var k = new FakeKotor(null);
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(k.Res, k.Info, k.Layout, log);
        Assert.Empty(ents);
        Assert.Single(log.Warnings, w => w.Code == "GIT_MISSING");
        Assert.Single(log.Warnings, w => w.Code == "ENTITIES");
    }

    [SkippableFact]
    public void Real_tar_m02aa_entities()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var res = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var sceneLog = new ImportLog();
        var scene = ModuleSceneBuilder.Build(res, "tar_m02aa", null, sceneLog);
        var layout = LayoutFile.Parse(Encoding.ASCII.GetString(res.Find("m02aa", ResourceType.Lyt)!));
        var log = new ImportLog();
        var ents = ModuleEntitiesReader.Read(res, scene.Info, layout, log);

        Assert.True(ents.Count(e => e.Kind == EntityKind.Door) >= 1);
        Assert.True(ents.Count(e => e.Kind == EntityKind.Placeable) >= 1);
        Assert.True(ents.Count(e => e.Kind == EntityKind.Waypoint) >= 1);

        int expectedLights = sceneLog.Warnings.Where(w => w.Code == "NODE_LIGHT")
            .Sum(w => int.Parse(Regex.Match(w.Message, @": (\d+) light node").Groups[1].Value));
        Assert.Equal(71, expectedLights); // somme du journal de la phase 1
        Assert.Equal(expectedLights, ents.Count(e => e.Kind == EntityKind.Light));

        var pts = scene.Meshes.SelectMany(m => m.Positions).Concat(scene.Walkmeshes.SelectMany(w => w.Positions)).ToList();
        var mn = pts.Aggregate(new Vector3(float.MaxValue), Vector3.Min) - new Vector3(5);
        var mx = pts.Aggregate(new Vector3(float.MinValue), Vector3.Max) + new Vector3(5);
        foreach (var e in ents)
        {
            Assert.True(e.Position.X >= mn.X && e.Position.Y >= mn.Y && e.Position.Z >= mn.Z
                     && e.Position.X <= mx.X && e.Position.Y <= mx.Y && e.Position.Z <= mx.Z,
                $"{e.Kind} {e.Name} hors boite : {e.Position} (boite {mn} .. {mx})");
            Assert.True(float.IsFinite(e.Yaw));
        }

        var dir = FindOutDir();
        Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        foreach (var g in ents.GroupBy(e => e.Kind)) sb.AppendLine($"# {g.Key} : {g.Count()}");
        foreach (var e in ents.OrderBy(e => e.Kind))
            sb.AppendLine(FormattableString.Invariant(
                $"{e.Kind}\t{e.Name}\t{e.Template}\t{e.Position.X:F2}\t{e.Position.Y:F2}\t{e.Position.Z:F2}\t{e.Yaw:F1}\t{(e.Polygon is null ? "" : e.Polygon.Length + " pts")}"));
        File.WriteAllText(Path.Combine(dir, "entities-tar_m02aa.txt"), sb.ToString());
    }

    private static string FindOutDir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Kotor2Ja.sln"))) d = d.Parent;
        return Path.Combine(d?.FullName ?? Path.GetTempPath(), "out");
    }
}

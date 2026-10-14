using System.Globalization;
using System.Numerics;
using System.Text;
using Kotor.Core.Archives;
using Kotor.Core.Layout;
using Kotor.Core.Mdl;

namespace Kotor.Core.Tests.Mdl;

public class MdlReaderTests
{
    private static KotorModel Load(ResourceResolver r, string name)
    {
        byte[]? mdl = r.Find(name, ResourceType.Mdl);
        byte[]? mdx = r.Find(name, ResourceType.Mdx);
        Assert.NotNull(mdl);
        Assert.NotNull(mdx);
        return MdlReader.Read(mdl!, mdx!);
    }

    private static string StatsPath()
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "Kotor2Ja.sln"))) d = Path.GetDirectoryName(d);
        string dir = Path.Combine(d ?? AppContext.BaseDirectory, "out");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "mdl-stats.txt");
    }

    private static readonly object StatsLock = new();

    private static void AppendStats(string text)
    {
        lock (StatsLock) File.AppendAllText(StatsPath(), text);
    }

    private static void CheckMesh(string model, KotorNode n)
    {
        var m = n.Mesh!;
        Assert.True(m.Triangles.Length % 3 == 0, model + "/" + n.Name);
        foreach (int i in m.Triangles) Assert.InRange(i, 0, m.Positions.Length - 1);
        if (m.HasLightmap) Assert.Equal(m.Positions.Length, m.Uv1?.Length);
        foreach (var p in m.Positions)
            Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), model + "/" + n.Name);
    }

    [SkippableFact]
    public void Real_room_model_reads()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var model = Load(r, "m02aa_01a");
        bool lightmapped = false;
        foreach (var (node, _) in model.Walk())
        {
            if (node.Mesh is null) continue;
            CheckMesh("m02aa_01a", node);
            if (node.Mesh.Render && node.Mesh.Texture1 is { } t && t.StartsWith("m02aa_01a_lm", StringComparison.Ordinal))
                lightmapped = true;
        }
        Assert.True(lightmapped);
    }

    [SkippableFact]
    public void Real_room_bbox_is_plausible()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var model = Load(r, "m02aa_01a");
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var (node, tr) in model.Walk())
        {
            if (node.Mesh is null) continue;
            foreach (var p in node.Mesh.Positions)
            {
                var w = Vector3.Transform(p, tr);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }
        }
        var size = max - min;
        Assert.InRange(size.X, 1f, 500f);
        Assert.InRange(size.Y, 1f, 500f);
    }

    [SkippableFact]
    public void Real_all_tar_m02aa_rooms_read()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var lyt = LayoutFile.Parse(Encoding.ASCII.GetString(r.Find("m02aa", ResourceType.Lyt)!));
        var sb = new StringBuilder("# tar_m02aa rooms\n");
        foreach (var room in lyt.Rooms)
        {
            var model = Load(r, room.Model);
            var kinds = new SortedDictionary<NodeKind, int>();
            long tris = 0;
            foreach (var (node, _) in model.Walk())
            {
                kinds[node.Kind] = kinds.GetValueOrDefault(node.Kind) + 1;
                if (node.Mesh is null) continue;
                CheckMesh(room.Model, node);
                tris += node.Mesh.Triangles.Length / 3;
            }
            sb.Append(room.Model).Append(": ")
              .Append(string.Join(", ", kinds.Select(k => $"{k.Key}={k.Value}")))
              .Append(CultureInfo.InvariantCulture, $"; triangles={tris}\n");
        }
        AppendStats(sb.ToString());
    }

    // Corps entier debout (Bastila, tete + corps, pose de repos). NB : aucun des 2832 modeles de models.bif ne porte de
    // controleur d'orientation compresse dans ses noeuds (ils sont dans les animations, non lues) : le decodeur est
    // donc couvert par le test unitaire, et ce test valide quaternions normalises et hauteur.
    private const string HumanModel = "p_bastilabb";

    [SkippableFact]
    public void Real_compressed_orientation_model()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var model = Load(r, HumanModel);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        int nodes = 0;
        foreach (var (node, tr) in model.Walk())
        {
            nodes++;
            Assert.InRange(node.Orientation.Length(), 0.999f, 1.001f);
            if (node.Mesh is null) continue;
            CheckMesh(HumanModel, node);
            foreach (var p in node.Mesh.Positions)
            {
                var w = Vector3.Transform(p, tr);
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }
        }
        float height = max.Z - min.Z;
        AppendStats(string.Create(CultureInfo.InvariantCulture,
            $"# {HumanModel}: nodes={nodes}, bbox min=({min.X:F3},{min.Y:F3},{min.Z:F3}) max=({max.X:F3},{max.Y:F3},{max.Z:F3}), height_Z={height:F3} m\n"));
        Assert.InRange(height, 0.5f, 5f);
    }


    [SkippableFact]
    public void Real_every_models_bif_model_reads()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var key = KeyBifArchive.Open(TestEnv.KotorDir!);
        int count = 0;
        foreach (var k in key.Keys.Where(k => k.Type == ResourceType.Mdl).OrderBy(k => k.ResRef))
        {
            Assert.True(key.TryRead(k, out var mdl));
            key.TryRead(new ResourceKey(k.ResRef, ResourceType.Mdx), out var mdx);
            var model = MdlReader.Read(mdl, mdx ?? Array.Empty<byte>());
            foreach (var (node, _) in model.Walk())
            {
                Assert.InRange(node.Orientation.Length(), 0.99f, 1.01f);
                if (node.Mesh is not null) CheckMesh(k.ResRef, node);
            }
            count++;
        }
        AppendStats($"# models.bif: {count} models read without exception\n");
    }
    [Fact]
    public void Compressed_quaternion_decodes_identity()
    {
        // x = 1 - (t & 0x7FF)/1023 = 0, y = 1 - ((t >> 11) & 0x7FF)/1023 = 0, z = 1 - (t >> 22)/511 = 0
        uint identity = 1023u | (1023u << 11) | (511u << 22);
        var q = MdlReader.DecodeCompressedQuaternion(identity);
        Assert.Equal(0f, q.X, 5);
        Assert.Equal(0f, q.Y, 5);
        Assert.Equal(0f, q.Z, 5);
        Assert.Equal(1f, q.W, 5);
    }

    [Fact]
    public void Truncated_file_is_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => MdlReader.Read(new byte[8], Array.Empty<byte>()));
    }
}

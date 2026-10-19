using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Jk.Core.Gob;
using Jk.Core.Textures;
using Jk.Pipeline;
using Kotor.Core.Diagnostics;
using Kotor.Core.Textures;

namespace Jk.Pipeline.Tests;

public class JkModelConverterTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    // --- Fabrication de fichiers synthetiques ---

    /// <summary>3DO a un noeud et une face carree (x de x0 a x1, y de 0 a 1, z = 0), texture <c>fan.mat</c> de 64 x 64.</summary>
    private static string Fan3do(float x0 = 1f, float x1 = 2f) => Fan3doText(F(x0), F(x1));

    private static string F(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static string Fan3doText(string x0, string x1) => $$"""
        SECTION: HEADER
        3DO 2.1
        SECTION: MODELRESOURCE
        MATERIALS 1
        0: fan.mat
        SECTION: GEOMETRYDEF
        RADIUS 2.0
        INSERT OFFSET 0.0 0.0 0.0
        GEOSETS 1
        GEOSET 0
        MESHES 1
        MESH 0
        NAME blade
        RADIUS 2.0
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 4
        0: {{x0}} 0.0 0.0 0.0
        1: {{x1}} 0.0 0.0 0.0
        2: {{x1}} 1.0 0.0 0.0
        3: {{x0}} 1.0 0.0 0.0
        TEXTURE VERTICES 4
        0: 0.0 0.0
        1: 64.0 0.0
        2: 64.0 -64.0
        3: 0.0 -64.0
        VERTEX NORMALS
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        3: 0.0 0.0 1.0
        FACES 1
        0: 0 0x0000 4 3 1 0.0 4 0,0 1,1 2,2 3,3
        FACE NORMALS
        0: 0.0 0.0 1.0
        SECTION: HIERARCHYDEF
        HIERARCHY NODES 1
        0: 0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade
        """;

    private static string Key(float fps, int frames, params string[] entries) => KeyText(F(fps), frames, entries);

    private static string KeyText(string fps, int frames, string[] entries) => $"""
        SECTION: HEADER
        FLAGS 0x0000
        TYPE 0xFFFF
        FRAMES {frames}
        FPS {fps}
        JOINTS 1
        SECTION: KEYFRAME NODES
        NODES 1
        NODE 0
        MESH NAME blade
        ENTRIES {entries.Length}
        {string.Join("\n", entries.Select((e, i) => $"{i}: {e}\n0 0 0 0 0 0"))}
        """;

    private static string Entry(int frame, float x, float y, float z, float yaw) =>
        FormattableString.Invariant($"{frame} 0x0001 {x} {y} {z} 0 {yaw} 0");

    private static byte[] Gob(params (string Path, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("GOB "));
        w.Write(0x14);
        w.Write(0);
        var offsets = new List<(int, int)>();
        foreach (var f in files)
        {
            byte[] data = Encoding.Latin1.GetBytes(f.Content);
            offsets.Add(((int)ms.Position, data.Length));
            w.Write(data);
        }
        int index = (int)ms.Position;
        w.Write(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            w.Write(offsets[i].Item1); w.Write(offsets[i].Item2);
            byte[] name = new byte[128];
            Encoding.ASCII.GetBytes(files[i].Path).CopyTo(name, 0);
            w.Write(name);
        }
        w.Flush();
        byte[] bytes = ms.ToArray();
        BitConverter.GetBytes(index).CopyTo(bytes, 8);
        return bytes;
    }

    private static JkFileSystem Fs(out string dir, params (string Path, string Content)[] files)
    {
        dir = Path.Combine(Path.GetTempPath(), "jkmodel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        File.WriteAllBytes(Path.Combine(dir, "Episode", "JK1.GOB"), Gob(files));
        return JkFileSystem.ForEpisode(dir, "JK1.GOB");
    }

    private static Dictionary<string, JkMaterial> Mats(int size = 64)
    {
        var tex = new KotorTexture(size, size, new byte[size * size * 4], false, TxiInfo.Empty);
        return new(StringComparer.OrdinalIgnoreCase) { ["fan.mat"] = new JkMaterial("fan.mat", [tex], false, false) };
    }

    private static int I(byte[] d, int o) => BinaryPrimitives.ReadInt32LittleEndian(d.AsSpan(o));

    // --- Tests ---

    [Fact]
    public void Converter_PureSpin_DetectedAsRotation()
    {
        // Lacet 0, 90, 180, 270, 360 : un quart de tour par image a 10 images par seconde = 900 degres par seconde autour de +Z.
        string key = Key(10, 5, Entry(0, 0, 0, 0, 0), Entry(1, 0, 0, 0, 90), Entry(2, 0, 0, 0, 180), Entry(3, 0, 0, 0, 270), Entry(4, 0, 0, 0, 360));
        using JkFileSystem fs = Fs(out string dir, (@"3do\fan.3do", Fan3do()), (@"3do\key\fan.key", key));
        try
        {
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), log);

            Assert.NotNull(m);
            Assert.True(m!.PureRotation);
            Assert.Equal(0f, m.RotationAxis.X, 3); Assert.Equal(0f, m.RotationAxis.Y, 3); Assert.Equal(1f, m.RotationAxis.Z, 3);
            Assert.Equal(900f, m.DegreesPerSecond, 1);
            Assert.Equal(10f, m.Fps);
            Assert.Equal(1, m.Frames); // images du MD3 (le fichier .key en avait 5)
            Assert.Equal("models/jk/fan.md3", m.Asset.Path);
            Assert.Equal(["fan"], m.Asset.Textures);
            // Rotation pure : le moteur fait tourner l'entite, le MD3 ne garde qu'une image.
            Assert.Equal(1, I(m.Asset.Data, 76));
            Assert.Empty(log.Warnings);
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Converter_Translation_IsNotPureRotation_AndKeepsAllFrames()
    {
        string key = Key(10, 3, Entry(0, 0, 0, 0, 0), Entry(2, 0, 0, 0.5f, 0));
        using JkFileSystem fs = Fs(out string dir, (@"3do\fan.3do", Fan3do()), (@"3do\key\fan.key", key));
        try
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), new ImportLog());

            Assert.NotNull(m);
            Assert.False(m!.PureRotation);
            Assert.Equal(3, m.Frames);
            Assert.Equal(3, I(m.Asset.Data, 76));
            // Boite englobante de toutes les images (metres de la scene) : le carre monte de 0,5 unite JK.
            float metres = JkWorldScale.MetresPerJkUnit;
            Assert.Equal(0.5f * metres, m.Max.Z, 3);
            Assert.Equal(0f, m.Min.Z, 3);
            Assert.Equal(1f * metres, m.Min.X, 3);
            Assert.Equal(2f * metres, m.Max.X, 3);
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Converter_NoKey_OneFrame_UvFromMaterialSize()
    {
        using JkFileSystem fs = Fs(out string dir, (@"3do\fan.3do", Fan3do(1f, 1.4f)));
        try
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "FAN.3DO", null, 40f, Mats(), new ImportLog());

            Assert.NotNull(m);
            Assert.False(m!.PureRotation);
            Assert.Equal(1, m.Frames);
            byte[] d = m.Asset.Data;
            int s = I(d, 100);
            int ofsSt = I(d, s + 96), ofsXyz = I(d, s + 100), nV = I(d, s + 80);
            Assert.Equal(4, nV);
            // Sommet 0 : texvert (0, 0) sur 64 x 64 -> s = 0, t (fichier) = v / hauteur = 0.
            Assert.Equal(0f, BitConverter.ToSingle(d, s + ofsSt), 4);
            Assert.Equal(0f, BitConverter.ToSingle(d, s + ofsSt + 4), 4);
            // Sommet 2 : texvert (64, -64) -> s = 1, t = -1 (repetition de la texture).
            Assert.Equal(1f, BitConverter.ToSingle(d, s + ofsSt + 16), 4);
            Assert.Equal(-1f, BitConverter.ToSingle(d, s + ofsSt + 20), 4);
            // Echelle : 1 unite JK = 8,6 m = 344 unites JA ; le sommet 0 est a x = 1.
            Assert.Equal((short)(344 * 64), BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(s + ofsXyz)));
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Converter_Oversize_DividedBelowMd3Limit()
    {
        // x jusqu'a 4 unites JK = 1376 unites JA > 511 : division par 3, modelscale a poser sur l'entite.
        using JkFileSystem fs = Fs(out string dir, (@"3do\big.3do", Fan3do(1f, 4f)));
        try
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "big.3do", null, 40f, Mats(), new ImportLog());

            Assert.NotNull(m);
            Assert.Equal(3, m!.ModelScale);
            byte[] d = m.Asset.Data;
            int s = I(d, 100), ofsXyz = I(d, s + 100);
            short x1 = BinaryPrimitives.ReadInt16LittleEndian(d.AsSpan(s + ofsXyz + 8)); // sommet 1 : x = 4
            Assert.Equal((short)Math.Round(4 * 344 / 3f * 64), x1);
            Assert.True(x1 / 64f <= 511f);
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Converter_MissingModel_ReturnsNullWithWarning()
    {
        using JkFileSystem fs = Fs(out string dir, (@"3do\fan.3do", Fan3do()));
        try
        {
            var log = new ImportLog();
            Assert.Null(JkModelConverter.ToMd3(fs, "nothere.3do", null, 40f, Mats(), log));
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_MISSING");
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Converter_MissingMaterial_KeepsFaceAndWarns()
    {
        using JkFileSystem fs = Fs(out string dir, (@"3do\fan.3do", Fan3do()));
        try
        {
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, new Dictionary<string, JkMaterial>(), log);

            Assert.NotNull(m);
            Assert.Equal(["fan"], m!.Asset.Textures);
            Assert.Contains(log.Warnings, w => w.Code == "TEXTURE_MISSING");
        }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    [SkippableFact]
    public void Converter_AllModelsInRes2_Convert()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, "JK1.GOB");
        var failures = new List<string>();
        int ok = 0;
        foreach (string path in fs.List("3do", ".3do"))
        {
            string name = Path.GetFileName(path);
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, name, null, 40f, new Dictionary<string, JkMaterial>(), log);
            if (m is null) failures.Add($"{name}: {string.Join("; ", log.Warnings.Where(w => w.Code != "TEXTURE_MISSING").Select(w => w.Code + " " + w.Message))}");
            else ok++;
        }
        // sphere.3do ne contient que des faces en mode « points » (196 faces geo 1) : rien a dessiner, journalise JK_MODEL_EMPTY.
        Assert.True(failures.All(x => x.StartsWith("sphere.3do:", StringComparison.Ordinal)), $"{ok} converted, {failures.Count} failed:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(30)));
        Assert.Equal(476, ok);
    }
}

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

    private const string BladeFace = "0 0x0000 4 3 1 0.0 4 0,0 1,1 2,2 3,3";
    private const string BladeNode = "0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade";

    private static string Fan3doText(string x0, string x1) => Build3do(x0, x1, [BladeFace], [BladeNode]);

    /// <summary>
    /// 3DO synthetique : un maillage a quatre sommets (carre x0..x1, y 0..1, z = 0), les faces et les noeuds donnes (sans leur indice, ajoute ici).
    /// <paramref name="noGeoset"/> : fichier tronque sans aucun geoset (pas de maillage, noeuds sans maillage).
    /// </summary>
    private static string Build3do(string x0, string x1, string[] faces, string[] nodes, bool noGeoset = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SECTION: HEADER\n3DO 2.1\nSECTION: MODELRESOURCE\nMATERIALS 1\n0: fan.mat\nSECTION: GEOMETRYDEF\nRADIUS 2.0\nINSERT OFFSET 0.0 0.0 0.0");
        if (noGeoset) sb.AppendLine("GEOSETS 0");
        else
        {
            sb.AppendLine($"GEOSETS 1\nGEOSET 0\nMESHES 1\nMESH 0\nNAME blade\nRADIUS 2.0\nGEOMETRYMODE 4\nLIGHTINGMODE 3\nTEXTUREMODE 1");
            sb.AppendLine($"VERTICES 4\n0: {x0} 0.0 0.0 0.0\n1: {x1} 0.0 0.0 0.0\n2: {x1} 1.0 0.0 0.0\n3: {x0} 1.0 0.0 0.0");
            sb.AppendLine("TEXTURE VERTICES 4\n0: 0.0 0.0\n1: 64.0 0.0\n2: 64.0 -64.0\n3: 0.0 -64.0");
            sb.AppendLine("VERTEX NORMALS\n0: 0.0 0.0 1.0\n1: 0.0 0.0 1.0\n2: 0.0 0.0 1.0\n3: 0.0 0.0 1.0");
            sb.AppendLine($"FACES {faces.Length}");
            for (int i = 0; i < faces.Length; i++) sb.AppendLine($"{i}: {faces[i]}");
            sb.AppendLine("FACE NORMALS");
            for (int i = 0; i < faces.Length; i++) sb.AppendLine($"{i}: 0.0 0.0 1.0");
        }
        sb.AppendLine($"SECTION: HIERARCHYDEF\nHIERARCHY NODES {nodes.Length}");
        for (int i = 0; i < nodes.Length; i++) sb.AppendLine($"{i}: {nodes[i]}");
        return sb.ToString();
    }

    private static string Key(float fps, int frames, params string[] entries) => KeyText(F(fps), frames, entries);

    private static string KeyText(string fps, int frames, string[] entries) => KeyTracks(fps, frames, ("blade", entries));

    /// <summary>Animation synthetique : une piste par (nom de noeud, entrees <c>frame flags x y z p y r</c>) ; la ligne de variations est mise a zero.</summary>
    private static string KeyTracks(string fps, int frames, params (string Name, string[] Entries)[] tracks)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"SECTION: HEADER\nFLAGS 0x0000\nTYPE 0xFFFF\nFRAMES {frames}\nFPS {fps}\nJOINTS {tracks.Length}\nSECTION: KEYFRAME NODES\nNODES {tracks.Length}");
        for (int t = 0; t < tracks.Length; t++)
        {
            sb.AppendLine($"NODE {t}\nMESH NAME {tracks[t].Name}\nENTRIES {tracks[t].Entries.Length}");
            for (int i = 0; i < tracks[t].Entries.Length; i++) sb.AppendLine($"{i}: {tracks[t].Entries[i]}\n0 0 0 0 0 0");
        }
        return sb.ToString();
    }

    private static string Entry(int frame, float x, float y, float z, float yaw) =>
        FormattableString.Invariant($"{frame} 0x0001 {x} {y} {z} 0 {yaw} 0");

    private static string EntryPyr(int frame, float x, float y, float z, float pitch, float yaw, float roll) =>
        FormattableString.Invariant($"{frame} 0x0001 {x} {y} {z} {pitch} {yaw} {roll}");

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

    private const string RootNoMesh = "0x0000 0x00001 -1 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 root";

    private static void WithFs(string model, string? keyName, string? keyContent, Action<JkFileSystem> body)
    {
        var files = new List<(string, string)> { (@"3do\fan.3do", model) };
        if (keyName is not null && keyContent is not null) files.Add((@"3do\key\" + keyName, keyContent));
        using JkFileSystem fs = Fs(out string dir, [.. files]);
        try { body(fs); }
        finally { fs.Dispose(); Directory.Delete(dir, true); }
    }

    private static readonly string[] SpinYaw =
        [Entry(0, 0, 0, 0, 0), Entry(1, 0, 0, 0, 90), Entry(2, 0, 0, 0, 180), Entry(3, 0, 0, 0, 270), Entry(4, 0, 0, 0, 360)];

    [Fact]
    public void Converter_TruncatedModelWithoutGeoset_ReturnsNullWithModelEmpty()
    {
        WithFs(Build3do("1", "2", [], [RootNoMesh], noGeoset: true), null, null, fs =>
        {
            var log = new ImportLog();
            Assert.Null(JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), log));
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_EMPTY");
        });
    }

    [Theory]
    [InlineData("0 0x0000 4 3 1 0.0 4 0,0 1,1 2,2 9,3")]
    [InlineData("0 0x0000 4 3 1 0.0 4 0,0 -1,1 2,2 3,3")]
    public void Converter_VertexIndexOutOfRange_ReturnsNullWithUnreadable(string face)
    {
        WithFs(Build3do("1", "2", [face], [BladeNode]), null, null, fs =>
        {
            var log = new ImportLog();
            Assert.Null(JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), log));
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_UNREADABLE");
        });
    }

    [Fact]
    public void Converter_CyclicHierarchy_ReturnsNullWithUnreadable()
    {
        string a = "0x0000 0x00001 0 1 1 -1 1 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade";
        string b = "0x0000 0x00001 -1 0 0 -1 1 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 other";
        WithFs(Build3do("1", "2", [BladeFace], [a, b]), null, null, fs =>
        {
            var log = new ImportLog();
            Assert.Null(JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), log));
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_UNREADABLE");
        });
    }

    [Fact]
    public void Converter_OnlyPointFaces_ReturnsNullWithModelEmpty()
    {
        WithFs(Build3do("1", "2", ["0 0x0000 1 3 1 0.0 4 0,0 1,1 2,2 3,3"], [BladeNode]), null, null, fs =>
        {
            var log = new ImportLog();
            Assert.Null(JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), log));
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_FACES_HIDDEN");
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_EMPTY");
        });
    }

    [Fact]
    public void Converter_HiddenFaces_AreSkippedAndWarned()
    {
        WithFs(Build3do("1", "2", [BladeFace, "0 0x0000 0 3 1 0.0 4 0,0 1,1 2,2 3,3"], [BladeNode]), null, null, fs =>
        {
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), log);
            Assert.NotNull(m);
            Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_FACES_HIDDEN" && w.Message.Contains("1 face"));
            int s = I(m!.Asset.Data, 100);
            Assert.Equal(4, I(m.Asset.Data, s + 80));  // une seule face dessinee : 4 sommets
            Assert.Equal(2, I(m.Asset.Data, s + 84));  // deux triangles
        });
    }

    [Fact]
    public void Converter_MissingKey_ConvertsStaticWithWarning()
    {
        WithFs(Fan3do(), null, null, fs =>
        {
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "nothere.key", 40f, Mats(), log);
            Assert.NotNull(m);
            Assert.Equal(1, m!.Frames);
            Assert.Equal(0f, m.Fps);
            Assert.Contains(log.Warnings, w => w.Code == "JK_KEY_MISSING");
        });
    }

    [Fact]
    public void Converter_UnreadableKey_ConvertsStaticWithWarning()
    {
        WithFs(Fan3do(), "fan.key", "this is not a key file", fs =>
        {
            var log = new ImportLog();
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), log);
            Assert.NotNull(m);
            Assert.Equal(1, m!.Frames);
            Assert.Contains(log.Warnings, w => w.Code == "JK_KEY_UNREADABLE");
        });
    }

    [Fact]
    public void Converter_TwoNodesWithPivot_AppliesPivotHierarchyAndRotation()
    {
        // Racine en (10, 0, 0), lacet 90 ; enfant en (0, 2, 0) de la racine, pivot (0, 0, 3). Sommet (1, 0, 0) :
        // + pivot -> (1, 0, 3) ; + position enfant -> (1, 2, 3) ; lacet 90 (x, y) -> (-y, x) -> (-2, 1, 3) ; + (10, 0, 0) -> (8, 1, 3).
        string root = "0x0000 0x00001 -1 -1 1 -1 1 10.0 0.0 0.0 0.0 90.0 0.0 0.0 0.0 0.0 root";
        string child = "0x0000 0x00001 0 0 -1 -1 0 0.0 2.0 0.0 0.0 0.0 0.0 0.0 0.0 3.0 blade";
        WithFs(Build3do("1", "2", [BladeFace], [root, child]), null, null, fs =>
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", null, 40f, Mats(), new ImportLog());
            Assert.NotNull(m);
            float k = JkWorldScale.MetresPerJkUnit;
            // Carre (x 1..2, y 0..1) -> x' = 10 - (y + 2) dans [7, 8], y' = x dans [1, 2], z' = 3 (signe du pivot positif).
            Assert.Equal(7f * k, m!.Min.X, 3); Assert.Equal(8f * k, m.Max.X, 3);
            Assert.Equal(1f * k, m.Min.Y, 3); Assert.Equal(2f * k, m.Max.Y, 3);
            Assert.Equal(3f * k, m.Min.Z, 3); Assert.Equal(3f * k, m.Max.Z, 3);
        });
    }

    [Fact]
    public void Converter_SpinWithFixedGeometryOutsideSubtree_IsNotPureRotation()
    {
        // Deux noeuds racine portant chacun un maillage : seul « spin » tourne, « fixed » est une partie fixe : pas de func_rotating.
        string fixedNode = "0x0000 0x00001 0 -1 -1 1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 fixed";
        string spinNode = "0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 spin";
        string key = KeyTracks("10", 5, ("spin", SpinYaw));
        WithFs(Build3do("1", "2", [BladeFace], [fixedNode, spinNode]), "fan.key", key, fs =>
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), new ImportLog());
            Assert.NotNull(m);
            Assert.False(m!.PureRotation);
            Assert.Equal(5, m.Frames);
        });
    }

    [Fact]
    public void Converter_SpinOfSubtreeHoldingAllGeometry_IsPureRotation()
    {
        // La racine sans maillage ne porte rien : toute la geometrie dessinee est dans le sous-arbre du noeud qui tourne seul.
        string root = "0x0000 0x00001 -1 -1 1 -1 1 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 root";
        string child = "0x0000 0x00001 0 0 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade";
        string key = KeyTracks("10", 5, ("blade", SpinYaw));
        WithFs(Build3do("1", "2", [BladeFace], [root, child]), "fan.key", key, fs =>
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), new ImportLog());
            Assert.NotNull(m);
            Assert.True(m!.PureRotation);
            Assert.Equal(1, m.Frames);
        });
    }

    [Fact]
    public void Converter_PureRotation_CenterAndAxisUseKeyPoseOfFrameZeroNotRestPose()
    {
        // La racine est au repos a l'origine sans rotation, mais sa piste la pose en (5, 0, 0) avec un lacet de 90 (valeurs absolues, et
        // c'est la pose ecrite dans le MD3). L'enfant tourne en roulis (axe +Y du parent) : le centre est (5, 0, 0) et l'axe devient -X.
        string root = "0x0000 0x00001 -1 -1 1 -1 1 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 root";
        string child = "0x0000 0x00001 0 0 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade";
        string key = KeyTracks("10", 5,
            ("root", [EntryPyr(0, 5, 0, 0, 0, 90, 0)]),
            ("blade", [EntryPyr(0, 0, 0, 0, 0, 0, 0), EntryPyr(1, 0, 0, 0, 0, 0, 90), EntryPyr(2, 0, 0, 0, 0, 0, 180),
                EntryPyr(3, 0, 0, 0, 0, 0, 270), EntryPyr(4, 0, 0, 0, 0, 0, 360)]));
        WithFs(Build3do("1", "2", [BladeFace], [root, child]), "fan.key", key, fs =>
        {
            JkMd3? m = JkModelConverter.ToMd3(fs, "fan.3do", "fan.key", 40f, Mats(), new ImportLog());
            Assert.NotNull(m);
            Assert.True(m!.PureRotation);
            float k = JkWorldScale.MetresPerJkUnit;
            Assert.Equal(5f * k, m.RotationCenter.X, 3); Assert.Equal(0f, m.RotationCenter.Y, 3); Assert.Equal(0f, m.RotationCenter.Z, 3);
            Assert.Equal(-1f, m.RotationAxis.X, 3); Assert.Equal(0f, m.RotationAxis.Y, 3); Assert.Equal(0f, m.RotationAxis.Z, 3);
            Assert.Equal(900f, m.DegreesPerSecond, 1);
        });
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
            if (m is null && name == "sphere.3do") Assert.Contains(log.Warnings, w => w.Code == "JK_MODEL_EMPTY");
            if (m is null) failures.Add($"{name}: {string.Join("; ", log.Warnings.Where(w => w.Code != "TEXTURE_MISSING").Select(w => w.Code + " " + w.Message))}");
            else ok++;
        }
        // sphere.3do ne contient que des faces en mode « points » (196 faces geo 1) : rien a dessiner, journalise JK_MODEL_EMPTY.
        Assert.True(failures.All(x => x.StartsWith("sphere.3do:", StringComparison.Ordinal)), $"{ok} converted, {failures.Count} failed:{Environment.NewLine}" + string.Join(Environment.NewLine, failures.Take(30)));
        Assert.Equal(476, ok);
    }
}

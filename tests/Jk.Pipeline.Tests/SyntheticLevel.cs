using System.Globalization;
using System.Text;
using Jk.Core.Jkl;
using Jk.Pipeline;
using Kotor.Core.Diagnostics;

namespace Jk.Pipeline.Tests;

/// <summary>
/// Niveau synthetique : <c>N</c> secteurs cubiques de 1 x 1 x 0,5 alignes sur X (secteur i entre x = i et x = i + 1), chacun avec un sol
/// (materiau 0) et, aux deux bouts, un mur d'adjoin vers ses voisins (materiau <see cref="AdjoinMaterial"/>, -1 = portail invisible).
/// </summary>
internal sealed class SyntheticLevel
{
    public int Sectors { get; init; } = 2;
    public int AdjoinMaterial { get; init; } = -1;
    public uint AdjoinFlags { get; init; } = 0x7;
    public int FloorMaterial { get; init; } = 0;
    public int FloorGeo { get; init; } = 4;
    public float Intensity { get; init; } = 0.5f;
    public string ThingLine { get; init; } = "0: walkplayer walkplayer 0.5 0.25 0.12 0 90 0 0 thingflags=0x20000401";

    public const float Height = 0.5f;

    private static string F(float v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    public string ToJkl()
    {
        int n = Sectors;
        var sb = new StringBuilder();
        sb.AppendLine("SECTION: HEADER\nVersion 1\nWorld Gravity 4.0");
        sb.AppendLine("SECTION: MATERIALS\nWorld materials 3\n0: floor.mat 1.0 1.0\n1: wall.mat 1.0 1.0\n2: ghost.mat 1.0 1.0");
        sb.AppendLine("SECTION: GEORESOURCE\nWorld Colormaps 1\n0: test.cmp");
        sb.AppendLine($"World vertices {n * 8}");
        for (int i = 0; i < n; i++)
        {
            float x0 = i, x1 = i + 1;
            var corners = new (float X, float Y, float Z)[]
            {
                (x0, 0, 0), (x1, 0, 0), (x1, 1, 0), (x0, 1, 0), (x0, 0, Height), (x1, 0, Height), (x1, 1, Height), (x0, 1, Height),
            };
            for (int k = 0; k < 8; k++) sb.AppendLine($"{i * 8 + k}: {F(corners[k].X)} {F(corners[k].Y)} {F(corners[k].Z)}");
        }
        sb.AppendLine("World texture vertices 4\n0: 0 0\n1: 8 0\n2: 8 8\n3: 0 8");
        sb.AppendLine($"World adjoins {n * 2}");
        for (int i = 0; i < n; i++)
        {
            int right = 2 * i, left = 2 * i + 1;
            sb.AppendLine($"{right}: 0x{AdjoinFlags:x} {(i + 1 < n ? 2 * (i + 1) + 1 : -1)} 0");
            sb.AppendLine($"{left}: 0x{AdjoinFlags:x} {(i > 0 ? 2 * (i - 1) : -1)} 0");
        }

        // Surfaces : par secteur, sol puis mur droit puis mur gauche (selon les voisins). Les index de sommets sont ceux du secteur.
        var surfaces = new List<string>();
        var normals = new List<string>();
        var sectorFirst = new int[n];
        var sectorCount = new int[n];
        string Surf(int mat, int geo, int adjoin, int[] verts, string intens, string flags = "0x4") =>
            $"{surfaces.Count}: {mat} {flags} 0x0 {geo} 3 1 {adjoin} 0.0 {verts.Length} "
            + string.Join(' ', verts.Select((v, k) => $"{v},{k}")) + " " + intens;
        for (int i = 0; i < n; i++)
        {
            int b = i * 8;
            sectorFirst[i] = surfaces.Count;
            string I4 = string.Join(' ', Enumerable.Repeat(F(Intensity), 4));
            surfaces.Add(Surf(FloorMaterial, FloorGeo, -1, [b, b + 1, b + 2, b + 3], I4)); normals.Add("0 0 1");
            if (i + 1 < n)
            {
                surfaces.Add(Surf(AdjoinMaterial, AdjoinMaterial >= 0 ? 4 : 0, 2 * i, [b + 1, b + 5, b + 6, b + 2], I4, AdjoinMaterial >= 0 ? "0x4" : "0x0"));
                normals.Add("-1 0 0");
            }
            if (i > 0)
            {
                surfaces.Add(Surf(AdjoinMaterial, AdjoinMaterial >= 0 ? 4 : 0, 2 * i + 1, [b, b + 3, b + 7, b + 4], I4, AdjoinMaterial >= 0 ? "0x4" : "0x0"));
                normals.Add("1 0 0");
            }
            sectorCount[i] = surfaces.Count - sectorFirst[i];
        }
        sb.AppendLine($"World surfaces {surfaces.Count}");
        foreach (string s in surfaces) sb.AppendLine(s);
        sb.AppendLine("#--- Surface normals ---");
        for (int i = 0; i < normals.Count; i++) sb.AppendLine($"{i}: {normals[i]}");

        sb.AppendLine($"SECTION: SECTORS\nWorld sectors {n}");
        for (int i = 0; i < n; i++)
            sb.AppendLine($"SECTOR {i}\nFLAGS 0x0\nAMBIENT LIGHT 0.1\nEXTRA LIGHT 0.0\nCOLORMAP 0\nTINT 0 0 0\nSURFACES {sectorFirst[i]} {sectorCount[i]}");

        sb.AppendLine("SECTION: TEMPLATES\nWorld templates 2\n_actor none type=actor\nwalkplayer _actor type=player");
        sb.AppendLine("SECTION: THINGS\nWorld things 1");
        sb.AppendLine(ThingLine);
        return sb.ToString();
    }

    public JklLevel Read() => JklReader.Read(ToJkl());

    /// <summary>Fichiers du jeu simules : deux materiaux 8 x 8 (l'index de palette = leur numero), une palette. <paramref name="missing"/> : materiaux a retirer.</summary>
    public static Func<string, byte[]?> Files(params string[] missing)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [@"mat\floor.mat"] = TexMat(8, 8, 10),
            [@"mat\wall.mat"] = TexMat(16, 8, 20),
            [@"mat\ghost.mat"] = TexMat(8, 8, 30),
            [@"misc\cmp\test.cmp"] = Cmp(),
        };
        foreach (string m in missing) files.Remove(@"mat\" + m);
        return p => files.TryGetValue(p, out byte[]? d) ? d : null;
    }

    public static byte[] Cmp()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("CMP "u8.ToArray());
        w.Write(0x1E);
        w.Write(0);
        w.Write(new byte[52]);
        for (int i = 0; i < 256; i++) { w.Write((byte)i); w.Write((byte)(255 - i)); w.Write((byte)128); }
        for (int level = 0; level < 64; level++)
            for (int i = 0; i < 256; i++) w.Write((byte)(i * (63 - level) / 63));
        return ms.ToArray();
    }

    /// <summary>MAT 8 bits indexes, une cellule, un seul niveau de mipmap, tous les pixels = <paramref name="index"/>.</summary>
    public static byte[] TexMat(int width, int height, byte index)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("MAT "u8.ToArray());
        w.Write(0x32);
        w.Write(2); // type texture
        w.Write(1); // cellules
        w.Write(1); // blocs
        w.Write(0); // mode indexe
        w.Write(8); // bits par pixel
        for (int i = 0; i < 12; i++) w.Write(0);
        w.Write(new byte[40]); // en-tete de cellule
        w.Write(width); w.Write(height); w.Write(0); w.Write(0); w.Write(0); w.Write(1);
        w.Write(Enumerable.Repeat(index, width * height).ToArray());
        return ms.ToArray();
    }

    public static JkPreparedScene Build(SyntheticLevel level, ImportLog log, JkSceneOptions? options = null, params string[] missingMaterials)
    {
        JklLevel jkl = level.Read();
        return JkSceneBuilder.Build(jkl, JkTemplates.FromLevels(null, jkl), Files(missingMaterials), "synthetic", log, options);
    }
}

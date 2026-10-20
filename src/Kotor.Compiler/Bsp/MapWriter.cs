// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Ecrit le <c>.map</c> source (format Quake 3 / Radiant) de la carte compilee : entites, brushes de collision (monde) et brushes
/// des sous-modeles (portes). Le <c>.map</c> de collision est range dans le pk3 a cote du <c>.bsp</c> ; le <c>.map</c> d'edition y ajoute
/// la geometrie visible texturee (<see cref="VisualMapWriter"/>) et reste dans le dossier de travail.
/// </summary>
public static class MapWriter
{
    public const string CollisionShader = "textures/common/caulk";

    private const float HalfSize = 64f;

    public static string Write(IReadOnlyList<IReadOnlyDictionary<string, string>> entities, IReadOnlyList<Brush> worldBrushes,
        IReadOnlyList<BspSubModel> subModels)
    {
        var sw = new StringWriter { NewLine = "\n" };
        Write(sw, entities, worldBrushes, subModels, null);
        return sw.ToString();
    }

    /// <summary>
    /// Ecrit le <c>.map</c> sur <paramref name="w"/>. Avec <paramref name="visual"/> non nul, ajoute en fin de fichier l'entite
    /// <c>func_group</c> de la geometrie visible texturee (voir <see cref="VisualMapWriter"/>) ; sans, la sortie est identique octet pour octet
    /// a celle de la version qui renvoie une chaine.
    /// </summary>
    public static VisualMapStats? Write(TextWriter w, IReadOnlyList<IReadOnlyDictionary<string, string>> entities, IReadOnlyList<Brush> worldBrushes,
        IReadOnlyList<BspSubModel> subModels, IReadOnlyList<SurfaceInput>? visual, int maxVisualBrushes = VisualMapWriter.DefaultMaxBrushes)
    {
        // .map d'edition (geometrie visible demandee) : les editeurs et q3map2 comptent les noms de texture a partir de « textures/ »
        // (ils ajoutent eux-memes le prefixe), donc « common/caulk ». Le .map du pk3 garde ses noms complets, octet pour octet.
        string Sh(string shader) => visual is null ? shader : VisualMapWriter.EditorName(shader);
        var sb = new StringBuilder();
        bool worldDone = false;
        foreach (var e in entities)
        {
            bool isWorld = e.TryGetValue("classname", out string? cls) && cls == "worldspawn";
            sb.Append("{\n");
            foreach (var kv in e)
            {
                // "model" "*N" designe un sous-modele du BSP : dans le .map ce sont les brushes de l'entite.
                if (kv.Key == "model" && kv.Value.StartsWith('*')) continue;
                sb.Append('"').Append(kv.Key).Append("\" \"").Append(kv.Value).Append("\"\n");
            }
            if (isWorld && !worldDone)
            {
                worldDone = true;
                AppendBrushes(sb, worldBrushes, Sh(CollisionShader));
            }
            else if (e.TryGetValue("model", out string? model) && model.StartsWith('*')
                && int.TryParse(model.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int idx)
                && idx >= 1 && idx <= subModels.Count)
            {
                AppendBrushes(sb, subModels[idx - 1].Brushes, Sh(string.IsNullOrWhiteSpace(subModels[idx - 1].Shader) ? CollisionShader : subModels[idx - 1].Shader));
            }
            sb.Append("}\n");
        }
        if (!worldDone)
        {
            var wb = new StringBuilder("{\n\"classname\" \"worldspawn\"\n");
            AppendBrushes(wb, worldBrushes, Sh(CollisionShader));
            wb.Append("}\n");
            sb.Insert(0, wb.ToString());
        }
        w.Write(sb.ToString());
        return visual is null ? null : VisualMapWriter.Write(w, visual, maxVisualBrushes);
    }

    private static void AppendBrushes(StringBuilder sb, IReadOnlyList<Brush> brushes, string shader)
    {
        for (int i = 0; i < brushes.Count; i++)
        {
            sb.Append("// brush ").Append(i.ToString(CultureInfo.InvariantCulture)).Append("\n{\n");
            foreach (BspPlane p in brushes[i].Planes) AppendFace(sb, p, shader);
            sb.Append("}\n");
        }
    }

    // Convention Q3 : normale = (p0 - p1) x (p2 - p1), vers l'exterieur du brush.
    private static void AppendFace(StringBuilder sb, BspPlane plane, string shader)
    {
        Vector3 n = Vector3.Normalize(plane.Normal);
        Vector3 u = MathF.Abs(n.Z) < 0.9f ? Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n)) : Vector3.Normalize(Vector3.Cross(Vector3.UnitX, n));
        Vector3 v = Vector3.Cross(n, u); // u x v = n
        Vector3 c = n * plane.Dist;
        Point(sb, c + u * HalfSize); Point(sb, c); Point(sb, c + v * HalfSize);
        sb.Append(' ').Append(shader).Append(" 0 0 0 0.5 0.5 0 0 0\n");
    }

    private static void Point(StringBuilder sb, Vector3 p) =>
        sb.Append("( ").Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append(" ) ");

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

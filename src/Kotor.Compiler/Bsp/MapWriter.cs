// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Ecrit l'unique <c>.map</c> (format Quake 3 / Radiant) de la carte : les entites, les brushes texturés du worldspawn
/// (<see cref="VisualMapWriter"/> : un prisme fin par triangle de rendu, a la fois ce qu'on voit et ce qui est solide, comme dans une carte
/// Jedi Academy ; aucune couche de collision a part) et les brushes des sous-modeles (portes).
/// </summary>
public static class MapWriter
{
    public const string CollisionShader = "textures/common/caulk";

    private const float HalfSize = 64f;

    /// <summary>Le <c>.map</c> sur <paramref name="w"/> ; renvoie le bilan de la geometrie ecrite dans le worldspawn.</summary>
    public static VisualMapStats Write(TextWriter w, IReadOnlyList<IReadOnlyDictionary<string, string>> entities, IReadOnlyList<BspSubModel> subModels,
        IReadOnlyList<SurfaceInput> surfaces, int maxBrushes = VisualMapWriter.DefaultMaxBrushes)
    {
        ArgumentNullException.ThrowIfNull(w);
        // Les editeurs et q3map2 comptent les noms de texture a partir de « textures/ » (ils ajoutent eux-memes le prefixe).
        static string Sh(string shader) => VisualMapWriter.EditorName(shader);
        VisualMapStats? stats = null;
        VisualMapStats World()
        {
            return stats = VisualMapWriter.Write(w, surfaces, maxBrushes);
        }

        if (!entities.Any(e => e.TryGetValue("classname", out string? c) && c == "worldspawn"))
        {
            w.Write("{\n\"classname\" \"worldspawn\"\n");
            World();
            w.Write("}\n");
        }
        foreach (var e in entities)
        {
            bool isWorld = stats is null && e.TryGetValue("classname", out string? cls) && cls == "worldspawn";
            var sb = new StringBuilder("{\n");
            foreach (var kv in e)
            {
                // "model" "*N" designe un sous-modele du BSP : dans le .map ce sont les brushes de l'entite.
                if (kv.Key == "model" && kv.Value.StartsWith('*')) continue;
                sb.Append('"').Append(kv.Key).Append("\" \"").Append(kv.Value).Append("\"\n");
            }
            w.Write(sb.ToString());
            if (isWorld) World();
            else if (e.TryGetValue("model", out string? model) && model.StartsWith('*')
                && int.TryParse(model.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int idx)
                && idx >= 1 && idx <= subModels.Count)
            {
                var brushes = new StringBuilder();
                AppendBrushes(brushes, subModels[idx - 1].Brushes, Sh(string.IsNullOrWhiteSpace(subModels[idx - 1].Shader) ? CollisionShader : subModels[idx - 1].Shader));
                w.Write(brushes.ToString());
            }
            w.Write("}\n");
        }
        return stats!;
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
        sb.Append(shader).Append(" 0 0 0 0.5 0.5 0 0 0\n");
    }

    private static void Point(StringBuilder sb, Vector3 p) =>
        sb.Append("( ").Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append(" ) ");

    private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}

// Inspiré de la démarche de q3map2 / NetRadiant-custom (chaîne .map vers .bsp, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler;

public sealed record CompileResult(bool Ok, IReadOnlyList<string> Warnings, string? Error);

/// <summary>Point d'entrée : <c>.map</c> (entités + <c>misc_model</c>) vers <c>.bsp</c> RBSP.</summary>
public static class Compiler
{
    /// <summary>Ciel par defaut si la cle <c>_tfu_sky</c> du worldspawn manque (meme valeur que <see cref="Kotor.Core.Export.JaDefaults.SkyShader"/>).</summary>
    public const string DefaultSkyShader = Kotor.Core.Export.JaDefaults.SkyShader;

    private static readonly Regex KeyValue = new("^\"([^\"]*)\"\\s+\"([^\"]*)\"", RegexOptions.Compiled);

    /// <summary>
    /// Compile <paramref name="mapPath"/> en <paramref name="bspPath"/>. Les chemins <c>model</c> et le fichier
    /// <c>models/&lt;Niveau&gt;/&lt;Niveau&gt;_collision.obj</c> sont résolus sous <paramref name="assetRoot"/>
    /// (par défaut <c>..\tfu2ja</c> à côté du dossier de la carte). Le journal <c>&lt;Niveau&gt;.journal.txt</c> y est complété.
    /// En cas d'erreur (dont lump plein) aucun <c>.bsp</c> n'est écrit.
    /// </summary>
    public static CompileResult Compile(string mapPath, string bspPath, Limits limits, string? assetRoot = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(mapPath);
        ArgumentException.ThrowIfNullOrEmpty(bspPath);
        ArgumentNullException.ThrowIfNull(limits);

        var warnings = new List<string>();
        string level = Path.GetFileNameWithoutExtension(mapPath);
        string root = assetRoot ?? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapPath))!, "..", "tfu2ja"));
        string? error = null;
        try
        {
            if (!File.Exists(mapPath)) throw new FileNotFoundException($"Carte introuvable : {mapPath}");
            var entities = ParseEntities(File.ReadAllText(mapPath));

            // Collision -> brushes -> arbre.
            string collisionObj = Path.Combine(root, "models", level, level + "_collision.obj");
            IReadOnlyList<Brush> brushes;
            if (File.Exists(collisionObj))
                brushes = BrushBuilder.FromCollision(ReadCollision(collisionObj), log: warnings.Add);
            else
            {
                warnings.Add($"Avertissement : pas de maillage de collision ({collisionObj}), aucun brush genere.");
                brushes = [];
            }

            // Collision du terrain : triangles marchables du maillage visible (ecrits par l'export).
            string walkableObj = Path.Combine(root, "models", level, level + "_walkable.obj");
            if (File.Exists(walkableObj))
            {
                int cap = WalkableCollision.MaxBrushesFor(limits, brushes.Count);
                var wr = WalkableCollision.Build(ReadCollision(walkableObj), cap, log: warnings.Add);
                if (wr.LimitReached) warnings.Add($"Avertissement : limite de brushes atteinte pour le terrain ({cap}).");
                brushes = brushes.Concat(wr.Brushes).ToList();
            }
            else
                warnings.Add("Terrain : 0 triangles marchables, 0 brush de collision ajoute.");
            BspTree tree = BspTree.Build(brushes, limits);

            // Shaders a double face (cull disable) : leur eclairage utilise abs(n.l).
            var doubleSided = ReadDoubleSidedShaders(Path.Combine(root, "shaders", level + ".shader"));
            var worldspawn = entities.FirstOrDefault(e => e.GetValueOrDefault("classname") == "worldspawn");
            var lighting = VertexLighting.FromWorldspawn(worldspawn);

            // Surfaces de rendu : un OBJ par misc_model.
            var surfaces = new List<RenderSurface>();
            foreach (var ent in entities.Where(e => e.GetValueOrDefault("classname") == "misc_model"))
            {
                string model = ent.GetValueOrDefault("model") ?? "";
                string objPath = Path.GetFullPath(Path.Combine(root, model.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(objPath))
                {
                    warnings.Add($"Avertissement : OBJ du misc_model introuvable ({model}), modele ignore.");
                    continue;
                }
                surfaces.AddRange(RenderSurfaces.FromObj(objPath, ParseOrigin(ent.GetValueOrDefault("origin")), tree)
                    .Select(s => doubleSided.Contains(s.Shader) ? s with { DoubleSided = true } : s));
            }

            // Boite de ciel : 6 faces autour de toute la geometrie (shader de ciel d'origine, nom lu dans _tfu_sky).
            string sky = entities.FirstOrDefault(e => e.GetValueOrDefault("classname") == "worldspawn")?.GetValueOrDefault("_tfu_sky") ?? "";
            if (string.IsNullOrWhiteSpace(sky)) sky = DefaultSkyShader;
            if (SkyBox.TryComputeBounds(tree, surfaces, out Vector3 skyMin, out Vector3 skyMax))
                surfaces.AddRange(SkyBox.Build(tree, sky.Trim(), skyMin, skyMax));
            else
                warnings.Add("Avertissement : aucune geometrie, pas de boite de ciel.");

            // Tout est construit en memoire : en cas de depassement, aucun fichier n'est ecrit.
            using var ms = new MemoryStream();
            BspWriter.Write(tree, ms, BuildEntityLump(entities), limits, surfaces, lighting, GlobalFog.FromWorldspawn(worldspawn, level));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bspPath))!);
            File.WriteAllBytes(bspPath, ms.ToArray());
        }
        catch (BspLimitException ex) { error = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            error = $"Echec de compilation de {level} : {ex.Message}";
        }

        WriteJournal(root, level, warnings, error);
        return new CompileResult(error is null, warnings, error);
    }

    /// <summary>Noms des shaders contenant <c>cull disable</c> dans le fichier shader du niveau (vide si absent).</summary>
    public static HashSet<string> ReadDoubleSidedShaders(string shaderPath)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(shaderPath)) return set;
        string? name = null; int depth = 0;
        foreach (string raw in File.ReadLines(shaderPath))
        {
            string line = raw.Trim();
            if (line == "{") depth++;
            else if (line == "}") { if (depth > 0) depth--; if (depth == 0) name = null; }
            else if (depth == 0 && line.Length > 0 && !line.StartsWith("//")) name = line;
            else if (depth == 1 && name is not null && line.StartsWith("cull", StringComparison.OrdinalIgnoreCase)
                     && line.Contains("disable", StringComparison.OrdinalIgnoreCase)) set.Add(name);
        }
        return set;
    }

    private static void WriteJournal(string root, string level, List<string> warnings, string? error)
    {
        try
        {
            Directory.CreateDirectory(root);
            var lines = new List<string> { $"[compilateur] {level}" };
            lines.AddRange(warnings);
            if (error is not null) lines.Add("ERREUR : " + error);
            if (warnings.Count == 0 && error is null) lines.Add("Aucun avertissement.");
            File.AppendAllLines(Path.Combine(root, level + ".journal.txt"), lines, new UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Avertissement : journal non ecrit : " + ex.Message);
        }
    }

    /// <summary>Lit un OBJ de collision (positions et faces) en <see cref="ModelMesh"/>, positions déjà en unités Quake.</summary>
    private static ModelMesh ReadCollision(string objPath)
    {
        var pos = new List<Vector3>();
        var tris = new List<int>();
        foreach (string raw in File.ReadLines(objPath))
        {
            string[] t = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length >= 4 && t[0] == "v")
                pos.Add(new Vector3(F(t[1]), F(t[2]), F(t[3])));
            else if (t.Length >= 4 && t[0] == "f")
            {
                int[] ix = t.Skip(1).Select(c =>
                {
                    int i = int.Parse(c.Split('/')[0], CultureInfo.InvariantCulture);
                    return i > 0 ? i - 1 : pos.Count + i;
                }).ToArray();
                if (ix.Any(i => (uint)i >= (uint)pos.Count)) continue;
                for (int i = 1; i + 1 < ix.Length; i++) { tris.Add(ix[0]); tris.Add(ix[i]); tris.Add(ix[i + 1]); }
            }
        }
        return new ModelMesh
        {
            Name = Path.GetFileNameWithoutExtension(objPath),
            SubMeshes = [new SubMesh { MaterialName = "collision", Positions = pos.ToArray(), Triangles = tris.ToArray() }],
        };
    }

    /// <summary>Entités du .map (paires clé/valeur ; les brushes éventuels sont ignorés).</summary>
    private static List<Dictionary<string, string>> ParseEntities(string text)
    {
        var list = new List<Dictionary<string, string>>();
        Dictionary<string, string>? cur = null;
        int brushDepth = 0;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line == "{")
            {
                if (cur is null) cur = new(StringComparer.OrdinalIgnoreCase); else brushDepth++;
            }
            else if (line == "}")
            {
                if (brushDepth > 0) brushDepth--;
                else if (cur is not null) { list.Add(cur); cur = null; }
            }
            else if (cur is not null && brushDepth == 0 && KeyValue.Match(line) is { Success: true } m)
                cur[m.Groups[1].Value] = m.Groups[2].Value;
        }
        return list;
    }

    /// <summary>Lump entités : tout sauf les misc_model (remplacés par des surfaces). Au moins un worldspawn.</summary>
    private static string BuildEntityLump(List<Dictionary<string, string>> entities)
    {
        var sb = new StringBuilder();
        bool world = false;
        foreach (var e in entities)
        {
            string cls = e.GetValueOrDefault("classname") ?? "";
            if (cls == "misc_model") continue;
            world |= cls == "worldspawn";
            sb.Append("{\n");
            foreach (var kv in e) sb.Append('"').Append(kv.Key).Append("\" \"").Append(kv.Value).Append("\"\n");
            sb.Append("}\n");
        }
        return world ? sb.ToString() : "{\n\"classname\" \"worldspawn\"\n}\n" + sb;
    }

    private static Vector3 ParseOrigin(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Vector3.Zero;
        string[] p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length >= 3 ? new Vector3(F(p[0]), F(p[1]), F(p[2])) : Vector3.Zero;
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

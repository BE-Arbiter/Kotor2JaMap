// Inspiré de la démarche de q3map2 / NetRadiant-custom (chaîne scène vers .bsp, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using System.Text;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;

namespace Kotor.Compiler;

public sealed record CompileResult(bool Ok, IReadOnlyList<string> Warnings, string? Error);

/// <summary>Point d'entrée : scène en mémoire (unités JA) vers <c>.bsp</c> RBSP.</summary>
public static class Compiler
{
    /// <summary>Ciel par defaut si <see cref="CompileInput.SkyShader"/> et la cle <c>_kotor_sky</c> du worldspawn manquent.</summary>
    public const string DefaultSkyShader = Kotor.Core.Export.JaDefaults.SkyShader;

    /// <summary>
    /// Compile <paramref name="input"/> en <paramref name="bspPath"/>. Les avertissements sont renvoyés dans le résultat et
    /// inscrits dans <paramref name="log"/> (code <c>BSP_WARNING</c>). En cas d'erreur (dont lump plein) aucun <c>.bsp</c> n'est écrit.
    /// </summary>
    public static CompileResult Compile(CompileInput input, string bspPath, Limits limits, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrEmpty(bspPath);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(log);

        var warnings = new List<string>();
        void Warn(string message) { warnings.Add(message); log.Warn("BSP_WARNING", message); }
        string? error = null;
        try
        {
            var world = input.Entities.Count > 0 ? input.Entities[0] : null;

            // Collision solide : triangles fusionnes en polygones convexes -> brushes.
            var polygons = CollisionMerger.MergeCoplanar(input.Solid);
            if (polygons.Count == 0) Warn("Avertissement : pas de maillage de collision, aucun brush genere.");
            polygons = CapPolygons(polygons, limits, log, warnings, out int sidesUsed);
            IReadOnlyList<Brush> brushes = BrushBuilder.FromPolygons(polygons, log: polygons.Count == 0 ? null : Warn);

            // Collision du terrain : triangles marchables du maillage visible. Les brushes solides sont comptes comme
            // des brushes de 11 cotes (hypothese de MaxBrushesFor), le plafond de cotes de brush reste ainsi respecte.
            int equivalent = (sidesUsed + 10) / 11;
            int cap = WalkableCollision.MaxBrushesFor(limits, Math.Max(equivalent, brushes.Count), margin: 0);
            var wr = WalkableCollision.Build(input.Walkable, cap, log: Warn);
            if (wr.LimitReached) Warn($"Avertissement : limite de brushes atteinte pour le terrain ({cap}).");
            brushes = brushes.Concat(wr.Brushes).ToList();
            BspTree tree = BspTree.Build(brushes, limits);

            // Surfaces de rendu.
            var surfaces = new List<RenderSurface>();
            foreach (SurfaceInput s in input.Surfaces) surfaces.AddRange(RenderSurfaces.FromInput(s, tree));

            // Boite de ciel : 6 faces autour de toute la geometrie.
            string sky = input.SkyShader;
            if (string.IsNullOrWhiteSpace(sky) && world is not null && world.TryGetValue(CompilerNames.SkyKey, out string? key)) sky = key;
            if (string.IsNullOrWhiteSpace(sky)) sky = DefaultSkyShader;
            if (SkyBox.TryComputeBounds(tree, surfaces, out Vector3 skyMin, out Vector3 skyMax))
                surfaces.AddRange(SkyBox.Build(tree, sky.Trim(), skyMin, skyMax));
            else
                Warn("Avertissement : aucune geometrie, pas de boite de ciel.");

            // Tout est construit en memoire : en cas de depassement, aucun fichier n'est ecrit.
            using var ms = new MemoryStream();
            BspWriter.Write(tree, ms, BuildEntityLump(input.Entities), limits, surfaces, input.Lighting, GlobalFog.FromWorldspawn(world, input.MapName));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bspPath))!);
            File.WriteAllBytes(bspPath, ms.ToArray());
        }
        catch (BspLimitException ex) { error = ex.Message; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            error = $"Echec de compilation de {input.MapName} : {ex.Message}";
        }

        if (error is not null) log.Warn("BSP_ERROR", error);
        return new CompileResult(error is null, warnings, error);
    }

    /// <summary>
    /// Plafonne la collision solide : au-delà de <c>MaxBrushes - 50</c> brushes (ou du budget de côtés), les polygones de plus
    /// petite aire sont retirés d'abord. Journalise <c>COLLISION_CAPPED</c> ; ne lève jamais.
    /// </summary>
    private static IReadOnlyList<Vector3[]> CapPolygons(IReadOnlyList<Vector3[]> polygons, Limits limits, ImportLog log,
        List<string> warnings, out int sidesUsed)
    {
        int maxCount = WalkableCollision.MaxBrushesFor(limits, 0);
        int maxSides = Limits.MaxBrushSides - 50 * 11;
        int Sides(Vector3[] p) => 6 + 2 + p.Length; // 6 plans axiaux ecrits par BspWriter + 2 faces + 1 par arete

        var keep = new HashSet<int>();
        int sides = 0;
        foreach (int i in Enumerable.Range(0, polygons.Count).OrderByDescending(i => Area(polygons[i])).ThenBy(i => i))
        {
            int s = Sides(polygons[i]);
            if (keep.Count >= maxCount || sides + s > maxSides) continue;
            keep.Add(i);
            sides += s;
        }
        sidesUsed = sides;
        int removed = polygons.Count - keep.Count;
        if (removed == 0) return polygons;

        string msg = $"Collision plafonnee : {removed} polygone(s) de plus petite aire retire(s) sur {polygons.Count} (limite {maxCount} brushes).";
        warnings.Add(msg);
        log.Warn("COLLISION_CAPPED", msg);
        return polygons.Where((_, i) => keep.Contains(i)).ToList();
    }

    private static float Area(Vector3[] p)
    {
        Vector3 n = Vector3.Zero;
        for (int i = 0; i < p.Length; i++) n += Vector3.Cross(p[i], p[(i + 1) % p.Length]);
        return n.Length() * 0.5f;
    }

    /// <summary>Lump entités : les entités fournies telles quelles. Au moins un worldspawn.</summary>
    private static string BuildEntityLump(IReadOnlyList<IReadOnlyDictionary<string, string>> entities)
    {
        var sb = new StringBuilder();
        bool world = false;
        foreach (var e in entities)
        {
            world |= e.TryGetValue("classname", out string? cls) && cls == "worldspawn";
            sb.Append("{\n");
            foreach (var kv in e) sb.Append('"').Append(kv.Key).Append("\" \"").Append(kv.Value).Append("\"\n");
            sb.Append("}\n");
        }
        return world ? sb.ToString() : "{\n\"classname\" \"worldspawn\"\n}\n" + sb;
    }
}

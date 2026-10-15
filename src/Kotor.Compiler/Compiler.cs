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

    /// <summary>Nombre maximal de reconstructions (reduction de 20 % de la collision a chaque depassement de limite BSP).</summary>
    private const int MaxShrinkAttempts = 80;

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

            // Budget de collision : brushes, plans et cotes de brush (BspWriter ecrit 6 plans axiaux + les plans du brush).
            // Le sol marchable est prioritaire : son budget est reserve en premier, le solide recoit le reste.
            int brushMargin = Math.Min(50, limits.MaxBrushes / 10);
            int planeMargin = Math.Min(50 * 11, limits.MaxPlanes / 10);
            int budgetBrushes = Math.Max(0, limits.MaxBrushes - brushMargin);
            int budgetSides = Math.Max(0, limits.MaxBrushSides - 50 * 11);
            int budgetPlanes = Math.Max(0, limits.MaxPlanes - planeMargin);

            // Collision du terrain : triangles marchables du maillage visible (brush prismatique : au plus 11 cotes et 11 plans).
            int walkCap = Math.Min(budgetBrushes, Math.Min(budgetSides / 11, budgetPlanes / 11));
            var wr = WalkableCollision.Build(input.Walkable, walkCap, log: Warn);
            if (wr.LimitReached)
            {
                string wmsg = $"Collision du terrain plafonnee : brushes marchables tronques a {walkCap} (limites brushes/plans/cotes).";
                warnings.Add(wmsg);
                log.Warn("WALKABLE_CAPPED", wmsg);
            }
            int usedBrushes = wr.Brushes.Count;
            int usedSides = wr.Brushes.Sum(b => 6 + b.Planes.Count);
            int usedPlanes = usedSides;

            // Collision solide : triangles fusionnes en polygones convexes -> brushes, avec ce qui reste du budget.
            var polygons = CollisionMerger.MergeCoplanar(input.Solid, log: log);
            if (polygons.Count == 0) Warn("Avertissement : pas de maillage de collision, aucun brush genere.");
            polygons = CapPolygons(polygons, Math.Max(0, budgetBrushes - usedBrushes), Math.Max(0, budgetSides - usedSides),
                Math.Max(0, budgetPlanes - usedPlanes), log, warnings);
            // Les plans de separation de l'arbre (un par noeud interne), les noeuds et les feuilles ne sont connus qu'apres
            // construction : on construit tout en memoire et, si une limite BSP est depassee, on reduit la collision
            // (plus petites aires d'abord, sol marchable prioritaire) puis on reconstruit, jusqu'a tenir dans les limites.
            polygons = polygons.OrderByDescending(Area).ToList();
            var walkBrushes = wr.Brushes;
            int solidKeep = polygons.Count, walkKeep = walkBrushes.Count;
            int solidInitial = solidKeep, walkInitial = walkKeep;
            string entityLump = BuildEntityLump(input.Entities);
            // Sous-modeles (portes) : bornes calculees depuis les plans ; un brush degenere est ignore avec un avertissement.
            var subModels = new List<BspSubModel>();
            for (int sm = 0; sm < input.SubModels.Count; sm++)
            {
                var brushesOfModel = new List<Brush>();
                foreach (var planes in input.SubModels[sm].Brushes)
                {
                    if (BrushBounds.FromPlanes(planes) is { } bb) brushesOfModel.Add(new Brush([.. planes], bb.Min, bb.Max));
                    else Warn($"Sub-model *{sm + 1}: degenerate or unbounded brush ignored.");
                }
                subModels.Add(new BspSubModel(brushesOfModel, input.SubModels[sm].Shader));
            }
            using var ms = new MemoryStream();
            ImportLog iterLog;
            List<string> iterWarnings;
            for (int attempt = 0; ; attempt++)
            {
                iterLog = new ImportLog();
                iterWarnings = [];
                void IterWarn(string message) { iterWarnings.Add(message); iterLog.Warn("BSP_WARNING", message); }
                try
                {
                    var solidPolys = polygons.Take(solidKeep).ToList();
                    IReadOnlyList<Brush> brushes = BrushBuilder.FromPolygons(solidPolys, log: solidPolys.Count == 0 ? null : IterWarn);
                    brushes = brushes.Concat(walkBrushes.Take(walkKeep)).ToList();
                    BspTree tree = BspTree.Build(brushes, limits);

                    // Surfaces de rendu.
                    var surfaces = new List<RenderSurface>();
                    surfaces.AddRange(RenderSurfaces.FromInputs(input.Surfaces, tree, iterLog));

                    // Boite de ciel : 6 faces autour de toute la geometrie.
                    string sky = input.SkyShader;
                    if (string.IsNullOrWhiteSpace(sky) && world is not null && world.TryGetValue(CompilerNames.SkyKey, out string? key)) sky = key;
                    if (string.IsNullOrWhiteSpace(sky)) sky = DefaultSkyShader;
                    if (SkyBox.TryComputeBounds(tree, surfaces, out Vector3 skyMin, out Vector3 skyMax))
                        surfaces.AddRange(SkyBox.Build(tree, sky.Trim(), skyMin, skyMax));
                    else
                        IterWarn("Avertissement : aucune geometrie, pas de boite de ciel.");

                    // Tout est construit en memoire : en cas de depassement, aucun fichier n'est ecrit.
                    ms.SetLength(0);
                    BspWriter.Write(tree, ms, entityLump, limits, surfaces, EffectiveLighting(input), GlobalFog.FromWorldspawn(world, input.MapName), subModels);
                    break;
                }
                catch (BspLimitException bx) when (bx.CollisionRelated && attempt < MaxShrinkAttempts && (solidKeep > 0 || walkKeep > 0))
                {
                    if (solidKeep > 0) solidKeep = solidKeep * 4 / 5;
                    else walkKeep = walkKeep * 4 / 5;
                }
            }
            foreach (string w in iterWarnings) warnings.Add(w);
            foreach (var (code, message) in iterLog.Warnings) log.Warn(code, message);

            if (solidKeep < solidInitial)
            {
                string msg = $"Collision plafonnee : {solidInitial - solidKeep} polygone(s) de plus petite aire retire(s) pour tenir dans les limites BSP ({solidKeep} conserve(s) sur {solidInitial}).";
                warnings.Add(msg);
                log.Warn("COLLISION_CAPPED", msg);
            }
            if (walkKeep < walkInitial)
            {
                string msg = $"Collision du terrain plafonnee : {walkKeep} brush(es) marchable(s) conserve(s) sur {walkInitial} (limites BSP).";
                warnings.Add(msg);
                log.Warn("WALKABLE_CAPPED", msg);
            }
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

    /// <summary>With point lights the lighting is the KOTOR mode (ambient of <c>Lighting</c> plus the lights, no sun); otherwise unchanged.</summary>
    public static VertexLighting EffectiveLighting(CompileInput input) =>
        input.Lights is { Count: > 0 } l ? VertexLighting.ForKotor(input.Lighting.Ambient, l, input.Lighting.OverbrightShift) : input.Lighting;

    /// <summary>
    /// Plafonne la collision solide selon le budget restant (brushes, cotes de brush, plans) : les polygones de plus petite
    /// aire sont retires d'abord. Chaque polygone coute 1 brush, <c>8 + n</c> cotes et au pire <c>8 + n</c> plans (6 plans axiaux
    /// ecrits par BspWriter + 2 faces + 1 par arete). Journalise <c>COLLISION_CAPPED</c> ; ne leve jamais.
    /// </summary>
    private static IReadOnlyList<Vector3[]> CapPolygons(IReadOnlyList<Vector3[]> polygons, int maxCount, int maxSides, int maxPlanes,
        ImportLog log, List<string> warnings)
    {
        int Cost(Vector3[] p) => 6 + 2 + p.Length;

        var keep = new HashSet<int>();
        int sides = 0, planes = 0;
        foreach (int i in Enumerable.Range(0, polygons.Count).OrderByDescending(i => Area(polygons[i])).ThenBy(i => i))
        {
            int c = Cost(polygons[i]);
            if (keep.Count >= maxCount || sides + c > maxSides || planes + c > maxPlanes) continue;
            keep.Add(i);
            sides += c;
            planes += c;
        }
        int removed = polygons.Count - keep.Count;
        if (removed == 0) return polygons;

        string msg = $"Collision plafonnee : {removed} polygone(s) de plus petite aire retire(s) sur {polygons.Count} (budget restant : {maxCount} brushes, {maxSides} cotes, {maxPlanes} plans).";
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

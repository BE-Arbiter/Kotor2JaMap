using System.Globalization;
using System.Numerics;
using System.Text;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Pipeline.Movers;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline;

/// <summary>
/// Sous-modeles BSP d'un niveau JK, numerotes a partir de 1 (<c>*1</c>...). Chaque convertisseur (objets, volumes, elements mobiles)
/// y ajoute les siens ; l'indice rendu est celui de la cle <c>model</c> de l'entite.
/// </summary>
public sealed class JkSubModels
{
    private readonly List<SubModelInput> _items = [];

    public IReadOnlyList<SubModelInput> Items => _items;

    /// <summary>Ajoute un sous-modele et rend son indice (<c>*indice</c>).</summary>
    public int Add(SubModelInput subModel)
    {
        _items.Add(subModel);
        return _items.Count;
    }
}

/// <summary>
/// Objets d'un niveau JK pour le pipeline : things (departs, objets a ramasser, decors, rotatifs, lumieres), volumes d'eau et de blessure,
/// sons de secteurs. Les elements mobiles (portes, ascenseurs, ponts : <see cref="MoverConverter"/>) numerotent leurs sous-modeles a la suite, par <see cref="JkSubModels"/>.
/// </summary>
public static class JkObjects
{
    /// <summary>Shader (nom seul, contenu <see cref="BspContents.Trigger"/>) des brushes des <c>trigger_hurt</c>.</summary>
    public static readonly string TriggerShader = ShaderNames.PrefixFor(KotorGame.JediKnight) + "trigger_hurt";

    /// <summary>Shader (contenu nul) des sous-modeles qui ne doivent rien bloquer : volume des rotatifs.</summary>
    public static readonly string NonSolidShader = ShaderNames.PrefixFor(KotorGame.JediKnight) + "mover_nonsolid";

    /// <summary>Plus grand indice de sous-modele (<c>*n</c>) accepte par le moteur (MAX_SUBMODELS = 512, le monde etant le modele 0).</summary>
    public const int MaxSubModels = 511;

    /// <summary>Haut-parleurs de secteur au plus par niveau (chaque entite compte dans la limite du moteur).</summary>
    public const int MaxSectorSpeakers = 96;

    /// <summary>Distance (unites JK) en dessous de laquelle deux secteurs voisins qui jouent le meme son partagent un haut-parleur.</summary>
    public const float SpeakerMergeDistance = 2f;

    /// <summary>Distance de fusion maximale (unites JK) quand un niveau depasse <see cref="MaxSectorSpeakers"/>.</summary>
    public const float MaxMergeDistance = 12f;

    /// <summary>
    /// Convertit les objets du niveau : entites JA, MD3, textures et shaders des modeles, sons, collision des decors et sous-modeles BSP.
    /// Rien n'est lu hors du systeme de fichiers <paramref name="fs"/> (GOB de l'episode, Res2, Res1hi).
    /// </summary>
    public static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        var subs = new JkSubModels();
        // Elements mobiles : classes d'abord (COG), leurs modeles charges dans le meme lot que les decors, puis convertis apres les volumes (sous-modeles a la suite).
        var pool = new JkModelPool(fs, prepared, opt, log);
        LevelMovers movers = ClassifyMovers(prepared, fs, log);
        var moverModels = new Dictionary<int, JkMd3>();
        foreach (int thing in movers.All.Where(b => b.Thing >= 0).Select(b => b.Thing).Distinct().Order())
            if (prepared.Level.Things.FirstOrDefault(t => t.Index == thing) is { } t
                && prepared.Templates.Resolve(t.Template).GetValueOrDefault("model3d") is { } model && !string.IsNullOrWhiteSpace(model) && !model.Equals("none", StringComparison.OrdinalIgnoreCase)
                && pool.Get(model, null) is { } md3)
                moverModels[thing] = md3;

        ConvertedEntities things = JkThingConverter.Convert(prepared, fs, opt, log, subs, movers.Movers, pool);
        ConvertedEntities volumes = ConvertVolumes(prepared, opt, log, subs);
        ConvertedEntities sounds = ConvertSectorSounds(prepared, fs, opt, log);
        var (mobile, mobileSubs) = MoverConverter.Convert(movers.All, prepared, moverModels, opt.Scale, subs.Items.Count + 1, log);
        foreach (SubModelInput sub in mobileSubs) subs.Add(sub);
        return (things.Merge(volumes).Merge(sounds).Merge(mobile), subs.Items);
    }

    /// <summary>Crochet <see cref="SceneObjects"/> d'une scene JK (voir <c>PipelineSteps.Import</c> et <c>KotorWorkflowSteps.UseScene</c>) ; <paramref name="fs"/> doit rester ouvert tant que l'etape Importer peut tourner.</summary>
    public static SceneObjects ForPipeline(JkPreparedScene prepared, JkFileSystem fs) => (scene, opt, log) => Convert(prepared, fs, opt, log);

    // ---------------------------------------------------------------- elements mobiles

    /// <summary>Classe les COG du niveau (lus dans les GOB) et journalise les scripts absents (<c>JK_COG_MISSING</c>) ou mal formes (<c>JK_COG_MALFORMED</c>).</summary>
    private static LevelMovers ClassifyMovers(JkPreparedScene prepared, JkFileSystem fs, ImportLog log)
    {
        LevelMovers movers = CogClassifier.ClassifyLevel(prepared.Level, file => fs.TryRead(@"cog\" + file) is { } raw ? Encoding.Latin1.GetString(raw) : null);
        if (movers.MissingScripts.Count > 0)
            log.Warn("JK_COG_MISSING", $"{movers.MissingScripts.Count} COG script(s) of this level are not in the game archives, so what they do is unknown: {string.Join(", ", movers.MissingScripts)}.");
        foreach (var (file, problems) in movers.MalformedScripts)
            log.Warn("JK_COG_MALFORMED", $"COG script {file} is malformed ({string.Join("; ", problems)}); what could be read was used.");
        return movers;
    }

    // ---------------------------------------------------------------- volumes

    private static ConvertedEntities ConvertVolumes(JkPreparedScene prepared, ExportOptions opt, ImportLog log, JkSubModels subs)
    {
        if (prepared.Volumes.Count == 0) return ConvertedEntities.Empty;
        float k = JkWorldScale.MetresPerJkUnit * opt.Scale;
        var entities = new List<IReadOnlyDictionary<string, string>>();
        var shaders = new List<string>();
        var water = new SortedDictionary<string, (Vector3 Color, List<IReadOnlyList<BspPlane>> Brushes)>(StringComparer.Ordinal);
        var hurt = new SortedDictionary<(int Kind, int Dmg), List<IReadOnlyList<BspPlane>>>();
        int waterPlanes = 0, waterBoxes = 0, hurtPlanes = 0, hurtBoxes = 0, lost = 0;

        foreach (JkVolume v in prepared.Volumes)
        {
            bool isWater = v.Kind == JkVolumeKind.Water;
            IReadOnlyList<BspPlane>? planes = v.Surface < 0 ? JkVolumeBrushes.SectorPlanes(prepared.Level, v.Sector, k) : null;
            if (planes is not null) { if (isWater) waterPlanes++; else hurtPlanes++; }
            else
            {
                planes = JkVolumeBrushes.Box(v.Min * k, v.Max * k);
                if (isWater) waterBoxes++; else hurtBoxes++;
            }
            if (BrushBounds.FromPlanes(planes) is null) { lost++; continue; }

            if (isWater)
            {
                string key = JkSurfaceEffects.WaterVolumeShaderName(v.Color);
                if (!water.TryGetValue(key, out var group)) water[key] = group = (v.Color, []);
                group.Brushes.Add(planes);
            }
            else
            {
                // Degats par seconde -> un coup de « dmg » par seconde ; la mort (>= 100 par application) est un coup enorme sans protection.
                int dmg = v.Kind == JkVolumeKind.Kill ? JkVolumeBrushes.KillDamage : Math.Max(1, (int)MathF.Round(v.Damage));
                var key = (v.Kind == JkVolumeKind.Kill ? 1 : 0, dmg);
                if (!hurt.TryGetValue(key, out var list)) hurt[key] = list = [];
                list.Add(planes);
            }
        }

        // Eau : un sous-modele par couleur de brouillard, contenu CONTENTS_WATER (le joueur nage ; SV_PointContents lit les brushes des entites).
        foreach (var (shader, group) in water)
        {
            int index = subs.Add(new SubModelInput(group.Brushes, shader) { Contents = BspContents.Water });
            entities.Add(new Dictionary<string, string> { ["classname"] = "func_static", ["model"] = $"*{index}", ["origin"] = "0 0 0" });
            shaders.Add(JkSurfaceEffects.WaterVolumeShader(group.Color));
        }

        // Blessure et mort : trigger_hurt, brushes CONTENTS_TRIGGER (jamais solides : ils ne bloquent pas le joueur).
        foreach (var ((kind, dmg), brushes) in hurt)
        {
            int index = subs.Add(new SubModelInput(brushes, TriggerShader) { Contents = BspContents.Trigger });
            var ent = new Dictionary<string, string>
            {
                ["classname"] = "trigger_hurt",
                ["model"] = $"*{index}",
                ["origin"] = "0 0 0",
                ["dmg"] = dmg.ToString(CultureInfo.InvariantCulture),
                ["wait"] = kind == 1 ? "0.5" : "1",
            };
            if (kind == 1) ent["spawnflags"] = "8"; // NO_PROTECTION
            entities.Add(ent);
        }

        int waterBrushes = water.Values.Sum(g => g.Brushes.Count), hurtBrushes = hurt.Values.Sum(l => l.Count);
        if (waterBrushes > 0)
            log.Warn("JK_WATER_BRUSH", $"{waterBrushes} water volume(s) in {water.Count} water entity(ies): the player swims in them (water contents). The underwater fog is not converted; "
                + $"{waterPlanes} follow their sector planes, {waterBoxes} use the sector bounding box (open or non-convex sector).");
        if (hurtBrushes > 0)
            log.Warn("JK_HURT_TRIGGER", $"{hurtBrushes} hurting or killing zone(s) in {hurt.Count} trigger_hurt entity(ies) (non-solid trigger brushes): "
                + $"{hurtPlanes} follow their sector planes, {hurtBoxes} use a bounding box (touched surface, open or non-convex sector).");
        if (lost > 0)
            log.Warn("JK_VOLUME_DEGENERATE", $"{lost} water or hurt volume(s) have a degenerate brush and are not placed.");
        return new ConvertedEntities(entities, [], []) { ModelShaders = shaders };
    }

    // ---------------------------------------------------------------- sons de secteurs

    private static ConvertedEntities ConvertSectorSounds(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log)
    {
        JklLevel level = prepared.Level;
        float k = JkWorldScale.MetresPerJkUnit * opt.Scale;
        var assets = new Dictionary<string, SoundAsset?>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Sound, SoundAsset Asset, Vector3 Center)>();
        int withSound = 0;

        for (int s = 0; s < level.Sectors.Count; s++)
        {
            JklSector sec = level.Sectors[s];
            if (string.IsNullOrWhiteSpace(sec.Sound) || !(sec.SoundVolume > 0f)) continue;
            if (!JkSurfaceEffects.SectorBounds(level, s, out Vector3 min, out Vector3 max)) continue;
            withSound++;
            string sound = sec.Sound.Trim();
            if (!assets.TryGetValue(sound, out SoundAsset? asset)) assets[sound] = asset = JkSounds.Load(fs, sound, log);
            if (asset is not null) candidates.Add((sound, asset, (min + max) * 0.5f));
        }
        if (withSound == 0) return ConvertedEntities.Empty;

        // Un haut-parleur par groupe de secteurs voisins qui jouent le meme son ; si le niveau en demande trop, la distance de fusion grandit.
        float radius = SpeakerMergeDistance;
        List<(string Sound, SoundAsset Asset, Vector3 Center)> placed;
        while (true)
        {
            placed = [];
            foreach (var c in candidates)
                if (!placed.Any(p => string.Equals(p.Sound, c.Sound, StringComparison.OrdinalIgnoreCase) && Vector3.Distance(p.Center, c.Center) <= radius)) placed.Add(c);
            if (placed.Count <= MaxSectorSpeakers || radius >= MaxMergeDistance) break;
            radius *= 1.5f;
        }
        int skipped = Math.Max(0, placed.Count - MaxSectorSpeakers);
        placed = [.. placed.Take(MaxSectorSpeakers)];

        var entities = new List<IReadOnlyDictionary<string, string>>();
        foreach (var p in placed)
            entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "target_speaker",
                ["noise"] = p.Asset.Path,
                ["origin"] = $"{Num(p.Center.X * k)} {Num(p.Center.Y * k)} {Num(p.Center.Z * k)}",
                ["spawnflags"] = "1", // en boucle
            });
        if (skipped > 0)
            log.Warn("JK_SOUND_SKIPPED", $"{skipped} sound group(s) are not placed: the level would need more than {MaxSectorSpeakers} looping speakers even after merging neighbours within {radius:0.#} JK units.");
        SoundAsset[] files = [.. placed.Select(p => p.Asset).DistinctBy(a => a.Path).OrderBy(a => a.Path, StringComparer.Ordinal)];
        log.Warn("JK_SECTOR_SOUND", $"{withSound} sector(s) play a sound: {placed.Count} looping target_speaker(s) placed at the sector centres (neighbours within {radius:0.#} JK units "
            + $"with the same sound share one), {files.Length} wav file(s) copied under {JkSounds.Folder}; volumes are not applied (a JA speaker has no volume).");
        return new ConvertedEntities(entities, [], []) { Sounds = files };
    }

    /// <summary>Nombre pour une cle d'entite : trois decimales au plus, sans « -0 ».</summary>
    internal static string Num(float v) => (MathF.Abs(v) < 0.0005f ? 0f : v).ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Brushes des volumes : plans du secteur (convexe) ou boite englobante, en unites JA.</summary>
internal static class JkVolumeBrushes
{
    /// <summary>Degats d'une zone mortelle : bien au-dela des 100 points de vie de Kyle.</summary>
    public const int KillDamage = 10000;

    /// <summary>Tolerance de convexite (unites JK) : un sommet du secteur doit etre de l'interieur de chaque plan a cette marge pres.</summary>
    private const float ConvexTolerance = 0.003f;

    /// <summary>
    /// Plans sortants du secteur <paramref name="sector"/> : un par plan de surface (les plans confondus sont fusionnes), mis a l'echelle <paramref name="k"/> (unites JA par unite JK).
    /// Un secteur JK est convexe ; null s'il ne l'est pas (un sommet depasse un plan), s'il est ouvert (moins de quatre plans) ou sans geometrie : l'appelant
    /// prend alors la boite englobante.
    /// </summary>
    public static IReadOnlyList<BspPlane>? SectorPlanes(JklLevel level, int sector, float k)
    {
        if (sector < 0 || sector >= level.Sectors.Count) return null;
        JklSector sec = level.Sectors[sector];
        var polygons = new List<Vector3[]>();
        for (int s = Math.Max(sec.FirstSurface, 0); s < sec.FirstSurface + sec.SurfaceCount && s < level.Surfaces.Count; s++)
        {
            int[] ids = level.Surfaces[s].VertexIndices;
            if (ids.Length < 3 || ids.Any(i => i < 0 || i >= level.Vertices.Length)) continue;
            polygons.Add([.. ids.Select(i => level.Vertices[i])]);
        }
        if (polygons.Count < 4) return null;
        Vector3[] all = [.. polygons.SelectMany(p => p).Distinct()];
        Vector3 centroid = all.Aggregate(Vector3.Zero, (a, b) => a + b) / all.Length;

        var planes = new List<(Vector3 N, float D)>();
        foreach (Vector3[] poly in polygons)
        {
            Vector3 n = Vector3.Zero;
            for (int i = 0; i < poly.Length; i++) n += Vector3.Cross(poly[i], poly[(i + 1) % poly.Length]);
            float len = n.Length();
            if (len < 1e-9f) continue;
            n /= len;
            float d = Vector3.Dot(n, poly[0]);
            if (Vector3.Dot(n, centroid) > d) { n = -n; d = -d; } // normale vers l'exterieur
            foreach (Vector3 v in all)
                if (Vector3.Dot(n, v) > d + ConvexTolerance) return null; // secteur non convexe
            if (planes.Any(p => Vector3.Dot(p.N, n) > 0.9999f && MathF.Abs(p.D - d) < 0.002f)) continue;
            planes.Add((n, d));
        }
        if (planes.Count < 4) return null;
        return [.. planes.Select(p => new BspPlane(p.N, p.D * k))];
    }

    /// <summary>Six plans d'une boite alignee sur les axes (unites JA), d'au moins 2 unites d'epaisseur par axe.</summary>
    public static IReadOnlyList<BspPlane> Box(Vector3 min, Vector3 max)
    {
        for (int axis = 0; axis < 3; axis++)
            if (max[axis] - min[axis] < 2f) { float mid = (min[axis] + max[axis]) / 2f; min[axis] = mid - 1f; max[axis] = mid + 1f; }
        return
        [
            new BspPlane(-Vector3.UnitX, -min.X), new BspPlane(Vector3.UnitX, max.X), new BspPlane(-Vector3.UnitY, -min.Y),
            new BspPlane(Vector3.UnitY, max.Y), new BspPlane(-Vector3.UnitZ, -min.Z), new BspPlane(Vector3.UnitZ, max.Z),
        ];
    }
}

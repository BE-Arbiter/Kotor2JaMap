using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;

namespace Kotor.Pipeline.Entities;

/// <summary>
/// Portes du GIT -> <c>func_door</c> : sous-modele de brush (boite englobante orientee du modele, solide et invisible,
/// <b>relative a l'origine</b> de la porte) + MD3 de la porte en <c>model2</c> + <c>origin</c>.
/// Conventions verifiees dans OpenJK-SWGL :
/// <list type="bullet">
/// <item><c>g_mover.cpp:1067-1085</c> (<c>InitMover</c>) : <c>model2</c> = modele de dessin, la collision vient des brushes ; le MD3 est dessine a <c>lerpOrigin</c> (<c>cg_ents.cpp:1296-1360</c>).</item>
/// <item><c>g_spawn.cpp:232,236,265-267</c> : cles <c>origin</c>, <c>model2</c>, <c>angle</c> (<c>-1</c> = haut, <c>G_SetMovedir</c> g_utils.cpp:702-718 remet ensuite <c>angles</c> a 0), <c>modelAngles</c> (vecteur).</item>
/// <item><c>cg_ents.cpp:1354-1358</c> : le MD3 est tourne de <c>lerpAngles + modelAngles</c> (<c>AnglesToAxis</c>) : meme formule que les objets, yaw seul.</item>
/// <item><c>cm_load.cpp:127-184</c> (<c>CMod_LoadSubmodels</c>) : un sous-modele est un "leaf" fabrique avec ses brushes (firstBrush, numBrushes) ; <c>cm_load.cpp:229-238</c> : les 6 premiers cotes sont les bornes axiales.</item>
/// <item><c>g_mover.cpp:1486-1493</c> : <c>SetBrushModel</c> prend mins/maxs du sous-modele ; distance = taille en Z - lip. Les brushes sont donc relatifs a <c>origin</c> (pos1 = origin).</item>
/// <item><c>g_local.h:521</c> : <c>MOVER_LOCKED</c> = 16 ; <c>Touch_DoorTrigger</c> (g_mover.cpp:1207) n'ouvre pas une porte verrouillee.</item>
/// </list>
/// </summary>
public static class DoorConverter
{
    /// <summary>Shader de collision : <c>textures/system/clip</c> existe dans les pk3 de base de JA (shaders/system.shader de assets1.pk3). Le contenu solide vient du lump des shaders du BSP.</summary>
    public const string ClipShader = "textures/system/clip";
    /// <summary>MOVER_LOCKED (OpenJK-SWGL code/game/g_local.h:521).</summary>
    public const int SpawnflagLocked = 16;
    /// <summary>Epaisseur minimale (unites JA) de la boite sur chaque axe : une porte plate ne doit pas donner un brush degenere.</summary>
    public const float MinThickness = 2f;

    public static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(ResourceResolver res, KotorGame game,
        IReadOnlyList<ModuleEntity> doors, float scale, int firstSubModelIndex, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        return Convert(new PlaceableConverter.ResolverAssets(res), game, doors, scale, firstSubModelIndex, log);
    }

    internal static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(IObjectAssets assets, KotorGame game,
        IReadOnlyList<ModuleEntity> doors, float scale, int firstSubModelIndex, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(log);
        string prefix = Md3ShaderWriter.ModelPrefix(game);
        var models = new Dictionary<string, PlaceableConverter.ModelInfo>(StringComparer.Ordinal);
        var entities = new List<IReadOnlyDictionary<string, string>>();
        var subModels = new List<SubModelInput>();
        int converted = 0, locked = 0, unlocked = 0, skipped = 0, total = 0;

        foreach (ModuleEntity e in doors)
        {
            if (e.Kind != EntityKind.Door) continue;
            total++;
            string? why0 = null;
            string? model = e.Model ?? assets.ModelOf(e, out why0);
            if (model is null) { Skip(e, why0 ?? "no model", log, ref skipped); continue; }
            if (!models.TryGetValue(model, out PlaceableConverter.ModelInfo? info))
                models[model] = info = PlaceableConverter.BuildModel(assets, model, game, scale, prefix, log, loadPwk: false);
            if (info.Assets.Count == 0) { Skip(e, info.Failure ?? "model not converted", log, ref skipped); continue; }
            if (info.Assets.Count > 1 || info.DivideBy > 1) { Skip(e, "door model too large for one MD3", log, ref skipped); continue; }

            float yaw = NormalizeDegrees(e.Yaw);
            int index = firstSubModelIndex + subModels.Count;
            subModels.Add(new SubModelInput([Box(info.Min * scale, info.Max * scale, yaw)], ClipShader));

            var ent = new Dictionary<string, string>
            {
                ["classname"] = "func_door",
                ["model"] = $"*{index}",
                ["model2"] = info.Assets[0].Path,
                ["origin"] = $"{PlaceableConverter.Num(e.Position.X * scale)} {PlaceableConverter.Num(e.Position.Y * scale)} {PlaceableConverter.Num(e.Position.Z * scale)}",
                // Meme orientation que les objets : yaw du GIT seul (rapport de l'etape 03), applique au MD3 par modelAngles
                // et au brush par la rotation des plans ci-dessous.
                ["modelAngles"] = $"0 {PlaceableConverter.Num(yaw)} 0",
                ["angle"] = "-1", // vers le haut
                ["lip"] = "8",
                ["speed"] = "200",
                ["wait"] = "3",
            };
            // Seules les portes Static (decoratives : sans cela elles s'ouvrent sur le vide) restent verrouillees. Les portes UTD
            // Locked et de transition (LinkedToModule) s'ouvrent a l'usage : le verrou KOTOR (cle, crochetage) n'existe pas dans JA.
            if (e.Static)
            {
                ent["spawnflags"] = SpawnflagLocked.ToString(System.Globalization.CultureInfo.InvariantCulture);
                locked++;
            }
            else if (e.Locked || e.LinkedToModule.Length > 0) unlocked++;
            entities.Add(ent);
            converted++;
        }

        var assetsOut = models.Values.SelectMany(m => m.Assets).OrderBy(a => a.Path, StringComparer.Ordinal).ToList();
        var textureData = new SortedDictionary<string, Kotor.Core.Textures.KotorTexture?>(StringComparer.Ordinal);
        foreach (string name in assetsOut.SelectMany(a => a.Textures).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            textureData[name] = assets.LoadTexture(name, log);
        var shaders = textureData.Select(kv => Md3ShaderWriter.Write(game, kv.Key, kv.Value)).ToList();

        if (total > 0)
            log.Warn("DOORS", $"{converted} door(s) converted ({locked} static kept closed, {unlocked} locked/transition opened), {skipped} skipped.");
        var result = new ConvertedEntities(entities, assetsOut, [])
        {
            TextureData = textureData,
            ModelShaders = shaders,
        };
        return (result, subModels);
    }

    /// <summary>
    /// Boite <paramref name="min"/>..<paramref name="max"/> (repere du modele, unites JA) tournee de <paramref name="yawDegrees"/> autour de Z :
    /// 6 plans a normale sortante, relatifs a l'origine de la porte.
    /// </summary>
    internal static IReadOnlyList<BspPlane> Box(Vector3 min, Vector3 max, float yawDegrees)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            float lo = min[axis], hi = max[axis];
            if (hi - lo < MinThickness) { float mid = (lo + hi) / 2f; min[axis] = mid - MinThickness / 2f; max[axis] = mid + MinThickness / 2f; }
        }
        float rad = yawDegrees * MathF.PI / 180f, c = MathF.Cos(rad), s = MathF.Sin(rad);
        var ex = new Vector3(c, s, 0);   // axe X du modele apres rotation anti-horaire
        var ey = new Vector3(-s, c, 0);
        return
        [
            new BspPlane(ex, max.X), new BspPlane(-ex, -min.X),
            new BspPlane(ey, max.Y), new BspPlane(-ey, -min.Y),
            new BspPlane(Vector3.UnitZ, max.Z), new BspPlane(-Vector3.UnitZ, -min.Z),
        ];
    }

    private static void Skip(ModuleEntity e, string reason, ImportLog log, ref int skipped)
    {
        skipped++;
        log.Warn("DOOR_SKIPPED", $"Door '{e.Name}' (template '{e.Template}') at ({PlaceableConverter.Num(e.Position.X)}, {PlaceableConverter.Num(e.Position.Y)}, {PlaceableConverter.Num(e.Position.Z)}): {reason}; skipped.");
    }

    private static float NormalizeDegrees(float deg)
    {
        deg %= 360f;
        if (deg < 0) deg += 360f;
        return deg >= 360f ? 0f : deg;
    }
}

using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Mdl;
using Kotor.Core.Models;

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
    /// <summary>MOVER_PLAYER_USE (g_local.h) : la porte s'ouvre au bouton +use et n'a plus de declencheur de proximite (g_mover.cpp:1122, 1525).</summary>
    public const int SpawnflagPlayerUse = 64;
    /// <summary>Epaisseur minimale (unites JA) de la boite sur chaque axe : une porte plate ne doit pas donner un brush degenere.</summary>
    public const float MinThickness = 2f;

    public static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(ResourceResolver res, KotorGame game,
        IReadOnlyList<ModuleEntity> doors, float scale, int firstSubModelIndex, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        return Convert(new PlaceableConverter.ResolverAssets(res), game, doors, scale, firstSubModelIndex, log, new AudioSources(res, game));
    }

    internal static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(IObjectAssets assets, KotorGame game,
        IReadOnlyList<ModuleEntity> doors, float scale, int firstSubModelIndex, ImportLog log, AudioSources? audio = null)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(doors);
        ArgumentNullException.ThrowIfNull(log);
        string prefix = Md3ShaderWriter.ModelPrefix(game);
        var models = new Dictionary<string, PlaceableConverter.ModelInfo>(StringComparer.Ordinal);
        var entities = new List<IReadOnlyDictionary<string, string>>();
        var subModels = new List<SubModelInput>();
        var sounds = new Dictionary<string, SoundAsset>(StringComparer.Ordinal);
        var rigs = new Dictionary<string, DoorRig?>(StringComparer.Ordinal);
        int animated = 0;
        int converted = 0, locked = 0, unlocked = 0, skipped = 0, total = 0;

        foreach (ModuleEntity e in doors)
        {
            if (e.Kind != EntityKind.Door) continue;
            total++;
            string? why0 = null;
            string? model = e.Model ?? assets.ModelOf(e, out why0);
            if (model is null) { Skip(e, why0 ?? "no model", log, ref skipped); continue; }

            // Porte animee : un func_door par groupe de panneaux qui coulissent, le cadre fixe en misc_model_static.
            if (!e.Static)
            {
                if (!rigs.TryGetValue(model, out DoorRig? rig)) rigs[model] = rig = BuildRig(assets, model, game, scale, prefix, log);
                if (rig is not null)
                {
                    if (AddRigged(e, rig, scale, firstSubModelIndex, subModels, entities, audio, sounds)) unlocked++;
                    converted++;
                    animated++;
                    continue;
                }
            }
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
            // Static (decorative : sans cela elle s'ouvre sur le vide) : verrouillee. Locked UTD et transition (LinkedToModule) : le verrou KOTOR
            // (cle, crochetage) n'existe pas dans JA, elles s'ouvrent au bouton +use seulement (PLAYER_USE supprime le declencheur de proximite).
            if (e.Static)
            {
                ent["spawnflags"] = SpawnflagLocked.ToString(System.Globalization.CultureInfo.InvariantCulture);
                locked++;
            }
            else if (e.Locked || e.LinkedToModule.Length > 0)
            {
                ent["spawnflags"] = SpawnflagPlayerUse.ToString(System.Globalization.CultureInfo.InvariantCulture);
                unlocked++;
            }
            // Sons d'ouverture et de fermeture (haut-parleurs tires par target / closetarget). Les portes Static ne bougent pas : sans son.
            if (audio is not null && !e.Static
                && AudioConverter.DoorSpeakers(audio, e, $"kd{index}", scale) is var (speakers, doorKeys, doorAssets))
            {
                foreach (var kv in doorKeys) ent[kv.Key] = kv.Value;
                entities.AddRange(speakers);
                foreach (SoundAsset a in doorAssets) sounds[a.Path] = a;
            }
            entities.Add(ent);
            converted++;
        }

        var assetsOut = models.Values.SelectMany(m => m.Assets).Concat(rigs.Values.Where(r => r is not null).SelectMany(r => r!.Assets))
            .GroupBy(a => a.Path, StringComparer.Ordinal).Select(g => g.First()).OrderBy(a => a.Path, StringComparer.Ordinal).ToList();
        var textureData = new SortedDictionary<string, Kotor.Core.Textures.KotorTexture?>(StringComparer.Ordinal);
        foreach (string name in assetsOut.SelectMany(a => a.Textures).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            textureData[name] = assets.LoadTexture(name, log);
        var shaders = textureData.Select(kv => Md3ShaderWriter.Write(game, kv.Key, kv.Value)).ToList();

        if (total > 0)
            log.Warn("DOORS", $"{converted} door(s) converted ({locked} static kept closed, {unlocked} locked/transition opened with +use), {skipped} skipped.{(animated > 0 ? $" {animated} with the KOTOR opening animation." : "")}");
        var result = new ConvertedEntities(entities, assetsOut, [])
        {
            TextureData = textureData,
            ModelShaders = shaders,
            Sounds = [.. sounds.Values.OrderBy(a => a.Path, StringComparer.Ordinal)],
        };
        return (result, subModels);
    }

    /// <summary>Panneau mobile : MD3, boite englobante (metres, repere du modele) et deplacement d'ouverture (metres, repere du modele).</summary>
    private sealed record RigPiece(Md3Asset Asset, Vector3 Min, Vector3 Max, Vector3 Offset);

    /// <summary>Porte animee : panneaux mobiles, cadre fixe (ou null) et duree d'ouverture (secondes).</summary>
    private sealed record DoorRig(IReadOnlyList<RigPiece> Pieces, Md3Asset? Frame, float Duration)
    {
        public IEnumerable<Md3Asset> Assets => Frame is null ? Pieces.Select(p => p.Asset) : Pieces.Select(p => p.Asset).Append(Frame);
    }

    /// <summary>
    /// Decoupe le modele d'une porte selon son animation d'ouverture (<see cref="DoorMotion"/>) : un MD3 par groupe de panneaux
    /// et un pour le cadre. Null (la porte garde l'ancien comportement : une boite qui monte) si le modele n'a pas d'animation
    /// de translation exploitable, ou si un morceau depasse la limite MD3.
    /// </summary>
    private static DoorRig? BuildRig(IObjectAssets assets, string model, KotorGame game, float scale, string prefix, ImportLog log)
    {
        KotorModel? mdl = assets.LoadModel(model, out _);
        DoorMotion? motion = mdl is null ? null : DoorMotion.Analyze(mdl);
        if (mdl is null || motion is null) return null;
        string dir = PlaceableConverter.ModelDirectory(prefix, scale);

        (Md3Asset Asset, Vector3 Min, Vector3 Max)? Make(string name, IReadOnlyList<KotorNode> nodes)
        {
            var set = new HashSet<KotorNode>(nodes);
            IReadOnlyList<ModelPart> parts = ModelMeshBuilder.Build(mdl, log, model, set.Contains);
            if (parts.Count == 0) return null;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            float biggest = 0;
            foreach (ModelPart p in parts)
                foreach (Vector3 v in p.Positions)
                {
                    min = Vector3.Min(min, v); max = Vector3.Max(max, v);
                    biggest = MathF.Max(biggest, MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z))));
                }
            if (!float.IsFinite(biggest) || biggest * scale > PlaceableConverter.ModelLimit) return null;
            var ja = parts.Select(p => p with { Positions = [.. p.Positions.Select(v => v * scale)] }).ToList();
            IReadOnlyList<Md3Writer.Md3File> files;
            try { files = Md3Writer.WriteSplit(name, ja, prefix); }
            catch (ArgumentException) { return null; }
            if (files.Count != 1) return null;
            return (new Md3Asset($"{dir}{name}.md3", files[0].Data, files[0].Textures), min, max);
        }

        var pieces = new List<RigPiece>();
        for (int k = 0; k < motion.Moving.Count; k++)
        {
            var made = Make($"{model}_d{k}", motion.Moving[k].Nodes);
            if (made is null) return null; // un panneau manquerait : mieux vaut l'ancienne porte entiere
            pieces.Add(new RigPiece(made.Value.Asset, made.Value.Min, made.Value.Max, motion.Moving[k].Offset));
        }
        Md3Asset? frame = null;
        if (motion.Fixed.Count > 0)
        {
            var made = Make($"{model}_df", motion.Fixed);
            frame = made?.Asset;
        }
        return new DoorRig(pieces, frame, motion.Duration);
    }

    /// <summary>
    /// Ajoute une porte animee : cadre en <c>misc_model_static</c>, puis un <c>func_door</c> par panneau (meme <c>team</c> : ils s'ouvrent ensemble,
    /// <c>Use_BinaryMover</c> g_mover.cpp:968). Un panneau se deplace de son vecteur d'animation : <c>angles</c> donne la direction (G_SetMovedir),
    /// <c>lip</c> = etendue de la boite le long de la direction - distance voulue (<c>distance = |movedir.taille| - lip</c>, g_mover.cpp:1486-1493),
    /// <c>speed</c> = distance / duree. Retourne vrai si la porte est ouvrable par +use seulement (Locked, transition).
    /// </summary>
    private static bool AddRigged(ModuleEntity e, DoorRig rig, float scale, int firstSubModelIndex, List<SubModelInput> subModels,
        List<IReadOnlyDictionary<string, string>> entities, AudioSources? audio, Dictionary<string, SoundAsset> sounds)
    {
        float yaw = NormalizeDegrees(e.Yaw);
        float rad = yaw * MathF.PI / 180f, c = MathF.Cos(rad), s = MathF.Sin(rad);
        Vector3 Rotate(Vector3 v) => new(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z);
        string origin = $"{PlaceableConverter.Num(e.Position.X * scale)} {PlaceableConverter.Num(e.Position.Y * scale)} {PlaceableConverter.Num(e.Position.Z * scale)}";
        bool playerUse = e.Locked || e.LinkedToModule.Length > 0;
        int first = firstSubModelIndex + subModels.Count;
        string team = $"kd{first}";

        if (rig.Frame is { } frame)
            entities.Add(new Dictionary<string, string> { ["classname"] = "misc_model_static", ["model"] = frame.Path, ["origin"] = origin, ["angles"] = $"0 {PlaceableConverter.Num(yaw)} 0" });

        bool firstPiece = true;
        foreach (RigPiece p in rig.Pieces)
        {
            int index = firstSubModelIndex + subModels.Count;
            Vector3 min = p.Min * scale, max = p.Max * scale;
            subModels.Add(new SubModelInput([Box(min, max, yaw)], ClipShader));

            Vector3 move = Rotate(p.Offset) * scale;
            float distance = move.Length();
            Vector3 dir = move / distance;
            // Etendue de la boite alignee sur les axes (celle du sous-modele du BSP) le long de la direction.
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = Rotate(new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z));
                lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner);
            }
            Vector3 size = hi - lo;
            float extent = MathF.Abs(dir.X) * size.X + MathF.Abs(dir.Y) * size.Y + MathF.Abs(dir.Z) * size.Z;
            float pitch = -MathF.Asin(Math.Clamp(dir.Z, -1f, 1f)) * 180f / MathF.PI;
            float heading = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;

            var ent = new Dictionary<string, string>
            {
                ["classname"] = "func_door",
                ["model"] = $"*{index}",
                ["model2"] = p.Asset.Path,
                ["origin"] = origin,
                ["modelAngles"] = $"0 {PlaceableConverter.Num(yaw)} 0",
                ["angles"] = $"{PlaceableConverter.Num(pitch)} {PlaceableConverter.Num(heading)} 0",
                ["lip"] = PlaceableConverter.Num(extent - distance),
                ["speed"] = PlaceableConverter.Num(MathF.Max(1f, distance / MathF.Max(0.05f, rig.Duration))),
                ["wait"] = "3",
            };
            if (rig.Pieces.Count > 1) ent["team"] = team;
            if (playerUse) ent["spawnflags"] = SpawnflagPlayerUse.ToString(System.Globalization.CultureInfo.InvariantCulture);
            // Sons : portes de l'equipe tirees par le premier panneau (le maitre de l'equipe) uniquement.
            if (firstPiece && audio is not null
                && AudioConverter.DoorSpeakers(audio, e, $"kd{index}", scale) is var (speakers, doorKeys, doorAssets))
            {
                foreach (var kv in doorKeys) ent[kv.Key] = kv.Value;
                entities.AddRange(speakers);
                foreach (SoundAsset a in doorAssets) sounds[a.Path] = a;
            }
            firstPiece = false;
            entities.Add(ent);
        }
        return playerUse;
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

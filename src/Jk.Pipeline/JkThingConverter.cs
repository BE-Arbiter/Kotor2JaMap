using System.Globalization;
using System.Numerics;
using System.Text;
using Jk.Core;
using Jk.Core.Cog;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Core.Models;
using Jk.Core.Text;
using Jk.Core.Textures;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Textures;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline;

/// <summary>Angles : rotation d'un thing JK -> angles d'entite JA.</summary>
internal static class JkAngles
{
    /// <summary>
    /// Angles JA (pitch, yaw, roll) dont <c>AnglesToAxis</c> donne la rotation du thing JK (pitch, yaw, roll JK, voir <see cref="ThreeDoModel.Rotation"/>).
    /// Le MD3 d'un modele JK reste dans le repere JK (+Y vers l'avant) : la rotation est la meme, on ne decale donc pas le lacet de 90 degres
    /// (pour un lacet seul, JA = JK). Le tangage et le roulis, eux, ne se correspondent pas axe a axe : on extrait les angles JA de la matrice
    /// (JA : <c>Rz(yaw) Ry(pitch) Rx(roll)</c> ; JK : <c>Rz(yaw) Rx(pitch) Ry(roll)</c>, en colonnes). Arrondi au millieme de degre.
    /// </summary>
    public static Vector3 ToJa(Vector3 jkPitchYawRoll)
    {
        Matrix4x4 m = ThreeDoModel.Rotation(jkPitchYawRoll); // vecteurs ligne : W[i][j] = m[j][i]
        const float ToDeg = 180f / MathF.PI;
        float pitch = -MathF.Asin(Math.Clamp(m.M13, -1f, 1f)) * ToDeg;
        float yaw, roll;
        if (MathF.Abs(m.M13) > 0.99999f) { roll = 0f; yaw = MathF.Atan2(-m.M21, m.M22) * ToDeg; } // tangage de +-90 : lacet et roulis confondus
        else { yaw = MathF.Atan2(m.M12, m.M11) * ToDeg; roll = MathF.Atan2(m.M23, m.M33) * ToDeg; }
        return new Vector3(Snap(pitch), Snap(yaw), Snap(roll));
    }

    /// <summary>Angle JA (<c>angle</c> d'un depart) du joueur dont le lacet JK est donne : 0 JK regarde +Y, 0 JA regarde +X, d'ou +90 ; 0 a 360.</summary>
    public static float StartAngle(float jkYaw)
    {
        float a = (jkYaw + 90f) % 360f;
        return Snap(a < 0 ? a + 360f : a);
    }

    private static float Snap(float degrees)
    {
        float r = MathF.Round(degrees, 3);
        return MathF.Abs(r) < 0.0005f ? 0f : r;
    }
}

/// <summary>Triangles de la premiere image d'un MD3 (unites JA telles qu'ecrites, avant <c>modelscale</c>).</summary>
internal static class JkMd3Geometry
{
    public static IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> Frame0Triangles(byte[] md3)
    {
        var tris = new List<(Vector3, Vector3, Vector3)>();
        if (md3.Length < 108 || md3[0] != 'I' || md3[1] != 'D' || md3[2] != 'P' || md3[3] != '3') return tris;
        int surfaces = BitConverter.ToInt32(md3, 84), ofsSurfaces = BitConverter.ToInt32(md3, 100);
        int at = ofsSurfaces;
        for (int s = 0; s < surfaces; s++)
        {
            if (at < 0 || at + 108 > md3.Length) break;
            int numVerts = BitConverter.ToInt32(md3, at + 80), numTris = BitConverter.ToInt32(md3, at + 84);
            int ofsTris = at + BitConverter.ToInt32(md3, at + 88), ofsXyz = at + BitConverter.ToInt32(md3, at + 100);
            if (numVerts <= 0 || numTris < 0 || ofsTris < 0 || ofsXyz < 0 || ofsTris + numTris * 12L > md3.Length || ofsXyz + numVerts * 8L > md3.Length) break;
            Vector3 V(int i) => new(BitConverter.ToInt16(md3, ofsXyz + i * 8) / 64f, BitConverter.ToInt16(md3, ofsXyz + i * 8 + 2) / 64f, BitConverter.ToInt16(md3, ofsXyz + i * 8 + 4) / 64f);
            for (int t = 0; t < numTris; t++)
            {
                int a = BitConverter.ToInt32(md3, ofsTris + t * 12), b = BitConverter.ToInt32(md3, ofsTris + t * 12 + 4), c = BitConverter.ToInt32(md3, ofsTris + t * 12 + 8);
                if (a < 0 || b < 0 || c < 0 || a >= numVerts || b >= numVerts || c >= numVerts) continue;
                tris.Add((V(a), V(b), V(c)));
            }
            at += BitConverter.ToInt32(md3, at + 104);
        }
        return tris;
    }
}

/// <summary>Charge les modeles <c>.3do</c> (et animations <c>.key</c>) d'un niveau une seule fois, avec les materiaux dont ils ont besoin.</summary>
internal sealed class JkModelPool
{
    private readonly JkFileSystem _fs;
    private readonly JkPreparedScene _prepared;
    private readonly ExportOptions _opt;
    private readonly ImportLog _log;
    private readonly Dictionary<string, JkMaterial> _mats = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _tried = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string), JkMd3?> _cache = [];
    private readonly Lazy<CmpPalette> _palette;
    private readonly Dictionary<string, IReadOnlyList<(Vector3, Vector3, Vector3)>> _collision = new(StringComparer.Ordinal);

    public JkModelPool(JkFileSystem fs, JkPreparedScene prepared, ExportOptions opt, ImportLog log)
    {
        _fs = fs; _prepared = prepared; _opt = opt; _log = log;
        // Materiaux deja decodes par la scene : memes noms de texture, aucun nouveau decodage.
        foreach (var kv in prepared.Materials) _mats[kv.Key + ".mat"] = kv.Value;
        _palette = new Lazy<CmpPalette>(() => JkSceneBuilder.LoadPalette(prepared.Level, fs.TryRead, new ImportLog()));
    }

    /// <summary>MD3 produits, par chemin.</summary>
    public SortedDictionary<string, Md3Asset> Assets { get; } = new(StringComparer.Ordinal);

    /// <summary>Texture de chaque nom de texture de modele (null : materiau introuvable, damier).</summary>
    public SortedDictionary<string, KotorTexture?> Textures { get; } = new(StringComparer.Ordinal);

    /// <summary>Modele <paramref name="model3do"/> (nom de fichier) avec l'animation <paramref name="key"/> (nom de fichier ou null) ; null si la conversion echoue (deja journalise).</summary>
    public JkMd3? Get(string model3do, string? key)
    {
        string name = Path.GetFileName(model3do.Replace('/', '\\'));
        string keyName = key is null ? "" : Path.GetFileName(key.Replace('/', '\\'));
        var id = (name.ToLowerInvariant(), keyName.ToLowerInvariant());
        if (_cache.TryGetValue(id, out JkMd3? known)) return known;

        EnsureMaterialsOf(name);
        JkMd3? md3 = JkModelConverter.ToMd3(_fs, name, key is null ? null : keyName, _opt.Scale, _mats, _log);
        if (md3 is not null && key is not null)
        {
            // Un meme modele avec et sans animation : deux fichiers distincts.
            string path = md3.Asset.Path;
            string bare = Path.GetFileNameWithoutExtension(keyName).ToLowerInvariant();
            md3 = md3 with { Asset = md3.Asset with { Path = path[..^".md3".Length] + "_" + bare + ".md3" } };
        }
        _cache[id] = md3;
        if (md3 is not null)
        {
            Assets[md3.Asset.Path] = md3.Asset;
            foreach (string tex in md3.Asset.Textures) Textures[tex] = _mats.TryGetValue(tex + ".mat", out JkMaterial? m) && m.Cels.Count > 0 ? m.Cels[0] : null;
        }
        return md3;
    }

    /// <summary>Triangles de collision d'un modele (images 0, unites JA, doublons de faces arriere retires), mis en cache par chemin de MD3.</summary>
    public IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> CollisionTriangles(JkMd3 md3)
    {
        if (_collision.TryGetValue(md3.Asset.Path, out var known)) return known;
        var seen = new HashSet<(long, long, long)>();
        var list = new List<(Vector3, Vector3, Vector3)>();
        foreach (var t in JkMd3Geometry.Frame0Triangles(md3.Asset.Data))
        {
            long a = Key(t.A), b = Key(t.B), c = Key(t.C);
            long[] sorted = [a, b, c];
            Array.Sort(sorted);
            if (seen.Add((sorted[0], sorted[1], sorted[2]))) list.Add(t);
        }
        return _collision[md3.Asset.Path] = list;

        static long Key(Vector3 v) => ((long)MathF.Round(v.X * 4f) & 0x1FFFFF) << 42 | ((long)MathF.Round(v.Y * 4f) & 0x1FFFFF) << 21 | ((long)MathF.Round(v.Z * 4f) & 0x1FFFFF);
    }

    private void EnsureMaterialsOf(string model3do)
    {
        byte[]? raw = _fs.TryRead(@"3do\" + model3do);
        if (raw is null) return;
        IReadOnlyList<string> files;
        try { files = ThreeDoReader.Read(ThreeDoReader.Decode(raw)).Materials; }
        catch (InvalidDataException) { return; } // le convertisseur journalise
        foreach (string file in files)
        {
            if (_mats.ContainsKey(file) || !_tried.Add(file)) continue;
            byte[]? data = _fs.TryRead(@"mat\" + file) ?? _fs.TryRead(@"3do\mat\" + file);
            if (data is null) continue; // le convertisseur journalise TEXTURE_MISSING
            try { _mats[file] = MatReader.Read(JkSceneBuilder.TextureKey(file), data, _palette.Value); }
            catch (InvalidDataException) { }
        }
    }
}

/// <summary>Animations <c>.key</c> jouees en boucle par les COG : thing -> fichier .key (appel <c>PlayKey(thing, cle, ...)</c> d'une instance ou du COG attache au gabarit).</summary>
internal static class JkThingAnimations
{
    public static Dictionary<int, string> Find(JkPreparedScene prepared, Func<string, CogScript?> script)
    {
        var result = new Dictionary<int, string>();
        // Instances de COG du niveau : le thing et la cle viennent des valeurs de l'instance (ou des valeurs par defaut du script).
        foreach (JklCogInstance inst in prepared.Level.Cogs)
        {
            CogScript? cog = script(inst.CogFile);
            if (cog is null) continue;
            IReadOnlyDictionary<string, string> values = cog.Bind(inst);
            foreach (CogCallSite call in cog.AllCalls())
            {
                if (!call.Function.Equals("PlayKey", StringComparison.OrdinalIgnoreCase) || call.Args.Length < 2) continue;
                if (int.TryParse(Value(call.Args[0], values), NumberStyles.Integer, CultureInfo.InvariantCulture, out int thing) && thing >= 0
                    && Value(call.Args[1], values) is { } key && key.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
                    result.TryAdd(thing, key);
            }
        }
        // COG attache au gabarit (class cog) : il agit sur le thing qui le porte ; la cle est la valeur par defaut du script.
        foreach (JklThing t in prepared.Level.Things)
        {
            if (!prepared.Templates.Resolve(t.Template).TryGetValue("cog", out string? file) || result.ContainsKey(t.Index)) continue;
            CogScript? cog = script(file);
            if (cog is null) continue;
            var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (CogSymbol s in cog.Symbols) if (s.Default is not null) defaults[s.Name] = s.Default;
            foreach (CogCallSite call in cog.AllCalls())
                if (call.Function.Equals("PlayKey", StringComparison.OrdinalIgnoreCase) && call.Args.Length >= 2
                    && cog.Symbols.Any(s => s.Type == "thing" && s.Default is null && s.Name.Equals(call.Args[0].Trim(), StringComparison.OrdinalIgnoreCase))
                    && Value(call.Args[1], defaults) is { } key && key.EndsWith(".key", StringComparison.OrdinalIgnoreCase))
                {
                    result.TryAdd(t.Index, key);
                    break;
                }
        }
        return result;
    }

    private static string? Value(string expr, IReadOnlyDictionary<string, string> values)
    {
        expr = expr.Trim().Trim('"');
        return values.TryGetValue(expr, out string? v) ? v : expr;
    }
}

/// <summary>
/// Conversion des things du niveau (spec §5.5) :
/// <list type="bullet">
/// <item><c>actor</c> : ignore (compte, <c>JK_ACTOR_SKIPPED</c>). <c>player</c> : depart (voir <see cref="Convert"/>). <c>ghost</c> et things sans modele : rien a voir, comptes dans le bilan.</item>
/// <item><c>item</c> : objet JA de <see cref="JkPickups"/>, sinon le modele JK en decor + <c>JK_ITEM_NO_EQUIVALENT</c> ; les drapeaux de CTF donnent aussi <c>team_CTF_*flag</c>.</item>
/// <item>Decors (<c>cog</c>, <c>debris</c>, <c>weapon</c> pose...) : <c>misc_model_static</c> ; modele anime (un .key joue par un COG) : <c>misc_model_breakable</c> en AUTOANIMATE
/// (EF_ANIM_ALLFAST : boucle sur toutes les images, 10 par seconde) ; rotation pure ou vitesse angulaire (<c>angvel</c>) : <c>func_rotating</c> + <c>model2</c>.
/// Collision (<c>collide=3</c>, pas de mouvement) : triangles du MD3 ; jamais pour un objet destructible (<c>JK_EXPLOSIVE_STATIC</c>) ni pour une chose qui tourne (ventilateur : traversable, meme pose sans tourner).</item>
/// <item>Lumieres (<c>light</c>) : entite <c>light</c>. Particules et sprites : <c>JK_PARTICLE_SKIPPED</c> / <c>JK_SPRITE_SKIPPED</c> (aucun dans les 34 niveaux).
/// Things crees par les COG en jeu : <c>JK_RUNTIME_THING</c>.</item>
/// </list>
/// </summary>
public static class JkThingConverter
{
    /// <summary>Bit « depart multijoueur » des <c>thingflags</c> d'un thing <c>player</c> (0x20080501 contre 0x20000401 pour le depart du solo).</summary>
    private const uint MultiplayerStartFlag = 0x80000;
    /// <summary>Triangles de collision par modele et par niveau au plus (le compilateur fait un brush mince par polygone fusionne).</summary>
    private const int MaxTrianglesPerModel = 800, MaxTrianglesPerLevel = 60_000;
    /// <summary>Drapeaux de <c>func_rotating</c> (g_mover.cpp) : en marche des le debut, autour de X, autour de Y.</summary>
    private const int SpawnStartOn = 1, SpawnXAxis = 4, SpawnYAxis = 8;

    /// <summary>
    /// <paramref name="handledElsewhere"/> : indices de things deja pris en charge par un autre convertisseur (elements mobiles de la tache 9), qu'on ne place pas.
    /// Departs : le depart du solo (sans le bit 0x80000) devient <c>info_player_start</c> dans tous les niveaux ; les autres departs (multijoueur) sont
    /// <c>info_player_deathmatch</c> dans un niveau multijoueur, <c>team_CTF_redplayer</c> / <c>team_CTF_blueplayer</c> dans un niveau CTF (equipe du drapeau le plus proche)
    /// et ne sont pas places dans un niveau solo (SWGL choisit au hasard parmi les <c>info_player_deathmatch</c> : le joueur ne demarrerait plus au bon endroit).
    /// </summary>
    public static ConvertedEntities Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log, JkSubModels subs, IReadOnlySet<int>? handledElsewhere = null) =>
        Convert(prepared, fs, opt, log, subs, handledElsewhere, null);

    /// <summary><paramref name="sharedPool"/> : modeles deja charges par l appelant (les mobiles de la tache 9) ; leurs MD3 et textures sont rendus avec ceux des decors.</summary>
    internal static ConvertedEntities Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log, JkSubModels subs, IReadOnlySet<int>? handledElsewhere, JkModelPool? sharedPool)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(subs);
        JklLevel level = prepared.Level;
        float k = JkWorldScale.MetresPerJkUnit * opt.Scale;
        var pool = sharedPool ?? new JkModelPool(fs, prepared, opt, log);
        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        CogScript? Script(string file)
        {
            if (!scripts.TryGetValue(file, out CogScript? s))
            {
                byte[]? raw = fs.TryRead(@"cog\" + file);
                scripts[file] = s = raw is null ? null : CogReader.Read(Encoding.Latin1.GetString(raw));
            }
            return s;
        }

        Dictionary<int, string> animations = JkThingAnimations.Find(prepared, Script);
        var entities = new List<IReadOnlyDictionary<string, string>>();
        var solid = new List<(Vector3, Vector3, Vector3)>();
        var players = new List<(JklThing Thing, bool Multi)>();
        var flags = new List<(bool Red, JklThing Thing)>();
        var noEquivalent = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var attachedScripts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        int actors = 0, ghosts = 0, invisible = 0, pickups = 0, decor = 0, animated = 0, spinning = 0, lights = 0, explosives = 0, particles = 0, sprites = 0;
        int collided = 0, collisionTriangles = 0, collisionCapped = 0, miscModels = 0, miscLimit = 0, approxRotation = 0;
        var otherSkipped = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (JklThing t in level.Things)
        {
            if (handledElsewhere?.Contains(t.Index) == true) continue;
            IReadOnlyDictionary<string, string> r = prepared.Templates.Resolve(t.Template);
            string type = r.GetValueOrDefault("type", "").ToLowerInvariant();
            string? model = ModelOf(r);
            switch (type)
            {
                case "actor": actors++; continue;
                case "ghost": ghosts++; continue;
                case "player": players.Add((t, (Flags(t, r) & MultiplayerStartFlag) != 0)); continue;
                case "particle": particles++; continue;
                case "sprite": sprites++; continue;
            }
            if (r.ContainsKey("particle") && model is null) { particles++; continue; }
            if (r.ContainsKey("sprite") && model is null) { sprites++; continue; }

            Vector3 origin = t.Position * k;
            if (Float(r, "light") is > 0f and float intensity)
            {
                // Lumiere de thing : entite « light » (le jeu solo la libere : l'eclairage de la carte vient des intensites de sommets).
                entities.Add(new Dictionary<string, string>
                {
                    ["classname"] = "light", ["origin"] = Vec(origin), ["light"] = JkObjects.Num(intensity * 2f * k), ["_color"] = "1 1 1",
                });
                lights++;
            }

            if (type == "item" && JkPickups.Map(t.Template) is { } ja)
            {
                entities.Add(new Dictionary<string, string> { ["classname"] = ja, ["origin"] = Vec(origin) });
                pickups++;
                continue;
            }
            if (type == "item" || type == "weapon")
            {
                if (model is not null) noEquivalent[t.Template] = noEquivalent.GetValueOrDefault(t.Template) + 1;
                if (type == "item" && t.Template.StartsWith("ctf_", StringComparison.OrdinalIgnoreCase))
                {
                    if (t.Template.Contains("redflag", StringComparison.OrdinalIgnoreCase)) flags.Add((true, t));
                    else if (t.Template.Contains("goldflag", StringComparison.OrdinalIgnoreCase) || t.Template.Contains("blueflag", StringComparison.OrdinalIgnoreCase)) flags.Add((false, t));
                    noEquivalent.Remove(t.Template); // les drapeaux ont leur entite : pas un objet sans equivalent
                }
            }
            if (model is null) { invisible++; continue; }

            // ---- decor ----
            string? key = animations.GetValueOrDefault(t.Index);
            JkMd3? md3 = pool.Get(model, key);
            if (md3 is null) continue; // la raison est deja dans le journal (JK_MODEL_*)

            Vector3 pyr = t.PitchYawRoll;
            Vector3 ja3 = JkAngles.ToJa(pyr);
            string angles = $"{JkObjects.Num(ja3.X)} {JkObjects.Num(ja3.Y)} {JkObjects.Num(ja3.Z)}";
            bool isItem = type is "item" or "weapon";
            bool destructible = !isItem && IsDestructible(r, Script, attachedScripts);

            // Rotation : pure (cle) ou vitesse angulaire du gabarit ; jamais pour un objet a ramasser (ils tournent d'eux-memes dans JA).
            (int Flag, float Speed, bool Exact)? spin = isItem ? null : SpinOf(md3, r);
            bool isSpinner = spin is not null; // un ventilateur (ou toute chose qui tourne) ne bloque jamais, meme pose sans tourner
            if (spin is not null && md3.ModelScale > 1)
            {
                log.Warn("JK_ROTATION_APPROX", $"Thing {t.Index} ({t.Template}): the spinning model is {md3.ModelScale} times too large for a func_rotating (no model scale); it is placed without spinning.");
                approxRotation++;
                spin = null;
            }
            if (spin is { } s)
            {
                float radius = Math.Max(Math.Max(Math.Abs(md3.Min.X), Math.Abs(md3.Max.X)), Math.Max(Math.Max(Math.Abs(md3.Min.Y), Math.Abs(md3.Max.Y)), Math.Max(Math.Abs(md3.Min.Z), Math.Abs(md3.Max.Z)))) * opt.Scale;
                radius = Math.Max(radius, 2f);
                int index = subs.Add(new SubModelInput([JkVolumeBrushes.Box(new Vector3(-radius), new Vector3(radius))], JkObjects.NonSolidShader) { Contents = BspContents.None });
                var ent = new Dictionary<string, string>
                {
                    ["classname"] = "func_rotating",
                    ["model"] = $"*{index}",
                    ["model2"] = md3.Asset.Path,
                    ["origin"] = Vec(origin),
                    ["spawnflags"] = (SpawnStartOn | s.Flag).ToString(CultureInfo.InvariantCulture),
                    ["speed"] = JkObjects.Num(s.Speed),
                };
                if (ja3 != Vector3.Zero) ent["modelAngles"] = angles;
                entities.Add(ent);
                if (!s.Exact)
                {
                    log.Warn("JK_ROTATION_APPROX", $"Thing {t.Index} ({t.Template}): the spin does not follow a single model axis (or the model has a rotation centre away from its origin); the closest single-axis spin is used.");
                    approxRotation++;
                }
                spinning++;
                continue;
            }

            if (md3.Frames > 1)
            {
                // Modele anime : misc_model_breakable AUTOANIMATE (2), ni SOLID (1) ni vie : il boucle ses images (EF_ANIM_ALLFAST, 10 par seconde) et ne bloque rien.
                float kk = md3.ModelScale;
                Vector3 min = md3.Min * opt.Scale, max = md3.Max * opt.Scale;
                float horiz = Math.Max(Math.Max(Math.Abs(min.X), Math.Abs(max.X)), Math.Max(Math.Abs(min.Y), Math.Abs(max.Y)));
                float oldMinZ = min.Z / kk;
                var ent = new Dictionary<string, string>
                {
                    ["classname"] = "misc_model_breakable",
                    ["model"] = md3.Asset.Path,
                    ["origin"] = Vec(origin),
                    ["angles"] = angles,
                    ["spawnflags"] = "2",
                    ["mins"] = $"{JkObjects.Num(-horiz / kk)} {JkObjects.Num(-horiz / kk)} {JkObjects.Num(oldMinZ)}",
                    ["maxs"] = $"{JkObjects.Num(horiz / kk)} {JkObjects.Num(horiz / kk)} {JkObjects.Num(max.Z / kk)}",
                };
                if (kk > 1)
                {
                    // SP_misc_model_breakable monte l'origine de (mins.z - mins.z * echelle) quand un modelscale est donne : on le compense.
                    ent["modelscale"] = md3.ModelScale.ToString(CultureInfo.InvariantCulture);
                    ent["origin"] = Vec(origin - new Vector3(0, 0, oldMinZ - oldMinZ * kk));
                }
                entities.Add(ent);
                animated++;
                continue;
            }

            if (miscModels >= PlaceableConverter.MaxMiscModels) { miscLimit++; continue; }
            var stat = new Dictionary<string, string>
            {
                ["classname"] = "misc_model_static",
                ["model"] = md3.Asset.Path,
                ["origin"] = Vec(origin),
                ["angles"] = angles,
            };
            if (md3.ModelScale > 1) stat["modelscale"] = md3.ModelScale.ToString(CultureInfo.InvariantCulture);
            entities.Add(stat);
            miscModels++;
            decor++;
            if (destructible) explosives++;

            // Collision de face (collide=3) : les triangles du modele, sauf s'il bouge, s'anime, tourne (ventilateur : traversable) ou se detruit.
            bool moves = (Float(r, "numframes") ?? Num(t.Params, "numframes")) > 1;
            if (!isItem && !destructible && !moves && !isSpinner && Int(r, "collide") == 3)
            {
                IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> tris = pool.CollisionTriangles(md3);
                if (tris.Count == 0) continue;
                if (tris.Count > MaxTrianglesPerModel || collisionTriangles + tris.Count > MaxTrianglesPerLevel) { collisionCapped++; continue; }
                Matrix4x4 rot = ThreeDoModel.Rotation(pyr);
                Vector3 P(Vector3 v) => Vector3.Transform(v * md3.ModelScale, rot) + origin;
                foreach (var (a, b, c) in tris) solid.Add((P(a), P(b), P(c)));
                collisionTriangles += tris.Count;
                collided++;
            }
        }

        // ---- departs et drapeaux ----
        EmitStarts(prepared.Kind, players, flags, k, entities, log);
        foreach (var (red, flag) in flags)
            entities.Add(new Dictionary<string, string>
            {
                ["classname"] = red ? "team_CTF_redflag" : "team_CTF_blueflag",
                ["origin"] = Vec(flag.Position * k),
                ["angle"] = JkObjects.Num(JkAngles.StartAngle(flag.PitchYawRoll.Y)),
            });

        // ---- journal ----
        if (actors > 0) log.Warn("JK_ACTOR_SKIPPED", $"{actors} actor(s) (enemies, NPCs and bosses) are not placed: this version has no enemies.");
        if (noEquivalent.Count > 0)
            log.Warn("JK_ITEM_NO_EQUIVALENT", $"{noEquivalent.Values.Sum()} item(s) have no Jedi Academy equivalent and are placed as models only: "
                + string.Join(", ", noEquivalent.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key)) + ".");
        if (explosives > 0) log.Warn("JK_EXPLOSIVE_STATIC", $"{explosives} explosive or breakable object(s) (barrels, tanks, crates) are placed as static models: they cannot be destroyed and have no collision.");
        if (particles > 0) log.Warn("JK_PARTICLE_SKIPPED", $"{particles} particle thing(s) are not converted (none exists in the 34 shipped levels).");
        if (sprites > 0) log.Warn("JK_SPRITE_SKIPPED", $"{sprites} sprite thing(s) are not converted (none exists in the 34 shipped levels).");
        if (miscLimit > 0) log.Warn("JK_MISC_MODEL_LIMIT", $"{miscLimit} model(s) are not placed: the engine accepts at most {PlaceableConverter.MaxMiscModels} misc_model_static.");
        if (collided + collisionCapped > 0)
            log.Warn("JK_DECOR_COLLISION", $"{collided} solid decor thing(s) (collide=3) get face collision from their model ({collisionTriangles} triangles)"
                + (collisionCapped > 0 ? $"; {collisionCapped} too detailed or over the level budget keep no collision." : "."));
        RuntimeThings(level, Script, attachedScripts, log);
        log.Warn("JK_OBJECTS", $"{level.Things.Count} thing(s): {pickups} pickup(s), {decor} static model(s), {animated} looping animation(s), {spinning} spinner(s), {lights} light(s), "
            + $"{players.Count} start(s), {flags.Count} flag(s); not placed: {actors} actor(s), {ghosts + invisible} invisible marker(s)"
            + (approxRotation > 0 ? $"; {approxRotation} approximated rotation(s)" : "") + ".");

        return new ConvertedEntities(entities, [.. pool.Assets.Values], solid)
        {
            TextureData = pool.Textures,
            ModelShaders = [.. pool.Textures.Select(kv => Md3ShaderWriter.Write(KotorGame.JediKnight, kv.Key, kv.Value))],
        };
    }

    private static void EmitStarts(JkLevelKind kind, List<(JklThing Thing, bool Multi)> players, List<(bool Red, JklThing Thing)> flags, float k,
        List<IReadOnlyDictionary<string, string>> entities, ImportLog log)
    {
        if (players.Count == 0) return;
        // Le depart du solo n'a pas le bit multijoueur ; a defaut, le premier thing.
        int primary = players.FindIndex(p => !p.Multi);
        if (primary < 0) primary = 0;
        Dictionary<string, string> Start(string classname, JklThing t) => new()
        {
            ["classname"] = classname, ["origin"] = Vec(t.Position * k), ["angle"] = JkObjects.Num(JkAngles.StartAngle(t.PitchYawRoll.Y)),
        };
        entities.Add(Start("info_player_start", players[primary].Thing));
        int skipped = 0, red = 0, blue = 0, dm = 0;
        JklThing? redFlag = flags.FirstOrDefault(f => f.Red).Thing, blueFlag = flags.FirstOrDefault(f => !f.Red).Thing;
        for (int i = 0; i < players.Count; i++)
        {
            if (i == primary) continue;
            JklThing t = players[i].Thing;
            switch (kind)
            {
                case JkLevelKind.Multiplayer:
                    entities.Add(Start("info_player_deathmatch", t)); dm++;
                    break;
                case JkLevelKind.Ctf when redFlag is not null && blueFlag is not null:
                    // Equipe du drapeau le plus proche (dans le plan), egalite : rouge.
                    bool isRed = Distance2D(t.Position, redFlag.Position) <= Distance2D(t.Position, blueFlag.Position);
                    entities.Add(Start(isRed ? "team_CTF_redplayer" : "team_CTF_blueplayer", t));
                    if (isRed) red++; else blue++;
                    break;
                case JkLevelKind.Ctf:
                    entities.Add(Start("info_player_deathmatch", t)); dm++;
                    break;
                default:
                    skipped++;
                    break;
            }
        }
        if (skipped > 0)
            log.Warn("JK_START_MULTI_SKIPPED", $"{skipped} multiplayer start(s) of this single player level are not placed (SWGL picks a random info_player_deathmatch; the player starts at the single player start).");
        if (kind == JkLevelKind.Ctf && red + blue > 0)
            log.Warn("JK_CTF_TEAMS", $"CTF starts: {red} red and {blue} blue (team of the nearest flag). SWGL solo has no CTF entities (team_CTF_* print an unknown spawn function message); the player starts at the first start.");
    }

    private static float Distance2D(Vector3 a, Vector3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ---------------------------------------------------------------- rotation

    /// <summary>
    /// Rotation continue d'un thing : <c>PureRotation</c> du .key, sinon <c>angvel</c> du gabarit. Rend le drapeau de <c>func_rotating</c> (4 : roulis JA = tangage JK, autour de X ;
    /// 8 : tangage JA = roulis JK, autour de Y ; 0 : lacet, autour de Z), la vitesse signee (degres par seconde) et si l'axe et le centre sont reproduits exactement.
    /// Les angles JA s'ajoutent par composante : le roulis JA est la rotation la plus interne, donc la rotation autour de X du modele est exacte quelle que soit l'orientation de base.
    /// </summary>
    private static (int Flag, float Speed, bool Exact)? SpinOf(JkMd3 md3, IReadOnlyDictionary<string, string> r)
    {
        if (md3.PureRotation)
        {
            Vector3 a = md3.RotationAxis;
            float ax = MathF.Abs(a.X), ay = MathF.Abs(a.Y), az = MathF.Abs(a.Z), top = Math.Max(ax, Math.Max(ay, az));
            if (top < 1e-6f) return null;
            bool exact = md3.RotationCenter.LengthSquared() < 1e-6f && (ax + ay + az - top) < 0.02f * top;
            if (ax == top) return (SpawnXAxis, md3.DegreesPerSecond * MathF.Sign(a.X), exact);
            if (ay == top) return (SpawnYAxis, md3.DegreesPerSecond * MathF.Sign(a.Y), exact);
            return (0, md3.DegreesPerSecond * MathF.Sign(a.Z), exact);
        }
        if (!r.TryGetValue("angvel", out string? text)) return null;
        Vector3 v = JklReader.ParseTriple(text); // (tangage, lacet, roulis) en degres par seconde
        float px = MathF.Abs(v.X), py = MathF.Abs(v.Y), pz = MathF.Abs(v.Z), best = Math.Max(px, Math.Max(py, pz));
        if (best < 0.01f) return null;
        bool single = (px + py + pz - best) < 0.02f * best;
        if (px == best) return (SpawnXAxis, v.X, single);
        if (pz == best) return (SpawnYAxis, v.Z, single);
        return (0, v.Y, single);
    }

    // ---------------------------------------------------------------- aides

    private static string? ModelOf(IReadOnlyDictionary<string, string> r) =>
        r.TryGetValue("model3d", out string? m) && !string.IsNullOrWhiteSpace(m) && !m.Equals("none", StringComparison.OrdinalIgnoreCase) ? m : null;

    /// <summary>Vrai si le COG attache au gabarit gere le message <c>damaged</c> (baril, caisse, reservoir : detruit par les tirs, JK l'explose).</summary>
    private static bool IsDestructible(IReadOnlyDictionary<string, string> r, Func<string, CogScript?> script, SortedSet<string> attached)
    {
        if (!r.TryGetValue("cog", out string? file) || string.IsNullOrWhiteSpace(file)) return false;
        attached.Add(file);
        return script(file) is { } cog && cog.HasMessage("damaged");
    }

    /// <summary>Scripts qui creent des things en jeu (<c>CreateThing</c>) : instances du niveau et COG attaches aux decors.</summary>
    private static void RuntimeThings(JklLevel level, Func<string, CogScript?> script, SortedSet<string> attached, ImportLog log)
    {
        var creators = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in level.Cogs.Select(c => c.CogFile).Concat(attached).Distinct(StringComparer.OrdinalIgnoreCase))
            if (script(file) is { } cog && cog.AllCalls().Any(c => c.Function.Equals("CreateThing", StringComparison.OrdinalIgnoreCase)))
                creators.Add(file);
        if (creators.Count > 0)
            log.Warn("JK_RUNTIME_THING", $"{creators.Count} COG script(s) create things while the game runs (explosions, debris, bonuses, enemies): those things are not placed. Scripts: "
                + string.Join(", ", creators.Take(12)) + (creators.Count > 12 ? ", ..." : "."));
    }

    private static uint Flags(JklThing t, IReadOnlyDictionary<string, string> r)
    {
        string? text = t.Params.TryGetValue("thingflags", out string? own) ? own : r.GetValueOrDefault("thingflags");
        if (string.IsNullOrWhiteSpace(text)) return 0;
        try { return JkTokenizer.ParseFlags(text); }
        catch (FormatException) { return 0; }
        catch (OverflowException) { return 0; }
    }

    private static float? Float(IReadOnlyDictionary<string, string> r, string key) =>
        r.TryGetValue(key, out string? s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f) ? f : null;

    private static float Num(IReadOnlyDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out string? s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f) ? f : 0f;

    private static int Int(IReadOnlyDictionary<string, string> r, string key) =>
        r.TryGetValue(key, out string? s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : -1;

    private static string Vec(Vector3 v) => $"{JkObjects.Num(v.X)} {JkObjects.Num(v.Y)} {JkObjects.Num(v.Z)}";
}

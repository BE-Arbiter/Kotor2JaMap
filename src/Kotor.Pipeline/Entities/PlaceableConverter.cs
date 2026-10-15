using System.Globalization;
using System.Numerics;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Mdl;
using Kotor.Core.Models;
using Kotor.Core.Textures;
using Kotor.Core.Walkmesh;

namespace Kotor.Pipeline.Entities;

/// <summary>Acces aux ressources d'un objet (modele, collision, texture). Separe pour tester sans archives binaires.</summary>
internal interface IObjectAssets
{
    /// <summary>Resref du modele de l'entite, ou null avec la raison.</summary>
    string? ModelOf(ModuleEntity e, out string? reason);
    KotorModel? LoadModel(string model, out string? reason);
    /// <summary>Collision PWK du modele (repere du modele) ; null avec la raison si absente ou illisible.</summary>
    Walkmesh? LoadPwk(string model, out string? reason);
    KotorTexture? LoadTexture(string name, ImportLog log);
}

/// <summary>
/// Objets places (<c>Placeable List</c>) -> <c>misc_model_static</c> + MD3 + collision PWK.
/// Entite : <c>model</c>, <c>origin</c> (unites JA), <c>angles "0 yaw 0"</c>, <c>modelscale</c> si le modele a du etre reduit.
/// Cles lues par le moteur : <c>modelscale</c>, <c>modelscale_vec</c>, <c>zoffset</c> (OpenJK-SWGL code/game/g_misc.cpp:408-417),
/// <c>model</c>, <c>origin</c> et <c>angles</c>/<c>angle</c> (g_spawn.cpp:265-266), puis CG_CreateMiscEntFromGent (code/cgame/cg_main.cpp:2192-2218).
/// </summary>
public static class PlaceableConverter
{
    /// <summary>MAX_MISC_ENTS du moteur (code/cgame/cg_main.cpp:2176) : au-dela, le chargement de la carte echoue.</summary>
    public const int MaxMiscModels = 2000;
    /// <summary>Un MD3 plafonne ses coordonnees a 511.98 ; on reduit a partir de 511.</summary>
    public const float ModelLimit = 511f;

    public static ConvertedEntities Convert(ResourceResolver res, KotorGame game, IReadOnlyList<ModuleEntity> entities, float scale, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        return Convert(new ResolverAssets(res), game, entities, scale, log);
    }

    internal static ConvertedEntities Convert(IObjectAssets assets, KotorGame game, IReadOnlyList<ModuleEntity> entities, float scale, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(log);
        string prefix = Md3ShaderWriter.ModelPrefix(game);
        var models = new Dictionary<string, ModelInfo>(StringComparer.Ordinal);
        var outEntities = new List<IReadOnlyDictionary<string, string>>();
        var solid = new List<(Vector3, Vector3, Vector3)>();
        int converted = 0, skipped = 0, miscEntities = 0;

        foreach (ModuleEntity e in entities)
        {
            if (e.Kind != EntityKind.Placeable) continue;
            string? model = e.Model ?? assets.ModelOf(e, out string? why0) ?? Skip(e, why0 ?? "no model", log, ref skipped);
            if (model is null) continue;
            if (!models.TryGetValue(model, out ModelInfo? info))
                models[model] = info = BuildModel(assets, model, game, scale, prefix, log);
            if (info.Assets.Count == 0) { Skip(e, info.Failure ?? "model not converted", log, ref skipped); continue; }
            if (miscEntities + info.Assets.Count > MaxMiscModels)
            {
                Skip(e, $"the engine accepts at most {MaxMiscModels} misc_model_static", log, ref skipped);
                continue;
            }

            float yaw = NormalizeDegrees(e.Yaw);
            // Un modele reparti sur plusieurs MD3 : une entite par fichier, meme origine, memes angles, meme modelscale.
            foreach (Md3Asset asset in info.Assets)
            {
                var ent = new Dictionary<string, string>
                {
                    ["classname"] = "misc_model_static",
                    ["model"] = asset.Path,
                    ["origin"] = $"{Num(e.Position.X * scale)} {Num(e.Position.Y * scale)} {Num(e.Position.Z * scale)}",
                    // Meme repere que KOTOR : les sommets du MD3 sont ceux du MDL et AnglesToAxis tourne de yaw autour de Z (anti-horaire),
                    // comme le Bearing du GIT. Pas de +90 (voir le rapport de l'etape 03).
                    ["angles"] = $"0 {Num(yaw)} 0",
                };
                if (info.DivideBy > 1) ent["modelscale"] = info.DivideBy.ToString(CultureInfo.InvariantCulture);
                outEntities.Add(ent);
                miscEntities++;
            }
            converted++;

            if (info.Pwk is { } pwk)
            {
                float rad = e.Yaw * MathF.PI / 180f, c = MathF.Cos(rad), s = MathF.Sin(rad);
                Vector3 Place(Vector3 v) => (new Vector3(v.X * c - v.Y * s, v.X * s + v.Y * c, v.Z) + e.Position) * scale;
                for (int t = 0; t + 2 < pwk.Triangles.Length; t += 3)
                    solid.Add((Place(pwk.Vertices[pwk.Triangles[t]]), Place(pwk.Vertices[pwk.Triangles[t + 1]]), Place(pwk.Vertices[pwk.Triangles[t + 2]])));
            }
        }

        var assetsOut = models.Values.SelectMany(m => m.Assets).OrderBy(a => a.Path, StringComparer.Ordinal).ToList();

        // Textures et shaders des modeles, une fois par texture.
        var textureData = new SortedDictionary<string, KotorTexture?>(StringComparer.Ordinal);
        foreach (string name in assetsOut.SelectMany(a => a.Textures).Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal))
            textureData[name] = assets.LoadTexture(name, log);
        var shaders = textureData.Select(kv => Md3ShaderWriter.Write(game, kv.Key, kv.Value)).ToList();

        if (entities.Any(e => e.Kind == EntityKind.Placeable))
            log.Warn("PLACEABLES", $"{converted} placeable(s) converted, {skipped} skipped, {assetsOut.Count} unique MD3 ({models.Values.Count(m => m.Assets.Count > 0)} model(s)).");
        return new ConvertedEntities(outEntities, assetsOut, solid)
        {
            TextureData = textureData,
            ModelShaders = shaders,
        };
    }

    private static string? Skip(ModuleEntity e, string reason, ImportLog log, ref int skipped)
    {
        skipped++;
        log.Warn("PLACEABLE_SKIPPED", $"Placeable '{e.Name}' (template '{e.Template}') at ({Num(e.Position.X)}, {Num(e.Position.Y)}, {Num(e.Position.Z)}): {reason}; skipped.");
        return null;
    }

    /// <summary>Resultat de la conversion d'un modele. <c>Min</c>/<c>Max</c> : boite englobante des maillages rendus, en metres KOTOR (repere du modele).</summary>
    internal sealed record ModelInfo(IReadOnlyList<Md3Asset> Assets, int DivideBy, Walkmesh? Pwk, string? Failure, Vector3 Min = default, Vector3 Max = default);

    internal static ModelInfo BuildModel(IObjectAssets assets, string model, KotorGame game, float scale, string prefix, ImportLog log, bool loadPwk = true)
    {
        KotorModel? mdl = assets.LoadModel(model, out string? reason);
        if (mdl is null) return Fail(reason ?? $"model '{model}' not found");
        IReadOnlyList<ModelPart> parts = ModelMeshBuilder.Build(mdl, log, model);
        if (parts.Count == 0) return Fail($"model '{model}' has no rendered mesh (invisible blocker, collision not added)");

        Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
        foreach (ModelPart p in parts)
            foreach (Vector3 v in p.Positions) { bmin = Vector3.Min(bmin, v); bmax = Vector3.Max(bmax, v); }
        float max = 0;
        foreach (ModelPart p in parts)
            foreach (Vector3 v in p.Positions)
                max = MathF.Max(max, MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z))));
        max *= scale;
        if (!float.IsFinite(max)) return Fail($"model '{model}' has non-finite coordinates");
        int k = max > ModelLimit ? (int)MathF.Ceiling(max / ModelLimit) : 1;
        float f = scale / k;
        var ja = parts.Select(p => p with { Positions = [.. p.Positions.Select(v => v * f)] }).ToList();
        IReadOnlyList<Md3Writer.Md3File> files;
        try { files = Md3Writer.WriteSplit(model, ja, prefix); }
        catch (ArgumentException ex) { return Fail($"MD3 not written ({ex.Message})"); }

        string? pwkReason = null;
        Walkmesh? pwk = loadPwk ? assets.LoadPwk(model, out pwkReason) : null;
        if (loadPwk && pwk is null) log.Warn("PLACEABLE_NO_PWK", $"Model '{model}': {pwkReason ?? "no PWK"}; the placeable has no collision.");
        // Un fichier : models/<dossier>/<modele>.md3 ; plusieurs : <modele>_p0.md3, <modele>_p1.md3...
        var list = files.Select((f, i) => new Md3Asset(files.Count == 1 ? $"{prefix}{model}.md3" : $"{prefix}{model}_p{i}.md3", f.Data, f.Textures)).ToList();
        return new ModelInfo(list, k, pwk, null, bmin, bmax);

        ModelInfo Fail(string why) => new([], 1, null, why);
    }

    private static float NormalizeDegrees(float deg)
    {
        deg %= 360f;
        if (deg < 0) deg += 360f;
        return deg >= 360f ? 0f : deg;
    }

    internal static string Num(float v) => (MathF.Abs(v) < 0.0005f ? 0f : v).ToString("0.###", CultureInfo.InvariantCulture); // evite "-0"

    /// <summary>Acces reel : archives du jeu.</summary>
    internal sealed class ResolverAssets(ResourceResolver res) : IObjectAssets
    {
        private readonly ObjectModelResolver _models = new(res);

        public string? ModelOf(ModuleEntity e, out string? reason) =>
            e.Kind == EntityKind.Door ? _models.ResolveDoor(e.Template, out reason) : _models.ResolvePlaceable(e.Template, out reason);

        public KotorModel? LoadModel(string model, out string? reason)
        {
            reason = null;
            byte[]? mdl = res.Find(model, ResourceType.Mdl), mdx = res.Find(model, ResourceType.Mdx);
            if (mdl is null || mdx is null) { reason = $"{(mdl is null ? "MDL" : "MDX")} '{model}' not found"; return null; }
            try { return MdlReader.Read(mdl, mdx); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or OverflowException
                or EndOfStreamException or IndexOutOfRangeException)
            { reason = $"model '{model}' unreadable ({ex.Message})"; return null; }
        }

        public Walkmesh? LoadPwk(string model, out string? reason)
        {
            reason = null;
            byte[]? data = res.Find(model, ResourceType.Pwk);
            if (data is null) { reason = "PWK not found"; return null; }
            try { return WokReader.Read(data); }
            catch (InvalidDataException ex) { reason = $"PWK unreadable ({ex.Message})"; return null; }
        }

        public KotorTexture? LoadTexture(string name, ImportLog log)
        {
            try
            {
                KotorTexture? t = TextureLookup.Find(res, name, TextureKind.Diffuse);
                if (t is null) log.Warn("TEXTURE_MISSING", $"{name} (model texture): not found, replaced by the missing-texture checker.");
                else if (t.Txi.EnvMapTexture is not null) log.Warn("TEXTURE_ENVMAP", $"{name}: reflection '{t.Txi.EnvMapTexture}' not converted.");
                return t;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                log.Warn("TEXTURE_UNSUPPORTED", $"{name}: {ex.Message}");
                return null;
            }
        }
    }
}

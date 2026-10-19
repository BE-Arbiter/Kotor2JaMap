using System.Globalization;
using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Models;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Entities;

/// <summary>
/// Modeles places de la scene (<see cref="ModuleScene.Models"/>, SWTOR) -> un MD3 par modele + un <c>misc_model_static</c> par instance retenue.
/// Le moteur plafonne ces entites (<see cref="MiscModelBudget.EngineMaxEntities"/>) : les instances qui retirent le plus de triangles
/// par entite sortent du BSP, les autres y sont remises (<see cref="SceneModel.Bake"/>).
/// Shaders des MD3 : ceux du BSP avec <c>rgbGen lightingDiffuse</c> au lieu de <c>rgbGen vertex</c> (un MD3 n'a pas de couleur de sommet).
/// </summary>
public static class StaticModelConverter
{
    /// <summary>Triangles au moins par entite consommee : en dessous, l'instance reste dans le BSP.</summary>
    public const long MinTrianglesPerEntity = 200;

    /// <summary>
    /// Entites, MD3, shaders et textures des instances retenues, et la scene a compiler : instances non retenues remises dans
    /// <see cref="ModuleScene.Meshes"/>, plus aucune instance.
    /// </summary>
    public static (ConvertedEntities Converted, ModuleScene Scene) Convert(ModuleScene scene, string mapName, ExportOptions opt, ImportLog log,
        int budget = MiscModelBudget.EngineMaxEntities)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        if (scene.ModelInstances.Count == 0) return (ConvertedEntities.Empty, scene);
        float scale = opt.Scale;
        string dir = "models/" + KotorGames.AssetFolder(opt.Game) + "/" + mapName + "/";

        // Un MD3 par modele utilise.
        var built = new Dictionary<int, BuiltModel?>();
        foreach (int mi in scene.ModelInstances.Select(i => i.Model).Distinct().Order())
            built[mi] = Build(scene, scene.Models[mi], dir + "m" + mi.ToString(CultureInfo.InvariantCulture), scale, opt.Game, log);

        var candidates = new List<MiscModelCandidate>();
        for (int i = 0; i < scene.ModelInstances.Count; i++)
        {
            SceneModelInstance inst = scene.ModelInstances[i];
            if (built[inst.Model] is { } b) candidates.Add(new MiscModelCandidate(i, scene.Models[inst.Model].Name, scene.Models[inst.Model].Triangles, b.Files.Count));
        }
        IReadOnlySet<int> chosen = MiscModelBudget.Select(candidates, budget, MinTrianglesPerEntity);

        var entities = new List<IReadOnlyDictionary<string, string>>();
        var baked = new List<SceneMesh>();
        var usedModels = new Dictionary<int, List<SceneModelInstance>>();
        long outTriangles = 0, bakedTriangles = 0;
        for (int i = 0; i < scene.ModelInstances.Count; i++)
        {
            SceneModelInstance inst = scene.ModelInstances[i];
            SceneModel model = scene.Models[inst.Model];
            MiscModelPlacement? placement = null;
            if (chosen.Contains(i))
            {
                // Sommets du MD3 : (modele - centre) x echelle ; origine = centre place. L'instance n'a qu'une rotation (echelle dans le modele).
                Matrix4x4 m = inst.Transform;
                m.Translation *= scale;
                placement = MiscModelTransform.FromMatrix(m);
                if (placement is not null && built[inst.Model] is { } bc)
                    placement = Vector3.Distance(placement.Scale, Vector3.One) > 1e-3f ? null
                        : placement with { Origin = Vector3.Transform(bc.Center, inst.Transform) * scale, Scale = Vector3.One };
            }
            if (placement is null || built[inst.Model] is not { } b)
            {
                baked.AddRange(model.Bake(inst.Transform, inst.Room));
                bakedTriangles += model.Triangles;
                continue;
            }
            if (!usedModels.TryGetValue(inst.Model, out var placed)) usedModels[inst.Model] = placed = [];
            placed.Add(inst);
            outTriangles += model.Triangles;
            foreach (string path in b.Files.Select(f => f.Path))
            {
                var ent = new Dictionary<string, string>
                {
                    ["classname"] = "misc_model_static",
                    ["model"] = path,
                    ["origin"] = placement.OriginKey,
                    ["angles"] = placement.AnglesKey,
                };
                entities.Add(ent);
            }
        }

        var models = usedModels.Keys.Order().SelectMany(mi => built[mi]!.Files).OrderBy(a => a.Path, StringComparer.Ordinal).ToList();
        // Shaders du BSP eclaires par le moteur comme les placeables KOTOR (rgbGen lightingDiffuse) : un MD3 n'a pas de couleur de sommet.
        var shaders = usedModels.Keys.SelectMany(mi => built[mi]!.Shaders)
            .Select(s => ShaderPlanner.Write(s.Planned with { Name = s.Name }, opt.Game).Replace("rgbGen vertex", "rgbGen lightingDiffuse", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var textures = new SortedDictionary<string, KotorTexture?>(StringComparer.Ordinal);
        foreach (int mi in usedModels.Keys)
            foreach (var (name, tex) in built[mi]!.Textures) textures.TryAdd(name, tex);

        log.Warn("STATIC_MODELS", $"{entities.Count:N0} misc_model_static (max {budget:N0}) for {chosen.Count:N0} of {scene.ModelInstances.Count:N0} placed model instance(s), "
            + $"{models.Count:N0} MD3: {outTriangles:N0} triangle(s) moved out of the BSP, {bakedTriangles:N0} kept in it. "
            + "Engine draw distance for misc_model_static: about 8192 units plus the model size.");
        var converted = new ConvertedEntities(entities, models, []) { TextureData = textures, ModelShaders = shaders };
        return (converted, scene with { Meshes = [.. scene.Meshes, .. baked], ModelInstances = [] });
    }

    private sealed record BuiltModel(IReadOnlyList<Md3Asset> Files, IReadOnlyList<(PlannedShader Planned, string Name)> Shaders, IReadOnlyList<(string Name, KotorTexture? Texture)> Textures,
        Vector3 Center);

    private static BuiltModel? Build(ModuleScene scene, SceneModel model, string name, float scale, KotorGame game, ImportLog log)
    {
        var parts = new List<ModelPart>();
        var shaders = new List<(PlannedShader, string)>();
        var textures = new List<(string, KotorTexture?)>();
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (SceneMesh src in model.Meshes)
        {
            if (src.Triangles.Length < 3 || src.Positions.Length == 0) continue;
            scene.Textures.TryGetValue(src.Texture, out KotorTexture? tex);
            SceneMesh mesh = (tex is null ? src with { Texture = ShaderNames.MissingTexture } : src) with { Lightmap = null, Uv1 = null };
            PlannedShader planned = ShaderPlanner.Plan(mesh, tex, game);
            // models/<dossier>/[<type>/]<texture> : le dernier segment est la texture, comme pour les placeables (l apercu s en sert).
            string shaderName = Md3ShaderWriter.ModelPrefix(game) + (planned.Kind == ShaderKind.Unlit ? "" : planned.Kind.ToString().ToLowerInvariant() + "/") + planned.Texture;
            // Nom de shader MD3 : 63 caracteres au plus. Au-dela, fin remplacee par 8 hex du SHA-256 du nom complet.
            if (shaderName.Length > 63)
                shaderName = shaderName[..54] + "_" + System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(shaderName)), 0, 4).ToLowerInvariant();
            shaders.Add((planned, shaderName));
            textures.Add((planned.Texture, tex));
            var pos = new Vector3[mesh.Positions.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = mesh.Positions[i] * scale;
                min = Vector3.Min(min, pos[i]); max = Vector3.Max(max, pos[i]);
            }
            Vector3[] nrm = mesh.Normals.Length == pos.Length ? mesh.Normals : [.. pos.Select(_ => Vector3.UnitZ)];
            Vector2[] uv = mesh.Uv0.Length == pos.Length ? mesh.Uv0 : new Vector2[pos.Length];
            parts.Add(new ModelPart(shaderName, pos, nrm, uv, mesh.Triangles));
        }
        if (parts.Count == 0) return null;

        // Coordonnees MD3 bornees a +-511.98 : sommets centres sur le milieu du modele (origine de l'entite). Plus grand : reste dans le BSP,
        // car le reduire demanderait un modelscale, qui fausse l'eclairage.
        Vector3 center = (min + max) * 0.5f;
        Vector3 half = (max - min) * 0.5f;
        if (half.Max() > Md3Writer.MaxCoordinate - 1f)
        {
            log.Warn("STATIC_MODEL_TOO_BIG", $"{model.Name}: {2 * half.Max():0} units across, more than an MD3 holds without scaling; its instances stay in the BSP.");
            return null;
        }
        parts = [.. parts.Select(p => p with { Positions = [.. p.Positions.Select(v => v - center)] })];
        try
        {
            IReadOnlyList<Md3Writer.Md3File> files = Md3Writer.WriteSplit(name, parts, "");
            var assets = files.Select((f, i) => new Md3Asset((files.Count == 1 ? name : $"{name}_p{i}") + ".md3", f.Data, f.Textures)).ToList();
            return new BuiltModel(assets, shaders, textures, center / scale);
        }
        catch (ArgumentException ex)
        {
            log.Warn("STATIC_MODEL_FAILED", $"{model.Name}: MD3 not written ({ex.Message}); its instances stay in the BSP.");
            return null;
        }
    }
}

file static class VectorExtensions
{
    public static float Max(this Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
}

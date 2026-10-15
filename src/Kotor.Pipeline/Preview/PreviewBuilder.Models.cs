using System.Globalization;
using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Pipeline.Entities;

namespace Kotor.Pipeline.Preview;

/// <summary>Modeles MD3 des vues Importer et Exporter : lots, textures et instances posees.</summary>
public static partial class PreviewBuilder
{
    /// <summary>Prefixe des cles de texture de modele dans <see cref="PreviewModel.Textures"/> (evite tout choc avec les textures du BSP).</summary>
    public const string ModelTexturePrefix = "md3:";

    public static string ModelTextureKey(string textureName) => ModelTexturePrefix + textureName;

    /// <summary>Nom de texture d'un shader de modele (<c>models/kotor/&lt;texture&gt;</c>).</summary>
    private static string TextureOf(string shader) => shader[(shader.LastIndexOf('/') + 1)..];

    /// <summary>Un lot par surface. Texture absente de <paramref name="textures"/> : damier. Cutout : double face, comme le shader du moteur.</summary>
    private static IReadOnlyList<PreviewBatch> ModelBatches(IReadOnlyList<Md3Surface> surfaces,
        IReadOnlyDictionary<string, PreviewTexture> textures, Func<string, bool> isCutout)
    {
        var batches = new List<PreviewBatch>(surfaces.Count);
        foreach (Md3Surface s in surfaces)
        {
            if (s.Indices.Length == 0) continue;
            string tex = TextureOf(s.Shader);
            string key = ModelTextureKey(tex);
            string diffuse = textures.ContainsKey(key) ? key : Key;
            bool cut = isCutout(tex);
            var verts = new PreviewVertex[s.Positions.Length];
            for (int i = 0; i < verts.Length; i++) verts[i] = new PreviewVertex(s.Positions[i], s.Normals[i], s.Uv[i], Vector2.Zero);
            batches.Add(new PreviewBatch(s.Name, diffuse, null, cut ? PreviewBlend.Cutout : PreviewBlend.Opaque, cut, verts, s.Indices));
        }
        return batches;
    }

    /// <summary>
    /// Instances posees d'apres des entites JA : <c>misc_model_static</c> (<c>model</c>, <c>angles</c>) et <c>func_door</c> (<c>model2</c>,
    /// <c>modelAngles</c>), avec <c>origin</c> et <c>modelscale</c>. Une entite dont le modele n'est pas <paramref name="known"/> est ignoree.
    /// Les portes sont dessinees fermees, a leur origine.
    /// </summary>
    private static List<PreviewModelInstance> InstancesOf(IEnumerable<IReadOnlyDictionary<string, string>> entities, Func<string, bool> known)
    {
        var list = new List<PreviewModelInstance>();
        foreach (var e in entities)
        {
            if (!e.TryGetValue("classname", out string? cls)) continue;
            EntityKind kind;
            string? model;
            string anglesKey;
            if (cls == "misc_model_static") { kind = EntityKind.Placeable; anglesKey = "angles"; e.TryGetValue("model", out model); }
            else if (cls == "func_door") { kind = EntityKind.Door; anglesKey = "modelAngles"; e.TryGetValue("model2", out model); }
            else continue;
            if (model is null || !known(model)) continue;
            float[]? origin = Floats(e, "origin", 3);
            if (origin is null) continue;
            float yaw = Floats(e, anglesKey, 3) is { } a ? a[1] : 0f;
            float scale = e.TryGetValue("modelscale", out string? ms) && float.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f > 0 ? f : 1f;
            Matrix4x4 m = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateRotationZ(yaw * MathF.PI / 180f)
                * Matrix4x4.CreateTranslation(origin[0], origin[1], origin[2]);
            list.Add(new PreviewModelInstance(model, m, kind, Path.GetFileNameWithoutExtension(model)));
        }
        return list;
    }

    private static float[]? Floats(IReadOnlyDictionary<string, string> e, string key, int count)
    {
        if (!e.TryGetValue(key, out string? v)) return null;
        string[] p = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != count) return null;
        var r = new float[count];
        for (int i = 0; i < count; i++)
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out r[i]) || !float.IsFinite(r[i])) return null;
        return r;
    }

    /// <summary>Vue Importer : MD3 des objets et des portes convertis, avec leurs textures (sans lightmap).</summary>
    private static PreviewModel WithConvertedModels(PreviewModel model, Dictionary<string, PreviewTexture> textures, ConvertedEntities converted)
    {
        var cutout = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, tex) in converted.TextureData)
        {
            if (tex is null) continue; // damier
            textures[ModelTextureKey(name)] = ToTexture(tex.Width, tex.Height, tex.Rgba);
            if (Md3ShaderWriter.HasCutout(tex)) cutout.Add(name);
        }
        var models = new Dictionary<string, IReadOnlyList<PreviewBatch>>(StringComparer.Ordinal);
        foreach (Md3Asset asset in converted.Models)
        {
            try { models[asset.Path] = ModelBatches(Md3Reader.Read(asset.Data), textures, cutout.Contains); }
            catch (InvalidDataException) { /* MD3 produit par nous : le modele est simplement absent de la vue */ }
        }
        return model with { Models = models, Instances = InstancesOf(converted.Entities, models.ContainsKey) };
    }

    /// <summary>Retire les reperes d'objets et de portes qui ont un modele dans la vue (meme type, meme position).</summary>
    private static List<PreviewMarker> WithoutModelledMarkers(IReadOnlyList<PreviewMarker> markers, IReadOnlyList<PreviewModelInstance> instances)
    {
        var result = new List<PreviewMarker>(markers.Count);
        foreach (PreviewMarker mk in markers)
        {
            bool covered = mk.Kind is EntityKind.Placeable or EntityKind.Door
                && instances.Any(i => i.Kind == mk.Kind && Vector3.DistanceSquared(i.Transform.Translation, mk.Position) < 1e-2f);
            if (!covered) result.Add(mk);
        }
        return result;
    }

    /// <summary>
    /// Vue Exporter : ajoute au BSP relu les MD3 du staging, poses selon les entites du lump (<c>origin</c>, <c>angles</c>/<c>modelAngles</c>,
    /// <c>modelscale</c>). Textures lues dans le staging ; la transparence vient du fichier de shaders du staging (<c>alphaFunc</c>).
    /// </summary>
    public static PreviewModel WithStagedModels(PreviewModel model, BspFile bsp, string stagingDir, KotorGame game, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentException.ThrowIfNullOrEmpty(stagingDir);
        ArgumentNullException.ThrowIfNull(log);
        var ents = ParseEntities(bsp.Entities);
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in ents)
        {
            if (!e.TryGetValue("classname", out string? cls)) continue;
            if (cls == "misc_model_static" && e.TryGetValue("model", out string? m)) paths.Add(m);
            else if (cls == "func_door" && e.TryGetValue("model2", out string? m2)) paths.Add(m2);
        }
        if (paths.Count == 0) return model;

        string shaderText = "";
        string shaderDir = Path.Combine(stagingDir, "shaders");
        if (Directory.Exists(shaderDir))
            shaderText = string.Join("\n", Directory.GetFiles(shaderDir, "*.shader").Order(StringComparer.Ordinal).Select(File.ReadAllText));

        var textures = new Dictionary<string, PreviewTexture>(model.Textures, StringComparer.Ordinal);
        var models = new Dictionary<string, IReadOnlyList<PreviewBatch>>(StringComparer.Ordinal);
        string texDir = ShaderNames.PrefixFor(game);
        foreach (string path in paths)
        {
            string file = Path.Combine(stagingDir, path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) { log.Warn("PREVIEW_MD3_MISSING", $"{path}: not found in the staging folder; the model is not shown."); continue; }
            IReadOnlyList<Md3Surface> surfaces;
            try { surfaces = Md3Reader.Read(File.ReadAllBytes(file)); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            { log.Warn("PREVIEW_MD3_UNREADABLE", $"{path}: {ex.Message}"); continue; }

            foreach (Md3Surface s in surfaces)
            {
                string tex = TextureOf(s.Shader), key = ModelTextureKey(tex);
                if (textures.ContainsKey(key)) continue;
                string tga = Path.Combine(stagingDir, (texDir + tex + ".tga").Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(tga)) continue; // damier
                try
                {
                    using var fs = File.OpenRead(tga);
                    var (w, h, rgba) = TgaReader.Read(fs);
                    textures[key] = new PreviewTexture(w, h, rgba);
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                { log.Warn("PREVIEW_TEXTURE_UNREADABLE", $"{tga}: {ex.Message}"); }
            }
            models[path] = ModelBatches(surfaces, textures, tex => IsCutoutShader(shaderText, Md3ShaderWriter.ModelPrefix(game) + tex));
        }
        return model with { Textures = textures, Models = models, Instances = InstancesOf(ents, models.ContainsKey) };
    }

    /// <summary>Le bloc du shader <paramref name="name"/> (ecrit par <see cref="Md3ShaderWriter"/>) demande-t-il un test alpha ?</summary>
    private static bool IsCutoutShader(string shaderText, string name)
    {
        int start = shaderText.IndexOf("\n" + name + "\n{", StringComparison.Ordinal);
        if (start < 0) start = shaderText.StartsWith(name + "\n{", StringComparison.Ordinal) ? 0 : -1;
        if (start < 0) return false;
        int end = shaderText.IndexOf("\n}\n", start + 1, StringComparison.Ordinal);
        string block = end < 0 ? shaderText[start..] : shaderText[start..end];
        return block.Contains("alphaFunc", StringComparison.Ordinal);
    }
}

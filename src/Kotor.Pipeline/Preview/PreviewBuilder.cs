using System.Globalization;
using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core.Entities;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Core.Geometry;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Preview;

/// <summary>Construit le <see cref="PreviewModel"/> de chacune des etapes (scene, plan d'import, BSP relu).</summary>
public static class PreviewBuilder
{
    private const string Key = PreviewModel.MissingTextureKey;

    /// <summary>Etape Previsualiser : scene KOTOR (metres, UV en convention GL) multipliee par <paramref name="scale"/>.</summary>
    public static PreviewModel FromScene(ModuleScene scene, float scale)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var textures = NewTextures();
        var batches = new List<PreviewBatch>();
        foreach (SceneMesh src in scene.Meshes)
        {
            if (src.Triangles.Length < 3 || src.Positions.Length == 0) continue;
            scene.Textures.TryGetValue(src.Texture, out KotorTexture? tex);
            SceneMesh mesh = tex is null ? src with { Texture = ShaderNames.MissingTexture } : src;
            KotorTexture? lm = null;
            if (mesh.Lightmap is not null
                && !(scene.Textures.TryGetValue(mesh.Lightmap, out lm) && lm is not null && mesh.Uv1 is not null))
                mesh = mesh with { Lightmap = null };

            PlannedShader planned = ShaderPlanner.Plan(mesh, tex);
            if (tex is not null) textures.TryAdd(planned.Texture, ToTexture(tex.Width, tex.Height, tex.Rgba));
            string? lmKey = null;
            if (planned.Lightmap is not null && lm is not null)
            {
                lmKey = planned.Lightmap;
                textures.TryAdd(lmKey, ToTexture(lm.Width, lm.Height, lm.Rgba));
            }

            var verts = new PreviewVertex[mesh.Positions.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector2 uv0 = i < mesh.Uv0.Length ? FlipV(mesh.Uv0[i]) : Vector2.Zero;
                Vector2 uv1 = lmKey is not null && mesh.Uv1 is not null && i < mesh.Uv1.Length ? FlipV(mesh.Uv1[i]) : Vector2.Zero;
                Vector3 n = i < mesh.Normals.Length ? mesh.Normals[i] : Vector3.UnitZ;
                verts[i] = new PreviewVertex(mesh.Positions[i] * scale, n, uv0, uv1);
            }
            batches.Add(new PreviewBatch($"{mesh.Room}/{mesh.Node}", tex is null ? Key : planned.Texture, lmKey, BlendOf(planned.Kind),
                planned.DoubleSided, verts, mesh.Triangles));
        }

        var collision = new List<Vector3>();
        foreach (SceneWalkmesh w in scene.Walkmeshes)
            foreach (int idx in w.Triangles) collision.Add(w.Positions[idx] * scale);

        Vector3 entry = scene.Info.Entry.Position * scale;
        Vector2 dir = scene.Info.Entry.Direction;
        float yaw = (float)(Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI);
        return Make(batches, textures, [.. collision], [], entry, yaw);
    }

    /// <summary>Etape Importer : le plan d'export (deja a l'echelle JA, UV Q3) plus les reperes d'entites (metres * <paramref name="scale"/>).</summary>
    public static PreviewModel FromImport(ImportStepResult imported, IReadOnlyList<ModuleEntity> entities, float scale,
        float lightmapBrightness = 1f)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(entities);
        ExportPlan plan = imported.Plan;
        var planned = IndexShaders(plan.Shaders);
        var textures = NewTextures();
        foreach (ExportTexture t in plan.Textures)
        {
            if (t.Texture is null) continue; // damier : deja present sous la cle "missing"
            byte[] rgba = t.Lightmap ? ModuleExporter.Brighten(t.Texture.Rgba, lightmapBrightness) : t.Texture.Rgba;
            textures.TryAdd(KeyOf(t), ToTexture(t.Texture.Width, t.Texture.Height, rgba));
        }

        var batches = new List<PreviewBatch>();
        foreach (SurfaceInput s in plan.Input.Surfaces)
        {
            planned.TryGetValue(s.Shader, out PlannedShader? ps);
            string diffuse = ps is null ? Key : textures.ContainsKey(ps.Texture) ? ps.Texture : Key;
            string? lm = ps?.Lightmap is { } l && textures.ContainsKey(l) && s.Uv1 is not null ? l : null;
            var verts = new PreviewVertex[s.Positions.Length];
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new PreviewVertex(s.Positions[i], i < s.Normals.Length ? s.Normals[i] : Vector3.UnitZ,
                    i < s.Uv0.Length ? s.Uv0[i] : Vector2.Zero, lm is not null && i < s.Uv1!.Length ? s.Uv1[i] : Vector2.Zero);
            batches.Add(new PreviewBatch(s.Shader, diffuse, lm, ps is null ? PreviewBlend.Opaque : BlendOf(ps.Kind), s.DoubleSided,
                verts, s.Indices));
        }

        var collision = new List<Vector3>();
        foreach (SubMesh sm in plan.Input.Walkable.SubMeshes)
            foreach (int idx in sm.Triangles) collision.Add(sm.Positions[idx]);
        foreach (SubMesh sm in plan.Input.Solid.SubMeshes.Where(m => m.MaterialName == "walkmesh_solid"))
            foreach (int idx in sm.Triangles) collision.Add(sm.Positions[idx]);

        var markers = new List<PreviewMarker>(entities.Count);
        foreach (ModuleEntity e in entities)
            markers.Add(new PreviewMarker(e.Kind, e.Name, e.Position * scale, e.Yaw, e.Polygon?.Select(p => p * scale).ToArray()));

        (Vector3? spawn, float yaw) = SpawnOf(plan.Input.Entities.Select(d => d).ToList());
        return Make(batches, textures, [.. collision], markers, spawn, yaw);
    }

    /// <summary>
    /// Etape Exporter : un lot par surface du BSP relu, boite de ciel comprise (lot <see cref="PreviewBatch.IsSky"/> : l'application le dessine ou non). La lightmap est retrouvee par le nom du shader
    /// (<c>textures/kotor/&lt;tex&gt;__&lt;lm&gt;</c>). <paramref name="shaders"/> (facultatif) donne le melange et le double face.
    /// </summary>
    public static PreviewModel FromBsp(BspFile bsp, IReadOnlyDictionary<string, PreviewTexture> textures,
        IEnumerable<PlannedShader>? shaders = null)
    {
        ArgumentNullException.ThrowIfNull(bsp);
        ArgumentNullException.ThrowIfNull(textures);
        var tex = NewTextures();
        foreach (var (k, v) in textures) tex[k] = v;
        var planned = IndexShaders(shaders ?? []);

        var batches = new List<PreviewBatch>();
        foreach (BspSurface s in bsp.Surfaces)
        {
            bool isSky = s.Shader == JaDefaults.SkyShader;
            string name = ShaderNames.StripPrefix(s.Shader) ?? s.Shader;
            string? lm = null;
            string diffuse = name;
            if (planned.TryGetValue(s.Shader, out PlannedShader? ps))
            {
                diffuse = ps.Texture;
                lm = ps.Lightmap;
            }
            else
            {
                for (int i = name.IndexOf("__", StringComparison.Ordinal); i > 0; i = name.IndexOf("__", i + 1, StringComparison.Ordinal))
                    if (tex.ContainsKey(name[..i]) && tex.ContainsKey(name[(i + 2)..])) { diffuse = name[..i]; lm = name[(i + 2)..]; break; }
            }
            if (isSky) { diffuse = Key; lm = null; }
            if (!tex.ContainsKey(diffuse)) diffuse = Key;
            if (lm is not null && !tex.ContainsKey(lm)) lm = null;

            var verts = new PreviewVertex[s.Positions.Length];
            for (int i = 0; i < verts.Length; i++)
                verts[i] = new PreviewVertex(s.Positions[i], i < s.Normals.Length ? s.Normals[i] : Vector3.UnitZ,
                    i < s.Uv0.Length ? s.Uv0[i] : Vector2.Zero, lm is not null && i < s.Uv1.Length ? s.Uv1[i] : Vector2.Zero);
            // Le compilateur ecrit l'enroulement JA (v0,v2,v1) : on echange deux indices pour revenir a la convention
            // des autres vues (KOTOR, face avant = antihoraire).
            var idx = (int[])s.Indices.Clone();
            for (int i = 0; i + 2 < idx.Length; i += 3) (idx[i + 1], idx[i + 2]) = (idx[i + 2], idx[i + 1]);
            batches.Add(new PreviewBatch(s.Shader, diffuse, lm, ps is null ? PreviewBlend.Opaque : BlendOf(ps.Kind),
                ps?.DoubleSided ?? false, verts, idx, isSky));
        }

        (Vector3? spawn, float yaw) = SpawnOf(ParseEntities(bsp.Entities));
        return Make(batches, tex, [], [], spawn, yaw);
    }

    /// <summary>Cle de texture d'un fichier du plan : nom sans dossier ni extension (les lightmaps sans le sous-dossier lm/).</summary>
    public static string KeyOf(ExportTexture t)
    {
        ArgumentNullException.ThrowIfNull(t);
        string rel = t.RelPath.Replace('\\', '/');
        string[] prefixes = t.Lightmap
            ? [ShaderNames.LightmapPrefixFor(KotorGame.Kotor2), ShaderNames.LightmapPrefixFor(KotorGame.Kotor1)]
            : [ShaderNames.PrefixFor(KotorGame.Kotor2), ShaderNames.PrefixFor(KotorGame.Kotor1)];
        foreach (string prefix in prefixes)
            if (rel.StartsWith(prefix, StringComparison.Ordinal)) { rel = rel[prefix.Length..]; break; }
        return rel.EndsWith(".tga", StringComparison.OrdinalIgnoreCase) ? rel[..^4] : rel;
    }

    private static PreviewModel Make(List<PreviewBatch> batches, Dictionary<string, PreviewTexture> textures, Vector3[] collision,
        IReadOnlyList<PreviewMarker> markers, Vector3? spawn, float yaw)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        foreach (PreviewBatch b in batches)
            foreach (PreviewVertex v in b.Vertices) { min = Vector3.Min(min, v.Position); max = Vector3.Max(max, v.Position); any = true; }
        if (!any) { min = max = Vector3.Zero; }
        return new PreviewModel(batches, textures, collision, markers, spawn, yaw, min, max);
    }

    /// <summary>Index des shaders par nom ; en cas de doublon, le premier est garde.</summary>
    private static Dictionary<string, PlannedShader> IndexShaders(IEnumerable<PlannedShader> shaders)
    {
        var d = new Dictionary<string, PlannedShader>(StringComparer.Ordinal);
        foreach (PlannedShader s in shaders) d.TryAdd(s.Name, s);
        return d;
    }

    private static Dictionary<string, PreviewTexture> NewTextures() =>
        new(StringComparer.Ordinal) { [Key] = new PreviewTexture(8, 8, ModuleExporter.Checker()) };

    private static PreviewTexture ToTexture(int w, int h, byte[] rgba) => new(w, h, rgba);

    private static Vector2 FlipV(Vector2 uv) => new(uv.X, 1f - uv.Y);

    private static PreviewBlend BlendOf(ShaderKind k) => k switch
    {
        ShaderKind.LightmappedCutout => PreviewBlend.Cutout,
        ShaderKind.Additive => PreviewBlend.Additive,
        ShaderKind.Blend => PreviewBlend.Alpha,
        _ => PreviewBlend.Opaque,
    };

    private static (Vector3? Spawn, float Yaw) SpawnOf(IReadOnlyList<IReadOnlyDictionary<string, string>> entities)
    {
        foreach (var e in entities)
        {
            if (!e.TryGetValue("classname", out string? c) || c != "info_player_start" || !e.TryGetValue("origin", out string? o)) continue;
            string[] p = o.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 3 || !float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;
            float yaw = e.TryGetValue("angle", out string? a) && float.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;
            return (new Vector3(x, y, z), yaw);
        }
        return (null, 0f);
    }

    /// <summary>Lit le texte d'entites d'un .bsp (blocs « { "cle" "valeur" ... } »).</summary>
    private static List<IReadOnlyDictionary<string, string>> ParseEntities(string text)
    {
        var result = new List<IReadOnlyDictionary<string, string>>();
        Dictionary<string, string>? cur = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line == "{") cur = new Dictionary<string, string>();
            else if (line == "}") { if (cur is not null) result.Add(cur); cur = null; }
            else if (cur is not null && line.Length > 0)
            {
                string[] q = line.Split('"');
                if (q.Length >= 4) cur[q[1]] = q[3];
            }
        }
        return result;
    }
}

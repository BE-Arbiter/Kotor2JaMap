using System.Numerics;
using System.Text;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Layout;
using Kotor.Core.Mdl;
using Kotor.Core.Modules;
using Kotor.Core.Textures;
using Kotor.Core.Tlk;
using Kotor.Core.TwoDa;
using Kotor.Core.Walkmesh;

namespace Kotor.Core.Scene;

/// <summary>
/// Assemble toutes les pieces d'un module en une scene unique (metres KOTOR). Rien n'est ignore sans entree au journal ;
/// les cas groupes (noeuds non rendus, lumieres...) sont journalises avec un compte par piece.
/// </summary>
public static class ModuleSceneBuilder
{
    /// <summary>
    /// Decision d'alignement des WOK de piece (voir le rapport de l'etape 06) :
    /// true = les sommets du WOK sont deja en coordonnees du module ; false = locaux a la piece, on ajoute room.Position.
    /// </summary>
    internal const bool WokVerticesAreModuleSpace = true;

    private const string NoTextureKey = "notexture";

    public static ModuleScene Build(ResourceResolver res, string module, TlkFile? tlk, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        ArgumentNullException.ThrowIfNull(log);
        var info = ModuleCatalog.Load(res, module, tlk);
        byte[] lytData = res.Find(info.AreaResRef, ResourceType.Lyt)
            ?? throw new InvalidDataException($"{info.AreaResRef}.lyt not found for {module}.");
        var layout = LayoutFile.Parse(Encoding.ASCII.GetString(lytData));

        var textures = new Dictionary<string, KotorTexture?>();
        var meshes = new List<SceneMesh>();
        var walks = new List<SceneWalkmesh>();
        var surfaceMat = LoadSurfaceMat(res, log);

        var lights = new List<SceneLight>();
        var stats = new RepairStats();
        foreach (var room in layout.Rooms)
        {
            AddRoomMeshes(res, room, textures, meshes, lights, stats, log);
            AddRoomWalkmesh(res, room, surfaceMat, walks, log);
        }
        if (stats.Lightmaps > 0)
            log.Warn("LIGHTMAP_REPAIRED", $"{module}: {stats.Lightmaps} lightmap(s) repaired, {stats.Texels} invalid texel(s) replaced by the nearest valid one.");
        var area = ReadAreaLighting(res, info.AreaResRef, log);
        return new ModuleScene(info, meshes, walks, textures)
        {
            Lights = lights,
            AmbientColor = area.Ambient,
            SunDiffuseColor = area.SunDiffuse,
            AreaLightingKnown = area.Readable,
            RoomVisibility = ReadVisibility(res, info.AreaResRef, layout, log),
        };
    }

    /// <summary>
    /// Room visibility from the module <c>.vis</c> (same resref as the area). Null (no PVS, one always-visible cluster) when the file is
    /// missing or unreadable. Rooms of the layout that the VIS does not list as a key are left out of the result: they see everything.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyCollection<string>>? ReadVisibility(ResourceResolver res, string area, LayoutFile layout, ImportLog log)
    {
        byte[]? data = res.Find(area, ResourceType.Vis);
        if (data is null) { log.Warn("VIS_MISSING", $"{area}.vis not found: no room visibility, everything stays visible."); return null; }
        try
        {
            var vis = VisFile.Parse(Encoding.ASCII.GetString(data));
            var rooms = layout.Rooms.Select(r => r.Model).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int unlisted = rooms.Count(r => !vis.Visible.ContainsKey(r));
            if (unlisted > 0) log.Warn("VIS_ROOM_UNLISTED", $"{area}.vis: {unlisted} room(s) of the layout without entry, treated as visible from everywhere.");
            return vis.Visible;
        }
        catch (InvalidDataException ex)
        {
            log.Warn("VIS_UNREADABLE", $"{area}.vis unreadable ({ex.Message}): no room visibility.");
            return null;
        }
    }

    /// <summary>Counts for the LIGHTMAP_REPAIRED summary line.</summary>
    internal sealed class RepairStats { public int Lightmaps; public long Texels; }

    /// <summary>Area lighting read from the ARE. <paramref name="Readable"/>: the ARE was found and parsed.</summary>
    internal readonly record struct AreaLighting(Vector3 Ambient, Vector3 SunDiffuse, bool Readable);

    /// <summary>
    /// Zone lighting: ambient = ARE <c>DynAmbientColor</c>, else <c>SunAmbientColor</c> (DWORD 0x00BBGGRR, PyKotor Color.from_bgr_integer),
    /// else (0.2, 0.2, 0.2); sun = ARE <c>SunDiffuseColor</c> (same encoding), else black.
    /// </summary>
    internal static AreaLighting ReadAreaLighting(ResourceResolver res, string area, ImportLog log)
    {
        byte[]? data = res.Find(area, ResourceType.Are);
        if (data is null)
        {
            log.Warn("AMBIENT_DEFAULT", $"{area}.are not found: default lighting used.");
            return new AreaLighting(ModuleScene.DefaultAmbient, Vector3.Zero, false);
        }
        try
        {
            var are = Gff.GffReader.Read(data, out _);
            Vector3? ambient = null;
            foreach (string label in new[] { "DynAmbientColor", "SunAmbientColor" })
                if (are.Get<uint?>(label) is { } v) { ambient = FromBgr(v); break; }
            if (ambient is null)
                log.Warn("AMBIENT_DEFAULT", $"{area}.are has no DynAmbientColor or SunAmbientColor: default ambient colour used.");
            Vector3 sun = are.Get<uint?>("SunDiffuseColor") is { } sv ? FromBgr(sv) : Vector3.Zero;
            return new AreaLighting(ambient ?? ModuleScene.DefaultAmbient, sun, true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException
            or EndOfStreamException or IndexOutOfRangeException)
        {
            log.Warn("AMBIENT_DEFAULT", $"{area}.are unreadable ({ex.Message}): default lighting used.");
            return new AreaLighting(ModuleScene.DefaultAmbient, Vector3.Zero, false);
        }
    }

    internal static Vector3 FromBgr(uint v) => new((v & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f);

    private static TwoDaFile? LoadSurfaceMat(ResourceResolver res, ImportLog log)
    {
        byte[]? data = res.Find("surfacemat", ResourceType.TwoDa);
        if (data is null) { log.Warn("SURFACEMAT_MISSING", "surfacemat.2da not found: no walkmesh triangle is marked walkable."); return null; }
        try { return TwoDaFile.Read(data); }
        catch (InvalidDataException ex)
        {
            log.Warn("SURFACEMAT_MISSING", "surfacemat.2da unreadable: " + ex.Message);
            return null;
        }
    }

    private static void AddRoomMeshes(ResourceResolver res, LayoutRoom room, Dictionary<string, KotorTexture?> textures,
        List<SceneMesh> meshes, List<SceneLight> lights, RepairStats stats, ImportLog log)
    {
        byte[]? mdl = res.Find(room.Model, ResourceType.Mdl);
        byte[]? mdx = res.Find(room.Model, ResourceType.Mdx);
        if (mdl is null || mdx is null)
        {
            log.Warn("ROOM_MODEL_MISSING", $"{room.Model}: {(mdl is null ? "MDL" : "MDX")} not found, room skipped.");
            return;
        }
        KotorModel model;
        try { model = MdlReader.Read(mdl, mdx); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            log.Warn("ROOM_MODEL_UNREADABLE", $"{room.Model}: {ex.Message}");
            return;
        }

        AddModel(res, room, model, textures, meshes, log, lights, stats);
    }

    /// <summary>Assemble les maillages d'un modele deja lu (separe de la lecture pour etre testable sans MDL binaire).</summary>
    internal static void AddModel(ResourceResolver res, LayoutRoom room, KotorModel model,
        Dictionary<string, KotorTexture?> textures, List<SceneMesh> meshes, ImportLog log,
        List<SceneLight>? sceneLights = null, RepairStats? stats = null)
    {
        int defaulted = 0, notRendered = 0, lights = 0, emitters = 0, refs = 0, cams = 0, animated = 0, empty = 0;
        var offset = Matrix4x4.CreateTranslation(room.Position);
        foreach (var (node, modelTransform) in model.Walk())
        {
            if (node.Kind.HasFlag(NodeKind.Light))
            {
                lights++;
                if (node.Light is { } kl)
                {
                    if (kl.Invalid || !float.IsFinite(kl.Radius) || kl.Radius <= 0f || !float.IsFinite(kl.Multiplier) || kl.Multiplier <= 0f
                        || float.IsNaN(kl.Color.X) || float.IsNaN(kl.Color.Y) || float.IsNaN(kl.Color.Z))
                    {
                        log.Warn("LIGHT_INVALID", $"{room.Model}: light '{node.Name}' ignored (unreadable controller, or non-finite/non-positive radius or multiplier, or NaN colour).");
                        lights--; continue;
                    }
                    if (kl.Defaulted) defaulted++;
                    sceneLights?.Add(new SceneLight(node.Name, Vector3.Transform(Vector3.Zero, modelTransform) + room.Position,
                        Vector3.Clamp(kl.Color, Vector3.Zero, Vector3.One), kl.Radius, kl.Multiplier, kl.AmbientOnly));
                }
            }
            if (node.Kind.HasFlag(NodeKind.Emitter)) emitters++;
            if (node.Kind.HasFlag(NodeKind.Reference)) refs++;
            if (node.Kind.HasFlag(NodeKind.Camera)) cams++;
            var m = node.Mesh;
            if (m is null) continue;
            if (!m.Render || node.Kind.HasFlag(NodeKind.Aabb)) { notRendered++; continue; }
            if (m.Triangles.Length == 0 || m.Positions.Length == 0) { empty++; continue; }
            if ((node.Kind & (NodeKind.Skin | NodeKind.Dangly | NodeKind.Anim | NodeKind.Saber)) != 0) animated++;
            var mesh = BuildMesh(res, room, node, m, modelTransform, offset, textures, log, stats);
            if (mesh is not null) meshes.Add(mesh);
        }
        string r = room.Model;
        if (notRendered > 0) log.Warn("MESH_NOT_RENDERED", $"{r}: {notRendered} non-rendered mesh(es) skipped.");
        if (empty > 0) log.Warn("MESH_EMPTY", $"{r}: {empty} empty mesh(es) (0 triangles or 0 vertices) skipped.");
        if (animated > 0)
            log.Warn("MESH_STATIC_FROM_ANIMATED", $"{r}: {animated} skin/dangly/anim/saber mesh(es) exported as static (deformation and animation lost).");
        if (lights > 0) log.Warn("NODE_LIGHT", $"{r}: {lights} light node(s) used for the vertex lighting.");
        if (defaulted > 0) log.Warn("LIGHT_DEFAULTS", $"{r}: {defaulted} light node(s) without colour, radius or multiplier controller: defaults used (white, {MdlReader.DefaultLightRadius:0} m, x1).");
        if (emitters > 0) log.Warn("NODE_EMITTER", $"{r}: {emitters} emitter(s) not converted.");
        if (refs > 0) log.Warn("NODE_REFERENCE", $"{r}: {refs} reference node(s) not converted.");
        if (cams > 0) log.Warn("NODE_CAMERA", $"{r}: {cams} camera(s) not converted.");
    }

    private static SceneMesh? BuildMesh(ResourceResolver res, LayoutRoom room, KotorNode node, KotorMesh m,
        Matrix4x4 modelTransform, Matrix4x4 offset, Dictionary<string, KotorTexture?> textures, ImportLog log,
        RepairStats? stats)
    {
        string where = $"{room.Model}/{node.Name}";
        if (m.Triangles.Any(i => i < 0 || i >= m.Positions.Length))
        {
            log.Warn("MESH_BAD_INDEX", $"{where}: triangle index out of range, mesh skipped.");
            return null;
        }

        var world = modelTransform * offset;
        var pos = m.Positions.Select(p => Vector3.Transform(p, world)).ToArray();
        Vector3[] normals = m.Normals is { } n && n.Length == pos.Length
            ? n.Select(v => SafeNormalize(Vector3.TransformNormal(v, modelTransform))).ToArray()
            : ComputeNormals(pos, m.Triangles);

        Vector2[] uv0;
        if (m.Uv0 is { } u && u.Length == pos.Length) uv0 = u;
        else
        {
            uv0 = new Vector2[pos.Length];
            log.Warn("MDL_NO_UV", $"{where}: no diffuse UV, UV set to zero.");
        }
        Vector2[]? uv1 = m.Uv1 is { } u1 && u1.Length == pos.Length ? u1 : null;

        string tex = m.Texture0?.ToLowerInvariant() ?? NoTextureKey;
        if (m.Texture0 is null)
        {
            textures.TryAdd(NoTextureKey, null);
            log.Warn("MESH_NO_TEXTURE", $"{where}: rendered mesh without texture.");
        }
        else Resolve(res, tex, TextureKind.Diffuse, textures, log, stats);

        string? lm = m.Texture1?.ToLowerInvariant();
        if (lm is not null && uv1 is null)
        {
            log.Warn("MDL_NO_UV1", $"{where}: lightmap '{lm}' present but UV1 missing or inconsistent, lightmap ignored.");
            lm = null;
        }
        if (lm is not null) Resolve(res, lm, TextureKind.Lightmap, textures, log, stats);

        var txi = textures.TryGetValue(tex, out var t) && t is not null ? t.Txi : TxiInfo.Empty;
        return new SceneMesh(room.Model, node.Name, tex, lm, txi, m.TransparencyHint, pos, normals, uv0, uv1, m.Triangles);
    }

    private static void Resolve(ResourceResolver res, string name, TextureKind kind,
        Dictionary<string, KotorTexture?> textures, ImportLog log, RepairStats? stats)
    {
        name = name.ToLowerInvariant();
        if (textures.ContainsKey(name)) return;
        KotorTexture? tex;
        try
        {
            tex = TextureLookup.Find(res, name, kind, (repaired, none) =>
            {
                if (stats is not null) { stats.Lightmaps++; stats.Texels += repaired; }
                if (none) log.Warn("LIGHTMAP_NO_VALID_TEXEL", $"{name}: lightmap has no valid texel (alpha = 255), replaced by neutral gray.");
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            log.Warn("TEXTURE_UNSUPPORTED", $"{name}: {ex.Message}");
            textures[name] = null;
            return;
        }
        textures[name] = tex;
        if (tex is null) { log.Warn("TEXTURE_MISSING", $"{name} ({kind}) not found."); return; }
        if (tex.Txi.IsAnimated) log.Warn("TEXTURE_ANIMATED", $"{name}: animated texture, exported as a still image.");
        if (tex.Txi.EnvMapTexture is not null)
            log.Warn("TEXTURE_ENVMAP", $"{name}: reflection '{tex.Txi.EnvMapTexture}' not converted.");
    }

    private static void AddRoomWalkmesh(ResourceResolver res, LayoutRoom room, TwoDaFile? surfaceMat,
        List<SceneWalkmesh> walks, ImportLog log)
    {
        byte[]? wok = res.Find(room.Model, ResourceType.Wok);
        if (wok is null) { log.Warn("WALKMESH_MISSING", $"{room.Model}: WOK not found."); return; }
        Walkmesh.Walkmesh wm;
        try { wm = WokReader.Read(wok); }
        catch (InvalidDataException ex)
        {
            log.Warn("WALKMESH_MISSING", $"{room.Model}: WOK unreadable ({ex.Message}).");
            return;
        }
        var add = WokVerticesAreModuleSpace ? Vector3.Zero : room.Position;
        var pos = wm.Vertices.Select(v => v + add).ToArray();
        var walkable = new bool[wm.Materials.Length];
        int unknown = 0;
        for (int i = 0; i < walkable.Length; i++)
        {
            string? w = surfaceMat?.Get(wm.Materials[i], "Walk");
            if (surfaceMat is not null && w is null) unknown++;
            walkable[i] = w == "1";
        }
        if (unknown > 0)
            log.Warn("SURFACEMAT_UNKNOWN", $"{room.Model}: {unknown} walkmesh triangle(s) with a material missing from surfacemat.2da, treated as not walkable.");
        walks.Add(new SceneWalkmesh(room.Model, pos, wm.Triangles, walkable));
    }

    private static Vector3 SafeNormalize(Vector3 v) => v.LengthSquared() > 1e-12f ? Vector3.Normalize(v) : Vector3.UnitZ;

    private static Vector3[] ComputeNormals(Vector3[] pos, int[] tris)
    {
        var acc = new Vector3[pos.Length];
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            var n = Vector3.Cross(pos[tris[i + 1]] - pos[tris[i]], pos[tris[i + 2]] - pos[tris[i]]);
            acc[tris[i]] += n; acc[tris[i + 1]] += n; acc[tris[i + 2]] += n;
        }
        for (int i = 0; i < acc.Length; i++) acc[i] = SafeNormalize(acc[i]);
        return acc;
    }
}

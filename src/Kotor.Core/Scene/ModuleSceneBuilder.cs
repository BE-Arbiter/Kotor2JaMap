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
            ?? throw new InvalidDataException($"{info.AreaResRef}.lyt introuvable pour {module}.");
        var layout = LayoutFile.Parse(Encoding.ASCII.GetString(lytData));

        var textures = new Dictionary<string, KotorTexture?>();
        var meshes = new List<SceneMesh>();
        var walks = new List<SceneWalkmesh>();
        var surfaceMat = LoadSurfaceMat(res, log);

        foreach (var room in layout.Rooms)
        {
            AddRoomMeshes(res, room, textures, meshes, log);
            AddRoomWalkmesh(res, room, surfaceMat, walks, log);
        }
        return new ModuleScene(info, meshes, walks, textures);
    }

    private static TwoDaFile? LoadSurfaceMat(ResourceResolver res, ImportLog log)
    {
        byte[]? data = res.Find("surfacemat", ResourceType.TwoDa);
        if (data is null) { log.Warn("SURFACEMAT_MISSING", "surfacemat.2da introuvable : aucun triangle de walkmesh marque praticable."); return null; }
        try { return TwoDaFile.Read(data); }
        catch (InvalidDataException ex)
        {
            log.Warn("SURFACEMAT_MISSING", "surfacemat.2da illisible : " + ex.Message);
            return null;
        }
    }

    private static void AddRoomMeshes(ResourceResolver res, LayoutRoom room, Dictionary<string, KotorTexture?> textures,
        List<SceneMesh> meshes, ImportLog log)
    {
        byte[]? mdl = res.Find(room.Model, ResourceType.Mdl);
        byte[]? mdx = res.Find(room.Model, ResourceType.Mdx);
        if (mdl is null || mdx is null)
        {
            log.Warn("ROOM_MODEL_MISSING", $"{room.Model} : {(mdl is null ? "MDL" : "MDX")} introuvable, piece sautee.");
            return;
        }
        KotorModel model;
        try { model = MdlReader.Read(mdl, mdx); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            log.Warn("ROOM_MODEL_UNREADABLE", $"{room.Model} : {ex.Message}");
            return;
        }

        int notRendered = 0, lights = 0, emitters = 0, refs = 0, cams = 0;
        var offset = Matrix4x4.CreateTranslation(room.Position);
        foreach (var (node, modelTransform) in model.Walk())
        {
            if (node.Kind.HasFlag(NodeKind.Light)) lights++;
            if (node.Kind.HasFlag(NodeKind.Emitter)) emitters++;
            if (node.Kind.HasFlag(NodeKind.Reference)) refs++;
            if (node.Kind.HasFlag(NodeKind.Camera)) cams++;
            var m = node.Mesh;
            if (m is null) continue;
            if (!m.Render || node.Kind.HasFlag(NodeKind.Aabb)) { notRendered++; continue; }
            var mesh = BuildMesh(res, room, node, m, modelTransform, offset, textures, log);
            if (mesh is not null) meshes.Add(mesh);
        }
        string r = room.Model;
        if (notRendered > 0) log.Warn("MESH_NOT_RENDERED", $"{r} : {notRendered} maillage(s) non rendu(s) ignore(s).");
        if (lights > 0) log.Warn("NODE_LIGHT", $"{r} : {lights} noeud(s) lumiere non converti(s).");
        if (emitters > 0) log.Warn("NODE_EMITTER", $"{r} : {emitters} emetteur(s) non converti(s).");
        if (refs > 0) log.Warn("NODE_REFERENCE", $"{r} : {refs} noeud(s) reference non converti(s).");
        if (cams > 0) log.Warn("NODE_CAMERA", $"{r} : {cams} camera(s) non convertie(s).");
    }

    private static SceneMesh? BuildMesh(ResourceResolver res, LayoutRoom room, KotorNode node, KotorMesh m,
        Matrix4x4 modelTransform, Matrix4x4 offset, Dictionary<string, KotorTexture?> textures, ImportLog log)
    {
        string where = $"{room.Model}/{node.Name}";
        if (m.Triangles.Length == 0 || m.Positions.Length == 0) return null;
        if (m.Triangles.Any(i => i < 0 || i >= m.Positions.Length))
        {
            log.Warn("MESH_BAD_INDEX", $"{where} : indice de triangle hors limites, maillage ignore.");
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
            log.Warn("MDL_NO_UV", $"{where} : pas d'UV de diffuse, UV a zero.");
        }
        Vector2[]? uv1 = m.Uv1 is { } u1 && u1.Length == pos.Length ? u1 : null;

        string tex = m.Texture0 ?? NoTextureKey;
        if (m.Texture0 is null)
        {
            textures.TryAdd(NoTextureKey, null);
            log.Warn("MESH_NO_TEXTURE", $"{where} : maillage rendu sans texture.");
        }
        else Resolve(res, tex, TextureKind.Diffuse, textures, log);

        string? lm = m.Texture1;
        if (lm is not null) Resolve(res, lm, TextureKind.Lightmap, textures, log);

        var txi = textures.TryGetValue(tex, out var t) && t is not null ? t.Txi : TxiInfo.Empty;
        return new SceneMesh(room.Model, node.Name, tex, lm, txi, m.TransparencyHint, pos, normals, uv0, uv1, m.Triangles);
    }

    private static void Resolve(ResourceResolver res, string name, TextureKind kind,
        Dictionary<string, KotorTexture?> textures, ImportLog log)
    {
        if (textures.ContainsKey(name)) return;
        KotorTexture? tex;
        try { tex = TextureLookup.Find(res, name, kind); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            log.Warn("TEXTURE_UNSUPPORTED", $"{name} : {ex.Message}");
            textures[name] = null;
            return;
        }
        textures[name] = tex;
        if (tex is null) { log.Warn("TEXTURE_MISSING", $"{name} ({kind}) introuvable."); return; }
        if (tex.Txi.IsAnimated) log.Warn("TEXTURE_ANIMATED", $"{name} : texture animee, exportee immobile.");
        if (tex.Txi.EnvMapTexture is not null)
            log.Warn("TEXTURE_ENVMAP", $"{name} : reflet '{tex.Txi.EnvMapTexture}' non converti.");
    }

    private static void AddRoomWalkmesh(ResourceResolver res, LayoutRoom room, TwoDaFile? surfaceMat,
        List<SceneWalkmesh> walks, ImportLog log)
    {
        byte[]? wok = res.Find(room.Model, ResourceType.Wok);
        if (wok is null) { log.Warn("WALKMESH_MISSING", $"{room.Model} : WOK introuvable."); return; }
        Walkmesh.Walkmesh wm;
        try { wm = WokReader.Read(wok); }
        catch (InvalidDataException ex)
        {
            log.Warn("WALKMESH_MISSING", $"{room.Model} : WOK illisible ({ex.Message}).");
            return;
        }
        var add = WokVerticesAreModuleSpace ? Vector3.Zero : room.Position;
        var pos = wm.Vertices.Select(v => v + add).ToArray();
        var walkable = new bool[wm.Materials.Length];
        for (int i = 0; i < walkable.Length; i++)
            walkable[i] = surfaceMat?.Get(wm.Materials[i], "Walk") == "1";
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

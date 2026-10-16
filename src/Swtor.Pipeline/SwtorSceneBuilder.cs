using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Gr2;

namespace Swtor.Pipeline;

/// <summary>Options de construction de la scene d'une zone SWTOR.</summary>
public sealed record SwtorBuildOptions
{
    /// <summary>Rooms a garder (insensible a la casse) ; null = toutes (sauf la room du ciel).</summary>
    public IReadOnlyCollection<string>? Rooms { get; init; }

    /// <summary>Garde la room <c>SPACE</c> (exterieur : coque du vaisseau, fenetres) quand toutes les rooms sont demandees.</summary>
    public bool IncludeSpaceRoom { get; init; } = true;

    /// <summary>Cote maximal des textures exportees (0 = pas de reduction).</summary>
    public int MaxTextureSize { get; init; } = 512;

    public AxisConvention Axes { get; init; } = SwtorAxes.Default;

    /// <summary>Composante Z minimale de la normale d'un triangle pour qu'il soit marchable (0.7 = pente de 45 degres).</summary>
    public float WalkableMinNormalZ { get; init; } = 0.7f;

    /// <summary>Ambiante plate (0..1) des zones sans lumiere convertie : la scene n'a pas de lightmap.</summary>
    public float Ambient { get; init; } = 0.55f;
}

/// <summary>Comptes de la construction, pour le rapport et les tests.</summary>
public sealed record SwtorSceneStats(int Rooms, int ModelInstances, int DistinctModels, int MissingModels, int Meshes, long Triangles,
    int SkippedCollisionMeshes, int Lights, int Textures, int WalkableTriangles, int SolidTriangles);

public sealed record SwtorSceneResult(ModuleScene Scene, SwtorSceneStats Stats);

/// <summary>
/// Construit le <see cref="ModuleScene"/> d'une zone SWTOR : pour chaque instance de modele, charge le .gr2 (cache par chemin), garde les
/// meshes visibles, les place dans le monde (matrice de <see cref="AreaLayout"/>), passe de Y haut a Z haut (<see cref="SwtorAxes"/>)
/// et regroupe les triangles par (room, materiau). Le walkmesh de chaque room reprend la geometrie visible : les triangles dont la
/// normale pointe vers le haut sont marchables, les autres bloquent.
/// Vit dans Swtor.Pipeline : Swtor.Core reste un pur lecteur de formats sans dependance vers Kotor, et Kotor.Pipeline ne depend pas de SWTOR.
/// </summary>
public static class SwtorSceneBuilder
{
    public static SwtorSceneResult Build(AreaLayout layout, ISwtorModelSource source, string internalName, SwtorBuildOptions? options, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(log);
        options ??= new SwtorBuildOptions();
        string skyRoom = layout.Area.AreaProperties.Get("skyscene_room") ?? "skyscene_room";

        var materials = new SwtorMaterials(source, options.MaxTextureSize, log);
        var models = new Dictionary<string, Gr2Model?>(StringComparer.OrdinalIgnoreCase);
        var meshes = new List<SceneMesh>();
        var lights = new List<SceneLight>();
        var roomMeshes = new Dictionary<string, List<SceneMesh>>(StringComparer.OrdinalIgnoreCase);
        var filter = options.Rooms is { Count: > 0 } r ? new HashSet<string>(r, StringComparer.OrdinalIgnoreCase) : null;

        int roomsUsed = 0, instances = 0, skippedCollision = 0, skippedHidden = 0, skippedKinds = 0, badMeshes = 0, noUv = 0;
        var skippedByKind = new Dictionary<InstanceKind, int>();
        long triangles = 0;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (roomName, room) in layout.Rooms)
        {
            if (string.Equals(roomName, skyRoom, StringComparison.OrdinalIgnoreCase)) continue;
            if (filter is not null ? !filter.Contains(roomName) : !options.IncludeSpaceRoom && string.Equals(roomName, "SPACE", StringComparison.OrdinalIgnoreCase)) continue;
            roomsUsed++;
            var acc = new Dictionary<string, MeshAccumulator>(StringComparer.Ordinal);
            var order = new List<MeshAccumulator>();

            foreach (RoomInstance inst in room.Instances)
            {
                if (inst.Kind == InstanceKind.Light)
                {
                    if (AddLight(inst, layout, options, lights)) { }
                    continue;
                }
                if (inst.Kind != InstanceKind.Model || inst.AssetPath is null)
                {
                    if (inst.Kind is not (InstanceKind.Unknown or InstanceKind.Other)) skippedByKind[inst.Kind] = skippedByKind.GetValueOrDefault(inst.Kind) + 1;
                    skippedKinds++;
                    continue;
                }
                instances++;
                if (!TryGetModel(inst.AssetPath, source, models, missing, log, out Gr2Model? model)) continue;

                Matrix4x4 world = layout.World(inst);
                bool det = world.GetDeterminant() < 0f;
                bool flip = det ^ SwtorAxes.FlipsWinding(options.Axes);
                Matrix4x4 normalM = Matrix4x4.Invert(world, out Matrix4x4 inv) ? Matrix4x4.Transpose(inv) : world;

                foreach (Gr2Mesh mesh in model!.Meshes)
                {
                    if (mesh.Name.Contains("collision", StringComparison.OrdinalIgnoreCase)) { skippedCollision++; continue; }
                    if (mesh.Positions.Length == 0 || mesh.Indices.Length < 3) continue;
                    if (mesh.Indices.Any(i => i >= mesh.Positions.Length)) { badMeshes++; log.Warn("SWTOR_MESH_BAD_INDEX", $"{inst.AssetPath}/{mesh.Name}: triangle index out of range, mesh skipped."); continue; }
                    Vector2[]? uv = mesh.UvSets.Count > 0 && mesh.UvSets[0].Length == mesh.Positions.Length ? mesh.UvSets[0] : null;
                    if (uv is null) { noUv++; log.Warn("SWTOR_NO_UV", $"{inst.AssetPath}/{mesh.Name}: no UV set, UV set to zero."); }

                    IReadOnlyList<Gr2Piece> pieces = mesh.Pieces.Count > 0
                        ? mesh.Pieces
                        : [new Gr2Piece(0, mesh.Indices.Length / 3, 0, Vector3.Zero, Vector3.Zero)];
                    foreach (Gr2Piece piece in pieces)
                    {
                        if (piece.MaterialIndex < 0 || piece.MaterialIndex >= model.Materials.Count)
                        {
                            log.Warn("SWTOR_MATERIAL_INDEX", $"{inst.AssetPath}/{mesh.Name}: material index {piece.MaterialIndex} out of range, piece skipped.");
                            continue;
                        }
                        SwtorMaterialInfo mat = materials.Resolve(model.Materials[piece.MaterialIndex]);
                        if (mat.Hidden) { skippedHidden++; continue; }
                        int first = piece.StartTriangle * 3, end = Math.Min(mesh.Indices.Length, (piece.StartTriangle + piece.TriangleCount) * 3);
                        if (first < 0 || first >= end) continue;

                        if (!acc.TryGetValue(mat.Key, out MeshAccumulator? a))
                        {
                            a = new MeshAccumulator(mat);
                            acc[mat.Key] = a;
                            order.Add(a);
                        }
                        a.Add(mesh, uv, mat.UvScale, world, normalM, flip, options.Axes, first, end);
                    }
                }
            }

            var list = new List<SceneMesh>();
            foreach (MeshAccumulator a in order)
            {
                if (a.Triangles.Count == 0) continue;
                SceneMesh sm = a.ToSceneMesh(roomName);
                list.Add(sm);
                triangles += sm.Triangles.Length / 3;
            }
            meshes.AddRange(list);
            roomMeshes[roomName] = list;
        }

        foreach (var (kind, count) in skippedByKind.OrderBy(k => k.Key.ToString(), StringComparer.Ordinal))
            log.Warn("SWTOR_KIND_SKIPPED", $"{count} instance(s) of kind {kind} not converted.");
        if (skippedCollision > 0) log.Warn("SWTOR_COLLISION_MESH_SKIPPED", $"{skippedCollision} collision mesh(es) not rendered (the visible geometry serves as collision).");
        if (skippedHidden > 0) log.Warn("SWTOR_HIDDEN_PIECE_SKIPPED", $"{skippedHidden} piece(s) with a collision-only material skipped.");
        if (lights.Count > 0) log.Warn("SWTOR_LIGHTS_APPROX", $"{lights.Count} light(s) converted with a guessed radius (SWTOR attenuation is not decoded).");
        if (meshes.Count == 0) log.Warn("SWTOR_EMPTY_SCENE", $"{internalName}: no mesh produced (rooms: {roomsUsed}).");

        var walks = BuildWalkmeshes(roomMeshes, options, out int walkableTris, out int solidTris);
        ModuleEntry entry = ChooseEntry(walks, meshes, roomMeshes, log);

        var info = new ModuleInfo(internalName, internalName, internalName, entry);
        var scene = new ModuleScene(info, meshes, walks, materials.Textures)
        {
            Lights = lights,
            AmbientColor = new Vector3(options.Ambient),
            SunDiffuseColor = new Vector3(0.25f),
            AreaLightingKnown = true,
        };
        var stats = new SwtorSceneStats(roomsUsed, instances, models.Count, missing.Count, meshes.Count, triangles, skippedCollision, lights.Count,
            materials.Textures.Count, walkableTris, solidTris);
        return new SwtorSceneResult(scene, stats);
    }

    private static bool TryGetModel(string path, ISwtorModelSource source, Dictionary<string, Gr2Model?> cache, HashSet<string> missing,
        ImportLog log, out Gr2Model? model)
    {
        if (cache.TryGetValue(path, out model)) return model is not null;
        try
        {
            model = source.LoadModel(path);
            if (model is null) log.Warn("SWTOR_MODEL_MISSING", $"{path}: .gr2 not found, instance(s) skipped.");
        }
        catch (GameFormatException ex)
        {
            model = null;
            log.Warn("SWTOR_MODEL_UNREADABLE", $"{path}: {ex.Message}");
        }
        cache[path] = model;
        if (model is null) missing.Add(path);
        return model is not null;
    }

    /// <summary>Lumiere ponctuelle. Rayon devine : SWTOR n'expose pas l'attenuation decodee (l'echelle de l'objet sert d'extension probable).</summary>
    private static bool AddLight(RoomInstance inst, AreaLayout layout, SwtorBuildOptions options, List<SceneLight> lights)
    {
        if (inst.Color is not { } c) return false;
        Vector3 pos = SwtorAxes.ToZUp(layout.WorldPosition(inst), options.Axes);
        float extent = MathF.Max(inst.Scale.X, MathF.Max(inst.Scale.Y, inst.Scale.Z));
        float radius = Math.Clamp(extent * 4f, 2.5f, 8f);
        lights.Add(new SceneLight("light_" + inst.Id.ToString("X"), pos, Vector3.Clamp(new Vector3(c.X, c.Y, c.Z), Vector3.Zero, Vector3.One), radius, 1f));
        return true;
    }

    /// <summary>Un walkmesh par room : les triangles visibles (hors additifs : ecrans, champs de force) ; marchables si la normale pointe vers le haut.</summary>
    private static List<SceneWalkmesh> BuildWalkmeshes(Dictionary<string, List<SceneMesh>> roomMeshes, SwtorBuildOptions options,
        out int walkableTris, out int solidTris)
    {
        walkableTris = 0; solidTris = 0;
        var result = new List<SceneWalkmesh>();
        foreach (var (room, list) in roomMeshes)
        {
            var pos = new List<Vector3>();
            var tris = new List<int>();
            var walk = new List<bool>();
            foreach (SceneMesh m in list)
            {
                if (m.Alpha == SurfaceAlpha.Additive) continue;
                int b = pos.Count;
                pos.AddRange(m.Positions);
                for (int i = 0; i + 2 < m.Triangles.Length; i += 3)
                {
                    int a0 = m.Triangles[i], a1 = m.Triangles[i + 1], a2 = m.Triangles[i + 2];
                    tris.Add(b + a0); tris.Add(b + a1); tris.Add(b + a2);
                    walk.Add(IsWalkable(m.Positions[a0], m.Positions[a1], m.Positions[a2], options.WalkableMinNormalZ));
                }
            }
            if (tris.Count == 0) continue;
            walkableTris += walk.Count(w => w);
            solidTris += walk.Count(w => !w);
            result.Add(new SceneWalkmesh(room, [.. pos], [.. tris], [.. walk]));
        }
        return result;
    }

    internal static bool IsWalkable(Vector3 a, Vector3 b, Vector3 c, float minNormalZ)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        float len = n.Length();
        return len > 1e-9f && n.Z / len >= minNormalZ;
    }

    /// <summary>
    /// Point d'apparition : au milieu du sol de la plus grande room marchable (hors SPACE), sur un triangle de sol avec un degagement
    /// d'environ 1,35 m au-dessus (joueur JA : 56 unites = 1,4 m a l'echelle 40) ; direction vers le centre de la room marchable suivante, sinon +X.
    /// </summary>
    internal static ModuleEntry ChooseEntry(IReadOnlyList<SceneWalkmesh> walks, IReadOnlyList<SceneMesh> meshes,
        Dictionary<string, List<SceneMesh>> roomMeshes, ImportLog log)
    {
        var rooms = new List<(string Room, float Area, Vector2 Center, SceneWalkmesh W)>();
        foreach (SceneWalkmesh w in walks)
        {
            float area = 0f; Vector2 sum = Vector2.Zero;
            for (int t = 0; t < w.Triangles.Length / 3; t++)
            {
                if (!w.Walkable[t]) continue;
                Vector3 a = w.Positions[w.Triangles[t * 3]], b = w.Positions[w.Triangles[t * 3 + 1]], c = w.Positions[w.Triangles[t * 3 + 2]];
                float ar = Vector3.Cross(b - a, c - a).Length() * 0.5f;
                area += ar;
                sum += new Vector2((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f) * ar;
            }
            if (area > 0f) rooms.Add((w.Room, area, sum / area, w));
        }
        if (rooms.Count == 0)
        {
            log.Warn("SWTOR_NO_FLOOR", "No walkable floor found: spawn placed at the origin.");
            return new ModuleEntry(Vector3.Zero, Vector2.UnitX);
        }
        var candidates = rooms.Where(r => !r.Room.Equals("SPACE", StringComparison.OrdinalIgnoreCase)).ToList();
        if (candidates.Count == 0) candidates = rooms;
        var best = candidates.OrderByDescending(r => r.Area).First();

        // Triangles du sol de cette room les plus proches du centre, plus un test de degagement vertical.
        var floor = new List<(Vector3 Point, float Dist)>();
        SceneWalkmesh bw = best.W;
        // Niveau principal : hauteur (par tranches de 10 cm) qui porte le plus de surface marchable. Evite d'apparaitre sur une table ou une passerelle.
        var levelArea = new Dictionary<int, float>();
        for (int t = 0; t < bw.Triangles.Length / 3; t++)
        {
            if (!bw.Walkable[t]) continue;
            Vector3 a = bw.Positions[bw.Triangles[t * 3]], b = bw.Positions[bw.Triangles[t * 3 + 1]], c0 = bw.Positions[bw.Triangles[t * 3 + 2]];
            int level = (int)MathF.Round((a.Z + b.Z + c0.Z) / 3f * 10f);
            levelArea[level] = levelArea.GetValueOrDefault(level) + Vector3.Cross(b - a, c0 - a).Length() * 0.5f;
        }
        int mainLevel = levelArea.OrderByDescending(kv => kv.Value).First().Key;
        for (int t = 0; t < bw.Triangles.Length / 3; t++)
        {
            if (!bw.Walkable[t]) continue;
            Vector3 c = (bw.Positions[bw.Triangles[t * 3]] + bw.Positions[bw.Triangles[t * 3 + 1]] + bw.Positions[bw.Triangles[t * 3 + 2]]) / 3f;
            if (Math.Abs((int)MathF.Round(c.Z * 10f) - mainLevel) > 1) continue;
            // Points d'essai repartis dans le triangle (barycentriques) : un triangle de dalle fait plusieurs metres, son centre peut etre sous un meuble.
            Vector3 pa = bw.Positions[bw.Triangles[t * 3]], pb = bw.Positions[bw.Triangles[t * 3 + 1]], pc = bw.Positions[bw.Triangles[t * 3 + 2]];
            const int N = 6;
            for (int i = 0; i <= N; i++)
                for (int j = 0; i + j <= N; j++)
                {
                    float u = (i + 0.33f) / (N + 1f), v = (j + 0.33f) / (N + 1f);
                    if (u + v >= 1f) continue;
                    Vector3 s = pa + (pb - pa) * u + (pc - pa) * v;
                    floor.Add((s, Vector2.Distance(new Vector2(s.X, s.Y), best.Center)));
                }
        }
        Vector3 spawn = default;
        bool found = false;
        foreach (var (point, _) in floor.OrderBy(f => f.Dist).Take(3000))
        {
            if (HasHeadroom(bw, point, 1.1f)) { spawn = point; found = true; break; }
        }
        if (!found)
        {
            spawn = floor.OrderBy(f => f.Dist).First().Point;
            log.Warn("SWTOR_SPAWN_HEADROOM", $"No floor point with 1.35 m of headroom near the centre of room {best.Room}: spawn placed on the closest floor triangle.");
        }

        Vector2 dir = Vector2.UnitX;
        var other = candidates.Where(r => r.Room != best.Room).OrderByDescending(r => r.Area).FirstOrDefault();
        if (other.Room is not null)
        {
            Vector2 d = other.Center - new Vector2(spawn.X, spawn.Y);
            if (d.LengthSquared() > 1e-4f) dir = Vector2.Normalize(d);
        }
        return new ModuleEntry(spawn, dir);
    }

    /// <summary>Aucun triangle de la room ne coupe le segment vertical de <paramref name="height"/> metres au-dessus de <paramref name="p"/>.</summary>
    private static bool HasHeadroom(SceneWalkmesh w, Vector3 p, float height)
    {
        Vector3 from = p + new Vector3(0, 0, 0.25f); // ignore les surfaces a moins de 25 cm (tapis, decalques, epaisseur de dalle)
        for (int t = 0; t < w.Triangles.Length / 3; t++)
        {
            Vector3 a = w.Positions[w.Triangles[t * 3]], b = w.Positions[w.Triangles[t * 3 + 1]], c = w.Positions[w.Triangles[t * 3 + 2]];
            if (RayHitsTriangleAbove(from, a, b, c, out float z) && z > from.Z && z < from.Z + height) return false;
        }
        return true;
    }

    /// <summary>Intersection du rayon vertical (x, y) avec le plan du triangle : Z du point si (x, y) est dans le triangle projete.</summary>
    private static bool RayHitsTriangleAbove(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float z)
    {
        z = 0;
        static float Cross(Vector3 o, Vector3 u, Vector3 v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        if (neg && pos) return false;
        float area = Cross(a, b, c);
        if (MathF.Abs(area) < 1e-9f) return false;
        float w0 = d2 / area, w1 = d3 / area, w2 = d1 / area;
        z = w0 * a.Z + w1 * b.Z + w2 * c.Z;
        return true;
    }

    /// <summary>Cumule les sommets d'un meme materiau dans une room (positions monde, Z haut).</summary>
    private sealed class MeshAccumulator(SwtorMaterialInfo material)
    {
        public readonly List<Vector3> Positions = [];
        public readonly List<Vector3> Normals = [];
        public readonly List<Vector2> Uv = [];
        public readonly List<int> Triangles = [];

        public void Add(Gr2Mesh mesh, Vector2[]? uv, Vector2 uvScale, Matrix4x4 world, Matrix4x4 normalM, bool flip, AxisConvention axes, int first, int end)
        {
            var remap = new Dictionary<int, int>();
            int Map(int src)
            {
                if (remap.TryGetValue(src, out int mapped)) return mapped;
                Vector3 p = SwtorAxes.ToZUp(Vector3.Transform(mesh.Positions[src], world), axes);
                Vector3 n = mesh.Normals is { } ns && ns.Length == mesh.Positions.Length
                    ? SwtorAxes.ToZUp(Vector3.TransformNormal(ns[src], normalM), axes)
                    : Vector3.UnitZ;
                n = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitZ;
                // SceneMesh : convention GL (v = 0 en bas) ; la texture SWTOR (D3D) a v = 0 en haut : le pipeline reinverse v a l'export.
                Vector2 t = uv is null ? Vector2.Zero : new Vector2(uv[src].X * uvScale.X, 1f - uv[src].Y * uvScale.Y);
                mapped = Positions.Count;
                Positions.Add(p); Normals.Add(n); Uv.Add(t);
                remap[src] = mapped;
                return mapped;
            }
            for (int i = first; i + 2 < end; i += 3)
            {
                int a = Map(mesh.Indices[i]), b = Map(mesh.Indices[i + 1]), c = Map(mesh.Indices[i + 2]);
                if (flip) (b, c) = (c, b);
                Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
            }
        }

        public SceneMesh ToSceneMesh(string room) =>
            new(room, material.Key, material.Key, null, TxiInfo.Empty, 0, [.. Positions], [.. Normals], [.. Uv], null, [.. Triangles])
            {
                Alpha = material.Alpha,
            };
    }
}

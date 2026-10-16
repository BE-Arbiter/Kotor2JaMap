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

    /// <summary>Garde la room <c>SPACE</c> (exterieur : coque du vaisseau, fenetres) quand toutes les rooms sont demandees. Faux par defaut : pour un interieur elle ajoute des centaines de milliers de triangles hors du jeu.</summary>
    public bool IncludeSpaceRoom { get; init; } = false;

    /// <summary>Cote maximal des textures exportees (0 = pas de reduction).</summary>
    public int MaxTextureSize { get; init; } = 512;

    public AxisConvention Axes { get; init; } = SwtorAxes.Default;

    /// <summary>Composante Z minimale de la normale d'un triangle pour qu'il soit marchable (0.7 = pente de 45 degres).</summary>
    public float WalkableMinNormalZ { get; init; } = 0.7f;

    /// <summary>Ambiante (0..1) d'une zone interieure qui a des lumieres converties : les lumieres s'y ajoutent.</summary>
    public float AmbientLit { get; init; } = 0.35f;

    /// <summary>Ambiante plate (0..1) d'une zone interieure sans aucune lumiere convertie (pas de lightmap).</summary>
    public float AmbientUnlit { get; init; } = 0.55f;

    /// <summary>Utilise les meshes de collision des .gr2 (<c>collision_override</c>, <c>collision*</c>, <c>generated_collision</c>) ; faux = geometrie visible comme en T4.</summary>
    public bool UseCollisionMeshes { get; init; } = true;

    /// <summary>Rayon JA d'une lumiere = plus grande echelle de l'objet x ce facteur (metres), borne par min/max. Valeur d'apparence, non mesuree en jeu.</summary>
    public float LightRadiusFactor { get; init; } = 1.5f;
    public float LightMinRadius { get; init; } = 1.5f;
    public float LightMaxRadius { get; init; } = 10f;

    /// <summary>Plafond du multiplicateur de luminosite d'une lumiere (le champ vaut jusqu'a 100).</summary>
    public float LightMaxBrightness { get; init; } = 2f;
}

/// <summary>Comptes de la construction, pour le rapport et les tests.</summary>
public sealed record SwtorSceneStats(int Rooms, int ModelInstances, int DistinctModels, int MissingModels, int Meshes, long Triangles,
    int SkippedCollisionMeshes, int Lights, int Textures, int WalkableTriangles, int SolidTriangles)
{
    /// <summary>Instances dont la collision vient d'un mesh de collision du .gr2.</summary>
    public int CollisionInstances { get; init; }
    /// <summary>Instances sans mesh de collision : la geometrie visible sert (hors additifs).</summary>
    public int FallbackInstances { get; init; }
    /// <summary>Instances dont la collision vient d'un mesh <c>collision_override</c>.</summary>
    public int OverrideInstances { get; init; }
    /// <summary>Triangles de rendu par room exportee (pour choisir un sous-ensemble quand les limites BSP sont depassees).</summary>
    public IReadOnlyDictionary<string, int> RoomTriangles { get; init; } = new Dictionary<string, int>();
}

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
        string skyRoom = layout.Area.AreaProperties.Get("skyscene_room") is { Length: > 0 } sr ? sr : "skyscene_room";

        var materials = new SwtorMaterials(source, options.MaxTextureSize, log);
        var models = new Dictionary<string, Gr2Model?>(StringComparer.OrdinalIgnoreCase);
        var meshes = new List<SceneMesh>();
        var lights = new List<SceneLight>();
        var roomMeshes = new Dictionary<string, List<SceneMesh>>(StringComparer.OrdinalIgnoreCase);
        var roomCollision = new Dictionary<string, MeshAccumulator>(StringComparer.OrdinalIgnoreCase);
        AreaLighting areaLighting = AreaLighting.From(layout.Area.AreaProperties);
        var filter = options.Rooms is { Count: > 0 } r ? new HashSet<string>(r, StringComparer.OrdinalIgnoreCase) : null;

        int collisionInstances = 0, fallbackInstances = 0, overrideInstances = 0;
        int roomsUsed = 0, instances = 0, skippedCollision = 0, skippedHidden = 0, skippedKinds = 0, badMeshes = 0, noUv = 0;
        var skippedByKind = new Dictionary<InstanceKind, int>();
        long triangles = 0;
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (roomName, room) in layout.Rooms)
        {
            if (string.Equals(roomName, skyRoom, StringComparison.OrdinalIgnoreCase) || roomName.Equals("skyscene", StringComparison.OrdinalIgnoreCase)) continue;
            if (filter is not null ? !filter.Contains(roomName) : !options.IncludeSpaceRoom && IsSpaceRoom(roomName)) continue;
            roomsUsed++;
            var acc = new Dictionary<string, MeshAccumulator>(StringComparer.Ordinal);
            var order = new List<MeshAccumulator>();
            var collAcc = new MeshAccumulator(null);
            roomCollision[roomName] = collAcc;

            foreach (RoomInstance inst in room.Instances)
            {
                if (inst.Kind == InstanceKind.Light)
                {
                    AddLight(inst, layout, options, lights);
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

                IReadOnlyList<Gr2Mesh> collisionMeshes = options.UseCollisionMeshes ? SelectCollisionMeshes(model!) : [];
                bool fallback = collisionMeshes.Count == 0;
                if (options.UseCollisionMeshes)
                {
                    if (fallback) fallbackInstances++;
                    else
                    {
                        collisionInstances++;
                        if (collisionMeshes[0].Name.Contains("collision_override", StringComparison.OrdinalIgnoreCase)) overrideInstances++;
                        foreach (Gr2Mesh cm in collisionMeshes)
                            AddCollision(cm, model!, materials, collAcc, world, normalM, flip, options.Axes, inst.AssetPath, log);
                    }
                }

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
                        int first = piece.StartTriangle * 3, end = Math.Min(mesh.Indices.Length, (piece.StartTriangle + piece.TriangleCount) * 3);
                        if (first < 0 || first >= end) continue;
                        // Pas de mesh de collision dans le .gr2 : la geometrie visible (et les pieces de collision seule) sert de collision, hors additifs.
                        if (fallback && mat.Alpha != SurfaceAlpha.Additive) collAcc.Add(mesh, null, Vector2.One, world, normalM, flip, options.Axes, first, end);
                        if (mat.Hidden) { skippedHidden++; continue; }

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
        if (skippedCollision > 0) log.Warn("SWTOR_COLLISION_MESH_SKIPPED", options.UseCollisionMeshes
            ? $"{skippedCollision} collision mesh(es) not rendered; {collisionInstances} instance(s) use their collision mesh ({overrideInstances} with collision_override), {fallbackInstances} fall back to the visible geometry."
            : $"{skippedCollision} collision mesh(es) not rendered (the visible geometry serves as collision).");
        else if (fallbackInstances > 0) log.Warn("SWTOR_COLLISION_FALLBACK", $"{fallbackInstances} instance(s) without collision mesh use the visible geometry.");
        if (skippedHidden > 0) log.Warn("SWTOR_HIDDEN_PIECE_SKIPPED", $"{skippedHidden} piece(s) with a collision-only material skipped.");
        if (lights.Count > 0) log.Warn("SWTOR_LIGHTS_APPROX", $"{lights.Count} light(s) converted as point lights (radius = {options.LightRadiusFactor:0.##} x object scale, clamped to {options.LightMinRadius:0.##}..{options.LightMaxRadius:0.##} m; falloff ramp, spot cone and shadows not reproduced).");
        if (areaLighting.Fog is not null) log.Warn("SWTOR_FOG_IGNORED", "The area defines static fog; fog is not exported.");
        if (meshes.Count == 0) log.Warn("SWTOR_EMPTY_SCENE", $"{internalName}: no mesh produced (rooms: {roomsUsed}).");

        int walkableTris, solidTris;
        var walks = options.UseCollisionMeshes ? BuildWalkmeshes(roomCollision, options, out walkableTris, out solidTris)
            : BuildWalkmeshes(roomMeshes, options, out walkableTris, out solidTris);
        ModuleEntry entry = ChooseEntry(walks, meshes, roomMeshes, log);

        var info = new ModuleInfo(internalName, internalName, internalName, entry);
        bool hasSpace = layout.Rooms.Any(r => IsSpaceRoom(r.Name));
        var scene = ApplyAreaLighting(new ModuleScene(info, meshes, walks, materials.Textures)
        {
            Lights = lights,
            AreaLightingKnown = true,
            RenderMeshesAreSolid = !options.UseCollisionMeshes,
            SkyShader = hasSpace ? SpaceSkyShader : null,
        }, areaLighting, options, lights.Count > 0);
        var stats = new SwtorSceneStats(roomsUsed, instances, models.Count, missing.Count, meshes.Count, triangles, skippedCollision, lights.Count,
            materials.Textures.Count, walkableTris, solidTris)
        {
            CollisionInstances = collisionInstances,
            FallbackInstances = fallbackInstances,
            OverrideInstances = overrideInstances,
            RoomTriangles = roomMeshes.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(m => m.Triangles.Length / 3), StringComparer.OrdinalIgnoreCase),
        };
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

    /// <summary>
    /// Lumiere ponctuelle depuis les champs decodes (<see cref="LightFields"/>). Rayon = plus grande echelle de l'objet x facteur, borne ;
    /// multiplicateur = luminosite x gain (plafonne), reduit pour un projecteur dont le cone n'est pas reproduit. Lumiere noire ou eteinte ignoree.
    /// </summary>
    internal static bool AddLight(RoomInstance inst, AreaLayout layout, SwtorBuildOptions options, List<SceneLight> lights)
    {
        if (inst.Color is not { } c) return false;
        LightParams lp = LightFields.Read(inst);
        SceneLight? light = MakeLight(inst.Id, SwtorAxes.ToZUp(layout.WorldPosition(inst), options.Axes), new Vector3(c.X, c.Y, c.Z), lp, options);
        if (light is null) return false;
        lights.Add(light);
        return true;
    }

    internal static SceneLight? MakeLight(ulong id, Vector3 position, Vector3 rgb, LightParams lp, SwtorBuildOptions options)
    {
        Vector3 color = Vector3.Clamp(rgb, Vector3.Zero, Vector3.One);
        float multiplier = Math.Clamp(lp.Brightness * lp.Gain, 0f, options.LightMaxBrightness);
        if (lp.IsSpot) multiplier *= 0.6f;
        if (!float.IsFinite(multiplier) || multiplier < 0.02f || MathF.Max(color.X, MathF.Max(color.Y, color.Z)) < 0.02f) return null;
        float radius = Math.Clamp(lp.MaxExtent * options.LightRadiusFactor, options.LightMinRadius, options.LightMaxRadius);
        return new SceneLight("light_" + id.ToString("X"), position, color, radius, multiplier);
    }

    /// <summary>Ambiante, soleil et ciel de la zone (area.dat) : interieur = ambiante sombre + lumieres ; exterieur = soleil garde avec les lumieres.</summary>
    internal static ModuleScene ApplyAreaLighting(ModuleScene scene, AreaLighting area, SwtorBuildOptions options, bool hasLights)
    {
        Vector3 sunColor = Vector3.Clamp(area.Front * area.Intensity * 0.7f, Vector3.Zero, Vector3.One);
        if (area.IsOutdoor)
        {
            Vector3 ambient = Vector3.Clamp(area.Side * area.Intensity * 0.6f, new Vector3(0.15f), Vector3.One);
            return scene with { AmbientColor = ambient, SunDiffuseColor = sunColor, SunDirection = area.SunTravel, SunWithLights = true };
        }
        return scene with
        {
            AmbientColor = new Vector3(hasLights ? options.AmbientLit : options.AmbientUnlit),
            SunDiffuseColor = new Vector3(0.25f),
            SunDirection = area.SunTravel,
        };
    }

    /// <summary>
    /// Meshes de collision d'un modele : <c>collision_override*</c> seul s'il existe ; sinon les meshes de collision explicites (<c>collision</c>, <c>collision01</c>,
    /// <c>xx_collision</c>...) qui remplacent le <c>generated_collision</c> automatique ; sinon <c>generated_collision</c> ; sinon aucun (repli sur le visible).
    /// </summary>
    internal static IReadOnlyList<Gr2Mesh> SelectCollisionMeshes(Gr2Model model)
    {
        var all = model.Meshes.Where(m => IsCollisionName(m.Name) && m.Positions.Length > 0 && m.Indices.Length >= 3).ToList();
        if (all.Count == 0) return [];
        var over = all.Where(m => m.Name.Contains("collision_override", StringComparison.OrdinalIgnoreCase)).ToList();
        if (over.Count > 0) return over;
        var explicitMeshes = all.Where(m => !m.Name.Equals("generated_collision", StringComparison.OrdinalIgnoreCase)).ToList();
        return explicitMeshes.Count > 0 ? explicitMeshes : all;
    }

    /// <summary>Room d'exterieur spatial (<c>SPACE</c>, <c>outer_space</c>, <c>f2_bridge_space</c>, <c>cine_x_space_room01</c>) : un mot « space » delimite par '_' ou les bords.</summary>
    internal static bool IsSpaceRoom(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "(^|_)space(_|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    internal static bool IsCollisionName(string name) => name.Contains("collision", StringComparison.OrdinalIgnoreCase);

    /// <summary>Ajoute les triangles d'un mesh de collision (monde, Z haut) ; pieces au materiau additif (ecrans, champs de force) ignorees.</summary>
    private static void AddCollision(Gr2Mesh mesh, Gr2Model model, SwtorMaterials materials, MeshAccumulator acc, Matrix4x4 world, Matrix4x4 normalM,
        bool flip, AxisConvention axes, string? path, ImportLog log)
    {
        if (mesh.Indices.Any(i => i >= mesh.Positions.Length)) { log.Warn("SWTOR_MESH_BAD_INDEX", $"{path}/{mesh.Name}: triangle index out of range, collision mesh skipped."); return; }
        IReadOnlyList<Gr2Piece> pieces = mesh.Pieces.Count > 0 ? mesh.Pieces : [new Gr2Piece(0, mesh.Indices.Length / 3, -1, Vector3.Zero, Vector3.Zero)];
        foreach (Gr2Piece piece in pieces)
        {
            if (piece.MaterialIndex >= 0 && piece.MaterialIndex < model.Materials.Count && materials.IsAdditive(model.Materials[piece.MaterialIndex])) continue;
            int first = piece.StartTriangle * 3, end = Math.Min(mesh.Indices.Length, (piece.StartTriangle + piece.TriangleCount) * 3);
            if (first < 0 || first >= end) continue;
            acc.Add(mesh, null, Vector2.One, world, normalM, flip, axes, first, end);
        }
    }

    /// <summary>Ciel des zones qui ont une room SPACE (vaisseaux) : champ d'etoiles de JA.</summary>
    public const string SpaceSkyShader = "textures/skies/space";

    /// <summary>Un walkmesh par room depuis la collision : marchable si la normale pointe vers le haut.</summary>
    private static List<SceneWalkmesh> BuildWalkmeshes(Dictionary<string, MeshAccumulator> roomCollision, SwtorBuildOptions options,
        out int walkableTris, out int solidTris)
    {
        walkableTris = 0; solidTris = 0;
        var result = new List<SceneWalkmesh>();
        foreach (var (room, acc) in roomCollision)
        {
            if (acc.Triangles.Count == 0) continue;
            var walk = new bool[acc.Triangles.Count / 3];
            for (int t = 0; t < walk.Length; t++)
                walk[t] = IsWalkable(acc.Positions[acc.Triangles[t * 3]], acc.Positions[acc.Triangles[t * 3 + 1]], acc.Positions[acc.Triangles[t * 3 + 2]], options.WalkableMinNormalZ);
            walkableTris += walk.Count(w => w);
            solidTris += walk.Count(w => !w);
            result.Add(new SceneWalkmesh(room, [.. acc.Positions], [.. acc.Triangles], walk));
        }
        return result;
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
        var candidates = rooms.Where(r => !IsSpaceRoom(r.Room)).ToList();
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
    private sealed class MeshAccumulator(SwtorMaterialInfo? material)
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
            new(room, material!.Key, material.Key, null, TxiInfo.Empty, 0, [.. Positions], [.. Normals], [.. Uv], null, [.. Triangles])
            {
                Alpha = material.Alpha,
            };
    }
}

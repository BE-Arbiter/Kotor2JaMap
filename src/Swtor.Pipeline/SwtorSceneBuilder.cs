using Swtor.Core.Dds;
using System.Globalization;
using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Gr2;
using Swtor.Core.Terrain;

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

    /// <summary>Multiplicateur manuel de l echelle du monde, applique en plus de la constante derivee (<see cref="SwtorWorldScale.MetresPerGr2Unit"/>) ; 0 ou 1 = aucun.</summary>
    public float WorldScale { get; init; } = 0f;

    /// <summary>Plafond du multiplicateur de luminosite d'une lumiere (le champ vaut jusqu'a 100).</summary>
    public float LightMaxBrightness { get; init; } = 2f;

    /// <summary>Decalage de l origine (unites GR2, plan X/Z) soustrait aux positions ; calcule automatiquement par <c>Build</c> quand la zone exportee est trop loin de l origine pour les limites du monde JA (131072).</summary>
    public Vector3 OriginOffset { get; init; } = Vector3.Zero;

    /// <summary>Convertit le terrain (objets heightmap des zones exterieures) en sol ; faux = zones sans sol comme avant.</summary>
    public bool TerrainEnabled { get; init; } = true;

    /// <summary>Triangles de rendu au plus pour tout le terrain de la zone ; 0 = aucune limite (pleine resolution, 0,2 unite par cellule) : le PVS par room evite de tout dessiner, le budget BSP ne s applique pas au sol. Sinon le pas de la grille est choisi pour tenir dedans.</summary>
    public int TerrainMaxTriangles { get; init; } = 0;

    /// <summary>Triangles de rendu au moins pour qu'un modele place sorte du BSP en MD3 + misc_model_static (voir <see cref="SceneModel"/>) ; 0 = tout reste dans le BSP.</summary>
    public int StaticModelMinTriangles { get; init; } = 200;
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
    /// <summary>Dalles de terrain converties en sol.</summary>
    public int TerrainTiles { get; init; }
    /// <summary>Triangles de rendu du sol (inclus dans <c>Triangles</c>).</summary>
    public int TerrainTriangles { get; init; }
    /// <summary>Pas d'echantillonnage de la grille de terrain (1 = 0,2 unite par cellule, sans decimation).</summary>
    public int TerrainStep { get; init; }
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
        float worldScale = SwtorWorldScale.Resolve(options.WorldScale);
        log.Warn("SWTOR_WORLD_SCALE", $"World scale {worldScale:0.###} m per GR2 unit ({worldScale * SwtorWorldScale.JaUnitsPerMetre:0.#} JA units, calibrated on JA stairs, seats and doors; SWTOR human {SwtorWorldScale.HumanHeightGr2} units = {SwtorWorldScale.HumanHeightGr2 * SwtorWorldScale.JaUnitsPerGr2Unit:0.#} JA units; manual multiplier --world-scale = {(options.WorldScale > 0f ? options.WorldScale : 1f):0.###}). Not verified in game.");
        options = options with { WorldScale = worldScale };
        var clock = System.Diagnostics.Stopwatch.StartNew();
        log.Progress(0.02, "Centering the area");
        options = options with { OriginOffset = ComputeOriginOffset(layout, options, worldScale, log) };
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
        var terrainTiles = new List<SwtorTerrainTile>();
        var planetCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var terrainErrors = new Dictionary<string, int>(StringComparer.Ordinal);
        int terrainSkipped = 0;
        var localModels = new List<SceneModel>();
        var localModelIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // -1 : reste dans le BSP
        var modelInstances = new List<SceneModelInstance>();
        var roomInstanceTriangles = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // Repere local des modeles : axes de la scene (Z haut), sans transformation d'instance. instM = zUp^-1 * world * zUp.
        Matrix4x4 zUp = AxisMatrix(options.Axes);
        Matrix4x4.Invert(zUp, out Matrix4x4 zUpInverse);

        // Total pour la barre de progression : toutes les instances des rooms retenues.
        bool Included(string roomName) =>
            !(string.Equals(roomName, skyRoom, StringComparison.OrdinalIgnoreCase) || roomName.Equals("skyscene", StringComparison.OrdinalIgnoreCase))
            && (filter is not null ? filter.Contains(roomName) : options.IncludeSpaceRoom || !IsSpaceRoom(roomName));
        int plannedRooms = 0, plannedInstances = 0;
        foreach (var (roomName, room) in layout.Rooms)
        {
            if (!Included(roomName)) continue;
            plannedRooms++;
            plannedInstances += room.Instances.Count;
        }
        log.Info($"Building the scene: {plannedRooms} room(s), {plannedInstances:N0} instance(s) to place (the first load of models and textures is the slow part).");
        int processed = 0, roomIndex = 0, nextReport = 0;
        const int ReportEvery = 20;
        double RoomsFraction() => 0.05 + 0.80 * (plannedInstances > 0 ? (double)processed / plannedInstances : 1d);

        foreach (var (roomName, room) in layout.Rooms)
        {
            if (!Included(roomName)) continue;
            roomsUsed++;
            roomIndex++;
            var roomClock = System.Diagnostics.Stopwatch.StartNew();
            int roomModelsAtStart = models.Count;
            long roomTrianglesAtStart = triangles;
            log.Info($"Room {roomIndex}/{plannedRooms} '{roomName}': {room.Instances.Count:N0} instance(s)...");
            log.Progress(RoomsFraction(), $"Room {roomIndex}/{plannedRooms} '{roomName}'");
            var acc = new Dictionary<string, MeshAccumulator>(StringComparer.Ordinal);
            var order = new List<MeshAccumulator>();
            var collAcc = new MeshAccumulator(null);
            roomCollision[roomName] = collAcc;
            int roomInstance = 0;

            foreach (RoomInstance inst in room.Instances)
            {
                processed++;
                roomInstance++;
                if (roomInstance % 250 == 0)
                    log.Info($"  '{roomName}': {roomInstance:N0}/{room.Instances.Count:N0} instances, {models.Count:N0} model(s) loaded ({missing.Count:N0} missing), {materials.Textures.Count:N0} texture(s), {clock.Elapsed.TotalSeconds:0}s elapsed.");
                if (processed >= nextReport)
                {
                    nextReport = processed + ReportEvery;
                    log.Progress(RoomsFraction(), $"Room {roomIndex}/{plannedRooms} '{roomName}': {roomInstance:N0}/{room.Instances.Count:N0} instances, {models.Count:N0} models, {materials.Textures.Count:N0} textures");
                }
                if (inst.Kind == InstanceKind.Light)
                {
                    AddLight(inst, layout, options, lights);
                    continue;
                }
                if (inst.Kind == InstanceKind.Heightmap && options.TerrainEnabled)
                {
                    if (TryReadTerrain(inst, out HeightmapTile? tile, out string? terrainError))
                        terrainTiles.Add(new SwtorTerrainTile(roomName, tile!, layout.World(inst) * Matrix4x4.CreateTranslation(-options.OriginOffset) * Matrix4x4.CreateScale(worldScale)));
                    else
                    {
                        terrainSkipped++;
                        terrainErrors[terrainError!] = terrainErrors.GetValueOrDefault(terrainError!) + 1;
                    }
                    continue;
                }
                if (inst.Kind != InstanceKind.Model || inst.AssetPath is null)
                {
                    if (inst.Kind is not (InstanceKind.Unknown or InstanceKind.Other)) skippedByKind[inst.Kind] = skippedByKind.GetValueOrDefault(inst.Kind) + 1;
                    skippedKinds++;
                    continue;
                }
                instances++;
                if (PlanetFolder(inst.AssetPath) is { } planet) planetCount[planet] = planetCount.GetValueOrDefault(planet) + 1;
                if (!TryGetModel(inst.AssetPath, source, models, missing, log, out Gr2Model? model)) continue;

                Matrix4x4 world = layout.World(inst) * Matrix4x4.CreateTranslation(-options.OriginOffset) * Matrix4x4.CreateScale(worldScale);
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

                // Objet assez lourd, sans miroir : rendu par un MD3 pose en misc_model_static (l'export choisit sous le plafond du moteur
                // et remet le reste dans le BSP). La collision reste celle de l'instance.
                // L'echelle (monde et instance) est mise dans les sommets du modele, un modele par echelle : l'entite ne porte qu'une rotation et
                // une position. Un modelscale fausse l'eclairage du moteur (modelLightDir = lightDir . axes mis a l'echelle, rd-vulkan tr_light.cpp:378).
                bool asModel = false;
                if (options.StaticModelMinTriangles > 0 && world.GetDeterminant() > 0f
                    && Matrix4x4.Decompose(world, out Vector3 instScale, out Quaternion instRot, out Vector3 instPos)
                    && instScale.X > 0f && instScale.Y > 0f && instScale.Z > 0f)
                {
                    Matrix4x4 rigid = Matrix4x4.CreateFromQuaternion(instRot) * Matrix4x4.CreateTranslation(instPos);
                    Matrix4x4 rebuilt = Matrix4x4.CreateScale(instScale) * rigid;
                    string key = string.Create(CultureInfo.InvariantCulture, $"{inst.AssetPath}|{instScale.X:0.###}|{instScale.Y:0.###}|{instScale.Z:0.###}");
                    if (!localModelIndex.TryGetValue(key, out int mi))
                    {
                        SceneModel local = BuildLocalModel(inst.AssetPath, model!, materials, options.Axes, instScale);
                        mi = local.Triangles >= options.StaticModelMinTriangles ? localModels.Count : -1;
                        if (mi >= 0) localModels.Add(local);
                        localModelIndex[key] = mi;
                    }
                    Matrix4x4 instM = zUpInverse * rigid * zUp;
                    // Pas de cisaillement : la decomposition doit redonner la matrice.
                    bool exact = Enumerable.Range(0, 16).All(k => MathF.Abs(rebuilt[k / 4, k % 4] - world[k / 4, k % 4]) <= 1e-3f * MathF.Max(1f, MathF.Abs(world[k / 4, k % 4])));
                    if (mi >= 0 && exact && MiscModelTransform.FromMatrix(instM) is not null)
                    {
                        modelInstances.Add(new SceneModelInstance(mi, roomName, instM));
                        long t = localModels[mi].Triangles;
                        triangles += t;
                        roomInstanceTriangles[roomName] = roomInstanceTriangles.GetValueOrDefault(roomName) + t;
                        asModel = true;
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
                        if (fallback && !mat.NoCollision && mat.Alpha is not (SurfaceAlpha.Additive or SurfaceAlpha.Multiply)) collAcc.Add(mesh, null, Vector2.One, world, normalM, flip, options.Axes, first, end);
                        if (mat.Hidden) { skippedHidden++; continue; }
                        if (asModel) continue;

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

            log.Progress(RoomsFraction(), $"Room {roomIndex}/{plannedRooms} '{roomName}': assembling meshes");
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
            log.Info($"Room {roomIndex}/{plannedRooms} '{roomName}' done in {roomClock.Elapsed.TotalSeconds:0.0}s: {list.Count} mesh(es), {triangles - roomTrianglesAtStart:N0} triangle(s), {models.Count - roomModelsAtStart:N0} new model(s).");
        }
        if (modelInstances.Count > 0)
            log.Info($"Static models: {modelInstances.Count:N0} instance(s) of {localModels.Count:N0} model(s) with at least {options.StaticModelMinTriangles:N0} triangles kept apart for misc_model_static ({roomInstanceTriangles.Values.Sum():N0} triangle(s)).");
        log.Info($"Rooms built in {clock.Elapsed.TotalSeconds:0.0}s: {instances:N0} model instance(s), {models.Count:N0} distinct model(s) ({missing.Count:N0} missing), {materials.Textures.Count:N0} texture(s), {triangles:N0} triangle(s).");

        int terrainTriangles = 0, terrainStep = 0;
        if (terrainTiles.Count > 0)
        {
            log.Progress(0.86, $"Terrain: {terrainTiles.Count} tile(s)");
            log.Info($"Converting {terrainTiles.Count} terrain tile(s)...");
            terrainTriangles = AddTerrain(terrainTiles, layout.Area.TerrainLayers, options, worldScale, triangles, materials, source, planetCount.Where(kv => kv.Key != "all_all" && kv.Key != "_test").OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList(), meshes, roomMeshes, roomCollision, log, out terrainStep);
        }
        triangles += terrainTriangles;
        foreach (var (error, count) in terrainErrors.OrderBy(k => k.Key, StringComparer.Ordinal))
            log.Warn("SWTOR_TERRAIN_UNSUPPORTED", $"{count} terrain tile(s) skipped: {error}.");

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

        log.Progress(0.90, "Building the walkmeshes");
        log.Info("Building the walkmeshes (collision)...");
        int walkableTris, solidTris;
        var walks = options.UseCollisionMeshes ? BuildWalkmeshes(roomCollision, options, out walkableTris, out solidTris)
            : BuildWalkmeshes(roomMeshes, options, out walkableTris, out solidTris);
        log.Info($"Walkmeshes: {walkableTris:N0} walkable + {solidTris:N0} solid triangle(s).");
        // Geometrie visible d'une room, instances de modeles comprises (hauteur de reference du point d'apparition).
        IEnumerable<Vector3> RoomPositions(string room) =>
            (roomMeshes.TryGetValue(room, out var list) ? list.SelectMany(m => m.Positions) : [])
            .Concat(modelInstances.Where(i => string.Equals(i.Room, room, StringComparison.OrdinalIgnoreCase))
                .SelectMany(i => localModels[i.Model].Bake(i.Transform, room)).SelectMany(m => m.Positions));
        ModuleEntry entry = ChooseEntry(walks, RoomPositions, log);
        log.Progress(0.95, "Computing room visibility");
        log.Info("Computing room visibility...");

        var info = new ModuleInfo(internalName, internalName, internalName, entry);
        bool hasSpace = layout.Rooms.Any(r => IsSpaceRoom(r.Name));
        var scene = ApplyAreaLighting(new ModuleScene(info, meshes, walks, materials.Textures)
        {
            Lights = lights,
            AreaLightingKnown = true,
            RenderMeshesAreSolid = !options.UseCollisionMeshes,
            CompactRenderSurfaces = true,
            RoomVisibility = SwtorVisibility.Build(layout, roomMeshes.Keys, log),
            Models = localModels,
            ModelInstances = modelInstances,
            SkyShader = hasSpace ? SpaceSkyShader : null,
        }, areaLighting, options, lights.Count > 0);
        var stats = new SwtorSceneStats(roomsUsed, instances, models.Count, missing.Count, meshes.Count, triangles, skippedCollision, lights.Count,
            materials.Textures.Count, walkableTris, solidTris)
        {
            CollisionInstances = collisionInstances,
            FallbackInstances = fallbackInstances,
            OverrideInstances = overrideInstances,
            RoomTriangles = roomMeshes.ToDictionary(kv => kv.Key, kv => kv.Value.Sum(m => m.Triangles.Length / 3) + (int)roomInstanceTriangles.GetValueOrDefault(kv.Key), StringComparer.OrdinalIgnoreCase),
            TerrainTiles = terrainTiles.Count,
            TerrainTriangles = terrainTriangles,
            TerrainStep = terrainStep,
        };
        log.Progress(1.0, "Scene ready");
        log.Info($"Scene built in {clock.Elapsed.TotalSeconds:0.0}s.");
        return new SwtorSceneResult(scene, stats);
    }

    /// <summary>Lit le blob de terrain d'un objet heightmap ; faux (avec la raison) si le format de cette dalle n'est pas gere (version 9, dalle degeneree...).</summary>
    private static bool TryReadTerrain(RoomInstance inst, out HeightmapTile? tile, out string? error)
    {
        tile = null;
        error = null;
        byte[]? blob = inst.Fields.Where(f => f.Type == 9 && f.Value is byte[] { Length: > 0 }).Select(f => (byte[])f.Value).FirstOrDefault();
        if (blob is null) { error = "no data blob"; return false; }
        try
        {
            tile = HeightmapTile.ReadBlob(blob);
            return true;
        }
        catch (GameFormatException ex)
        {
            error = ex.Message.Split('(')[0].Trim();
            return false;
        }
    }

    /// <summary>Dossier de planete (art/static/area/<c>dossier</c>/...) d'un chemin de modele, null si le chemin n'est pas range par planete.</summary>
    internal static string? PlanetFolder(string? path)
    {
        if (path is null) return null;
        string[] parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        int i = Array.FindIndex(parts, p => p.Equals("area", StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 2 < parts.Length ? parts[i + 1] : null;
    }

    private static readonly string[] GroundKinds = ["grass", "moss", "dirt", "mud", "ground", "swamp", "sand", "rock", "rubble"];
    private static readonly string[] BadGroundLayer = ["metal", "debris", "snow", "path", "cliff", "ember", "puddle", "trash", "mask", "alpha", "ruin", "city", "tech", "foundation", "kolto", "wave"];
    private static readonly string[] GroundSuffixes = ["01", "_01", "02", "_02", ""];

    /// <summary>
    /// Vraie texture de sol de la planete : <c>art/static/area/&lt;planete&gt;/terrain_map/ter_&lt;code&gt;_&lt;sorte&gt;&lt;n&gt;_d.dds</c>, la premiere trouvee dans l'ordre herbe, mousse, terre, boue...
    /// (repli des dalles sans carte de melange lisible : une seule couche pour ces dalles). Null si la planete n'est pas connue ou n'a rien de tel.
    /// </summary>
    private static KotorTexture? LoadPlanetGround(ISwtorModelSource source, List<string> planets, ImportLog log)
    {
        foreach (string planet in planets)
        {
            string[] listed = [.. source.ListFiles($"art/static/area/{planet}/terrain_map").Where(f => f.StartsWith("ter_", StringComparison.OrdinalIgnoreCase) && f.EndsWith("_d.dds", StringComparison.OrdinalIgnoreCase) && !f.Contains("tiny", StringComparison.OrdinalIgnoreCase) && !BadGroundLayer.Any(b => f.Contains(b, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => { int k = Array.FindIndex(GroundKinds, w => f.Contains(w, StringComparison.OrdinalIgnoreCase)); return k < 0 ? 99 : k; }).ThenBy(f => f, StringComparer.OrdinalIgnoreCase)];
        string code = planet.Contains('_') ? planet[..planet.IndexOf('_')] : planet;
            IEnumerable<string> names = listed.Length > 0 ? listed.Select(f => f[..^4]) : GroundKinds.SelectMany(k => GroundSuffixes.Select(s => $"ter_{code}_{k}{s}_d"));
            // Plusieurs couches d'herbe existent (ex. Tython : grass_01 olive sombre, grass_04 vert franc) : on garde la plus verte de la premiere sorte trouvee.
            var candidates = new List<(string Name, DdsImage Img, double Score)>();
            int bestKind = int.MaxValue;
            foreach (string name in names)
            {
                int kind = Array.FindIndex(GroundKinds, w => name.Contains(w, StringComparison.OrdinalIgnoreCase));
                if (kind < 0) kind = 99;
                if (candidates.Count > 0 && kind > bestKind) break;
                DdsImage? loaded;
                try { loaded = source.LoadTexture($"art/static/area/{planet}/terrain_map/{name}"); }
                catch (GameFormatException) { continue; }
                if (loaded is null) continue;
                bestKind = kind;
                long r = 0, g = 0, b = 0, n = 0;
                byte[] px = loaded.Rgba;
                for (int i = 0; i + 3 < px.Length; i += 16) { r += px[i]; g += px[i + 1]; b += px[i + 2]; n++; }
                double score = n == 0 ? 0 : (g - 0.5 * (r + b)) / n;
                candidates.Add((name, loaded, score));
            }
            foreach (var (name, img, _) in candidates.OrderByDescending(c => c.Score))
            {
                byte[] rgba = img.Rgba;
                int w = img.Width, h = img.Height;
                while (w > 512 && h > 512 && w % 2 == 0 && h % 2 == 0)
                {
                    var half = new byte[w / 2 * (h / 2) * 4];
                    for (int y = 0; y < h / 2; y++)
                        for (int x = 0; x < w / 2; x++)
                            for (int c = 0; c < 4; c++)
                                half[(y * (w / 2) + x) * 4 + c] = (byte)((rgba[((2 * y) * w + 2 * x) * 4 + c] + rgba[((2 * y) * w + 2 * x + 1) * 4 + c] + rgba[((2 * y + 1) * w + 2 * x) * 4 + c] + rgba[((2 * y + 1) * w + 2 * x + 1) * 4 + c]) / 4);
                    rgba = half; w /= 2; h /= 2;
                }
                // Les couches de terrain sont tres sombres (moyenne ~70/255) : on remonte la luminosite moyenne vers ~110 pour que l'herbe se lise en jeu.
                long sum = 0;
                for (int i = 0; i + 2 < rgba.Length; i += 4) sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
                double mean = sum / (3.0 * (rgba.Length / 4));
                double gain = mean > 1 ? Math.Clamp(110.0 / mean, 1.0, 1.8) : 1.0;
                if (gain > 1.01)
                    for (int i = 0; i + 2 < rgba.Length; i += 4)
                        for (int c = 0; c < 3; c++) rgba[i + c] = (byte)Math.Min(255, (int)(rgba[i + c] * gain));
                for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
                log.Warn("SWTOR_TERRAIN_TEXTURE", $"Terrain textured with the planet ground layer '{name}' (fallback for tiles without a readable blend map).");
                return new KotorTexture(w, h, rgba, false, TxiInfo.Empty);
            }
        }
        log.Warn("SWTOR_TERRAIN_TEXTURE", $"No ground layer texture found in the planet folders used by the models ({string.Join(", ", planets)}).");
        return null;
    }

    private static readonly string[] GroundWords = ["dirt", "grass", "ground", "moss", "mud", "soil", "sand", "rock", "stone", "gravel", "swamp", "earth", "rubble", "cliff"];

    private static readonly string[] NotGroundWords = ["glow", "acid", "emis", "lava", "fire", "water", "neon", "holo", "screen", "light", "lamp", "fx_", "sky", "liquid", "slime", "toxic", "gel", "glass", "metal", "tech", "panel", "wall", "trim", "floor", "door", "stair"];

    /// <summary>Faux pour les materiaux qui ne peuvent pas etre un sol (lumineux, liquides, metal...) : la couleur de sol empruntee ne doit pas etre un acide vert fluo.</summary>
    private static bool NotGroundLike(string key) =>
        NotGroundWords.Any(w => key.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ajoute le sol : une grille par dalle, decimee au pas qui tient dans le budget, et sa collision dans le walkmesh de la room. Les couches de la
    /// carte de melange (<see cref="SwtorTerrainLayers"/>) donnent, par room, un mesh par couche seule et un par paire de couches fondues
    /// (<see cref="SwtorTerrain.SplitByLayers"/>) ; les dalles sans carte prennent le sol de repli (<see cref="ChooseFallbackGround"/>).
    /// </summary>
    private static int AddTerrain(List<SwtorTerrainTile> tiles, IReadOnlyDictionary<ulong, string> layerNames, SwtorBuildOptions options, float worldScale, long modelTriangles,
        SwtorMaterials materials, ISwtorModelSource source, List<string> planets,
        List<SceneMesh> meshes, Dictionary<string, List<SceneMesh>> roomMeshes, Dictionary<string, MeshAccumulator> roomCollision, ImportLog log, out int step)
    {
        long cap = options.TerrainMaxTriangles > 0
            ? options.TerrainMaxTriangles
            : long.MaxValue;
        step = SwtorTerrain.ChooseStep([.. tiles.Select(t => t.Tile)], cap);
        float uvPeriod = 3.2f * worldScale;
        var layers = new SwtorTerrainLayers(materials, layerNames, log);
        var perRoom = new Dictionary<string, SwtorTerrainMesh>(StringComparer.OrdinalIgnoreCase);
        foreach (IGrouping<string, SwtorTerrainTile> g in tiles.GroupBy(t => t.Room, StringComparer.OrdinalIgnoreCase))
            perRoom[g.Key] = SwtorTerrain.BuildMesh(g.Select(t => (t.Tile, t.World)), step, p => SwtorAxes.ToZUp(p, options.Axes), SwtorAxes.FlipsWinding(options.Axes), uvPeriod,
                layers.Slots, SwtorTerrainLayers.Fallback);
        layers.Finish();
        var parts = perRoom.ToDictionary(kv => kv.Key, kv => SwtorTerrain.SplitByLayers(kv.Value), StringComparer.OrdinalIgnoreCase);

        string ground = FlatGroundTexture;
        SurfaceAlpha alpha = SurfaceAlpha.Opaque;
        int fallbackTriangles = parts.Values.Sum(ps => ps.Where(p => p.Base == SwtorTerrainLayers.Fallback).Sum(p => p.Triangles.Length / 3));
        if (fallbackTriangles > 0)
        {
            if (layers.Count > 1) log.Warn("SWTOR_TERRAIN_TEXTURE", $"{fallbackTriangles:N0} terrain triangle(s) without a readable blend map layer use the fallback ground.");
            (ground, alpha) = ChooseFallbackGround(perRoom, worldScale, materials, source, planets, meshes, log);
        }

        int total = 0, blended = 0;
        foreach (var (room, mesh) in perRoom)
        {
            if (mesh.Triangles.Length == 0) continue;
            if (!roomMeshes.TryGetValue(room, out var list)) roomMeshes[room] = list = [];
            foreach (SwtorTerrainPart part in parts[room])
            {
                bool fallback = part.Base == SwtorTerrainLayers.Fallback;
                Vector2 scale = layers.Scale(part.Base);
                Vector2[] uv = [.. part.Uv.Select(u => u * scale)];
                var sm = new SceneMesh(room, TerrainNode, fallback ? ground : layers.Key(part.Base), null, TxiInfo.Empty, 0, part.Positions, part.Normals, uv, null, part.Triangles)
                {
                    Alpha = fallback ? alpha : SurfaceAlpha.Opaque,
                    OverlayTexture = part.Overlay >= 0 ? layers.Key(part.Overlay) : null,
                    OverlayUvScale = part.Overlay >= 0 ? layers.Scale(part.Overlay) / scale : Vector2.One,
                    VertexAlpha = part.Alpha,
                };
                meshes.Add(sm);
                list.Add(sm);
                if (part.Overlay >= 0) blended += part.Triangles.Length / 3;
            }
            if (roomCollision.TryGetValue(room, out MeshAccumulator? coll)) coll.AddRaw(mesh);
            total += mesh.Triangles.Length / 3;
        }
        int pairs = parts.Values.SelectMany(ps => ps).Where(p => p.Overlay >= 0).Select(p => (p.Base, p.Overlay)).Distinct().Count();
        log.Warn("SWTOR_TERRAIN", $"{tiles.Count} terrain tile(s) converted to {total:N0} triangles (grid step {step} = {step * HeightmapTile.CellSize:0.##} GR2 units per cell, budget {cap:N0}); "
            + $"{layers.Count - 1} ground layer(s), {blended:N0} triangle(s) blending two layers ({pairs} pair shader(s)).");
        return total;
    }

    /// <summary>
    /// Sol de repli (dalles sans carte de melange) : vraie couche de sol de la planete (<see cref="LoadPlanetGround"/>), sinon le materiau le plus
    /// represente parmi les surfaces tournees vers le haut posees sur le terrain (<see cref="SwtorTerrain.ChooseGroundTexture"/>), sinon une couleur de sable.
    /// </summary>
    private static (string Ground, SurfaceAlpha Alpha) ChooseFallbackGround(Dictionary<string, SwtorTerrainMesh> perRoom, float worldScale, SwtorMaterials materials,
        ISwtorModelSource source, List<string> planets, List<SceneMesh> meshes, ImportLog log)
    {
        KotorTexture? real = LoadPlanetGround(source, planets, log);
        Func<string,bool> has = k => materials.Textures.TryGetValue(k, out var tex) && tex is not null;
        IEnumerable<Vector3> tv = perRoom.Values.SelectMany(m => m.Positions);
        SurfaceAlpha alpha = SurfaceAlpha.Opaque;
        string? ground = real is not null ? FlatGroundTexture : SwtorTerrain.ChooseGroundTexture(meshes, tv, 0.25f * worldScale, k => has(k) && !NotGroundLike(k) && GroundWords.Any(w => k.Contains(w, StringComparison.OrdinalIgnoreCase)), out alpha)
            ?? SwtorTerrain.ChooseGroundTexture(meshes, tv, 0.25f * worldScale, k => has(k) && !NotGroundLike(k), out alpha);
        if (real is not null)
        {
            materials.Textures[FlatGroundTexture] = real;
            alpha = SurfaceAlpha.Opaque;
        }
        else
        if (real is null && ground is null)
        {
            ground = FlatGroundTexture;
            alpha = SurfaceAlpha.Opaque;
            byte[] rgba = new byte[4 * 4 * 4];
            for (int i = 0; i < rgba.Length; i += 4) { rgba[i] = 150; rgba[i + 1] = 132; rgba[i + 2] = 105; rgba[i + 3] = 255; }
            materials.Textures[ground] = new KotorTexture(4, 4, rgba, false, TxiInfo.Empty);
            log.Warn("SWTOR_TERRAIN_TEXTURE", "No ground-level surface found to borrow a texture from: terrain drawn with a flat sand colour (tiles without a readable blend map).");
        }
        else if (real is null)
        {
            // La texture d'une roche etiree en projection plane sur des pentes donne des stries : on n'en garde que les basses frequences (16 x 16, tirees vers la moyenne).
            KotorTexture src = materials.Textures[ground!]!;
            materials.Textures[FlatGroundTexture] = SwtorTerrain.SmoothGroundTexture(src);
            log.Warn("SWTOR_TERRAIN_TEXTURE", $"Terrain coloured from '{ground}', the material most used by up-facing surfaces lying on the ground, reduced to a smooth 16x16 tile (tiles without a readable blend map).");
            ground = FlatGroundTexture;
            alpha = SurfaceAlpha.Opaque;
        }
        return (ground ?? FlatGroundTexture, alpha);
    }

    /// <summary>Cle de la texture unie de repli du sol.</summary>
    internal const string FlatGroundTexture = "swtor_terrain";

    /// <summary>Nom de noeud des meshes de sol (diagnostic, apercus).</summary>
    public const string TerrainNode = "terrain";

    private static bool TryGetModel(string path, ISwtorModelSource source, Dictionary<string, Gr2Model?> cache, HashSet<string> missing,
        ImportLog log, out Gr2Model? model)
    {
        if (cache.TryGetValue(path, out model)) return model is not null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            model = source.LoadModel(path);
            if (clock.ElapsedMilliseconds >= 500)
                log.Info($"  slow model {path}: {clock.Elapsed.TotalSeconds:0.0}s" + (model is null ? " (not found)" : $", {model.Meshes.Count} mesh(es)"));
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
    /// Les limites du monde JA sont de +-131072 unites : si le centre (X/Z) des modeles exportes est plus loin que 60000 unites JA de l origine,
    /// renvoie le decalage (unites GR2) qui le ramene a l origine (sinon zero). Log <c>SWTOR_RECENTERED</c>.
    /// </summary>
    internal static Vector3 ComputeOriginOffset(AreaLayout layout, SwtorBuildOptions options, float worldScale, ImportLog log)
    {
        if (options.OriginOffset != Vector3.Zero) return options.OriginOffset;
        string skyRoom = layout.Area.AreaProperties.Get("skyscene_room") is { Length: > 0 } sr ? sr : "skyscene_room";
        var filter = options.Rooms is { Count: > 0 } r ? new HashSet<string>(r, StringComparer.OrdinalIgnoreCase) : null;
        Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
        foreach (var (roomName, room) in layout.Rooms)
        {
            if (string.Equals(roomName, skyRoom, StringComparison.OrdinalIgnoreCase) || roomName.Equals("skyscene", StringComparison.OrdinalIgnoreCase)) continue;
            if (filter is not null ? !filter.Contains(roomName) : !options.IncludeSpaceRoom && IsSpaceRoom(roomName)) continue;
            foreach (RoomInstance inst in room.Instances)
            {
                if (inst.Kind != InstanceKind.Model || inst.AssetPath is null) continue;
                Vector3 p = layout.WorldPosition(inst);
                mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p);
            }
        }
        if (mn.X > mx.X) return Vector3.Zero;
        var centre = new Vector3((mn.X + mx.X) / 2f, 0f, (mn.Z + mx.Z) / 2f);
        float ja = worldScale * SwtorWorldScale.JaUnitsPerMetre;
        float far = MathF.Max(MathF.Abs(centre.X), MathF.Abs(centre.Z)) * ja;
        float half = MathF.Max(mx.X - mn.X, mx.Z - mn.Z) / 2f * ja;
        if (far + half <= 60000f) return Vector3.Zero;
        log.Warn("SWTOR_RECENTERED", FormattableString.Invariant($"Area centre ({centre.X:F2}, {centre.Z:F2}) GR2 units is {far:F0} JA units from the origin (world limit 131072): scene translated by (-{centre.X:F2}, -{centre.Z:F2}) GR2 units."));
        return centre;
    }

    /// <summary>
    /// Lumiere ponctuelle depuis les champs decodes (<see cref="LightFields"/>). Rayon = plus grande echelle de l'objet x facteur, borne ;
    /// multiplicateur = luminosite x gain (plafonne), reduit pour un projecteur dont le cone n'est pas reproduit. Lumiere noire ou eteinte ignoree.
    /// </summary>
    internal static bool AddLight(RoomInstance inst, AreaLayout layout, SwtorBuildOptions options, List<SceneLight> lights)
    {
        if (inst.Color is not { } c) return false;
        LightParams lp = LightFields.Read(inst);
        SceneLight? light = MakeLight(inst.Id, SwtorAxes.ToZUp((layout.WorldPosition(inst) - options.OriginOffset) * options.WorldScale, options.Axes), new Vector3(c.X, c.Y, c.Z), lp, options);
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
        float radius = Math.Clamp(lp.MaxExtent * options.LightRadiusFactor, options.LightMinRadius, options.LightMaxRadius) * (options.WorldScale > 0f ? options.WorldScale : 1f);
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
            if (piece.MaterialIndex >= 0 && piece.MaterialIndex < model.Materials.Count && materials.IsNonSolid(model.Materials[piece.MaterialIndex])) continue;
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
                if (m.Alpha is SurfaceAlpha.Additive or SurfaceAlpha.Multiply) continue;
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
    /// de 2 m au-dessus (joueur JA : 64 unites = 1,6 m a l echelle 40) ; direction vers le centre de la room marchable suivante, sinon +X.
    /// </summary>
    internal static ModuleEntry ChooseEntry(IReadOnlyList<SceneWalkmesh> walks, Func<string, IEnumerable<Vector3>> roomPositions, ImportLog log)
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
        // Niveau principal : hauteur (tranches de 10 cm) qui porte le plus de surface marchable, parmi celles proches de la geometrie visible de la room
        // (mediane des Z des sommets, +-4 m scene = 160 u JA) : evite un plan de collision geant tres bas, un toit ou une passerelle.
        int mainLevel = levelArea.OrderByDescending(kv => kv.Value).First().Key;
        float[] zs = [.. roomPositions(best.Room).Select(p => p.Z).Order()];
        if (zs.Length > 0)
        {
            float refZ = zs[zs.Length / 2];
            var near = levelArea.Where(kv => Math.Abs(kv.Key / 10f - refZ) <= 4f).ToList();
            if (near.Count > 0) mainLevel = near.OrderByDescending(kv => kv.Value).First().Key;
        }
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
            if (HasHeadroom(bw, point, 1.75f)) { spawn = point; found = true; break; }
        }
        if (!found)
        {
            spawn = floor.OrderBy(f => f.Dist).First().Point;
            log.Warn("SWTOR_SPAWN_HEADROOM", $"No floor point with 2 m of headroom near the centre of room {best.Room}: spawn placed on the closest floor triangle.");
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
    /// <summary>Matrice de <see cref="SwtorAxes.ToZUp"/> (convention p * M).</summary>
    private static Matrix4x4 AxisMatrix(AxisConvention axes)
    {
        Vector3 x = SwtorAxes.ToZUp(Vector3.UnitX, axes), y = SwtorAxes.ToZUp(Vector3.UnitY, axes), z = SwtorAxes.ToZUp(Vector3.UnitZ, axes);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>
    /// Maillages de rendu d'un modele a l echelle de l instance (<paramref name="scale"/>, echelle du monde comprise), axes de la scene, sans rotation ni position, un par materiau,
    /// avec les memes regles que les pieces (collision, pieces cachees et materiaux hors limites ignores ; journalises par l'instance).
    /// </summary>
    private static SceneModel BuildLocalModel(string path, Gr2Model model, SwtorMaterials materials, AxisConvention axes, Vector3 scale)
    {
        Matrix4x4 scaleM = Matrix4x4.CreateScale(scale);
        Matrix4x4 normalM = Matrix4x4.CreateScale(1f / scale.X, 1f / scale.Y, 1f / scale.Z);
        var acc = new Dictionary<string, MeshAccumulator>(StringComparer.Ordinal);
        var order = new List<MeshAccumulator>();
        bool flip = SwtorAxes.FlipsWinding(axes);
        foreach (Gr2Mesh mesh in model.Meshes)
        {
            if (mesh.Name.Contains("collision", StringComparison.OrdinalIgnoreCase)) continue;
            if (mesh.Positions.Length == 0 || mesh.Indices.Length < 3 || mesh.Indices.Any(i => i >= mesh.Positions.Length)) continue;
            Vector2[]? uv = mesh.UvSets.Count > 0 && mesh.UvSets[0].Length == mesh.Positions.Length ? mesh.UvSets[0] : null;
            IReadOnlyList<Gr2Piece> pieces = mesh.Pieces.Count > 0
                ? mesh.Pieces
                : [new Gr2Piece(0, mesh.Indices.Length / 3, 0, Vector3.Zero, Vector3.Zero)];
            foreach (Gr2Piece piece in pieces)
            {
                if (piece.MaterialIndex < 0 || piece.MaterialIndex >= model.Materials.Count) continue;
                SwtorMaterialInfo mat = materials.Resolve(model.Materials[piece.MaterialIndex]);
                int first = piece.StartTriangle * 3, end = Math.Min(mesh.Indices.Length, (piece.StartTriangle + piece.TriangleCount) * 3);
                if (first < 0 || first >= end || mat.Hidden) continue;
                if (!acc.TryGetValue(mat.Key, out MeshAccumulator? a))
                {
                    a = new MeshAccumulator(mat);
                    acc[mat.Key] = a;
                    order.Add(a);
                }
                a.Add(mesh, uv, mat.UvScale, scaleM, normalM, flip, axes, first, end);
            }
        }
        return new SceneModel(path, [.. order.Where(a => a.Triangles.Count > 0).Select(a => a.ToSceneMesh(""))]);
    }

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

        /// <summary>Ajoute un maillage deja en repere de scene (terrain).</summary>
        public void AddRaw(SwtorTerrainMesh mesh)
        {
            int b = Positions.Count;
            Positions.AddRange(mesh.Positions);
            Normals.AddRange(mesh.Normals);
            Uv.AddRange(mesh.Uv);
            foreach (int i in mesh.Triangles) Triangles.Add(b + i);
        }

        public SceneMesh ToSceneMesh(string room) =>
            new(room, material!.Key, material.Key, null, TxiInfo.Empty, 0, [.. Positions], [.. Normals], [.. Uv], null, [.. Triangles])
            {
                Alpha = material.Alpha,
            };
    }
}

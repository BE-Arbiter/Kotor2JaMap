using System.Numerics;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Core.Textures;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Jk.Pipeline;

/// <summary>Reglages du builder de scene JK (les valeurs par defaut sont celles de l'application).</summary>
public sealed record JkSceneOptions
{
    /// <summary>Nombre maximal de pieces de visibilite : au-dela, des secteurs voisins sont fusionnes ; aucune surface n'est supprimee.</summary>
    public int MaxRooms { get; init; } = 1024;

    /// <summary>Nombre maximal de clusters de visibilite du BSP (la matrice et le temps d'ecriture croissent comme son carre : 4 500 clusters = environ 4 s et 2,5 Mo).</summary>
    public int MaxClusters { get; init; } = 4500;

    /// <summary>Sauts d'adjoin suivis par la visibilite.</summary>
    public int VisibilityHops { get; init; } = JkVisibility.DefaultHops;

    /// <summary>Hauteur de contremarche (unites JA) a partir de laquelle une rampe de collision est ajoutee (le pas du joueur JA est de 18 u).</summary>
    public float StairRampMinRiseJa { get; init; } = 14f;

    /// <summary>Normale Z minimale d'un triangle marchable (MIN_WALK_NORMAL de JA).</summary>
    public float WalkableMinNormalZ { get; init; } = 0.7f;

    /// <summary>
    /// Facteur applique a la vitesse des textures qui defilent (<c>SlideWall</c>). 1 : la vitesse du COG est lue en unites JK par seconde (la texture suit
    /// le mouvement dans le monde). Unite non verifiee en jeu : a regler ici si les tapis et les cascades vont trop vite ou trop lentement.
    /// </summary>
    public float ScrollSpeedScale { get; init; } = 1f;
}

/// <summary>Scene JK prete pour le pipeline : <see cref="Scene"/> (en metres), le niveau lu, les gabarits et les materiaux decodes (cle = nom sans extension, en minuscules).</summary>
public sealed record JkPreparedScene(ModuleScene Scene, JklLevel Level, JkTemplates Templates, IReadOnlyDictionary<string, JkMaterial> Materials)
{
    /// <summary>Volumes d'eau, de blessure et de mort (unites JK, voir <see cref="JkSurfaceEffects"/>) ; les entites correspondantes sont produites avec les objets du niveau.</summary>
    public IReadOnlyList<JkVolume> Volumes { get; init; } = [];

    /// <summary>Type du niveau (solo, multijoueur, CTF) : fixe par <see cref="JkSceneBuilder.Build(JkFileSystem, JkLevelRef, ImportLog, JkSceneOptions?)"/>, solo pour une scene construite autrement. Sert au choix des entites de depart.</summary>
    public JkLevelKind Kind { get; init; } = JkLevelKind.SinglePlayer;
}

/// <summary>
/// Construit la <see cref="ModuleScene"/> d'un niveau JK.
/// <list type="bullet">
/// <item>Rooms : un secteur = une piece <c>s&lt;index&gt;</c> (groupes <c>g&lt;n&gt;</c> si le niveau depasse <see cref="JkSceneOptions.MaxRooms"/>) ; un maillage par (piece, materiau, rampe de lumiere).</item>
/// <item>Rendu : surface avec materiau et <c>geo != 0</c>, polygone en eventail. UV = texvert / taille de la cellule 0 (v inversee : convention GL de la scene).
/// Un adjoin avec materiau est dessine des deux cotes ; une face translucide (<c>0x2</c>) devient un melange a 50 %.</item>
/// <item>Collision : walkmesh de toutes les surfaces (visibles ou non) sauf les adjoins franchissables (bit <c>0x2</c>) ; <c>RenderMeshesAreSolid = false</c>.</item>
/// <item>Eclairage : voir <see cref="VertexLight"/> et <see cref="LightRamp"/>.</item>
/// </list>
/// </summary>
public static class JkSceneBuilder
{
    private const uint AdjoinMove = 0x2;
    private const uint FaceDoubleSided = 0x1, FaceTranslucent = 0x2;
    private const uint SurfaceHorizonSky = 0x200, SurfaceCeilingSky = 0x400;
    private const uint SurfaceSkyMask = SurfaceHorizonSky | SurfaceCeilingSky;
    private const uint SectorUnderwater = 0x2;
    /// <summary>Plafond d'images d'un animMap (MAX_IMAGE_ANIMATIONS de JA).</summary>
    private const int MaxAnimFrames = 24;
    private static readonly System.Text.RegularExpressions.Regex LavaMaterial =
        new("lava|magma|molten|sludge|sldge|slime|acid|fire", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private const int RampWidth = 256;
    private const int RampHeight = 4;

    public static JkPreparedScene Build(JkFileSystem fs, JkLevelRef level, ImportLog log, JkSceneOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(level);
        byte[] data = fs.TryRead($@"jkl\{level.Name}.jkl") ?? throw new FileNotFoundException($"Level file not found: jkl\\{level.Name}.jkl");
        JklLevel jkl = JklReader.Read(Encoding.Latin1.GetString(data));
        JklLevel? common = null;
        if (fs.TryRead(@"jkl\static.jkl") is { } staticData)
        {
            try { common = JklReader.Read(Encoding.Latin1.GetString(staticData)); }
            catch (InvalidDataException ex) { log.Warn("JK_STATIC_UNREADABLE", $"static.jkl could not be read ({ex.Message}); its templates are ignored."); }
        }
        return Build(jkl, JkTemplates.FromLevels(common, jkl), fs.TryRead, level.Name, log, options) with { Kind = level.Kind };
    }

    /// <summary>Construit la scene d'un niveau deja lu ; <paramref name="read"/> donne le contenu d'un fichier du jeu (<c>mat\x.mat</c>...) ou null.</summary>
    internal static JkPreparedScene Build(JklLevel level, JkTemplates templates, Func<string, byte[]?> read, string name, ImportLog log, JkSceneOptions? options)
    {
        ArgumentNullException.ThrowIfNull(level);
        options ??= new JkSceneOptions();
        float scale = JkWorldScale.MetresPerJkUnit;

        foreach (string section in level.SkippedSections.Distinct(StringComparer.OrdinalIgnoreCase))
            log.Warn("JK_SECTION_SKIPPED", $"JKL section '{section}' is not converted.");

        // Palette de la colormap 0 : celle des textures (une texture exportee n'a qu'une palette).
        CmpPalette palette = LoadPalette(level, read, log);
        var textures = new Dictionary<string, KotorTexture?>(StringComparer.Ordinal);
        var materials = new Dictionary<string, JkMaterial>(StringComparer.Ordinal);
        var matInfo = new MatEntry?[level.Materials.Count];

        // Effets : volumes, textures qui defilent ou s'animent (COG de demarrage), derive du ciel.
        JkLevelEffects fx = JkSurfaceEffects.Analyze(level, f => read(@"cog\" + f) is { } d ? Encoding.Latin1.GetString(d) : null, log);
        string prefix = ShaderNames.PrefixFor(KotorGame.JediKnight);

        // Pieces : un secteur chacune, ou des groupes de secteurs voisins au-dela de la limite.
        IReadOnlyList<int>[] graph = JkVisibility.SectorGraph(level);
        int[] group = JkVisibility.ChooseGroups(level, graph, JkWorldScale.JaUnitsPerJkUnit, options.MaxRooms, options.MaxClusters, out int clusters);
        string[] roomNames = JkVisibility.RoomNames(group);
        if (roomNames.Length < level.Sectors.Count)
            log.Warn("JK_VIS_MERGED", $"{level.Sectors.Count} sectors would give too many visibility clusters or rooms (limits: {options.MaxRooms} rooms, {options.MaxClusters} clusters): merged into {roomNames.Length} groups of neighbouring sectors, {clusters} cluster(s) (no geometry removed).");

        var sectorOfSurface = new int[level.Surfaces.Count];
        Array.Fill(sectorOfSurface, -1);
        for (int s = 0; s < level.Sectors.Count; s++)
        {
            JklSector sec = level.Sectors[s];
            for (int k = sec.FirstSurface; k < sec.FirstSurface + sec.SurfaceCount && k < level.Surfaces.Count; k++)
                if (k >= 0) sectorOfSurface[k] = s;
        }
        int orphans = sectorOfSurface.Count(s => s < 0);
        if (orphans > 0) log.Warn("JK_SURFACE_ORPHAN", $"{orphans} surface(s) belong to no sector; they are placed in room 'orphan'.");

        // Ciel : un seul shader par niveau, construit sur le materiau de ciel le plus utilise. L'adjoin d'une surface appartient au secteur qui la porte ;
        // le secteur d'en face (surface miroir) donne le cote de l'eau.
        var missing = new HashSet<string>(StringComparer.Ordinal);
        SkyInfo? skyInfo = PrepareSky(level, matInfo, read, palette, textures, materials, missing, log, fx, prefix, name);
        bool[] underwater = level.Sectors.Select(s => (s.Flags & SectorUnderwater) != 0).ToArray();
        var adjoinSector = new Dictionary<int, int>();
        for (int k = 0; k < level.Surfaces.Count; k++)
            if (level.Surfaces[k].Adjoin >= 0 && sectorOfSurface[k] >= 0) adjoinSector.TryAdd(level.Surfaces[k].Adjoin, sectorOfSurface[k]);
        int skyMeshSurfaces = 0, animated = 0, scrolling = 0, waterSurfaces = 0, lavaSurfaces = 0, slideFlat = 0;

        var meshes = new List<MeshAcc>();
        var meshIndex = new Dictionary<(string, string, SurfaceAlpha, string, string), MeshAcc>();
        var ramps = new Dictionary<string, KotorTexture?>(StringComparer.Ordinal);
        var walks = new Dictionary<string, WalkAcc>(StringComparer.Ordinal);

        int hidden = 0, degenerate = 0, geoApprox = 0, sky = 0, translucent = 0, doubleSided = 0, solidSurfaces = 0, passable = 0;

        for (int si = 0; si < level.Surfaces.Count; si++)
        {
            JklSurface surf = level.Surfaces[si];
            int sector = sectorOfSurface[si];
            string room = sector >= 0 ? roomNames[group[sector]] : "orphan";
            JklSector? sec = sector >= 0 ? level.Sectors[sector] : null;
            int n = surf.VertexIndices.Length;
            if (n < 3 || surf.VertexIndices.Any(v => v < 0 || v >= level.Vertices.Length)) { degenerate++; continue; }
            Vector3[] pos = surf.VertexIndices.Select(v => level.Vertices[v] * scale).ToArray();
            Vector3 normal = PolygonNormal(pos);
            if (normal == Vector3.Zero) { degenerate++; continue; }
            // Le sens des sommets doit suivre la normale du fichier ; sinon on inverse le polygone (jamais observe sur les 34 niveaux).
            bool reversed = surf.Normal != Vector3.Zero && Vector3.Dot(normal, surf.Normal) < 0f;
            if (reversed) normal = -normal;

            bool isPassable = IsPassableAdjoin(level, surf, fx.OpenedAdjoins);
            if (isPassable) passable++;
            else
            {
                solidSurfaces++;
                if (!walks.TryGetValue(room, out WalkAcc? walk)) walks[room] = walk = new WalkAcc();
                walk.AddPolygon(pos, reversed, options.WalkableMinNormalZ);
            }

            if (surf.Material < 0 || surf.Geo == 0) { hidden++; continue; }
            if (surf.Material >= level.Materials.Count) { degenerate++; continue; }
            if (surf.Geo is 1 or 2 or 3) geoApprox++;
            bool isSky = skyInfo is not null && (surf.SurfaceFlags & SurfaceSkyMask) != 0;
            if ((surf.SurfaceFlags & SurfaceSkyMask) != 0) sky++;

            MatEntry mat = matInfo[surf.Material] ??= LoadMaterial(level.Materials[surf.Material], read, palette, textures, materials, missing, log);
            bool translucentFace = !isSky && (surf.FaceFlags & FaceTranslucent) != 0;
            string texKey = mat.Key;
            SurfaceAlpha alpha = mat.HasTransparency ? SurfaceAlpha.Cutout : SurfaceAlpha.Opaque;
            ShaderHints hints = ShaderHints.None;
            if (isSky)
            {
                // Ouverture sur le ciel : le shader de ciel du niveau (nuages), jamais eclaire ni translucide.
                texKey = skyInfo!.TextureKey;
                alpha = SurfaceAlpha.Opaque;
                hints = skyInfo.Hints;
                skyMeshSurfaces++;
            }
            else
            {
                if (translucentFace)
                {
                    texKey = mat.Key + "_t";
                    if (!textures.ContainsKey(texKey)) textures[texKey] = Translucent(textures[mat.Key]);
                    alpha = SurfaceAlpha.Blend;
                    translucent++;
                }
                hints = SurfaceHints(level, surf, si, sector, mat, translucentFace, texKey, fx, materials, textures, underwater, adjoinSector, options.ScrollSpeedScale, ref animated, ref scrolling, ref waterSurfaces, ref lavaSurfaces, ref slideFlat);
            }

            Vector3 tint = sec?.Tint ?? Vector3.Zero;
            // Pas de rampe de lumiere pour les surfaces melangees, le ciel et la pleine lumiere (couleur de la texture sans eclairage).
            string ramp = alpha == SurfaceAlpha.Blend || hints.FullBright || hints.Sky ? "" : LightRamp(tint, textures, ramps);
            var key = (room, texKey, alpha, ramp, hints.Key);
            if (!meshIndex.TryGetValue(key, out MeshAcc? acc))
            {
                acc = new MeshAcc(room, hints.IsNone ? $"{room}_{texKey}" : $"{room}_{texKey}_{meshes.Count}", texKey, alpha, ramp.Length > 0 ? ramp : null, hints);
                meshIndex[key] = acc;
                meshes.Add(acc);
            }

            Vector2[] uv0 = new Vector2[n];
            for (int k = 0; k < n; k++)
            {
                int t = k < surf.TexIndices.Length ? surf.TexIndices[k] : -1;
                if (t >= 0 && t < level.TexVertices.Length)
                {
                    Vector2 tv = level.TexVertices[t];
                    uv0[k] = new Vector2(tv.X / mat.Width, 1f - tv.Y / mat.Height);
                }
            }
            float[] light = VertexLight(surf, sec);
            Vector2[]? uv1 = ramp.Length == 0 ? null : light.Select(l => new Vector2((MathF.Round(l * (RampWidth - 1)) + 0.5f) / RampWidth, 0.5f)).ToArray();

            // Les types de shader sans « cull none » (opaque eclaire) ne voient qu'une face : un adjoin avec materiau, ou une face double,
            // est donc ecrit des deux cotes.
            bool both = alpha == SurfaceAlpha.Opaque && (surf.Adjoin >= 0 || (surf.FaceFlags & FaceDoubleSided) != 0);
            if (both) doubleSided++;
            acc.AddPolygon(pos, normal, uv0, uv1, reversed, both);
        }

        // Rampes de collision sur les contremarches trop hautes pour le pas de JA.
        float minRise = options.StairRampMinRiseJa / JkWorldScale.JaUnitsPerJkUnit;
        List<JkStairRamps.Ramp> stairRamps = JkStairRamps.Compute(level, s => !IsPassableAdjoin(level, s, fx.OpenedAdjoins), minRise, out int steepSkipped);
        foreach (JkStairRamps.Ramp r in stairRamps)
        {
            string room = roomNames[group[r.Sector]];
            if (!walks.TryGetValue(room, out WalkAcc? walk)) walks[room] = walk = new WalkAcc();
            walk.AddRamp(r.A * scale, r.B * scale, r.C * scale, r.D * scale);
        }
        if (stairRamps.Count > 0 || steepSkipped > 0)
            log.Warn("JK_STAIR_RAMP", $"{stairRamps.Count} stair riser(s) taller than {options.StairRampMinRiseJa:0.#} JA units got a walkable collision ramp (rendering unchanged); {steepSkipped} too steep for a ramp.");

        // --- Scene ---
        var sceneMeshes = meshes.Select(m => m.ToSceneMesh()).ToList();
        var walkmeshes = walks.Where(kv => kv.Value.Triangles.Count > 0)
            .Select(kv => new SceneWalkmesh(kv.Key, [.. kv.Value.Positions], [.. kv.Value.Triangles], [.. kv.Value.Walkable])).ToList();

        ModuleEntry entry = ChooseEntry(level, templates, scale, log);
        var info = new ModuleInfo(name, name, name, entry);
        var allTextures = new Dictionary<string, KotorTexture?>(textures, StringComparer.Ordinal);
        foreach (var kv in ramps) allTextures[kv.Key] = kv.Value;

        var scene = new ModuleScene(info, sceneMeshes, walkmeshes, allTextures)
        {
            // Pas de soleil ni de lumiere ponctuelle : l'eclairage JK est dans la rampe de lumiere de chaque sommet.
            // L'ambiante ne sert qu'aux surfaces melangees (vitres), qui n'ont pas de rampe.
            AmbientColor = new Vector3(0.7f),
            SunDiffuseColor = Vector3.Zero,
            AreaLightingKnown = true,
            RenderMeshesAreSolid = false,
            CompactRenderSurfaces = true,
            RoomVisibility = JkVisibility.Build(graph, group, roomNames, options.VisibilityHops),
            SkyShader = skyInfo is not null && skyMeshSurfaces > 0 ? skyInfo.ShaderName : null,
        };

        foreach (string unresolved in ResolveAllTemplates(level, templates))
            log.Warn("JK_TEMPLATE_UNRESOLVED", $"Template '{unresolved}' is missing or circular; things using it keep what could be resolved.");
        if (hidden > 0) log.Warn("JK_SURFACE_HIDDEN", $"{hidden} surface(s) without material or with geometry mode 0 are not rendered (invisible portals and hidden faces); they still collide unless they are passable adjoins.");
        if (degenerate > 0) log.Warn("JK_SURFACE_DEGENERATE", $"{degenerate} degenerate or invalid surface(s) skipped.");
        if (geoApprox > 0) log.Warn("JK_GEO_APPROX", $"{geoApprox} surface(s) with geometry mode 1 to 3 (points, wireframe, solid colour) are drawn textured.");
        if (sky > 0)
            log.Warn("JK_SKY_APPROX", skyInfo is null
                ? $"{sky} sky surface(s) without a usable sky material are drawn as ordinary textured surfaces."
                : $"{skyMeshSurfaces} sky surface(s) show the level sky shader built on {skyInfo.Material} (a tiled cloud layer: JK's horizon cylinder, horizon distance, ceiling height and sky offsets are approximated or ignored)"
                + (skyInfo.OtherMaterials.Count > 0 ? $"; the other sky material(s) {string.Join(", ", skyInfo.OtherMaterials)} use the same sky." : "."));
        if (animated > 0) log.Warn("JK_ANIM_MATERIAL", $"{animated} surface(s) play their material cels as an animMap (animated by SurfaceAnim or MaterialAnim at level start).");
        if (scrolling > 0) log.Warn("JK_SCROLL_SURFACE", $"{scrolling} surface(s) scroll their texture (SlideWall / SlideSurface at level start; conveyor belts, streams).");
        if (slideFlat > 0) log.Warn("JK_SLIDE_SKIPPED", $"{slideFlat} sliding surface(s) cannot scroll (texture axes undefined or slide perpendicular to the surface).");
        if (waterSurfaces > 0) log.Warn("JK_WATER_SURFACE", $"{waterSurfaces} translucent surface(s) between air and water get the rippling water look.");
        if (lavaSurfaces > 0) log.Warn("JK_LAVA_SURFACE", $"{lavaSurfaces} hazardous liquid surface(s) are drawn full bright and rippling.");
        if (translucent > 0) log.Warn("JK_TRANSLUCENT_APPROX", $"{translucent} translucent surface(s) use a fixed 50 % blend.");
        log.Warn("JK_SCENE", $"{name}: {level.Sectors.Count} sector(s) in {roomNames.Length} room(s), {sceneMeshes.Count} mesh(es), {sceneMeshes.Sum(m => m.Triangles.Length / 3):N0} triangle(s) "
            + $"({doubleSided} surface(s) drawn on both sides), {solidSurfaces} solid and {passable} passable surface(s), {materials.Count} material(s), world scale {scale:0.##} m per JK unit. Not verified in game.");
        return new JkPreparedScene(scene, level, templates, materials) { Volumes = fx.Volumes };
    }

    // ---- eclairage ----

    /// <summary>
    /// Lumiere de chaque sommet, 0..1. JK : intensite du sommet (precalculee dans le niveau) + lumiere additionnelle de la surface + <c>EXTRA LIGHT</c> du secteur,
    /// bornee a 1 ; l'ambiance du secteur sert de plancher. Mode d'eclairage de la surface : 0 pleine lumiere (1), 1 non eclairee (ambiance seule), 2 plat (moyenne), 3 gouraud.
    /// </summary>
    internal static float[] VertexLight(JklSurface surf, JklSector? sec)
    {
        int n = surf.VertexIndices.Length;
        float ambient = Math.Clamp(sec?.AmbientLight ?? 0f, 0f, 1f);
        float extra = surf.ExtraLight + (sec?.ExtraLight ?? 0f);
        var result = new float[n];
        if (surf.Light == 0) { Array.Fill(result, 1f); return result; }
        float average = surf.Intensities.Length > 0 ? surf.Intensities.Average() : 0f;
        for (int i = 0; i < n; i++)
        {
            float v = surf.Light switch
            {
                1 => 0f,
                2 => average,
                _ => i < surf.Intensities.Length ? surf.Intensities[i] : average,
            };
            result[i] = Math.Clamp(Math.Max(Math.Clamp(v + extra, 0f, 1f), ambient), 0f, 1f);
        }
        return result;
    }

    /// <summary>
    /// Rampe de lumiere : texture 256 x 4 en degrade de gris (ou teinte), partagee par tous les sommets du meme secteur-teinte. Un sommet de lumiere l (0..1)
    /// recoit la coordonnee de lightmap u = l : la lightmap de JA est interpolee sur le triangle, ce qui reproduit exactement l'ombrage de Gouraud de JK.
    /// Le mixage JA est texture x lightmap x 2 : la rampe monte a 0,5 pour que la pleine lumiere (l = 1) laisse la texture intacte. <c>TINT</c> du secteur s'ajoute a la lumiere (par canal).
    /// </summary>
    internal static string LightRamp(Vector3 tint, Dictionary<string, KotorTexture?> textures, Dictionary<string, KotorTexture?> ramps)
    {
        byte T(float c) => (byte)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
        bool tinted = tint != Vector3.Zero;
        string name = tinted ? $"jk_light_{T(tint.X):x2}{T(tint.Y):x2}{T(tint.Z):x2}" : "jk_light";
        if (ramps.ContainsKey(name)) return name;
        var rgba = new byte[RampWidth * RampHeight * 4];
        for (int x = 0; x < RampWidth; x++)
        {
            float l = x / (float)(RampWidth - 1);
            byte r = (byte)MathF.Round(Math.Clamp(l + tint.X, 0f, 1f) * 127.5f);
            byte g = (byte)MathF.Round(Math.Clamp(l + tint.Y, 0f, 1f) * 127.5f);
            byte b = (byte)MathF.Round(Math.Clamp(l + tint.Z, 0f, 1f) * 127.5f);
            for (int y = 0; y < RampHeight; y++)
            {
                int o = (y * RampWidth + x) * 4;
                rgba[o] = r; rgba[o + 1] = g; rgba[o + 2] = b; rgba[o + 3] = 255;
            }
        }
        ramps[name] = new KotorTexture(RampWidth, RampHeight, rgba, false, TxiInfo.Empty);
        return name;
    }

    // ---- materiaux ----

    private sealed record MatEntry(string Key, int Width, int Height, bool HasTransparency);

    internal static CmpPalette LoadPalette(JklLevel level, Func<string, byte[]?> read, ImportLog log)
    {
        string? cmp = level.Colormaps.Count > 0 ? level.Colormaps[0] : null;
        byte[]? data = cmp is null ? null : read(@"misc\cmp\" + cmp);
        if (data is not null)
        {
            try { return CmpPalette.Read(data); }
            catch (InvalidDataException ex) { log.Warn("JK_CMP_MISSING", $"Colormap {cmp} is unreadable ({ex.Message}); the default grey palette is used."); return CmpPalette.Default; }
        }
        log.Warn("JK_CMP_MISSING", $"Colormap {cmp ?? "(none)"} not found; the default grey palette is used.");
        return CmpPalette.Default;
    }

    private static MatEntry LoadMaterial(string file, Func<string, byte[]?> read, CmpPalette palette, Dictionary<string, KotorTexture?> textures,
        Dictionary<string, JkMaterial> materials, HashSet<string> missing, ImportLog log)
    {
        string key = TextureKey(file);
        if (textures.TryGetValue(key, out KotorTexture? known))
            return known is null ? new MatEntry(key, 64, 64, false) : new MatEntry(key, known.Width, known.Height, known.HasAlpha);
        byte[]? data = read(@"mat\" + file) ?? read(@"3do\mat\" + file);
        JkMaterial? mat = null;
        string? reason = data is null ? "file not found" : null;
        if (data is not null)
        {
            try { mat = MatReader.Read(key, data, palette); }
            catch (InvalidDataException ex) { reason = ex.Message; }
        }
        if (mat is null)
        {
            textures[key] = null;
            missing.Add(key);
            log.Warn("TEXTURE_MISSING", $"Material {file}: {reason}; surfaces keep a placeholder texture.");
            return new MatEntry(key, 64, 64, false);
        }
        materials[key] = mat;
        KotorTexture first = mat.Cels[0];
        textures[key] = first;
        return new MatEntry(key, first.Width, first.Height, mat.HasTransparency);
    }

    internal static string TextureKey(string materialFile)
    {
        string n = Path.GetFileNameWithoutExtension(materialFile).ToLowerInvariant();
        var sb = new StringBuilder(n.Length);
        foreach (char c in n) sb.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' ? c : '_');
        return sb.Length == 0 ? "material" : sb.ToString();
    }

    /// <summary>Copie de la texture avec une opacite de 50 % (la transparence d'origine reste a 0).</summary>
    private static KotorTexture? Translucent(KotorTexture? t)
    {
        if (t is null) return null;
        var rgba = (byte[])t.Rgba.Clone();
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = (byte)(rgba[i] / 2);
        return new KotorTexture(t.Width, t.Height, rgba, true, TxiInfo.Empty);
    }

    // ---- effets de surface (ciel, animation, defilement, eau, lave) ----

    /// <summary>Ciel du niveau : materiau retenu, autres materiaux de ciel (ramenes au meme ciel), cle de la texture dans la scene, nom du shader et indices communs a tous les maillages de ciel.</summary>
    private sealed record SkyInfo(string Material, IReadOnlyList<string> OtherMaterials, string TextureKey, string ShaderName, ShaderHints Hints);

    /// <summary>
    /// Prepare le shader de ciel : un seul par niveau (le compilateur entoure la carte du meme ciel), construit sur le materiau de ciel le plus utilise.
    /// La texture est copiee sans transparence sous <c>sky_&lt;niveau&gt;</c>. Le <c>HEADER</c> regle la repetition de la couche de nuages
    /// (<c>Horizon Pixels per Rev</c> : plus la texture couvre de pixels par tour, moins on la repete), la derive vient de <c>SlideHorizonSky</c> /
    /// <c>SlideCeilingSky</c> (pixels de texture par seconde). Null : aucun ciel dans le niveau.
    /// </summary>
    private static SkyInfo? PrepareSky(JklLevel level, MatEntry?[] matInfo, Func<string, byte[]?> read, CmpPalette palette, Dictionary<string, KotorTexture?> textures,
        Dictionary<string, JkMaterial> materials, HashSet<string> missing, ImportLog log, JkLevelEffects fx, string prefix, string levelName)
    {
        var counts = new Dictionary<int, int>();
        int horizon = 0, ceiling = 0;
        foreach (JklSurface s in level.Surfaces)
        {
            if ((s.SurfaceFlags & SurfaceSkyMask) == 0 || s.Material < 0 || s.Material >= level.Materials.Count || s.Geo == 0) continue;
            counts[s.Material] = counts.GetValueOrDefault(s.Material) + 1;
            if ((s.SurfaceFlags & SurfaceHorizonSky) != 0) horizon++; else ceiling++;
        }
        if (counts.Count == 0) return null;
        int best = counts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
        MatEntry mat = matInfo[best] ??= LoadMaterial(level.Materials[best], read, palette, textures, materials, missing, log);

        string skyKey = ShaderNames.Short("sky_" + TextureKey(levelName), KotorGame.JediKnight);
        textures[skyKey] = textures.TryGetValue(mat.Key, out KotorTexture? source) && source is not null ? WithoutAlpha(source) : null;

        float revolution = level.Header.HorizonPixelsPerRev > 0f ? level.Header.HorizonPixelsPerRev : 768f;
        float tile = Math.Clamp(revolution / (2f * mat.Width), 1f, 8f);
        // Derive : celle du ciel qui montre le plus de surfaces ; SlideHorizonSky / SlideCeilingSky donnent des pixels de texture par seconde.
        Vector2? slide = horizon >= ceiling ? fx.HorizonSlide ?? fx.CeilingSlide : fx.CeilingSlide ?? fx.HorizonSlide;
        Vector2? scroll = slide is { } v && v != Vector2.Zero ? new Vector2(v.X / mat.Width, -v.Y / mat.Height) : null;

        var others = counts.Keys.Where(i => i != best).Select(i => level.Materials[i])
            .Where(m => !TextureKey(m).Equals(mat.Key, StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return new SkyInfo(level.Materials[best], others, skyKey, prefix + skyKey, ShaderHints.None with { Sky = true, SkyTile = tile, Scroll = scroll });
    }

    private static KotorTexture WithoutAlpha(KotorTexture t)
    {
        var rgba = (byte[])t.Rgba.Clone();
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        return new KotorTexture(t.Width, t.Height, rgba, false, TxiInfo.Empty);
    }

    /// <summary>
    /// Effets d'une surface visible (hors ciel) : animation de cellules, defilement, eau, lave, pleine lumiere.
    /// Eau : surface translucide entre un secteur sous l'eau et un autre qui ne l'est pas. Lave : materiau de liquide dangereux (lave, boue, acide...)
    /// dans une zone qui blesse ou tue. Pleine lumiere : mode d'eclairage 0 de JK, ou lave.
    /// </summary>
    private static ShaderHints SurfaceHints(JklLevel level, JklSurface surf, int si, int sector, MatEntry mat, bool translucentFace, string texKey, JkLevelEffects fx,
        Dictionary<string, JkMaterial> materials, Dictionary<string, KotorTexture?> textures, bool[] underwater, Dictionary<int, int> adjoinSector, float speedScale,
        ref int animated, ref int scrolling, ref int waterSurfaces, ref int lavaSurfaces, ref int slideFlat)
    {
        string materialFile = level.Materials[surf.Material];

        IReadOnlyList<string>? frames = null;
        float fps = 0f;
        JkLevelEffects.Animation? anim = fx.SurfaceAnims.TryGetValue(si, out var sa) ? sa : fx.MaterialAnims.TryGetValue(materialFile, out var ma) ? ma : null;
        if (anim is { } an && materials.TryGetValue(mat.Key, out JkMaterial? jm))
        {
            // Drapeaux de SurfaceAnim / MaterialAnim : 0x2 saute la derniere image, 0x4 saute les deux premieres (la boucle commence a la troisieme).
            int start = (an.Flags & 0x4) != 0 ? 2 : 0;
            int end = Math.Min(jm.Cels.Count - ((an.Flags & 0x2) != 0 ? 1 : 0), start + MaxAnimFrames);
            if (end - start >= 2)
            {
                var list = new List<string>();
                for (int c = start; c < end; c++)
                {
                    string frame = c == 0 ? texKey : $"{texKey}_c{c}";
                    if (c > 0 && !textures.ContainsKey(frame)) textures[frame] = translucentFace ? Translucent(jm.Cels[c]) : jm.Cels[c];
                    list.Add(frame);
                }
                frames = list;
                fps = an.Fps;
                animated++;
            }
        }

        Vector2? scroll = null;
        if (fx.Slides.TryGetValue(si, out JkLevelEffects.Slide slide))
        {
            scroll = ScrollOf(level, surf, slide with { Speed = slide.Speed * speedScale }, mat.Width, mat.Height);
            if (scroll is null) slideFlat++; else scrolling++;
        }

        bool isWater = false;
        if (translucentFace && surf.Adjoin >= 0 && surf.Adjoin < level.Adjoins.Count && sector >= 0)
        {
            int mirror = level.Adjoins[surf.Adjoin].Mirror;
            isWater = mirror >= 0 && adjoinSector.TryGetValue(mirror, out int other) && other >= 0 && other < underwater.Length && underwater[other] != underwater[sector];
        }
        if (isWater) waterSurfaces++;

        bool isLava = LavaMaterial.IsMatch(Path.GetFileNameWithoutExtension(materialFile)) && (fx.HazardSurfaces.Contains(si) || (sector >= 0 && fx.HazardSectors.Contains(sector)));
        if (isLava) lavaSurfaces++;

        return new ShaderHints(frames, fps, scroll, false, isWater, isLava, surf.Light == 0 || isLava);
    }

    /// <summary>
    /// Vitesse du contenu de la texture en largeurs de texture par seconde, en UV de scene, pour un glissement dans le monde (direction unitaire, vitesse en unites JK
    /// par seconde). Les axes de la texture sont tires de trois sommets de la surface : le contenu qui avance de <c>w</c> dans le plan se retrouve a la coordonnee
    /// de texture <c>c + dT(w)</c>, dT etant l'application lineaire sommets -&gt; texverts (en pixels, v vers le bas), d'ou (dTu / largeur, -dTv / hauteur).
    /// Null : axes indetermines ou glissement perpendiculaire a la surface.
    /// </summary>
    internal static Vector2? ScrollOf(JklLevel level, JklSurface surf, JkLevelEffects.Slide slide, int width, int height)
    {
        int n = surf.VertexIndices.Length;
        Vector2? Tex(int k) => k < surf.TexIndices.Length && surf.TexIndices[k] >= 0 && surf.TexIndices[k] < level.TexVertices.Length ? level.TexVertices[surf.TexIndices[k]] : null;
        if (n < 3 || Tex(0) is not { } t0 || Tex(1) is not { } t1) return null;
        Vector3 p0 = level.Vertices[surf.VertexIndices[0]];
        Vector3 e1 = level.Vertices[surf.VertexIndices[1]] - p0;
        for (int k = 2; k < n; k++)
        {
            if (Tex(k) is not { } tk) continue;
            Vector3 e2 = level.Vertices[surf.VertexIndices[k]] - p0;
            Vector3 normal = Vector3.Cross(e1, e2);
            float area2 = normal.LengthSquared();
            if (area2 < 1e-12f) continue;
            normal /= MathF.Sqrt(area2);
            Vector3 w = slide.Direction * slide.Speed;
            w -= normal * Vector3.Dot(w, normal); // composante dans le plan de la surface
            float g11 = Vector3.Dot(e1, e1), g12 = Vector3.Dot(e1, e2), g22 = Vector3.Dot(e2, e2);
            float r1 = Vector3.Dot(e1, w), r2 = Vector3.Dot(e2, w);
            float det = g11 * g22 - g12 * g12;
            if (MathF.Abs(det) < 1e-12f) continue;
            float a = (r1 * g22 - r2 * g12) / det, b = (r2 * g11 - r1 * g12) / det;
            Vector2 dT = a * (t1 - t0) + b * (tk - t0);
            var scroll = new Vector2(MathF.Round(dT.X / width, 3) + 0f, MathF.Round(-dT.Y / height, 3) + 0f); // + 0 : pas de -0
            return scroll == Vector2.Zero ? null : scroll;
        }
        return null;
    }

    // ---- depart du joueur ----

    private static ModuleEntry ChooseEntry(JklLevel level, JkTemplates templates, float scale, ImportLog log)
    {
        foreach (JklThing t in level.Things)
        {
            if (!templates.Resolve(t.Template).TryGetValue("type", out string? type) || !type.Equals("player", StringComparison.OrdinalIgnoreCase)) continue;
            Vector3 p = t.Position;
            p.Z -= JkWorldScale.PlayerOriginAboveFloor; // le point d'apparition du pipeline est au niveau du sol
            return new ModuleEntry(p * scale, YawDirection(t.PitchYawRoll.Y));
        }
        log.Warn("JK_NO_PLAYER_START", "The level has no player start thing; the entry is placed at the origin.");
        return new ModuleEntry(Vector3.Zero, new Vector2(1f, 0f));
    }

    /// <summary>Direction horizontale d'un lacet JK : 0 = +Y, positif = vers la gauche (sens trigonometrique).</summary>
    internal static Vector2 YawDirection(float yawDegrees)
    {
        double a = yawDegrees * Math.PI / 180.0;
        return new Vector2((float)-Math.Sin(a), (float)Math.Cos(a));
    }

    private static IEnumerable<string> ResolveAllTemplates(JklLevel level, JkTemplates templates)
    {
        foreach (JklThing t in level.Things) templates.Resolve(t.Template);
        return templates.Unresolved;
    }

    // ---- geometrie ----

    /// <summary>Adjoin franchissable : bit de passage dans le fichier, ou ouvert par un COG sans mouvement (<paramref name="opened"/>).</summary>
    private static bool IsPassableAdjoin(JklLevel level, JklSurface s, IReadOnlySet<int> opened) =>
        s.Adjoin >= 0 && s.Adjoin < level.Adjoins.Count && ((level.Adjoins[s.Adjoin].Flags & AdjoinMove) != 0 || opened.Contains(s.Adjoin));

    /// <summary>Normale du polygone (methode de Newell), unitaire ; nulle si le polygone est degenere.</summary>
    internal static Vector3 PolygonNormal(IReadOnlyList<Vector3> v)
    {
        Vector3 n = default;
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 a = v[i], b = v[(i + 1) % v.Count];
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        float len = n.Length();
        return len < 1e-12f ? Vector3.Zero : n / len;
    }

    private sealed class MeshAcc(string room, string node, string texture, SurfaceAlpha alpha, string? lightmap, ShaderHints hints)
    {
        private readonly List<Vector3> _pos = [], _nrm = [];
        private readonly List<Vector2> _uv0 = [], _uv1 = [];
        private readonly List<int> _tri = [];

        public void AddPolygon(Vector3[] pos, Vector3 normal, Vector2[] uv0, Vector2[]? uv1, bool reversed, bool both)
        {
            Emit(pos, normal, uv0, uv1, reversed);
            if (both) Emit(pos, -normal, uv0, uv1, !reversed);
        }

        // Eventail depuis le premier sommet ; l'ordre du fichier est antihoraire vu du cote de la normale (verifie sur 01narshadda, 4595 / 4595).
        private void Emit(Vector3[] pos, Vector3 normal, Vector2[] uv0, Vector2[]? uv1, bool reversed)
        {
            int n = pos.Length, b = _pos.Count;
            for (int k = 0; k < n; k++)
            {
                int i = reversed ? n - 1 - k : k;
                _pos.Add(pos[i]); _nrm.Add(normal); _uv0.Add(uv0[i]);
                if (uv1 is not null) _uv1.Add(uv1[i]);
            }
            for (int k = 1; k + 1 < n; k++) { _tri.Add(b); _tri.Add(b + k); _tri.Add(b + k + 1); }
        }

        public SceneMesh ToSceneMesh() =>
            new(room, node, texture, lightmap, TxiInfo.Empty, 0, [.. _pos], [.. _nrm], [.. _uv0], lightmap is null ? null : [.. _uv1], [.. _tri])
            { Alpha = alpha, Hints = hints };
    }

    private sealed class WalkAcc
    {
        public List<Vector3> Positions { get; } = [];
        public List<int> Triangles { get; } = [];
        public List<bool> Walkable { get; } = [];

        public void AddPolygon(Vector3[] pos, bool reversed, float minNormalZ)
        {
            int n = pos.Length;
            var v = reversed ? pos.Reverse().ToArray() : pos;
            for (int k = 1; k + 1 < n; k++) AddTriangle(v[0], v[k], v[k + 1], minNormalZ);
        }

        public void AddRamp(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            // Quadrilatere a, b, c, d : le sens est choisi pour que la normale pointe vers le haut.
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.Z < 0f) { (a, b, c, d) = (d, c, b, a); }
            AddTriangle(a, b, c, 0f, forceWalkable: true);
            AddTriangle(a, c, d, 0f, forceWalkable: true);
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, float minNormalZ, bool forceWalkable = false)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float len = cross.Length();
            if (len < 1e-9f) return;
            int i = Positions.Count;
            Positions.Add(a); Positions.Add(b); Positions.Add(c);
            Triangles.Add(i); Triangles.Add(i + 1); Triangles.Add(i + 2);
            Walkable.Add(forceWalkable || cross.Z / len >= minNormalZ);
        }
    }
}

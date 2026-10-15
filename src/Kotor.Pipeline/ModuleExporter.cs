using System.Globalization;
using System.Numerics;
using System.Text;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Geometry;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Pipeline;

/// <param name="JournalError">Message si le journal .journal.txt n a pas pu etre ecrit (n echoue pas l export), sinon null.</param>
public sealed record ExportResult(bool Ok, string? Pk3Path, string? Error, int Meshes, int Triangles, int MissingTextures,
    string? JournalError = null);

/// <summary>Un fichier TGA a ecrire dans le pk3. <c>Texture</c> null : damier de remplacement. <c>RelPath</c> relatif au dossier du pk3.</summary>
public sealed record ExportTexture(string RelPath, KotorTexture? Texture, bool Lightmap);

/// <summary>Resultat de la preparation : scene du compilateur (unites JA), shaders et fichiers de texture.</summary>
public sealed record ExportPlan(CompileInput Input, IReadOnlyList<PlannedShader> Shaders, IReadOnlyList<ExportTexture> Textures,
    int Meshes, int Triangles, int MissingTextures);

/// <remarks>Vit dans Kotor.Pipeline : <c>Export</c> enchaine les etapes de <see cref="PipelineSteps"/> (Previsualiser, Importer, Exporter, Creer PK3).</remarks>
public static class ModuleExporter
{
    public const int MaxShaders = 1024; // MAX_MAP_SHADERS
    private const float SpawnHeight = 25f;

    /// <summary>Charge le module KOTOR installe puis l'exporte.</summary>
    public static ExportResult Export(string module, string? mapName, ExportOptions opt, ImportLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(module);
        if (opt.Validate() is { } invalid) return new ExportResult(false, null, invalid, 0, 0, 0);
        module = module.ToLowerInvariant();
        string name = MapNames.Normalize(mapName ?? module);
        if (MapNames.Validate(name) is { } badName)
        {
            log.Warn("MAP_NAME_INVALID", badName);
            return new ExportResult(false, null, badName, 0, 0, 0); // pas de journal : le nom ne doit pas servir de chemin
        }
        ModuleScene scene;
        try
        {
            scene = PipelineSteps.Preview(module, opt, log);
        }
        catch (PipelineStepException ex)
        {
            string? je = WriteJournal(opt, name, log);
            return new ExportResult(false, null, ex.Message, 0, 0, 0, je);
        }
        return Export(scene, mapName, opt, log);
    }

    /// <summary>Exporte une scene deja assemblee (le nom de carte par defaut est le module de la scene).</summary>
    public static ExportResult Export(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log)
    {
        string name = MapNames.Normalize(mapName ?? scene.Info.Module);
        if (opt.Validate() is { } invalid) return new ExportResult(false, null, invalid, 0, 0, 0);
        if (MapNames.Validate(name) is { } badName)
        {
            log.Warn("MAP_NAME_INVALID", badName);
            return new ExportResult(false, null, badName, 0, 0, 0);
        }
        ImportStepResult imported = PipelineSteps.Import(scene, mapName, opt, log);
        ExportPlan plan = imported.Plan;
        ExportResult result;
        try
        {
            ExportStepResult exported = PipelineSteps.Export(imported, opt, log);
            Pk3StepResult pk3 = PipelineSteps.CreatePk3(exported, imported, opt, log);
            result = new ExportResult(true, pk3.Pk3Path, null, plan.Meshes, plan.Triangles, plan.MissingTextures);
        }
        catch (PipelineStepException ex)
        {
            result = new ExportResult(false, null, ex.Message, plan.Meshes, plan.Triangles, plan.MissingTextures);
        }
        string? journalError = WriteJournal(opt, name, log);
        return journalError is null ? result : result with { JournalError = journalError };
    }

    /// <summary>Convertit la scene KOTOR (metres) en entree du compilateur (unites JA) et prepare shaders et textures.</summary>
    public static ExportPlan BuildPlan(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(opt);
        string name = MapNames.Normalize(mapName ?? scene.Info.Module);
        if (opt.Validate() is { } invalidOptions) throw new ArgumentException(invalidOptions, nameof(opt));
        float scale = opt.Scale;

        var shaders = new Dictionary<string, PlannedShader>(StringComparer.Ordinal);
        var textures = new Dictionary<string, ExportTexture>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var surfaces = new List<SurfaceInput>();
        var solid = new List<SubMesh>();
        int meshCount = 0;
        long triangles = 0;

        foreach (SceneMesh src in scene.Meshes)
        {
            if (src.Triangles.Length < 3 || src.Positions.Length == 0) continue;
            scene.Textures.TryGetValue(src.Texture, out KotorTexture? tex);
            SceneMesh mesh = src;
            if (tex is null)
            {
                missing.Add(src.Texture);
                mesh = mesh with { Texture = ShaderNames.MissingTexture };
            }
            KotorTexture? lmTex = null;
            if (mesh.Lightmap is not null)
            {
                bool found = scene.Textures.TryGetValue(mesh.Lightmap, out lmTex) && lmTex is not null;
                if (!found || mesh.Uv1 is null)
                {
                    log.Warn("LIGHTMAP_MISSING", found
                        ? $"Maillage {mesh.Room}/{mesh.Node} : lightmap {mesh.Lightmap} sans UV de lightmap, shader sans lightmap."
                        : $"Maillage {mesh.Room}/{mesh.Node} : lightmap {mesh.Lightmap} introuvable, shader sans lightmap.");
                    mesh = mesh with { Lightmap = null };
                }
            }

            PlannedShader planned = ShaderPlanner.Plan(mesh, tex, opt.Game);
            if (shaders.TryGetValue(planned.Name, out PlannedShader? existing) && existing != planned)
            {
                string renamed = planned.Name + "_" + planned.Kind.ToString().ToLowerInvariant();
                log.Warn("SHADER_NAME_CONFLICT", $"{planned.Name} demande par {existing.Kind} et {planned.Kind} : le second devient {renamed}.");
                planned = planned with { Name = renamed };
            }
            shaders.TryAdd(planned.Name, planned);

            string texFile = ShaderNames.PrefixFor(opt.Game) + planned.Texture + ".tga";
            textures.TryAdd(texFile, new ExportTexture(texFile, tex, false));
            if (planned.Lightmap is not null)
            {
                string lmFile = ShaderNames.LightmapPrefixFor(opt.Game) + planned.Lightmap + ".tga";
                textures.TryAdd(lmFile, new ExportTexture(lmFile, lmTex, true));
            }

            Vector3[] pos = Scaled(mesh.Positions, scale);
            surfaces.Add(new SurfaceInput(planned.Name, pos, mesh.Normals, FlipV(mesh.Uv0),
                planned.Lightmap is null || mesh.Uv1 is null ? null : FlipV(mesh.Uv1), mesh.Triangles, planned.DoubleSided));
            if (planned.Kind is not (ShaderKind.Additive or ShaderKind.Blend))
                solid.Add(new SubMesh { MaterialName = planned.Name, Positions = pos, Triangles = mesh.Triangles });
            meshCount++;
            triangles += mesh.Triangles.Length / 3;
        }

        var walkable = new List<SubMesh>();
        foreach (SceneWalkmesh w in scene.Walkmeshes)
        {
            Vector3[] pos = Scaled(w.Positions, scale);
            var walk = new List<int>();
            var block = new List<int>();
            for (int t = 0; t < w.Triangles.Length / 3; t++)
                (w.Walkable[t] ? walk : block).AddRange([w.Triangles[t * 3], w.Triangles[t * 3 + 1], w.Triangles[t * 3 + 2]]);
            if (walk.Count > 0) walkable.Add(new SubMesh { MaterialName = "walkable", Positions = pos, Triangles = [.. walk] });
            if (block.Count > 0) solid.Add(new SubMesh { MaterialName = "walkmesh_solid", Positions = pos, Triangles = [.. block] });
        }

        var entities = new List<IReadOnlyDictionary<string, string>>
        {
            new Dictionary<string, string>
            {
                ["classname"] = "worldspawn", ["message"] = scene.Info.AreaName, ["_kotor_module"] = scene.Info.Module,
            },
        };
        Vector3 origin = SpawnOrigin(scene, scale, log);
        Vector2 dir = scene.Info.Entry.Direction;
        string angle = Num((float)(Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI));
        string originText = $"{Num(origin.X)} {Num(origin.Y)} {Num(origin.Z)}";
        entities.Add(new Dictionary<string, string> { ["classname"] = "info_player_start", ["origin"] = originText, ["angle"] = angle });
        // Un point de depart deathmatch pour que devmap fonctionne aussi en mode FFA.
        entities.Add(new Dictionary<string, string> { ["classname"] = "info_player_deathmatch", ["origin"] = originText, ["angle"] = angle });

        // KOTOR lighting for the surfaces without lightmap, in JA units.
        // With light nodes: zone ambient + the lights (no sun). Without: zone ambient + a directional sun of the ARE SunDiffuseColor
        // (default direction); without a readable ARE: the compiler defaults.
        var lights = scene.Lights
            .Where(l => l.Radius > 0f && float.IsFinite(l.Radius) && l.Multiplier > 0f && float.IsFinite(l.Multiplier))
            .Select(l => new PointLight(l.Position * scale, l.Color, l.Radius * scale, l.Multiplier, l.AmbientOnly))
            .ToList();
        VertexLighting lighting = lights.Count > 0
            ? VertexLighting.Default with { Ambient = scene.AmbientColor }
            : scene.AreaLightingKnown
                ? VertexLighting.Default with { Ambient = scene.AmbientColor, Sun = scene.SunDiffuseColor }
                : VertexLighting.Default;

        var input = new CompileInput(name, entities, surfaces,
            new ModelMesh { Name = "solid", SubMeshes = solid }, new ModelMesh { Name = "walkable", SubMeshes = walkable },
            JaDefaults.SkyShader, lighting, lights);
        return new ExportPlan(input, [.. shaders.Values], [.. textures.Values], meshCount, (int)triangles, missing.Count);
    }

    private static Vector3 SpawnOrigin(ModuleScene scene, float scale, ImportLog log)
    {
        Vector3 entry = scene.Info.Entry.Position;
        Vector3 up = new(0, 0, SpawnHeight);
        foreach (SceneWalkmesh w in scene.Walkmeshes)
            for (int t = 0; t < w.Triangles.Length / 3; t++)
                if (w.Walkable[t] && ContainsXY(w.Positions[w.Triangles[t * 3]], w.Positions[w.Triangles[t * 3 + 1]],
                        w.Positions[w.Triangles[t * 3 + 2]], entry))
                    return entry * scale + up;

        SceneWalkmesh? best = null;
        Vector3 bestCenter = default;
        float bestDist = float.MaxValue;
        foreach (SceneWalkmesh w in scene.Walkmeshes)
        {
            if (w.Positions.Length == 0) continue;
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Vector3 p in w.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            Vector3 c = (min + max) * 0.5f;
            float d = Vector3.DistanceSquared(c, entry);
            if (d < bestDist) { bestDist = d; best = w; bestCenter = c; }
        }
        if (best is null)
        {
            log.Warn("SPAWN_FALLBACK", "Aucun walkmesh : point d'apparition place a l'entree du module.");
            return entry * scale + up;
        }
        log.Warn("SPAWN_FALLBACK", $"Entree ({entry.X:0.##}, {entry.Y:0.##}, {entry.Z:0.##}) hors walkmesh : point d'apparition au-dessus du centre du walkmesh de la piece {best.Room}.");
        return bestCenter * scale + up;
    }

    /// <summary>Projection verticale : le point (x, y) est-il dans le triangle vu du dessus (bords inclus) ?</summary>
    private static bool ContainsXY(Vector3 a, Vector3 b, Vector3 c, Vector3 p)
    {
        static float Cross(Vector3 o, Vector3 u, Vector3 v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
        const float eps = 1e-5f;
        bool neg = d1 < -eps || d2 < -eps || d3 < -eps;
        bool pos = d1 > eps || d2 > eps || d3 > eps;
        return !(neg && pos);
    }

    private static Vector3[] Scaled(Vector3[] p, float scale)
    {
        var r = new Vector3[p.Length];
        for (int i = 0; i < p.Length; i++) r[i] = p[i] * scale;
        return r;
    }

    /// <summary>KOTOR/GL : v = 0 en bas ; le compilateur attend la convention Quake : t = 1 - v.</summary>
    private static Vector2[] FlipV(Vector2[] uv)
    {
        var r = new Vector2[uv.Length];
        for (int i = 0; i < uv.Length; i++) r[i] = new Vector2(uv[i].X, 1f - uv[i].Y);
        return r;
    }

    internal static byte[] Brighten(byte[] rgba, float k)
    {
        if (k == 1f) return rgba;
        var r = (byte[])rgba.Clone();
        for (int i = 0; i < r.Length; i += 4)
            for (int c = 0; c < 3; c++) r[i + c] = (byte)Math.Clamp((int)MathF.Round(r[i + c] * k), 0, 255);
        return r;
    }

    internal static byte[] Checker()
    {
        var b = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                int i = (y * 8 + x) * 4;
                bool on = ((x / 4) + (y / 4)) % 2 == 0;
                b[i] = (byte)(on ? 255 : 0); b[i + 2] = (byte)(on ? 255 : 0); b[i + 3] = 255;
            }
        return b;
    }

    private static string Num(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string? WriteJournal(ExportOptions opt, string name, ImportLog log)
    {
        try
        {
            string dir = Path.Combine(opt.WorkDir, name);
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var (code, message) in log.Warnings) sb.Append(code).Append('\t').Append(message).Append('\n');
            sb.Append("\nResume par code :\n");
            foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
                sb.Append("  ").Append(g.Key).Append(" : ").Append(g.Count()).Append('\n');
            File.WriteAllText(Path.Combine(dir, name + ".journal.txt"), sb.ToString());
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            string msg = $"Journal non ecrit ({ex.Message}).";
            log.Warn("JOURNAL_WRITE_FAILED", msg);
            return msg;
        }
    }
}

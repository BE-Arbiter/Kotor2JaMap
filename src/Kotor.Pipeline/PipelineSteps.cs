using System.Numerics;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Geometry;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Core.Tlk;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Entities;

namespace Kotor.Pipeline;

/// <summary>Les quatre etapes (Previsualiser, Importer, Exporter, Creer PK3), utilisables separement par la CLI et l'application.</summary>
public static class PipelineSteps
{
    /// <summary>Charge le module KOTOR installe (resolveur, TLK, scene). Echec : <c>MODULE_UNREADABLE</c>.</summary>
    public static ModuleScene Preview(string module, ExportOptions opt, ImportLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(module);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        module = module.ToLowerInvariant();
        try
        {
            TlkFile? tlk = null;
            string tlkPath = Path.Combine(opt.KotorDir, "dialog.tlk");
            if (File.Exists(tlkPath))
            {
                try { tlk = TlkFile.Open(tlkPath); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    log.Warn("TLK_UNREADABLE", $"dialog.tlk unreadable ({ex.Message}), module names not translated.");
                }
            }
            using var res = ResourceResolver.ForModule(opt.KotorDir, module, opt.Game);
            return ModuleSceneBuilder.Build(res, module, tlk, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("MODULE_UNREADABLE", ex.Message);
            throw new PipelineStepException("MODULE_UNREADABLE", $"Module unreadable: {ex.Message}");
        }
    }

    /// <summary>Convertit la scene en plan d'export (unites JA, shaders, textures). N'ecrit rien.</summary>
    public static ImportStepResult Import(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(opt);
        if (opt.Validate() is { } invalid)
        {
            log.Warn("OPTIONS_INVALID", invalid);
            throw new PipelineStepException("OPTIONS_INVALID", invalid);
        }
        string name = MapNames.Normalize(mapName ?? scene.Info.Module);
        if (MapNames.Validate(name) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);
        ExportPlan plan = ModuleExporter.BuildPlan(scene, mapName, opt, log);
        (ConvertedEntities converted, IReadOnlyList<SubModelInput> subModels) = ConvertObjects(scene, opt, log);
        return new ImportStepResult(scene, ApplyObjects(plan, converted, subModels, opt.Game), name, converted);
    }

    /// <summary>
    /// Convertit les objets places du GIT (MD3 + entites + collision PWK). Sans dossier de jeu lisible (scene synthetique),
    /// il n'y a rien a lire : resultat vide. Une erreur de lecture du GIT est deja journalisee par le lecteur.
    /// </summary>
    private static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) ConvertObjects(ModuleScene scene, ExportOptions opt, ImportLog log)
    {
        if (opt.Game == KotorGame.Swtor) return (ConvertedEntities.Empty, []); // SWTOR objects are not converted (journaled by the scene builder)
        if (string.IsNullOrWhiteSpace(opt.KotorDir) || !Directory.Exists(opt.KotorDir))
        {
            log.Warn("PLACEABLES_NO_GAME_DIR", "No game folder available: placeable conversion skipped, the map has no placed objects.");
            return (ConvertedEntities.Empty, []);
        }
        if (scene.Parts is not null) return ConvertAssembledObjects(scene, scene.Parts, opt, log);
        try
        {
            using var res = ResourceResolver.ForModule(opt.KotorDir, scene.Info.Module.ToLowerInvariant(), opt.Game);
            IReadOnlyList<ModuleEntity> objects = ModuleEntitiesReader.ReadObjects(res, scene.Info, log);
            ConvertedEntities placeables = PlaceableConverter.Convert(res, opt.Game, objects, opt.Scale, log);
            var (doors, subModels) = DoorConverter.Convert(res, opt.Game, objects, opt.Scale, 1, log);
            ConvertedEntities audio = ConvertAudio(res, scene, scene.Info, ModuleEntitiesReader.ReadSounds(res, scene.Info, log), includeMusic: true, opt, log);
            return (placeables.Merge(doors).Merge(audio), subModels);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("PLACEABLES_UNREADABLE", $"Placeables could not be read ({ex.Message}); none converted.");
            return (ConvertedEntities.Empty, []);
        }
    }

    /// <summary>
    /// Objets d'une carte assemblee : chaque partie est lue avec son propre resolveur, passee en repere assemble et filtree
    /// (<see cref="ModuleAssembler.ApplyPart"/>), convertie puis fusionnee ; les sous-modeles des portes sont numerotes a la suite.
    /// Une partie illisible est journalisee et ignoree, les autres sont converties.
    /// </summary>
    private static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) ConvertAssembledObjects(ModuleScene scene, IReadOnlyList<AssemblyPartRef> parts,
        ExportOptions opt, ImportLog log)
    {
        ConvertedEntities all = ConvertedEntities.Empty;
        var subModels = new List<SubModelInput>();
        bool first = true;
        foreach (AssemblyPartRef part in parts)
        {
            string module = part.Module.ToLowerInvariant();
            try
            {
                using var res = ResourceResolver.ForModule(opt.KotorDir, module, opt.Game);
                ModuleInfo info = part.Info ?? new ModuleInfo(module, module, module, new ModuleEntry(Vector3.Zero, Vector2.UnitY));
                IReadOnlyList<ModuleEntity> objects = ModuleAssembler.ApplyPart(ModuleEntitiesReader.ReadObjects(res, info, log), part);
                ConvertedEntities placeables = PlaceableConverter.Convert(res, opt.Game, objects, opt.Scale, log);
                var (doors, doorModels) = DoorConverter.Convert(res, opt.Game, objects, opt.Scale, 1 + subModels.Count, log);
                // Une seule musique pour la carte : celle du premier module. Ambiance et sons places : ceux de chaque module.
                IReadOnlyList<ModuleEntity> sounds = ModuleAssembler.ApplyPart(ModuleEntitiesReader.ReadSounds(res, info, log), part);
                ConvertedEntities audio = ConvertAudio(res, scene, info, sounds, includeMusic: first, opt, log);
                first = false;
                all = all.Merge(placeables).Merge(doors).Merge(audio);
                subModels.AddRange(doorModels);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                log.Warn("PLACEABLES_UNREADABLE", $"{module}: placeables could not be read ({ex.Message}); none converted for this module.");
            }
        }
        return (all, subModels);
    }

    /// <summary>Musique, ambiance et sons places d'un module (<see cref="AudioConverter"/>) ; l'ambiance de zone est posee au centre des pieces du module.</summary>
    private static ConvertedEntities ConvertAudio(ResourceResolver res, ModuleScene scene, ModuleInfo info, IReadOnlyList<ModuleEntity> sounds,
        bool includeMusic, ExportOptions opt, ImportLog log)
    {
        var rooms = scene.Walkmeshes.Where(w => w.Positions.Length > 0 && w.Room.StartsWith(info.AreaResRef + "_", StringComparison.OrdinalIgnoreCase)).ToList();
        if (rooms.Count == 0 && scene.Parts is null) rooms = scene.Walkmeshes.Where(w => w.Positions.Length > 0).ToList();
        var centers = new List<Vector3>();
        foreach (SceneWalkmesh w in rooms)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Vector3 p in w.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            centers.Add((min + max) * 0.5f);
        }
        return AudioConverter.Convert(new AudioSources(res, opt.Game), info, sounds, centers, includeMusic, opt.Scale, log);
    }

    /// <summary>Ajoute au plan les entites, la collision solide, et les textures des objets (les shaders MD3 sont ecrits par l'etape Exporter).</summary>
    private static ExportPlan ApplyObjects(ExportPlan plan, ConvertedEntities converted, IReadOnlyList<SubModelInput> subModels, KotorGame game)
    {
        if (converted.Entities.Count == 0 && converted.ExtraSolid.Count == 0 && converted.TextureData.Count == 0 && subModels.Count == 0
            && converted.WorldKeys.Count == 0) return plan;
        CompileInput input = plan.Input;
        var entities = new List<IReadOnlyDictionary<string, string>>(input.Entities);
        entities.AddRange(converted.Entities);
        if (converted.WorldKeys.Count > 0)
        {
            // Cles du worldspawn (music) : la premiere entite de la carte.
            int world = entities.FindIndex(e => e.TryGetValue("classname", out string? c) && c == "worldspawn");
            if (world >= 0)
            {
                var keys = new Dictionary<string, string>(entities[world], StringComparer.Ordinal);
                foreach (var kv in converted.WorldKeys) keys[kv.Key] = kv.Value;
                entities[world] = keys;
            }
        }
        ModelMesh solid = input.Solid;
        if (converted.ExtraSolid.Count > 0)
        {
            var pos = new Vector3[converted.ExtraSolid.Count * 3];
            var tri = new int[pos.Length];
            for (int i = 0; i < converted.ExtraSolid.Count; i++)
            {
                (pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]) = converted.ExtraSolid[i];
                tri[i * 3] = i * 3; tri[i * 3 + 1] = i * 3 + 1; tri[i * 3 + 2] = i * 3 + 2;
            }
            solid = new ModelMesh
            {
                Name = solid.Name, BBoxCenter = solid.BBoxCenter, BBoxExtents = solid.BBoxExtents,
                SubMeshes = [.. solid.SubMeshes, new SubMesh { MaterialName = "placeable_solid", Positions = pos, Triangles = tri }],
            };
        }
        var textures = new List<ExportTexture>(plan.Textures);
        var known = new HashSet<string>(textures.Select(t => t.RelPath), StringComparer.Ordinal);
        foreach (var (name, tex) in converted.TextureData)
        {
            string rel = ShaderNames.PrefixFor(game) + (tex is null ? ShaderNames.MissingTexture : name) + ".tga";
            if (known.Add(rel)) textures.Add(new ExportTexture(rel, tex, false));
        }
        return plan with { Input = input with { Entities = entities, Solid = solid, SubModels = subModels }, Textures = textures };
    }

    /// <summary>Ecrit shader et textures dans le staging, compile le .bsp puis le relit.</summary>
    public static ExportStepResult Export(ImportStepResult imported, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(opt);
        ExportPlan plan = imported.Plan;
        string name = imported.MapName;
        if (MapNames.Validate(name) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);

        if (plan.Shaders.Count > ModuleExporter.MaxShaders)
            throw Fail(log, "SHADER_LIMIT", $"{plan.Shaders.Count} distinct shaders: exceeds MAX_MAP_SHADERS ({ModuleExporter.MaxShaders}).");
        RequireJaBaseDir(opt, log);

        try
        {
            string stage = Path.Combine(opt.WorkDir, name, "pk3");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);

            string bsp = Path.Combine(stage, "maps", name + ".bsp");
            var cr = global::Kotor.Compiler.Compiler.Compile(plan.Input, bsp, Limits.FromName(opt.BspLimits), log);
            if (!cr.Ok) throw new PipelineStepException("BSP_ERROR", cr.Error ?? "Compilation failed."); // deja journalise par le compilateur

            Directory.CreateDirectory(Path.Combine(stage, "shaders")); // le moteur SWGL ne liste que shaders/*.shader (tr_shader.cpp)
            // Shaders du BSP, puis ceux des modeles MD3 (models/<dossier>/<texture>) dans le meme fichier.
            IEnumerable<string> shaderBlocks = plan.Shaders.Select(s => ShaderPlanner.Write(s, opt.Game))
                .Concat(imported.Converted?.ModelShaders ?? []);
            File.WriteAllText(Path.Combine(stage, "shaders", KotorGames.MapPrefix(opt.Game) + "_" + name + ".shader"),
                string.Join("\n", shaderBlocks));
            foreach (Md3Asset md3 in imported.Converted?.Models ?? [])
            {
                string md3Path = Path.Combine(stage, md3.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(md3Path)!);
                File.WriteAllBytes(md3Path, md3.Data);
            }

            foreach (SoundAsset snd in imported.Converted?.Sounds ?? [])
            {
                string sndPath = Path.Combine(stage, snd.Path.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(sndPath)!);
                File.WriteAllBytes(sndPath, snd.Data);
            }

            foreach (ExportTexture t in plan.Textures)
            {
                string path = Path.Combine(stage, t.RelPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = File.Create(path);
                if (t.Texture is null) TgaWriter.Write(fs, 8, 8, ModuleExporter.Checker());
                else TgaWriter.Write(fs, t.Texture.Width, t.Texture.Height,
                    t.Lightmap ? ModuleExporter.Brighten(t.Texture.Rgba, opt.LightmapBrightness) : t.Texture.Rgba);
            }

            return new ExportStepResult(bsp, ReadBack(File.ReadAllBytes(bsp), plan, log), stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>Relit le .bsp ecrit et verifie qu'il correspond au plan (au moins une surface, meme nombre total d'index).</summary>
    internal static BspFile ReadBack(byte[] bspBytes, ExportPlan plan, ImportLog log)
    {
        BspFile parsed;
        try { parsed = BspReader.Read(bspBytes); }
        catch (InvalidDataException ex) { throw Fail(log, "BSP_READBACK_FAILED", $"Cannot read the .bsp back: {ex.Message}"); }

        long planned = plan.Input.Surfaces.Sum(s => (long)s.Indices.Length);
        long read = parsed.Surfaces.Sum(s => (long)s.Indices.Length);
        // Le compilateur ajoute la boite de ciel (SkyBox.IndexCount index) a la geometrie du plan, sans rien retirer.
        if (parsed.Surfaces.Count == 0 || (read != planned && read != planned + SkyBox.IndexCount))
            throw Fail(log, "BSP_READBACK_FAILED",
                $"The .bsp read back does not match the plan: {parsed.Surfaces.Count} surface(s), {read} indexes read for {planned} planned (+{SkyBox.IndexCount} for the sky).");
        return parsed;
    }

    /// <summary>Zippe le staging en <c>&lt;JaBaseDir&gt;\&lt;prefix&gt;_&lt;carte&gt;.pk3</c> (le pk3 existant n'est remplace que si tout reussit).</summary>
    public static Pk3StepResult CreatePk3(ExportStepResult exported, ImportStepResult imported, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(exported);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(opt);
        if (MapNames.Validate(imported.MapName) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);
        RequireJaBaseDir(opt, log);
        try
        {
            string pk3 = Path.Combine(opt.JaBaseDir, KotorGames.MapPrefix(opt.Game) + "_" + imported.MapName + ".pk3");
            Pk3Builder.Build(exported.StagingDir, pk3);
            return new Pk3StepResult(pk3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>Le dossier <c>base</c> du jeu doit exister : on ne cree jamais d'arborescence fantome.</summary>
    private static void RequireJaBaseDir(ExportOptions opt, ImportLog log)
    {
        if (!Directory.Exists(opt.JaBaseDir))
            throw Fail(log, "JA_BASE_MISSING", $"Jedi Academy base folder not found: {opt.JaBaseDir}");
    }

    private static PipelineStepException Fail(ImportLog log, string code, string message)
    {
        log.Warn(code, message);
        return new PipelineStepException(code, message);
    }
}

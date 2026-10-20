using System.Globalization;
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

/// <summary>
/// Conversion des objets d'une scene fournie par un jeu autre que KOTOR (Jedi Knight) : entites JA, MD3, sons, textures de modeles et sous-modeles BSP
/// (numerotes a partir de 1 : <c>*1</c>...). Quand elle est donnee a <see cref="PipelineSteps.Import"/>, elle remplace la lecture des objets KOTOR
/// (GIT, portes, sons) ; <c>null</c> : comportement inchange pour KOTOR et SWTOR.
/// </summary>
public delegate (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) SceneObjects(ModuleScene scene, ExportOptions opt, ImportLog log);

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
    public static ImportStepResult Import(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log, SceneObjects? objects = null)
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
        // Modeles places (SWTOR) : MD3 + misc_model_static sous le plafond du moteur, le reste remis dans les maillages du BSP.
        (ConvertedEntities statics, ModuleScene compiled) = StaticModelConverter.Convert(scene, name, opt, log);
        ExportPlan plan = ModuleExporter.BuildPlan(compiled, mapName, opt, log);
        // Crochet (Jedi Knight) : il remplace la conversion des objets KOTOR ; sans lui, rien ne change.
        (ConvertedEntities converted, IReadOnlyList<SubModelInput> subModels) = objects is null ? ConvertObjects(scene, opt, log) : objects(scene, opt, log);
        converted = statics.Merge(converted);
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
            // .map d'edition (geometrie visible texturee) : hors du pk3, dans <travail>\<carte>\edit.
            string editDir = EditDir(opt, name);
            // Avec --map-visuals off on ne touche a rien dans edit. Sinon l'ancien .map est sauvegarde (jamais supprime en silence) ;
            // si la sauvegarde echoue, le .map d'edition n'est pas ecrit : l'export du jeu n'en depend pas.
            bool writeEdit = opt.MapVisuals && BackupEditMap(EditMapPath(opt, name), name, log);
            var input = writeEdit ? plan.Input with { VisualMapPath = EditMapPath(opt, name) } : plan.Input;
            var cr = global::Kotor.Compiler.Compiler.Compile(input, bsp, Limits.FromName(opt.BspLimits), log);
            if (!cr.Ok) throw new PipelineStepException("BSP_ERROR", cr.Error ?? "Compilation failed."); // deja journalise par le compilateur

            Directory.CreateDirectory(Path.Combine(stage, "shaders")); // le moteur SWGL ne liste que shaders/*.shader (tr_shader.cpp)
            // Shaders du BSP, puis ceux des modeles MD3 (models/<dossier>/<texture>) dans le meme fichier.
            IEnumerable<string> shaderBlocks = plan.Shaders.Select(s => ShaderPlanner.Write(s, opt.Game))
                .Concat(imported.Converted?.ModelShaders ?? []);
            File.WriteAllText(Path.Combine(stage, "shaders", ShaderNames.ShaderFileName(name)),
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

            if (writeEdit) PrepareEditFolder(stage, editDir, EditMapPath(opt, name), log);
            return new ExportStepResult(bsp, ReadBack(File.ReadAllBytes(bsp), plan, log), stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>
    /// Dossier d'edition de la carte : <c>&lt;travail&gt;\&lt;carte&gt;\edit</c>. C'est le « chemin du moteur » de l'editeur de cartes : il contient
    /// <c>base\maps</c>, <c>base\textures</c> et <c>base\shaders</c> (la structure d'un dossier de jeu Jedi Academy).
    /// </summary>
    public static string EditDir(ExportOptions opt, string name) => Path.Combine(opt.WorkDir, name, "edit");

    /// <summary>Le .map d'edition : <c>&lt;travail&gt;\&lt;carte&gt;\edit\base\maps\&lt;carte&gt;.map</c>.</summary>
    public static string EditMapPath(ExportOptions opt, string name) => Path.Combine(EditDir(opt, name), "base", "maps", name + ".map");

    /// <summary>Nombre maximal de sauvegardes <c>&lt;carte&gt;.&lt;horodatage&gt;.map.bak</c> conservees a cote du .map d'edition.</summary>
    public const int MaxEditBackups = 5;

    /// <summary>
    /// Sauvegarde le .map d'edition existant (renomme en <c>&lt;carte&gt;.&lt;horodatage&gt;.map.bak</c>, au plus <see cref="MaxEditBackups"/>
    /// conservees) avant qu'un nouvel export ne le remplace. Renvoie false (avec l'avertissement <c>MAP_VISUAL_WRITE_FAILED</c>) si une erreur
    /// d'E/S empeche la sauvegarde : le .map existant reste alors intact et n'est pas reecrit. Ne leve jamais d'erreur d'E/S.
    /// </summary>
    private static bool BackupEditMap(string editMap, string name, ImportLog log)
    {
        try
        {
            if (!File.Exists(editMap)) return true;
            string dir = Path.GetDirectoryName(editMap)!;
            string bak;
            int n = 0;
            do
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + (n == 0 ? "" : "-" + n);
                bak = Path.Combine(dir, $"{name}.{stamp}.map.bak");
                n++;
            } while (File.Exists(bak));
            File.Move(editMap, bak);
            log.Info($"Edit map: previous {Path.GetFileName(editMap)} kept as {Path.GetFileName(bak)}.");

            var olds = Directory.GetFiles(dir, name + ".*.map.bak").OrderBy(f => File.GetLastWriteTimeUtc(f)).ThenBy(f => f, StringComparer.Ordinal).ToList();
            for (int i = 0; i < olds.Count - MaxEditBackups; i++)
            {
                try { File.Delete(olds[i]); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("MAP_VISUAL_WRITE_FAILED", $"Edit map not written: the existing one cannot be backed up ({ex.Message}); the pk3 is not affected.");
            return false;
        }
    }

    /// <summary>
    /// Complete le dossier d'edition pour qu'il soit une base d'editeur autonome : copie de <c>textures</c> et <c>shaders</c> du staging, et
    /// un caulk gris (<c>textures/common/caulk.tga</c>, absent du pk3 car fourni par le jeu). Ne touche jamais au staging ni au pk3.
    /// </summary>
    private static void PrepareEditFolder(string stage, string editDir, string editMap, ImportLog log)
    {
        if (!File.Exists(editMap)) return; // ecriture du .map en echec (deja journalisee) : rien a completer
        try { CopyEditFiles(stage, editDir, editMap, log); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warn("MAP_VISUAL_WRITE_FAILED", $"Edit folder incomplete ({ex.Message}); the pk3 is not affected.");
        }
    }

    private static void CopyEditFiles(string stage, string editDir, string editMap, ImportLog log)
    {
        foreach (string sub in new[] { "textures", "shaders" })
        {
            string src = Path.Combine(stage, sub);
            if (!Directory.Exists(src)) continue;
            foreach (string f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                string dst = Path.Combine(editDir, "base", Path.GetRelativePath(stage, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(f, dst, true);
            }
        }
        string caulk = Path.Combine(editDir, "base", "textures", "common", "caulk.tga");
        Directory.CreateDirectory(Path.GetDirectoryName(caulk)!);
        var grey = new byte[8 * 8 * 4];
        for (int i = 0; i < grey.Length; i += 4) { grey[i] = grey[i + 1] = grey[i + 2] = 96; grey[i + 3] = 255; }
        using (var fs = File.Create(caulk)) TgaWriter.Write(fs, 8, 8, grey);
        log.Info($"Edit folder: {editDir}: open base/maps/{Path.GetFileName(editMap)} in the map editor with the engine path set to this folder.");
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

    /// <summary>
    /// Zippe le staging en <c>&lt;JaBaseDir&gt;\&lt;carte&gt;.pk3</c> (le pk3 existant n'est remplace que si tout reussit). Le nom etant libre,
    /// refuse de remplacer un pk3 qui n'est pas cette carte (ex. <c>assets0</c>) et un <c>shaders/&lt;carte&gt;.shader</c> deja fourni par un
    /// autre pk3 de base (le moteur n'en garderait qu'un : les shaders du jeu ou de la carte seraient perdus).
    /// </summary>
    public static Pk3StepResult CreatePk3(ExportStepResult exported, ImportStepResult imported, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(exported);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(opt);
        string name = imported.MapName;
        if (MapNames.Validate(name) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);
        RequireJaBaseDir(opt, log);
        try
        {
            string pk3 = Path.Combine(opt.JaBaseDir, name + ".pk3");
            if (File.Exists(pk3) && Pk3Contains(pk3, "maps/" + name + ".bsp") == false)
                throw Fail(log, "MAP_NAME_TAKEN", $"{pk3} exists and is not the map {name}: choose another map name.");
            string shader = "shaders/" + ShaderNames.ShaderFileName(name);
            if (File.Exists(Path.Combine(opt.JaBaseDir, shader.Replace('/', Path.DirectorySeparatorChar))))
                throw Fail(log, "MAP_NAME_TAKEN", $"{Path.Combine(opt.JaBaseDir, shader)} already exists: choose another map name.");
            foreach (string other in Directory.EnumerateFiles(opt.JaBaseDir, "*.pk3"))
                if (!string.Equals(Path.GetFullPath(other), Path.GetFullPath(pk3), StringComparison.OrdinalIgnoreCase) && Pk3Contains(other, shader) == true)
                    throw Fail(log, "MAP_NAME_TAKEN", $"{Path.GetFileName(other)} already provides {shader}: choose another map name.");

            string legacy = Path.Combine(opt.JaBaseDir, KotorGames.MapPrefix(opt.Game) + "_" + name + ".pk3");
            if (File.Exists(legacy))
                log.Warn("LEGACY_PK3", $"{legacy} (old naming) also contains maps/{name}.bsp and is loaded after {name}.pk3: delete it.");

            Pk3Builder.Build(exported.StagingDir, pk3);
            return new Pk3StepResult(pk3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>Le pk3 contient-il <paramref name="entry"/> (chemin a '/', casse ignoree) ? null si ce n'est pas un zip lisible (vide, tronque).</summary>
    private static bool? Pk3Contains(string pk3, string entry)
    {
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(pk3);
            return zip.Entries.Any(e => string.Equals(e.FullName.Replace('\\', '/'), entry, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidDataException) { return null; }
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

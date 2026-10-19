using System.Numerics;
using System.Text;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Layout;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Tlk;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Workflow;

/// <summary>Enchaine <see cref="PipelineSteps"/> et garde l'etat (scene, plan, BSP) d'une etape a l'autre.</summary>
public sealed class KotorWorkflowSteps(ExportOptions options) : IWorkflowSteps
{
    private readonly ExportOptions _base = options ?? throw new ArgumentNullException(nameof(options));
    private ExportOptions _opt = options;   // options of the game being worked on (set by Preview)
    private ModuleScene? _scene;
    private IReadOnlyList<ModuleEntity> _entities = [];
    private ImportStepResult? _imported;
    private ExportStepResult? _exported;
    private PreviewModel? _importModel;
    private string? _mapName;               // nom de carte impose (scene fournie par UseScene) ; null : nom du module

    public bool IsInstalled(KotorGame game)
    {
        string dir = _base.ForGame(game).KotorDir;
        return !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir);
    }

    public IReadOnlyList<ModuleListItem> Modules()
    {
        var list = new List<ModuleListItem>();
        foreach (KotorGame game in new[] { KotorGame.Kotor1, KotorGame.Kotor2 })
        {
            if (!IsInstalled(game)) continue;
            string dir = _base.ForGame(game).KotorDir;
            TlkFile? tlk = null;
            string tlkPath = Path.Combine(dir, "dialog.tlk");
            try { if (File.Exists(tlkPath)) tlk = TlkFile.Open(tlkPath); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { }

            foreach (string module in ModuleCatalog.ListModuleNames(dir))
            {
                string name = module;
                try
                {
                    using var res = ResourceResolver.ForModule(dir, module, game);
                    name = ModuleCatalog.Load(res, module, tlk).AreaName;
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { }
                list.Add(new ModuleListItem(game, module, name));
            }
        }
        return list;
    }

    public PreviewModel Preview(KotorGame game, string module, IProgress<string> progress)
    {
        var log = new ImportLog();
        try
        {
            ExportOptions opt = _base.ForGame(game);
            if (!IsInstalled(game))
                throw new PipelineStepException("GAME_NOT_FOUND", $"{KotorGames.DisplayName(game)} folder not found: {opt.KotorDir}");
            ModuleScene scene = PipelineSteps.Preview(module, opt, log);
            IReadOnlyList<ModuleEntity> entities = ReadEntities(opt, scene, module, log);
            PreviewModel model = PreviewBuilder.FromScene(scene, opt.Scale);
            _opt = opt;
            _scene = scene; Assembly = null; _imported = null; _exported = null; _importModel = null; _mapName = null;
            _entities = entities;
            progress.Report($"Preview: {scene.Meshes.Count} mesh(es), {model.TriangleCount} triangle(s), {entities.Count} entity(ies).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    /// <summary>
    /// Fournit une scene deja construite (zone SWTOR) : les etapes Importer, Exporter et Creer PK3 s'enchainent ensuite comme pour un module
    /// KOTOR. <paramref name="opt"/> : options du jeu de la scene ; <paramref name="mapName"/> : nom de carte (null : module de la scene).
    /// </summary>
    public void UseScene(ModuleScene scene, ExportOptions opt, string? mapName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(opt);
        _opt = opt;
        _scene = scene; Assembly = null; _imported = null; _exported = null; _importModel = null;
        _entities = [];
        _mapName = mapName;
    }

    public AssemblySession? Assembly { get; private set; }
    private int _builtRevision = -1;
    public bool AssemblyUpToDate => Assembly is { } a && a.Revision == _builtRevision;
    private ImportLog _frameLog = new();   // journal recevant les avertissements des cadres de porte pendant Build
    private readonly Dictionary<(string Module, string Door, Vector3 Pos), DoorFrame?> _frames = [];

    public PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> progress)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count == 0) throw new ArgumentException("At least one module is required.", nameof(modules));
        if (modules.Count == 1) return Preview(game, modules[0], progress);
        _frames.Clear();
        var log = new ImportLog();
        try
        {
            ExportOptions opt = _base.ForGame(game);
            if (!IsInstalled(game))
                throw new PipelineStepException("GAME_NOT_FOUND", $"{KotorGames.DisplayName(game)} folder not found: {opt.KotorDir}");
            var (parts, links) = AssemblyLoader.LoadParts(modules, opt, log);
            var session = new AssemblySession([.. modules], parts, links, (part, door) => FrameOf(opt, part, door));
            PreviewModel model = BuildAssembled(session, opt, log);
            _opt = opt;
            Assembly = session; _mapName = null;
            _imported = null; _exported = null; _importModel = null;
            progress.Report($"Preview: {modules.Count} modules, {session.AutoLinks.Count} link(s), {session.UnlinkedGroups.Count} unlinked group(s), {model.TriangleCount} triangle(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel ApplyAssembly(IProgress<string> progress)
    {
        AssemblySession session = Assembly ?? throw new InvalidOperationException("Preview an assembly first.");
        var log = new ImportLog();
        try
        {
            PreviewModel model = BuildAssembled(session, _opt, log);
            _imported = null; _exported = null; _importModel = null;
            progress.Report($"Assembly: {session.Modules.Count} modules, {session.UnlinkedGroups.Count} unlinked group(s), {model.TriangleCount} triangle(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    /// <summary>Assemble, retient la scene et les entites (repere assemble) et fabrique l'apercu avec les reperes de toutes les parties.</summary>
    private PreviewModel BuildAssembled(AssemblySession session, ExportOptions opt, ImportLog log)
    {
        _frameLog = log;
        AssemblyResult result = session.Build(log);
        _builtRevision = session.Revision;
        var entities = result.EntitiesByPart.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).ToList();
        PreviewModel model = PreviewBuilder.FromScene(result.Scene, opt.Scale);
        model = model with
        {
            Markers = [.. entities.Select(e => new PreviewMarker(e.Kind, e.Name, e.Position * opt.Scale, e.Yaw, e.Polygon?.Select(p => p * opt.Scale).ToArray()))],
        };
        _scene = result.Scene;
        _entities = entities;
        return model;
    }

    /// <summary>Cadre d'une porte (modele de porte du jeu), mis en cache ; null (cadre par defaut) si le modele est introuvable.</summary>
    private DoorFrame? FrameOf(ExportOptions opt, AssemblyPart part, ModuleEntity door)
    {
        string module = part.Scene.Info.Module;
        var key = (module.ToLowerInvariant(), door.Name, door.Position);
        if (_frames.TryGetValue(key, out DoorFrame? cached)) return cached;
        DoorFrame? frame = AssemblyLoader.FrameOf(opt, part, door, _frameLog);
        _frames[key] = frame;
        return frame;
    }

    public PreviewModel Import(IProgress<string> progress)
    {
        ModuleScene scene = _scene ?? throw new InvalidOperationException("Preview first.");
        if (Assembly is { } a && !(a.IsComplete && a.Revision == _builtRevision))
            throw new PipelineStepException("ASSEMBLY_NOT_LINKED", "The modules are not all linked: link every group before importing.");
        var log = new ImportLog();
        try
        {
            ImportStepResult imported = PipelineSteps.Import(scene, _mapName, _opt, log);
            PreviewModel model = PreviewBuilder.FromImport(imported, _entities, _opt.Scale, _opt.LightmapBrightness, log);
            _imported = imported; _exported = null;
            _importModel = model;
            progress.Report($"Import: {model.TriangleCount} triangle(s), {model.Markers.Count} marker(s), {model.Instances.Count} placed model(s), {imported.Plan.Shaders.Count} shader(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel SetSpawn(System.Numerics.Vector3 position, float yawDegrees)
    {
        ImportStepResult imported = _imported ?? throw new InvalidOperationException("Import first.");
        PreviewModel view = _importModel ?? throw new InvalidOperationException("Import first.");
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string origin = string.Create(ci, $"{position.X:0.###} {position.Y:0.###} {position.Z:0.###}");
        string angle = yawDegrees.ToString("0.###", ci);
        var entities = new List<IReadOnlyDictionary<string, string>>();
        foreach (var e in imported.Plan.Input.Entities)
        {
            if (e.TryGetValue("classname", out string? c) && c is "info_player_start" or "info_player_deathmatch")
                entities.Add(new Dictionary<string, string>(e) { ["origin"] = origin, ["angle"] = angle });
            else entities.Add(e);
        }
        _imported = imported with { Plan = imported.Plan with { Input = imported.Plan.Input with { Entities = entities } } };
        _exported = null;
        return _importModel = view with { Spawn = position, SpawnYaw = yawDegrees };
    }

    public PreviewModel Export(IProgress<string> progress)
    {
        ImportStepResult imported = _imported ?? throw new InvalidOperationException("Import first.");
        var log = new ImportLog();
        try
        {
            ExportStepResult exported = PipelineSteps.Export(imported, _opt, log);
            var textures = new Dictionary<string, PreviewTexture>(StringComparer.Ordinal);
            foreach (ExportTexture t in imported.Plan.Textures)
            {
                string path = Path.Combine(exported.StagingDir, t.RelPath.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    using var fs = File.OpenRead(path);
                    var (w, h, rgba) = TgaReader.Read(fs);
                    textures.TryAdd(PreviewBuilder.KeyOf(t), new PreviewTexture(w, h, rgba));
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    log.Warn("PREVIEW_TEXTURE_UNREADABLE", $"{t.RelPath} : {ex.Message}");
                }
            }
            // La texture "missing" est ecrite comme les autres : on la garde sous la cle de repli.
            if (textures.TryGetValue(ShaderNames.MissingTexture, out PreviewTexture? missing)) textures[PreviewModel.MissingTextureKey] = missing;

            PreviewModel model = PreviewBuilder.FromBsp(exported.Bsp, textures, imported.Plan.Shaders);
            model = PreviewBuilder.WithStagedModels(model, exported.Bsp, exported.StagingDir, _opt.Game, log);
            // Le BSP ne garde ni reperes ni triangles de collision lisibles : on reprend ceux de l'etape Importer.
            if (_importModel is not null) model = model with { Markers = _importModel.Markers, CollisionTriangles = _importModel.CollisionTriangles };
            _exported = exported;
            WarnIfCollisionReduced(log);
            progress.Report($"Export: {model.TriangleCount} triangle(s) read back from {Path.GetFileName(exported.BspPath)}, {model.Instances.Count} placed model(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public string CreatePk3(IProgress<string> progress)
    {
        ImportStepResult imported = _imported ?? throw new InvalidOperationException("Import first.");
        ExportStepResult exported = _exported ?? throw new InvalidOperationException("Export first.");
        var log = new ImportLog();
        try
        {
            string pk3 = PipelineSteps.CreatePk3(exported, imported, _opt, log).Pk3Path;
            progress.Report("Create PK3: " + pk3);
            return pk3;
        }
        finally { Flush(log, progress); }
    }

    private static IReadOnlyList<ModuleEntity> ReadEntities(ExportOptions opt, ModuleScene scene, string module, ImportLog log) =>
        AssemblyLoader.ReadEntities(opt, scene, module, log);

    /// <summary>La vue Exporter reprend la collision de l etape Importer : l avertir si le compilateur l a reduite.</summary>
    internal static void WarnIfCollisionReduced(ImportLog log)
    {
        if (log.Warnings.Any(w => w.Code is "COLLISION_CAPPED" or "WALKABLE_CAPPED"))
            log.Warn("EXPORT_VIEW_COLLISION_STALE",
                "Warning: collisions were reduced by the compiler; the Export view shows the collisions as they were before the reduction.");
    }

    private static void Flush(ImportLog log, IProgress<string> progress)
    {
        foreach (var (code, message) in log.Warnings) progress.Report($"{code}: {message}");
    }
}

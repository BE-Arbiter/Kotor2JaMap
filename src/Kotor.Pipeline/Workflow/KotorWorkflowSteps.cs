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
            _scene = scene; _imported = null; _exported = null; _importModel = null;
            _entities = entities;
            progress.Report($"Previsualiser : {scene.Meshes.Count} maillage(s), {model.TriangleCount} triangle(s), {entities.Count} entite(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel Import(IProgress<string> progress)
    {
        ModuleScene scene = _scene ?? throw new InvalidOperationException("Previsualiser d'abord.");
        var log = new ImportLog();
        try
        {
            ImportStepResult imported = PipelineSteps.Import(scene, null, _opt, log);
            PreviewModel model = PreviewBuilder.FromImport(imported, _entities, _opt.Scale, _opt.LightmapBrightness);
            _imported = imported; _exported = null;
            _importModel = model;
            progress.Report($"Importer : {model.TriangleCount} triangle(s), {model.Markers.Count} repere(s), {model.Instances.Count} modele(s) pose(s), {imported.Plan.Shaders.Count} shader(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel Export(IProgress<string> progress)
    {
        ImportStepResult imported = _imported ?? throw new InvalidOperationException("Importer d'abord.");
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
            progress.Report($"Exporter : {model.TriangleCount} triangle(s) relus dans {Path.GetFileName(exported.BspPath)}, {model.Instances.Count} modele(s) pose(s).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public string CreatePk3(IProgress<string> progress)
    {
        ImportStepResult imported = _imported ?? throw new InvalidOperationException("Importer d'abord.");
        ExportStepResult exported = _exported ?? throw new InvalidOperationException("Exporter d'abord.");
        var log = new ImportLog();
        try
        {
            string pk3 = PipelineSteps.CreatePk3(exported, imported, _opt, log).Pk3Path;
            progress.Report("Creer PK3 : " + pk3);
            return pk3;
        }
        finally { Flush(log, progress); }
    }

    private static IReadOnlyList<ModuleEntity> ReadEntities(ExportOptions opt, ModuleScene scene, string module, ImportLog log)
    {
        try
        {
            using var res = ResourceResolver.ForModule(opt.KotorDir, module.ToLowerInvariant(), opt.Game);
            byte[] lyt = res.Find(scene.Info.AreaResRef, ResourceType.Lyt)
                ?? throw new InvalidDataException($"{scene.Info.AreaResRef}.lyt introuvable.");
            LayoutFile layout = LayoutFile.Parse(Encoding.ASCII.GetString(lyt));
            return ModuleEntitiesReader.Read(res, scene.Info, layout, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("ENTITIES_UNREADABLE", $"Entites du module illisibles ({ex.Message}) : aucun repere.");
            return [];
        }
    }

    /// <summary>La vue Exporter reprend la collision de l etape Importer : l avertir si le compilateur l a reduite.</summary>
    internal static void WarnIfCollisionReduced(ImportLog log)
    {
        if (log.Warnings.Any(w => w.Code is "COLLISION_CAPPED" or "WALKABLE_CAPPED"))
            log.Warn("EXPORT_VIEW_COLLISION_STALE",
                "Attention : collisions reduites par le compilateur, la vue Exporter montre les collisions d avant reduction.");
    }

    private static void Flush(ImportLog log, IProgress<string> progress)
    {
        foreach (var (code, message) in log.Warnings) progress.Report($"{code} : {message}");
    }
}

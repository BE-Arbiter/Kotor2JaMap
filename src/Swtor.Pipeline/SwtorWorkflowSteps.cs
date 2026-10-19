using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;
using Swtor.Core;
using Swtor.Core.Areas;

namespace Swtor.Pipeline;

/// <summary>
/// Les quatre etapes de l'application pour une zone SWTOR. Le « module » est le texte <c>id[:room1,room2]</c> (<see cref="SwtorAreaSpec"/>).
/// L'apercu construit la scene par le meme chemin que la CLI (<see cref="SwtorExporter.Prepare"/>) ; Importer, Exporter et Creer PK3
/// reprennent <see cref="KotorWorkflowSteps"/> (meme code que KOTOR, scene fournie par <see cref="KotorWorkflowSteps.UseScene"/>).
/// </summary>
public sealed class SwtorWorkflowSteps : IWorkflowSteps
{
    private readonly ExportOptions _opt;
    private readonly KotorWorkflowSteps _pipeline;

    public SwtorWorkflowSteps(ExportOptions options, SwtorAssets? assets = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opt = options.ForGame(KotorGame.Swtor);
        Assets = assets ?? new SwtorAssets(options.SwtorDir);
        _pipeline = new KotorWorkflowSteps(_opt);
    }

    public SwtorAssets Assets { get; }

    /// <summary>Options de construction de la scene (CLI : ses options ; application : valeurs par defaut).</summary>
    public SwtorBuildOptions Build { get; set; } = new();

    /// <summary>Nom de carte saisi par l'utilisateur (null ou vide : nom derive de la zone et des rooms, comme la CLI).</summary>
    public string? MapNameOverride { get; set; }

    /// <summary>Nom de carte de la derniere zone previsualisee.</summary>
    public string? MapName { get; private set; }

    /// <summary>Le dossier des zones existe-t-il ? Sinon la branche SWTOR est masquee.</summary>
    public bool IsInstalled(KotorGame game) => game == KotorGame.Swtor && Directory.Exists(Path.Combine(Assets.Root, "world", "areas"));

    /// <summary>Liste vide : les zones viennent du catalogue (<see cref="LoadCatalog"/>), pas de la liste des modules KOTOR.</summary>
    public IReadOnlyList<ModuleListItem> Modules() => [];

    /// <summary>Catalogue des zones, lu du cache JSON du dossier de travail (reconstruit s'il est absent ou obsolete : quelques secondes).</summary>
    public SwtorAreaCatalog LoadCatalog(bool rebuild = false) =>
        SwtorAreaCatalog.Load(Assets, SwtorAreaCatalog.CachePath(_opt.WorkDir), rebuild);

    /// <summary>Rooms et triangles par room d'une zone (identifiant ou nom interne). Quelques secondes pour une grande zone.</summary>
    public SwtorAreaAnalysis Analyze(string area)
    {
        string folder = SwtorExporter.ResolveFolder(area, Assets, _opt.WorkDir)
            ?? throw new PipelineStepException("SWTOR_AREA_NOT_FOUND", $"SWTOR area '{area}' not found under {Assets.Root}.");
        return SwtorAreaAnalyzer.Analyze(folder, Assets)
            ?? throw new PipelineStepException("SWTOR_AREA_UNREADABLE", $"Cannot read the SWTOR area '{area}'.");
    }

    public PreviewModel Preview(KotorGame game, string module, IProgress<string> progress)
    {
        var log = new ImportLog();
        try
        {
            if (!IsInstalled(KotorGame.Swtor))
                throw new PipelineStepException("GAME_NOT_FOUND", $"SWTOR resources folder not found: {Assets.Root} (swtorAssetsDir in the configuration, or SWTOR_ASSETS).");
            var spec = SwtorAreaSpec.Parse(module);
            string folder = SwtorExporter.ResolveFolder(spec.Area, Assets, _opt.WorkDir)
                ?? throw new PipelineStepException("SWTOR_AREA_NOT_FOUND", $"SWTOR area '{spec.Area}' not found under {Assets.Root}.");
            string? requested = string.IsNullOrWhiteSpace(MapNameOverride) ? null : MapNames.Normalize(MapNameOverride);
            if (requested is not null && MapNames.Validate(requested) is { } badName)
                throw new PipelineStepException("MAP_NAME_INVALID", badName);

            SwtorPreparedScene? prepared = SwtorExporter.Prepare(folder, spec, requested, Assets, Build, log, out SwtorExportResult? failure);
            if (prepared is null)
                throw new PipelineStepException(log.Warnings.LastOrDefault().Code ?? "SWTOR_AREA_FAILED", failure?.Export.Error ?? "The area could not be loaded.");

            PreviewModel model = PreviewBuilder.FromScene(prepared.Scene, _opt.Scale);
            _pipeline.UseScene(prepared.Scene, _opt, prepared.MapName);
            MapName = prepared.MapName;
            SwtorSceneStats st = prepared.Stats;
            progress.Report($"Preview: area {prepared.InternalName} -> map {prepared.MapName}, {st.Rooms} room(s), {st.ModelInstances} model instance(s) "
                            + $"({st.MissingModels} missing), {st.Triangles:N0} render triangle(s), {st.Lights} light(s).");
            if (SwtorBudget.Message(st.Triangles) is { } budget)
                progress.Report((SwtorBudget.Level(st.Triangles) == SwtorBudgetLevel.Exceeded ? "WARN SWTOR_BSP_BUDGET: " : "SWTOR_BSP_BUDGET: ") + budget);
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> progress)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count != 1)
            throw new PipelineStepException("SWTOR_ASSEMBLY_UNSUPPORTED", "SWTOR areas cannot be assembled: select a single area (and its rooms).");
        return Preview(game, modules[0], progress);
    }

    public AssemblySession? Assembly => null;
    public bool AssemblyUpToDate => false;
    public PreviewModel ApplyAssembly(IProgress<string> log) => throw new InvalidOperationException("SWTOR areas are not assembled.");

    public PreviewModel Import(IProgress<string> log) => _pipeline.Import(log);

    public PreviewModel Export(IProgress<string> log) => _pipeline.Export(log);

    /// <summary>Un pk3 verrouille (jeu ouvert) donne une erreur claire, jamais un plantage.</summary>
    public string CreatePk3(IProgress<string> log)
    {
        try { return _pipeline.CreatePk3(log); }
        catch (PipelineStepException ex) when (ex.Code == "EXPORT_IO_ERROR")
        {
            throw new PipelineStepException("PK3_LOCKED",
                $"Cannot write the pk3 ({ex.Message}). If Jedi Academy is running, close it (it keeps the pk3 open) and click Create PK3 again; the staged files in {_opt.WorkDir} are kept.");
        }
    }

    /// <summary>Un message par avertissement ; au-dela de 3 pour un meme code, les suivants sont comptes.</summary>
    private static void Flush(ImportLog log, IProgress<string> progress)
    {
        foreach (var group in log.Warnings.GroupBy(w => w.Code))
        {
            int n = 0;
            foreach (var (code, message) in group)
            {
                if (++n <= 3) progress.Report($"{code}: {message}");
            }
            if (n > 3) progress.Report($"{group.Key}: ... and {n - 3} more.");
        }
    }
}

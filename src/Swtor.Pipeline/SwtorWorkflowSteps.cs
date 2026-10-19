using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;
using Swtor.Core;
using Swtor.Core.Areas;
using Swtor.Core.Myp;

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

    private readonly SwtorGameData? _game;

    public SwtorWorkflowSteps(ExportOptions options, SwtorAssets? assets = null, HttpClient? http = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opt = options.ForGame(KotorGame.Swtor);
        if (assets is null && _opt.SwtorFromGame)
        {
            // "From Game" : archives du jeu + cache dans le dossier de travail.
            string? gameDir = string.IsNullOrWhiteSpace(_opt.SwtorGameDir) ? SwtorGameLocator.Detect() : _opt.SwtorGameDir;
            SwtorGameSource? source = gameDir is null ? null : SwtorGameSource.Create(gameDir);
            GameDir = gameDir;
            Assets = new SwtorAssets(GameCacheRoot(_opt.WorkDir), source);
            if (source is not null) _game = new SwtorGameData(source, Assets.Root, NamesCachePath(_opt.WorkDir)) { Http = http };
        }
        else Assets = assets ?? new SwtorAssets(options.SwtorDir);
        _pipeline = new KotorWorkflowSteps(_opt);
    }

    public SwtorAssets Assets { get; }

    /// <summary>Mode Â« From Game Â» : lit les archives du jeu (cache dans le dossier de travail) au lieu d un dossier extrait.</summary>
    public bool FromGame => _opt.SwtorFromGame;

    /// <summary>Dossier d installation utilise en mode Â« From Game Â» (configure ou detecte) ; null si introuvable.</summary>
    public string? GameDir { get; }

    /// <summary>Cache des ressources extraites du jeu : <c><travail>/swtor_game/resources</c>.</summary>
    public static string GameCacheRoot(string workDir) => Path.Combine(workDir, "swtor_game", "resources");

    /// <summary>Cache de la liste de noms de Jedipedia (partage avec la commande swtor-extract).</summary>
    public static string NamesCachePath(string workDir) => Path.Combine(workDir, "swtor_filenames.txt");

    /// <summary>Recoit les messages d avancement des preparations longues (premiere extraction depuis le jeu).</summary>
    public Action<string>? Status { get; set; }

    private IProgress<string>? StatusProgress => Status is { } s ? new SyncProgress(s) : null;

    private sealed class SyncProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }

    private Action<double, string>? _progressSink;

    /// <summary>Recoit l'avancement (fraction 0..1 et libelle) de l'etape en cours : l'application en fait une barre de progression.</summary>
    public void SetProgressSink(Action<double, string>? sink) => _progressSink = sink;

    // « 1500 / 8000 file(s) » (extraction depuis les archives) devient une fraction ; le message continue vers le journal.
    private static readonly System.Text.RegularExpressions.Regex ExtractCount = new(@"^(\d+) / (\d+) file\(s\)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IProgress<string> WithExtractionProgress(IProgress<string>? inner, Action<double, string>? sink) =>
        new SyncProgress(text =>
        {
            inner?.Report(text);
            if (sink is null) return;
            var m = ExtractCount.Match(text);
            if (m.Success && double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) is > 0 and var total)
                sink(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / total, "Extracting from the game archives: " + text);
            else sink(-1d, text);   // etape sans total connu : barre indeterminee, libelle seul
        });

    /// <summary>Texte d une ligne : d ou viennent les ressources.</summary>
    public string SourceDescription => FromGame
        ? (GameDir is null ? "From Game: SWTOR install not found" : "From Game: " + GameDir)
        : "From extracted files: " + Assets.Root;

    /// <summary>Options de construction de la scene (CLI : ses options ; application : valeurs par defaut).</summary>
    public SwtorBuildOptions Build { get; set; } = new();

    /// <summary>Nom de carte saisi par l'utilisateur (null ou vide : nom derive de la zone et des rooms, comme la CLI).</summary>
    public string? MapNameOverride { get; set; }

    /// <summary>Nom de carte de la derniere zone previsualisee.</summary>
    public string? MapName { get; private set; }

    /// <summary>Le dossier des zones existe-t-il ? Sinon la branche SWTOR est masquee.</summary>
    public bool IsInstalled(KotorGame game) =>
        game == KotorGame.Swtor && (FromGame ? _game is not null : Directory.Exists(Path.Combine(Assets.Root, "world", "areas")));

    /// <summary>Liste vide : les zones viennent du catalogue (<see cref="LoadCatalog"/>), pas de la liste des modules KOTOR.</summary>
    public IReadOnlyList<ModuleListItem> Modules() => [];

    /// <summary>Catalogue des zones, lu du cache JSON du dossier de travail (reconstruit s'il est absent ou obsolete : quelques secondes).</summary>
    public SwtorAreaCatalog LoadCatalog(bool rebuild = false)
    {
        _game?.EnsureBase(StatusProgress);
        return SwtorAreaCatalog.Load(Assets, SwtorAreaCatalog.CachePath(_opt.WorkDir), rebuild);
    }

    // Dossier de la zone ; en mode Â« From Game Â», la base puis les fichiers de la zone sont extraits d abord.
    private string? FolderOf(string area, IProgress<string>? progress, Action<double, string>? sink)
    {
        IProgress<string>? p = _game is null ? progress : WithExtractionProgress(progress, sink);
        sink?.Invoke(-1d, "Locating the area");
        _game?.EnsureBase(p);
        sink?.Invoke(-1d, "Resolving the area folder");
        string? folder = SwtorExporter.ResolveFolder(area, Assets, _opt.WorkDir);
        if (folder is not null) _game?.EnsureArea(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), p);
        return folder;
    }

    /// <summary>
    /// Rooms et triangles par room d'une zone (identifiant ou nom interne). Quelques secondes pour une grande zone, plusieurs minutes
    /// pour une carte immense : <paramref name="onProgress"/> (fraction 0..1, negative = indeterminee, et libelle) alimente une barre.
    /// </summary>
    public SwtorAreaAnalysis Analyze(string area, Action<double, string>? onProgress = null)
    {
        string folder = FolderOf(area, StatusProgress, onProgress)
            ?? throw new PipelineStepException("SWTOR_AREA_NOT_FOUND", $"SWTOR area '{area}' not found under {Assets.Root}.");
        var log = new ImportLog { InfoSink = Status, ProgressSink = onProgress };
        return SwtorAreaAnalyzer.Analyze(folder, Assets, log)
            ?? throw new PipelineStepException("SWTOR_AREA_UNREADABLE", $"Cannot read the SWTOR area '{area}'.");
    }

    public PreviewModel Preview(KotorGame game, string module, IProgress<string> progress)
    {
        Action<double, string>? sink = _progressSink;
        var log = new ImportLog { InfoSink = progress.Report, ProgressSink = sink };
        try
        {
            if (!IsInstalled(KotorGame.Swtor))
                throw new PipelineStepException("GAME_NOT_FOUND", FromGame
                    ? "SWTOR install not found: set the game folder in Settings (From Game)."
                    : $"SWTOR resources folder not found: {Assets.Root} (swtorAssetsDir in the configuration, or SWTOR_ASSETS).");
            var spec = SwtorAreaSpec.Parse(module);
            progress.Report($"Preview of SWTOR area '{spec.Area}'" + (spec.Rooms.Count > 0 ? $", {spec.Rooms.Count} room(s) selected." : ", all default rooms."));
            string folder = FolderOf(spec.Area, progress, sink)
                ?? throw new PipelineStepException("SWTOR_AREA_NOT_FOUND", $"SWTOR area '{spec.Area}' not found under {Assets.Root}.");
            progress.Report($"Area folder: {folder}");
            sink?.Invoke(0.01, "Reading the area layout");
            progress.Report("Reading the area layout...");
            string? requested = string.IsNullOrWhiteSpace(MapNameOverride) ? null : MapNames.Normalize(MapNameOverride);
            if (requested is not null && MapNames.Validate(requested) is { } badName)
                throw new PipelineStepException("MAP_NAME_INVALID", badName);

            SwtorPreparedScene? prepared = SwtorExporter.Prepare(folder, spec, requested, Assets, Build, log, out SwtorExportResult? failure);
            if (prepared is null)
                throw new PipelineStepException(log.Warnings.LastOrDefault().Code ?? "SWTOR_AREA_FAILED", failure?.Export.Error ?? "The area could not be loaded.");

            progress.Report("Building the 3D preview...");
            sink?.Invoke(-1d, "Building the 3D preview");
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

    public PreviewModel SetSpawn(System.Numerics.Vector3 position, float yawDegrees) => _pipeline.SetSpawn(position, yawDegrees);

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

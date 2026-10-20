using System.Text.RegularExpressions;
using Jk.Core;
using Jk.Core.Gob;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;

namespace Jk.Pipeline;

/// <summary>
/// Les etapes de l'application pour un niveau de Jedi Knight. Le « module » est le nom du niveau (<c>01narshadda</c>, <c>m2</c>, <c>c1</c>).
/// L'apercu construit la scene par <see cref="JkSceneBuilder.Build(JkFileSystem, JkLevelRef, ImportLog, JkSceneOptions?)"/> puis convertit les objets et les
/// elements mobiles (<see cref="JkObjects"/>) pour les montrer avec leurs vrais modeles ; Importer, Exporter et Creer PK3 reprennent
/// <see cref="KotorWorkflowSteps"/> (scene et conversion des objets fournies par <see cref="KotorWorkflowSteps.UseScene"/>).
/// La carte s'appelle <c>jk_&lt;niveau&gt;</c>. Le jeu JK n'est que lu.
/// </summary>
public sealed class JkWorkflowSteps : IWorkflowSteps, IDisposable
{
    private static readonly Regex CodeLine = new(@"^([A-Z][A-Z0-9_]+): ", RegexOptions.Compiled);

    private readonly ExportOptions _opt;
    private readonly KotorWorkflowSteps _pipeline;
    private IReadOnlyList<JkLevelEntry>? _entries;

    // Etat du dernier apercu : le systeme de fichiers reste ouvert tant que l'etape Importer peut tourner.
    private JkFileSystem? _fs;
    private JkPreparedScene? _prepared;
    private PreviewModel? _importView;   // vue Importer calculee pendant l'apercu (reutilisee si le nom de carte n'a pas change)
    private string? _importViewName;
    private string? _levelName;      // nom du niveau previsualise (le nom de carte par defaut en derive)

    public JkWorkflowSteps(ExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opt = options.ForGame(KotorGame.JediKnight);
        _pipeline = new KotorWorkflowSteps(_opt);
    }

    /// <summary>Dossier d'installation de Jedi Knight (configure ou detecte) ; null ou vide s'il n'est pas renseigne.</summary>
    public string? JkDir => _opt.JediKnightDir;

    /// <summary>Nom de carte saisi par l'utilisateur (null ou vide : <c>jk_&lt;niveau&gt;</c>).</summary>
    public string? MapNameOverride { get; set; }

    /// <summary>Nom de carte du dernier niveau previsualise.</summary>
    public string? MapName { get; private set; }

    /// <summary>Nom de carte par defaut d'un niveau : <c>jk_</c> et le nom du <c>.jkl</c> en minuscules.</summary>
    public static string DefaultMapName(string level) => MapNames.Normalize("jk_" + level);

    /// <summary>Intitule du sous-groupe de l'arbre pour un type de niveau.</summary>
    public static string GroupLabel(JkLevelKind kind) => kind switch
    {
        JkLevelKind.Multiplayer => "Multiplayer",
        JkLevelKind.Ctf => "CTF",
        _ => "Single player",
    };

    public bool IsInstalled(KotorGame game) => game == KotorGame.JediKnight && JkInstall.IsInstalled(JkDir);

    /// <summary>Les niveaux de l'installation (les 34 du jeu), dans l'ordre de leur <c>episode.jk</c>, avec leur nom lisible ; vide si le jeu est absent.</summary>
    public IReadOnlyList<ModuleListItem> Modules()
    {
        if (!IsInstalled(KotorGame.JediKnight)) return [];
        return Entries().Select(e => new ModuleListItem(KotorGame.JediKnight, e.Level.Name, e.DisplayName)).ToList();
    }

    /// <summary>Type (solo, multijoueur, CTF) d'un niveau, ou null s'il n'existe pas.</summary>
    public JkLevelKind? KindOf(string module) =>
        (_entries is not null || IsInstalled(KotorGame.JediKnight)) ? Entries().FirstOrDefault(e => string.Equals(e.Level.Name, module, StringComparison.OrdinalIgnoreCase))?.Level.Kind : null;

    private IReadOnlyList<JkLevelEntry> Entries()
    {
        if (_entries is { } cached) return cached;
        string dir = JkDir!;
        try { return _entries = JkInstall.LevelNames(dir, JkInstall.ListLevels(dir)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            throw new PipelineStepException("JK_LEVELS_UNREADABLE", $"Cannot list the Jedi Knight levels in {dir}: {ex.Message}");
        }
    }

    public PreviewModel Preview(KotorGame game, string module, IProgress<string> progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        var log = new ImportLog { InfoSink = progress.Report };
        try
        {
            if (!IsInstalled(KotorGame.JediKnight))
                throw new PipelineStepException("GAME_NOT_FOUND", $"Jedi Knight folder not found: {(string.IsNullOrWhiteSpace(JkDir) ? "(not configured)" : JkDir)} (set it in Settings).");
            JkLevelRef level = Entries().Select(e => e.Level).FirstOrDefault(l => string.Equals(l.Name, module.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? throw new PipelineStepException("JK_LEVEL_NOT_FOUND", $"Jedi Knight level '{module}' not found in {JkDir} (levels are the .jkl names, such as 01narshadda, m10 or c1).");
            string name = ResolveMapName(level.Name);
            progress.Report($"Preview of Jedi Knight level '{level.Name}' ({JkWorkflowSteps.GroupLabel(level.Kind)}) -> map {name}.");

            CloseLevel();
            JkFileSystem fs = JkFileSystem.ForEpisode(JkDir!, level.EpisodeGob);
            JkPreparedScene prepared;
            try { prepared = JkSceneBuilder.Build(fs, level, log); }
            catch (Exception ex) when (ex is not (OutOfMemoryException or PipelineStepException))   // le niveau est inconnu : toute erreur de lecture devient un message clair
            {
                fs.Dispose();
                throw new PipelineStepException("JK_LEVEL_UNREADABLE", $"Cannot read the level '{level.Name}': {ex.Message}");
            }
            _fs = fs;
            _prepared = prepared;
            MapName = name;
            _levelName = level.Name;

            progress.Report("Building the 3D preview...");
            PreviewModel model = PreviewBuilder.FromScene(prepared.Scene, _opt.Scale);
            model = WithObjects(model, name, progress);
            progress.Report($"Preview: level {level.Name} -> map {name}, {model.TriangleCount:N0} triangle(s), {prepared.Level.Sectors.Count} sector(s), "
                            + $"{model.Instances.Count} placed model(s) (objects and movers).");
            return model;
        }
        finally { Flush(log, progress); }
    }

    public PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> progress)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count != 1)
            throw new PipelineStepException("JK_ASSEMBLY_UNSUPPORTED", "Jedi Knight levels cannot be assembled: select a single level.");
        return Preview(game, modules[0], progress);
    }

    private string ResolveMapName(string level)
    {
        string name = string.IsNullOrWhiteSpace(MapNameOverride) ? DefaultMapName(level) : MapNames.Normalize(MapNameOverride);
        if (MapNames.Validate(name) is { } bad) throw new PipelineStepException("MAP_NAME_INVALID", bad);
        return name;
    }

    // Objets et mobiles : la conversion complete de l'etape Importer est lancee des l'apercu (modeles MD3 reels), la vue Importer est gardee
    // pour l'etape suivante. Une erreur ici ne prive pas de la geometrie : elle est signalee et l'etape Importer la rejouera.
    private PreviewModel WithObjects(PreviewModel scenePreview, string name, IProgress<string> progress)
    {
        _importView = null;
        _importViewName = null;
        try
        {
            progress.Report("Converting objects and movers...");
            var lines = new Collector();
            UseScene(name);
            PreviewModel view = _pipeline.Import(lines);
            _importView = view;
            _importViewName = name;
            foreach (string line in Grouped(lines.Lines.Where(l => !l.StartsWith("Import: ", StringComparison.Ordinal)).ToList())) progress.Report(line);
            var textures = new Dictionary<string, PreviewTexture>(scenePreview.Textures);
            foreach (var (key, tex) in view.Textures)
                if (key.StartsWith(PreviewBuilder.ModelTexturePrefix, StringComparison.Ordinal)) textures[key] = tex;
            return scenePreview with { Textures = textures, Models = view.Models, Instances = view.Instances };
        }
        catch (Exception ex) when (ex is PipelineStepException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            progress.Report($"WARN JK_PREVIEW_OBJECTS: objects and movers could not be shown in the preview ({ex.Message}); the Import step will retry.");
            return scenePreview;
        }
    }

    private void UseScene(string name) =>
        _pipeline.UseScene(_prepared!.Scene, _opt, name, JkObjects.ForPipeline(_prepared, _fs!));

    public AssemblySession? Assembly => null;
    public bool AssemblyUpToDate => false;
    public PreviewModel ApplyAssembly(IProgress<string> log) => throw new InvalidOperationException("Jedi Knight levels are not assembled.");

    public PreviewModel Import(IProgress<string> progress)
    {
        if (_prepared is null || _fs is null) throw new InvalidOperationException("Preview first.");
        string name = ResolveMapName(_levelName!);
        if (_importView is { } cached && _importViewName == name)
        {
            // Deja calcule pendant l'apercu (memes objets, meme nom de carte) : pas de seconde conversion des modeles.
            progress.Report($"Import: {cached.TriangleCount:N0} triangle(s), {cached.Instances.Count} placed model(s) (converted during the preview).");
            return cached;
        }
        UseScene(name);
        MapName = name;
        var lines = new Collector();
        PreviewModel view = _pipeline.Import(lines);
        foreach (string line in Grouped(lines.Lines)) progress.Report(line);
        _importView = view;
        _importViewName = name;
        return view;
    }


    public PreviewModel SetSpawn(System.Numerics.Vector3 position, float yawDegrees) => _pipeline.SetSpawn(position, yawDegrees);

    public PreviewModel Export(IProgress<string> progress) => Report(progress, p => _pipeline.Export(p));

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

    private static PreviewModel Report(IProgress<string> progress, Func<IProgress<string>, PreviewModel> step)
    {
        var lines = new Collector();
        try { return step(lines); }
        finally { foreach (string line in Grouped(lines.Lines)) progress.Report(line); }
    }

    private void CloseLevel()
    {
        _importView = null;
        _importViewName = null;
        _prepared = null;
        var fs = _fs;
        _fs = null;
        fs?.Dispose();
    }

    public void Dispose() => CloseLevel();

    private sealed class Collector : IProgress<string>
    {
        public List<string> Lines { get; } = [];
        public void Report(string value) => Lines.Add(value);
    }

    /// <summary>Un message par avertissement ; au-dela de 3 pour un meme code, les suivants sont comptes. Les lignes sans code passent telles quelles.</summary>
    private static IEnumerable<string> Grouped(IReadOnlyList<string> lines)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            Match m = CodeLine.Match(line);
            if (!m.Success) { yield return line; continue; }
            string code = m.Groups[1].Value;
            int n = counts[code] = counts.GetValueOrDefault(code) + 1;
            if (n <= 3) yield return line;
        }
        foreach (var (code, n) in counts)
            if (n > 3) yield return $"{code}: ... and {n - 3} more.";
    }

    private static void Flush(ImportLog log, IProgress<string> progress)
    {
        foreach (string line in Grouped(log.Warnings.Select(w => $"{w.Code}: {w.Message}").ToList())) progress.Report(line);
    }
}

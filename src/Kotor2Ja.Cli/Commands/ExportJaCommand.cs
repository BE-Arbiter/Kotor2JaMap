using System.CommandLine;
using Kotor.Pipeline;
using Kotor.Pipeline.Assembly;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;

namespace Kotor2Ja.Cli.Commands;

public static class ExportJaCommand
{
    public static Command Create(Option<string?> kotorOption, Option<string>? gameOption = null)
    {
        gameOption ??= GameOption.Create();
        var cmd = new Command("export-ja", "Exports a KOTOR module (or several linked modules assembled into one map), or with --game jk a Jedi Knight level, as a pk3 for Jedi Academy (BSP, shaders, textures).");
        var module = new Argument<string>("module")
        {
            Description = "Module name (e.g. tar_m02aa), or several comma-separated modules assembled into one map (e.g. end_m01aa,end_m01ab). With --game jk: a level name (e.g. 01narshadda, m10, c1).",
        };
        var nameOpt = new Option<string?>("--name") { Description = "Map name (default: the module name; several modules: <reference>_<count>, e.g. end_m01aa_2; Jedi Knight: jk_<level>)." };
        var refOpt = new Option<string?>("--reference") { Description = "Reference module of an assembly: it keeps its position, lighting and spawn point (default: the first module)." };
        var scaleOpt = new Option<float?>("--scale") { Description = "Scale from KOTOR metres to JA units (default: config, 40)." };
        var lmOpt = new Option<float?>("--lightmap-brightness") { Description = "Lightmap brightness multiplier." };
        var limOpt = new Option<string?>("--bsp-limits") { Description = "BSP limits profile: swgl (default) or vanilla." };
        var cfgOpt = new Option<string?>("--config") { Description = "JSON configuration file (default: kotor2ja.config.json next to the executable)." };
        var assetsOpt = new Option<string?>("--assets") { Description = "With --game swtor: root of the extracted SWTOR resources (default: SWTOR_ASSETS, then the default resources folder)." };
        var noVisOpt = new Option<bool>("--novis") { Description = "Disable the room visibility (PVS): one always-visible cluster, the engine draws everything in the frustum." };
        cmd.Arguments.Add(module);
        foreach (Option o in new Option[] { nameOpt, refOpt, scaleOpt, lmOpt, limOpt, cfgOpt, kotorOption, gameOption, assetsOpt, noVisOpt }) cmd.Options.Add(o);
        cmd.SetAction(pr =>
        {
            string? cfgPath = pr.GetValue(cfgOpt) ?? GameOption.DefaultConfigPath;
            if (!TryLoadConfig(cfgPath, Console.Error, out ExportOptions opt)) return 1;
            if (pr.GetValue(noVisOpt)) opt = opt with { NoVis = true };
            if (string.Equals(pr.GetValue(gameOption)?.Trim(), "swtor", StringComparison.OrdinalIgnoreCase))
                return ExportSwtorCommand.Run(opt, pr.GetValue(module)!, pr.GetValue(nameOpt), pr.GetValue(assetsOpt), pr.GetValue(scaleOpt),
                    pr.GetValue(limOpt), null, false, null, Console.Out, Console.Error);
            if (!GameOption.TryParse(pr.GetValue(gameOption), out KotorGame game))
            {
                Console.Error.WriteLine(GameOption.BadValue(pr.GetValue(gameOption), allowSwtor: true));
                return 1;
            }
            opt = opt.ForGame(game);
            string? kotor = pr.GetValue(kotorOption);
            // --kotor est le dossier du jeu choisi : pour Jedi Knight, c'est JediKnightDir (lu par JkExporter), pas KotorDir.
            if (!string.IsNullOrWhiteSpace(kotor)) opt = game == KotorGame.JediKnight ? opt with { JediKnightDir = kotor } : opt with { KotorDir = kotor };
            if (pr.GetValue(scaleOpt) is { } s) opt = opt with { Scale = s };
            if (pr.GetValue(lmOpt) is { } b) opt = opt with { LightmapBrightness = b };
            if (!string.IsNullOrWhiteSpace(pr.GetValue(limOpt))) opt = opt with { BspLimits = pr.GetValue(limOpt)! };
            return Run(opt, pr.GetValue(module)!, pr.GetValue(nameOpt), Console.Out, Console.Error, pr.GetValue(refOpt));
        });
        return cmd;
    }

    /// <summary>Loads the configuration file; an unreadable file gives a one-line English message (file and reason) instead of a stack trace.</summary>
    public static bool TryLoadConfig(string? path, TextWriter error, out ExportOptions options)
    {
        try
        {
            options = ConfigLoader.Load(path);
            return true;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            error.WriteLine($"Cannot read the configuration file '{path}': {e.Message}");
            options = null!;
            return false;
        }
    }

    /// <summary>
    /// Code de sortie : 0 si OK, 1 si echec de compilation ou de limite (ou modules non relies), 2 si un module ou l'installation est inconnu.
    /// <paramref name="module"/> peut lister plusieurs modules separes par des virgules : ils sont assembles (liaison automatique seulement).
    /// </summary>
    public static int Run(ExportOptions opt, string module, string? mapName, TextWriter output, TextWriter error, string? reference = null)
    {
        if (opt.Validate() is { } invalid)
        {
            error.WriteLine($"Invalid options: {invalid}");
            return 1;
        }
        if (mapName is not null && MapNames.Validate(MapNames.Normalize(mapName)) is { } badName)
        {
            error.WriteLine(badName);
            return 1;
        }
        if (!ModuleSelection.TryParse(module, out IReadOnlyList<string> modules, out string? listError))
        {
            error.WriteLine($"Invalid module list: {listError}");
            return 1;
        }
        if (opt.Game == KotorGame.JediKnight) return RunJk(opt, modules, mapName, output, error);
        int referenceIndex = 0;
        if (!string.IsNullOrWhiteSpace(reference))
        {
            referenceIndex = modules.ToList().IndexOf(reference.Trim().ToLowerInvariant());
            if (referenceIndex < 0)
            {
                error.WriteLine($"Reference module '{reference.Trim()}' is not one of the exported modules ({string.Join(", ", modules)}).");
                return 1;
            }
        }
        if (!Directory.Exists(opt.KotorDir))
        {
            error.WriteLine(GameOption.NotFound(opt.Game, opt.KotorDir));
            return 2;
        }
        var known = ModuleCatalog.ListModuleNames(opt.KotorDir);
        foreach (string m in modules)
            if (!known.Contains(m))
            {
                error.WriteLine($"Unknown module: {m}");
                return 2;
            }
        return modules.Count == 1
            ? RunSingle(opt, modules[0], mapName, output, error)
            : RunAssembly(opt, modules, referenceIndex, mapName, output, error);
    }

    /// <summary>
    /// Niveau Jedi Knight : une seule scene (pas d'assemblage), objets et mobiles convertis comme dans l'application.
    /// Code de sortie : 0 si OK, 1 si echec (ou plusieurs niveaux), 2 si le niveau ou l'installation est inconnu.
    /// </summary>
    private static int RunJk(ExportOptions opt, IReadOnlyList<string> levels, string? mapName, TextWriter output, TextWriter error)
    {
        if (levels.Count != 1)
        {
            error.WriteLine("Jedi Knight levels cannot be assembled: give a single level.");
            return 1;
        }
        if (!Jk.Core.JkInstall.IsInstalled(opt.JediKnightDir))
        {
            error.WriteLine(GameOption.NotFound(KotorGame.JediKnight, opt.JediKnightDir));
            return 2;
        }
        Jk.Core.JkLevelRef? level = Jk.Core.JkInstall.ListLevels(opt.JediKnightDir!)
            .FirstOrDefault(l => string.Equals(l.Name, levels[0], StringComparison.OrdinalIgnoreCase));
        if (level is null)
        {
            error.WriteLine($"Unknown level: {levels[0]} (use list-modules --game jk to see the 34 levels).");
            return 2;
        }
        var log = new ImportLog();
        ExportResult r = Jk.Pipeline.JkExporter.Export(level, mapName, opt, log);
        output.WriteLine($"Level {level.Name} ({level.Kind}) -> map {MapNames.Normalize(string.IsNullOrWhiteSpace(mapName) ? Jk.Pipeline.JkWorkflowSteps.DefaultMapName(level.Name) : mapName)}");
        return Report(r, log, output, error);
    }

    private static int RunSingle(ExportOptions opt, string module, string? mapName, TextWriter output, TextWriter error)
    {
        var log = new ImportLog();
        ExportResult r = ModuleExporter.Export(module, mapName, opt, log);
        output.WriteLine($"Module {module} -> map {MapNames.Normalize(mapName ?? module)}");
        return Report(r, log, output, error);
    }

    /// <summary>Assemble les modules (liaison automatique) puis exporte la scene assemblee ; un groupe non relie fait echouer (code 1).</summary>
    private static int RunAssembly(ExportOptions opt, IReadOnlyList<string> modules, int referenceIndex, string? mapName, TextWriter output, TextWriter error)
    {
        string name = MapNames.Normalize(mapName ?? ModuleSelection.DefaultMapName(modules[referenceIndex], modules.Count));
        var log = new ImportLog();
        AssemblySession session;
        try
        {
            session = AssemblyLoader.Load(modules, referenceIndex, opt, log);
        }
        catch (PipelineStepException e)
        {
            error.WriteLine($"Failed: {e.Message}");
            return 1;
        }
        if (!session.IsComplete)
        {
            error.WriteLine(ModuleSelection.NotLinkedMessage(ModuleSelection.UnlinkedModules(session)));
            return 1;
        }
        AssemblyResult assembled = session.Build(log);
        ExportResult r = ModuleExporter.Export(assembled.Scene, name, opt, log);
        output.WriteLine($"Modules {string.Join(", ", modules)} (reference {modules[referenceIndex]}) -> map {name}");
        foreach (DoorLink l in session.AutoLinks)
            output.WriteLine($"Link: {modules[l.FromPart]} door '{l.FromDoor.Name}' -> {modules[l.ToPart]} '{l.ToTarget.Name}'");
        if (assembled.Overlaps.Count > 0) output.WriteLine($"Overlaps: {assembled.Overlaps.Count}");
        return Report(r, log, output, error);
    }

    private static int Report(ExportResult r, ImportLog log, TextWriter output, TextWriter error)
    {
        output.WriteLine($"Meshes: {r.Meshes}");
        output.WriteLine($"Triangles: {r.Triangles}");
        output.WriteLine($"Missing textures: {r.MissingTextures}");
        output.WriteLine("Log by code:");
        foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {g.Key}: {g.Count()}");
        if (r.JournalError is not null) error.WriteLine($"Warning: {r.JournalError}");
        if (!r.Ok)
        {
            error.WriteLine($"Failed: {r.Error}");
            return 1;
        }
        output.WriteLine($"pk3: {r.Pk3Path}");
        return 0;
    }
}

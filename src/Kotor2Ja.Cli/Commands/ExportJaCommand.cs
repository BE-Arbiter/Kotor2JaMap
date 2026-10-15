using System.CommandLine;
using Kotor.Pipeline;
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
        var cmd = new Command("export-ja", "Exports a KOTOR module as a pk3 for Jedi Academy (BSP, shaders, textures).");
        var module = new Argument<string>("module") { Description = "Module name (e.g. tar_m02aa)." };
        var nameOpt = new Option<string?>("--name") { Description = "Map name (default: the module name)." };
        var scaleOpt = new Option<float?>("--scale") { Description = "Scale from KOTOR metres to JA units (default: config, 40)." };
        var lmOpt = new Option<float?>("--lightmap-brightness") { Description = "Lightmap brightness multiplier." };
        var limOpt = new Option<string?>("--bsp-limits") { Description = "BSP limits profile: swgl (default) or vanilla." };
        var cfgOpt = new Option<string?>("--config") { Description = "JSON configuration file (default: kotor2ja.config.json next to the executable)." };
        cmd.Arguments.Add(module);
        foreach (Option o in new Option[] { nameOpt, scaleOpt, lmOpt, limOpt, cfgOpt, kotorOption, gameOption }) cmd.Options.Add(o);
        cmd.SetAction(pr =>
        {
            string? cfgPath = pr.GetValue(cfgOpt) ?? GameOption.DefaultConfigPath;
            if (!TryLoadConfig(cfgPath, Console.Error, out ExportOptions opt)) return 1;
            if (!GameOption.TryParse(pr.GetValue(gameOption), out KotorGame game))
            {
                Console.Error.WriteLine(GameOption.BadValue(pr.GetValue(gameOption)));
                return 1;
            }
            opt = opt.ForGame(game);
            string? kotor = pr.GetValue(kotorOption);
            if (!string.IsNullOrWhiteSpace(kotor)) opt = opt with { KotorDir = kotor };
            if (pr.GetValue(scaleOpt) is { } s) opt = opt with { Scale = s };
            if (pr.GetValue(lmOpt) is { } b) opt = opt with { LightmapBrightness = b };
            if (!string.IsNullOrWhiteSpace(pr.GetValue(limOpt))) opt = opt with { BspLimits = pr.GetValue(limOpt)! };
            return Run(opt, pr.GetValue(module)!, pr.GetValue(nameOpt), Console.Out, Console.Error);
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

    /// <summary>Code de sortie : 0 si OK, 1 si echec de compilation ou de limite, 2 si le module ou l'installation est inconnu.</summary>
    public static int Run(ExportOptions opt, string module, string? mapName, TextWriter output, TextWriter error)
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
        if (!Directory.Exists(opt.KotorDir))
        {
            error.WriteLine(GameOption.NotFound(opt.Game, opt.KotorDir));
            return 2;
        }
        module = module.ToLowerInvariant();
        if (!ModuleCatalog.ListModuleNames(opt.KotorDir).Contains(module))
        {
            error.WriteLine($"Unknown module: {module}");
            return 2;
        }
        var log = new ImportLog();
        ExportResult r = ModuleExporter.Export(module, mapName, opt, log);
        output.WriteLine($"Module {module} -> map {MapNames.Normalize(mapName ?? module)}");
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

using System.CommandLine;
using Kotor.Compiler.Export;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;

namespace Kotor2Ja.Cli.Commands;

public static class ExportJaCommand
{
    public static Command Create(Option<string?> kotorOption)
    {
        var cmd = new Command("export-ja", "Exporte un module KOTOR en pk3 pour Jedi Academy (BSP, shaders, textures).");
        var module = new Argument<string>("module") { Description = "Nom du module (ex. tar_m02aa)." };
        var nameOpt = new Option<string?>("--name") { Description = "Nom de la carte (defaut : le module)." };
        var scaleOpt = new Option<float?>("--scale") { Description = "Echelle metres KOTOR vers unites JA (defaut : config, 36)." };
        var lmOpt = new Option<float?>("--lightmap-brightness") { Description = "Multiplicateur de luminosite des lightmaps." };
        var limOpt = new Option<string?>("--bsp-limits") { Description = "Profil de limites BSP : swgl (defaut) ou vanilla." };
        var cfgOpt = new Option<string?>("--config") { Description = "Fichier de configuration JSON (defaut : kotor2ja.config.json a cote de l'exe)." };
        cmd.Arguments.Add(module);
        foreach (Option o in new Option[] { nameOpt, scaleOpt, lmOpt, limOpt, cfgOpt, kotorOption }) cmd.Options.Add(o);
        cmd.SetAction(pr =>
        {
            string? cfgPath = pr.GetValue(cfgOpt) ?? Path.Combine(AppContext.BaseDirectory, "kotor2ja.config.json");
            ExportOptions opt = ConfigLoader.Load(cfgPath);
            string? kotor = pr.GetValue(kotorOption);
            if (!string.IsNullOrWhiteSpace(kotor)) opt = opt with { KotorDir = kotor };
            if (pr.GetValue(scaleOpt) is { } s) opt = opt with { Scale = s };
            if (pr.GetValue(lmOpt) is { } b) opt = opt with { LightmapBrightness = b };
            if (!string.IsNullOrWhiteSpace(pr.GetValue(limOpt))) opt = opt with { BspLimits = pr.GetValue(limOpt)! };
            return Run(opt, pr.GetValue(module)!, pr.GetValue(nameOpt), Console.Out, Console.Error);
        });
        return cmd;
    }

    /// <summary>Code de sortie : 0 si OK, 1 si echec de compilation ou de limite, 2 si le module ou l'installation est inconnu.</summary>
    public static int Run(ExportOptions opt, string module, string? mapName, TextWriter output, TextWriter error)
    {
        if (opt.Validate() is { } invalid)
        {
            error.WriteLine($"Options invalides : {invalid}");
            return 1;
        }
        if (!Directory.Exists(opt.KotorDir))
        {
            error.WriteLine($"Installation KOTOR introuvable : {opt.KotorDir}");
            return 2;
        }
        module = module.ToLowerInvariant();
        if (!ModuleCatalog.ListModuleNames(opt.KotorDir).Contains(module))
        {
            error.WriteLine($"Module inconnu : {module}");
            return 2;
        }
        var log = new ImportLog();
        ExportResult r = ModuleExporter.Export(module, mapName, opt, log);
        output.WriteLine($"Module {module} -> carte {(mapName ?? module).ToLowerInvariant()}");
        output.WriteLine($"Maillages : {r.Meshes}");
        output.WriteLine($"Triangles : {r.Triangles}");
        output.WriteLine($"Textures manquantes : {r.MissingTextures}");
        output.WriteLine("Journal par code :");
        foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {g.Key} : {g.Count()}");
        if (r.JournalError is not null) error.WriteLine($"Avertissement : {r.JournalError}");
        if (!r.Ok)
        {
            error.WriteLine($"Echec : {r.Error}");
            return 1;
        }
        output.WriteLine($"pk3 : {r.Pk3Path}");
        return 0;
    }
}

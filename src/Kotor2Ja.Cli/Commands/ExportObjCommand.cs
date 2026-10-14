using System.CommandLine;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Tlk;

namespace Kotor2Ja.Cli.Commands;

public static class ExportObjCommand
{
    public static Command Create(Option<string?> kotorOption)
    {
        var cmd = new Command("export-obj", "Exporte un module KOTOR en OBJ (controle visuel dans Blender).");
        var module = new Argument<string>("module") { Description = "Nom du module (ex. tar_m02aa)." };
        var outOpt = new Option<string>("--out") { Description = "Dossier de sortie.", Required = true };
        cmd.Arguments.Add(module);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(kotorOption);
        cmd.SetAction(pr => Run(ListModulesCommand.ResolveKotorDir(pr.GetValue(kotorOption)), pr.GetValue(module)!,
            pr.GetValue(outOpt)!, Console.Out, Console.Error));
        return cmd;
    }

    /// <summary>Code de sortie : 0 si au moins un triangle, 1 sinon, 2 si le module ou l'installation est inconnu.</summary>
    public static int Run(string? kotorDir, string module, string outDir, TextWriter output, TextWriter error)
    {
        if (kotorDir is null || !Directory.Exists(kotorDir))
        {
            error.WriteLine($"Installation KOTOR introuvable : {kotorDir ?? "(aucun chemin)"}");
            return 2;
        }
        module = module.ToLowerInvariant();
        if (!ModuleCatalog.ListModuleNames(kotorDir).Contains(module))
        {
            error.WriteLine($"Module inconnu : {module}");
            return 2;
        }
        string tlkPath = Path.Combine(kotorDir, "dialog.tlk");
        var log = new ImportLog();
        TlkFile? tlk = null;
        if (File.Exists(tlkPath))
        {
            try { tlk = TlkFile.Open(tlkPath); }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                log.Warn("TLK_UNREADABLE", $"dialog.tlk illisible ({ex.Message}), noms de module non traduits.");
            }
        }
        ModuleScene scene;
        try
        {
            using var res = ResourceResolver.ForModule(kotorDir, module);
            scene = ModuleSceneBuilder.Build(res, module, tlk, log);
        }
        catch (InvalidDataException ex)
        {
            error.WriteLine($"Module illisible : {ex.Message}");
            return 2;
        }
        ObjExporter.Export(scene, outDir);
        File.WriteAllLines(Path.Combine(outDir, module + ".journal.txt"), log.Warnings.Select(w => $"{w.Code}\t{w.Message}"));

        long tris = scene.Meshes.Sum(m => (long)m.Triangles.Length / 3);
        int rooms = scene.Meshes.Select(m => m.Room).Distinct().Count();
        int missing = scene.Textures.Count(kv => kv.Value is null);
        output.WriteLine($"Module {module} ({scene.Info.AreaName})");
        output.WriteLine($"Pieces : {rooms}");
        output.WriteLine($"Maillages : {scene.Meshes.Count}");
        output.WriteLine($"Triangles : {tris}");
        output.WriteLine($"Textures manquantes : {missing}");
        output.WriteLine("Journal par code :");
        foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {g.Key} : {g.Count()}");
        output.WriteLine($"Sortie : {Path.GetFullPath(outDir)}");
        return tris > 0 ? 0 : 1;
    }
}

using System.CommandLine;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Tlk;

namespace Kotor2Ja.Cli.Commands;

public static class ExportObjCommand
{
    public static Command Create(Option<string?> kotorOption, Option<string>? gameOption = null)
    {
        gameOption ??= GameOption.Create();
        var cmd = new Command("export-obj", "Exports a KOTOR module as OBJ (visual check in Blender).");
        var module = new Argument<string>("module") { Description = "Module name (e.g. tar_m02aa)." };
        var outOpt = new Option<string>("--out") { Description = "Output folder.", Required = true };
        cmd.Arguments.Add(module);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(kotorOption);
        cmd.Options.Add(gameOption);
        cmd.SetAction(pr =>
        {
            if (!GameOption.TryParse(pr.GetValue(gameOption), out KotorGame game))
            {
                Console.Error.WriteLine(GameOption.BadValue(pr.GetValue(gameOption)));
                return 1;
            }
            if (game == KotorGame.JediKnight)
            {
                Console.Error.WriteLine("export-obj reads KOTOR modules only; use export-ja --game jk for Jedi Knight levels.");
                return 1;
            }
            return Run(GameOption.ResolveDir(pr.GetValue(kotorOption), game), pr.GetValue(module)!, pr.GetValue(outOpt)!,
                Console.Out, Console.Error, game);
        });
        return cmd;
    }

    /// <summary>Code de sortie : 0 si au moins un triangle, 1 sinon, 2 si le module ou l'installation est inconnu.</summary>
    public static int Run(string? kotorDir, string module, string outDir, TextWriter output, TextWriter error, KotorGame game = KotorGame.Kotor1)
    {
        if (kotorDir is null || !Directory.Exists(kotorDir))
        {
            error.WriteLine(GameOption.NotFound(game, kotorDir));
            return 2;
        }
        module = module.ToLowerInvariant();
        if (!ModuleCatalog.ListModuleNames(kotorDir).Contains(module))
        {
            error.WriteLine($"Unknown module: {module}");
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
                log.Warn("TLK_UNREADABLE", $"dialog.tlk unreadable ({ex.Message}), module names not translated.");
            }
        }
        ModuleScene scene;
        try
        {
            using var res = ResourceResolver.ForModule(kotorDir, module, game);
            scene = ModuleSceneBuilder.Build(res, module, tlk, log);
        }
        catch (InvalidDataException ex)
        {
            error.WriteLine($"Module unreadable: {ex.Message}");
            return 2;
        }
        ObjExporter.Export(scene, outDir);
        File.WriteAllLines(Path.Combine(outDir, module + ".journal.txt"), log.Warnings.Select(w => $"{w.Code}\t{w.Message}"));

        long tris = scene.Meshes.Sum(m => (long)m.Triangles.Length / 3);
        int rooms = scene.Meshes.Select(m => m.Room).Distinct().Count();
        int missing = scene.Textures.Count(kv => kv.Value is null);
        output.WriteLine($"Module {module} ({scene.Info.AreaName})");
        output.WriteLine($"Rooms: {rooms}");
        output.WriteLine($"Meshes: {scene.Meshes.Count}");
        output.WriteLine($"Triangles: {tris}");
        output.WriteLine($"Missing textures: {missing}");
        output.WriteLine("Log by code:");
        foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {g.Key}: {g.Count()}");
        output.WriteLine($"Output: {Path.GetFullPath(outDir)}");
        return tris > 0 ? 0 : 1;
    }
}

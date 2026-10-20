using System.CommandLine;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Modules;
using Kotor.Core.Tlk;

namespace Kotor2Ja.Cli.Commands;

public static class ListModulesCommand
{

    public static Command Create(Option<string?> kotorOption, Option<string>? gameOption = null)
    {
        gameOption ??= GameOption.Create();
        var cmd = new Command("list-modules", "Lists the KOTOR modules with their area name (with --game jk: the 34 Jedi Knight levels with their names).");
        cmd.Options.Add(kotorOption);
        cmd.Options.Add(gameOption);
        cmd.SetAction(pr =>
        {
            if (!GameOption.TryParse(pr.GetValue(gameOption), out KotorGame game))
            {
                Console.Error.WriteLine(GameOption.BadValue(pr.GetValue(gameOption)));
                return 1;
            }
            return Run(GameOption.ResolveDir(pr.GetValue(kotorOption), game), Console.Out, Console.Error, game);
        });
        return cmd;
    }

    public static int Run(string? kotorDir, TextWriter output, TextWriter error, KotorGame game = KotorGame.Kotor1)
    {
        if (game == KotorGame.JediKnight)
        {
            if (!Jk.Core.JkInstall.IsInstalled(kotorDir))
            {
                error.WriteLine(GameOption.NotFound(game, kotorDir));
                return 2;
            }
            foreach (Jk.Core.JkLevelEntry level in Jk.Core.JkInstall.LevelNames(kotorDir!, Jk.Core.JkInstall.ListLevels(kotorDir!)))
                output.WriteLine($"{level.Level.Name}\t{level.DisplayName}");
            return 0;
        }
        if (kotorDir is null || !Directory.Exists(kotorDir))
        {
            error.WriteLine(GameOption.NotFound(game, kotorDir));
            return 2;
        }
        string tlkPath = Path.Combine(kotorDir, "dialog.tlk");
        TlkFile? tlk = File.Exists(tlkPath) ? TlkFile.Open(tlkPath) : null;
        foreach (string module in ModuleCatalog.ListModuleNames(kotorDir))
        {
            string name;
            try
            {
                using var res = ResourceResolver.ForModule(kotorDir, module, game);
                name = ModuleCatalog.Load(res, module, tlk).AreaName;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                name = "(unreadable: " + ex.Message + ")";
            }
            output.WriteLine($"{module}\t{name}");
        }
        return 0;
    }
}

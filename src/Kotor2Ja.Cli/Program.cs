// Point d'entree de la commande kotor2ja.
using System.CommandLine;
using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var root = BuildRoot();
        return root.Parse(args).Invoke();
    }

    public static RootCommand BuildRoot()
    {
        var kotorOption = new Option<string?>("--kotor")
        {
            Description = "Install folder of the chosen game (default: KOTOR_PATH / KOTOR2_PATH, then the config file, then the Steam path).",
        };
        var gameOption = new Option<string>("--game")
        {
            Description = "Game to read: kotor (default), kotor2 or swtor (export-ja only: the module argument is then a SWTOR area).",
            DefaultValueFactory = _ => "kotor",
        };
        var root = new RootCommand("Converts KOTOR modules to Jedi Academy.");
        root.Subcommands.Add(ListModulesCommand.Create(kotorOption, gameOption));
        root.Subcommands.Add(ExportObjCommand.Create(kotorOption, gameOption));
        root.Subcommands.Add(ExportJaCommand.Create(kotorOption, gameOption));
        root.Subcommands.Add(ExportSwtorCommand.Create());
        root.Subcommands.Add(SwtorDumpAreaCommand.Create());
        root.Subcommands.Add(SwtorListAreasCommand.Create());
        root.Subcommands.Add(SwtorExtractCommand.Create());
        return root;
    }
}

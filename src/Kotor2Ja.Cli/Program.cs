// Point d'entree de la commande kotor2ja.
using System.CommandLine;
using Kotor2Ja.Cli.Commands;

namespace Kotor2Ja.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        var kotorOption = new Option<string?>("--kotor")
        {
            Description = "Dossier d'installation de KOTOR (defaut : variable KOTOR_PATH, puis le chemin Steam par defaut).",
        };
        var root = new RootCommand("Convertit des modules KOTOR vers Jedi Academy.");
        root.Subcommands.Add(ListModulesCommand.Create(kotorOption));
        root.Subcommands.Add(ExportObjCommand.Create(kotorOption));
        return root.Parse(args).Invoke();
    }
}

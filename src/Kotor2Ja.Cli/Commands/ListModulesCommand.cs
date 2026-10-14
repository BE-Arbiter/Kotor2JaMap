using System.CommandLine;
using Kotor.Core.Archives;
using Kotor.Core.Modules;
using Kotor.Core.Tlk;

namespace Kotor2Ja.Cli.Commands;

public static class ListModulesCommand
{
    public const string DefaultKotorDir = @"C:\Program Files (x86)\Steam\steamapps\common\swkotor";

    public static string? ResolveKotorDir(string? fromOption)
    {
        if (!string.IsNullOrWhiteSpace(fromOption)) return fromOption;
        string? env = Environment.GetEnvironmentVariable("KOTOR_PATH");
        return !string.IsNullOrWhiteSpace(env) ? env : DefaultKotorDir;
    }

    public static Command Create(Option<string?> kotorOption)
    {
        var cmd = new Command("list-modules", "Liste les modules KOTOR avec le nom de leur zone.");
        cmd.Options.Add(kotorOption);
        cmd.SetAction(pr => Run(ResolveKotorDir(pr.GetValue(kotorOption)), Console.Out, Console.Error));
        return cmd;
    }

    public static int Run(string? kotorDir, TextWriter output, TextWriter error)
    {
        if (kotorDir is null || !Directory.Exists(kotorDir))
        {
            error.WriteLine($"Installation KOTOR introuvable : {kotorDir ?? "(aucun chemin)"}");
            return 2;
        }
        string tlkPath = Path.Combine(kotorDir, "dialog.tlk");
        TlkFile? tlk = File.Exists(tlkPath) ? TlkFile.Open(tlkPath) : null;
        foreach (string module in ModuleCatalog.ListModuleNames(kotorDir))
        {
            string name;
            try
            {
                using var res = ResourceResolver.ForModule(kotorDir, module);
                name = ModuleCatalog.Load(res, module, tlk).AreaName;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                name = "(illisible : " + ex.Message + ")";
            }
            output.WriteLine($"{module}\t{name}");
        }
        return 0;
    }
}

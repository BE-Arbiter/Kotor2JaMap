using System.Runtime.InteropServices;
using Kotor.Pipeline.Workflow;
using Kotor2Ja.App;

// Application WinExe : sans console propre. En ligne de commande, on s'attache a celle du parent pour que les messages restent lisibles.
if (args.Length > 0 && OperatingSystem.IsWindows()) AttachConsole(-1);

AutomationOptions? automation = AutomationParser.Parse(args, out string? error);
if (error is not null)
{
    Console.Error.WriteLine("Error: " + error);
    Console.Error.WriteLine(AutomationParser.Usage);
    return 2;
}

using var game = new AppGame(automation);
game.Run();
return game.ExitCode;

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AttachConsole(int processId);

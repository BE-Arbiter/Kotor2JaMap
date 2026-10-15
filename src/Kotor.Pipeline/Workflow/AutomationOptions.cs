using System.Globalization;
using System.Numerics;
using Kotor.Core;

namespace Kotor.Pipeline.Workflow;

/// <summary>
/// Pilotage de l'application en ligne de commande (essais visuels sans clic). <c>RunTo</c> : <c>preview</c>, <c>import</c>, <c>export</c> ou <c>pk3</c>.
/// <c>CameraPosition</c> null : camera par defaut. Position en unites JA, <c>CameraYaw</c> et <c>CameraPitch</c> en degres.
/// </summary>
public sealed record AutomationOptions(KotorGame Game, string Module, string RunTo /* preview|import|export|pk3 */,
    Vector3? CameraPosition, float CameraYaw, float CameraPitch, string? ScreenshotPath, bool Exit)
{
    public static readonly IReadOnlyList<string> Stages = ["preview", "import", "export", "pk3"];

    /// <summary>Position de <see cref="RunTo"/> dans <see cref="Stages"/> (0 = preview ... 3 = pk3).</summary>
    public int StageIndex => Stages.ToList().IndexOf(RunTo);
}

public static class AutomationParser
{
    public const string Usage =
        "Usage: Kotor2Ja.App [--game kotor|kotor2] --module <module> [--run-to preview|import|export|pk3] "
        + "[--camera \"x y z yaw pitch\"] [--screenshot <png>] [--exit]";

    /// <summary>
    /// Analyse la ligne de commande. Sans <c>--module</c> : renvoie null sans erreur (l'application demarre normalement).
    /// Argument invalide : renvoie null et <paramref name="error"/> (message en anglais).
    /// </summary>
    public static AutomationOptions? Parse(string[] args, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        error = null;
        var game = KotorGame.Kotor1;
        string? module = null, screenshot = null;
        string runTo = "preview";
        Vector3? cameraPos = null;
        float yaw = 0, pitch = 0;
        bool exit = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--exit":
                    exit = true;
                    break;
                case "--game":
                case "--module":
                case "--run-to":
                case "--camera":
                case "--screenshot":
                    if (i + 1 >= args.Length) { error = $"Missing value for {a}."; return null; }
                    string v = args[++i];
                    switch (a)
                    {
                        case "--game":
                            switch (v.ToLowerInvariant())
                            {
                                case "kotor": game = KotorGame.Kotor1; break;
                                case "kotor2": game = KotorGame.Kotor2; break;
                                default: error = $"Invalid --game '{v}' (expected kotor or kotor2)."; return null;
                            }
                            break;
                        case "--module":
                            if (string.IsNullOrWhiteSpace(v)) { error = "--module must not be empty."; return null; }
                            module = v.Trim();
                            break;
                        case "--run-to":
                            runTo = v.ToLowerInvariant();
                            if (!AutomationOptions.Stages.Contains(runTo)) { error = $"Invalid --run-to '{v}' (expected preview, import, export or pk3)."; return null; }
                            break;
                        case "--camera":
                            if (!TryCamera(v, out Vector3 p, out yaw, out pitch)) { error = $"Invalid --camera '{v}' (expected \"x y z yaw pitch\", five numbers)."; return null; }
                            cameraPos = p;
                            break;
                        case "--screenshot":
                            if (string.IsNullOrWhiteSpace(v)) { error = "--screenshot must not be empty."; return null; }
                            screenshot = v;
                            break;
                    }
                    break;
                default:
                    error = $"Unknown argument '{a}'.";
                    return null;
            }
        }
        if (module is null)
        {
            if (args.Length > 0) error = "--module is required with --game, --run-to, --camera, --screenshot and --exit.";
            return null;
        }
        return new AutomationOptions(game, module, runTo, cameraPos, yaw, pitch, screenshot, exit);
    }

    private static bool TryCamera(string text, out Vector3 position, out float yaw, out float pitch)
    {
        position = default; yaw = pitch = 0;
        string[] p = text.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 5) return false;
        var f = new float[5];
        for (int i = 0; i < 5; i++)
            if (!float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out f[i]) || !float.IsFinite(f[i])) return false;
        position = new Vector3(f[0], f[1], f[2]);
        yaw = f[3]; pitch = f[4];
        return true;
    }
}

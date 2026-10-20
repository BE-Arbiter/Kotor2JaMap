using System.CommandLine;
using Kotor.Core;
using Kotor.Core.Export;

namespace Kotor2Ja.Cli.Commands;

/// <summary>Parsing of the <c>--game kotor|kotor2|jk</c> option and default install folders per game.</summary>
public static class GameOption
{

    public static Option<string> Create() => new("--game")
    {
        Description = "Game to read: kotor (default), kotor2, jk (Jedi Knight: the module argument is a level such as 01narshadda, m10 or c1) or swtor (export-ja only: the module argument is then a SWTOR area).",
        DefaultValueFactory = _ => "kotor",
    };
    public static bool TryParse(string? text, out KotorGame game)
    {
        switch ((text ?? "kotor").Trim().ToLowerInvariant())
        {
            case "kotor" or "kotor1" or "1": game = KotorGame.Kotor1; return true;
            case "kotor2" or "2": game = KotorGame.Kotor2; return true;
            case "jk" or "jediknight" or "jedi-knight": game = KotorGame.JediKnight; return true;
            default: game = KotorGame.Kotor1; return false;
        }
    }

    /// <summary>Message pour une valeur de --game inconnue ; swtor n'est annonce que pour les commandes qui l'acceptent (export-ja).</summary>
    public static string BadValue(string? text, bool allowSwtor = false) =>
        $"Unknown game: {text} (expected {(allowSwtor ? "kotor, kotor2, swtor or jk" : "kotor, kotor2 or jk")}).";

    /// <summary>
    /// Install folder of a game: the --kotor value, else the shared rule of <see cref="ConfigLoader"/>
    /// (KOTOR_PATH / KOTOR2_PATH, then the config file, then the folders detected through Steam, then the default Steam path).
    /// </summary>
    public static string? ResolveDir(string? fromOption, KotorGame game, string? configPath = null, Func<string, string?>? getEnv = null, Func<GamePaths>? detect = null)
    {
        if (!string.IsNullOrWhiteSpace(fromOption)) return fromOption;
        ExportOptions options;
        try { options = ConfigLoader.Load(configPath ?? DefaultConfigPath, getEnv, detect); }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            options = ConfigLoader.Defaults(getEnv, detect);
        }
        return game == KotorGame.JediKnight ? options.JediKnightDir : options.ForGame(game).KotorDir;
    }

    public static string DefaultConfigPath => ConfigLoader.FindConfig(AppContext.BaseDirectory) ?? Path.Combine(AppContext.BaseDirectory, ConfigLoader.ConfigFileName);

    public static string NotFound(KotorGame game, string? dir) =>
        $"{KotorGames.DisplayName(game)} installation not found: {dir ?? "(no path)"}";
}

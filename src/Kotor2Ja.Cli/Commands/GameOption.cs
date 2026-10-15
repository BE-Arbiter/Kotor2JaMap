using System.CommandLine;
using Kotor.Core;
using Kotor.Core.Export;

namespace Kotor2Ja.Cli.Commands;

/// <summary>Parsing of the <c>--game kotor|kotor2</c> option and default install folders per game.</summary>
public static class GameOption
{

    public static Option<string> Create() => new("--game")
    {
        Description = "Game to read: kotor (default) or kotor2.",
        DefaultValueFactory = _ => "kotor",
    };
    public static bool TryParse(string? text, out KotorGame game)
    {
        switch ((text ?? "kotor").Trim().ToLowerInvariant())
        {
            case "kotor" or "kotor1" or "1": game = KotorGame.Kotor1; return true;
            case "kotor2" or "2": game = KotorGame.Kotor2; return true;
            default: game = KotorGame.Kotor1; return false;
        }
    }

    public static string BadValue(string? text) => $"Unknown game: {text} (expected kotor or kotor2).";

    /// <summary>Install folder of a game: the --kotor value, else KOTOR_PATH / KOTOR2_PATH, else the default Steam path.</summary>
    public static string? ResolveDir(string? fromOption, KotorGame game)
    {
        if (!string.IsNullOrWhiteSpace(fromOption)) return fromOption;
        return ConfigLoader.Defaults().ForGame(game).KotorDir;
    }
}

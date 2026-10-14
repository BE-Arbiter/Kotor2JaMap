namespace Kotor.Core.Tests;

/// <summary>
/// Chemins utilises par les tests d'integration.
/// <see cref="KotorDir"/> : variable d'environnement KOTOR_PATH, sinon le chemin Steam par defaut s'il existe, sinon null
/// (les tests reels sont alors ignores).
/// </summary>
public static class TestEnv
{
    private const string DefaultKotorDir = @"C:\Program Files (x86)\Steam\steamapps\common\swkotor";

    /// <summary>Dossier d'installation de KOTOR, ou null si introuvable.</summary>
    public static string? KotorDir
    {
        get
        {
            string? fromEnv = Environment.GetEnvironmentVariable("KOTOR_PATH");
            if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
            return Directory.Exists(DefaultKotorDir) ? DefaultKotorDir : null;
        }
    }

    /// <summary>Dossier <c>base</c> de Jedi Academy SWGL ou sont ecrits les <c>kotor_&lt;module&gt;.pk3</c>.</summary>
    public static string JaBaseDir { get; } =
        @"C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base";
}

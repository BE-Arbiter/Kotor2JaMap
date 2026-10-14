namespace Kotor.Pipeline.Tests;

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
}

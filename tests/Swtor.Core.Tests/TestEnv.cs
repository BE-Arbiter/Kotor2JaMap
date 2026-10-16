namespace Swtor.Core.Tests;

/// <summary>Chemins des tests d'integration SWTOR : SWTOR_ASSETS, sinon C:\jka_tor_assets\resources s'il existe, sinon null (tests ignores).</summary>
public static class TestEnv
{
    public static string? SwtorAssetsDir
    {
        get
        {
            string root = new SwtorAssets().Root;
            return Directory.Exists(root) ? root : null;
        }
    }
}

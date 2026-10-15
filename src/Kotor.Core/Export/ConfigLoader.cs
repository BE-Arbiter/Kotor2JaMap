using System.Text.Json;

namespace Kotor.Core.Export;

/// <summary>Charge <see cref="ExportOptions"/> depuis un fichier JSON ; les valeurs absentes (ou le fichier) donnent les defauts.</summary>
public static class ConfigLoader
{
    public const string DefaultKotorDir = @"C:\Program Files (x86)\Steam\steamapps\common\swkotor";
    public const string DefaultKotor2Dir = @"C:\Program Files (x86)\Steam\steamapps\common\Knights of the Old Republic II";
    public const string DefaultJaBaseDir = @"C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base";
    public const string DefaultWorkDir = @"C:\jka_maps\kotor_work";

    /// <summary>
    /// Single resolution rule for the game folders: the environment variable (KOTOR_PATH / KOTOR2_PATH) wins over the config file,
    /// which wins over the default Steam path. <paramref name="getEnv"/> lets tests supply a fake environment.
    /// </summary>
    public static ExportOptions Defaults(Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        return new ExportOptions(EnvOr(getEnv, "KOTOR_PATH", DefaultKotorDir), DefaultJaBaseDir, DefaultWorkDir,
            Kotor2Dir: EnvOr(getEnv, "KOTOR2_PATH", DefaultKotor2Dir));
    }

    private static string EnvOr(Func<string, string?> getEnv, string name, string def) =>
        getEnv(name) is { } v && !string.IsNullOrWhiteSpace(v) ? v : def;

    public static ExportOptions Load(string? configPath, Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        var o = Defaults(getEnv);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) return o;
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return o;
        string Str(string key, string def) =>
            Find(root, key) is { ValueKind: JsonValueKind.String } e && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString()! : def;
        float Num(string key, float def) =>
            Find(root, key) is { ValueKind: JsonValueKind.Number } e ? e.GetSingle() : def;
        // Game folders: environment variable first, then the config file, then the default.
        string GameDir(string key, string envName, string current) =>
            getEnv(envName) is { } v && !string.IsNullOrWhiteSpace(v) ? v : Str(key, current);
        return new ExportOptions(GameDir("kotorDir", "KOTOR_PATH", o.KotorDir), Str("jaBaseDir", o.JaBaseDir), Str("workDir", o.WorkDir),
            Num("scale", o.Scale), Num("lightmapBrightness", o.LightmapBrightness), Str("bspLimits", o.BspLimits),
            Kotor2Dir: GameDir("kotor2Dir", "KOTOR2_PATH", o.Kotor2Dir ?? ""));
    }

    private static JsonElement? Find(JsonElement obj, string key)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
}

using System.Text.Json;

namespace Kotor.Core.Export;

/// <summary>Charge <see cref="ExportOptions"/> depuis un fichier JSON ; les valeurs absentes (ou le fichier) donnent les defauts.</summary>
public static class ConfigLoader
{
    public const string DefaultKotorDir = @"C:\Program Files (x86)\Steam\steamapps\common\swkotor";
    public const string DefaultKotor2Dir = @"C:\Program Files (x86)\Steam\steamapps\common\Knights of the Old Republic II";
    public const string DefaultJaBaseDir = @"C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base";
    public static string DefaultWorkDir => GameLocator.DefaultWorkDir();
    public const string DefaultSwtorAssetsDir = @"C:\jka_tor_assets\resources";
    public const string ConfigFileName = "kotor2ja.config.json";

    /// <summary>
    /// Single resolution rule for the game folders: the environment variable (KOTOR_PATH / KOTOR2_PATH) wins over the config file,
    /// which wins over the folders detected through Steam, which win over the default Steam paths.
    /// <paramref name="getEnv"/> and <paramref name="detect"/> let tests supply a fake environment and a fake detection.
    /// </summary>
    public static ExportOptions Defaults(Func<string, string?>? getEnv = null, Func<GamePaths>? detect = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        GamePaths found = (detect ?? (() => GameLocator.Detect()))();
        return new ExportOptions(EnvOr(getEnv, "KOTOR_PATH", found.KotorDir ?? DefaultKotorDir), found.JaBaseDir ?? DefaultJaBaseDir,
            found.WorkDir, Kotor2Dir: EnvOr(getEnv, "KOTOR2_PATH", found.Kotor2Dir ?? DefaultKotor2Dir),
            SwtorDir: getEnv("SWTOR_ASSETS") is { } sw && !string.IsNullOrWhiteSpace(sw) ? sw : null);
    }

    /// <summary>User-level config folder: %APPDATA%Kotor2Ja.</summary>
    public static string DefaultAppDataDir()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(roaming)) roaming = Path.GetTempPath();
        return Path.Combine(roaming, "Kotor2Ja");
    }

    /// <summary>Reading order: the file next to the executable, then the one in %APPDATA%Kotor2Ja. Null when neither exists.</summary>
    public static string? FindConfig(string exeDir, string? appDataDir = null)
    {
        string beside = Path.Combine(exeDir, ConfigFileName);
        if (File.Exists(beside)) return beside;
        string user = Path.Combine(appDataDir ?? DefaultAppDataDir(), ConfigFileName);
        return File.Exists(user) ? user : null;
    }

    /// <summary>The config file (next to the executable, else in %APPDATA%) if it exists, else the detected/default values.</summary>
    public static ExportOptions LoadOrDetect(string exeDir, Func<string, string?>? getEnv = null, Func<GamePaths>? detect = null,
        string? appDataDir = null) =>
        Load(FindConfig(exeDir, appDataDir), getEnv, detect);

    /// <summary>
    /// Saves next to the executable; if that folder is not writable, in %APPDATA%Kotor2Ja. Returns the file written.
    /// An existing file in %APPDATA% only is updated there (the file next to the executable would otherwise take precedence anyway).
    /// </summary>
    public static string SaveWithFallback(string exeDir, ExportOptions o, string? appDataDir = null)
    {
        string beside = Path.Combine(exeDir, ConfigFileName);
        try { Save(beside, o); return beside; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            string user = Path.Combine(appDataDir ?? DefaultAppDataDir(), ConfigFileName);
            Save(user, o);
            return user;
        }
    }

    /// <summary>Writes the options as JSON (same keys as the config file, including kotor2Dir).</summary>
    public static void Save(string path, ExportOptions o)
    {
        var data = new Dictionary<string, object?>
        {
            ["kotorDir"] = o.KotorDir, ["kotor2Dir"] = o.Kotor2Dir ?? "", ["jaBaseDir"] = o.JaBaseDir, ["workDir"] = o.WorkDir,
            ["scale"] = o.Scale, ["lightmapBrightness"] = o.LightmapBrightness, ["bspLimits"] = o.BspLimits,
        };
        if (!string.IsNullOrWhiteSpace(o.SwtorDir)) data["swtorAssetsDir"] = o.SwtorDir;
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string EnvOr(Func<string, string?> getEnv, string name, string def) =>
        getEnv(name) is { } v && !string.IsNullOrWhiteSpace(v) ? v : def;

    public static ExportOptions Load(string? configPath, Func<string, string?>? getEnv = null, Func<GamePaths>? detect = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;
        var o = Defaults(getEnv, detect);
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
            Kotor2Dir: GameDir("kotor2Dir", "KOTOR2_PATH", o.Kotor2Dir ?? ""),
            SwtorDir: getEnv("SWTOR_ASSETS") is { } sw && !string.IsNullOrWhiteSpace(sw) ? sw : Str("swtorAssetsDir", o.SwtorDir ?? "") is { Length: > 0 } cfg ? cfg : null);
    }

    private static JsonElement? Find(JsonElement obj, string key)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
}

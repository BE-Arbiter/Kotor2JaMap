using System.Text.Json;

namespace Kotor.Core.Export;

/// <summary>Charge <see cref="ExportOptions"/> depuis un fichier JSON ; les valeurs absentes (ou le fichier) donnent les defauts.</summary>
public static class ConfigLoader
{
    public const string DefaultKotorDir = @"C:\Program Files (x86)\Steam\steamapps\common\swkotor";
    public const string DefaultJaBaseDir = @"C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\base";
    public const string DefaultWorkDir = @"C:\jka_maps\kotor_work";

    public static ExportOptions Defaults()
    {
        string? env = Environment.GetEnvironmentVariable("KOTOR_PATH");
        return new ExportOptions(!string.IsNullOrWhiteSpace(env) ? env : DefaultKotorDir, DefaultJaBaseDir, DefaultWorkDir);
    }

    public static ExportOptions Load(string? configPath)
    {
        var o = Defaults();
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath)) return o;
        using var doc = JsonDocument.Parse(File.ReadAllText(configPath), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return o;
        string Str(string key, string def) =>
            Find(root, key) is { ValueKind: JsonValueKind.String } e && !string.IsNullOrWhiteSpace(e.GetString()) ? e.GetString()! : def;
        float Num(string key, float def) =>
            Find(root, key) is { ValueKind: JsonValueKind.Number } e ? e.GetSingle() : def;
        return new ExportOptions(Str("kotorDir", o.KotorDir), Str("jaBaseDir", o.JaBaseDir), Str("workDir", o.WorkDir),
            Num("scale", o.Scale), Num("lightmapBrightness", o.LightmapBrightness), Str("bspLimits", o.BspLimits));
    }

    private static JsonElement? Find(JsonElement obj, string key)
    {
        foreach (var p in obj.EnumerateObject())
            if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase)) return p.Value;
        return null;
    }
}

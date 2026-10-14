namespace Kotor.Core.Textures;

/// <summary>Proprietes utiles d'un fichier TXI. <see cref="Raw"/> garde toutes les paires cle/valeur (cles en minuscules).</summary>
public sealed record TxiInfo(string Blending, bool Decal, string? EnvMapTexture, bool IsAnimated,
    IReadOnlyDictionary<string, string> Raw)
{
    public static TxiInfo Empty { get; } = Parse(null);

    public static TxiInfo Parse(string? text)
    {
        var raw = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            foreach (string line in text.Split('\n'))
            {
                string l = line.Trim().TrimEnd('\0');
                if (l.Length == 0) continue;
                int sp = l.IndexOfAny([' ', '\t']);
                string key = (sp < 0 ? l : l[..sp]).ToLowerInvariant();
                string val = sp < 0 ? "" : l[(sp + 1)..].Trim();
                raw[key] = val;
            }
        }
        string blending = raw.TryGetValue("blending", out var b) && b.Length > 0 ? b.ToLowerInvariant() : "default";
        bool decal = raw.TryGetValue("decal", out var d) && d == "1";
        string? env = raw.TryGetValue("envmaptexture", out var e) && e.Length > 0 ? e.ToLowerInvariant()
            : raw.TryGetValue("bumpyshinytexture", out var s) && s.Length > 0 ? s.ToLowerInvariant() : null;
        bool anim = raw.ContainsKey("proceduretype") || raw.ContainsKey("numx") || raw.ContainsKey("numy")
                    || raw.ContainsKey("defaultwidth");
        return new TxiInfo(blending, decal, env, anim, raw);
    }
}

using System.Numerics;
using System.Text.RegularExpressions;
using Jk.Core.Text;

namespace Jk.Core.Jkl;

/// <summary>Lecteur de niveaux .jkl (texte en sections). Voir <c>docs/superpowers/specs/jk-formats.md</c>.</summary>
public static class JklReader
{
    private static readonly Regex FrameGroup = new(@"\(([^)]*)\)", RegexOptions.Compiled);

    public static JklLevel Read(string text)
    {
        var skipped = new List<string>();
        JklHeader header = ReadHeader(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        var materials = new List<string>();
        var colormaps = new List<string>();
        Vector3[] vertices = Array.Empty<Vector3>();
        Vector2[] texVertices = Array.Empty<Vector2>();
        var adjoins = new List<JklAdjoin>();
        var surfaces = new List<JklSurface>();
        var sectors = new List<JklSector>();
        var things = new List<JklThing>();
        var cogs = new List<JklCogInstance>();
        var templates = new Dictionary<string, JklTemplate>(StringComparer.OrdinalIgnoreCase);
        var models = new List<string>();
        var sounds = new List<string>();

        foreach (var (name, lines) in SplitSections(text))
        {
            switch (name.ToLowerInvariant())
            {
                case "header": header = ReadHeader(ReadKeyValues(lines)); break;
                case "sounds": ReadNames(lines, sounds); break;
                case "materials": ReadNames(lines, materials); break; // "idx: nom.mat xTile yTile" : seul le nom est garde
                case "georesource": ReadGeo(lines, colormaps, ref vertices, ref texVertices, adjoins, surfaces); break;
                case "sectors": ReadSectors(lines, sectors); break;
                case "models": ReadNames(lines, models); break;
                case "cogs": ReadCogs(lines, cogs); break;
                case "templates": ReadTemplates(lines, templates); break;
                case "things": ReadThings(lines, things); break;
                // Sections connues mais sans modele ici (lues plus loin via leurs fichiers, ou sans interet pour la conversion).
                case "jk": case "copyright": case "aiclass": case "sprites": case "keyframes": case "animclass":
                case "soundclass": case "cogscripts":
                    break;
                default: skipped.Add(name); break;
            }
        }

        return new JklLevel(header, materials, colormaps, vertices, texVertices, adjoins, surfaces, sectors, things, cogs, templates, models, sounds)
        {
            SkippedSections = skipped,
        };
    }

    /// <summary>
    /// Lit des valeurs <c>frame=(x/y/z:p/y/r)</c> (le prefixe <c>frame=</c> est facultatif ; plusieurs groupes sont acceptes)
    /// et renvoie (position, pitch/yaw/roll) par groupe.
    /// </summary>
    public static IReadOnlyList<(Vector3 Pos, Vector3 Pyr)> ReadFrames(string value)
    {
        var frames = new List<(Vector3, Vector3)>();
        foreach (Match m in FrameGroup.Matches(value))
        {
            string[] halves = m.Groups[1].Value.Split(':');
            frames.Add((ParseTriple(halves[0]), halves.Length > 1 ? ParseTriple(halves[1]) : Vector3.Zero));
        }
        return frames;
    }

    /// <summary>Triplet <c>a/b/c</c> (entre parentheses ou non) ; manquant ou mal forme : zero.</summary>
    public static Vector3 ParseTriple(string s)
    {
        string[] p = s.Trim('(', ')').Split('/');
        if (p.Length < 3) return Vector3.Zero;
        return new Vector3(JkTokenizer.ParseFloat(p[0]), JkTokenizer.ParseFloat(p[1]), JkTokenizer.ParseFloat(p[2]));
    }

    // ---- decoupage en sections ----

    private static List<(string Name, List<(int Number, string Text)> Lines)> SplitSections(string text)
    {
        var result = new List<(string, List<(int, string)>)>();
        List<(int, string)>? current = null;
        foreach (var (n, line) in JkTokenizer.Lines(text))
        {
            if (line.StartsWith("section:", StringComparison.OrdinalIgnoreCase))
            {
                current = new List<(int, string)>();
                result.Add((line[8..].Trim(), current));
                continue;
            }
            current?.Add((n, line));
        }
        return result;
    }

    private static InvalidDataException Bad(int line, string what, Exception? inner = null) =>
        new($"JKL line {line}: {what}", inner);

    // ---- HEADER ----

    private static Dictionary<string, string> ReadKeyValues(List<(int Number, string Text)> lines)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, line) in lines)
        {
            // Le nom de la cle est le prefixe non numerique de la ligne ("World Gravity", "Horizon Sky Offset"...).
            string[] t = JkTokenizer.Split(line);
            int k = 0;
            while (k < t.Length && !LooksNumeric(t[k])) k++;
            if (k == 0) continue;
            raw[string.Join(' ', t[..k]).ToLowerInvariant()] = string.Join(' ', t[k..]);
        }
        return raw;
    }

    private static bool LooksNumeric(string s) => char.IsDigit(s[0]) || ((s[0] == '-' || s[0] == '.') && s.Length > 1);

    private static JklHeader ReadHeader(Dictionary<string, string> raw)
    {
        float[] Floats(string key)
        {
            if (!raw.TryGetValue(key, out string? v)) return Array.Empty<float>();
            try { return JkTokenizer.Split(v).Select(JkTokenizer.ParseFloat).ToArray(); }
            catch (FormatException ex) { throw new InvalidDataException($"JKL header: malformed value for '{key}'", ex); }
        }
        float F(string key, float def = 0) { float[] a = Floats(key); return a.Length > 0 ? a[0] : def; }
        Vector2 V2(string key) { float[] a = Floats(key); return a.Length >= 2 ? new Vector2(a[0], a[1]) : Vector2.Zero; }
        return new JklHeader((int)F("version", 1), F("world gravity", 4f), F("ceiling sky z"), F("horizon distance"),
            F("horizon pixels per rev"), V2("horizon sky offset"), V2("ceiling sky offset"), Floats("mipmap distances"),
            Floats("lod distances"), F("perspective distance"), F("gouraud distance"), raw);
    }

    // ---- listes de noms (sons, materiaux, modeles) ----

    private static bool IsCountLine(string line) => line.StartsWith("world ", StringComparison.OrdinalIgnoreCase);

    private static void ReadNames(List<(int Number, string Text)> lines, List<string> into)
    {
        foreach (var (_, line) in lines)
        {
            if (IsCountLine(line)) continue;
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) break;
            bool indexed = JkTokenizer.TryStripIndex(line, out int idx, out string rest);
            string[] t = JkTokenizer.Split(rest);
            if (t.Length == 0) continue;
            // Un index explicite fait foi (les lignes sont normalement contigues ; un trou est comble par "").
            if (indexed && idx > into.Count && idx < into.Count + 100000) while (into.Count < idx) into.Add("");
            into.Add(t[0]);
        }
    }

    // ---- GEORESOURCE ----

    private static void ReadGeo(List<(int Number, string Text)> lines, List<string> colormaps, ref Vector3[] vertices, ref Vector2[] texVertices,
        List<JklAdjoin> adjoins, List<JklSurface> surfaces)
    {
        int i = 0;
        while (i < lines.Count)
        {
            var (n, line) = lines[i++];
            string[] t = JkTokenizer.Split(line);
            if (!t[0].Equals("world", StringComparison.OrdinalIgnoreCase) || t.Length < 3) continue;
            int count;
            try { count = JkTokenizer.ParseInt(t[^1]); }
            catch (FormatException ex) { throw Bad(n, "malformed count", ex); }
            string kind = string.Join(' ', t[1..^1]).ToLowerInvariant();
            int end = Math.Min(lines.Count, i + count);
            try
            {
                switch (kind)
                {
                    case "colormaps":
                        for (; i < end; i++) colormaps.Add(Fields(lines[i], 1)[0]);
                        break;
                    case "vertices":
                        vertices = new Vector3[end - i];
                        for (int k = 0; i < end; i++, k++)
                        {
                            string[] f = Fields(lines[i], 3);
                            vertices[k] = new Vector3(JkTokenizer.ParseFloat(f[0]), JkTokenizer.ParseFloat(f[1]), JkTokenizer.ParseFloat(f[2]));
                        }
                        break;
                    case "texture vertices":
                        texVertices = new Vector2[end - i];
                        for (int k = 0; i < end; i++, k++)
                        {
                            string[] f = Fields(lines[i], 2);
                            texVertices[k] = new Vector2(JkTokenizer.ParseFloat(f[0]), JkTokenizer.ParseFloat(f[1]));
                        }
                        break;
                    case "adjoins":
                        for (; i < end; i++)
                        {
                            string[] f = Fields(lines[i], 2);
                            adjoins.Add(new JklAdjoin(JkTokenizer.ParseFlags(f[0]), JkTokenizer.ParseInt(f[1])));
                        }
                        break;
                    case "surfaces":
                    {
                        int first = i;
                        i = end;
                        // Les normales suivent les surfaces : une ligne "idx: x y z" par surface.
                        for (int k = 0; first + k < end; k++)
                        {
                            Vector3 normal = Vector3.Zero;
                            if (i < lines.Count)
                            {
                                string[] nf = Fields(lines[i], 3);
                                normal = new Vector3(JkTokenizer.ParseFloat(nf[0]), JkTokenizer.ParseFloat(nf[1]), JkTokenizer.ParseFloat(nf[2]));
                                i++;
                            }
                            surfaces.Add(ParseSurface(Fields(lines[first + k], 9), lines[first + k].Number, normal));
                        }
                        break;
                    }
                    default:
                        i = end;
                        break;
                }
            }
            catch (FormatException ex)
            {
                throw Bad(n, $"malformed '{kind}' entry", ex);
            }
        }
    }

    /// <summary>Champs d'une ligne indexee, sans le prefixe <c>idx:</c> ; erreur s'il y en a moins de <paramref name="min"/>.</summary>
    private static string[] Fields((int Number, string Text) l, int min)
    {
        JkTokenizer.TryStripIndex(l.Text, out _, out string rest);
        string[] f = JkTokenizer.Split(rest);
        if (f.Length < min) throw Bad(l.Number, $"expected at least {min} fields, got {f.Length}");
        return f;
    }

    private static JklSurface ParseSurface(string[] f, int line, Vector3 normal)
    {
        try
        {
            int nv = JkTokenizer.ParseInt(f[8]);
            if (nv < 0 || f.Length < 9 + nv) throw Bad(line, "surface vertex list is truncated");
            var vi = new int[nv];
            var ti = new int[nv];
            for (int k = 0; k < nv; k++)
            {
                string[] p = f[9 + k].Split(',');
                vi[k] = JkTokenizer.ParseInt(p[0]);
                ti[k] = p.Length > 1 ? JkTokenizer.ParseInt(p[1]) : -1;
            }
            // Intensites : une par sommet (JK) ; trois par sommet (RVB) seraient moyennees.
            int rest = f.Length - 9 - nv;
            var inten = new float[nv];
            int stride = nv > 0 && rest >= 3 * nv ? 3 : 1;
            for (int k = 0; k < nv && (k + 1) * stride <= rest; k++)
            {
                float sum = 0;
                for (int c = 0; c < stride; c++) sum += JkTokenizer.ParseFloat(f[9 + nv + k * stride + c]);
                inten[k] = sum / stride;
            }
            return new JklSurface(JkTokenizer.ParseInt(f[0]), JkTokenizer.ParseFlags(f[1]), JkTokenizer.ParseFlags(f[2]),
                JkTokenizer.ParseInt(f[3]), JkTokenizer.ParseInt(f[4]), JkTokenizer.ParseInt(f[5]), JkTokenizer.ParseInt(f[6]),
                JkTokenizer.ParseFloat(f[7]), vi, ti, inten, normal);
        }
        catch (FormatException ex)
        {
            throw Bad(line, "malformed surface", ex);
        }
    }

    // ---- SECTORS ----

    private static void ReadSectors(List<(int Number, string Text)> lines, List<JklSector> into)
    {
        bool open = false;
        int index = 0, cmap = 0, first = 0, count = 0;
        uint flags = 0;
        float amb = 0, extra = 0, vol = 0;
        Vector3 tint = default, thrust = default;
        string? sound = null;

        void Flush()
        {
            if (open) into.Add(new JklSector(index, flags, amb, extra, cmap, tint, sound, vol, first, count, thrust));
            open = false;
        }

        foreach (var (n, line) in lines)
        {
            string[] t = JkTokenizer.Split(line);
            try
            {
                switch (t[0].ToUpperInvariant())
                {
                    case "SECTOR":
                        Flush();
                        open = true;
                        index = JkTokenizer.ParseInt(t[1]);
                        flags = 0; amb = 0; extra = 0; cmap = 0; tint = default; sound = null; vol = 0; first = 0; count = 0; thrust = default;
                        break;
                    case "FLAGS": flags = JkTokenizer.ParseFlags(t[1]); break;
                    case "AMBIENT": amb = JkTokenizer.ParseFloat(t[^1]); break; // "AMBIENT LIGHT x"
                    case "EXTRA": extra = JkTokenizer.ParseFloat(t[^1]); break;  // "EXTRA LIGHT x"
                    case "COLORMAP": cmap = JkTokenizer.ParseInt(t[1]); break;
                    case "TINT": tint = new Vector3(JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2]), JkTokenizer.ParseFloat(t[3])); break;
                    case "SOUND": sound = t[1]; vol = t.Length > 2 ? JkTokenizer.ParseFloat(t[2]) : 1f; break;
                    case "THRUST": thrust = new Vector3(JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2]), JkTokenizer.ParseFloat(t[3])); break;
                    case "SURFACES": first = JkTokenizer.ParseInt(t[1]); count = JkTokenizer.ParseInt(t[2]); break;
                    default: break; // WORLD, BOUNDBOX, COLLIDEBOX, CENTER, RADIUS, VERTICES et sa liste d'index (deduite des surfaces)
                }
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
            {
                throw Bad(n, $"malformed sector line '{line}'", ex);
            }
        }
        Flush();
    }

    // ---- COGS, TEMPLATES, THINGS ----

    private static void ReadCogs(List<(int Number, string Text)> lines, List<JklCogInstance> into)
    {
        foreach (var (n, line) in lines)
        {
            if (IsCountLine(line)) continue;
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) break;
            if (!JkTokenizer.TryStripIndex(line, out int idx, out string rest)) throw Bad(n, "cog line without index");
            string[] t = JkTokenizer.Split(rest);
            if (t.Length == 0) continue;
            into.Add(new JklCogInstance(idx, t[0], t[1..]));
        }
    }

    private static Dictionary<string, string> ParseParams(string[] tokens, int from)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = from; i < tokens.Length; i++)
        {
            int eq = tokens[i].IndexOf('=');
            if (eq <= 0) continue;
            string k = tokens[i][..eq], v = tokens[i][(eq + 1)..];
            // Plusieurs frame= : concatenes ; la lecture se fait par ReadFrames.
            d[k] = k.Equals("frame", StringComparison.OrdinalIgnoreCase) && d.TryGetValue(k, out string? old) ? old + " " + v : v;
        }
        return d;
    }

    private static void ReadTemplates(List<(int Number, string Text)> lines, Dictionary<string, JklTemplate> into)
    {
        foreach (var (n, line) in lines)
        {
            if (IsCountLine(line)) continue;
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) break;
            string[] t = JkTokenizer.Split(line);
            if (t.Length < 2) throw Bad(n, "template line needs a name and a parent");
            into[t[0]] = new JklTemplate(t[0], t[1], ParseParams(t, 2));
        }
    }

    private static void ReadThings(List<(int Number, string Text)> lines, List<JklThing> into)
    {
        foreach (var (n, line) in lines)
        {
            if (IsCountLine(line)) continue;
            if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) break;
            if (!JkTokenizer.TryStripIndex(line, out int idx, out string rest)) throw Bad(n, "thing line without index");
            string[] t = JkTokenizer.Split(rest);
            if (t.Length < 9) throw Bad(n, $"thing line needs 9 fields, got {t.Length}");
            try
            {
                into.Add(new JklThing(idx, t[0], t[1],
                    new Vector3(JkTokenizer.ParseFloat(t[2]), JkTokenizer.ParseFloat(t[3]), JkTokenizer.ParseFloat(t[4])),
                    new Vector3(JkTokenizer.ParseFloat(t[5]), JkTokenizer.ParseFloat(t[6]), JkTokenizer.ParseFloat(t[7])),
                    JkTokenizer.ParseInt(t[8]), ParseParams(t, 9)));
            }
            catch (FormatException ex)
            {
                throw Bad(n, "malformed thing", ex);
            }
        }
    }
}

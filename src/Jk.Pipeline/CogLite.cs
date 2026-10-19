using System.Text.RegularExpressions;
using Jk.Core.Jkl;

namespace Jk.Pipeline;

/// <summary>Symbole d'un script COG (<c>surface sign mask=0x448</c>) : type en minuscules, valeur par defaut eventuelle, <c>local</c> = pas de valeur dans l'instance.</summary>
internal sealed record CogSymbol(string Type, string Name, string? Default, bool Local);

/// <summary>Appel de fonction du code d'un script : nom (casse du script) et arguments sous forme de texte, virgules de premier niveau.</summary>
internal sealed record CogCall(string Function, string[] Args);

/// <summary>
/// Lecture MINIMALE d'un script COG, juste ce qu'il faut aux effets de surface (tache 5) : symboles, liaison aux valeurs d'une instance du niveau,
/// appels de fonction du message <c>startup</c>. Pas d'interpretation du code (boucles, conditions et delais sont ignores) : la lecture et le
/// classement complets des COG sont la tache 8.
/// </summary>
internal sealed class CogLite
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "for", "while", "switch", "return", "call", "else", "do", "sizeof",
    };

    private readonly Dictionary<string, string> _bodies = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CogSymbol> Symbols { get; }

    /// <summary>Tout le code (sans commentaires), pour les recherches de texte.</summary>
    public string Code { get; }

    private CogLite(IReadOnlyList<CogSymbol> symbols, string code)
    {
        Symbols = symbols;
        Code = code;
        SplitMessages();
    }

    public bool HasMessage(string name) => Symbols.Any(s => s.Type == "message" && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static CogLite Parse(string text)
    {
        // Commentaires : /* ... */, // ... et # ... jusqu'a la fin de ligne.
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\r\n]*", "");
        text = Regex.Replace(text, @"#[^\r\n]*", "");
        var symbols = new List<CogSymbol>();
        var code = new System.Text.StringBuilder();
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Equals("symbols", StringComparison.OrdinalIgnoreCase)) { section = "symbols"; continue; }
            if (line.Equals("code", StringComparison.OrdinalIgnoreCase)) { section = "code"; continue; }
            if (section == "symbols")
            {
                if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) { section = ""; continue; }
                if (ParseSymbol(line) is { } sym) symbols.Add(sym);
            }
            else if (section == "code")
            {
                if (line.Equals("end", StringComparison.OrdinalIgnoreCase)) { section = ""; continue; }
                code.AppendLine(line);
            }
        }
        return new CogLite(symbols, code.ToString());
    }

    private static CogSymbol? ParseSymbol(string line)
    {
        List<string> tokens = Tokens(line);
        if (tokens.Count < 2) return null;
        string type = tokens[0].ToLowerInvariant();
        string name = tokens[1];
        string? def = null;
        int eq = name.IndexOf('=');
        if (eq >= 0) { def = Unquote(name[(eq + 1)..]); name = name[..eq]; }
        bool local = false;
        for (int i = 2; i < tokens.Count; i++)
            foreach (string part in tokens[i].Split(','))
                if (part.Equals("local", StringComparison.OrdinalIgnoreCase)) local = true;
        return name.Length == 0 ? null : new CogSymbol(type, name, def, local);
    }

    /// <summary>Jetons separes par des blancs ; un texte entre apostrophes (<c>'0 0 1'</c>) reste un seul jeton.</summary>
    private static List<string> Tokens(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '\'') quoted = !quoted;
            if (char.IsWhiteSpace(c) && !quoted) { if (cur.Length > 0) { result.Add(cur.ToString()); cur.Clear(); } }
            else cur.Append(c);
        }
        if (cur.Length > 0) result.Add(cur.ToString());
        return result;
    }

    private static string Unquote(string s) => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;

    /// <summary>
    /// Valeurs des symboles pour une instance : les symboles ni <c>local</c> ni <c>message</c>, dans l'ordre, prennent les valeurs de l'instance ;
    /// un symbole sans valeur (instance trop courte, ou <c>local</c>) garde sa valeur par defaut, s'il en a une. Cles insensibles a la casse.
    /// </summary>
    public IReadOnlyDictionary<string, string> Bind(JklCogInstance instance)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int k = 0;
        foreach (CogSymbol s in Symbols)
        {
            if (s.Type == "message") continue;
            if (!s.Local && k < instance.Values.Count) values[s.Name] = Unquote(instance.Values[k++]);
            else
            {
                if (!s.Local) k++;
                if (s.Default is not null) values[s.Name] = s.Default;
            }
        }
        return values;
    }

    /// <summary>
    /// Symboles qu'un acces <c>nom[i]</c> parcourt : <paramref name="name"/> puis les symboles suivants du meme type nommes comme lui a un
    /// numero pres (<c>convey0</c>, <c>convey1</c>... ; <c>sign</c>, <c>sign1</c>...).
    /// </summary>
    public IReadOnlyList<string> Family(string name)
    {
        int start = Symbols.ToList().FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return [];
        CogSymbol first = Symbols[start];
        string stem = first.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var family = new List<string> { first.Name };
        for (int i = start + 1; i < Symbols.Count; i++)
        {
            CogSymbol s = Symbols[i];
            if (s.Type != first.Type || !Regex.IsMatch(s.Name, "^" + Regex.Escape(stem) + @"\d+$", RegexOptions.IgnoreCase)) break;
            family.Add(s.Name);
        }
        return family;
    }

    // ---- messages et appels ----

    private void SplitMessages()
    {
        var labels = new List<(string Name, int Start, int End)>();
        foreach (Match m in Regex.Matches(Code, @"(?m)^[ \t]*([A-Za-z_]\w*)[ \t]*:"))
            if (HasMessage(m.Groups[1].Value)) labels.Add((m.Groups[1].Value, m.Index, m.Index + m.Length));
        for (int i = 0; i < labels.Count; i++)
        {
            int end = i + 1 < labels.Count ? labels[i + 1].Start : Code.Length;
            _bodies[labels[i].Name] = Code[labels[i].End..end];
        }
        // Etiquettes consecutives qui partagent un corps (touched: entered: ...) : celles dont le corps est vide prennent celui de la suivante.
        for (int i = labels.Count - 2; i >= 0; i--)
            if (_bodies[labels[i].Name].Trim().Length == 0) _bodies[labels[i].Name] = _bodies[labels[i + 1].Name];
    }

    /// <summary>Appels du message <c>startup</c>, ceux des messages qu'il appelle par <c>call nom;</c> compris (dans l'ordre du texte, trois niveaux au plus).</summary>
    public IReadOnlyList<CogCall> StartupCalls() =>
        _bodies.ContainsKey("startup") ? ExtractCalls(Expand(_bodies["startup"], 3)) : [];

    /// <summary>Tous les appels du script, tous messages confondus.</summary>
    public IReadOnlyList<CogCall> AllCalls() => ExtractCalls(Code);

    private string Expand(string body, int depth) =>
        depth <= 0 ? body : Regex.Replace(body, @"\bcall\s+(\w+)\s*;", m =>
            _bodies.TryGetValue(m.Groups[1].Value, out string? other) ? Expand(other, depth - 1) : "");

    private static List<CogCall> ExtractCalls(string text)
    {
        var calls = new List<CogCall>();
        foreach (Match m in Regex.Matches(text, @"\b([A-Za-z_]\w*)\s*\("))
        {
            string fn = m.Groups[1].Value;
            if (Keywords.Contains(fn)) continue;
            int open = m.Index + m.Length - 1;
            int depth = 0, close = -1;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) { close = i; break; }
            }
            if (close < 0) continue;
            calls.Add(new CogCall(fn, SplitArgs(text[(open + 1)..close])));
        }
        return calls;
    }

    private static string[] SplitArgs(string args)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        bool quoted = false;
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (c == '\'') quoted = !quoted;
            else if (quoted) continue;
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0) { parts.Add(args[last..i].Trim()); last = i + 1; }
        }
        string tail = args[last..].Trim();
        if (tail.Length > 0 || parts.Count > 0) parts.Add(tail);
        return [.. parts];
    }
}

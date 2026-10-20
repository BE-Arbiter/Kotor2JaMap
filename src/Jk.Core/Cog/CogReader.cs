using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jk.Core.Cog;

/// <summary>
/// Lecture d'un script COG (texte). Sections <c>symbols ... end</c> et <c>code ... end</c> ; commentaires <c>//</c>, <c>#</c> et <c>/* */</c>.
/// Le code est decoupe en etiquettes ; pour chacune, on garde les appels de verbes dans l'ordre avec leurs conditions englobantes.
/// Pas d'interpretation (valeurs, boucles, delais) : c'est le travail du classifieur. Un script mal forme ne leve pas d'exception, on en lit ce qu'on peut.
/// </summary>
public static class CogReader
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "if", "for", "while", "switch", "return", "call", "else", "do", "sizeof", "case", "default",
    };

    public static CogScript Read(string text)
    {
        var warnings = new List<string>();
        text = StripComments(text, warnings);

        var symbols = new List<CogSymbol>();
        var code = new StringBuilder();
        string section = "";
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.Equals("symbols", StringComparison.OrdinalIgnoreCase)) { section = "symbols"; continue; }
            if (line.Equals("code", StringComparison.OrdinalIgnoreCase)) { section = "code"; continue; }
            if (section != "" && line.Equals("end", StringComparison.OrdinalIgnoreCase)) { section = ""; continue; }
            if (section == "symbols") { if (ParseSymbol(line) is { } sym) symbols.Add(sym); }
            else if (section == "code") code.AppendLine(line);
        }

        var messageNames = new HashSet<string>(symbols.Where(s => s.Type == "message").Select(s => s.Name), StringComparer.OrdinalIgnoreCase);
        if (section != "") warnings.Add($"section '{section}' is not closed by 'end' (the script looks truncated)");
        return new CogScript(symbols, SplitHandlers(code.ToString(), messageNames, warnings)) { Warnings = warnings };
    }

    /// <summary>
    /// Retire les commentaires <c>//</c>, <c>#</c> et <c>/* */</c> sans toucher aux litteraux entre apostrophes ou guillemets
    /// (un litteral ne traverse pas une ligne : la fin de ligne le referme). Un commentaire de bloc non ferme est signale.
    /// </summary>
    internal static string StripComments(string text, List<string> warnings)
    {
        var sb = new StringBuilder(text.Length);
        char quote = '\0';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (c == quote || c == '\n') quote = '\0';
                sb.Append(c);
            }
            else if (c is '\'' or '"') { quote = c; sb.Append(c); }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { warnings.Add("block comment '/*' is not closed (the script looks truncated)"); break; }
                sb.Append(' ');
                i = end + 1;
            }
            else if (c == '#' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i + 1 < text.Length && text[i + 1] != '\r' && text[i + 1] != '\n') i++;
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- symboles ----

    private static CogSymbol? ParseSymbol(string line)
    {
        List<string> tokens = Tokens(line);
        if (tokens.Count < 2) return null;
        string type = tokens[0].ToLowerInvariant();
        string name = tokens[1];
        string? def = null;
        int eq = name.IndexOf('=');
        if (eq >= 0) { def = Unquote(name[(eq + 1)..]); name = name[..eq]; }
        bool local = false, noLink = false;
        string? desc = null, mask = null;
        int? linkId = null;
        for (int i = 2; i < tokens.Count; i++)
            foreach (string part in tokens[i].Split(','))
            {
                if (part.Equals("local", StringComparison.OrdinalIgnoreCase)) local = true;
                else if (part.Equals("nolink", StringComparison.OrdinalIgnoreCase)) noLink = true;
                else if (part.StartsWith("desc=", StringComparison.OrdinalIgnoreCase)) desc = part[5..];
                else if (part.StartsWith("mask=", StringComparison.OrdinalIgnoreCase)) mask = part[5..];
                else if (part.StartsWith("linkid=", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(part[7..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) linkId = id;
            }
        return name.Length == 0 ? null : new CogSymbol(type, name, def, local, noLink, desc, mask, linkId);
    }

    /// <summary>Jetons separes par des blancs ; un texte entre apostrophes (<c>'0 0 1'</c>) reste un seul jeton.</summary>
    private static List<string> Tokens(string line)
    {
        var result = new List<string>();
        var cur = new StringBuilder();
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

    internal static string Unquote(string s) => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;

    // ---- code ----

    private static List<CogHandler> SplitHandlers(string code, HashSet<string> messages, List<string> warnings)
    {
        var labels = new List<(string Name, int Start, int BodyStart)>();
        foreach (Match m in Regex.Matches(code, @"(?m)^[ \t]*([A-Za-z_]\w*)[ \t]*:(?!:)"))
            if (!Keywords.Contains(m.Groups[1].Value)) labels.Add((m.Groups[1].Value, m.Index, m.Index + m.Length));
        var handlers = new List<CogHandler>();
        for (int i = 0; i < labels.Count; i++)
        {
            int end = i + 1 < labels.Count ? labels[i + 1].Start : code.Length;
            var parser = new StatementParser(code[labels[i].BodyStart..end]);
            List<CogCallSite> calls = parser.Run();
            foreach (string w in parser.Problems) warnings.Add($"label '{labels[i].Name}': {w}");
            bool returns = calls.Any(c => c.Function.Equals("return", StringComparison.OrdinalIgnoreCase) && c.Conditions.Count == 0);
            handlers.Add(new CogHandler(labels[i].Name, messages.Contains(labels[i].Name), calls, returns));
        }
        return handlers;
    }

    /// <summary>Parcours des instructions d'un corps : <c>if/else</c>, boucles, blocs, instructions simples ; collecte les appels avec leurs conditions.</summary>
    private sealed class StatementParser
    {
        private readonly string _s;
        private int _i;
        private readonly List<CogCallSite> _calls = [];

        /// <summary>Anomalies de structure rencontrees (parenthese ou accolade jamais fermee, accolade fermante en trop).</summary>
        public List<string> Problems { get; } = [];

        public StatementParser(string body) => _s = body;

        public List<CogCallSite> Run()
        {
            while (_i < _s.Length)
            {
                int before = _i;
                Statement([], false);
                if (_i == before) _i++; // securite : toujours avancer
            }
            return _calls;
        }

        private void SkipSpace() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        private string PeekWord()
        {
            int j = _i;
            while (j < _s.Length && (char.IsLetterOrDigit(_s[j]) || _s[j] == '_')) j++;
            return _s[_i..j];
        }

        private bool ParenFollows(int at)
        {
            while (at < _s.Length && char.IsWhiteSpace(_s[at])) at++;
            return at < _s.Length && _s[at] == '(';
        }

        private string ReadParens()
        {
            SkipSpace();
            if (_i >= _s.Length || _s[_i] != '(') return "";
            int depth = 0, start = _i + 1;
            for (; _i < _s.Length; _i++)
            {
                if (_s[_i] == '(') depth++;
                else if (_s[_i] == ')' && --depth == 0) { string r = _s[start.._i]; _i++; return r.Trim(); }
            }
            Problems.Add("'(' is never closed");
            return _s[start..].Trim();
        }

        private void Statement(IReadOnlyList<string> conds, bool loop)
        {
            SkipSpace();
            if (_i >= _s.Length) return;
            char c = _s[_i];
            if (c == '{')
            {
                _i++;
                while (_i < _s.Length)
                {
                    SkipSpace();
                    if (_i < _s.Length && _s[_i] == '}') { _i++; return; }
                    int before = _i;
                    Statement(conds, loop);
                    if (_i == before) _i++;
                }
                Problems.Add("'{' is never closed");
                return;
            }
            if (c == '}') { Problems.Add("'}' without a matching '{'"); _i++; return; }
            if (c == ';') { _i++; return; }

            string word = PeekWord();
            string lower = word.ToLowerInvariant();
            // « switch », « if »... suivis d une parenthese seulement : COG utilise « switch » comme nom de variable.
            if (lower is "if" or "for" or "while" or "switch" && !ParenFollows(_i + word.Length)) lower = "";
            switch (lower)
            {
                case "if":
                {
                    _i += word.Length;
                    string cond = ReadParens();
                    Statement([.. conds, cond], loop);
                    SkipSpace();
                    if (PeekWord().Equals("else", StringComparison.OrdinalIgnoreCase))
                    {
                        _i += 4;
                        Statement([.. conds, "!(" + cond + ")"], loop);
                    }
                    return;
                }
                case "else":
                    _i += 4;
                    Statement(conds, loop);
                    return;
                case "for":
                case "while":
                    _i += word.Length;
                    ReadParens();
                    Statement(conds, true);
                    return;
                case "do":
                    _i += 2;
                    Statement(conds, true);
                    SkipSpace();
                    if (PeekWord().Equals("while", StringComparison.OrdinalIgnoreCase)) { _i += 5; ReadParens(); }
                    return;
                case "switch":
                    _i += word.Length;
                    ReadParens();
                    Statement(conds, loop);
                    return;
                case "case":
                case "default":
                    while (_i < _s.Length && _s[_i] != ':') _i++;
                    _i++;
                    return;
            }

            // Instruction simple : jusqu'au ';' de premier niveau.
            int start = _i, depth = 0;
            char quote = '\0'; // litteral en cours : apostrophe ou guillemet (une apostrophe dans "..." n est pas un delimiteur)
            for (; _i < _s.Length; _i++)
            {
                char ch = _s[_i];
                if (quote != '\0') { if (ch == quote) quote = '\0'; continue; }
                else if (ch is '\'' or '"') { quote = ch; continue; }
                else if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == ';' && depth <= 0) break;
                else if (ch == '}' && depth <= 0) break;
            }
            string text = _s[start..Math.Min(_i, _s.Length)].Trim();
            if (_i < _s.Length && _s[_i] == ';') _i++;
            AddCalls(text, conds, loop);
        }

        private void AddCalls(string text, IReadOnlyList<string> conds, bool loop)
        {
            if (text.Length == 0) return;
            Match call = Regex.Match(text, @"^call\s+(\w+)\s*$", RegexOptions.IgnoreCase);
            if (call.Success) { _calls.Add(new CogCallSite("call", [call.Groups[1].Value], conds, loop)); return; }
            if (Regex.IsMatch(text, @"^return\b", RegexOptions.IgnoreCase)) { _calls.Add(new CogCallSite("return", [], conds, loop)); return; }
            foreach (Match m in Regex.Matches(text, @"\b([A-Za-z_]\w*)\s*\("))
            {
                string fn = m.Groups[1].Value;
                if (Keywords.Contains(fn)) continue;
                int open = m.Index + m.Length - 1;
                int depth = 0, close = -1;
                for (int k = open; k < text.Length; k++)
                {
                    if (text[k] == '(') depth++;
                    else if (text[k] == ')' && --depth == 0) { close = k; break; }
                }
                if (close < 0) continue;
                _calls.Add(new CogCallSite(fn, SplitArgs(text[(open + 1)..close]), conds, loop));
            }
        }
    }

    internal static string[] SplitArgs(string args)
    {
        var parts = new List<string>();
        int depth = 0, last = 0;
        char quote = '\0';
        for (int i = 0; i < args.Length; i++)
        {
            char c = args[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
            else if (c is '\'' or '"') { quote = c; continue; }
            else if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == ',' && depth == 0) { parts.Add(args[last..i].Trim()); last = i + 1; }
        }
        string tail = args[last..].Trim();
        if (tail.Length > 0 || parts.Count > 0) parts.Add(tail);
        return [.. parts];
    }
}

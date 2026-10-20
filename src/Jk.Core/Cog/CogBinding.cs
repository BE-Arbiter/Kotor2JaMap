using System.Globalization;
using Jk.Core.Jkl;

namespace Jk.Core.Cog;

/// <summary>Script COG lie aux valeurs d'une instance du niveau. <c>Values</c> : nom du symbole (insensible a la casse) -> valeur en texte (sans apostrophes).</summary>
public sealed class BoundCog
{
    public CogScript Script { get; }
    public JklCogInstance Instance { get; }
    public IReadOnlyDictionary<string, string> Values { get; }

    public string File => Instance.CogFile;

    internal BoundCog(CogScript script, JklCogInstance instance, IReadOnlyDictionary<string, string> values)
    {
        Script = script;
        Instance = instance;
        Values = values;
    }

    public string? Get(string name) => Values.GetValueOrDefault(name);

    /// <summary>Valeur entiere d'un symbole (index de thing, de surface, de secteur) ; <c>null</c> si absente ou non numerique.</summary>
    public int? Int(string name) => Get(name) is { } v && TryNumber(v, out double d) ? (int)d : null;

    /// <summary>
    /// Evalue une expression numerique simple : nombres (decimaux ou <c>0x..</c>), symboles lies, parentheses, <c>+ - * /</c>, moins unaire.
    /// <c>null</c> si l'expression contient autre chose (appel, indexation, variable non liee).
    /// </summary>
    public double? Number(string expr)
    {
        var p = new ExprParser(expr, this);
        double? v = p.Sum();
        return p.AtEnd ? v : null;
    }

    internal static bool TryNumber(string s, out double d)
    {
        s = s.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long h)) { d = h; return true; }
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
    }

    private sealed class ExprParser
    {
        private readonly string _s;
        private readonly BoundCog _cog;
        private int _i;

        public ExprParser(string s, BoundCog cog) { _s = s; _cog = cog; }

        public bool AtEnd { get { Skip(); return _i >= _s.Length; } }

        private void Skip() { while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++; }

        public double? Sum()
        {
            double? v = Product();
            while (true)
            {
                Skip();
                if (_i < _s.Length && _s[_i] is '+' or '-')
                {
                    char op = _s[_i++];
                    double? r = Product();
                    v = v is null || r is null ? null : op == '+' ? v + r : v - r;
                }
                else return v;
            }
        }

        private double? Product()
        {
            double? v = Unary();
            while (true)
            {
                Skip();
                if (_i < _s.Length && _s[_i] is '*' or '/')
                {
                    char op = _s[_i++];
                    double? r = Unary();
                    v = v is null || r is null ? null : op == '*' ? v * r : r == 0 ? null : v / r;
                }
                else return v;
            }
        }

        private double? Unary()
        {
            Skip();
            if (_i >= _s.Length) return null;
            if (_s[_i] == '-') { _i++; return -Unary(); }
            if (_s[_i] == '+') { _i++; return Unary(); }
            if (_s[_i] == '(')
            {
                _i++;
                double? v = Sum();
                Skip();
                if (_i < _s.Length && _s[_i] == ')') _i++; else return null;
                return v;
            }
            int start = _i;
            while (_i < _s.Length && (char.IsLetterOrDigit(_s[_i]) || _s[_i] is '_' or '.')) _i++;
            if (_i == start) return null;
            string tok = _s[start.._i];
            if (TryNumber(tok, out double d)) return d;
            Skip();
            if (_i < _s.Length && _s[_i] is '(' or '[') { _i = _s.Length; return null; } // appel ou indexation : inconnu
            return _cog.Get(tok) is { } val && TryNumber(val, out double dv) ? dv : null;
        }
    }
}

public static class CogBinding
{
    /// <summary>
    /// Valeurs des symboles pour une instance : les symboles ni <c>local</c> ni <c>message</c>, dans l'ordre de declaration (les <c>nolink</c> comptent),
    /// prennent les valeurs de l'instance ; un symbole <c>local</c> ou sans valeur (instance trop courte) garde sa valeur par defaut, s'il en a une.
    /// </summary>
    public static BoundCog Bind(CogScript script, JklCogInstance instance, JklLevel level) => Bind(script, instance);

    /// <summary>Comme <see cref="Bind(CogScript, JklCogInstance, JklLevel)"/> : la liaison ne depend pas du niveau.</summary>
    public static BoundCog Bind(CogScript script, JklCogInstance instance)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int k = 0;
        foreach (CogSymbol s in script.Symbols)
        {
            if (s.Type == "message") continue;
            if (!s.Local && k < instance.Values.Count) values[s.Name] = CogReader.Unquote(instance.Values[k++]);
            else
            {
                if (!s.Local) k++;
                if (s.Default is not null) values[s.Name] = s.Default;
            }
        }
        return new BoundCog(script, instance, values);
    }
}

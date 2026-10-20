using System.Text.RegularExpressions;

namespace Jk.Core.Cog;

/// <summary>
/// Symbole d'un script COG (<c>surface sign mask=0x448 linkid=2</c>). <c>Type</c> en minuscules, <c>Default</c> = valeur par defaut,
/// <c>Local</c> = pas de valeur dans l'instance, <c>NoLink</c> = pas de lien (mais il prend quand meme une valeur d'instance),
/// <c>Description</c> = <c>desc=</c>, <c>Mask</c> = texte du <c>mask=</c>, <c>LinkId</c> = <c>linkid=</c>.
/// </summary>
public sealed record CogSymbol(string Type, string Name, string? Default, bool Local, bool NoLink, string? Description, string? Mask, int? LinkId);

/// <summary>
/// Appel d'un verbe dans un message : nom (casse du script), arguments (texte, virgules de premier niveau) et conditions qui l'entourent
/// (<c>if</c> englobants ; une branche <c>else</c> donne <c>!(condition)</c>). <c>call etiquette;</c> est un appel de fonction <c>call</c> d'argument
/// l'etiquette ; <c>return;</c> est un appel <c>return</c>. <c>InLoop</c> : dans un <c>for</c>, <c>while</c> ou <c>do</c>.
/// </summary>
public sealed record CogCallSite(string Function, string[] Args, IReadOnlyList<string> Conditions, bool InLoop);

/// <summary>Une etiquette du code : un message (<c>activated</c>, <c>killed</c>...) ou une sous-routine appelee par <c>call</c>.</summary>
public sealed record CogHandler(string Name, bool IsMessage, IReadOnlyList<CogCallSite> Calls, bool EndsWithReturn);

/// <summary>Script COG lu : symboles et code decoupe en messages et sous-routines, avec les appels de verbes de chacun.</summary>
public sealed class CogScript
{
    private readonly Dictionary<string, CogHandler> _handlers = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];

    public IReadOnlyList<CogSymbol> Symbols { get; }

    /// <summary>Etiquettes dans l'ordre du texte.</summary>
    public IReadOnlyList<CogHandler> Handlers => _order.Select(n => _handlers[n]).ToList();

    internal CogScript(IReadOnlyList<CogSymbol> symbols, IEnumerable<CogHandler> handlers)
    {
        Symbols = symbols;
        foreach (CogHandler h in handlers)
            if (_handlers.TryAdd(h.Name, h)) _order.Add(h.Name);
    }

    public bool HasMessage(string name) =>
        Symbols.Any(s => s.Type == "message" && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public CogSymbol? Symbol(string name) =>
        Symbols.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public CogHandler? Handler(string name) => _handlers.GetValueOrDefault(name);

    /// <summary>Noms des messages declares qui ont du code.</summary>
    public IEnumerable<string> MessageNames => _order.Where(n => _handlers[n].IsMessage);

    /// <summary>
    /// Appels executes par un message : ceux de son code, ceux des sous-routines appelees par <c>call</c> (les conditions de l'appel s'ajoutent),
    /// et, quand le code ne se termine pas par <c>return</c>, ceux de l'etiquette suivante (le script « tombe » dedans, comme le moteur).
    /// Pas de recursion infinie : une etiquette deja en cours d'expansion n'est pas reprise.
    /// </summary>
    public IReadOnlyList<CogCallSite> Expand(string handler)
    {
        var result = new List<CogCallSite>();
        ExpandInto(handler, [], false, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result;
    }

    private void ExpandInto(string name, IReadOnlyList<string> outer, bool outerLoop, HashSet<string> active, List<CogCallSite> result)
    {
        if (!_handlers.TryGetValue(name, out CogHandler? h) || !active.Add(name)) return;
        foreach (CogCallSite c in h.Calls)
        {
            IReadOnlyList<string> conds = outer.Count == 0 ? c.Conditions : [.. outer, .. c.Conditions];
            bool loop = outerLoop || c.InLoop;
            if (c.Function.Equals("call", StringComparison.OrdinalIgnoreCase) && c.Args.Length == 1)
                ExpandInto(c.Args[0], conds, loop, active, result);
            else
                result.Add(c with { Conditions = conds, InLoop = loop });
        }
        if (!h.EndsWithReturn)
        {
            int at = _order.FindIndex(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (at >= 0 && at + 1 < _order.Count) ExpandInto(_order[at + 1], outer, outerLoop, active, result);
        }
        active.Remove(name);
    }

    /// <summary>
    /// Symboles qu'un acces <c>nom[i]</c> parcourt : <paramref name="name"/> puis les symboles suivants du meme type nommes comme lui a un
    /// numero pres (<c>door0</c>, <c>door1</c>... ; <c>sign</c>, <c>sign1</c>...).
    /// </summary>
    public IReadOnlyList<CogSymbol> Family(string name)
    {
        int start = Symbols.ToList().FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (start < 0) return [];
        CogSymbol first = Symbols[start];
        string stem = first.Name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        var family = new List<CogSymbol> { first };
        for (int i = start + 1; i < Symbols.Count; i++)
        {
            CogSymbol s = Symbols[i];
            if (s.Type != first.Type || !Regex.IsMatch(s.Name, "^" + Regex.Escape(stem) + @"\d+$", RegexOptions.IgnoreCase)) break;
            family.Add(s);
        }
        return family;
    }
}

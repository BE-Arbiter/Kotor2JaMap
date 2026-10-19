namespace Jk.Core.Jkl;

/// <summary>
/// Resolution des gabarits : un gabarit herite de son parent jusqu'a <c>none</c>, l'enfant l'emporte.
/// Un parent absent ou un cycle n'est jamais une exception : on garde ce qui est resolu et on note le nom dans <see cref="Unresolved"/>
/// (a journaliser <c>JK_TEMPLATE_UNRESOLVED</c>).
/// </summary>
public sealed class JkTemplates
{
    private readonly Dictionary<string, JklTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _unresolved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gabarits des sources donnees, dans l'ordre : une source plus tardive remplace un gabarit de meme nom (ex. <c>static.jkl</c>, puis le niveau).</summary>
    public JkTemplates(params IEnumerable<JklTemplate>[] sources)
    {
        foreach (var source in sources)
            foreach (JklTemplate t in source) _templates[t.Name] = t;
    }

    public static JkTemplates FromLevels(JklLevel? staticLevel, JklLevel level) =>
        new(staticLevel?.Templates.Values ?? Enumerable.Empty<JklTemplate>(), level.Templates.Values);

    /// <summary>Noms (tries) des gabarits manquants ou circulaires rencontres par <see cref="Resolve"/>.</summary>
    public IReadOnlyCollection<string> Unresolved => _unresolved;

    public bool Contains(string name) => _templates.ContainsKey(name);

    public IReadOnlyDictionary<string, string> Resolve(string name)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var chain = new List<JklTemplate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? current = name;
        while (!string.IsNullOrEmpty(current) && !current.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            if (!seen.Add(current)) { _unresolved.Add(current); break; } // cycle
            if (!_templates.TryGetValue(current, out JklTemplate? t)) { _unresolved.Add(current); break; } // parent absent
            chain.Add(t);
            current = t.Parent;
        }
        for (int i = chain.Count - 1; i >= 0; i--) // du plus ancien parent a l'enfant : l'enfant ecrase
            foreach (var kv in chain[i].Params) result[kv.Key] = kv.Value;
        return result;
    }
}

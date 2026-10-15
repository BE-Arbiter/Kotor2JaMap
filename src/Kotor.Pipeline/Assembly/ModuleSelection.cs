namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Liste de modules saisie en ligne de commande (<c>a,b</c>), nom de carte par defaut d'un assemblage et message des modules
/// non relies, partages par la CLI et l'automatisation de l'application.
/// </summary>
public static class ModuleSelection
{
    /// <summary>
    /// Decoupe <paramref name="text"/> sur les virgules (espaces retires, minuscules). Entree vide ou module en double : faux et
    /// <paramref name="error"/> (anglais).
    /// </summary>
    public static bool TryParse(string? text, out IReadOnlyList<string> modules, out string? error)
    {
        modules = [];
        error = null;
        var list = new List<string>();
        foreach (string raw in (text ?? "").Split(','))
        {
            string m = raw.Trim().ToLowerInvariant();
            if (m.Length == 0) { error = "The module list contains an empty entry (expected e.g. end_m01aa,end_m01ab)."; return false; }
            if (list.Contains(m)) { error = $"The module '{m}' is listed twice."; return false; }
            list.Add(m);
        }
        modules = list;
        return true;
    }

    /// <summary>Nom de carte par defaut d'un assemblage : <c>&lt;reference&gt;_&lt;nombre de modules&gt;</c> (ex. <c>end_m01aa_2</c>).</summary>
    public static string DefaultMapName(string reference, int count) => $"{reference.ToLowerInvariant()}_{count}";

    /// <summary>Message (anglais) des modules restes non relies apres la liaison automatique.</summary>
    public static string NotLinkedMessage(IEnumerable<string> modules) =>
        $"Modules not linked: {string.Join(", ", modules)}. Use the application to place them manually.";

    /// <summary>Modules des groupes non relies a la reference, dans l'ordre de la selection.</summary>
    public static IReadOnlyList<string> UnlinkedModules(AssemblySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UnlinkedGroups.SelectMany(g => g).Order().Select(i => session.Modules[i]).ToList();
    }
}

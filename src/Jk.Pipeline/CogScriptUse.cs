using Jk.Core.Cog;
using Jk.Core.Jkl;

namespace Jk.Pipeline;

/// <summary>
/// Aides de lecture d'un <see cref="CogScript"/> pour les effets de surface et les choses du niveau. Remplace l'ancien « CogLite » (lecteur a
/// regex qui retirait <c>//</c> et <c>#</c> meme dans les litteraux et ne signalait aucun script mal forme) : toute la lecture passe maintenant par
/// <see cref="CogReader"/>, comme les mobiles.
/// </summary>
internal static class CogScriptUse
{
    /// <summary>Valeurs des symboles pour une instance du niveau (voir <see cref="CogBinding"/>).</summary>
    public static IReadOnlyDictionary<string, string> Bind(this CogScript script, JklCogInstance instance) =>
        CogBinding.Bind(script, instance).Values;

    /// <summary>Appels du message <c>startup</c> avec ceux des etiquettes qu'il appelle ou dans lesquelles il tombe ; les pseudo-appels <c>call</c> et <c>return</c> sont retires.</summary>
    public static IReadOnlyList<CogCallSite> StartupCalls(this CogScript script) =>
        script.Handler("startup") is null ? [] : [.. script.Expand("startup").Where(IsVerb)];

    /// <summary>Tous les appels de verbes du script, tous messages confondus (hors <c>call</c> et <c>return</c>).</summary>
    public static IReadOnlyList<CogCallSite> AllCalls(this CogScript script) =>
        [.. script.Handlers.SelectMany(h => h.Calls).Where(IsVerb)];

    /// <summary>Vrai si le code (sans commentaires) contient <paramref name="text"/>, par exemple <c>damagething(</c> (insensible a la casse).</summary>
    public static bool CodeContains(this CogScript script, string text) => script.Code.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static bool IsVerb(CogCallSite c) =>
        !c.Function.Equals("call", StringComparison.OrdinalIgnoreCase) && !c.Function.Equals("return", StringComparison.OrdinalIgnoreCase);
}

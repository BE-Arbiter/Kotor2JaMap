using Kotor.Core;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Workflow;

public sealed record ModuleListItem(KotorGame Game, string Module, string AreaName);

public enum WorkflowStage { None, Previewed, Imported, Exported, Packed }

/// <summary>Les quatre etapes vues par l'application. L'implementation garde l'etat entre les etapes.</summary>
public interface IWorkflowSteps
{
    /// <summary>Modules of both games (a game whose folder is missing contributes none).</summary>
    IReadOnlyList<ModuleListItem> Modules();
    /// <summary>Is the folder of this game configured and present?</summary>
    bool IsInstalled(KotorGame game) => true;
    PreviewModel Preview(KotorGame game, string module, IProgress<string> log);
    /// <summary>
    /// Previsualise plusieurs modules assembles en une carte (un seul module : meme resultat que la surcharge d'un module).
    /// Modules d'un autre jeu : <c>ASSEMBLY_MIXED_GAMES</c>.
    /// </summary>
    PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> log);
    /// <summary>Session d'edition de l'assemblage (non nulle quand la selection a plus d'un module).</summary>
    AssemblySession? Assembly { get; }
    /// <summary>Reconstruit l'apercu apres l'edition de <see cref="Assembly"/> et remet les etapes suivantes a zero.</summary>
    PreviewModel ApplyAssembly(IProgress<string> log);
    /// <summary>A partir de l'etat garde par l'implementation.</summary>
    PreviewModel Import(IProgress<string> log);
    PreviewModel Export(IProgress<string> log);
    /// <returns>Chemin du pk3.</returns>
    string CreatePk3(IProgress<string> log);
}

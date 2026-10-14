using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Workflow;

public sealed record ModuleListItem(string Module, string AreaName);

public enum WorkflowStage { None, Previewed, Imported, Exported, Packed }

/// <summary>Les quatre etapes vues par l'application. L'implementation garde l'etat entre les etapes.</summary>
public interface IWorkflowSteps
{
    IReadOnlyList<ModuleListItem> Modules();
    PreviewModel Preview(string module, IProgress<string> log);
    /// <summary>A partir de l'etat garde par l'implementation.</summary>
    PreviewModel Import(IProgress<string> log);
    PreviewModel Export(IProgress<string> log);
    /// <returns>Chemin du pk3.</returns>
    string CreatePk3(IProgress<string> log);
}

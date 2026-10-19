using Kotor.Core;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;

namespace Swtor.Pipeline;

/// <summary>
/// Les etapes de l'application pour tous les jeux : KOTOR 1 / 2 (<see cref="KotorWorkflowSteps"/>) et SWTOR (<see cref="SwtorWorkflowSteps"/>).
/// L'apercu choisit l'implementation ; les etapes suivantes vont a celle de la derniere apercu reussie.
/// </summary>
public sealed class GameWorkflowSteps(KotorWorkflowSteps kotor, SwtorWorkflowSteps swtor) : IWorkflowSteps
{
    private volatile IWorkflowSteps _active = kotor;

    public KotorWorkflowSteps Kotor => kotor;
    public SwtorWorkflowSteps Swtor => swtor;

    public IReadOnlyList<ModuleListItem> Modules() => kotor.Modules();
    public bool IsInstalled(KotorGame game) => game == KotorGame.Swtor ? swtor.IsInstalled(game) : kotor.IsInstalled(game);

    private IWorkflowSteps For(KotorGame game) => game == KotorGame.Swtor ? swtor : kotor;

    public PreviewModel Preview(KotorGame game, string module, IProgress<string> log) => Chosen(game, s => s.Preview(game, module, log));
    public PreviewModel Preview(KotorGame game, IReadOnlyList<string> modules, IProgress<string> log) => Chosen(game, s => s.Preview(game, modules, log));

    private PreviewModel Chosen(KotorGame game, Func<IWorkflowSteps, PreviewModel> preview)
    {
        IWorkflowSteps steps = For(game);
        PreviewModel model = preview(steps);
        _active = steps;
        return model;
    }

    public AssemblySession? Assembly => _active.Assembly;
    public bool AssemblyUpToDate => _active.AssemblyUpToDate;
    public PreviewModel ApplyAssembly(IProgress<string> log) => _active.ApplyAssembly(log);
    public PreviewModel Import(IProgress<string> log) => _active.Import(log);
    public PreviewModel Export(IProgress<string> log) => _active.Export(log);
    public PreviewModel SetSpawn(System.Numerics.Vector3 position, float yawDegrees) => _active.SetSpawn(position, yawDegrees);
    public string CreatePk3(IProgress<string> log) => _active.CreatePk3(log);
}

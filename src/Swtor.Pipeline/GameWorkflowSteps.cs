using Jk.Pipeline;
using Kotor.Core;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;

namespace Swtor.Pipeline;

/// <summary>
/// Les etapes de l'application pour tous les jeux : KOTOR 1 / 2 (<see cref="KotorWorkflowSteps"/>), SWTOR (<see cref="SwtorWorkflowSteps"/>)
/// et Jedi Knight (<see cref="JkWorkflowSteps"/>). L'apercu choisit l'implementation ; les etapes suivantes vont a celle de la derniere apercu reussie.
/// </summary>
public sealed class GameWorkflowSteps(KotorWorkflowSteps kotor, SwtorWorkflowSteps swtor, JkWorkflowSteps jk) : IWorkflowSteps
{
    private volatile IWorkflowSteps _active = kotor;

    public KotorWorkflowSteps Kotor => kotor;
    public SwtorWorkflowSteps Swtor => swtor;
    public JkWorkflowSteps Jk => jk;

    /// <summary>Les modules de KOTOR / KOTOR 2 et les niveaux de Jedi Knight (les zones SWTOR viennent de leur catalogue).</summary>
    public IReadOnlyList<ModuleListItem> Modules() => [.. kotor.Modules(), .. jk.Modules()];

    public bool IsInstalled(KotorGame game) => game switch
    {
        KotorGame.Swtor => swtor.IsInstalled(game),
        KotorGame.JediKnight => jk.IsInstalled(game),
        _ => kotor.IsInstalled(game),
    };

    private IWorkflowSteps For(KotorGame game) => game switch
    {
        KotorGame.Swtor => swtor,
        KotorGame.JediKnight => jk,
        _ => kotor,
    };

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

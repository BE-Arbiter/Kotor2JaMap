using System.Collections.Concurrent;
using Kotor.Core;
using Kotor.Pipeline.Preview;

namespace Kotor.Pipeline.Workflow;

/// <summary>
/// Automate des quatre etapes (Previsualiser, Importer, Exporter, Creer PK3). Chaque etape tourne sur <see cref="Task.Run(Action)"/> ;
/// un compteur de generation ecarte le resultat d'une etape lancee avant un changement de module.
/// </summary>
public sealed class WorkflowController
{
    private readonly object _gate = new();
    private readonly ConcurrentQueue<string> _log = new();
    private readonly IWorkflowSteps _steps;
    private string? _module;
    private IReadOnlyList<string> _modules = [];
    private KotorGame _game;
    private WorkflowStage _stage;
    private bool _busy;
    private int _generation;
    private PreviewModel? _preview;
    private string? _lastError;

    public WorkflowController(IWorkflowSteps steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps;
    }

    public IReadOnlyList<ModuleListItem> Modules() => _steps.Modules();
    public string? SelectedModule { get { lock (_gate) return _module; } }
    public KotorGame SelectedGame { get { lock (_gate) return _game; } }
    public bool IsInstalled(KotorGame game) => _steps.IsInstalled(game);
    public WorkflowStage Stage { get { lock (_gate) return _stage; } }
    public bool IsBusy { get { lock (_gate) return _busy; } }
    public bool CanPreview { get { lock (_gate) return _module is not null && !_busy; } }
    public PreviewModel? Preview { get { lock (_gate) return _preview; } }
    public string? LastError { get { lock (_gate) return _lastError; } }

    public string? NextLabel
    {
        get
        {
            lock (_gate)
                return _stage switch
                {
                    WorkflowStage.Previewed => "Import",
                    WorkflowStage.Imported => "Export",
                    WorkflowStage.Exported => "Create PK3",
                    _ => null,
                };
        }
    }

    /// <summary>Modules selectionnes (un seul hors assemblage) ; le premier est aussi <see cref="SelectedModule"/>.</summary>
    public IReadOnlyList<string> SelectedModules { get { lock (_gate) return _modules; } }

    /// <summary>Vrai tant que l'assemblage a des groupes de modules non relies : <see cref="CanNext"/> reste faux.</summary>
    public bool NeedsLinking { get { lock (_gate) return NeedsLinkingLocked(); } }

    private bool NeedsLinkingLocked() =>
        _modules.Count > 1 && _stage != WorkflowStage.None && _steps.Assembly is { IsComplete: false };

    public bool CanNext { get { lock (_gate) return !_busy && !NeedsLinkingLocked() && _stage is WorkflowStage.Previewed or WorkflowStage.Imported or WorkflowStage.Exported; } }

    /// <summary>Refuse (false) pendant une etape ; sinon retient le jeu et le module et remet le deroule a <see cref="WorkflowStage.None"/>.</summary>
    public bool SelectModule(KotorGame game, string module) => SelectModules(game, [module]);

    /// <summary>Comme <see cref="SelectModule"/> pour plusieurs modules assembles ; un seul jeu (<c>ASSEMBLY_MIXED_GAMES</c> sinon).</summary>
    public bool SelectModules(IReadOnlyList<ModuleListItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) return false;
        if (items.Any(i => i.Game != items[0].Game))
            throw new PipelineStepException("ASSEMBLY_MIXED_GAMES", "Modules of different games cannot be assembled: select modules of a single game.");
        return SelectModules(items[0].Game, items.Select(i => i.Module).ToList());
    }

    public bool SelectModules(KotorGame game, IReadOnlyList<string> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        if (modules.Count == 0) return false;
        lock (_gate)
        {
            if (_busy) return false;
            _game = game;
            _modules = [.. modules];
            _module = modules[0];
            ResetLocked();
            return true;
        }
    }

    /// <summary>Nouvelle generation : le resultat d'une etape en cours sera ecarte.</summary>
    public void Reset()
    {
        lock (_gate) ResetLocked();
    }

    private void ResetLocked()
    {
        _generation++;
        _stage = WorkflowStage.None;
        _preview = null;
        _lastError = null;
    }

    public IReadOnlyList<string> DrainLog()
    {
        var lines = new List<string>();
        while (_log.TryDequeue(out string? l)) lines.Add(l);
        return lines;
    }

    public Task PreviewAsync()
    {
        IReadOnlyList<string> modules;
        lock (_gate) modules = _modules;
        return Run(canRun: () => _module is not null,
        run: (game, module, progress) => (WorkflowStage.Previewed,
            modules.Count > 1 ? _steps.Preview(game, modules, progress) : _steps.Preview(game, module!, progress)));
    }

    /// <summary>Reconstruit l'apercu apres l'edition de l'assemblage ; les etapes suivantes sont a refaire.</summary>
    public Task ApplyAssemblyAsync()
    {
        return Run(canRun: () => _modules.Count > 1 && _stage != WorkflowStage.None,
            run: (_, _, progress) => (WorkflowStage.Previewed, _steps.ApplyAssembly(progress)));
    }

    public Task NextAsync()
    {
        WorkflowStage stage;
        lock (_gate) stage = _stage;
        return stage switch
        {
            WorkflowStage.Previewed => Run(() => _stage == WorkflowStage.Previewed && !NeedsLinkingLocked(),
                (_, _, p) => (WorkflowStage.Imported, _steps.Import(p))),
            WorkflowStage.Imported => Run(() => _stage == WorkflowStage.Imported,
                (_, _, p) => (WorkflowStage.Exported, _steps.Export(p))),
            WorkflowStage.Exported => Run(() => _stage == WorkflowStage.Exported, (_, _, p) =>
            {
                string pk3 = _steps.CreatePk3(p);
                p.Report("PK3: " + pk3);
                return (WorkflowStage.Packed, null); // la vue ne change pas
            }),
            _ => Task.CompletedTask,
        };
    }

    private Task Run(Func<bool> canRun, Func<KotorGame, string?, IProgress<string>, (WorkflowStage Stage, PreviewModel? Model)> run)
    {
        string? module;
        KotorGame game;
        int gen;
        lock (_gate)
        {
            if (_busy || !canRun()) return Task.CompletedTask;
            _busy = true;
            gen = _generation;
            module = _module;
            game = _game;
        }

        var progress = new SyncProgress(l => { if (IsCurrent(gen)) _log.Enqueue(l); });
        return Task.Run(() =>
        {
            (WorkflowStage Stage, PreviewModel? Model)? result = null;
            string? error = null;
            try
            {
                result = run(game, module, progress);
            }
            catch (PipelineStepException e)
            {
                error = $"{e.Code}: {e.Message}";
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                error = $"Unexpected exception: {e.Message}";
            }

            lock (_gate)
            {
                try
                {
                    if (gen != _generation) return;
                    if (result is { } r)
                    {
                        if (r.Model is not null) _preview = r.Model;
                        _stage = r.Stage;
                        _lastError = null;
                    }
                    else
                    {
                        _log.Enqueue("ERROR: " + error);
                        _lastError = error;
                    }
                }
                finally
                {
                    _busy = false;
                }
            }
        });
    }

    private bool IsCurrent(int gen) { lock (_gate) return gen == _generation; }

    private sealed class SyncProgress(Action<string> sink) : IProgress<string>
    {
        public void Report(string value) => sink(value);
    }
}

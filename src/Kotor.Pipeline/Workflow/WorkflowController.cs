using System.Collections.Concurrent;
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
                    WorkflowStage.Previewed => "Importer",
                    WorkflowStage.Imported => "Exporter",
                    WorkflowStage.Exported => "Créer PK3",
                    _ => null,
                };
        }
    }

    public bool CanNext { get { lock (_gate) return !_busy && _stage is WorkflowStage.Previewed or WorkflowStage.Imported or WorkflowStage.Exported; } }

    /// <summary>Refuse (false) pendant une etape ; sinon retient le module et remet le deroule a <see cref="WorkflowStage.None"/>.</summary>
    public bool SelectModule(string module)
    {
        lock (_gate)
        {
            if (_busy) return false;
            _module = module;
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

    public Task PreviewAsync() => Run(canRun: () => _module is not null,
        run: (module, progress) => (WorkflowStage.Previewed, _steps.Preview(module!, progress)));

    public Task NextAsync()
    {
        WorkflowStage stage;
        lock (_gate) stage = _stage;
        return stage switch
        {
            WorkflowStage.Previewed => Run(() => _stage == WorkflowStage.Previewed,
                (_, p) => (WorkflowStage.Imported, _steps.Import(p))),
            WorkflowStage.Imported => Run(() => _stage == WorkflowStage.Imported,
                (_, p) => (WorkflowStage.Exported, _steps.Export(p))),
            WorkflowStage.Exported => Run(() => _stage == WorkflowStage.Exported, (_, p) =>
            {
                string pk3 = _steps.CreatePk3(p);
                _log.Enqueue("PK3 : " + pk3);
                return (WorkflowStage.Packed, null); // la vue ne change pas
            }),
            _ => Task.CompletedTask,
        };
    }

    private Task Run(Func<bool> canRun, Func<string?, IProgress<string>, (WorkflowStage Stage, PreviewModel? Model)> run)
    {
        string? module;
        int gen;
        lock (_gate)
        {
            if (_busy || !canRun()) return Task.CompletedTask;
            _busy = true;
            gen = _generation;
            module = _module;
        }

        var progress = new SyncProgress(l => { if (IsCurrent(gen)) _log.Enqueue(l); });
        return Task.Run(() =>
        {
            (WorkflowStage Stage, PreviewModel? Model)? result = null;
            string? error = null;
            try
            {
                result = run(module, progress);
            }
            catch (PipelineStepException e)
            {
                error = $"{e.Code} : {e.Message}";
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                error = $"Exception inattendue : {e.Message}";
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
                        _log.Enqueue("ERREUR : " + error);
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

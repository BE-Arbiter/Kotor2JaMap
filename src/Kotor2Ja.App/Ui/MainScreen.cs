using System.Numerics;
using ImGuiNET;
using Kotor.Core;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Workflow;
using Kotor2Ja.App.Rendering;

namespace Kotor2Ja.App.Ui;

/// <summary>
/// Interface principale : colonne de gauche (liste KOTOR, bouton Previsualiser, journal), cases a cocher en haut a droite de la vue,
/// bouton d'etape suivante en bas a droite. La vue 3D occupe tout ce qui est a droite de la colonne.
/// </summary>
public sealed class MainScreen
{
    private const float BaseLeftWidth = 330f;

    /// <summary>UI scale factor (1 at 900 px high), recomputed each frame from the back buffer size.</summary>
    public static float UiScale { get; private set; } = 1f;

    /// <summary>Left column width in pixels: scaled with the UI, never more than 45% of the window.</summary>
    public static float LeftWidth { get; private set; } = BaseLeftWidth;

    /// <summary>Recomputes <see cref="UiScale"/> and <see cref="LeftWidth"/> for a back buffer size.</summary>
    public static void Fit(int width, int height)
    {
        UiScale = Math.Clamp(MathF.Round(height / 900f * 20f) / 20f, 0.8f, 3f);
        LeftWidth = Math.Min(BaseLeftWidth * UiScale, width * 0.45f);
    }
    private const int MaxLogLines = 5000;

    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Orange = new(1f, 0.65f, 0.15f, 1f);

    private WorkflowController _workflow;
    private readonly ViewOptions _view;
    private readonly List<string> _log = [];
    private volatile ModuleListItem[]? _modules;
    private volatile string[] _labels = [];
    private bool _scrollLog;
    private readonly PlacementEditor _editor = new();
    private SwtorPanel? _swtor;

    public MainScreen(WorkflowController workflow, ViewOptions view)
    {
        _workflow = workflow;
        _view = view;
    }

    /// <summary>Editeur de placement (ouvert par « Link modules » / « Edit placement »).</summary>
    public PlacementEditor Editor => _editor;

    /// <summary>Faux tant que la branche SWTOR charge son catalogue ou les rooms d une zone.</summary>
    public bool SwtorIdle => _swtor is not { } p || p.IsIdle;

    /// <summary>Branche SWTOR de l arbre (remplacee quand les reglages sont enregistres).</summary>
    public void SetSwtor(Swtor.Pipeline.SwtorWorkflowSteps steps) => _swtor = new SwtorPanel(steps, AddLog);

    /// <summary>Liste des modules (appelable depuis un autre fil).</summary>
    public void SetModules(IReadOnlyList<ModuleListItem> modules)
    {
        ModuleListItem[] items = modules.ToArray();
        _labels = items.Select(m => $"{m.Module} - {m.AreaName}").ToArray();
        _modules = items;
    }

    /// <summary>Recopie chaque ligne du journal (mode automatise : sortie console).</summary>
    public Action<string>? LogSink { get; set; }

    public void AddLog(string line)
    {
        lock (_log) { _log.Add(line); _scrollLog = true; }
        LogSink?.Invoke(line);
    }

    public void Draw(Vector2 displaySize, PreviewRenderer renderer)
    {
        foreach (string line in _workflow.DrainLog()) AddLog(line);
        lock (_log) if (_log.Count > MaxLogLines) _log.RemoveRange(0, _log.Count - MaxLogLines);

        if (_editor.IsOpen)
        {
            _editor.Draw(displaySize, _workflow);
            return;
        }

        const ImGuiWindowFlags panel = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus;

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(LeftWidth, displaySize.Y));
        if (ImGui.Begin("##left", panel)) DrawLeft();
        ImGui.End();

        DrawViewOptions(displaySize, renderer);
        DrawNext(displaySize);
    }

    private void DrawLeft()
    {
        float height = ImGui.GetContentRegionAvail().Y;
        bool busy = _workflow.IsBusy;

        foreach (KotorGame game in new[] { KotorGame.Kotor1, KotorGame.Kotor2 })
            DrawGameNode(game, height * 0.15f, busy);
        _swtor?.Draw(_workflow, busy, height * 0.16f);
        ImGui.TextDisabled("Ctrl+click: select several modules of one game.");
        DrawAssemblySelection(busy);

        ImGui.BeginDisabled(!_workflow.CanPreview || (_workflow.SelectedGame == KotorGame.Swtor && _swtor is { NoRoomSelected: true }));
        if (ImGui.Button("Preview", new Vector2(-1, 0))) _ = _workflow.PreviewAsync();
        ImGui.EndDisabled();
        DrawLinkButton(busy);

        DrawSettings();
        ImGui.Separator();
        string? error = _workflow.LastError;
        float errorHeight = error is null ? 0f : ImGui.GetTextLineHeightWithSpacing() * 4f;
        ImGui.TextUnformatted("Log");
        if (ImGui.BeginChild("##log", new Vector2(-1, -errorHeight), ImGuiChildFlags.Borders))
        {
            lock (_log)
            {
                foreach (string line in _log)
                {
                    if (line.StartsWith("ERROR", StringComparison.Ordinal)) ImGui.TextColored(Red, line);
                    else if (line.StartsWith("Warning", StringComparison.Ordinal) || line.StartsWith("WARN", StringComparison.Ordinal))
                        ImGui.TextColored(Orange, line);
                    else ImGui.TextUnformatted(line);
                }
                if (_scrollLog) { ImGui.SetScrollHereY(1f); _scrollLog = false; }
            }
        }
        ImGui.EndChild();
        if (error is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped(error);
            ImGui.PopStyleColor();
        }
    }

    // ---- Settings panel -------------------------------------------------------------------------------------------------------------

    private ExportOptions _settings = new("", "", "");
    private string _kotorText = "", _kotor2Text = "", _jaText = "", _workText = "";
    private string? _settingsMessage;
    private bool _settingsMessageIsError;
    // Disk and registry access (path checks, Detect) runs on background tasks; the render thread only reads their results.
    private PathStatus _status = PathStatus.Unknown;
    private (string, string, string, string)? _checkedFor;       // texts the current _status was computed for
    private (string, string, string, string) _lastSeen;
    private long _editAt;
    private readonly BackgroundJob<(PathStatus Status, (string, string, string, string) Texts)> _checkJob = new();
    private readonly BackgroundJob<ExportOptions> _detectJob = new();

    /// <summary>Called by Save with the edited options; returns an error message, or null when saved.</summary>
    public Func<ExportOptions, string?>? SaveSettings { get; set; }

    /// <summary>Called by Detect: the folders found through Steam.</summary>
    /// <summary>Set by the Save callback: where the file was written (shown instead of "Saved.").</summary>
    public string? SavedInfo { get; set; }

    public Func<GamePaths>? DetectSettings { get; set; }

    /// <summary>Replaces the workflow (after Settings were saved).</summary>
    public void SetWorkflow(WorkflowController workflow) => _workflow = workflow;

    /// <summary>Fills the Settings fields from the options in use.</summary>
    public void SetSettings(ExportOptions o)
    {
        _settings = o;
        _kotorText = o.KotorDir ?? "";
        _kotor2Text = o.Kotor2Dir ?? "";
        _jaText = o.JaBaseDir ?? "";
        _workText = o.WorkDir ?? "";
        _checkedFor = null;
    }

    private void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Settings")) return;
        long now = Environment.TickCount64;
        if (_checkJob.TryTake(out var checkResult)) { _status = checkResult.Status; _checkedFor = checkResult.Texts; }
        if (_detectJob.TryTake(out ExportOptions? detected) && detected is not null)
        {
            _kotorText = detected.KotorDir ?? ""; _kotor2Text = detected.Kotor2Dir ?? ""; _jaText = detected.JaBaseDir ?? ""; _workText = detected.WorkDir ?? "";
            _settingsMessage = "Folders detected through Steam (valid fields were kept). Click Save to keep them.";
            _settingsMessageIsError = false;
        }
        var texts = (_kotorText, _kotor2Text, _jaText, _workText);
        if (texts != _lastSeen) { _lastSeen = texts; _editAt = now; }
        if (_checkedFor != texts && now - _editAt > 300 && !_checkJob.IsRunning)
            _checkJob.TryStart(() => (SettingsProbe.Check(texts.Item1, texts.Item2, texts.Item3, texts.Item4), texts));
        PathField("KOTOR", ref _kotorText, _status.Kotor);
        PathField("KOTOR 2", ref _kotor2Text, _status.Kotor2);
        PathField("JA base", ref _jaText, _status.Ja);
        PathField("Work dir", ref _workText, _status.Work);
        bool busy = _workflow.IsBusy;
        ImGui.BeginDisabled(_detectJob.IsRunning);
        if (ImGui.Button("Detect") && DetectSettings is { } detect)
        {
            ExportOptions snapshot = _settings with { KotorDir = _kotorText.Trim(), Kotor2Dir = _kotor2Text.Trim(), JaBaseDir = _jaText.Trim(), WorkDir = _workText.Trim() };
            _settingsMessage = "Detecting...";
            _settingsMessageIsError = false;
            _detectJob.TryStart(() => SettingsProbe.ApplyDetected(snapshot, detect(),
                SettingsProbe.Check(snapshot.KotorDir, snapshot.Kotor2Dir ?? "", snapshot.JaBaseDir, snapshot.WorkDir)));
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Save") && SaveSettings is { } save)
        {
            string? error = save(_settings with
            {
                KotorDir = _kotorText.Trim(), Kotor2Dir = _kotor2Text.Trim(), JaBaseDir = _jaText.Trim(), WorkDir = _workText.Trim(),
            });
            _settingsMessageIsError = error is not null;
            _settingsMessage = error ?? SavedInfo ?? "Saved.";
            if (error is null)
                _settings = _settings with { KotorDir = _kotorText.Trim(), Kotor2Dir = _kotor2Text.Trim(), JaBaseDir = _jaText.Trim(), WorkDir = _workText.Trim() };
        }
        ImGui.EndDisabled();
        if (_settingsMessage is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _settingsMessageIsError ? Red : new Vector4(0.6f, 0.9f, 0.6f, 1f));
            ImGui.TextWrapped(_settingsMessage);
            ImGui.PopStyleColor();
        }
    }

    private static void PathField(string label, ref string text, bool ok)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1);
        if (!ok) ImGui.PushStyleColor(ImGuiCol.Text, Red);
        ImGui.InputText("##set" + label, ref text, 520);
        if (!ok) ImGui.PopStyleColor();
        if (!ok && ImGui.IsItemHovered()) ImGui.SetTooltip("Folder not found");
    }

    /// <summary>One tree node per game; a game whose folder is missing is greyed out and labelled "(not found)".</summary>
    private void DrawGameNode(KotorGame game, float height, bool busy)
    {
        bool installed = _workflow.IsInstalled(game);
        string label = installed ? KotorGames.DisplayName(game) : KotorGames.DisplayName(game) + " (not found)";
        ImGui.BeginDisabled(!installed);
        if (ImGui.TreeNodeEx(label + "###game" + (int)game, installed ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
        {
            if (ImGui.BeginChild("##modules" + (int)game, new Vector2(-1, height), ImGuiChildFlags.Borders))
            {
                ModuleListItem[]? modules = _modules;
                string[] labels = _labels;
                if (modules is null) ImGui.TextDisabled("Loading modules...");
                else
                {
                    IReadOnlyList<string> selection = _workflow.SelectedGame == game ? _workflow.SelectedModules : [];
                    for (int i = 0; i < modules.Length; i++)
                    {
                        if (modules[i].Game != game) continue;
                        string module = modules[i].Module;
                        bool isSelected = selection.Contains(module, StringComparer.OrdinalIgnoreCase);
                        if (ImGui.Selectable(labels[i] + "###" + game + module, isSelected) && !busy)
                        {
                            // Ctrl+clic : ajoute ou retire le module de la selection du meme jeu ; sinon (ou dans l'autre jeu) : selection simple.
                            if (ImGui.GetIO().KeyCtrl && selection.Count > 0)
                            {
                                List<string> next = isSelected
                                    ? selection.Where(m => !string.Equals(m, module, StringComparison.OrdinalIgnoreCase)).ToList()
                                    : [.. selection, module];
                                if (next.Count > 0) _workflow.SelectModules(game, next);
                            }
                            else _workflow.SelectModule(game, module);
                        }
                    }
                }
            }
            ImGui.EndChild();
            ImGui.TreePop();
        }
        ImGui.EndDisabled();
    }


    /// <summary>Plusieurs modules selectionnes : liste et choix du module de reference.</summary>
    private void DrawAssemblySelection(bool busy)
    {
        IReadOnlyList<string> selection = _workflow.SelectedModules;
        if (selection.Count < 2) return;
        AssemblySession? session = _workflow.Assembly;
        int reference = session?.ReferenceIndex ?? 0;
        ImGui.TextUnformatted($"{selection.Count} modules selected (one map)");
        ImGui.TextUnformatted("Reference module");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-1);
        ImGui.BeginDisabled(busy);
        if (ImGui.BeginCombo("##assemblyref", selection[Math.Clamp(reference, 0, selection.Count - 1)]))
        {
            for (int i = 0; i < selection.Count; i++)
            {
                if (!ImGui.Selectable(selection[i] + "###aref" + i, i == reference) || i == reference) continue;
                if (session is not null) session.ReferenceIndex = i;   // apres l'apercu : refait le placement, a appliquer
                else _workflow.SelectModules(_workflow.SelectedGame, [selection[i], .. selection.Where((_, k) => k != i)]);
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Sky, ambient light and spawn point come from the reference module.");
    }

    /// <summary>Assemblage previsualise : « Link modules » quand il faut lier ou appliquer, « Edit placement » sinon.</summary>
    private void DrawLinkButton(bool busy)
    {
        if (_workflow.Assembly is null) return;
        bool needs = _workflow.NeedsLinking;
        if (needs)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Orange);
            ImGui.TextWrapped("The modules are not all linked, or the placement was changed: link them and click Apply.");
            ImGui.PopStyleColor();
        }
        ImGui.BeginDisabled(busy);
        if (ImGui.Button((needs ? "Link modules" : "Edit placement") + "###link", new Vector2(-1, 0))) OpenEditor();
        ImGui.EndDisabled();
    }

    private void OpenEditor()
    {
        if (_workflow.IsBusy || _workflow.Assembly is not { } session) return;
        _editor.Open(new PlacementEditorModel(session));
    }

    private void DrawViewOptions(Vector2 displaySize, PreviewRenderer renderer)
    {
        ImGui.SetNextWindowPos(new Vector2(displaySize.X - 10 * UiScale, 10 * UiScale), ImGuiCond.Always, new Vector2(1, 0));
        ImGui.SetNextWindowBgAlpha(0.35f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (ImGui.Begin("##viewopts", flags))
        {
            ImGui.Checkbox("Collisions", ref _view.Collisions);
            if (renderer.HasSky) ImGui.Checkbox("Sky", ref _view.Sky);
            ImGui.Checkbox("Double-sided", ref _view.BothSides);
            if (renderer.KindsPresent.Count > 0)
            {
                ImGui.Separator();
                foreach (EntityKind kind in renderer.KindsPresent.OrderBy(k => k))
                {
                    bool shown = !_view.HiddenKinds.Contains(kind);
                    if (ImGui.Checkbox(kind.ToString(), ref shown))
                    {
                        if (shown) _view.HiddenKinds.Remove(kind); else _view.HiddenKinds.Add(kind);
                    }
                }
            }
        }
        ImGui.End();
    }

    private void DrawNext(Vector2 displaySize)
    {
        string? label = _workflow.NextLabel;
        if (label is null) return;
        ImGui.SetNextWindowPos(new Vector2(displaySize.X - 16 * UiScale, displaySize.Y - 16 * UiScale), ImGuiCond.Always, new Vector2(1, 1));
        ImGui.SetNextWindowBgAlpha(0f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (ImGui.Begin("##next", flags))
        {
            bool busy = _workflow.IsBusy;
            if (_workflow.NeedsLinking)
            {
                ImGui.BeginDisabled(busy);
                if (ImGui.Button("Link modules###nextlink", new Vector2(180, 40) * UiScale)) OpenEditor();
                ImGui.EndDisabled();
                ImGui.SameLine();
            }
            ImGui.BeginDisabled(!_workflow.CanNext);
            if (ImGui.Button((busy ? "..." : label) + "###next", new Vector2(180, 40) * UiScale)) _ = _workflow.NextAsync();
            ImGui.EndDisabled();
        }
        ImGui.End();
    }
}

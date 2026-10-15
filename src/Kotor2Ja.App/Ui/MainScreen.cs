using System.Numerics;
using ImGuiNET;
using Kotor.Core;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Pipeline.Workflow;
using Kotor2Ja.App.Rendering;

namespace Kotor2Ja.App.Ui;

/// <summary>
/// Interface principale : colonne de gauche (liste KOTOR, bouton Previsualiser, journal), cases a cocher en haut a droite de la vue,
/// bouton d'etape suivante en bas a droite. La vue 3D occupe tout ce qui est a droite de la colonne.
/// </summary>
public sealed class MainScreen
{
    public const float LeftWidth = 330f;
    private const int MaxLogLines = 5000;

    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Orange = new(1f, 0.65f, 0.15f, 1f);

    private WorkflowController _workflow;
    private readonly ViewOptions _view;
    private readonly List<string> _log = [];
    private volatile ModuleListItem[]? _modules;
    private volatile string[] _labels = [];
    private bool _scrollLog;

    public MainScreen(WorkflowController workflow, ViewOptions view)
    {
        _workflow = workflow;
        _view = view;
    }

    /// <summary>Liste des modules (appelable depuis un autre fil).</summary>
    public void SetModules(IReadOnlyList<ModuleListItem> modules)
    {
        ModuleListItem[] items = modules.ToArray();
        _labels = items.Select(m => $"{m.Module} — {m.AreaName}").ToArray();
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
            DrawGameNode(game, height * 0.25f, busy);

        ImGui.BeginDisabled(!_workflow.CanPreview);
        if (ImGui.Button("Preview", new Vector2(-1, 0))) _ = _workflow.PreviewAsync();
        ImGui.EndDisabled();

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
    private long _checkedAt;
    private bool _kotorOk = true, _kotor2Ok = true, _jaOk = true, _workOk = true;

    /// <summary>Called by Save with the edited options; returns an error message, or null when saved.</summary>
    public Func<ExportOptions, string?>? SaveSettings { get; set; }

    /// <summary>Called by Detect: the folders found through Steam.</summary>
    public Func<ExportOptions>? DetectSettings { get; set; }

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
        _checkedAt = 0;
    }

    private void DrawSettings()
    {
        if (!ImGui.CollapsingHeader("Settings")) return;
        long now = Environment.TickCount64;
        if (now - _checkedAt > 500)
        {
            _checkedAt = now;
            _kotorOk = FolderOk(_kotorText, needChitin: true);
            _kotor2Ok = _kotor2Text.Length == 0 || FolderOk(_kotor2Text, needChitin: true);
            _jaOk = FolderOk(_jaText, needChitin: false);
            _workOk = _workText.Length > 0 && !File.Exists(_workText);   // created on demand
        }
        PathField("KOTOR", ref _kotorText, _kotorOk);
        PathField("KOTOR 2", ref _kotor2Text, _kotor2Ok);
        PathField("JA base", ref _jaText, _jaOk);
        PathField("Work dir", ref _workText, _workOk);
        bool busy = _workflow.IsBusy;
        if (ImGui.Button("Detect") && DetectSettings is { } detect)
        {
            ExportOptions d = detect();
            _kotorText = d.KotorDir ?? ""; _kotor2Text = d.Kotor2Dir ?? ""; _jaText = d.JaBaseDir ?? ""; _workText = d.WorkDir ?? "";
            _checkedAt = 0;
            _settingsMessage = "Folders detected through Steam. Click Save to keep them.";
            _settingsMessageIsError = false;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Save") && SaveSettings is { } save)
        {
            string? error = save(_settings with
            {
                KotorDir = _kotorText.Trim(), Kotor2Dir = _kotor2Text.Trim(), JaBaseDir = _jaText.Trim(), WorkDir = _workText.Trim(),
            });
            _settingsMessageIsError = error is not null;
            _settingsMessage = error ?? "Saved.";
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

    private static bool FolderOk(string path, bool needChitin)
    {
        try
        {
            string p = path.Trim();
            if (p.Length == 0 || !Directory.Exists(p)) return false;
            return !needChitin || File.Exists(Path.Combine(p, "chitin.key"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
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
                    string? selected = _workflow.SelectedModule;
                    bool selectedGame = _workflow.SelectedGame == game;
                    for (int i = 0; i < modules.Length; i++)
                    {
                        if (modules[i].Game != game) continue;
                        if (ImGui.Selectable(labels[i] + "###" + game + modules[i].Module, selectedGame && modules[i].Module == selected) && !busy)
                            _workflow.SelectModule(game, modules[i].Module);
                    }
                }
            }
            ImGui.EndChild();
            ImGui.TreePop();
        }
        ImGui.EndDisabled();
    }


    private void DrawViewOptions(Vector2 displaySize, PreviewRenderer renderer)
    {
        ImGui.SetNextWindowPos(new Vector2(displaySize.X - 10, 10), ImGuiCond.Always, new Vector2(1, 0));
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
        ImGui.SetNextWindowPos(new Vector2(displaySize.X - 16, displaySize.Y - 16), ImGuiCond.Always, new Vector2(1, 1));
        ImGui.SetNextWindowBgAlpha(0f);
        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBringToFrontOnFocus;
        if (ImGui.Begin("##next", flags))
        {
            bool busy = _workflow.IsBusy;
            ImGui.BeginDisabled(!_workflow.CanNext);
            if (ImGui.Button((busy ? "..." : label) + "###next", new Vector2(180, 40))) _ = _workflow.NextAsync();
            ImGui.EndDisabled();
        }
        ImGui.End();
    }
}

using System.Numerics;
using ImGuiNET;
using Kotor.Core;
using Kotor.Core.Entities;
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

    private readonly WorkflowController _workflow;
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
        if (ImGui.Button("Prévisualiser", new Vector2(-1, 0))) _ = _workflow.PreviewAsync();
        ImGui.EndDisabled();

        ImGui.Separator();
        string? error = _workflow.LastError;
        float errorHeight = error is null ? 0f : ImGui.GetTextLineHeightWithSpacing() * 4f;
        if (ImGui.BeginChild("##log", new Vector2(-1, -errorHeight), ImGuiChildFlags.Borders))
        {
            lock (_log)
            {
                foreach (string line in _log)
                {
                    if (line.StartsWith("ERREUR", StringComparison.Ordinal)) ImGui.TextColored(Red, line);
                    else if (line.StartsWith("Avertissement", StringComparison.Ordinal) || line.StartsWith("WARN", StringComparison.Ordinal))
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
                if (modules is null) ImGui.TextDisabled("Chargement des modules...");
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
            if (renderer.HasSky) ImGui.Checkbox("Ciel", ref _view.Sky);
            ImGui.Checkbox("Double face", ref _view.BothSides);
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
            if (ImGui.Button((busy ? "…" : label) + "###next", new Vector2(180, 40))) _ = _workflow.NextAsync();
            ImGui.EndDisabled();
        }
        ImGui.End();
    }
}

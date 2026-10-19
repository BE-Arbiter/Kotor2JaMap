using System.Numerics;
using ImGuiNET;
using Kotor.Core;
using Kotor.Core.Export;
using Kotor.Pipeline.Workflow;
using Swtor.Core.Areas;
using Swtor.Pipeline;

namespace Kotor2Ja.App.Ui;

/// <summary>
/// Branche « SWTOR » de l'arbre de gauche : zones du catalogue groupees par categorie, filtre, puis rooms de la zone choisie
/// (cases a cocher, triangles par room, total et jauge du budget BSP) et nom de carte. Catalogue et analyse tournent hors du fil de rendu.
/// L'etat vient du <see cref="WorkflowController"/> (module = <c>id[:rooms]</c>) : l'automatisation <c>--game swtor --module id:rooms</c> remplit aussi la liste.
/// </summary>
public sealed class SwtorPanel
{
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Orange = new(1f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 Green = new(0.6f, 0.9f, 0.6f, 1f);

    private sealed record AnalysisResult(string Area, SwtorAreaAnalysis? Analysis, string? Error);
    private sealed record CatalogResult(SwtorAreaCatalog? Catalog, string? Error);

    private readonly SwtorWorkflowSteps _steps;
    private readonly Action<string> _log;
    private volatile CatalogResult? _catalog;
    private volatile AnalysisResult? _analysisDone;
    private int _analysisSeq;
    private string? _loadingArea;            // zone dont l'analyse est en cours
    private string _selectedArea = "";       // identifiant ou nom interne de la zone affichee
    private IReadOnlyList<string> _pendingRooms = [];   // rooms demandees par le module du flux de travail (automatisation)
    private SwtorRoomSelection? _selection;
    private string? _analysisError;
    private string _filter = "";
    private string _mapName = "";
    private bool _nameEdited;
    private bool _forceOpen;
    private volatile string _status = "";   // derniere etape de preparation signalee (extraction depuis le jeu)
    private double _analysisFraction = -1d;  // avancement de l'analyse de zone (negatif = indetermine)
    private volatile string _analysisLabel = "";

    public SwtorPanel(SwtorWorkflowSteps steps, Action<string> log)
    {
        _steps = steps;
        _log = log;
        steps.Status = text => { _status = text; log("SWTOR: " + text); };
        if (steps.IsInstalled(KotorGame.Swtor)) StartCatalog();
    }

    /// <summary>Au-dela de ce nombre de rooms, la zone est apercue en entier puis les rooms se choisissent sur la carte (en dessous : tout est coche, on decoche).</summary>
    public const int MapSelectThreshold = 32;

    /// <summary>Grande zone : Preview (toute la zone), choix des rooms sur la carte, puis Import. Les rooms demandees par le module (automatisation) gardent le flux classique.</summary>
    public bool MapSelectMode => _selection is { } s && s.Rooms.Count > MapSelectThreshold && _pendingRooms.Count == 0;

    /// <summary>Vrai quand la zone affichee n a aucune room cochee : pas d apercu possible (en mode carte, l apercu prend toute la zone).</summary>
    public bool NoRoomSelected => !MapSelectMode && _selection is { SelectedCount: 0 };

    /// <summary>Le clic sur une room de la vue change la selection : mode carte, apercu fait, rien en cours.</summary>
    public bool CanPickRooms(WorkflowController workflow) =>
        MapSelectMode && workflow.SelectedGame == KotorGame.Swtor && workflow.Stage == WorkflowStage.Previewed && !workflow.IsBusy;

    /// <summary>Import impossible : mode carte, apercu fait, aucune room choisie.</summary>
    public bool ImportBlocked(WorkflowController workflow) =>
        MapSelectMode && workflow.Stage == WorkflowStage.Previewed && _selection is { SelectedCount: 0 };

    /// <summary>Rooms choisies, a surligner dans la vue ; null hors mode carte ou hors etape Apercu.</summary>
    public HashSet<string>? HighlightedRooms(WorkflowController workflow) =>
        MapSelectMode && workflow.SelectedGame == KotorGame.Swtor && workflow.Stage == WorkflowStage.Previewed
            ? new HashSet<string>(_selection!.SelectedRooms, StringComparer.OrdinalIgnoreCase)
            : null;

    /// <summary>Room dont le bouton « Go to » vient d'etre presse (la vue y amene la camera) ; lue une fois puis effacee.</summary>
    public string? GoToRoom { get; set; }

    /// <summary>Coche ou decoche la room cliquee dans la vue ; faux si elle n'est pas dans la liste de la zone.</summary>
    public bool ToggleRoom(string room)
    {
        if (_selection is not { } sel || !sel.Rooms.Any(r => r.Name.Equals(room, StringComparison.OrdinalIgnoreCase))) return false;
        string name = sel.Rooms.First(r => r.Name.Equals(room, StringComparison.OrdinalIgnoreCase)).Name;
        sel.Set(name, !sel.IsChecked(name));
        if (!_nameEdited) _mapName = sel.DefaultMapName;
        _log($"SWTOR: room {name} {(sel.IsChecked(name) ? "selected" : "deselected")} ({sel.SelectedCount}/{sel.Rooms.Count}).");
        return true;
    }

    /// <summary>Catalogue lu et analyse de zone terminee et affichee (mode automatise : attendre avant la capture).</summary>
    public bool IsIdle => _catalog is not null && _loadingArea is null && _analysisDone is null;

    private void StartCatalog()
    {
        Task.Run(() =>
        {
            try
            {
                SwtorAreaCatalog c = _steps.LoadCatalog();
                _catalog = new CatalogResult(c, null);
                _log($"SWTOR: {c.Entries.Count} area(s) ({(c.FromCache ? "catalogue cache" : "catalogue rebuilt")}).");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _catalog = new CatalogResult(null, e.Message);
                _log("ERROR: cannot list the SWTOR areas: " + e.Message);
            }
        });
    }

    private void StartAnalysis(string area)
    {
        _selectedArea = area;
        _selection = null;
        _analysisError = null;
        _loadingArea = area;
        int seq = Interlocked.Increment(ref _analysisSeq);
        _analysisFraction = -1d;
        _analysisLabel = "Preparing the area";
        _log($"SWTOR: analysing area {area} (counting triangles per room; large maps take a while)...");
        Task.Run(() =>
        {
            AnalysisResult result;
            try
            {
                result = new AnalysisResult(area, _steps.Analyze(area, (fraction, label) =>
                {
                    if (seq != Volatile.Read(ref _analysisSeq)) return;
                    Volatile.Write(ref _analysisFraction, fraction);
                    _analysisLabel = label;
                }), null);
            }
            catch (Exception e) when (e is not OutOfMemoryException) { result = new AnalysisResult(area, null, e.Message); }
            if (seq == Volatile.Read(ref _analysisSeq)) _analysisDone = result;
        });
    }

    /// <summary>Dessine le noeud « SWTOR » (rien si le dossier des ressources est absent).</summary>
    public void Draw(WorkflowController workflow, bool busy, float height)
    {
        if (!workflow.IsInstalled(KotorGame.Swtor)) return;
        Sync(workflow);
        if (_forceOpen) { ImGui.SetNextItemOpen(true, ImGuiCond.Always); _forceOpen = false; }
        if (!ImGui.TreeNodeEx("SWTOR###gameSwtor", ImGuiTreeNodeFlags.None)) return;

        ImGui.TextDisabled(_steps.SourceDescription);
        CatalogResult? catalog = _catalog;
        if (catalog is null) ProgressDisplay.Draw(-1d, _status.Length > 0 ? _status : "Loading areas...");
        else if (catalog.Catalog is null) { ImGui.PushStyleColor(ImGuiCol.Text, Red); ImGui.TextWrapped(catalog.Error ?? "Catalogue unavailable."); ImGui.PopStyleColor(); }
        else
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##swtorfilter", "Filter: name, internal name, id, category", ref _filter, 80);
            if (ImGui.BeginChild("##swtorareas", new Vector2(-1, height), ImGuiChildFlags.Borders)) DrawAreas(catalog.Catalog, workflow, busy);
            ImGui.EndChild();
            DrawRooms(workflow, busy, height);
        }
        ImGui.TreePop();
    }

    // Le module du flux de travail (id[:rooms]) fait foi : une selection venue de l'automatisation ouvre la branche et charge les rooms.
    private void Sync(WorkflowController workflow)
    {
        if (workflow.SelectedGame != KotorGame.Swtor || workflow.SelectedModule is not { } module) return;
        var spec = SwtorAreaSpec.Parse(module);
        if (string.Equals(spec.Area, _selectedArea, StringComparison.OrdinalIgnoreCase)) return;
        _pendingRooms = spec.Rooms;
        _nameEdited = false;
        _forceOpen = true;
        StartAnalysis(spec.Area);
    }

    private void DrawAreas(SwtorAreaCatalog catalog, WorkflowController workflow, bool busy)
    {
        bool filtering = !string.IsNullOrWhiteSpace(_filter);
        foreach (var group in catalog.Filter(_filter).GroupBy(e => e.Category).OrderBy(g => CategoryOrder(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            if (filtering) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
            if (!ImGui.TreeNodeEx($"{CategoryLabel(group.Key)} ({group.Count()})###swcat{group.Key}")) continue;
            foreach (SwtorAreaEntry e in group.OrderBy(x => x.Planet, StringComparer.Ordinal).ThenBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                bool selected = string.Equals(e.Id, _selectedArea, StringComparison.OrdinalIgnoreCase)
                                || (e.InternalName.Length > 0 && string.Equals(e.InternalName, _selectedArea, StringComparison.OrdinalIgnoreCase));
                string label = e.InternalName.Length > 0 ? $"{e.DisplayName} - {e.InternalName}" : $"{e.DisplayName} - {e.Id}";
                if (ImGui.Selectable(label + "###swa" + e.Id, selected) && !busy && workflow.SelectModules(KotorGame.Swtor, [e.Id]))
                {
                    _pendingRooms = [];
                    _nameEdited = false;
                    StartAnalysis(e.Id);
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{e.Id}\n{e.RoomCount} room(s), {e.ModelAssetCount} model(s)" + (e.Planet.Length > 0 ? "\nPlanet: " + e.Planet : ""));
            }
            ImGui.TreePop();
        }
    }

    private void DrawRooms(WorkflowController workflow, bool busy, float height)
    {
        if (_analysisDone is { } done && string.Equals(done.Area, _loadingArea, StringComparison.Ordinal))
        {
            _analysisDone = null;
            _loadingArea = null;
            if (done.Analysis is { } a)
            {
                _selection = a.NewSelection();
                if (_pendingRooms.Count > 0)
                {
                    _selection.SelectAll(false);
                    foreach (string r in _pendingRooms) _selection.Set(r, true);
                }
                else if (_selection.Rooms.Count > MapSelectThreshold) _selection.SelectAll(false);   // grande zone : on choisit sur la carte
                _selectedArea = a.AreaId;
                _mapName = _selection.DefaultMapName;
                _steps.MapNameOverride = null;
            }
            else
            {
                _analysisError = done.Error;
                _log("ERROR: " + done.Error);
            }
        }

        if (_loadingArea is not null)
        {
            ProgressDisplay.Draw(Volatile.Read(ref _analysisFraction), _analysisLabel.Length > 0 ? _analysisLabel : "Counting triangles per room");
            return;
        }
        if (_analysisError is not null) { ImGui.PushStyleColor(ImGuiCol.Text, Red); ImGui.TextWrapped(_analysisError); ImGui.PopStyleColor(); return; }
        if (_selection is not { } sel) { ImGui.TextDisabled("Select an area."); return; }

        bool changed = false;
        bool mapMode = MapSelectMode;
        // Mode carte : la liste suit la vue, modifiable avant l'apercu et pendant l'etape Preview ; ensuite elle est figee (refaire Preview pour changer).
        bool editable = !busy && (!mapMode || workflow.Stage is WorkflowStage.None or WorkflowStage.Previewed);
        _steps.ImportRooms = mapMode ? sel.SelectedRooms : null;
        ImGui.TextUnformatted($"Rooms ({sel.SelectedCount}/{sel.Rooms.Count})");
        if (mapMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Orange);
            ImGui.TextWrapped($"More than {MapSelectThreshold} rooms: Preview shows the whole area, then click rooms in the view (green = kept) or tick them here, then Import.");
            ImGui.PopStyleColor();
        }
        ImGui.BeginDisabled(!editable);
        if (ImGui.SmallButton("Default")) { sel.SelectDefault(); changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("All")) { sel.SelectAll(true); changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("None")) { sel.SelectAll(false); changed = true; }
        ImGui.SameLine();
        if (ImGui.SmallButton("Export JSON")) ExportSelection(sel);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Saves the selected rooms and map name to " + SwtorSelectionFile.DefaultPath(_steps.WorkDir, sel.AreaId));
        ImGui.SameLine();
        if (ImGui.SmallButton("Import JSON") && ImportSelection(sel)) changed = true;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Loads the rooms from " + SwtorSelectionFile.DefaultPath(_steps.WorkDir, sel.AreaId));
        ImGui.EndDisabled();
        if (ImGui.BeginChild("##swtorrooms", new Vector2(-1, height), ImGuiChildFlags.Borders))
        {
            foreach (SwtorRoomInfo room in sel.Rooms)
            {
                bool on = sel.IsChecked(room.Name);
                string label = $"{room.Triangles:N0}  {room.Name}{(room.IsSpace ? "  [space]" : "")}###swr{room.Name}";
                ImGui.BeginDisabled(!editable);
                if (ImGui.Checkbox(label, ref on)) { sel.Set(room.Name, on); changed = true; }
                ImGui.EndDisabled();
                // Bouton colle a droite de la ligne (« float: right »).
                float goWidth = ImGui.CalcTextSize("Go to").X + ImGui.GetStyle().FramePadding.X * 2f;
                ImGui.SameLine();
                ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - goWidth));
                ImGui.BeginDisabled(workflow.Preview is null);
                if (ImGui.SmallButton($"Go to##go{room.Name}")) GoToRoom = room.Name;
                ImGui.EndDisabled();
            }
        }
        ImGui.EndChild();

        SwtorBudgetLevel level = sel.Level;
        Vector4 color = level switch { SwtorBudgetLevel.Exceeded => Red, SwtorBudgetLevel.Near => Orange, _ => Green };
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped($"Total: {sel.TotalTriangles:N0} / {SwtorBudget.MaxRenderTriangles:N0} render triangles (~{sel.EstimatedDrawIndexes:N0} / {SwtorBudget.MaxDrawIndexes:N0} draw indexes)");
        if (sel.BudgetMessage is { } msg) ImGui.TextWrapped((level == SwtorBudgetLevel.Exceeded ? "WARNING: " : "") + msg);
        ImGui.PopStyleColor();
        if (sel.SelectedCount == 0 && (!mapMode || workflow.Stage == WorkflowStage.Previewed))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            ImGui.TextWrapped(mapMode ? "Select at least one room on the map." : "Check at least one room.");
            ImGui.PopStyleColor();
        }

        if (changed && !_nameEdited) _mapName = sel.DefaultMapName;
        if (changed && !busy && !mapMode)
        {
            if (sel.ModuleSpec is { } spec) workflow.SelectModules(KotorGame.Swtor, [spec]);
            else workflow.Reset();   // plus de preview ni d'etape suivante tant qu'aucune room n'est cochee
        }

        ImGui.TextUnformatted("Map name");
        ImGui.SetNextItemWidth(-1);
        ImGui.BeginDisabled(busy);
        if (ImGui.InputText("##swtormap", ref _mapName, (uint)MapNames.MaxLength)) _nameEdited = true;
        ImGui.EndDisabled();
        string? bad = _nameEdited && !string.IsNullOrWhiteSpace(_mapName) ? MapNames.Validate(MapNames.Normalize(_mapName)) : null;
        if (bad is not null) { ImGui.PushStyleColor(ImGuiCol.Text, Red); ImGui.TextWrapped(bad); ImGui.PopStyleColor(); }
        _steps.MapNameOverride = _nameEdited && !string.IsNullOrWhiteSpace(_mapName) ? _mapName : null;
    }

    private void ExportSelection(SwtorRoomSelection sel)
    {
        string path = SwtorSelectionFile.DefaultPath(_steps.WorkDir, sel.AreaId);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sel.ToFile(_nameEdited ? _mapName : null).ToJson());
            _log($"SWTOR: {sel.SelectedCount} room(s) saved to {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _log("ERROR: cannot save the room selection: " + e.Message); }
    }

    private bool ImportSelection(SwtorRoomSelection sel)
    {
        string path = SwtorSelectionFile.DefaultPath(_steps.WorkDir, sel.AreaId);
        try
        {
            if (!File.Exists(path)) { _log("ERROR: no saved room selection: " + path); return false; }
            SwtorSelectionFile? file = SwtorSelectionFile.FromJson(File.ReadAllText(path));
            if (file is null) { _log("ERROR: empty room selection file: " + path); return false; }
            IReadOnlyList<string> unknown = sel.Apply(file);
            if (!string.IsNullOrWhiteSpace(file.MapName)) { _mapName = file.MapName; _nameEdited = true; }
            _log($"SWTOR: {sel.SelectedCount} room(s) loaded from {path}" + (unknown.Count > 0 ? $" ({unknown.Count} unknown room(s) ignored: {string.Join(", ", unknown)})." : "."));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _log("ERROR: cannot load the room selection: " + e.Message);
            return false;
        }
    }

    private static int CategoryOrder(string c) => Array.IndexOf(CategoryOrderList, c) is var i and >= 0 ? i : CategoryOrderList.Length;

    private static readonly string[] CategoryOrderList =
        ["planet", "story", "ship", "flashpoint", "operation", "stronghold", "pvp", "fleet", "daily", "event", "uprising", "space", "system", "other"];

    private static string CategoryLabel(string c) => c switch
    {
        "planet" => "Planets", "story" => "Story", "ship" => "Ships", "flashpoint" => "Flashpoints", "operation" => "Operations",
        "stronghold" => "Strongholds", "pvp" => "PvP", "fleet" => "Fleets", "daily" => "Dailies", "event" => "Events",
        "uprising" => "Uprisings", "space" => "Space", "system" => "System", _ => "Other",
    };
}

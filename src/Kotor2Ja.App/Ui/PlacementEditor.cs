using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Kotor.Core.Entities;
using Kotor.Core.Scene;
using Kotor.Pipeline.Assembly;
using Kotor.Pipeline.Workflow;
using Kotor2Ja.App.Rendering;

namespace Kotor2Ja.App.Ui;

/// <summary>
/// Editeur de placement des modules d'un assemblage (plein ecran) : colonne de gauche (module de reference, modules, position et
/// yaw numeriques, portes de jonction, chevauchements, Apply / Cancel) et, a droite, vue de dessus (dessinee par ImGui) ou vue
/// libre (dessinee par <see cref="PlacementRenderer"/> dans <see cref="ViewRect"/>). Toute la logique passe par
/// <see cref="PlacementEditorModel"/>.
/// </summary>
public sealed class PlacementEditor
{
    private static readonly Vector4 Red = new(1f, 0.35f, 0.35f, 1f);
    private static readonly Vector4 Orange = new(1f, 0.65f, 0.15f, 1f);
    private static readonly Vector4 Green = new(0.6f, 0.9f, 0.6f, 1f);

    private PlacementEditorModel? _model;
    private int _selected;
    private bool _view3D;
    private Vector2 _viewCentre;      // centre de la vue de dessus (metres)
    private float _zoom = 4f;         // pixels par metre
    private float _step = 0.5f;       // pas de deplacement (metres)
    private float _angleStep = 5f;    // pas de rotation (degres)
    private int _partA, _partB, _doorA = -1, _doorB = -1;
    private string? _message;
    private bool _messageIsError;
    private enum Drag { None, Move, Rotate, Pan }
    private Drag _drag;
    private float _lastAngle;
    private bool _frameOnDraw;       // cadrer la vue de dessus a la premiere image (taille connue)

    public PlacementEditorModel? Model => _model;
    public bool IsOpen => _model is not null;
    public bool Shows3D => _view3D;
    public int Selected => _selected;

    /// <summary>Zone de la vue libre (pixels fenetre) lors de la derniere image : x, y, largeur, hauteur.</summary>
    public (int X, int Y, int Width, int Height) ViewRect { get; private set; }

    /// <summary>Portes de jonction choisies (pour la vue libre).</summary>
    public IReadOnlyList<(int Part, ModuleEntity Door)> ChosenDoors
    {
        get
        {
            if (_model is null) return [];
            var list = new List<(int, ModuleEntity)>(2);
            if (Door(_partA, _doorA) is { } a) list.Add((_partA, a));
            if (Door(_partB, _doorB) is { } b) list.Add((_partB, b));
            return list;
        }
    }

    /// <summary>Ouvre l'editeur sur la session : selection et module B sur le premier module non relie, module A sur la reference.</summary>
    public void Open(PlacementEditorModel model)
    {
        _model = model;
        AssemblySession s = model.Session;
        int firstUnlinked = Enumerable.Range(0, model.Count).FirstOrDefault(i => !s.IsLinked(i), -1);
        _partA = s.ReferenceIndex;
        _partB = firstUnlinked >= 0 ? firstUnlinked : (s.ReferenceIndex == 0 ? 1 : 0);
        _selected = _partB;
        _doorA = model.Doors(_partA).Count > 0 ? 0 : -1;
        _doorB = model.Doors(_partB).Count > 0 ? 0 : -1;
        _message = null;
        _drag = Drag.None;
        _view3D = false;
        _frameOnDraw = true;
    }

    private void Close() => _model = null;

    /// <summary>Dessine l'editeur. Apply appelle <see cref="WorkflowController.ApplyAssemblyAsync"/> puis ferme l'editeur.</summary>
    public void Draw(Vector2 displaySize, WorkflowController workflow)
    {
        if (_model is not { } model) return;
        float left = MainScreen.LeftWidth, scale = MainScreen.UiScale;
        const ImGuiWindowFlags panel = ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings;

        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(left, displaySize.Y));
        if (ImGui.Begin("##placement_left", panel)) DrawLeft(model, workflow);
        ImGui.End();
        if (_model is null) return;   // Apply ou Cancel

        float barHeight = ImGui.GetFrameHeightWithSpacing() * 2f + 8f * scale;
        ImGui.SetNextWindowPos(new Vector2(left, 0));
        ImGui.SetNextWindowSize(new Vector2(displaySize.X - left, barHeight));
        if (ImGui.Begin("##placement_bar", panel)) DrawBar();
        ImGui.End();

        var viewPos = new Vector2(left, barHeight);
        var viewSize = new Vector2(Math.Max(1f, displaySize.X - left), Math.Max(1f, displaySize.Y - barHeight));
        ViewRect = ((int)viewPos.X, (int)viewPos.Y, (int)viewSize.X, (int)viewSize.Y);
        if (!_view3D)
        {
            ImGui.SetNextWindowPos(viewPos);
            ImGui.SetNextWindowSize(viewSize);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            if (ImGui.Begin("##placement_top", panel | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
                DrawTopView(model, viewPos, viewSize);
            ImGui.End();
            ImGui.PopStyleVar();
        }
        HandleKeys(model);
    }

    // ---- Colonne de gauche ------------------------------------------------------------------------------------------------------

    private void DrawLeft(PlacementEditorModel model, WorkflowController workflow)
    {
        AssemblySession s = model.Session;
        ImGui.TextUnformatted("Link modules");
        ImGui.Separator();

        // Module de reference : le changer refait le placement automatique.
        ImGui.TextUnformatted("Reference module");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##reference", s.Modules[s.ReferenceIndex]))
        {
            for (int i = 0; i < s.Modules.Count; i++)
                if (ImGui.Selectable(s.Modules[i] + "###ref" + i, i == s.ReferenceIndex) && i != s.ReferenceIndex)
                {
                    s.ReferenceIndex = i;
                    _partA = i;
                    SetMessage("Reference changed: automatic placement redone, manual changes forgotten.", false);
                }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sky, ambient light and spawn point come from the reference module.\nChanging it redoes the automatic placement.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Modules");
        for (int i = 0; i < model.Count; i++)
        {
            Vector3 c = PlacementRenderer.PartColor(i);
            ImGui.ColorButton("##col" + i, new Vector4(c, 1f), ImGuiColorEditFlags.NoTooltip, new Vector2(ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            string tag = i == s.ReferenceIndex ? " (reference)" : s.IsLinked(i) ? "" : " (not linked)";
            bool unlinked = !s.IsLinked(i);
            if (unlinked) ImGui.PushStyleColor(ImGuiCol.Text, Orange);
            if (ImGui.Selectable(s.Modules[i] + tag + "###part" + i, i == _selected)) _selected = i;
            if (unlinked) ImGui.PopStyleColor();
        }

        ImGui.Separator();
        DrawPlacement(model);
        ImGui.Separator();
        DrawJunction(model);
        ImGui.Separator();
        DrawOverlaps(model);
        ImGui.Separator();

        if (!s.IsComplete)
        {
            string names = string.Join(", ", s.UnlinkedGroups.SelectMany(g => g).Select(i => s.Modules[i]));
            ImGui.PushStyleColor(ImGuiCol.Text, Orange);
            ImGui.TextWrapped($"Not linked to the reference: {names}. Choose a door on each side and click Snap doors.");
            ImGui.PopStyleColor();
        }
        if (_message is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, _messageIsError ? Red : Green);
            ImGui.TextWrapped(_message);
            ImGui.PopStyleColor();
        }

        // Apply / Cancel en bas de la colonne.
        float buttonsHeight = ImGui.GetFrameHeightWithSpacing() * 1.6f;
        float y = ImGui.GetWindowHeight() - buttonsHeight - ImGui.GetStyle().WindowPadding.Y;
        if (ImGui.GetCursorPosY() < y) ImGui.SetCursorPosY(y);
        bool busy = workflow.IsBusy;
        float w = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Apply", new Vector2(w, ImGui.GetFrameHeight() * 1.5f)))
        {
            _ = workflow.ApplyAssemblyAsync();
            Close();
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Rebuild the preview with this placement.");
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(w, ImGui.GetFrameHeight() * 1.5f)))
        {
            model.Cancel();
            Close();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Forget the changes made since the editor was opened.");
    }

    private void DrawPlacement(PlacementEditorModel model)
    {
        int p = _selected;
        RigidTransform t = model.Transform(p);
        ImGui.TextUnformatted($"Placement of {model.Session.Modules[p]}");
        Vector3 pos = t.Translation;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputFloat3("##pos", ref pos, "%.2f") && Finite(pos)) model.SetNumeric(p, pos, t.YawDegrees);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("X, Y, Z of the module origin (metres).");
        float yaw = t.YawDegrees;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputFloat("##yaw", ref yaw, 1f, 15f, "Yaw %.2f deg") && float.IsFinite(yaw) && yaw != t.YawDegrees)
            model.Rotate(p, yaw - t.YawDegrees);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotation around Z (degrees); the module turns around its centre.");

        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
        ImGui.InputFloat("Step (m)##step", ref _step, 0.1f, 1f, "%.2f");
        _step = Math.Clamp(_step, 0.01f, 100f);
        float bw = (ImGui.GetContentRegionAvail().X - 5 * ImGui.GetStyle().ItemSpacing.X) / 6f;
        var bs = new Vector2(bw, 0);
        if (ImGui.Button("-X", bs)) model.Nudge(p, new Vector3(-_step, 0, 0)); ImGui.SameLine();
        if (ImGui.Button("+X", bs)) model.Nudge(p, new Vector3(_step, 0, 0)); ImGui.SameLine();
        if (ImGui.Button("-Y", bs)) model.Nudge(p, new Vector3(0, -_step, 0)); ImGui.SameLine();
        if (ImGui.Button("+Y", bs)) model.Nudge(p, new Vector3(0, _step, 0)); ImGui.SameLine();
        if (ImGui.Button("-Z", bs)) model.Nudge(p, new Vector3(0, 0, -_step)); ImGui.SameLine();
        if (ImGui.Button("+Z", bs)) model.Nudge(p, new Vector3(0, 0, _step));
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.5f);
        ImGui.InputFloat("Angle (deg)##astep", ref _angleStep, 1f, 15f, "%.2f");
        _angleStep = Math.Clamp(_angleStep, 0.01f, 180f);
        float rw = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        if (ImGui.Button("Rotate left", new Vector2(rw, 0))) model.Rotate(p, _angleStep);
        ImGui.SameLine();
        if (ImGui.Button("Rotate right", new Vector2(rw, 0))) model.Rotate(p, -_angleStep);
        ImGui.TextDisabled("Arrows / Page Up / Page Down move the selected module.");
    }

    private void DrawJunction(PlacementEditorModel model)
    {
        AssemblySession s = model.Session;
        ImGui.TextUnformatted("Junction doors");
        float half = (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X) / 2f;
        float listHeight = ImGui.GetTextLineHeightWithSpacing() * 5f;
        ImGui.BeginGroup();
        DoorColumn(model, "A", ref _partA, ref _doorA, half, listHeight);
        ImGui.EndGroup();
        ImGui.SameLine();
        ImGui.BeginGroup();
        DoorColumn(model, "B", ref _partB, ref _doorB, half, listHeight);
        ImGui.EndGroup();

        ModuleEntity? a = Door(_partA, _doorA), b = Door(_partB, _doorB);
        ImGui.BeginDisabled(a is null || b is null || _partA == _partB);
        if (ImGui.Button("Snap doors", new Vector2(-1, 0)) && a is not null && b is not null)
        {
            try
            {
                model.SnapDoors(_partA, a, _partB, b);
                SetMessage($"Door '{a.Name}' of {s.Modules[_partA]} joined to door '{b.Name}' of {s.Modules[_partB]}.", false);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                SetMessage(e.Message, true);
            }
        }
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(_partA == _partB ? "Choose two different modules." : "Place the two doors back to back; the module not linked to the reference moves.");
    }

    private static void DoorColumn(PlacementEditorModel model, string side, ref int part, ref int door, float width, float listHeight)
    {
        AssemblySession s = model.Session;
        ImGui.SetNextItemWidth(width);
        if (ImGui.BeginCombo("##part" + side, s.Modules[part]))
        {
            for (int i = 0; i < model.Count; i++)
                if (ImGui.Selectable(s.Modules[i] + "###" + side + i, i == part) && i != part)
                {
                    part = i;
                    door = model.Doors(i).Count > 0 ? 0 : -1;
                }
            ImGui.EndCombo();
        }
        if (ImGui.BeginListBox("##doors" + side, new Vector2(width, listHeight)))
        {
            IReadOnlyList<ModuleEntity> doors = model.Doors(part);
            if (doors.Count == 0) ImGui.TextDisabled("No door");
            for (int k = 0; k < doors.Count; k++)
            {
                ModuleEntity d = doors[k];
                string label = string.Create(CultureInfo.InvariantCulture, $"{d.Name} ({d.Position.X:0.0}, {d.Position.Y:0.0})");
                if (!string.IsNullOrEmpty(d.LinkedToModule)) label += " -> " + d.LinkedToModule;
                if (ImGui.Selectable(label + "###d" + side + k, k == door)) door = k;
            }
            ImGui.EndListBox();
        }
    }

    private static void DrawOverlaps(PlacementEditorModel model)
    {
        IReadOnlyList<(int A, int B, string Detail)> overlaps = model.Overlaps;
        if (overlaps.Count == 0)
        {
            ImGui.TextColored(Green, "No overlap.");
            return;
        }
        ImGui.TextColored(Red, $"Overlaps ({overlaps.Count})");
        float h = Math.Min(overlaps.Count, 4) * ImGui.GetTextLineHeightWithSpacing() * 2f + 6f;
        if (ImGui.BeginChild("##overlaps", new Vector2(-1, h), ImGuiChildFlags.Borders))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, Red);
            foreach (var (_, _, detail) in overlaps) ImGui.TextWrapped(detail);
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();
    }

    // ---- Barre d'outils et vue de dessus ----------------------------------------------------------------------------------------

    private void DrawBar()
    {
        if (ImGui.RadioButton("Top view", !_view3D)) _view3D = false;
        ImGui.SameLine();
        if (ImGui.RadioButton("3D view", _view3D)) _view3D = true;
        ImGui.SameLine();
        if (!_view3D && ImGui.Button("Frame all")) FrameAll(new Vector2(ViewRect.Width, ViewRect.Height));
        ImGui.TextDisabled(_view3D
            ? "Right-drag: look around   WASD/ZQSD: fly   Space/E, C: up, down   Shift: faster   Wheel: speed"
            : "Left-drag: move a module (or pan on empty space)   Right-drag: rotate the selected module   Middle-drag: pan   Wheel: zoom");
    }

    private void DrawTopView(PlacementEditorModel model, Vector2 origin, Vector2 size)
    {
        if (_frameOnDraw) { FrameAll(size); _frameOnDraw = false; }
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + size, ImGui.GetColorU32(new Vector4(0.08f, 0.1f, 0.14f, 1f)));
        dl.PushClipRect(origin, origin + size, true);
        Vector2 half = size * 0.5f;
        Vector2 ToScreen(Vector3 w) => origin + half + new Vector2(w.X - _viewCentre.X, -(w.Y - _viewCentre.Y)) * _zoom;
        Vector2 ToWorld(Vector2 sp) => _viewCentre + new Vector2(sp.X - origin.X - half.X, -(sp.Y - origin.Y - half.Y)) / _zoom;

        DrawGrid(dl, origin, size, ToWorld);

        var overlapping = new HashSet<int>(model.Overlaps.SelectMany(o => new[] { o.A, o.B }));
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < model.Count; i++)
            {
                // La partie selectionnee est dessinee en dernier (au-dessus des autres).
                if ((i == _selected) != (pass == 1)) continue;
                DrawPart(dl, model, i, ToScreen, overlapping.Contains(i));
            }
        DrawDoors(dl, model, ToScreen);
        dl.PopClipRect();

        // Interaction : bouton invisible sur toute la vue.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##topcanvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        ImGuiIOPtr io = ImGui.GetIO();
        bool hovered = ImGui.IsItemHovered();
        Vector2 mouse = io.MousePos;
        if (hovered && _drag == Drag.None)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                int? hit = model.PartAt(ToWorld(mouse));
                if (hit is { } h) { _selected = h; _drag = Drag.Move; }
                else _drag = Drag.Pan;
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                _drag = Drag.Rotate;
                _lastAngle = ScreenAngle(mouse, ToScreen(model.Centre(_selected)));
            }
            else if (ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) _drag = Drag.Pan;
        }
        switch (_drag)
        {
            case Drag.Move when ImGui.IsMouseDown(ImGuiMouseButton.Left):
                if (io.MouseDelta != Vector2.Zero) model.Nudge(_selected, new Vector3(io.MouseDelta.X / _zoom, -io.MouseDelta.Y / _zoom, 0f));
                break;
            case Drag.Pan when ImGui.IsMouseDown(ImGuiMouseButton.Left) || ImGui.IsMouseDown(ImGuiMouseButton.Middle):
                _viewCentre -= new Vector2(io.MouseDelta.X, -io.MouseDelta.Y) / _zoom;
                break;
            case Drag.Rotate when ImGui.IsMouseDown(ImGuiMouseButton.Right):
            {
                float a = ScreenAngle(mouse, ToScreen(model.Centre(_selected)));
                float d = a - _lastAngle;
                if (d > 180f) d -= 360f; else if (d < -180f) d += 360f;
                if (MathF.Abs(d) > 0.001f) model.Rotate(_selected, d);
                _lastAngle = a;
                break;
            }
            default:
                _drag = Drag.None;
                break;
        }
        if (hovered && io.MouseWheel != 0f)
        {
            // Zoom autour du curseur : le point sous la souris reste en place.
            Vector2 before = ToWorld(mouse);
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.2f, io.MouseWheel), 0.05f, 400f);
            _viewCentre += before - ToWorld(mouse);
        }
    }

    /// <summary>Angle (degres, sens trigonometrique en repere monde) du point ecran autour du centre ecran.</summary>
    private static float ScreenAngle(Vector2 p, Vector2 centre) => MathF.Atan2(-(p.Y - centre.Y), p.X - centre.X) * 180f / MathF.PI;

    private void DrawGrid(ImDrawListPtr dl, Vector2 origin, Vector2 size, Func<Vector2, Vector2> toWorld)
    {
        // Pas de grille : puissance de 10 donnant au moins 40 pixels.
        float spacing = MathF.Pow(10f, MathF.Ceiling(MathF.Log10(40f / _zoom)));
        Vector2 w0 = toWorld(origin), w1 = toWorld(origin + size);
        uint minor = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)), axis = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.25f));
        for (float x = MathF.Floor(w0.X / spacing) * spacing; x <= w1.X; x += spacing)
        {
            float sx = origin.X + size.X * 0.5f + (x - _viewCentre.X) * _zoom;
            dl.AddLine(new Vector2(sx, origin.Y), new Vector2(sx, origin.Y + size.Y), MathF.Abs(x) < spacing * 0.01f ? axis : minor);
        }
        for (float y = MathF.Floor(w1.Y / spacing) * spacing; y <= w0.Y; y += spacing)
        {
            float sy = origin.Y + size.Y * 0.5f - (y - _viewCentre.Y) * _zoom;
            dl.AddLine(new Vector2(origin.X, sy), new Vector2(origin.X + size.X, sy), MathF.Abs(y) < spacing * 0.01f ? axis : minor);
        }
        dl.AddText(origin + new Vector2(8, 8), ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f)),
            string.Create(CultureInfo.InvariantCulture, $"Grid: {spacing:0.##} m"));
    }

    private void DrawPart(ImDrawListPtr dl, PlacementEditorModel model, int i, Func<Vector3, Vector2> toScreen, bool overlapping)
    {
        RigidTransform t = model.Transform(i);
        Vector3 c = PlacementRenderer.PartColor(i);
        bool selected = i == _selected;
        uint fill = ImGui.GetColorU32(new Vector4(c, selected ? 0.55f : 0.3f));
        IReadOnlyList<Vector3> floor = model.LocalFloor(i);
        for (int k = 0; k + 2 < floor.Count; k += 3)
            dl.AddTriangleFilled(toScreen(t.Apply(floor[k])), toScreen(t.Apply(floor[k + 1])), toScreen(t.Apply(floor[k + 2])), fill);

        // Contour de la boite du module (tournee avec lui) : rouge s'il chevauche un autre module.
        var (min, max) = model.LocalBounds(i);
        Vector2[] corners =
        [
            toScreen(t.Apply(new Vector3(min.X, min.Y, 0))), toScreen(t.Apply(new Vector3(max.X, min.Y, 0))),
            toScreen(t.Apply(new Vector3(max.X, max.Y, 0))), toScreen(t.Apply(new Vector3(min.X, max.Y, 0))),
        ];
        uint outline = overlapping ? ImGui.GetColorU32(Red) : ImGui.GetColorU32(new Vector4(c, selected ? 1f : 0.7f));
        for (int k = 0; k < 4; k++) dl.AddLine(corners[k], corners[(k + 1) % 4], outline, selected ? 2.5f : 1.2f);
        Vector2 centre = toScreen(model.Centre(i));
        string name = model.Session.Modules[i];
        dl.AddText(centre - ImGui.CalcTextSize(name) * 0.5f, ImGui.GetColorU32(new Vector4(1, 1, 1, selected ? 1f : 0.75f)), name);
        if (selected) dl.AddCircle(centre, 5f, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.9f)));
    }

    private void DrawDoors(ImDrawListPtr dl, PlacementEditorModel model, Func<Vector3, Vector2> toScreen)
    {
        var door = PreviewRenderer.KindColor(EntityKind.Door);
        uint normal = ImGui.GetColorU32(new Vector4(door.R / 255f, door.G / 255f, door.B / 255f, 1f));
        uint chosen = ImGui.GetColorU32(new Vector4(1f, 0.9f, 0.15f, 1f));
        ModuleEntity? a = Door(_partA, _doorA), b = Door(_partB, _doorB);
        for (int i = 0; i < model.Count; i++)
        {
            RigidTransform t = model.Transform(i);
            foreach (ModuleEntity d in model.Doors(i))
            {
                bool isChosen = (i == _partA && ReferenceEquals(d, a)) || (i == _partB && ReferenceEquals(d, b));
                Vector2 p = toScreen(t.Apply(d.Position));
                // Trait dans la direction du yaw de la porte (0 = +Y, sens trigonometrique).
                float yr = t.ApplyYaw(d.Yaw) * MathF.PI / 180f;
                var dir = new Vector2(-MathF.Sin(yr), -MathF.Cos(yr));   // ecran : Y vers le bas
                float r = isChosen ? 6f : 4f;
                dl.AddCircleFilled(p, r, isChosen ? chosen : normal);
                dl.AddLine(p, p + dir * r * 2.5f, isChosen ? chosen : normal, 2f);
                if (isChosen || _zoom > 8f)
                    dl.AddText(p + new Vector2(r + 2, -r - 2), isChosen ? chosen : normal, d.Name);
            }
        }
    }

    private void HandleKeys(PlacementEditorModel model)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        if (io.WantTextInput) return;
        Vector3 d = Vector3.Zero;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) d.X -= _step;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) d.X += _step;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) d.Y += _step;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) d.Y -= _step;
        if (ImGui.IsKeyPressed(ImGuiKey.PageUp)) d.Z += _step;
        if (ImGui.IsKeyPressed(ImGuiKey.PageDown)) d.Z -= _step;
        if (d != Vector3.Zero) model.Nudge(_selected, d);
    }

    /// <summary>Cadre la vue de dessus sur l'ensemble des modules.</summary>
    private void FrameAll(Vector2 viewSize)
    {
        if (_model is not { } model) return;
        Vector2 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < model.Count; i++)
        {
            var (bmin, bmax) = model.LocalBounds(i);
            RigidTransform t = model.Transform(i);
            foreach (var corner in new[] { new Vector3(bmin.X, bmin.Y, 0), new Vector3(bmax.X, bmin.Y, 0), new Vector3(bmax.X, bmax.Y, 0), new Vector3(bmin.X, bmax.Y, 0) })
            {
                Vector3 w = t.Apply(corner);
                min = Vector2.Min(min, new Vector2(w.X, w.Y));
                max = Vector2.Max(max, new Vector2(w.X, w.Y));
            }
        }
        _viewCentre = (min + max) * 0.5f;
        Vector2 extent = Vector2.Max(max - min, new Vector2(10f));
        _zoom = Math.Clamp(MathF.Min(viewSize.X / extent.X, viewSize.Y / extent.Y) * 0.85f, 0.05f, 400f);
    }

    /// <summary>Boite englobante de tous les modules transformes (metres), pour cadrer la vue libre.</summary>
    public (Vector3 Min, Vector3 Max) WorldBounds()
    {
        if (_model is not { } model) return (Vector3.Zero, Vector3.Zero);
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < model.Count; i++)
        {
            var (bmin, bmax) = model.LocalBounds(i);
            RigidTransform t = model.Transform(i);
            for (int k = 0; k < 8; k++)
            {
                Vector3 w = t.Apply(new Vector3((k & 1) == 0 ? bmin.X : bmax.X, (k & 2) == 0 ? bmin.Y : bmax.Y, (k & 4) == 0 ? bmin.Z : bmax.Z));
                min = Vector3.Min(min, w);
                max = Vector3.Max(max, w);
            }
        }
        return (min, max);
    }

    private ModuleEntity? Door(int part, int door)
    {
        if (_model is not { } model || part < 0 || part >= model.Count) return null;
        IReadOnlyList<ModuleEntity> doors = model.Doors(part);
        return door >= 0 && door < doors.Count ? doors[door] : null;
    }

    private void SetMessage(string text, bool error)
    {
        _message = text;
        _messageIsError = error;
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

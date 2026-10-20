using System.Text.Json;
using ImGuiNET;
using Kotor.Core.Export;
using Kotor.Pipeline.Preview;
using Kotor.Pipeline.Workflow;
using Kotor2Ja.App.Rendering;
using Kotor2Ja.App.Ui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Kotor2Ja.App;

/// <summary>Fenetre principale (MonoGame + ImGui). Squelette adapte de l'application du projet TFU.</summary>
public sealed class AppGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private ImGuiRenderer _imgui = null!;
    private WorkflowController _workflow = null!;
    private MainScreen _screen = null!;
    private PreviewRenderer _renderer = null!;
    private readonly FreeCamera _camera = new();
    private PlacementRenderer _placement = null!;
    private readonly FreeCamera _editorCamera = new();   // vue libre de l'editeur de placement
    private readonly ViewOptions _options = new();
    private int _lastWheel;
    private bool _looking;
    private string? _framedModule;
    private System.Numerics.Vector3 _framedMin, _framedMax;

    private readonly AutomationOptions? _auto;
    private int _autoPhase;      // 0 demarrage, 1 etapes, 2 attente de la vue, 3 images avant capture, 4 termine
    private Task? _modulesTask;   // liste des modules (le mode automatise l attend avant la capture)
    private int _autoFrames;

    /// <summary>Code de sortie du processus (1 : echec en mode automatise avec --exit).</summary>
    public int ExitCode { get; private set; }

    public AppGame(AutomationOptions? automation = null)
    {
        _auto = automation;
        // Initial size: 85% of the desktop so it fits any screen.
        DisplayMode desktop = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Math.Clamp((int)(desktop.Width * 0.85), 800, 2560),
            PreferredBackBufferHeight = Math.Clamp((int)(desktop.Height * 0.85), 600, 1440),
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnClientSizeChanged;
        Window.Title = "KOTOR → Jedi Academy";
        IsMouseVisible = true;
    }

    private PreviewModel? _failedPreview;
    private Swtor.Pipeline.SwtorWorkflowSteps? _swtorSteps;
    private KotorWorkflowSteps? _kotorSteps;
    private Jk.Pipeline.JkWorkflowSteps? _jkSteps;

    // The back buffer follows the window when it is resized, maximized or the screen resolution changes.
    private void OnClientSizeChanged(object? sender, EventArgs e)
    {
        Rectangle b = Window.ClientBounds;
        if (b.Width < 1 || b.Height < 1) return;   // minimized
        if (b.Width == _graphics.PreferredBackBufferWidth && b.Height == _graphics.PreferredBackBufferHeight) return;
        _graphics.PreferredBackBufferWidth = b.Width;
        _graphics.PreferredBackBufferHeight = b.Height;
        _graphics.ApplyChanges();
    }

    private void FitUi(ImGuiIOPtr io)
    {
        PresentationParameters pp = GraphicsDevice.PresentationParameters;
        MainScreen.Fit(pp.BackBufferWidth, pp.BackBufferHeight);
        io.FontGlobalScale = MainScreen.UiScale;
    }

    protected override void Initialize()
    {
        _imgui = new ImGuiRenderer(this);
        _imgui.RebuildFontAtlas();
        _renderer = new PreviewRenderer(GraphicsDevice);
        _placement = new PlacementRenderer(GraphicsDevice);

        // Same configuration file as the CLI, next to the executable. Without it: folders detected through Steam.
        string configPath = ConfigLoader.FindConfig(AppContext.BaseDirectory) ?? Path.Combine(AppContext.BaseDirectory, ConfigLoader.ConfigFileName);
        ExportOptions options;
        string? configWarning = null;
        try
        {
            options = ConfigLoader.Load(configPath);
            if (!File.Exists(configPath)) configWarning = "No configuration file (next to the executable or in %APPDATA%\\Kotor2Ja): game folders detected through Steam (see Settings).";
            else if (options.Validate() is { } bad) { configWarning = $"ERROR: invalid configuration ({configPath}): {bad}"; options = ConfigLoader.Defaults(); }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            configWarning = $"ERROR: unreadable configuration ({configPath}): {e.Message} Default values used.";
            options = ConfigLoader.Defaults();
        }
        _workflow = NewWorkflow(options);
        _screen = new MainScreen(_workflow, _options);
        _screen.SetSwtor(_swtorSteps!);
        _screen.SetKotor(_kotorSteps!);
        _screen.SetJk(_jkSteps!);
        _screen.SaveSettings = SaveSettings;
        _screen.DetectSettings = () => GameLocator.Detect();
        if (_auto is not null) _screen.LogSink = Console.WriteLine;
        if (configWarning is not null) _screen.AddLog(configWarning);
        _screen.SetSettings(options);
        LogFolders(options);
        LoadModules();

        base.Initialize();
    }

    // KOTOR 1 / 2, SWTOR et Jedi Knight derriere un meme flux de travail ; les branches SWTOR et Jedi Knight de l arbre reprennent leurs etapes.
    private WorkflowController NewWorkflow(ExportOptions options)
    {
        var swtor = _swtorSteps = new Swtor.Pipeline.SwtorWorkflowSteps(options);
        var kotor = _kotorSteps = new KotorWorkflowSteps(options);
        _jkSteps?.Dispose();   // referme les archives GOB du niveau precedent
        var jk = _jkSteps = new Jk.Pipeline.JkWorkflowSteps(options);
        _screen?.SetSwtor(swtor);
        _screen?.SetKotor(kotor);
        _screen?.SetJk(jk);
        return new WorkflowController(new Swtor.Pipeline.GameWorkflowSteps(kotor, swtor, jk));
    }

    private void LogFolders(ExportOptions options)
    {
        _screen.AddLog($"KOTOR: {options.KotorDir} (scale {options.Scale:0.##})");
        _screen.AddLog($"KOTOR 2: {(string.IsNullOrWhiteSpace(options.Kotor2Dir) ? "(not configured)" : options.Kotor2Dir)}");
        _screen.AddLog($"Jedi Knight: {(_jkSteps is { } jk && jk.IsInstalled(Kotor.Core.KotorGame.JediKnight) ? jk.JkDir : string.IsNullOrWhiteSpace(options.JediKnightDir) ? "(not configured, node greyed out)" : options.JediKnightDir + " (not found, node greyed out)")}");
        if (_swtorSteps is { } sw) _screen.AddLog($"SWTOR: {(sw.IsInstalled(Kotor.Core.KotorGame.Swtor) ? sw.Assets.Root : sw.Assets.Root + " (not found, branch hidden)")}");
    }

    // Settings "Save": writes the config file next to the executable (else in %APPDATA%\Kotor2Ja), then reloads the module list with the new folders.
    private string? SaveSettings(ExportOptions options)
    {
        if (_workflow.IsBusy) return "Wait for the current step to finish.";
        if (options.Validate() is { } bad) return bad;
        string written;
        try { written = ConfigLoader.SaveWithFallback(AppContext.BaseDirectory, options); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return "Cannot write the configuration file: " + e.Message; }
        bool fallback = !string.Equals(Path.GetDirectoryName(written), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        string info = "Saved to " + written + (fallback ? " (the executable folder is not writable)." : ".");
        _screen.SavedInfo = info;
        _screen.AddLog("Settings " + char.ToLowerInvariant(info[0]) + info[1..]);
        _workflow = NewWorkflow(options);
        _screen.SetWorkflow(_workflow);
        LogFolders(options);
        LoadModules();
        return null;
    }

    private void LoadModules()
    {
        WorkflowController workflow = _workflow;
        _screen.SetModules([]);
        _modulesTask = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<ModuleListItem> modules = workflow.Modules();
                if (ReferenceEquals(workflow, _workflow)) _screen.SetModules(modules);
                _screen.AddLog($"{modules.Count} module(s) found.");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _screen.SetModules([]);
                _screen.AddLog("ERROR: cannot list the modules: " + e.Message);
            }
        });
    }

    protected override void Update(GameTime gameTime)
    {
        var io = ImGui.GetIO();
        FitUi(io);
        MouseState mouse = Mouse.GetState();
        KeyboardState kb = Keyboard.GetState();

        // Nouvelle vue : envoi GPU dans le fil de rendu quand la reference change.
        PreviewModel? preview = _workflow.Preview;
        if (!ReferenceEquals(preview, _renderer.Model) && !ReferenceEquals(preview, _failedPreview))
        {
            try
            {
                _renderer.Upload(preview);
                if (preview is not null) FrameIfNeeded(preview);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _renderer.Upload(null);
                _failedPreview = preview;  // ne plus reessayer (ni journaliser) tant que la vue ne change pas
                _screen.AddLog("ERROR: cannot display the view: " + e.Message);
            }
        }

        if (_auto is not null) StepAutomation();

        // Editeur de placement : envoi GPU des modules a l'ouverture, cadrage de sa camera.
        PlacementEditor editor = _screen.Editor;
        if (!ReferenceEquals(editor.Model, _placement.Model))
        {
            try
            {
                _placement.Upload(editor.Model);
                if (editor.Model is not null)
                {
                    var (min, max) = editor.WorldBounds();
                    _editorCamera.Frame(new Vector3(min.X, min.Y, min.Z) * PlacementRenderer.Scale, new Vector3(max.X, max.Y, max.Z) * PlacementRenderer.Scale);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _placement.Upload(null);
                _screen.AddLog("ERROR: cannot display the modules in 3D: " + e.Message);
            }
        }

        int left = (int)MainScreen.LeftWidth;
        var center = new Point(left + (Window.ClientBounds.Width - left) / 2, Window.ClientBounds.Height / 2);
        if (editor.IsOpen)
        {
            // Vue de dessus : la souris appartient a ImGui ; vue libre : camera de l'editeur.
            bool captured = !editor.Shows3D || (io.WantCaptureMouse && !_looking);
            var r = editor.ViewRect;
            var c3 = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
            _editorCamera.Update((float)gameTime.ElapsedGameTime.TotalSeconds, kb, mouse, _lastWheel,
                captured, io.WantTextInput || !editor.Shows3D, IsActive, c3, out _looking);
        }
        else
            _camera.Update((float)gameTime.ElapsedGameTime.TotalSeconds, kb, mouse, _lastWheel,
                io.WantCaptureMouse && !_looking, io.WantTextInput, IsActive, center, out _looking);
        _lastWheel = mouse.ScrollWheelValue;
        if (!editor.IsOpen)
        {
            UpdateSpawnPlacement(io, mouse, kb);
            UpdateRoomPicking(io, mouse);
            if (_screen.TakeGoToRoom() is { } goRoom && _workflow.Preview is { } goModel && PreviewPicker.RoomBounds(goModel, goRoom) is var (gmin, gmax))
                _camera.Frame(new Vector3(gmin.X, gmin.Y, gmin.Z), new Vector3(gmax.X, gmax.Y, gmax.Z));
            _options.SelectedRooms = _screen.HighlightedRooms;
        }
        _wasLeftDown = mouse.LeftButton == ButtonState.Pressed;
        IsMouseVisible = !_looking;
        base.Update(gameTime);
    }

    private bool _wasLeftDown;

    // Mode « Place spawn » : un clic gauche sur la vue tire un rayon depuis la camera ; le point touche (releve de la hauteur du
    // joueur, comme le point automatique) devient le spawn, oriente comme la camera. Echap quitte le mode.
    private void UpdateSpawnPlacement(ImGuiIOPtr io, MouseState mouse, KeyboardState kb)
    {
        if (!_screen.PlacingSpawn) return;
        if (kb.IsKeyDown(Keys.Escape)) { _screen.PlacingSpawn = false; return; }
        bool click = mouse.LeftButton == ButtonState.Pressed && !_wasLeftDown;
        PreviewModel? model = _workflow.Preview;
        if (!click || io.WantCaptureMouse || _looking || model is null || !IsActive) return;

        PresentationParameters pp = GraphicsDevice.PresentationParameters;
        int left = (int)MainScreen.LeftWidth;
        var viewport = new Viewport(left, 0, Math.Max(1, pp.BackBufferWidth - left), Math.Max(1, pp.BackBufferHeight)) { MinDepth = 0f, MaxDepth = 1f };
        if (mouse.X < left) return;
        Matrix view = _camera.View, proj = _camera.Projection(viewport.AspectRatio);
        Vector3 near = viewport.Unproject(new Vector3(mouse.X, mouse.Y, 0f), proj, view, Matrix.Identity);
        Vector3 far = viewport.Unproject(new Vector3(mouse.X, mouse.Y, 1f), proj, view, Matrix.Identity);
        var origin = new System.Numerics.Vector3(_camera.Position.X, _camera.Position.Y, _camera.Position.Z);
        var dir = new System.Numerics.Vector3(far.X - near.X, far.Y - near.Y, far.Z - near.Z);
        if (PreviewPicker.Raycast(model, origin, dir) is not { } hit)
        {
            _screen.AddLog("Place spawn: nothing under the cursor, click on the floor.");
            return;
        }
        var spawn = hit + new System.Numerics.Vector3(0f, 0f, SpawnHeight);
        if (_workflow.PlaceSpawn(spawn, _camera.Yaw))
        {
            _screen.AddLog($"Spawn placed at {spawn.X:0} {spawn.Y:0} {spawn.Z:0}, angle {_camera.Yaw:0}.");
            _screen.PlacingSpawn = false;
        }
    }

    // Grande zone SWTOR apercue : un clic gauche sur la vue tire un rayon ; la room touchee est choisie ou retiree (surbrillance).
    private void UpdateRoomPicking(ImGuiIOPtr io, MouseState mouse)
    {
        if (!_screen.CanPickRooms) return;
        bool click = mouse.LeftButton == ButtonState.Pressed && !_wasLeftDown;
        PreviewModel? model = _workflow.Preview;
        if (!click || io.WantCaptureMouse || _looking || model is null || !IsActive) return;

        PresentationParameters pp = GraphicsDevice.PresentationParameters;
        int left = (int)MainScreen.LeftWidth;
        var viewport = new Viewport(left, 0, Math.Max(1, pp.BackBufferWidth - left), Math.Max(1, pp.BackBufferHeight)) { MinDepth = 0f, MaxDepth = 1f };
        if (mouse.X < left) return;
        Matrix view = _camera.View, proj = _camera.Projection(viewport.AspectRatio);
        Vector3 near = viewport.Unproject(new Vector3(mouse.X, mouse.Y, 0f), proj, view, Matrix.Identity);
        Vector3 far = viewport.Unproject(new Vector3(mouse.X, mouse.Y, 1f), proj, view, Matrix.Identity);
        var origin = new System.Numerics.Vector3(_camera.Position.X, _camera.Position.Y, _camera.Position.Z);
        var dir = new System.Numerics.Vector3(far.X - near.X, far.Y - near.Y, far.Z - near.Z);
        if (PreviewPicker.RaycastRoom(model, origin, dir) is not { Room: { } room }) return;
        _screen.ToggleRoom(room);
    }

    private const float SpawnHeight = 25f;  // comme ModuleExporter.SpawnHeight : hauteur du point au-dessus du sol

    // Camera sur le point d'apparition, sinon sur les bornes. Les etapes suivantes d'un meme module gardent la position du joueur
    // quand les bornes ne changent pas ; un autre module ou d'autres bornes recadrent.
    private void FrameIfNeeded(PreviewModel m)
    {
        string? module = _workflow.SelectedModule;
        if (module == _framedModule && m.BoundsMin == _framedMin && m.BoundsMax == _framedMax) return;
        _framedModule = module;
        _framedMin = m.BoundsMin;
        _framedMax = m.BoundsMax;
        if (m.Spawn is { } sp) _camera.PlaceAt(new Vector3(sp.X, sp.Y, sp.Z), m.SpawnYaw);
        else _camera.Frame(new Vector3(m.BoundsMin.X, m.BoundsMin.Y, m.BoundsMin.Z), new Vector3(m.BoundsMax.X, m.BoundsMax.Y, m.BoundsMax.Z));
    }

    // Mode automatise (--module) : selection, etapes jusqu'a --run-to, camera, 3 images, capture, sortie. Aucun clic.
    private void StepAutomation()
    {
        AutomationOptions a = _auto!;
        switch (_autoPhase)
        {
            case 0:
                if (_modulesTask is { IsCompleted: false }) return;   // la liste des modules doit etre chargee avant la selection
                // Plusieurs modules : comme la selection multiple (assemblage, liaison automatique a l'apercu).
                if (!_workflow.SelectModules(a.Game, a.Modules)) { FailAutomation("Could not select the module."); return; }
                _ = _workflow.PreviewAsync();
                _autoPhase = 1;
                break;
            case 1:
                if (_workflow.IsBusy) return;
                if (_workflow.LastError is { } error) { FailAutomation(error); return; }
                int reached = _workflow.Stage switch
                {
                    WorkflowStage.Previewed => 0, WorkflowStage.Imported => 1, WorkflowStage.Exported => 2, WorkflowStage.Packed => 3, _ => -1,
                };
                if (reached < 0) { FailAutomation("The preview step did not complete."); return; }
                // Sans editeur, un assemblage incomplet ne peut pas aller plus loin que l'apercu.
                if (reached < a.StageIndex && _workflow.UnlinkedMessage is { } unlinked) { FailAutomation(unlinked); return; }
                if (reached < a.StageIndex) { _ = _workflow.NextAsync(); return; }
                _autoPhase = 2;
                break;
            case 2:
                if (_workflow.Preview is null || ReferenceEquals(_workflow.Preview, _failedPreview)) { FailAutomation($"The view could not be displayed. (preview null={_workflow.Preview is null}, stage={_workflow.Stage}, err={_workflow.LastError})"); return; }
                if (!ReferenceEquals(_workflow.Preview, _renderer.Model)) return;
                if (a.CameraPosition is { } p)
                {
                    _camera.Position = new Vector3(p.X, p.Y, p.Z);
                    _camera.Yaw = a.CameraYaw;
                    _camera.Pitch = a.CameraPitch;
                }
                else if (_workflow.Preview?.Spawn is { } sp)
                {
                    // Hors du cube du point d apparition : 64 unites en arriere du regard, 48 plus haut, vers le point.
                    float yaw = _workflow.Preview.SpawnYaw;
                    _camera.Yaw = yaw;
                    _camera.Pitch = -MathHelper.ToDegrees(MathF.Atan2(48f, 64f));
                    float yr = MathHelper.ToRadians(yaw);
                    _camera.Position = new Vector3(sp.X, sp.Y, sp.Z) - new Vector3(MathF.Cos(yr), MathF.Sin(yr), 0f) * 64f + new Vector3(0, 0, 48f);
                }
                if (a.Game == Kotor.Core.KotorGame.Swtor && !_screen.SwtorIdle) return;   // catalogue et rooms visibles sur la capture
                _autoFrames = 0;
                _autoPhase = 3;
                break;
        }
    }

    private void FinishAutomation()
    {
        _autoPhase = 4;
        AutomationOptions a = _auto!;
        if (a.ScreenshotPath is { } path)
        {
            try
            {
                PresentationParameters pp = GraphicsDevice.PresentationParameters;
                int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
                var data = new byte[w * h * 4];
                GraphicsDevice.GetBackBufferData(data);
                for (int i = 3; i < data.Length; i += 4) data[i] = 255;
                string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (dir is not null) Directory.CreateDirectory(dir);
                using var fs = File.Create(path);
                Kotor.Core.Export.PngWriter.WriteRgba(fs, w, h, data);
                Console.WriteLine($"Screenshot written: {Path.GetFullPath(path)} ({w}x{h})");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                FailAutomation("Screenshot failed: " + e.Message);
                return;
            }
        }
        if (a.Exit) Exit();
    }

    private void FailAutomation(string message)
    {
        _autoPhase = 4;
        Console.Error.WriteLine("Error: " + message);
        if (_auto!.Exit) { ExitCode = 1; Exit(); }
    }

    protected override void UnloadContent()
    {
        _renderer.Dispose();
        _placement.Dispose();
        base.UnloadContent();
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 40, 60));
        PresentationParameters pp = GraphicsDevice.PresentationParameters;
        int left = (int)MainScreen.LeftWidth;
        PlacementEditor editor = _screen.Editor;
        if (editor.IsOpen)
        {
            if (editor.Shows3D)
            {
                var r = editor.ViewRect;
                var v3 = new Viewport(r.X, r.Y, Math.Max(1, r.Width), Math.Max(1, r.Height)) { MinDepth = 0f, MaxDepth = 1f };
                _placement.Draw(_editorCamera, v3, editor.Selected, editor.ChosenDoors);
            }
        }
        else
        {
            var viewport = new Viewport(left, 0, Math.Max(1, pp.BackBufferWidth - left), Math.Max(1, pp.BackBufferHeight)) { MinDepth = 0f, MaxDepth = 1f };
            _renderer.Draw(_camera, _options, viewport);
        }

        _imgui.BeforeLayout(gameTime);
        if (!editor.IsOpen) _renderer.DrawLabels(_options);
        _screen.Draw(ImGui.GetIO().DisplaySize, _renderer);
        _imgui.AfterLayout();
        if (_autoPhase == 3 && ++_autoFrames >= 3) FinishAutomation();
        base.Draw(gameTime);
    }
}

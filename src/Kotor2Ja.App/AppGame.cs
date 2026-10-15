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
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            GraphicsProfile = GraphicsProfile.HiDef,
        };
        Window.AllowUserResizing = true;
        Window.Title = "KOTOR → Jedi Academy";
        IsMouseVisible = true;
    }

    private PreviewModel? _failedPreview;

    protected override void Initialize()
    {
        _imgui = new ImGuiRenderer(this);
        _imgui.RebuildFontAtlas();
        _renderer = new PreviewRenderer(GraphicsDevice);

        // Meme fichier de configuration que la CLI, copie a cote de l'executable.
        string configPath = Path.Combine(AppContext.BaseDirectory, "kotor2ja.config.json");
        ExportOptions options;
        string? configWarning = null;
        try
        {
            options = ConfigLoader.Load(configPath);
            if (!File.Exists(configPath)) configWarning = $"AVERTISSEMENT : {configPath} absent, valeurs par défaut utilisées.";
            else if (options.Validate() is { } bad) { configWarning = $"ERREUR : configuration invalide ({configPath}) : {bad}"; options = ConfigLoader.Defaults(); }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            configWarning = $"ERREUR : configuration illisible ({configPath}) : {e.Message} Valeurs par défaut utilisées.";
            options = ConfigLoader.Defaults();
        }
        _workflow = new WorkflowController(new KotorWorkflowSteps(options));
        _screen = new MainScreen(_workflow, _options);
        if (_auto is not null) _screen.LogSink = Console.WriteLine;
        if (configWarning is not null) _screen.AddLog(configWarning);
        _screen.AddLog($"KOTOR : {options.KotorDir} (échelle {options.Scale:0.##})");
        _screen.AddLog($"KOTOR 2 : {(string.IsNullOrWhiteSpace(options.Kotor2Dir) ? "(not configured)" : options.Kotor2Dir)}");

        // La liste des modules lit les noms de zone un par un : hors du fil d interface. Le mode automatise l attend avant la capture.
        _modulesTask = Task.Run(() =>
        {
            try
            {
                IReadOnlyList<ModuleListItem> modules = _workflow.Modules();
                _screen.SetModules(modules);
                _screen.AddLog($"{modules.Count} module(s) trouvé(s).");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _screen.SetModules([]);
                _screen.AddLog("ERREUR : impossible de lister les modules : " + e.Message);
            }
        });

        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        var io = ImGui.GetIO();
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
                _screen.AddLog("ERREUR : affichage de la vue : " + e.Message);
            }
        }

        if (_auto is not null) StepAutomation();

        int left = (int)MainScreen.LeftWidth;
        var center = new Point(left + (Window.ClientBounds.Width - left) / 2, Window.ClientBounds.Height / 2);
        _camera.Update((float)gameTime.ElapsedGameTime.TotalSeconds, kb, mouse, _lastWheel,
            io.WantCaptureMouse && !_looking, io.WantTextInput, IsActive, center, out _looking);
        _lastWheel = mouse.ScrollWheelValue;
        IsMouseVisible = !_looking;
        base.Update(gameTime);
    }

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
                if (!_workflow.SelectModule(a.Game, a.Module)) { FailAutomation("Could not select the module."); return; }
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
                if (reached < a.StageIndex) { _ = _workflow.NextAsync(); return; }
                _autoPhase = 2;
                break;
            case 2:
                if (_workflow.Preview is null || ReferenceEquals(_workflow.Preview, _failedPreview)) { FailAutomation("The view could not be displayed."); return; }
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
        base.UnloadContent();
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(30, 40, 60));
        PresentationParameters pp = GraphicsDevice.PresentationParameters;
        int left = (int)MainScreen.LeftWidth;
        var viewport = new Viewport(left, 0, Math.Max(1, pp.BackBufferWidth - left), Math.Max(1, pp.BackBufferHeight)) { MinDepth = 0f, MaxDepth = 1f };
        _renderer.Draw(_camera, _options, viewport);

        _imgui.BeforeLayout(gameTime);
        _renderer.DrawLabels(_options);
        _screen.Draw(ImGui.GetIO().DisplaySize, _renderer);
        _imgui.AfterLayout();
        if (_autoPhase == 3 && ++_autoFrames >= 3) FinishAutomation();
        base.Draw(gameTime);
    }
}

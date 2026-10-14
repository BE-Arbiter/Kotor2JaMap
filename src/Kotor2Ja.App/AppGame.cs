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

    public AppGame()
    {
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

    protected override void Initialize()
    {
        _imgui = new ImGuiRenderer(this);
        _imgui.RebuildFontAtlas();
        _renderer = new PreviewRenderer(GraphicsDevice);

        // Meme fichier de configuration que la CLI, copie a cote de l'executable.
        string configPath = Path.Combine(AppContext.BaseDirectory, "kotor2ja.config.json");
        ExportOptions options = ConfigLoader.Load(configPath);
        _workflow = new WorkflowController(new KotorWorkflowSteps(options));
        _screen = new MainScreen(_workflow, _options);
        _screen.AddLog($"KOTOR : {options.KotorDir} (échelle {options.Scale:0.##})");

        // La liste des modules lit les noms de zone un par un : hors du fil d'interface.
        Task.Run(() =>
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
        if (!ReferenceEquals(preview, _renderer.Model))
        {
            try
            {
                _renderer.Upload(preview);
                if (preview is not null) FrameIfNeeded(preview);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _renderer.Upload(null);
                _screen.AddLog("ERREUR : affichage de la vue : " + e.Message);
            }
        }

        int left = (int)MainScreen.LeftWidth;
        var center = new Point(left + (Window.ClientBounds.Width - left) / 2, Window.ClientBounds.Height / 2);
        _camera.Update((float)gameTime.ElapsedGameTime.TotalSeconds, kb, mouse, _lastWheel,
            io.WantCaptureMouse && !_looking, io.WantCaptureKeyboard, IsActive, center, out _looking);
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
        base.Draw(gameTime);
    }
}

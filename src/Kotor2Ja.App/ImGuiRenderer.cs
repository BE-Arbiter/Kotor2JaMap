// Adapte de l'exemple MonoGame du depot ImGui.NET (https://github.com/ImGuiNET/ImGui.NET,
// ImGui.NET.SampleProgram.XNA/ImGuiRenderer.cs), licence MIT. Voir THIRD-PARTY-NOTICES.md.
using System.Runtime.InteropServices;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Kotor2Ja.App;

/// <summary>Moteur de rendu ImGui pour MonoGame.</summary>
public sealed class ImGuiRenderer
{
    private readonly Game _game;
    private readonly GraphicsDevice _gd;
    private BasicEffect? _effect;
    private readonly RasterizerState _rasterizer;
    private byte[]? _vertexData;
    private VertexBuffer? _vertexBuffer;
    private int _vertexBufferSize;
    private byte[]? _indexData;
    private IndexBuffer? _indexBuffer;
    private int _indexBufferSize;
    private readonly Dictionary<IntPtr, Texture2D> _loadedTextures = new();
    private int _textureId = 1;
    private IntPtr? _fontTextureId;
    private int _scrollWheel;

    public unsafe ImGuiRenderer(Game game)
    {
        _game = game;
        _gd = game.GraphicsDevice;
        ImGui.SetCurrentContext(ImGui.CreateContext());
        _rasterizer = new RasterizerState
        {
            CullMode = CullMode.None,
            DepthBias = 0,
            FillMode = FillMode.Solid,
            MultiSampleAntiAlias = false,
            ScissorTestEnable = true,
            SlopeScaleDepthBias = 0,
        };
        var io = ImGui.GetIO();
        unsafe { io.NativePtr->IniFilename = null; }  // pas de imgui.ini : disposition fixe
        // Pas de navigation clavier : aucune touche ne peut activer un bouton d'etape (Importer, Exporter, Creer PK3).
        io.ConfigFlags &= ~(ImGuiConfigFlags.NavEnableKeyboard | ImGuiConfigFlags.NavEnableGamepad);
        game.Window.TextInput += (_, e) =>
        {
            if (e.Character != '\t') io.AddInputCharacter(e.Character);
        };
        io.Fonts.AddFontDefault();
    }

    public unsafe void RebuildFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixelData, out int width, out int height, out int bytesPerPixel);
        var pixels = new byte[width * height * bytesPerPixel];
        Marshal.Copy(new IntPtr(pixelData), pixels, 0, pixels.Length);
        var tex = new Texture2D(_gd, width, height, false, SurfaceFormat.Color);
        tex.SetData(pixels);
        if (_fontTextureId.HasValue) UnbindTexture(_fontTextureId.Value);
        _fontTextureId = BindTexture(tex);
        io.Fonts.SetTexID(_fontTextureId.Value);
        io.Fonts.ClearTexData();
    }

    public IntPtr BindTexture(Texture2D texture)
    {
        var id = new IntPtr(_textureId++);
        _loadedTextures.Add(id, texture);
        return id;
    }

    public void UnbindTexture(IntPtr textureId) => _loadedTextures.Remove(textureId);

    public void BeforeLayout(GameTime gameTime)
    {
        var io = ImGui.GetIO();
        io.DeltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (io.DeltaTime <= 0f) io.DeltaTime = 1f / 60f;
        UpdateInput(io);
        ImGui.NewFrame();
    }

    public void AfterLayout()
    {
        ImGui.Render();
        RenderDrawData(ImGui.GetDrawData());
    }

    private void UpdateInput(ImGuiIOPtr io)
    {
        io.DisplaySize = new System.Numerics.Vector2(
            _gd.PresentationParameters.BackBufferWidth, _gd.PresentationParameters.BackBufferHeight);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);
        if (!_game.IsActive) return;

        var mouse = Mouse.GetState();
        var keyboard = Keyboard.GetState();

        foreach (var (xk, ik) in KeyMap)
            io.AddKeyEvent(ik, keyboard.IsKeyDown(xk));
        io.AddKeyEvent(ImGuiKey.ModCtrl, keyboard.IsKeyDown(Keys.LeftControl) || keyboard.IsKeyDown(Keys.RightControl));
        io.AddKeyEvent(ImGuiKey.ModShift, keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));
        io.AddKeyEvent(ImGuiKey.ModAlt, keyboard.IsKeyDown(Keys.LeftAlt) || keyboard.IsKeyDown(Keys.RightAlt));

        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        int scroll = mouse.ScrollWheelValue - _scrollWheel;
        _scrollWheel = mouse.ScrollWheelValue;
        if (scroll != 0) io.AddMouseWheelEvent(0, scroll / 120f);
    }

    private static readonly (Keys, ImGuiKey)[] KeyMap =
    {
        (Keys.Tab, ImGuiKey.Tab), (Keys.Left, ImGuiKey.LeftArrow), (Keys.Right, ImGuiKey.RightArrow),
        (Keys.Up, ImGuiKey.UpArrow), (Keys.Down, ImGuiKey.DownArrow), (Keys.PageUp, ImGuiKey.PageUp),
        (Keys.PageDown, ImGuiKey.PageDown), (Keys.Home, ImGuiKey.Home), (Keys.End, ImGuiKey.End),
        (Keys.Delete, ImGuiKey.Delete), (Keys.Back, ImGuiKey.Backspace), 
        (Keys.Escape, ImGuiKey.Escape),
        (Keys.A, ImGuiKey.A), (Keys.C, ImGuiKey.C), (Keys.V, ImGuiKey.V), (Keys.X, ImGuiKey.X),
        (Keys.Y, ImGuiKey.Y), (Keys.Z, ImGuiKey.Z),
    };

    private void RenderDrawData(ImDrawDataPtr drawData)
    {
        var lastViewport = _gd.Viewport;
        var lastScissor = _gd.ScissorRectangle;
        var lastBlend = _gd.BlendState;
        var lastDepth = _gd.DepthStencilState;
        var lastRaster = _gd.RasterizerState;

        _gd.BlendFactor = Color.White;
        _gd.BlendState = BlendState.NonPremultiplied;
        _gd.RasterizerState = _rasterizer;
        _gd.DepthStencilState = DepthStencilState.DepthRead;

        if (drawData.CmdListsCount > 0 && drawData.TotalVtxCount > 0)
        {
            UpdateBuffers(drawData);
            RenderCommandLists(drawData);
        }

        _gd.Viewport = lastViewport;
        _gd.ScissorRectangle = lastScissor;
        _gd.BlendState = lastBlend;
        _gd.DepthStencilState = lastDepth;
        _gd.RasterizerState = lastRaster;
    }

    private unsafe void UpdateBuffers(ImDrawDataPtr drawData)
    {
        if (drawData.TotalVtxCount > _vertexBufferSize)
        {
            _vertexBuffer?.Dispose();
            _vertexBufferSize = (int)(drawData.TotalVtxCount * 1.5f);
            _vertexBuffer = new VertexBuffer(_gd, DrawVertDeclaration.Declaration, _vertexBufferSize, BufferUsage.None);
            _vertexData = new byte[_vertexBufferSize * DrawVertDeclaration.Size];
        }
        if (drawData.TotalIdxCount > _indexBufferSize)
        {
            _indexBuffer?.Dispose();
            _indexBufferSize = (int)(drawData.TotalIdxCount * 1.5f);
            _indexBuffer = new IndexBuffer(_gd, IndexElementSize.SixteenBits, _indexBufferSize, BufferUsage.None);
            _indexData = new byte[_indexBufferSize * sizeof(ushort)];
        }

        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            fixed (void* vtxDst = &_vertexData![vtxOffset * DrawVertDeclaration.Size])
            fixed (void* idxDst = &_indexData![idxOffset * sizeof(ushort)])
            {
                Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vtxDst,
                    _vertexData.Length - vtxOffset * DrawVertDeclaration.Size,
                    cmdList.VtxBuffer.Size * DrawVertDeclaration.Size);
                Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, idxDst,
                    _indexData.Length - idxOffset * sizeof(ushort),
                    cmdList.IdxBuffer.Size * sizeof(ushort));
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }

        _vertexBuffer!.SetData(_vertexData!, 0, drawData.TotalVtxCount * DrawVertDeclaration.Size);
        _indexBuffer!.SetData(_indexData!, 0, drawData.TotalIdxCount * sizeof(ushort));
    }

    private void RenderCommandLists(ImDrawDataPtr drawData)
    {
        _gd.SetVertexBuffer(_vertexBuffer);
        _gd.Indices = _indexBuffer;

        var io = ImGui.GetIO();
        _effect ??= new BasicEffect(_gd);
        _effect.World = Matrix.Identity;
        _effect.View = Matrix.Identity;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0f, io.DisplaySize.X, io.DisplaySize.Y, 0f, -1f, 1f);
        _effect.TextureEnabled = true;
        _effect.VertexColorEnabled = true;

        int vtxOffset = 0, idxOffset = 0;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var cmdList = drawData.CmdLists[n];
            for (int cmdi = 0; cmdi < cmdList.CmdBuffer.Size; cmdi++)
            {
                var cmd = cmdList.CmdBuffer[cmdi];
                if (cmd.ElemCount == 0) continue;
                if (!_loadedTextures.TryGetValue((IntPtr)cmd.TextureId, out var tex))
                    throw new InvalidOperationException($"Texture ImGui inconnue : {cmd.TextureId}");

                _gd.ScissorRectangle = new Rectangle(
                    (int)cmd.ClipRect.X, (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X), (int)(cmd.ClipRect.W - cmd.ClipRect.Y));
                _effect.Texture = tex;
                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList,
                        vtxOffset + (int)cmd.VtxOffset, idxOffset + (int)cmd.IdxOffset,
                        (int)cmd.ElemCount / 3);
                }
            }
            vtxOffset += cmdList.VtxBuffer.Size;
            idxOffset += cmdList.IdxBuffer.Size;
        }
    }

    private static class DrawVertDeclaration
    {
        public static readonly VertexDeclaration Declaration;
        public static readonly int Size;

        static DrawVertDeclaration()
        {
            unsafe { Size = sizeof(ImDrawVert); }
            Declaration = new VertexDeclaration(Size,
                new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
                new VertexElement(8, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
                new VertexElement(16, VertexElementFormat.Color, VertexElementUsage.Color, 0));
        }
    }
}

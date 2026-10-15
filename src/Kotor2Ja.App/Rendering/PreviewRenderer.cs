using System.Runtime.InteropServices;
using ImGuiNET;
using Kotor.Core.Entities;
using Kotor.Pipeline.Preview;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;
using XnaVector4 = Microsoft.Xna.Framework.Vector4;
using GfxViewport = Microsoft.Xna.Framework.Graphics.Viewport;

namespace Kotor2Ja.App.Rendering;

/// <summary>Options d'affichage choisies dans la vue.</summary>
public sealed class ViewOptions
{
    public bool Collisions;
    public bool Sky;            // boite de ciel du compilateur : cachee par defaut (elle enferme toute la carte)
    public bool BothSides;      // desactive le culling de toutes les faces
    public readonly HashSet<EntityKind> HiddenKinds = [];
}

/// <summary>Sommet a deux jeux d'UV pour DualTextureEffect (diffuse x lightmap).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct VertexDual(XnaVector3 position, XnaVector2 uv0, XnaVector2 uv1) : IVertexType
{
    public XnaVector3 Position = position;
    public XnaVector2 Uv0 = uv0;
    public XnaVector2 Uv1 = uv1;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(20, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 1));

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}

/// <summary>
/// Dessine un <see cref="PreviewModel"/> : lots (DualTextureEffect avec lightmap, BasicEffect sinon, AlphaTestEffect pour Cutout),
/// collisions en lignes, reperes en petits cubes. Tout est envoye au GPU par <see cref="Upload"/> dans le fil de rendu.
/// </summary>
public sealed class PreviewRenderer : IDisposable
{
    private const float MarkerSize = 16f;
    private const float LabelDistance = 900f;
    private static readonly XnaColor SkyColor = new(18, 28, 70);

    private static readonly RasterizerState CullBack = new() { CullMode = CullMode.CullClockwiseFace };
    private static readonly RasterizerState CullNone = RasterizerState.CullNone;
    private static readonly RasterizerState LineRaster = new() { CullMode = CullMode.None, DepthBias = -0.00002f, SlopeScaleDepthBias = -1f };

    private sealed class GpuBatch
    {
        public required PreviewBatch Source;
        public required VertexBuffer Vb;
        public required IndexBuffer Ib;
        public required bool Dual;
        public required Texture2D Diffuse;
        public Texture2D? Lightmap;
        public int Triangles;
    }

    private readonly GraphicsDevice _gd;
    private readonly BasicEffect _basic;
    private readonly AlphaTestEffect _alphaTest;
    private readonly DualTextureEffect _dual;
    private readonly BasicEffect _flat; // couleurs de sommet, sans texture
    private readonly Dictionary<string, Texture2D> _textures = [];
    private readonly List<GpuBatch> _batches = [];
    private readonly Dictionary<string, List<GpuBatch>> _modelBatches = [];
    private readonly List<(EntityKind Kind, Matrix World, List<GpuBatch> Batches)> _instances = [];
    private readonly Dictionary<EntityKind, VertexBuffer> _markerCubes = [];
    private readonly Dictionary<EntityKind, int> _markerCubeCounts = [];
    private readonly Dictionary<EntityKind, VertexBuffer> _markerLines = [];
    private readonly Dictionary<EntityKind, int> _markerLineCounts = [];
    private VertexBuffer? _collisionLines;
    private int _collisionCount;
    private VertexBuffer? _spawnCube, _spawnLines;
    private int _spawnCubeCount, _spawnLineCount;
    private PreviewModel? _model;
    private Texture2D? _fallback;
    private Matrix _view, _proj;
    private GfxViewport _viewport;
    private XnaVector3 _cameraPos;

    public PreviewRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        _basic = new BasicEffect(gd) { TextureEnabled = true, LightingEnabled = false, VertexColorEnabled = false };
        _alphaTest = new AlphaTestEffect(gd) { AlphaFunction = CompareFunction.Greater, ReferenceAlpha = 128 };
        _dual = new DualTextureEffect(gd);
        _flat = new BasicEffect(gd) { TextureEnabled = false, LightingEnabled = false, VertexColorEnabled = true };
    }

    /// <summary>Le modele actuellement envoye au GPU (comparaison par reference).</summary>
    public PreviewModel? Model => _model;

    public IReadOnlyCollection<EntityKind> KindsPresent => _markerCubes.Keys.Concat(_instances.Select(i => i.Kind)).Distinct().ToList();
    public bool HasSky => _batches.Any(b => b.Source.IsSky);

    /// <summary>Remplace tout le contenu GPU par celui du modele (null = vide). A appeler depuis le fil de rendu.</summary>
    public void Upload(PreviewModel? model)
    {
        Clear();
        _model = model;
        if (model is null) return;

        foreach (var (key, tex) in model.Textures) _textures[key] = MakeTexture(tex);
        Texture2D missing = _textures.TryGetValue(PreviewModel.MissingTextureKey, out var m) ? m : Fallback();

        foreach (PreviewBatch b in model.Batches)
            if (MakeGpuBatch(b, missing) is { } gb) _batches.Add(gb);

        // Modeles MD3 (objets et portes) : un jeu de tampons par modele, dessine une fois par instance.
        foreach (var (key, list) in model.Models)
        {
            var gpu = new List<GpuBatch>();
            foreach (PreviewBatch b in list)
                if (MakeGpuBatch(b, missing) is { } gb) gpu.Add(gb);
            _modelBatches[key] = gpu;
        }
        foreach (PreviewModelInstance inst in model.Instances)
            if (_modelBatches.TryGetValue(inst.ModelKey, out var gpuBatches) && gpuBatches.Count > 0)
                _instances.Add((inst.Kind, ToXna(inst.Transform), gpuBatches));

        UploadOverlays(model);
    }

    private GpuBatch? MakeGpuBatch(PreviewBatch b, Texture2D missing)
    {
        {
            if (b.Vertices.Length == 0 || b.Indices.Length == 0) return null;
            Texture2D diffuse = _textures.TryGetValue(b.DiffuseKey, out var d) ? d : missing;
            Texture2D? lm = b.LightmapKey is not null && _textures.TryGetValue(b.LightmapKey, out var l) ? l : null;
            bool dual = lm is not null && !b.IsSky;
            VertexBuffer vb;
            if (dual)
            {
                var v = new VertexDual[b.Vertices.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    PreviewVertex s = b.Vertices[i];
                    v[i] = new VertexDual(ToXna(s.Position), new(s.Uv0.X, s.Uv0.Y), new(s.Uv1.X, s.Uv1.Y));
                }
                vb = new VertexBuffer(_gd, VertexDual.Declaration, v.Length, BufferUsage.WriteOnly);
                vb.SetData(v);
            }
            else
            {
                var v = new VertexPositionTexture[b.Vertices.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    PreviewVertex s = b.Vertices[i];
                    v[i] = new VertexPositionTexture(ToXna(s.Position), new(s.Uv0.X, s.Uv0.Y));
                }
                vb = new VertexBuffer(_gd, VertexPositionTexture.VertexDeclaration, v.Length, BufferUsage.WriteOnly);
                vb.SetData(v);
            }
            var ib = new IndexBuffer(_gd, IndexElementSize.ThirtyTwoBits, b.Indices.Length, BufferUsage.WriteOnly);
            ib.SetData(b.Indices);
            return new GpuBatch
            {
                Source = b, Vb = vb, Ib = ib, Dual = dual, Diffuse = diffuse, Lightmap = dual ? lm : null,
                Triangles = b.Indices.Length / 3,
            };
        }
    }

    private void UploadOverlays(PreviewModel model)
    {
        if (model.CollisionTriangles.Length >= 3)
        {
            var lines = new List<VertexPositionColor>(model.CollisionTriangles.Length * 2);
            var c = new XnaColor(80, 255, 120);
            for (int i = 0; i + 2 < model.CollisionTriangles.Length; i += 3)
            {
                XnaVector3 a = ToXna(model.CollisionTriangles[i]), b = ToXna(model.CollisionTriangles[i + 1]), d = ToXna(model.CollisionTriangles[i + 2]);
                lines.Add(new(a, c)); lines.Add(new(b, c));
                lines.Add(new(b, c)); lines.Add(new(d, c));
                lines.Add(new(d, c)); lines.Add(new(a, c));
            }
            _collisionLines = MakeColorBuffer(lines);
            _collisionCount = lines.Count / 2;
        }

        var cubes = new Dictionary<EntityKind, List<VertexPositionColor>>();
        var outlines = new Dictionary<EntityKind, List<VertexPositionColor>>();
        foreach (PreviewMarker mk in model.Markers)
        {
            XnaColor col = KindColor(mk.Kind);
            if (!cubes.TryGetValue(mk.Kind, out var cl)) cubes[mk.Kind] = cl = [];
            AddCube(cl, ToXna(mk.Position), MarkerSize, col);
            if (mk.Polygon is { Length: > 1 } poly)
            {
                if (!outlines.TryGetValue(mk.Kind, out var ol)) outlines[mk.Kind] = ol = [];
                for (int i = 0; i < poly.Length; i++)
                {
                    ol.Add(new(ToXna(poly[i]), col));
                    ol.Add(new(ToXna(poly[(i + 1) % poly.Length]), col));
                }
            }
        }
        foreach (var (kind, list) in cubes) { _markerCubes[kind] = MakeColorBuffer(list); _markerCubeCounts[kind] = list.Count / 3; }
        foreach (var (kind, list) in outlines) { _markerLines[kind] = MakeColorBuffer(list); _markerLineCounts[kind] = list.Count / 2; }

        if (model.Spawn is { } sp)
        {
            var yellow = new XnaColor(255, 230, 40);
            var cl = new List<VertexPositionColor>();
            XnaVector3 p = ToXna(sp);
            AddCube(cl, p, MarkerSize * 1.5f, yellow);
            _spawnCube = MakeColorBuffer(cl); _spawnCubeCount = cl.Count / 3;
            float yaw = MathHelper.ToRadians(model.SpawnYaw);
            XnaVector3 dir = new(MathF.Cos(yaw), MathF.Sin(yaw), 0f);
            XnaVector3 tip = p + dir * 80f, side = new XnaVector3(-dir.Y, dir.X, 0f) * 12f;
            var ll = new List<VertexPositionColor>
            {
                new(p, yellow), new(tip, yellow),
                new(tip, yellow), new(tip - dir * 20f + side, yellow),
                new(tip, yellow), new(tip - dir * 20f - side, yellow),
            };
            _spawnLines = MakeColorBuffer(ll); _spawnLineCount = ll.Count / 2;
        }
    }

    private static Matrix ToXna(System.Numerics.Matrix4x4 m) => new(m.M11, m.M12, m.M13, m.M14, m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34, m.M41, m.M42, m.M43, m.M44);

    private VertexBuffer MakeColorBuffer(List<VertexPositionColor> v)
    {
        var vb = new VertexBuffer(_gd, VertexPositionColor.VertexDeclaration, v.Count, BufferUsage.WriteOnly);
        vb.SetData(v.ToArray());
        return vb;
    }

    /// <summary>Dessine la scene dans <paramref name="viewport"/> (portion de la fenetre).</summary>
    public void Draw(FreeCamera camera, ViewOptions opt, GfxViewport viewport)
    {
        _viewport = viewport;
        _view = camera.View;
        _proj = camera.Projection(viewport.AspectRatio);
        _cameraPos = camera.Position;
        if (_model is null) return;

        GfxViewport saved = _gd.Viewport;
        _gd.Viewport = viewport;
        try
        {
            _gd.SamplerStates[0] = SamplerState.LinearWrap;
            _gd.SamplerStates[1] = SamplerState.LinearClamp;
            // Opaques, cutouts et ciel d'abord, puis les lots transparents sans ecriture de profondeur.
            foreach (GpuBatch b in _batches)
                if (b.Source.Blend is PreviewBlend.Opaque or PreviewBlend.Cutout && Visible(b, opt)) DrawBatch(b, opt, Matrix.Identity);
            foreach (var (kind, world, batches) in _instances)
                if (!opt.HiddenKinds.Contains(kind))
                    foreach (GpuBatch b in batches) DrawBatch(b, opt, world);
            foreach (GpuBatch b in _batches)
                if (b.Source.Blend is PreviewBlend.Additive or PreviewBlend.Alpha && Visible(b, opt)) DrawBatch(b, opt, Matrix.Identity);

            _flat.World = Matrix.Identity; _flat.View = _view; _flat.Projection = _proj;
            _flat.TextureEnabled = false; _flat.VertexColorEnabled = true; _flat.DiffuseColor = XnaVector3.One;
            _gd.BlendState = BlendState.Opaque;
            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.RasterizerState = CullNone;
            foreach (var (kind, vb) in _markerCubes)
                if (!opt.HiddenKinds.Contains(kind)) DrawColor(vb, _markerCubeCounts[kind], PrimitiveType.TriangleList);
            foreach (var (kind, vb) in _markerLines)
                if (!opt.HiddenKinds.Contains(kind)) DrawColor(vb, _markerLineCounts[kind], PrimitiveType.LineList);
            if (_spawnCube is not null) DrawColor(_spawnCube, _spawnCubeCount, PrimitiveType.TriangleList);
            if (_spawnLines is not null) DrawColor(_spawnLines, _spawnLineCount, PrimitiveType.LineList);

            if (opt.Collisions && _collisionLines is not null)
            {
                _gd.DepthStencilState = DepthStencilState.DepthRead;
                _gd.RasterizerState = LineRaster;
                DrawColor(_collisionLines, _collisionCount, PrimitiveType.LineList);
            }
        }
        finally { _gd.Viewport = saved; }
    }

    private static bool Visible(GpuBatch b, ViewOptions opt) => !b.Source.IsSky || opt.Sky;

    private void DrawBatch(GpuBatch b, ViewOptions opt, Matrix world)
    {
        PreviewBatch s = b.Source;
        _gd.RasterizerState = s.DoubleSided || s.IsSky || opt.BothSides ? CullNone : CullBack;
        bool blended = s.Blend is PreviewBlend.Additive or PreviewBlend.Alpha;
        _gd.BlendState = s.Blend switch
        {
            PreviewBlend.Additive => AddBlend,
            PreviewBlend.Alpha => BlendState.NonPremultiplied,
            _ => BlendState.Opaque,
        };
        _gd.DepthStencilState = blended ? DepthStencilState.DepthRead : DepthStencilState.Default;
        _gd.SetVertexBuffer(b.Vb);
        _gd.Indices = b.Ib;

        Effect effect;
        if (s.IsSky)
        {
            // Ciel : aplat bleu sombre, sans texture (le shader de ciel n'a pas de diffuse utile).
            _flat.World = world; _flat.View = _view; _flat.Projection = _proj;
            _flat.VertexColorEnabled = false; _flat.TextureEnabled = false; _flat.DiffuseColor = SkyColor.ToVector3();
            effect = _flat;
        }
        else if (b.Dual && s.Blend == PreviewBlend.Cutout)
        {
            // Cutout + lightmap, deux passes : 1) profondeur seule des texels opaques (alpha test), 2) diffuse x lightmap
            // sur exactement ces pixels (profondeur Equal, sans ecriture).
            _alphaTest.World = world; _alphaTest.View = _view; _alphaTest.Projection = _proj;
            _alphaTest.Texture = b.Diffuse;
            _gd.BlendState = DepthOnly;
            _gd.DepthStencilState = DepthStencilState.Default;
            foreach (EffectPass pass in _alphaTest.CurrentTechnique.Passes)
            {
                pass.Apply();
                _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, b.Triangles);
            }
            _gd.BlendState = BlendState.Opaque;
            _gd.DepthStencilState = DepthEqual;
            _dual.World = world; _dual.View = _view; _dual.Projection = _proj;
            _dual.Texture = b.Diffuse; _dual.Texture2 = b.Lightmap;
            effect = _dual;
        }
        else if (b.Dual)
        {
            _dual.World = world; _dual.View = _view; _dual.Projection = _proj;
            _dual.Texture = b.Diffuse; _dual.Texture2 = b.Lightmap;
            effect = _dual;
        }
        else if (s.Blend == PreviewBlend.Cutout)
        {
            _alphaTest.World = world; _alphaTest.View = _view; _alphaTest.Projection = _proj;
            _alphaTest.Texture = b.Diffuse;
            effect = _alphaTest;
        }
        else
        {
            _basic.World = world; _basic.View = _view; _basic.Projection = _proj;
            _basic.Texture = b.Diffuse;
            effect = _basic;
        }
        foreach (EffectPass pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, b.Triangles);
        }
    }

    private void DrawColor(VertexBuffer vb, int primitives, PrimitiveType type)
    {
        if (primitives == 0) return;
        _gd.SetVertexBuffer(vb);
        foreach (EffectPass pass in _flat.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawPrimitives(type, 0, primitives);
        }
    }

    /// <summary>Ecrit le nom des reperes proches (ImGui, a la position projetee). A appeler pendant la construction de l'interface.</summary>
    public void DrawLabels(ViewOptions opt)
    {
        if (_model is null) return;
        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        Matrix vp = _view * _proj;
        float maxSq = LabelDistance * LabelDistance;
        int drawn = 0;
        foreach (PreviewMarker mk in _model.Markers)
        {
            if (opt.HiddenKinds.Contains(mk.Kind) || string.IsNullOrEmpty(mk.Label)) continue;
            XnaColor c = KindColor(mk.Kind);
            if (!Label(dl, vp, ToXna(mk.Position) + new XnaVector3(0, 0, MarkerSize), maxSq, c, mk.Label)) continue;
            if (++drawn >= 300) break;
        }
        if (_model.Spawn is { } sp)
            Label(dl, vp, ToXna(sp) + new XnaVector3(0, 0, MarkerSize * 2), maxSq * 4, new XnaColor(255, 230, 40), "Apparition");
    }

    private bool Label(ImDrawListPtr dl, Matrix vp, XnaVector3 p, float maxSq, XnaColor c, string text)
    {
        if (XnaVector3.DistanceSquared(p, _cameraPos) > maxSq) return false;
        XnaVector4 clip = XnaVector4.Transform(new XnaVector4(p, 1f), vp);
        if (clip.W <= 0.01f) return false;
        float nx = clip.X / clip.W, ny = clip.Y / clip.W;
        if (nx < -1 || nx > 1 || ny < -1 || ny > 1) return false;
        float sx = _viewport.X + (nx * 0.5f + 0.5f) * _viewport.Width;
        float sy = _viewport.Y + (0.5f - ny * 0.5f) * _viewport.Height;
        uint col = ImGui.GetColorU32(new System.Numerics.Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
        dl.AddText(new System.Numerics.Vector2(sx, sy), col, text);
        return true;
    }

    public static XnaColor KindColor(EntityKind kind) => kind switch
    {
        EntityKind.Placeable => new XnaColor(230, 150, 60),
        EntityKind.Door => new XnaColor(200, 120, 255),
        EntityKind.Waypoint => new XnaColor(80, 200, 255),
        EntityKind.Trigger => new XnaColor(255, 90, 90),
        EntityKind.Creature => new XnaColor(120, 255, 120),
        EntityKind.Sound => new XnaColor(255, 255, 255),
        EntityKind.Store => new XnaColor(255, 200, 200),
        EntityKind.Encounter => new XnaColor(255, 140, 190),
        EntityKind.Camera => new XnaColor(170, 170, 170),
        EntityKind.Light => new XnaColor(255, 255, 140),
        _ => XnaColor.Gray,
    };

    private static XnaVector3 ToXna(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    private static void AddCube(List<VertexPositionColor> list, XnaVector3 c, float size, XnaColor col)
    {
        float h = size * 0.5f;
        XnaVector3[] p =
        [
            c + new XnaVector3(-h, -h, -h), c + new XnaVector3(h, -h, -h), c + new XnaVector3(h, h, -h), c + new XnaVector3(-h, h, -h),
            c + new XnaVector3(-h, -h, h), c + new XnaVector3(h, -h, h), c + new XnaVector3(h, h, h), c + new XnaVector3(-h, h, h),
        ];
        int[][] faces = [[0, 1, 2, 3], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7]];
        foreach (int[] f in faces)
        {
            list.Add(new(p[f[0]], col)); list.Add(new(p[f[1]], col)); list.Add(new(p[f[2]], col));
            list.Add(new(p[f[0]], col)); list.Add(new(p[f[2]], col)); list.Add(new(p[f[3]], col));
        }
    }

    private static readonly BlendState AddBlend = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One,
    };
    private static readonly BlendState DepthOnly = new() { ColorWriteChannels = ColorWriteChannels.None };
    private static readonly DepthStencilState DepthEqual = new()
    {
        DepthBufferEnable = true, DepthBufferWriteEnable = false, DepthBufferFunction = CompareFunction.Equal,
    };

    private Texture2D Fallback()
    {
        if (_fallback is null)
        {
            _fallback = new Texture2D(_gd, 2, 2, false, SurfaceFormat.Color);
            var m = new XnaColor(255, 0, 255); var k = new XnaColor(0, 0, 0);
            _fallback.SetData([m, k, k, m]);
        }
        return _fallback;
    }

    /// <summary>Texture GPU avec mipmaps (reduction par moyenne 2x2).</summary>
    private Texture2D MakeTexture(PreviewTexture t)
    {
        int w = Math.Max(1, t.Width), h = Math.Max(1, t.Height);
        if (t.Rgba.Length < w * h * 4) return Fallback();
        var tex = new Texture2D(_gd, w, h, true, SurfaceFormat.Color);
        byte[] cur = t.Rgba;
        int level = 0;
        while (true)
        {
            tex.SetData(level, null, cur, 0, w * h * 4);
            if (w == 1 && h == 1) break;
            int nw = Math.Max(1, w / 2), nh = Math.Max(1, h / 2);
            var next = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    int x0 = Math.Min(x * 2, w - 1), x1 = Math.Min(x * 2 + 1, w - 1);
                    int y0 = Math.Min(y * 2, h - 1), y1 = Math.Min(y * 2 + 1, h - 1);
                    for (int ch = 0; ch < 4; ch++)
                    {
                        int sum = cur[(y0 * w + x0) * 4 + ch] + cur[(y0 * w + x1) * 4 + ch] + cur[(y1 * w + x0) * 4 + ch] + cur[(y1 * w + x1) * 4 + ch];
                        next[(y * nw + x) * 4 + ch] = (byte)((sum + 2) / 4);
                    }
                }
            cur = next; w = nw; h = nh; level++;
        }
        return tex;
    }

    private void Clear()
    {
        foreach (GpuBatch b in _batches) { b.Vb.Dispose(); b.Ib.Dispose(); }
        _batches.Clear();
        foreach (List<GpuBatch> list in _modelBatches.Values)
            foreach (GpuBatch b in list) { b.Vb.Dispose(); b.Ib.Dispose(); }
        _modelBatches.Clear();
        _instances.Clear();
        foreach (Texture2D t in _textures.Values) if (!ReferenceEquals(t, _fallback)) t.Dispose();  // le damier partage est libere par Dispose()
        _textures.Clear();
        foreach (VertexBuffer v in _markerCubes.Values) v.Dispose();
        foreach (VertexBuffer v in _markerLines.Values) v.Dispose();
        _markerCubes.Clear(); _markerCubeCounts.Clear(); _markerLines.Clear(); _markerLineCounts.Clear();
        _collisionLines?.Dispose(); _collisionLines = null; _collisionCount = 0;
        _spawnCube?.Dispose(); _spawnCube = null;
        _spawnLines?.Dispose(); _spawnLines = null;
        _model = null;
    }

    public void Dispose()
    {
        Clear();
        _fallback?.Dispose();
        _basic.Dispose(); _alphaTest.Dispose(); _dual.Dispose(); _flat.Dispose();
    }
}

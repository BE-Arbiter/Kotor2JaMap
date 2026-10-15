using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Pipeline.Assembly;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using GfxViewport = Microsoft.Xna.Framework.Graphics.Viewport;

namespace Kotor2Ja.App.Rendering;

/// <summary>
/// Vue libre de l'editeur de placement : maillages de chaque module (repere du module) en aplat eclaire d'une couleur par module,
/// dessines avec la transformation courante de la session ; portes en petits cubes. Unites d'affichage : metres x <see cref="Scale"/>
/// (echelle JA par defaut, pour que la camera libre garde sa vitesse habituelle).
/// </summary>
public sealed class PlacementRenderer : IDisposable
{
    public const float Scale = ExportOptions.DefaultScale;
    private const float DoorSize = 0.6f;   // metres

    private static readonly RasterizerState CullNone = RasterizerState.CullNone;

    private readonly GraphicsDevice _gd;
    private readonly BasicEffect _lit;
    private readonly BasicEffect _flat;
    private readonly List<(VertexBuffer Vb, IndexBuffer Ib, int Triangles)>[] _empty = [];
    private List<(VertexBuffer Vb, IndexBuffer Ib, int Triangles)>[] _parts = [];
    private PlacementEditorModel? _model;

    public PlacementRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        _lit = new BasicEffect(gd) { TextureEnabled = false, VertexColorEnabled = false, LightingEnabled = true, PreferPerPixelLighting = false };
        _lit.EnableDefaultLighting();
        _flat = new BasicEffect(gd) { TextureEnabled = false, LightingEnabled = false, VertexColorEnabled = true };
    }

    public PlacementEditorModel? Model => _model;

    /// <summary>Distinct colour of a part (selection list, top view and 3D view).</summary>
    public static System.Numerics.Vector3 PartColor(int part)
    {
        System.Numerics.Vector3[] palette =
        [
            new(0.35f, 0.65f, 1f), new(1f, 0.6f, 0.25f), new(0.45f, 0.9f, 0.45f), new(0.9f, 0.45f, 0.9f),
            new(0.95f, 0.9f, 0.35f), new(0.4f, 0.9f, 0.9f), new(1f, 0.45f, 0.45f), new(0.7f, 0.7f, 1f),
        ];
        return palette[part % palette.Length];
    }

    /// <summary>Envoie au GPU les maillages des parties du modele (null = vide). Fil de rendu.</summary>
    public void Upload(PlacementEditorModel? model)
    {
        Clear();
        _model = model;
        if (model is null) return;
        _parts = new List<(VertexBuffer, IndexBuffer, int)>[model.Count];
        for (int i = 0; i < model.Count; i++)
        {
            _parts[i] = [];
            ModuleScene scene = model.Session.Parts[i].Scene;
            if (scene.Meshes.Count > 0)
                foreach (SceneMesh m in scene.Meshes) Add(i, m.Positions, m.Normals, m.Triangles);
            else
                foreach (SceneWalkmesh w in scene.Walkmeshes) Add(i, w.Positions, null, w.Triangles);
        }
    }

    private void Add(int part, System.Numerics.Vector3[] positions, System.Numerics.Vector3[]? normals, int[] triangles)
    {
        if (positions.Length == 0 || triangles.Length < 3) return;
        var v = new VertexPositionNormalTexture[positions.Length];
        for (int k = 0; k < v.Length; k++)
        {
            System.Numerics.Vector3 n = normals is not null && k < normals.Length ? normals[k] : System.Numerics.Vector3.UnitZ;
            v[k] = new VertexPositionNormalTexture(new XnaVector3(positions[k].X, positions[k].Y, positions[k].Z), new XnaVector3(n.X, n.Y, n.Z), Vector2.Zero);
        }
        int count = triangles.Length / 3 * 3;
        var idx = new int[count];
        for (int k = 0; k < count; k++) idx[k] = Math.Clamp(triangles[k], 0, positions.Length - 1);
        var vb = new VertexBuffer(_gd, VertexPositionNormalTexture.VertexDeclaration, v.Length, BufferUsage.WriteOnly);
        vb.SetData(v);
        var ib = new IndexBuffer(_gd, IndexElementSize.ThirtyTwoBits, idx.Length, BufferUsage.WriteOnly);
        ib.SetData(idx);
        _parts[part].Add((vb, ib, count / 3));
    }

    /// <summary>Matrice monde d'une partie : rotation Z, translation (metres), puis echelle d'affichage.</summary>
    public static Matrix World(RigidTransform t) =>
        Matrix.CreateRotationZ(MathHelper.ToRadians(t.YawDegrees))
        * Matrix.CreateTranslation(t.Translation.X, t.Translation.Y, t.Translation.Z)
        * Matrix.CreateScale(Scale);

    /// <summary>
    /// Dessine les parties dans <paramref name="viewport"/>. <paramref name="selected"/> : partie mise en valeur ;
    /// <paramref name="highlightDoors"/> : portes de jonction choisies (partie, porte), dessinees en jaune.
    /// </summary>
    public void Draw(FreeCamera camera, GfxViewport viewport, int selected, IReadOnlyList<(int Part, ModuleEntity Door)> highlightDoors)
    {
        PlacementEditorModel? model = _model;
        if (model is null) return;
        GfxViewport saved = _gd.Viewport;
        _gd.Viewport = viewport;
        try
        {
            Matrix view = camera.View, proj = camera.Projection(viewport.AspectRatio);
            _gd.BlendState = BlendState.Opaque;
            _gd.DepthStencilState = DepthStencilState.Default;
            _gd.RasterizerState = CullNone;
            _lit.View = view; _lit.Projection = proj;
            for (int i = 0; i < _parts.Length; i++)
            {
                System.Numerics.Vector3 c = PartColor(i) * (i == selected ? 1f : 0.6f);
                _lit.DiffuseColor = new XnaVector3(c.X, c.Y, c.Z);
                _lit.World = World(model.Transform(i));
                foreach (var (vb, ib, tris) in _parts[i])
                {
                    _gd.SetVertexBuffer(vb);
                    _gd.Indices = ib;
                    foreach (EffectPass pass in _lit.CurrentTechnique.Passes)
                    {
                        pass.Apply();
                        _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, tris);
                    }
                }
            }

            // Portes : cubes violets, jaunes pour les portes de jonction choisies (toujours visibles : sans test de profondeur).
            var cubes = new List<VertexPositionColor>();
            for (int i = 0; i < model.Count; i++)
            {
                RigidTransform t = model.Transform(i);
                foreach (ModuleEntity d in model.Doors(i))
                {
                    bool chosen = highlightDoors.Any(h => h.Part == i && ReferenceEquals(h.Door, d));
                    System.Numerics.Vector3 p = t.Apply(d.Position + new System.Numerics.Vector3(0, 0, 1f)) * Scale;
                    AddCube(cubes, new XnaVector3(p.X, p.Y, p.Z), DoorSize * Scale * (chosen ? 1.6f : 1f),
                        chosen ? new XnaColor(255, 230, 40) : PreviewRenderer.KindColor(EntityKind.Door));
                }
            }
            if (cubes.Count > 0)
            {
                _flat.World = Matrix.Identity; _flat.View = view; _flat.Projection = proj;
                _gd.DepthStencilState = DepthStencilState.None;
                foreach (EffectPass pass in _flat.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _gd.DrawUserPrimitives(PrimitiveType.TriangleList, cubes.ToArray(), 0, cubes.Count / 3);
                }
                _gd.DepthStencilState = DepthStencilState.Default;
            }
        }
        finally { _gd.Viewport = saved; }
    }

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

    private void Clear()
    {
        foreach (var list in _parts)
            foreach (var (vb, ib, _) in list) { vb.Dispose(); ib.Dispose(); }
        _parts = _empty;
        _model = null;
    }

    public void Dispose()
    {
        Clear();
        _lit.Dispose();
        _flat.Dispose();
    }
}

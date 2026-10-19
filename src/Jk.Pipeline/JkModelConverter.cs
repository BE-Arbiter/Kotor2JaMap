using System.Globalization;
using System.Numerics;
using Jk.Core.Gob;
using Jk.Core.Models;
using Jk.Core.Textures;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Models;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline;

/// <summary>
/// Un modele JK converti en MD3. <see cref="Min"/> / <see cref="Max"/> : boite englobante de toutes les images, en metres de la scene
/// (unites JK x <see cref="JkWorldScale.MetresPerJkUnit"/>, repere du modele, Z vers le haut, +Y vers l'avant). <see cref="Frames"/> : images
/// du fichier MD3 (1 pour un modele statique ou une rotation pure : le moteur fait tourner l'entite). <see cref="PureRotation"/> : un seul noeud
/// tourne, a vitesse constante autour d'un axe fixe (<see cref="RotationAxis"/>, repere du modele, <see cref="DegreesPerSecond"/> signe,
/// sens trigonometrique autour de l'axe) : l'entite doit etre un <c>func_rotating</c>.
/// </summary>
public sealed record JkMd3(Md3Asset Asset, Vector3 Min, Vector3 Max, int Frames, float Fps, bool PureRotation, Vector3 RotationAxis, float DegreesPerSecond)
{
    /// <summary>Diviseur de taille (cle <c>modelscale</c> de l'entite) quand le modele depasse +-511 unites JA ; 1 sinon.</summary>
    public int ModelScale { get; init; } = 1;

    /// <summary>Centre de la rotation pure (origine du noeud qui tourne), metres de la scene, repere du modele ; zero hors rotation pure.</summary>
    public Vector3 RotationCenter { get; init; }
}

/// <summary>
/// Conversion des modeles <c>.3do</c> (et de leur animation <c>.key</c>) en MD3. Geoset 0 (le plus detaille), noeuds hierarchiques, une surface MD3
/// par materiau. Chemin <c>models/jk/&lt;modele&gt;.md3</c> (<c>models/jk/s&lt;echelle&gt;/</c> hors echelle par defaut, comme les objets KOTOR),
/// shader <c>models/jk/&lt;texture&gt;</c>. Un modele reparti sur plusieurs MD3 n'est pas gere : au-dela de <see cref="Md3Writer.MaxSurfaces"/> surfaces
/// la conversion echoue (journalisee). Voir <c>docs/superpowers/specs/jk-formats.md</c> pour les constats sur les formats.
/// </summary>
public static class JkModelConverter
{
    public const string ShaderPrefix = "models/jk/";

    /// <summary>Seuils sous lesquels une position (unites JK) ou un angle (degres) est considere comme constant d'une image a l'autre.</summary>
    private const float PositionEpsilon = 1e-5f, AngleEpsilonDegrees = 1e-3f;

    /// <param name="fs">Systeme de fichiers du niveau (GOB de l'episode, Res2, Res1hi).</param>
    /// <param name="model3do">Nom du modele (<c>fan.3do</c>) ou chemin (<c>3do\fan.3do</c>).</param>
    /// <param name="key">Nom de l'animation (<c>fan.key</c>, dossier <c>3do\key</c>) ou null.</param>
    /// <param name="scale">Unites JA par metre de la scene (<c>ExportOptions.Scale</c>).</param>
    /// <param name="mats">Materiaux decodes, par nom de fichier (<c>fan.mat</c>, insensible a la casse) : sert aux dimensions pour les UV.</param>
    public static JkMd3? ToMd3(JkFileSystem fs, string model3do, string? key, float scale, IReadOnlyDictionary<string, JkMaterial> mats, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(mats);
        ArgumentNullException.ThrowIfNull(log);
        if (!(scale > 0) || !float.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(scale));

        string modelName = Path.GetFileNameWithoutExtension(model3do.Replace('/', '\\').Split('\\')[^1]).ToLowerInvariant();
        string modelPath = HasFolder(model3do) ? model3do : @"3do\" + model3do;
        byte[]? raw = fs.TryRead(modelPath);
        if (raw is null)
        {
            log.Warn("JK_MODEL_MISSING", $"Model {model3do}: not found in the game archives; the thing is not placed with a model.");
            return null;
        }
        ThreeDoModel model;
        try { model = ThreeDoReader.Read(ThreeDoReader.Decode(raw)); }
        catch (InvalidDataException ex)
        {
            log.Warn("JK_MODEL_UNREADABLE", $"Model {model3do}: {ex.Message}");
            return null;
        }

        // Fichier tronque : aucun geoset (ou geoset 0 sans maillage), donc rien a dessiner.
        if (model.Geosets.Count == 0 || model.Geosets[0].Meshes.Count == 0)
        {
            log.Warn("JK_MODEL_EMPTY", $"Model {model3do}: no geometry (no geoset or no mesh); the thing is not placed with a model.");
            return null;
        }

        KeyAnimation? anim = null;
        if (key is not null)
        {
            string keyPath = HasFolder(key) ? key : @"3do\key\" + key;
            byte[]? keyRaw = fs.TryRead(keyPath);
            if (keyRaw is null) log.Warn("JK_KEY_MISSING", $"Animation {key} (model {model3do}): not found; the model is exported without animation.");
            else
            {
                try { anim = KeyReader.Read(KeyReader.Decode(keyRaw)); }
                catch (InvalidDataException ex) { log.Warn("JK_KEY_UNREADABLE", $"Animation {key} (model {model3do}): {ex.Message}; the model is exported without animation."); }
            }
        }

        // --- Poses : une liste de matrices de noeud par image ---
        int[] keyOf = anim is null ? [] : MatchKeyNodes(model, anim, key!, log);
        int frameCount = anim is null ? 1 : Math.Max(1, Math.Min(anim.Frames, Md3Writer.MaxFrames));
        if (anim is not null && anim.Frames > Md3Writer.MaxFrames)
            log.Warn("JK_KEY_TRUNCATED", $"Animation {key}: {anim.Frames} frames, only the first {Md3Writer.MaxFrames} are kept.");
        var poses = new Matrix4x4[frameCount][];
        var pyrs = new (Vector3 Pos, Vector3 Pyr)[frameCount][];
        try
        {
            for (int f = 0; f < frameCount; f++)
            {
                pyrs[f] = new (Vector3, Vector3)[model.Nodes.Count];
                for (int n = 0; n < model.Nodes.Count; n++)
                    pyrs[f][n] = anim is not null && keyOf[n] >= 0 ? anim.Nodes[keyOf[n]].PoseAt(f) : (model.Nodes[n].Position, model.Nodes[n].PitchYawRoll);
                var captured = pyrs[f];
                poses[f] = model.NodeMatrices((i, _) => captured[i]);
            }
        }
        catch (InvalidDataException ex) // hierarchie cyclique
        {
            log.Warn("JK_MODEL_UNREADABLE", $"Model {model3do}: {ex.Message}");
            return null;
        }

        // --- Animation : statique, rotation pure ou images multiples ---
        bool[] moving = MovingNodes(model, pyrs);
        int movers = moving.Count(m => m);
        bool pure = false;
        Vector3 axis = Vector3.Zero, center = Vector3.Zero;
        float dps = 0;
        if (anim is not null && movers == 0) frameCount = 1; // rien ne bouge : une image
        else if (anim is not null && movers == 1 && frameCount > 1)
        {
            int node = Array.IndexOf(moving, true);
            if (TryPureRotation(model, node, pyrs, poses[0], anim.Fps, out axis, out dps))
            {
                pure = true;
                center = Vector3.Transform(Vector3.Zero, poses[0][node]) * JkWorldScale.MetresPerJkUnit; // pose ecrite dans le MD3
            }
        }

        // --- Geometrie : topologie commune, positions par image ---
        BuiltTopology? topo = BuildTopology(model, mats, log, model3do);
        if (topo is null)
        {
            log.Warn("JK_MODEL_EMPTY", $"Model {model3do}: no drawable face; the thing is not placed with a model.");
            return null;
        }
        float metres = JkWorldScale.MetresPerJkUnit;
        var framesMetres = new List<List<Vector3[]>>(frameCount); // image -> partie -> positions (metres de la scene)
        var framesNormals = new List<List<Vector3[]>>(frameCount);
        Vector3 bmin = new(float.MaxValue), bmax = new(float.MinValue);
        float maxAbs = 0;
        int writtenFrames = pure ? 1 : frameCount;
        for (int f = 0; f < frameCount; f++)
        {
            (List<Vector3[]> pos, List<Vector3[]> nor) = topo.Pose(model, poses[f], metres);
            framesMetres.Add(pos); framesNormals.Add(nor);
            foreach (Vector3[] part in pos)
                foreach (Vector3 p in part)
                {
                    bmin = Vector3.Min(bmin, p); bmax = Vector3.Max(bmax, p);
                    if (f < writtenFrames) maxAbs = MathF.Max(maxAbs, MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z))));
                }
        }
        maxAbs *= scale;
        if (!float.IsFinite(maxAbs))
        {
            log.Warn("JK_MODEL_UNREADABLE", $"Model {model3do}: non-finite coordinates.");
            return null;
        }
        int k = maxAbs > PlaceableConverter.ModelLimit ? (int)MathF.Ceiling(maxAbs / PlaceableConverter.ModelLimit) : 1;
        float factor = scale / k;

        var frames = new List<IReadOnlyList<ModelPart>>(writtenFrames);
        for (int f = 0; f < writtenFrames; f++)
        {
            var parts = new List<ModelPart>(topo.Parts.Count);
            for (int p = 0; p < topo.Parts.Count; p++)
            {
                BuiltPart bp = topo.Parts[p];
                parts.Add(new ModelPart(bp.Texture, [.. framesMetres[f][p].Select(v => v * factor)], framesNormals[f][p], bp.Uv, bp.Indices));
            }
            frames.Add(parts);
        }

        byte[] data;
        try { data = Md3Writer.WriteFrames(modelName, frames, ShaderPrefix); }
        catch (ArgumentException ex)
        {
            log.Warn("JK_MODEL_MD3_FAILED", $"Model {model3do}: MD3 not written ({ex.Message}).");
            return null;
        }
        string dir = ModelDirectory(scale);
        var asset = new Md3Asset($"{dir}{modelName}.md3", data, [.. topo.Parts.Select(p => p.Texture).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal)]);
        return new JkMd3(asset, bmin, bmax, writtenFrames, anim?.Fps ?? 0f, pure, axis, dps)
        {
            ModelScale = k,
            RotationCenter = center,
        };
    }

    private static bool HasFolder(string name) => name.Contains('\\') || name.Contains('/');

    /// <summary>Dossier des MD3 : <c>models/jk/</c> a l'echelle par defaut, sinon <c>models/jk/s&lt;echelle&gt;/</c> (comme les objets KOTOR).</summary>
    internal static string ModelDirectory(float scale) =>
        scale == ExportOptions.DefaultScale ? ShaderPrefix : $"{ShaderPrefix}s{scale.ToString("0.######", CultureInfo.InvariantCulture)}/";

    // --- Correspondance noeuds du modele <-> pistes de l'animation ---

    /// <summary>Par noeud du modele, indice de la piste de l'animation (-1 = pose de repos). Appariement par nom, puis par indice si les nombres concordent.</summary>
    private static int[] MatchKeyNodes(ThreeDoModel model, KeyAnimation anim, string key, ImportLog log)
    {
        var result = new int[model.Nodes.Count];
        int unmatched = 0;
        for (int n = 0; n < result.Length; n++)
        {
            result[n] = -1;
            for (int t = 0; t < anim.Nodes.Count; t++)
                if (string.Equals(anim.Nodes[t].MeshName, model.Nodes[n].Name, StringComparison.OrdinalIgnoreCase)) { result[n] = t; break; }
            if (result[n] < 0 && anim.Nodes.Count == model.Nodes.Count) result[n] = n;
            if (result[n] < 0) unmatched++;
        }
        if (unmatched > 0)
            log.Warn("JK_KEY_NODE_UNMATCHED", $"Animation {key}: {unmatched} node(s) of the model have no track and keep their rest pose.");
        return result;
    }

    /// <summary>Noeuds dont la pose change au cours de l'animation.</summary>
    private static bool[] MovingNodes(ThreeDoModel model, (Vector3 Pos, Vector3 Pyr)[][] pyrs)
    {
        var moving = new bool[model.Nodes.Count];
        for (int f = 1; f < pyrs.Length; f++)
            for (int n = 0; n < moving.Length; n++)
            {
                if (moving[n]) continue;
                var (p0, r0) = pyrs[0][n];
                var (p, r) = pyrs[f][n];
                if (Vector3.Distance(p0, p) > PositionEpsilon || AngleDiff(r0.X, r.X) > AngleEpsilonDegrees
                    || AngleDiff(r0.Y, r.Y) > AngleEpsilonDegrees || AngleDiff(r0.Z, r.Z) > AngleEpsilonDegrees) moving[n] = true;
            }
        return moving;
    }

    /// <summary>Vrai si tous les noeuds qui dessinent quelque chose (face de mode geometrie 3 ou plus) sont <paramref name="root"/> ou ses descendants.</summary>
    private static bool AllDrawnGeometryUnder(ThreeDoModel model, int root)
    {
        ThreeDoGeoset geoset = model.Geosets[0];
        for (int n = 0; n < model.Nodes.Count; n++)
        {
            int mesh = model.Nodes[n].Mesh;
            if (mesh < 0 || !geoset.Meshes[mesh].Faces.Any(f => f.Geo >= 3 && f.Corners.Length >= 3)) continue;
            int cur = n, steps = 0;
            while (cur >= 0 && cur != root && steps++ <= model.Nodes.Count) cur = model.Nodes[cur].Parent;
            if (cur != root) return false;
        }
        return true;
    }

    private static float AngleDiff(float a, float b)
    {
        float d = b - a;
        d -= 360f * MathF.Round(d / 360f);
        return MathF.Abs(d);
    }

    /// <summary>
    /// Rotation pure : la position du noeud est constante et l'increment de rotation d'une image a la suivante a le meme axe (a 1,8 degres pres)
    /// et le meme angle (a 3 % pres) sur toute l'animation. Toute la geometrie dessinee doit etre dans le sous-arbre du noeud (sinon une partie
    /// fixe tournerait avec lui). Axe renvoye dans le repere du modele (matrices de l'image 0, celle du MD3) ; vitesse en degres par seconde.
    /// </summary>
    private static bool TryPureRotation(ThreeDoModel model, int node, (Vector3 Pos, Vector3 Pyr)[][] pyrs, Matrix4x4[] frame0, float fps,
        out Vector3 axisModel, out float degreesPerSecond)
    {
        axisModel = Vector3.Zero; degreesPerSecond = 0;
        if (!AllDrawnGeometryUnder(model, node)) return false;
        for (int f = 1; f < pyrs.Length; f++)
            if (Vector3.Distance(pyrs[0][node].Pos, pyrs[f][node].Pos) > PositionEpsilon) return false;

        Vector3? axis0 = null;
        float angle0 = 0;
        for (int f = 0; f + 1 < pyrs.Length; f++)
        {
            Matrix4x4 a = ThreeDoModel.Rotation(pyrs[f][node].Pyr), b = ThreeDoModel.Rotation(pyrs[f + 1][node].Pyr);
            if (!Matrix4x4.Invert(a, out Matrix4x4 aInv)) return false;
            Quaternion q = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(aInv * b));
            if (q.W < 0) q = new Quaternion(-q.X, -q.Y, -q.Z, -q.W);
            float sinHalf = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
            float angle = 2f * MathF.Atan2(sinHalf, q.W) * 180f / MathF.PI;
            if (sinHalf < 1e-6f || angle < AngleEpsilonDegrees) return false; // image sans mouvement au milieu : pas une vitesse constante
            Vector3 axis = new Vector3(q.X, q.Y, q.Z) / sinHalf;
            if (axis0 is null) { axis0 = axis; angle0 = angle; continue; }
            if (Vector3.Dot(axis, axis0.Value) < 0.9995f || MathF.Abs(angle - angle0) > 0.03f * angle0) return false;
        }
        if (axis0 is null) return false;
        // Axe du repere du parent vers le repere du modele (la partie rotation de la matrice du parent au repos).
        Matrix4x4 parentFrame = model.Nodes[node].Parent >= 0 ? frame0[model.Nodes[node].Parent] : Matrix4x4.Identity;
        Vector3 inModel = Vector3.Normalize(Vector3.TransformNormal(axis0.Value, parentFrame));
        axisModel = inModel;
        degreesPerSecond = angle0 * fps;
        return true;
    }

    // --- Topologie : sommets dedoubles par (noeud, sommet, sommet de texture, normale), une partie par materiau ---

    private sealed record BuiltPart(string Texture, Vector2[] Uv, int[] Indices, (int Node, int Mesh, int Vertex, Vector3? FlatNormal, bool Back)[] Source);

    private sealed class BuiltTopology(List<BuiltPart> parts)
    {
        public List<BuiltPart> Parts { get; } = parts;

        /// <summary>Positions (metres de la scene) et normales de chaque partie pour une pose.</summary>
        public (List<Vector3[]> Positions, List<Vector3[]> Normals) Pose(ThreeDoModel model, Matrix4x4[] world, float metresPerUnit)
        {
            var pos = new List<Vector3[]>(Parts.Count);
            var nor = new List<Vector3[]>(Parts.Count);
            foreach (BuiltPart part in Parts)
            {
                var p = new Vector3[part.Source.Length];
                var n = new Vector3[part.Source.Length];
                for (int i = 0; i < p.Length; i++)
                {
                    var (node, mesh, vertex, flat, back) = part.Source[i];
                    ThreeDoMesh m = model.Geosets[0].Meshes[mesh];
                    p[i] = Vector3.Transform(m.Vertices[vertex] + model.Nodes[node].Pivot, world[node]) * metresPerUnit;
                    Vector3 local = flat ?? (vertex < m.VertexNormals.Length ? m.VertexNormals[vertex] : Vector3.UnitZ);
                    if (back) local = -local;
                    Vector3 rotated = Vector3.TransformNormal(local, world[node]);
                    n[i] = rotated.LengthSquared() > 1e-12f ? Vector3.Normalize(rotated) : Vector3.UnitZ;
                }
                pos.Add(p); nor.Add(n);
            }
            return (pos, nor);
        }
    }

    private sealed class PartBuilder
    {
        public readonly List<Vector2> Uv = new();
        public readonly List<int> Indices = new();
        public readonly List<(int Node, int Mesh, int Vertex, Vector3? FlatNormal, bool Back)> Source = new();
        public readonly Dictionary<(int, int, int, int, int, bool), int> Lookup = new();
    }

    private static BuiltTopology? BuildTopology(ThreeDoModel model, IReadOnlyDictionary<string, JkMaterial> mats, ImportLog log, string modelName)
    {
        var builders = new SortedDictionary<string, PartBuilder>(StringComparer.Ordinal);
        var missingLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int hiddenFaces = 0, badMaterialFaces = 0;
        ThreeDoGeoset geoset = model.Geosets[0];

        for (int n = 0; n < model.Nodes.Count; n++)
        {
            ThreeDoNode node = model.Nodes[n];
            if (node.Mesh < 0) continue;
            ThreeDoMesh mesh = geoset.Meshes[node.Mesh];
            for (int fi = 0; fi < mesh.Faces.Length; fi++)
            {
                ThreeDoFace face = mesh.Faces[fi];
                if (face.Corners.Length < 3) continue;
                if (face.Geo < 3) { hiddenFaces++; continue; } // non dessinee, points, fil de fer
                if (face.Material < 0 || face.Material >= model.Materials.Count) { badMaterialFaces++; continue; }
                string file = model.Materials[face.Material];
                string texture = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                JkMaterial? mat = FindMaterial(mats, file);
                int w = 64, h = 64;
                if (mat is not null && mat.Cels.Count > 0) { w = mat.Cels[0].Width; h = mat.Cels[0].Height; }
                else if (missingLogged.Add(file))
                    log.Warn("TEXTURE_MISSING", $"{file} (model {modelName}): material not found; faces keep a placeholder texture.");

                if (!builders.TryGetValue(texture, out PartBuilder? pb)) builders[texture] = pb = new PartBuilder();
                Vector3? flat = FaceNormal(mesh, fi);
                bool gouraud = face.Light >= 3;
                bool twoSided = (face.Type & 1) != 0;
                for (int side = 0; side < (twoSided ? 2 : 1); side++)
                {
                    bool back = side == 1;
                    var ids = new int[face.Corners.Length];
                    for (int c = 0; c < ids.Length; c++)
                    {
                        ThreeDoCorner corner = face.Corners[c];
                        // Sommets partages entre faces seulement si l'ombrage est lisse (sinon la normale de face fait partie de l'identite).
                        var id = (n, node.Mesh, corner.Vertex, corner.TexVertex, gouraud ? -1 : fi, back);
                        if (!pb.Lookup.TryGetValue(id, out int index))
                        {
                            index = pb.Source.Count;
                            pb.Lookup[id] = index;
                            pb.Source.Add((n, node.Mesh, corner.Vertex, gouraud ? null : flat, back));
                            Vector2 uv = Vector2.Zero;
                            if (corner.TexVertex >= 0 && corner.TexVertex < mesh.TexVertices.Length)
                            {
                                Vector2 t = mesh.TexVertices[corner.TexVertex];
                                uv = new Vector2(t.X / w, 1f - t.Y / h); // v = rangee depuis le haut, comme les surfaces du niveau
                            }
                            pb.Uv.Add(uv);
                        }
                        ids[c] = index;
                    }
                    // Eventail (faces convexes, sens antihoraire comme JA) ; face arriere : sens inverse.
                    for (int c = 1; c + 1 < ids.Length; c++)
                    {
                        if (!back) { pb.Indices.Add(ids[0]); pb.Indices.Add(ids[c]); pb.Indices.Add(ids[c + 1]); }
                        else { pb.Indices.Add(ids[0]); pb.Indices.Add(ids[c + 1]); pb.Indices.Add(ids[c]); }
                    }
                }
            }
        }
        if (hiddenFaces > 0)
            log.Warn("JK_MODEL_FACES_HIDDEN", $"Model {modelName}: {hiddenFaces} face(s) use a non-drawn geometry mode (invisible, points or wireframe) and are not exported.");
        if (badMaterialFaces > 0)
            log.Warn("JK_MODEL_FACE_MATERIAL", $"Model {modelName}: {badMaterialFaces} face(s) have no valid material and are not exported.");
        if (builders.Count == 0) return null;
        return new BuiltTopology(builders.Select(kv => new BuiltPart(kv.Key, [.. kv.Value.Uv], [.. kv.Value.Indices], [.. kv.Value.Source])).ToList());
    }

    private static Vector3? FaceNormal(ThreeDoMesh mesh, int face) =>
        face < mesh.FaceNormals.Length && mesh.FaceNormals[face].LengthSquared() > 1e-12f ? mesh.FaceNormals[face] : null;

    private static JkMaterial? FindMaterial(IReadOnlyDictionary<string, JkMaterial> mats, string file)
    {
        if (mats.TryGetValue(file, out JkMaterial? m)) return m;
        string bare = Path.GetFileNameWithoutExtension(file);
        if (mats.TryGetValue(bare, out m)) return m;
        foreach (var (name, mat) in mats)
            if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase) || string.Equals(name, bare, StringComparison.OrdinalIgnoreCase)) return mat;
        return null;
    }
}

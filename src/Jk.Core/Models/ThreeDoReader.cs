using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Jk.Core.Text;

namespace Jk.Core.Models;

/// <summary>
/// Lecteur de modeles <c>.3do</c> (texte Latin-1, version « 3DO 2.1 »). Sections : <c>HEADER</c>, <c>MODELRESOURCE</c> (<c>MATERIALS</c>),
/// <c>GEOMETRYDEF</c> (rayon, <c>INSERT OFFSET</c>, <c>GEOSETS</c> ; par maillage : sommets, sommets de texture, normales, faces) et
/// <c>HIERARCHYDEF</c> (noeuds). Voir <c>docs/superpowers/specs/jk-formats.md</c>.
/// </summary>
public static class ThreeDoReader
{
    private static readonly Regex Comma = new(@"\s*,\s*", RegexOptions.Compiled);

    public static string Decode(byte[] data) => Encoding.Latin1.GetString(data);

    private enum Mode { None, Materials, Vertices, TexVertices, VertexNormals, Faces, FaceNormals, Nodes }

    private sealed class MeshBuilder
    {
        public string Name = "";
        public float Radius;
        public int Geometry = 4, Lighting = 3, Texture = 1;
        public List<Vector3> Vertices = new();
        public List<float> Intensities = new();
        public List<Vector2> TexVertices = new();
        public List<Vector3> VertexNormals = new();
        public List<ThreeDoFace> Faces = new();
        public List<Vector3> FaceNormals = new();
        public int VertexCount = -1, TexVertexCount = -1, FaceCount = -1;

        public ThreeDoMesh Build(string where)
        {
            if (VertexCount >= 0 && Vertices.Count != VertexCount) throw new InvalidDataException($"3DO {where}: {Vertices.Count} vertices read, {VertexCount} declared.");
            if (TexVertexCount >= 0 && TexVertices.Count != TexVertexCount) throw new InvalidDataException($"3DO {where}: {TexVertices.Count} texture vertices read, {TexVertexCount} declared.");
            if (FaceCount >= 0 && Faces.Count != FaceCount) throw new InvalidDataException($"3DO {where}: {Faces.Count} faces read, {FaceCount} declared.");
            // Indices de sommet des faces : doivent designer un sommet existant (les indices de texture sont toleres hors plage : les donnees
            // du jeu en contiennent pour des maillages sans coordonnees de texture, le convertisseur les traite).
            for (int f = 0; f < Faces.Count; f++)
                foreach (ThreeDoCorner c in Faces[f].Corners)
                    if (c.Vertex < 0 || c.Vertex >= Vertices.Count)
                        throw new InvalidDataException($"3DO {where}: face {f} uses vertex {c.Vertex}, outside 0..{Vertices.Count - 1}.");
            return new ThreeDoMesh(Name, Radius, Geometry, Lighting, Texture, [.. Vertices], [.. Intensities], [.. TexVertices],
                [.. VertexNormals], [.. Faces], [.. FaceNormals]);
        }
    }

    public static ThreeDoModel Read(string text)
    {
        var materials = new List<string>();
        var geosets = new List<List<MeshBuilder>>();
        var nodes = new List<ThreeDoNode>();
        MeshBuilder? mesh = null;
        float radius = 0;
        Vector3 insert = Vector3.Zero;
        bool header = false;
        int declaredNodes = -1, declaredMaterials = -1;
        Mode mode = Mode.None;

        foreach (var (number, raw) in JkTokenizer.Lines(text))
        {
            string line = raw;
            try
            {
                if (JkTokenizer.TryStripIndex(line, out _, out string rest) && mode != Mode.None)
                {
                    ReadIndexed(mode, rest);
                    continue;
                }
                string[] t = JkTokenizer.Split(line);
                string key = t[0].ToUpperInvariant();
                switch (key)
                {
                    case "3DO": header = true; mode = Mode.None; break;
                    case "SECTION:": mode = Mode.None; break;
                    case "MATERIALS": declaredMaterials = JkTokenizer.ParseInt(t[1]); mode = Mode.Materials; break;
                    case "INSERT": insert = new Vector3(JkTokenizer.ParseFloat(t[2]), JkTokenizer.ParseFloat(t[3]), JkTokenizer.ParseFloat(t[4])); break;
                    case "RADIUS":
                        if (mesh is null) radius = JkTokenizer.ParseFloat(t[1]); else mesh.Radius = JkTokenizer.ParseFloat(t[1]);
                        break;
                    case "GEOSETS": mode = Mode.None; break;
                    case "GEOSET": geosets.Add(new List<MeshBuilder>()); mesh = null; mode = Mode.None; break;
                    case "MESHES": break;
                    case "MESH":
                        if (geosets.Count == 0) throw new InvalidDataException("MESH before GEOSET.");
                        mesh = new MeshBuilder();
                        geosets[^1].Add(mesh);
                        mode = Mode.None;
                        break;
                    case "NAME": RequireMesh().Name = t.Length > 1 ? string.Join(' ', t.Skip(1)) : ""; break;
                    case "GEOMETRYMODE": RequireMesh().Geometry = JkTokenizer.ParseInt(t[1]); break;
                    case "LIGHTINGMODE": RequireMesh().Lighting = JkTokenizer.ParseInt(t[1]); break;
                    case "TEXTUREMODE": RequireMesh().Texture = JkTokenizer.ParseInt(t[1]); break;
                    case "VERTICES": RequireMesh().VertexCount = JkTokenizer.ParseInt(t[1]); mode = Mode.Vertices; break;
                    case "TEXTURE": // TEXTURE VERTICES n
                        RequireMesh().TexVertexCount = JkTokenizer.ParseInt(t[2]); mode = Mode.TexVertices; break;
                    case "VERTEX": RequireMesh(); mode = Mode.VertexNormals; break; // VERTEX NORMALS
                    case "FACES": RequireMesh().FaceCount = JkTokenizer.ParseInt(t[1]); mode = Mode.Faces; break;
                    case "FACE": RequireMesh(); mode = Mode.FaceNormals; break; // FACE NORMALS
                    case "HIERARCHY": declaredNodes = JkTokenizer.ParseInt(t[2]); mode = Mode.Nodes; break;
                    default: throw new InvalidDataException($"unexpected line '{line}'");
                }
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
            {
                throw new InvalidDataException($"3DO line {number}: cannot parse '{line}' ({ex.Message}).", ex);
            }
            catch (InvalidDataException ex) when (!ex.Message.StartsWith("3DO", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"3DO line {number}: {ex.Message}", ex);
            }
        }

        if (!header) throw new InvalidDataException("Not a 3DO file (no '3DO' header line).");
        if (declaredMaterials >= 0 && materials.Count != declaredMaterials)
            throw new InvalidDataException($"3DO: {materials.Count} materials read, {declaredMaterials} declared.");
        if (declaredNodes >= 0 && nodes.Count != declaredNodes)
            throw new InvalidDataException($"3DO: {nodes.Count} hierarchy nodes read, {declaredNodes} declared.");

        var built = new List<ThreeDoGeoset>();
        for (int g = 0; g < geosets.Count; g++)
            built.Add(new ThreeDoGeoset(geosets[g].Select((m, i) => m.Build($"geoset {g} mesh {i}")).ToList()));
        foreach (ThreeDoNode n in nodes)
            if (n.Parent >= nodes.Count || n.Mesh >= (built.Count > 0 ? built[0].Meshes.Count : 0))
                throw new InvalidDataException($"3DO: node {n.Index} ('{n.Name}') references a missing parent or mesh.");
        // Hierarchie sans cycle : en remontant les parents on doit atteindre une racine en au plus Count pas.
        foreach (ThreeDoNode n in nodes)
        {
            int cur = n.Index, steps = 0;
            while (cur >= 0)
            {
                if (++steps > nodes.Count) throw new InvalidDataException($"3DO: hierarchy has a cycle (node {n.Index} '{n.Name}').");
                cur = nodes[cur].Parent;
            }
        }
        return new ThreeDoModel { Materials = materials, Geosets = built, Nodes = nodes, Radius = radius, InsertOffset = insert };

        MeshBuilder RequireMesh() => mesh ?? throw new InvalidDataException("mesh keyword outside a MESH block");

        void ReadIndexed(Mode m, string body)
        {
            string[] t = JkTokenizer.Split(m == Mode.Faces ? Comma.Replace(body, ",") : body);
            switch (m)
            {
                case Mode.Materials: materials.Add(t[0]); break;
                case Mode.Vertices:
                    RequireMesh().Vertices.Add(new Vector3(JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2])));
                    mesh!.Intensities.Add(t.Length > 3 ? JkTokenizer.ParseFloat(t[3]) : 0f);
                    break;
                case Mode.TexVertices: RequireMesh().TexVertices.Add(new Vector2(JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseFloat(t[1]))); break;
                case Mode.VertexNormals: RequireMesh().VertexNormals.Add(new Vector3(JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2]))); break;
                case Mode.FaceNormals: RequireMesh().FaceNormals.Add(new Vector3(JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2]))); break;
                case Mode.Faces:
                {
                    // material type geo light tex extralight nverts v,t v,t ...
                    int count = JkTokenizer.ParseInt(t[6]);
                    if (t.Length < 7 + count) throw new InvalidDataException($"face declares {count} vertices but has {t.Length - 7}");
                    var corners = new ThreeDoCorner[count];
                    for (int i = 0; i < count; i++)
                    {
                        string[] vt = t[7 + i].Split(',');
                        corners[i] = new ThreeDoCorner(JkTokenizer.ParseInt(vt[0]), vt.Length > 1 ? JkTokenizer.ParseInt(vt[1]) : -1);
                    }
                    RequireMesh().Faces.Add(new ThreeDoFace(JkTokenizer.ParseInt(t[0]), JkTokenizer.ParseFlags(t[1]), JkTokenizer.ParseInt(t[2]),
                        JkTokenizer.ParseInt(t[3]), JkTokenizer.ParseInt(t[4]), JkTokenizer.ParseFloat(t[5]), corners));
                    break;
                }
                case Mode.Nodes:
                {
                    // flags type mesh parent child sibling numChildren x y z pitch yaw roll pivotx pivoty pivotz name
                    if (t.Length < 17) throw new InvalidDataException("hierarchy node needs 17 fields and a name");
                    nodes.Add(new ThreeDoNode(nodes.Count, JkTokenizer.ParseFlags(t[0]), JkTokenizer.ParseFlags(t[1]), JkTokenizer.ParseInt(t[2]),
                        JkTokenizer.ParseInt(t[3]), JkTokenizer.ParseInt(t[4]), JkTokenizer.ParseInt(t[5]), JkTokenizer.ParseInt(t[6]),
                        new Vector3(JkTokenizer.ParseFloat(t[7]), JkTokenizer.ParseFloat(t[8]), JkTokenizer.ParseFloat(t[9])),
                        new Vector3(JkTokenizer.ParseFloat(t[10]), JkTokenizer.ParseFloat(t[11]), JkTokenizer.ParseFloat(t[12])),
                        new Vector3(JkTokenizer.ParseFloat(t[13]), JkTokenizer.ParseFloat(t[14]), JkTokenizer.ParseFloat(t[15])),
                        string.Join(' ', t.Skip(16))));
                    break;
                }
            }
        }
    }
}

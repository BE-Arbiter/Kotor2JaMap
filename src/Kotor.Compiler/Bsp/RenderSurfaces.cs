// Inspiré de la démarche de q3map2 / NetRadiant-custom (surfaces triangle soup rattachées aux feuilles, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Globalization;
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>Surface de type triangle soup : sommets (unités Quake), indices locaux, shader et feuille d'attache.</summary>
public sealed record RenderSurface(string Shader, Vector3[] Positions, Vector2[] Uvs, Vector3[] Normals, int[] Indices, int Leaf, bool Sky = false, bool DoubleSided = false);

public static class RenderSurfaces
{
    // Moteur (OpenJK ParseTriSurf, qfiles.h) : SHADER_MAX_VERTEXES / SHADER_MAX_INDEXES (6x), refus si le compte est >= a la limite.
    // L'erreur observee en jeu cite 1200 : on reste strictement sous 1200 sommets et 7200 indices (multiple de 3).
    public const int MaxSurfaceVerts = 1199;
    public const int MaxSurfaceIndexes = 7197;

    /// <summary>Nom du shader d'un OBJ : <c>textures/tfu2ja/&lt;nom de fichier sans extension&gt;</c> (voir ShaderWriter).</summary>
    public static string ShaderFor(string objPath) => Kotor.Core.Export.ShaderNames.FullName(Path.GetFileNameWithoutExtension(objPath));

    /// <summary>Feuille contenant le point (descente de l'arbre ; front = côté positif du plan).</summary>
    public static int FindLeaf(BspTree tree, Vector3 p)
    {
        if (tree.Nodes.Count == 0) return 0;
        int n = 0;
        while (true)
        {
            BspNode node = tree.Nodes[n];
            BspPlane pl = tree.Planes[node.PlaneIndex];
            int child = Vector3.Dot(pl.Normal, p) >= pl.Dist ? node.FrontChild : node.BackChild;
            if (child < 0) return -1 - child;
            n = child;
        }
    }

    /// <summary>
    /// Lit un OBJ (v, vt, vn, f) et le découpe en surfaces : une par feuille touchée (triangle rattaché à la feuille
    /// de son centroïde). <paramref name="origin"/> est ajouté aux positions (clé <c>origin</c> du misc_model).
    /// </summary>
    public static IReadOnlyList<RenderSurface> FromObj(string objPath, Vector3 origin, BspTree tree)
    {
        var pos = new List<Vector3>();
        var uv = new List<Vector2>();
        var nrm = new List<Vector3>();
        var tris = new List<(int V, int T, int N)[]>();

        foreach (string raw in File.ReadLines(objPath))
        {
            string line = raw.Trim();
            if (line.Length < 2) continue;
            string[] t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (t[0])
            {
                case "v" when t.Length >= 4: pos.Add(new Vector3(F(t[1]), F(t[2]), F(t[3])) + origin); break;
                case "vt" when t.Length >= 3: 
                    // Les OBJ sont en convention OBJ (v = 0 en bas, voir JaModelWriter : vt u (1 - v)). Quake 3 / JA et TFU
                    // (DirectX) ont t = 0 en haut : on re-inverse ici, seul endroit de lecture de ces OBJ.
                    uv.Add(new Vector2(F(t[1]), 1f - F(t[2])));
                    break;
                case "vn" when t.Length >= 4: nrm.Add(new Vector3(F(t[1]), F(t[2]), F(t[3]))); break;
                case "f" when t.Length >= 4:
                    var corners = new (int V, int T, int N)[t.Length - 1];
                    for (int i = 1; i < t.Length; i++) corners[i - 1] = Corner(t[i], pos.Count, uv.Count, nrm.Count);
                    for (int i = 1; i + 1 < corners.Length; i++) tris.Add([corners[0], corners[i], corners[i + 1]]);
                    break;
            }
        }

        string shader = ShaderFor(objPath);
        var byLeaf = new SortedDictionary<int, List<Builder>>();
        foreach (var tri in tris)
        {
            if (tri.Any(c => (uint)c.V >= (uint)pos.Count)) continue; // indice invalide : triangle ignoré
            Vector3 a = pos[tri[0].V], b = pos[tri[1].V], c = pos[tri[2].V];
            int leaf = FindLeaf(tree, (a + b + c) / 3f);
            if (!byLeaf.TryGetValue(leaf, out List<Builder>? chunks)) byLeaf[leaf] = chunks = [new Builder()];
            Builder bl = chunks[^1];
            // Découpage par triangles entiers : au plus 3 nouveaux sommets par triangle.
            if (bl.Positions.Count + 3 > MaxSurfaceVerts || bl.Indices.Count + 3 > MaxSurfaceIndexes)
                chunks.Add(bl = new Builder());
            Vector3 faceN = Vector3.Cross(b - a, c - a);
            faceN = faceN.LengthSquared() > 0 ? Vector3.Normalize(faceN) : Vector3.UnitZ;
            // Enroulement JA (mesure etape 15) : ordre inverse de l OBJ, on emet v0, v2, v1.
            foreach (var corner in new[] { tri[0], tri[2], tri[1] })
            {
                bool hasN = (uint)corner.N < (uint)nrm.Count;
                Vector2 st = (uint)corner.T < (uint)uv.Count ? uv[corner.T] : Vector2.Zero;
                Vector3 n = hasN ? nrm[corner.N] : faceN;
                // Sans normale d'OBJ la normale dépend de la face : pas de partage de sommet.
                bl.Add(hasN ? (corner.V, corner.T, corner.N) : (corner.V, corner.T, -1 - bl.Positions.Count), pos[corner.V], st, n);
            }
        }

        return byLeaf.SelectMany(kv => kv.Value.Select(b => new RenderSurface(shader, b.Positions.ToArray(), b.Uvs.ToArray(),
            b.Normals.ToArray(), b.Indices.ToArray(), kv.Key))).ToList();
    }

    private sealed class Builder
    {
        private readonly Dictionary<(int, int, int), int> _map = [];
        public List<Vector3> Positions { get; } = [];
        public List<Vector2> Uvs { get; } = [];
        public List<Vector3> Normals { get; } = [];
        public List<int> Indices { get; } = [];

        public void Add((int, int, int) key, Vector3 p, Vector2 st, Vector3 n)
        {
            if (!_map.TryGetValue(key, out int i))
            {
                i = Positions.Count;
                _map[key] = i;
                Positions.Add(p); Uvs.Add(st); Normals.Add(n);
            }
            Indices.Add(i);
        }
    }

    // "v", "v/vt", "v//vn", "v/vt/vn" ; indices 1-based, négatifs = relatifs ; renvoie des indices 0-based (-1 = absent).
    private static (int V, int T, int N) Corner(string s, int nv, int nt, int nn)
    {
        string[] p = s.Split('/');
        return (Idx(p[0], nv), p.Length > 1 ? Idx(p[1], nt) : -1, p.Length > 2 ? Idx(p[2], nn) : -1);
    }

    private static int Idx(string s, int count)
    {
        if (s.Length == 0 || !int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return -1;
        return i > 0 ? i - 1 : count + i;
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

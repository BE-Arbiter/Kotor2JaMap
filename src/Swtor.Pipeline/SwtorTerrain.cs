using System.Numerics;
using Kotor.Core.Scene;
using Swtor.Core.Terrain;

namespace Swtor.Pipeline;

/// <summary>Une dalle de terrain placee : sa room, ses hauteurs et sa matrice (locale de l'objet, puis recentrage et echelle de la scene, comme les modeles).</summary>
internal sealed record SwtorTerrainTile(string Room, HeightmapTile Tile, Matrix4x4 World);

/// <summary>Maillage de terrain en positions de scene (Z haut) : sommets, normales, UV de projection au sol, triangles (sens trigonometrique vu de dessus).</summary>
internal sealed record SwtorTerrainMesh(Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, int[] Triangles)
{
    /// <summary>Couches de sol de chaque sommet (au plus <see cref="SwtorTerrain.LayersPerVertex"/>, -1 = vide) : <c>[sommet * 4 + k]</c>. Null sans carte de melange demandee.</summary>
    public int[]? VertexLayers { get; init; }

    /// <summary>Poids (0 a 255) de <see cref="VertexLayers"/>, meme indice.</summary>
    public byte[]? VertexWeights { get; init; }
}

/// <summary>
/// Morceau de sol d'une couche (<paramref name="Overlay"/> = -1) ou de deux couches fondues : <paramref name="Overlay"/> par-dessus <paramref name="Base"/>,
/// opacite par sommet <paramref name="Alpha"/>. Couches : indices du parametre <c>slotLayers</c> de <see cref="SwtorTerrain.BuildMesh"/>.
/// </summary>
internal sealed record SwtorTerrainPart(int Base, int Overlay, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv, byte[]? Alpha, int[] Triangles);

/// <summary>
/// Maillage du sol des zones exterieures SWTOR depuis les dalles <see cref="HeightmapTile"/> : echantillonnage regulier (un echantillon sur
/// <c>pas</c>, le dernier de chaque bord toujours garde pour que deux dalles voisines, qui partagent leur bord, se raccordent sans fente),
/// pas choisi pour tenir dans un budget de triangles, trous du masque respectes, diagonale de chaque cellule choisie pour suivre le relief.
/// </summary>
internal static class SwtorTerrain
{
    /// <summary>Pas d echantillonnage essayes, du plus fin au plus grossier : des diviseurs de 64 pour que les bords des dalles de 65 et de 129 echantillons, de tailles melangees, gardent les memes sommets (sinon fentes en T).</summary>
    internal static readonly int[] Steps = [1, 2, 4, 8, 16, 32, 64];

    /// <summary>Indices d'echantillons gardes le long d'un axe de <paramref name="count"/> echantillons : 0, pas, 2 pas... et toujours le dernier.</summary>
    internal static int[] SampleIndices(int count, int step)
    {
        var list = new List<int>();
        for (int i = 0; i < count - 1; i += step) list.Add(i);
        list.Add(count - 1);
        return [.. list];
    }

    /// <summary>Nombre de triangles d'une dalle echantillonnee au pas donne (cellules percees exclues).</summary>
    internal static int CountTriangles(HeightmapTile tile, int step)
    {
        int[] ri = SampleIndices(tile.Rows, step), ci = SampleIndices(tile.Columns, step);
        int cells = 0;
        for (int a = 0; a + 1 < ri.Length; a++)
            for (int b = 0; b + 1 < ci.Length; b++)
                if (CoarseCellPresent(tile, ri[a], ri[a + 1], ci[b], ci[b + 1])) cells++;
        return cells * 2;
    }

    /// <summary>Plus petit pas de <see cref="Steps"/> dont le total de triangles tient dans <paramref name="maxTriangles"/> (le plus grossier sinon).</summary>
    internal static int ChooseStep(IReadOnlyList<HeightmapTile> tiles, long maxTriangles)
    {
        foreach (int step in Steps)
        {
            long total = 0;
            foreach (HeightmapTile t in tiles)
            {
                total += CountTriangles(t, step);
                if (total > maxTriangles) break;
            }
            if (total <= maxTriangles) return step;
        }
        return Steps[^1];
    }

    private static bool CoarseCellPresent(HeightmapTile tile, int r0, int r1, int c0, int c1)
    {
        if (tile.VertexPresent is null) return true;
        for (int r = r0; r < r1; r++)
            for (int c = c0; c < c1; c++)
                if (tile.IsCellPresent(r, c)) return true;
        return false;
    }

    /// <summary>Couches de sol gardees par sommet (les plus lourdes).</summary>
    internal const int LayersPerVertex = 4;

    /// <summary>
    /// Maillage de toutes les dalles au pas donne. <paramref name="toScene"/> convertit un point SWTOR deja transforme par la matrice monde
    /// (Y haut) en point de scene (Z haut). <paramref name="uvPeriod"/> : cote (unites de scene) d'une repetition de texture.
    /// Avec <paramref name="slotLayers"/> (couche de sol de chaque emplacement de la carte de melange d'une dalle, -1 = ignore ; null = dalle sans carte),
    /// chaque sommet recoit les poids du texel de la carte sous lui ; sans carte ou sans poids utile, la couche <paramref name="fallbackLayer"/>.
    /// </summary>
    internal static SwtorTerrainMesh BuildMesh(IEnumerable<(HeightmapTile Tile, Matrix4x4 World)> tiles, int step, Func<Vector3, Vector3> toScene,
        bool flipWinding, float uvPeriod, Func<HeightmapTile, int[]?>? slotLayers = null, int fallbackLayer = 0)
    {
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        List<int>? layers = slotLayers is null ? null : [];
        List<byte>? weights = slotLayers is null ? null : [];
        var sum = new int[256];
        foreach (var (tile, world) in tiles)
        {
            bool flip = flipWinding ^ (world.GetDeterminant() < 0f);
            int[] ri = SampleIndices(tile.Rows, step), ci = SampleIndices(tile.Columns, step);
            int[]? slots = slotLayers?.Invoke(tile);
            if (slots is not null && (tile.BlendWeights is null || slots.Length != tile.Layers.Length)) slots = null;
            var vertex = new int[ri.Length * ci.Length];
            Array.Fill(vertex, -1);
            int Vertex(int a, int b)
            {
                int slot = a * ci.Length + b;
                if (vertex[slot] >= 0) return vertex[slot];
                (float x, float z) = tile.LocalXZ(ri[a], ci[b]);
                Vector3 world3 = Vector3.Transform(new Vector3(x, tile.HeightAt(ri[a], ci[b]), z), world);
                positions.Add(toScene(world3));
                uvs.Add(new Vector2(world3.X / uvPeriod, world3.Z / uvPeriod));
                if (layers is not null) AddVertexLayers(tile, slots, 2 * ri[a], 2 * ci[b], fallbackLayer, sum, layers, weights!);
                return vertex[slot] = positions.Count - 1;
            }
            for (int a = 0; a + 1 < ri.Length; a++)
                for (int b = 0; b + 1 < ci.Length; b++)
                {
                    if (!CoarseCellPresent(tile, ri[a], ri[a + 1], ci[b], ci[b + 1])) continue;
                    int v00 = Vertex(a, b), v10 = Vertex(a + 1, b), v01 = Vertex(a, b + 1), v11 = Vertex(a + 1, b + 1);
                    // Diagonale qui relie les deux coins de hauteurs les plus proches : moins de pics sur les arretes.
                    float d1 = MathF.Abs(tile.HeightAt(ri[a], ci[b]) - tile.HeightAt(ri[a + 1], ci[b + 1]));
                    float d2 = MathF.Abs(tile.HeightAt(ri[a + 1], ci[b]) - tile.HeightAt(ri[a], ci[b + 1]));
                    // Sommets dans l'ordre (X, Z) : (v00, v01, v10) regarde vers le haut (+Y) dans l'espace SWTOR.
                    if (d1 <= d2)
                    {
                        AddTriangle(triangles, v00, v01, v11, flip);
                        AddTriangle(triangles, v00, v11, v10, flip);
                    }
                    else
                    {
                        AddTriangle(triangles, v00, v01, v10, flip);
                        AddTriangle(triangles, v01, v11, v10, flip);
                    }
                }
        }

        Vector3[] pos = [.. positions];
        var normals = new Vector3[pos.Length];
        for (int i = 0; i + 2 < triangles.Count; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            Vector3 n = Vector3.Cross(pos[b] - pos[a], pos[c] - pos[a]);
            normals[a] += n; normals[b] += n; normals[c] += n;
        }
        for (int i = 0; i < normals.Length; i++)
            normals[i] = normals[i].LengthSquared() > 1e-18f ? Vector3.Normalize(normals[i]) : Vector3.UnitZ;
        return new SwtorTerrainMesh(pos, normals, [.. uvs], [.. triangles])
        {
            VertexLayers = layers is null ? null : [.. layers],
            VertexWeights = weights is null ? null : [.. weights],
        };
    }

    /// <summary>Ajoute les <see cref="LayersPerVertex"/> couches les plus lourdes du texel (emplacements de meme couche additionnes), ou le repli.</summary>
    private static void AddVertexLayers(HeightmapTile tile, int[]? slots, int blendRow, int blendColumn, int fallbackLayer, Span<int> sum,
        List<int> layers, List<byte> weights)
    {
        int start = layers.Count;
        if (slots is not null)
        {
            sum.Clear();
            int total = 0;
            for (int s = 0; s < slots.Length; s++)
            {
                int layer = slots[s];
                if (layer is < 0 or > 255) continue;
                int w = tile.BlendWeight(s, blendRow, blendColumn);
                sum[layer] += w;
                total += w;
            }
            for (int k = 0; k < LayersPerVertex && total > 0; k++)
            {
                int best = -1;
                for (int l = 0; l < 256; l++)
                    if (sum[l] > 0 && (best < 0 || sum[l] > sum[best])) best = l;
                if (best < 0) break;
                layers.Add(best);
                weights.Add((byte)Math.Min(255, sum[best]));
                sum[best] = 0;
            }
        }
        if (layers.Count == start) { layers.Add(fallbackLayer); weights.Add(255); }
        while (layers.Count - start < LayersPerVertex) { layers.Add(-1); weights.Add(0); }
    }

    /// <summary>
    /// Coupe le sol par couches : chaque triangle va aux deux couches les plus lourdes sur ses trois sommets ; la seconde est ignoree si elle pese
    /// moins de 2 % (triangle d'une seule couche). Paire : la couche d'indice le plus petit dessous, l'autre par-dessus avec l'opacite
    /// <c>poids dessus / (poids dessous + poids dessus)</c> a chaque sommet. Les sommets sont recopies par morceau.
    /// </summary>
    internal static List<SwtorTerrainPart> SplitByLayers(SwtorTerrainMesh mesh)
    {
        int[] vl = mesh.VertexLayers ?? throw new ArgumentException("Maillage sans couches de sol.", nameof(mesh));
        byte[] vw = mesh.VertexWeights!;
        int Weight(int v, int layer)
        {
            for (int k = 0; k < LayersPerVertex; k++) if (vl[v * LayersPerVertex + k] == layer) return vw[v * LayersPerVertex + k];
            return 0;
        }

        var parts = new Dictionary<(int Base, int Overlay), PartBuilder>();
        var order = new List<(int, int)>();
        var totals = new Dictionary<int, int>();
        for (int t = 0; t + 2 < mesh.Triangles.Length; t += 3)
        {
            totals.Clear();
            for (int j = 0; j < 3; j++)
            {
                int v = mesh.Triangles[t + j];
                for (int k = 0; k < LayersPerVertex; k++)
                {
                    int layer = vl[v * LayersPerVertex + k];
                    if (layer >= 0) totals[layer] = totals.GetValueOrDefault(layer) + vw[v * LayersPerVertex + k];
                }
            }
            var ranked = totals.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).ToList();
            int a = ranked.Count > 0 ? ranked[0].Key : 0;
            bool pair = ranked.Count > 1 && ranked[1].Value * 50 >= ranked[0].Value + ranked[1].Value;
            var key = pair ? (Math.Min(a, ranked[1].Key), Math.Max(a, ranked[1].Key)) : (a, -1);
            if (!parts.TryGetValue(key, out PartBuilder? pb)) { parts[key] = pb = new PartBuilder(); order.Add(key); }
            for (int j = 0; j < 3; j++)
            {
                int v = mesh.Triangles[t + j];
                byte alpha = 0;
                if (pair)
                {
                    int wb = Weight(v, key.Item1), wo = Weight(v, key.Item2);
                    alpha = wb + wo == 0 ? (byte)0 : (byte)Math.Round(255.0 * wo / (wb + wo));
                }
                pb.Add(mesh, v, alpha);
            }
        }
        return [.. order.Select(k => parts[k].Build(k.Item1, k.Item2))];
    }

    private sealed class PartBuilder
    {
        private readonly Dictionary<(int Vertex, byte Alpha), int> _map = [];
        private readonly List<Vector3> _p = [], _n = [];
        private readonly List<Vector2> _uv = [];
        private readonly List<byte> _alpha = [];
        private readonly List<int> _tris = [];

        public void Add(SwtorTerrainMesh mesh, int v, byte alpha)
        {
            if (!_map.TryGetValue((v, alpha), out int i))
            {
                i = _p.Count;
                _map[(v, alpha)] = i;
                _p.Add(mesh.Positions[v]); _n.Add(mesh.Normals[v]); _uv.Add(mesh.Uv[v]); _alpha.Add(alpha);
            }
            _tris.Add(i);
        }

        public SwtorTerrainPart Build(int baseLayer, int overlay) =>
            new(baseLayer, overlay, [.. _p], [.. _n], [.. _uv], overlay >= 0 ? [.. _alpha] : null, [.. _tris]);
    }

    private static void AddTriangle(List<int> triangles, int a, int b, int c, bool flip)
    {
        triangles.Add(a);
        triangles.Add(flip ? c : b);
        triangles.Add(flip ? b : c);
    }

    /// <summary>
    /// Materiau du sol : celui qui couvre le plus de surface « vers le haut » (normale a plus de 0,8 en Z) a moins de <paramref name="band"/> du terrain
    /// parmi les meshes de la scene (roches, dalles, routes posees sur le sol : le terrain lui-meme n'a pas de materiau lisible). Null si aucun.
    /// Les textures absentes (damier), les additifs et les translucides sont ignores.
    /// </summary>
    internal static string? ChooseGroundTexture(IReadOnlyList<SceneMesh> meshes, IEnumerable<Vector3> terrainVertices, float band, Func<string, bool> usable, out SurfaceAlpha alpha)
    {
        alpha = SurfaceAlpha.Opaque;
        float cell = MathF.Max(band * 4f, 1e-3f);
        var ground = new Dictionary<(int, int), float>();
        foreach (Vector3 p in terrainVertices)
        {
            var key = ((int)MathF.Floor(p.X / cell), (int)MathF.Floor(p.Y / cell));
            ground[key] = ground.TryGetValue(key, out float z) ? MathF.Max(z, p.Z) : p.Z;
        }

        var area = new Dictionary<string, (double Area, SurfaceAlpha Alpha)>(StringComparer.Ordinal);
        foreach (SceneMesh m in meshes)
        {
            if (m.Alpha is SurfaceAlpha.Additive or SurfaceAlpha.Blend or SurfaceAlpha.Multiply || !usable(m.Texture)) continue;
            for (int t = 0; t + 2 < m.Triangles.Length; t += 3)
            {
                Vector3 a = m.Positions[m.Triangles[t]], b = m.Positions[m.Triangles[t + 1]], c = m.Positions[m.Triangles[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                float len = n.Length();
                if (len < 1e-12f || n.Z / len < 0.8f) continue;
                Vector3 centre = (a + b + c) / 3f;
                var key = ((int)MathF.Floor(centre.X / cell), (int)MathF.Floor(centre.Y / cell));
                if (!ground.TryGetValue(key, out float gz) || MathF.Abs(centre.Z - gz) > band) continue;
                area[m.Texture] = (area.GetValueOrDefault(m.Texture).Area + len * 0.5, m.Alpha);
            }
        }
        if (area.Count == 0) return null;
        var best = area.OrderByDescending(kv => kv.Value.Area).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
        alpha = best.Value.Alpha == SurfaceAlpha.Auto ? SurfaceAlpha.Opaque : best.Value.Alpha;
        return best.Key;
    }

    /// <summary>Version lissee d'une texture : moyenne par bloc en 8 x 8, chaque texel tire aux deux tiers vers la couleur moyenne, alpha opaque.</summary>
    internal static Kotor.Core.Textures.KotorTexture SmoothGroundTexture(Kotor.Core.Textures.KotorTexture src)
    {
        const int n = 16;
        var sum = new double[n * n * 3];
        var count = new int[n * n];
        double[] total = new double[3];
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                int cell = (y * n / src.Height) * n + x * n / src.Width;
                int o = (y * src.Width + x) * 4;
                for (int c = 0; c < 3; c++) { sum[cell * 3 + c] += src.Rgba[o + c]; total[c] += src.Rgba[o + c]; }
                count[cell]++;
            }
        double pixels = Math.Max(1, src.Width * src.Height);
        var rgba = new byte[n * n * 4];
        for (int cell = 0; cell < n * n; cell++)
        {
            for (int c = 0; c < 3; c++)
            {
                double local = count[cell] > 0 ? sum[cell * 3 + c] / count[cell] : total[c] / pixels;
                rgba[cell * 4 + c] = (byte)Math.Clamp(Math.Round(total[c] / pixels * 0.5 + local * 0.5), 0, 255);
            }
            rgba[cell * 4 + 3] = 255;
        }
        return new Kotor.Core.Textures.KotorTexture(n, n, rgba, false, Kotor.Core.Textures.TxiInfo.Empty);
    }}

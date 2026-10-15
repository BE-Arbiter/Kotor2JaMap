// Inspiré de la démarche de q3map2 / NetRadiant-custom (écriture des lumps BSP, GPL-2.0) : aucun code copié.
// Format : RBSP version 1 (OpenJK, codemp/qcommon/qfiles.h). Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using System.Text;

namespace Kotor.Compiler.Bsp;

/// <summary>
/// Écrit un <see cref="BspTree"/> en BSP RBSP v1 (brushes de collision, surfaces triangle soup optionnelles, vis à un seul cluster, pas de lumière).
/// Tout est construit en mémoire avant écriture : en cas de dépassement de limite, rien n'est écrit.
/// </summary>
public static class BspWriter
{
    public const int LumpCount = 18; // HEADER_LUMPS d'OpenJK (le plan en annonçait 17)
    public const int HeaderSize = 8 + LumpCount * 8;

    public const int LumpEntities = 0, LumpShaders = 1, LumpPlanes = 2, LumpNodes = 3, LumpLeafs = 4,
        LumpLeafSurfaces = 5, LumpLeafBrushes = 6, LumpModels = 7, LumpBrushes = 8, LumpBrushSides = 9,
        LumpDrawVerts = 10, LumpDrawIndexes = 11, LumpFogs = 12, LumpSurfaces = 13, LumpLightmaps = 14,
        LumpLightGrid = 15, LumpVisibility = 16, LumpLightArray = 17;

    public const string CollisionShader = CompilerNames.CollisionShader;
    private const int ContentsSolid = 1;
    /// <summary>SURF_SKY de JA : bit 0x2000, releve dans les BSP d'origine (textures/skies/yavin = 0x182000 dans academy1.bsp). Contents = 0 (non solide).</summary>
    public const int SurfaceSky = 0x2000;
    private const int SurfaceTriangleSoup = 3; // MST_TRIANGLE_SOUP
    private const int LightmapByVertex = -3;
    private const string DefaultEntities = "{\n\"classname\" \"worldspawn\"\n}\n";

    public static void Write(BspTree tree, Stream output, string? entities = null, Limits? limits = null,
        IReadOnlyList<RenderSurface>? surfaces = null, VertexLighting? lighting = null, GlobalFog? fog = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(output);
        limits ??= Limits.Vanilla;

        var lumps = Build(tree, entities ?? DefaultEntities, limits, surfaces ?? [], lighting ?? VertexLighting.Default, fog);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RBSP"));
        w.Write(1);
        int ofs = HeaderSize;
        var placed = new (int Ofs, int Len)[LumpCount];
        for (int i = 0; i < LumpCount; i++)
        {
            placed[i] = (ofs, lumps[i].Length);
            ofs += (lumps[i].Length + 3) & ~3;
        }
        foreach (var (o, l) in placed) { w.Write(o); w.Write(l); }
        for (int i = 0; i < LumpCount; i++)
        {
            w.Write(lumps[i]);
            for (int pad = lumps[i].Length; (pad & 3) != 0; pad++) w.Write((byte)0);
        }
        w.Flush();
        ms.Position = 0;
        ms.CopyTo(output);
    }

    private static byte[][] Build(BspTree tree, string entities, Limits limits, IReadOnlyList<RenderSurface> surfaces, VertexLighting lighting, GlobalFog? fog)
    {
        var lumps = new byte[LumpCount][];
        for (int i = 0; i < LumpCount; i++) lumps[i] = [];

        // Plans : ceux de l'arbre, plus les 6 plans axiaux de chaque brush (Q3 borne les brushes avec les 6 premiers côtés).
        var planes = new List<BspPlane>(tree.Planes);
        var index = new Dictionary<(float, float, float, float), int>();
        for (int i = 0; i < planes.Count; i++) index.TryAdd(Key(planes[i]), i);
        int Plane(BspPlane p)
        {
            var k = Key(p);
            if (index.TryGetValue(k, out int i)) return i;
            planes.Add(p);
            return index[k] = planes.Count - 1;
        }

        // Côtés de brush.
        var sides = new List<(int Plane, int Shader, int DrawSurf)>();
        var brushes = new List<(int First, int Count)>();
        foreach (Brush b in tree.Brushes)
        {
            int first = sides.Count;
            sides.Add((Plane(new BspPlane(-Vector3.UnitX, -b.Min.X)), 0, -1));
            sides.Add((Plane(new BspPlane(Vector3.UnitX, b.Max.X)), 0, -1));
            sides.Add((Plane(new BspPlane(-Vector3.UnitY, -b.Min.Y)), 0, -1));
            sides.Add((Plane(new BspPlane(Vector3.UnitY, b.Max.Y)), 0, -1));
            sides.Add((Plane(new BspPlane(-Vector3.UnitZ, -b.Min.Z)), 0, -1));
            sides.Add((Plane(new BspPlane(Vector3.UnitZ, b.Max.Z)), 0, -1));
            foreach (BspPlane p in b.Planes) sides.Add((Plane(p), 0, -1));
            brushes.Add((first, sides.Count - first));
        }

        // Nœuds : arbre vide ou réduit à une feuille -> un nœud synthétique (le moteur exige un nœud racine).
        var nodes = tree.Nodes.ToList();
        var leaves = tree.Leaves.ToList();
        if (nodes.Count == 0)
        {
            if (leaves.Count == 0) leaves.Add(new BspLeaf([], Vector3.Zero, Vector3.Zero));
            nodes.Add(new BspNode(Plane(new BspPlane(Vector3.UnitZ, 0f)), -1, -1));
        }

        // Surfaces de rendu : rattachees a leur feuille ; la feuille est agrandie pour englober ses surfaces
        // (le moteur s'en sert pour le culling) et passe en cluster 0 (les feuilles de cluster -1 ne sont jamais dessinees).
        var shaderIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [CollisionShader] = 0 };
        var shaderNames = new List<string> { CollisionShader };
        var skyShaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var leafSurfaces = new List<int>?[leaves.Count];
        var leafLo = new Vector3[leaves.Count];
        var leafHi = new Vector3[leaves.Count];
        for (int i = 0; i < leaves.Count; i++) { leafLo[i] = new Vector3(float.MaxValue); leafHi[i] = new Vector3(float.MinValue); }
        for (int si = 0; si < surfaces.Count; si++)
        {
            RenderSurface sf = surfaces[si];
            if (sf.Leaves.Count == 0) throw new ArgumentException($"Surface {si} sans feuille.");
            foreach (int leaf in sf.Leaves)
                if (leaf < 0 || leaf >= leaves.Count)
                    throw new ArgumentException($"Surface {si} rattachee a une feuille inexistante ({leaf}).");
            if (!shaderIndex.ContainsKey(sf.Shader)) { shaderIndex[sf.Shader] = shaderNames.Count; shaderNames.Add(sf.Shader); }
            if (sf.Sky) skyShaders.Add(sf.Shader);
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (Vector3 v in sf.Positions) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
            foreach (int leaf in sf.Leaves)
            {
                (leafSurfaces[leaf] ??= []).Add(si);
                leafLo[leaf] = Vector3.Min(leafLo[leaf], lo);
                leafHi[leaf] = Vector3.Max(leafHi[leaf], hi);
            }
        }
        for (int i = 0; i < leaves.Count; i++)
        {
            if (leafSurfaces[i] is null) continue;
            bool replace = tree.Brushes.Count == 0; // feuille sans region propre : bornes = celles des surfaces
            leaves[i] = leaves[i] with
            {
                Min = replace ? leafLo[i] : Vector3.Min(leaves[i].Min, leafLo[i]),
                Max = replace ? leafHi[i] : Vector3.Max(leaves[i].Max, leafHi[i]),
            };
        }
        int totalVerts = surfaces.Sum(s => s.Positions.Length);
        int totalIndexes = surfaces.Sum(s => s.Indices.Length);
        int leafSurfaceCount = leafSurfaces.Sum(l => l?.Count ?? 0);

        int leafBrushCount = leaves.Sum(l => l.BrushIndices.Count);
        limits.CheckPlanes(planes.Count);
        limits.CheckNodes(nodes.Count);
        limits.CheckLeaves(leaves.Count);
        limits.CheckBrushes(brushes.Count);
        CheckMax("MAX_MAP_BRUSHSIDES", sides.Count, limits.MaxBrushSides);
        CheckMax("MAX_MAP_LEAFBRUSHES", leafBrushCount, limits.MaxLeafBrushes);
        LimitCheck.CheckRenderLimits(shaderNames.Count, surfaces.Count, totalVerts, totalIndexes, leafSurfaceCount, limits.MaxLeafSurfaces);
        foreach (RenderSurface sf in surfaces) LimitCheck.CheckSurface(sf.Shader, sf.Positions.Length, sf.Indices.Length);

        // Bornes des nœuds : union des bornes des enfants.
        var nodeMin = new Vector3[nodes.Count];
        var nodeMax = new Vector3[nodes.Count];
        var done = new bool[nodes.Count];
        void Bounds(int n)
        {
            if (done[n]) return;
            done[n] = true;
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (int c in new[] { nodes[n].FrontChild, nodes[n].BackChild })
            {
                Vector3 cl, ch;
                if (c >= 0) { Bounds(c); cl = nodeMin[c]; ch = nodeMax[c]; }
                else { cl = leaves[-1 - c].Min; ch = leaves[-1 - c].Max; }
                lo = Vector3.Min(lo, cl); hi = Vector3.Max(hi, ch);
            }
            nodeMin[n] = lo; nodeMax[n] = hi;
        }
        for (int n = 0; n < nodes.Count; n++) Bounds(n);

        Vector3 worldMin = nodeMin[0], worldMax = nodeMax[0];

        lumps[LumpEntities] = Encoding.ASCII.GetBytes(entities + "\0");
        lumps[LumpShaders] = Make(w =>
        {
            for (int i = 0; i < shaderNames.Count; i++)
            {
                byte[] name = new byte[64];
                Encoding.ASCII.GetBytes(shaderNames[i]).Take(63).ToArray().CopyTo(name, 0);
                w.Write(name); w.Write(skyShaders.Contains(shaderNames[i]) ? SurfaceSky : 0); w.Write(i == 0 ? ContentsSolid : 0);
            }
        });
        lumps[LumpPlanes] = Make(w =>
        {
            foreach (BspPlane p in planes) { Vec(w, p.Normal); w.Write(p.Dist); }
        });
        lumps[LumpNodes] = Make(w =>
        {
            for (int n = 0; n < nodes.Count; n++)
            {
                w.Write(nodes[n].PlaneIndex); w.Write(nodes[n].FrontChild); w.Write(nodes[n].BackChild);
                IntVec(w, nodeMin[n], floor: true); IntVec(w, nodeMax[n], floor: false);
            }
        });
        lumps[LumpLeafs] = Make(w =>
        {
            int firstLeafBrush = 0, firstLeafSurface = 0, leafIndex = -1;
            foreach (BspLeaf l in leaves)
            {
                leafIndex++;
                int nSurf = leafSurfaces[leafIndex]?.Count ?? 0;
                w.Write(l.IsSolid && nSurf == 0 ? -1 : 0); // cluster
                w.Write(0);                  // area
                IntVec(w, l.Min, floor: true); IntVec(w, l.Max, floor: false);
                w.Write(firstLeafSurface); w.Write(nSurf); // leafsurfaces
                firstLeafSurface += nSurf;
                w.Write(firstLeafBrush); w.Write(l.BrushIndices.Count);
                firstLeafBrush += l.BrushIndices.Count;
            }
        });
        lumps[LumpLeafSurfaces] = Make(w =>
        {
            foreach (var list in leafSurfaces) if (list is not null) foreach (int si in list) w.Write(si);
        });
        lumps[LumpLeafBrushes] = Make(w =>
        {
            foreach (BspLeaf l in leaves) foreach (int bi in l.BrushIndices) w.Write(bi);
        });
        lumps[LumpFogs] = Make(w =>
        {
            if (fog is null) return;
            // dfog_t : shader[64], brushNum, visibleSide ; brushNum = -1 = brouillard global (comme les cartes d origine).
            byte[] name = new byte[64];
            Encoding.ASCII.GetBytes(fog.Shader).Take(63).ToArray().CopyTo(name, 0);
            w.Write(name); w.Write(-1); w.Write(-1);
        });
        lumps[LumpModels] = Make(w =>
        {
            Vec(w, worldMin); Vec(w, worldMax);
            w.Write(0); w.Write(surfaces.Count); // surfaces
            w.Write(0); w.Write(brushes.Count);  // brushes
        });
        lumps[LumpBrushes] = Make(w =>
        {
            foreach (var (first, count) in brushes) { w.Write(first); w.Write(count); w.Write(0); }
        });
        lumps[LumpBrushSides] = Make(w =>
        {
            foreach (var (p, s, d) in sides) { w.Write(p); w.Write(s); w.Write(d); }
        });
        lumps[LumpVisibility] = Make(w =>
        {
            // Sans lump de visibilite le moteur a 0 cluster et ne dessine rien : un cluster, toujours visible (comme q3map2 -novis).
            w.Write(1); w.Write(8);
            for (int i = 0; i < 8; i++) w.Write((byte)0xFF);
        });
        lumps[LumpDrawVerts] = Make(w =>
        {
            foreach (RenderSurface sf in surfaces)
                for (int i = 0; i < sf.Positions.Length; i++)
                {
                    Vec(w, sf.Positions[i]);
                    w.Write(sf.Uvs[i].X); w.Write(sf.Uvs[i].Y);
                    // lightmap[4][2] : style 0 = UV1 si la surface a une lightmap, sinon 0.
                    if (sf.LightmapUvs is { } lm) { w.Write(lm[i].X); w.Write(lm[i].Y); } else { w.Write(0f); w.Write(0f); }
                    for (int k = 2; k < 8; k++) w.Write(0f);
                    Vec(w, sf.Normals[i]);
                    // couleurs[4][4] : style 0. Lightmap : blanc (l'eclairage vient de la lightmap via le shader) ; ciel : blanc ; sinon eclairage cuit.
                    byte[] rgba = sf.Sky || sf.LightmapUvs is not null ? [255, 255, 255, 255] : lighting.Evaluate(sf.Positions[i], sf.Normals[i], sf.DoubleSided);
                    w.Write(rgba);
                    for (int k = 4; k < 16; k++) w.Write((byte)255);
                }
        });
        lumps[LumpDrawIndexes] = Make(w =>
        {
            foreach (RenderSurface sf in surfaces) foreach (int ix in sf.Indices) w.Write(ix);
        });
        lumps[LumpSurfaces] = Make(w =>
        {
            int firstVert = 0, firstIndex = 0;
            foreach (RenderSurface sf in surfaces)
            {
                w.Write(shaderIndex[sf.Shader]); w.Write(fog is null ? -1 : 0); w.Write(SurfaceTriangleSoup);
                w.Write(firstVert); w.Write(sf.Positions.Length); w.Write(firstIndex); w.Write(sf.Indices.Length);
                w.Write(new byte[] { 0, 255, 255, 255 });              // lightmapStyles
                w.Write(new byte[] { 0, 255, 255, 255 });              // vertexStyles
                for (int k = 0; k < 4; k++) w.Write(LightmapByVertex); // lightmapNum
                for (int k = 0; k < 8; k++) w.Write(0);                // lightmapX[4], lightmapY[4]
                w.Write(0); w.Write(0);                                // lightmapWidth/Height
                for (int k = 0; k < 12; k++) w.Write(0f);              // lightmapOrigin + lightmapVecs[3]
                w.Write(0); w.Write(0);                                // patchWidth/Height
                firstVert += sf.Positions.Length; firstIndex += sf.Indices.Length;
            }
        });
        return lumps;
    }

    private static (float, float, float, float) Key(BspPlane p) =>
        (MathF.Round(p.Normal.X, 4), MathF.Round(p.Normal.Y, 4), MathF.Round(p.Normal.Z, 4), MathF.Round(p.Dist, 3));

    private static void CheckMax(string name, int count, int max)
    {
        LimitCheck.Check(name, count, max, collisionRelated: true);
    }

    private static byte[] Make(Action<BinaryWriter> fill)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        fill(w);
        w.Flush();
        return ms.ToArray();
    }

    private static void Vec(BinaryWriter w, Vector3 v) { w.Write(v.X); w.Write(v.Y); w.Write(v.Z); }

    private static void IntVec(BinaryWriter w, Vector3 v, bool floor)
    {
        foreach (float f in new[] { v.X, v.Y, v.Z }) w.Write((int)(floor ? MathF.Floor(f) : MathF.Ceiling(f)));
    }
}

using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using BspCompiler = Kotor.Compiler.Compiler;

namespace Kotor.Compiler.Tests;

/// <summary>
/// .map d'edition : geometrie visible texturee (un prisme fin par triangle de rendu) dans un func_group « _kotor_visual ».
/// Le lecteur de .map et la formule de projection de texture ci-dessous sont des reecritures independantes de celles de q3map2 / Radiant.
/// </summary>
public sealed class VisualMapWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "k2j_visualmap_" + Guid.NewGuid().ToString("N"));

    public VisualMapWriterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    // ---- lecteur de .map (format Quake 3 classique) ----

    private sealed record Face(Vector3 P0, Vector3 P1, Vector3 P2, string Shader, float ShiftX, float ShiftY, float Rot, float ScaleX, float ScaleY)
    {
        public Vector3 Normal => Vector3.Normalize(Vector3.Cross(P0 - P1, P2 - P1));
        public float Dist => Vector3.Dot(Normal, P1);
    }

    private sealed record Ent(Dictionary<string, string> Keys, List<List<Face>> Brushes);

    private static readonly Regex FaceRx = new(
        @"^\(\s*(\S+) (\S+) (\S+)\s*\)\s*\(\s*(\S+) (\S+) (\S+)\s*\)\s*\(\s*(\S+) (\S+) (\S+)\s*\)\s+(\S+) (\S+) (\S+) (\S+) (\S+) (\S+) (\S+) (\S+) (\S+)\s*$");

    private static float Fl(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static List<Ent> Parse(string map)
    {
        var ents = new List<Ent>();
        Ent? cur = null;
        List<Face>? brush = null;
        foreach (string raw in map.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//")) continue;
            if (line == "{")
            {
                if (cur is null) { cur = new Ent([], []); ents.Add(cur); }
                else brush = [];
            }
            else if (line == "}")
            {
                if (brush is not null) { cur!.Brushes.Add(brush); brush = null; }
                else cur = null;
            }
            else if (line.StartsWith('"'))
            {
                var m = Regex.Match(line, "^\"([^\"]*)\" \"([^\"]*)\"$");
                Assert.True(m.Success, line);
                cur!.Keys[m.Groups[1].Value] = m.Groups[2].Value;
            }
            else
            {
                var m = FaceRx.Match(line);
                Assert.True(m.Success, "face line: " + line);
                var g = m.Groups;
                brush!.Add(new Face(
                    new(Fl(g[1].Value), Fl(g[2].Value), Fl(g[3].Value)), new(Fl(g[4].Value), Fl(g[5].Value), Fl(g[6].Value)),
                    new(Fl(g[7].Value), Fl(g[8].Value), Fl(g[9].Value)), g[10].Value,
                    Fl(g[11].Value), Fl(g[12].Value), Fl(g[13].Value), Fl(g[14].Value), Fl(g[15].Value)));
            }
        }
        return ents;
    }

    private static Ent Visual(string map) => Parse(map).Single(e => e.Keys.GetValueOrDefault("_kotor_visual") == "1");

    /// <summary>Sommets du brush (intersections de trois plans, gardees si dans tous les demi-espaces) ; verifie qu'il est borne et convexe.</summary>
    private static List<Vector3> Vertices(List<Face> brush)
    {
        var verts = new List<Vector3>();
        int n = brush.Count;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                for (int k = j + 1; k < n; k++)
                {
                    Vector3 a = brush[i].Normal, b = brush[j].Normal, c = brush[k].Normal;
                    float det = Vector3.Dot(a, Vector3.Cross(b, c));
                    if (MathF.Abs(det) < 1e-4f) continue;
                    Vector3 p = (brush[i].Dist * Vector3.Cross(b, c) + brush[j].Dist * Vector3.Cross(c, a) + brush[k].Dist * Vector3.Cross(a, b)) / det;
                    if (brush.All(f => Vector3.Dot(f.Normal, p) <= f.Dist + 0.01f) && !verts.Any(v => Vector3.Distance(v, p) < 0.01f)) verts.Add(p);
                }
        return verts;
    }

    private static void AssertValidPrism(List<Face> brush, float expectedThickness)
    {
        Assert.Equal(5, brush.Count);
        var verts = Vertices(brush);
        Assert.Equal(6, verts.Count); // prisme triangulaire
        Vector3 centre = verts.Aggregate(Vector3.Zero, (s, v) => s + v) / verts.Count;
        foreach (Face f in brush)
        {
            Assert.True(Vector3.Dot(f.Normal, centre) < f.Dist, "la normale d'un plan doit pointer vers l'exterieur");
            Assert.True(verts.Count(v => MathF.Abs(Vector3.Dot(f.Normal, v) - f.Dist) < 0.01f) >= 3, "chaque plan porte une vraie face");
        }
        // Epaisseur = ecart entre les deux grandes faces (normales opposees).
        Face top = brush[0];
        Face bottom = brush.Single(f => Vector3.Dot(f.Normal, top.Normal) < -0.999f);
        Assert.Equal(expectedThickness, top.Dist + bottom.Dist, 0.01f);
        Assert.True(expectedThickness > 0);
    }

    // ---- projection de texture de q3map2 (QuakeTextureVecs + TextureAxisFromPlane) ----

    private static readonly float[][] BaseAxis =
    [
        [0, 0, 1], [1, 0, 0], [0, -1, 0],
        [0, 0, -1], [1, 0, 0], [0, -1, 0],
        [1, 0, 0], [0, 1, 0], [0, 0, -1],
        [-1, 0, 0], [0, 1, 0], [0, 0, -1],
        [0, 1, 0], [1, 0, 0], [0, 0, -1],
        [0, -1, 0], [1, 0, 0], [0, 0, -1],
    ];

    private static Vector2 Q3TexCoord(Face f, Vector3 p, int width, int height)
    {
        Vector3 nrm = f.Normal;
        float best = 0; int bestAxis = 0;
        for (int i = 0; i < 6; i++)
        {
            float d = nrm.X * BaseAxis[i * 3][0] + nrm.Y * BaseAxis[i * 3][1] + nrm.Z * BaseAxis[i * 3][2];
            if (d > best) { best = d; bestAxis = i; }
        }
        float[][] vecs = [(float[])BaseAxis[bestAxis * 3 + 1].Clone(), (float[])BaseAxis[bestAxis * 3 + 2].Clone()];
        double ang = f.Rot / 180.0 * Math.PI;
        float sinv = (float)Math.Sin(ang), cosv = (float)Math.Cos(ang);
        int sv = vecs[0][0] != 0 ? 0 : vecs[0][1] != 0 ? 1 : 2;
        int tv = vecs[1][0] != 0 ? 0 : vecs[1][1] != 0 ? 1 : 2;
        for (int i = 0; i < 2; i++)
        {
            float ns = cosv * vecs[i][sv] - sinv * vecs[i][tv];
            float nt = sinv * vecs[i][sv] + cosv * vecs[i][tv];
            vecs[i][sv] = ns; vecs[i][tv] = nt;
        }
        float s = (vecs[0][0] * p.X + vecs[0][1] * p.Y + vecs[0][2] * p.Z) / f.ScaleX + f.ShiftX;
        float t = (vecs[1][0] * p.X + vecs[1][1] * p.Y + vecs[1][2] * p.Z) / f.ScaleY + f.ShiftY;
        return new Vector2(s / width, t / height);
    }

    private static float WrapDiff(float a, float b)
    {
        float d = a - b;
        return MathF.Abs(d - MathF.Round(d)); // les motifs se repetent : on compare modulo 1
    }

    // ---- scenes ----

    private static SurfaceInput Wall(Vector3[] pos, Vector2[] uv, string shader = "textures/jk/__jk_light", string? tex = "textures/jk/01wbolts",
        int w = 64, int h = 128, bool doubleSided = false)
    {
        Vector3 n = Vector3.Normalize(Vector3.Cross(pos[1] - pos[0], pos[2] - pos[0]));
        return new SurfaceInput(shader, pos, [n, n, n, n], uv, null, [0, 1, 2, 0, 2, 3], doubleSided)
        {
            VisualTexture = tex, VisualTextureSize = new Vector2(w, h),
        };
    }

    private static string Write(params SurfaceInput[] surfaces)
    {
        var sw = new StringWriter { NewLine = "\n" };
        MapWriter.Write(sw, TestScenes.Entities(), [], [], surfaces);
        return sw.ToString();
    }

    [Fact]
    public void Floor_BecomesTwoThinPrisms_WithRealTextureOnTheVisibleFaceAndCaulkElsewhere()
    {
        string map = Write(Wall([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            shader: "textures/kotor/floor__lm01", tex: "textures/kotor/floor"));
        Ent e = Visual(map);

        Assert.Equal("func_group", e.Keys["classname"]);
        Assert.Equal(2, e.Brushes.Count);
        foreach (var b in e.Brushes)
        {
            AssertValidPrism(b, VisualMapWriter.DefaultThickness);
            Assert.Single(b, f => f.Shader == "kotor/floor"); // la face visible porte la texture de base, pas le shader a lightmap
            Assert.Equal(4, b.Count(f => f.Shader == "common/caulk"));
            Assert.DoesNotContain(b, f => f.Shader.Contains("__lm"));
        }
        // Dimensions : la face visible (normale +Z) est dans le plan z = 0, l'epaisseur est sous le sol.
        var all = e.Brushes.SelectMany(Vertices).ToList();
        Assert.Equal(0f, all.Max(v => v.Z), 0.01f);
        Assert.Equal(-VisualMapWriter.DefaultThickness, all.Min(v => v.Z), 0.01f);
        Assert.Equal(0f, all.Min(v => v.X), 0.01f); Assert.Equal(100f, all.Max(v => v.X), 0.01f);
        Assert.Equal(0f, all.Min(v => v.Y), 0.01f); Assert.Equal(100f, all.Max(v => v.Y), 0.01f);
        Assert.All(e.Brushes, b => Assert.True(b.Single(f => f.Shader == "kotor/floor").Normal.Z > 0.999f));
    }

    [Fact]
    public void VisualGroup_IsSeparateFromCollision_AndMarked()
    {
        string map = Write(Wall([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]));
        var ents = Parse(map);
        Assert.Equal(TestScenes.Entities().Count + 1, ents.Count);
        Assert.Equal("worldspawn", ents[0].Keys["classname"]);
        Assert.Empty(ents[0].Brushes); // aucune geometrie visible dans le worldspawn (pas de doublon a la recompilation)
        Assert.Single(ents, e => e.Keys.ContainsKey("_kotor_visual"));
        Assert.Contains("_kotor_visual", map);
    }

    [Fact]
    public void WithoutVisualSurfaces_MapIsByteIdenticalToTheCollisionOnlyMap()
    {
        var planes = SubModelTests.BoxPlanes(new(0, 0, 0), new(10, 10, 10));
        var brushes = new[] { new Brush(planes, new(0, 0, 0), new(10, 10, 10)) };
        string before = MapWriter.Write(TestScenes.Entities(), brushes, []);
        var sw = new StringWriter { NewLine = "\n" };
        MapWriter.Write(sw, TestScenes.Entities(), brushes, [], null);
        Assert.Equal(before, sw.ToString());
        Assert.DoesNotContain("_kotor_visual", before);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Uv_AreReproducedByTheQuake3ProjectionOfTheEditor(int kind)
    {
        // Sol tourne, mur axial, mur diagonal (45 degres), plafond a l'envers : textures de 64 x 128.
        Vector3[] pos;
        Vector2[] uv;
        switch (kind)
        {
            case 0: // sol, UV tournees de 30 degres, echelle 1/37 par unite
            {
                pos = [new(10, 20, 5), new(110, 20, 5), new(110, 90, 5), new(10, 90, 5)];
                uv = pos.Select(p => Rot(new Vector2(p.X, p.Y) / 37f, 30f)).ToArray();
                break;
            }
            case 1: // mur plein sud, v vers le haut
                pos = [new(0, 50, 0), new(200, 50, 0), new(200, 50, 120), new(0, 50, 120)];
                uv = pos.Select(p => new Vector2(p.X / 100f, 1f - p.Z / 120f)).ToArray();
                break;
            case 2: // mur oblique (pas a 45 degres : a 45 degres l'axe dominant depend du bruit de calcul), u le long du mur
                pos = [new(0, 0, 0), new(100, 50, 0), new(100, 50, 80), new(0, 0, 80)];
                uv = pos.Select(p => new Vector2((p.X * 0.894427f + p.Y * 0.447214f) / 100f, 1f - p.Z / 80f)).ToArray();
                break;
            default: // plafond (normale -Z), UV avec miroir en u
                pos = [new(0, 0, 200), new(0, 100, 200), new(100, 100, 200), new(100, 0, 200)];
                uv = pos.Select(p => new Vector2(-p.X / 64f, p.Y / 50f)).ToArray();
                break;
        }
        SurfaceInput s = Wall(pos, uv, w: 64, h: 128);
        Ent e = Visual(Write(s));
        Assert.Equal(2, e.Brushes.Count);

        float worst = 0;
        foreach (var b in e.Brushes)
        {
            Face vis = b.Single(f => f.Shader == "jk/01wbolts");
            foreach (Vector3 p in pos)
            {
                // Le sommet du quad appartient au plan visible seulement s'il est dans ce triangle ; on teste donc les sommets de la face visible.
                if (MathF.Abs(Vector3.Dot(vis.Normal, p) - vis.Dist) > 0.01f) continue;
                Vector2 got = Q3TexCoord(vis, p, 64, 128);
                int idx = Array.IndexOf(pos, p);
                worst = MathF.Max(worst, MathF.Max(WrapDiff(got.X, uv[idx].X), WrapDiff(got.Y, uv[idx].Y)));
            }
        }
        Assert.True(worst < 0.002f, $"ecart de texture maximal {worst} (en tour de texture)");
    }

    private static Vector2 Rot(Vector2 v, float deg)
    {
        float a = deg * MathF.PI / 180f, c = MathF.Cos(a), sn = MathF.Sin(a);
        return new Vector2(c * v.X - sn * v.Y, sn * v.X + c * v.Y);
    }

    [Fact]
    public void DoubleSidedSurface_TexturesBothBigFaces()
    {
        string map = Write(Wall([new(0, 0, 0), new(100, 0, 0), new(100, 0, 100), new(0, 0, 100)], [new(0, 1), new(1, 1), new(1, 0), new(0, 0)], doubleSided: true));
        Ent e = Visual(map);
        Assert.All(e.Brushes, b => Assert.Equal(2, b.Count(f => f.Shader == "jk/01wbolts")));
        Assert.All(e.Brushes, b => AssertValidPrism(b, VisualMapWriter.DefaultThickness));
    }

    [Fact]
    public void MissingVisualTexture_FallsBackToTheShaderName()
    {
        var s = Wall([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            shader: "textures/kotor/plain", tex: null);
        Assert.All(Visual(Write(s)).Brushes, b => Assert.Single(b, f => f.Shader == "kotor/plain"));
    }

    [Fact]
    public void DegenerateTriangles_AreSkipped()
    {
        var s = Wall([new(0, 0, 0), new(100, 0, 0), new(200, 0, 0), new(300, 0, 0)], [new(0, 0), new(1, 0), new(2, 0), new(3, 0)]);
        var sw = new StringWriter { NewLine = "\n" };
        VisualMapStats st = VisualMapWriter.Write(sw, [s]);
        Assert.Equal(0, st.Brushes);
        Assert.Equal(2, st.DegenerateTriangles);
    }

    [Fact]
    public void Needle_TooThinForTheMapPrecision_IsSkipped()
    {
        // 480 de haut mais 0,001 de large : a l'arrondi du .map les trois points deviennent colineaires (q3map2 : « degenerate plane »).
        var s = Wall([new(0, 0, 0), new(0, 0.001f, 0), new(0, 0, 480), new(0, 0.001f, 480)], [new(0, 0), new(1, 0), new(0, 1), new(1, 1)]);
        VisualMapStats st = VisualMapWriter.Write(new StringWriter(), [s]);
        Assert.Equal(0, st.Brushes);
    }

    [Fact]
    public void EditMap_DoorBrushesToo_UseNamesWithoutTexturesPrefix()
    {
        var ents = new List<IReadOnlyDictionary<string, string>>
        {
            new Dictionary<string, string> { ["classname"] = "worldspawn" },
            new Dictionary<string, string> { ["classname"] = "func_door", ["model"] = "*1" },
        };
        var door = new BspSubModel([new Brush(SubModelTests.BoxPlanes(new(0, 0, 0), new(10, 10, 10)), new(0, 0, 0), new(10, 10, 10))], "textures/system/clip");
        var sw = new StringWriter { NewLine = "\n" };
        MapWriter.Write(sw, ents, [], [door], []);
        Assert.Contains(" system/clip ", sw.ToString());
        Assert.DoesNotContain("textures/", sw.ToString());
        // Sans geometrie visible demandee, le .map du pk3 garde le nom complet.
        Assert.Contains(" textures/system/clip ", MapWriter.Write(ents, [], [door]));
    }

    [Fact]
    public void BrushCount_IsBounded_KeepingTheLargestTriangles()
    {
        var pos = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
        for (int i = 0; i < 300; i++)
        {
            float size = 10 + i; // les triangles grandissent
            int b = pos.Count;
            pos.AddRange([new(i * 400, 0, 0), new(i * 400 + size, 0, 0), new(i * 400, size, 0)]);
            uv.AddRange([new(0, 0), new(1, 0), new(0, 1)]);
            idx.AddRange([b, b + 1, b + 2]);
        }
        var s = new SurfaceInput("textures/kotor/floor", [.. pos], pos.Select(_ => Vector3.UnitZ).ToArray(), [.. uv], null, [.. idx]);
        var sw = new StringWriter { NewLine = "\n" };
        VisualMapStats st = VisualMapWriter.Write(sw, [s], maxBrushes: 100);

        Assert.Equal(100, st.Brushes);
        Assert.Equal(200, st.CappedTriangles);
        Assert.Equal(100, Parse(sw.ToString()).Single().Brushes.Count);
        // Ce sont les 100 plus grands (x >= 200 * 400) qui restent.
        Assert.All(Parse(sw.ToString()).Single().Brushes, b => Assert.True(Vertices(b).Min(v => v.X) >= 200 * 400 - 1));
    }

    [Fact]
    public void Stats_ListTexturesAndReportUvError()
    {
        var s = Wall([new(0, 0, 0), new(100, 0, 0), new(100, 100, 0), new(0, 100, 0)], [new(0, 0), new(1, 0), new(1, 1), new(0, 1)]);
        VisualMapStats st = VisualMapWriter.Write(new StringWriter(), [s]);
        Assert.Contains("textures/jk/01wbolts", st.Textures);
        Assert.Equal(2, st.Brushes);
        Assert.True(st.MaxUvError < 0.002);
    }

    // ---- compilateur : le BSP et le .map du pk3 ne changent pas ----

    private string BspPath => Path.Combine(_dir, "m.bsp");

    [Fact]
    public void Compile_WithVisualMap_LeavesBspAndPk3MapUnchanged_AndWritesTheEditMap()
    {
        var input = TestScenes.Input(sky: "textures/skies/test");
        Assert.True(BspCompiler.Compile(input, BspPath, Limits.Swgl, new ImportLog()).Ok);
        byte[] bsp0 = File.ReadAllBytes(BspPath);
        string map0 = File.ReadAllText(Path.ChangeExtension(BspPath, ".map"));

        string edit = Path.Combine(_dir, "edit", "maps", "m.map");
        var withVisual = input with { VisualMapPath = edit };
        Assert.True(BspCompiler.Compile(withVisual, BspPath, Limits.Swgl, new ImportLog()).Ok);

        Assert.Equal(bsp0, File.ReadAllBytes(BspPath));
        Assert.Equal(map0, File.ReadAllText(Path.ChangeExtension(BspPath, ".map")));
        Assert.DoesNotContain("_kotor_visual", map0);
        string editMap = File.ReadAllText(edit);
        Assert.Contains("_kotor_visual", editMap);
        Assert.Contains("common/caulk", editMap); Assert.DoesNotContain("textures/", editMap); // noms sans « textures/ » : Radiant et q3map2 l ajoutent
        Assert.StartsWith(map0.Replace("textures/", "").TrimEnd(), editMap); // = le .map du pk3 (noms de texture sans prefixe) + le func_group visuel
        Assert.Equal(2, Visual(editMap).Brushes.Count);
    }

    [Fact]
    public void Compile_BspHashStaysTheGoldenOne_WhenAVisualMapIsRequested()
    {
        var input = TestScenes.Input(sky: "textures/skies/test") with { VisualMapPath = Path.Combine(_dir, "e.map") };
        Assert.True(BspCompiler.Compile(input, BspPath, Limits.Swgl, new ImportLog()).Ok);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(BspPath)));
        Assert.Equal("73E8A0E8F017D36ADD7B09505FE4108DB714105FC8DEAD0F4C0E6CC29E7FC390", hash);
    }
}

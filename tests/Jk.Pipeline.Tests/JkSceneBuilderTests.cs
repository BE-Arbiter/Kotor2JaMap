using System.IO.Compression;
using System.Numerics;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Modules;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline;

namespace Jk.Pipeline.Tests;

public class JkSceneBuilderTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";
    private const float Scale = JkWorldScale.MetresPerJkUnit;

    private static IEnumerable<SceneMesh> RoomMeshes(JkPreparedScene s, string room) =>
        s.Scene.Meshes.Where(m => m.Room == room);

    private static int Triangles(JkPreparedScene s) => s.Scene.Meshes.Sum(m => m.Triangles.Length / 3);

    [Fact]
    public void TwoSectors_TwoRooms_VisibleFromEachOther()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2 }, new ImportLog());
        Assert.Equal(["s0", "s1"], s.Scene.Meshes.Select(m => m.Room).Distinct().Order());
        Assert.NotNull(s.Scene.RoomVisibility);
        Assert.Contains("s1", s.Scene.RoomVisibility!["s0"]);
        Assert.Contains("s0", s.Scene.RoomVisibility["s1"]);
        Assert.False(s.Scene.RenderMeshesAreSolid);
        Assert.Equal(2, s.Level.Sectors.Count);
    }

    [Fact]
    public void PassableAdjoin_NotSolid()
    {
        // Adjoin 0x7 : le bit « move » (0x2) est present, le mur de l'adjoin (plan x = 1) ne bloque pas.
        JkPreparedScene open = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinFlags = 0x7 }, new ImportLog());
        Assert.DoesNotContain(WallTriangles(open, x: 1f * Scale), t => true);
        Assert.True(open.Scene.Walkmeshes.Sum(w => w.Walkable.Count(f => f)) >= 4, "floors must stay walkable (2 triangles per floor)");

        // Adjoin 0x5 : pas de bit « move », le mur bloque (solide, non marchable).
        JkPreparedScene shut = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinFlags = 0x5 }, new ImportLog());
        var walls = WallTriangles(shut, x: 1f * Scale).ToList();
        Assert.NotEmpty(walls);
        Assert.All(walls, w => Assert.False(w.Walkable));
    }

    private static IEnumerable<(SceneWalkmesh Mesh, int Tri, bool Walkable)> WallTriangles(JkPreparedScene s, float x)
    {
        foreach (SceneWalkmesh w in s.Scene.Walkmeshes)
            for (int t = 0; t < w.Triangles.Length / 3; t++)
                if (Enumerable.Range(0, 3).All(k => Math.Abs(w.Positions[w.Triangles[t * 3 + k]].X - x) < 1e-3f))
                    yield return (w, t, w.Walkable[t]);
    }

    [Fact]
    public void AdjoinWithMaterial_RenderedDoubleSided()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinMaterial = 1 }, new ImportLog());
        SceneMesh[] wallMeshes = s.Scene.Meshes.Where(m => m.Texture == "wall").ToArray();
        Assert.NotEmpty(wallMeshes);
        // Les deux faces du portail (une par secteur, chacune dessinee des deux cotes) : normales +X et -X dans chaque piece.
        foreach (string room in new[] { "s0", "s1" })
        {
            Vector3[] normals = wallMeshes.Where(m => m.Room == room).SelectMany(m => m.Normals).ToArray();
            Assert.Contains(normals, n => n.X > 0.9f);
            Assert.Contains(normals, n => n.X < -0.9f);
        }
        // Un portail invisible (materiau -1) n'est pas dessine.
        JkPreparedScene invisible = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinMaterial = -1 }, new ImportLog());
        Assert.DoesNotContain(invisible.Scene.Meshes, m => m.Texture == "wall");
    }

    [Fact]
    public void MissingMaterial_SurfaceKeptWithPlaceholder()
    {
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorMaterial = 0 }, log, null, "floor.mat");
        SceneMesh floor = Assert.Single(s.Scene.Meshes);
        Assert.Equal("floor", floor.Texture);
        Assert.Equal(2, floor.Triangles.Length / 3);
        Assert.True(s.Scene.Textures.ContainsKey("floor"));
        Assert.Null(s.Scene.Textures["floor"]); // null = introuvable : l'export met le damier de remplacement
        Assert.Contains(log.Warnings, w => w.Code == "TEXTURE_MISSING" && w.Message.Contains("floor"));
        // Le plan d'export garde la surface avec la texture de remplacement.
        ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_test", Options(Path.GetTempPath(), Path.GetTempPath()), new ImportLog());
        Assert.Equal(1, plan.MissingTextures);
        Assert.NotEmpty(plan.Input.Surfaces);
    }

    [Fact]
    public void MissingCmp_LogsAndUsesDefaultPalette()
    {
        var log = new ImportLog();
        var files = SyntheticLevel.Files();
        JklLevel level = new SyntheticLevel { Sectors = 1 }.Read();
        JkPreparedScene s = JkSceneBuilder.Build(level, JkTemplates.FromLevels(null, level), p => p.EndsWith(".cmp") ? null : files(p), "x", log, null);
        Assert.Contains(log.Warnings, w => w.Code == "JK_CMP_MISSING");
        Assert.NotNull(s.Scene.Textures["floor"]);
    }

    [Fact]
    public void VisibilityClusters_MergedUnderLimit_NoSurfaceDropped()
    {
        var chain = new SyntheticLevel { Sectors = 300 };
        JkPreparedScene full = SyntheticLevel.Build(chain, new ImportLog(), new JkSceneOptions { MaxRooms = 10_000 });
        var log = new ImportLog();
        JkPreparedScene merged = SyntheticLevel.Build(chain, log, new JkSceneOptions { MaxRooms = 50 });

        Assert.Equal(Triangles(full), Triangles(merged));
        Assert.Equal(full.Scene.Meshes.Sum(m => m.Positions.Length), merged.Scene.Meshes.Sum(m => m.Positions.Length));
        Assert.Equal(full.Scene.Walkmeshes.Sum(w => w.Triangles.Length), merged.Scene.Walkmeshes.Sum(w => w.Triangles.Length));
        Assert.Equal(300, full.Scene.Meshes.Select(m => m.Room).Distinct().Count());
        int rooms = merged.Scene.Meshes.Select(m => m.Room).Distinct().Count();
        Assert.InRange(rooms, 2, 50);
        Assert.Contains(log.Warnings, w => w.Code == "JK_VIS_MERGED");
        // Chaque piece du dictionnaire de visibilite existe et se voit elle-meme ; le voisin de chaine est visible.
        foreach (string room in merged.Scene.Meshes.Select(m => m.Room).Distinct())
            Assert.Contains(room, merged.Scene.RoomVisibility![room]);
    }

    [Fact]
    public void VisibilityClusters_OverClusterBudget_MergedAndNoSurfaceDropped()
    {
        var chain = new SyntheticLevel { Sectors = 300 };
        JkPreparedScene full = SyntheticLevel.Build(chain, new ImportLog(), new JkSceneOptions { MaxClusters = int.MaxValue });
        var log = new ImportLog();
        JkPreparedScene merged = SyntheticLevel.Build(chain, log, new JkSceneOptions { MaxClusters = 60 });

        Assert.Equal(Triangles(full), Triangles(merged));
        Assert.Equal(300, full.Scene.Meshes.Select(m => m.Room).Distinct().Count());
        Assert.InRange(merged.Scene.Meshes.Select(m => m.Room).Distinct().Count(), 1, 59);
        Assert.Contains(log.Warnings, w => w.Code == "JK_VIS_MERGED");
        // Le compte annonce (clusters du BSP) respecte la limite.
        var boxes = JkVisibility.SectorBoxes(merged.Level, JkWorldScale.JaUnitsPerJkUnit);
        Assert.True(JkVisibility.ClusterCount(boxes.Where(b => b is not null).Select(b => b!.Value).ToList()) > 60, "sanity: the unmerged chain exceeds the budget");
    }

    [Fact]
    public void Uv_AreTexvertOverCelSize_VFlippedForGl()
    {
        // Le sol (8 x 8 pixels) a des texverts 0..8 : UV 0..1 ; v de la scene est inversee (convention GL, v = 0 en bas).
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1 }, new ImportLog());
        SceneMesh floor = Assert.Single(s.Scene.Meshes);
        Assert.Contains(floor.Uv0, uv => Math.Abs(uv.X) < 1e-4f && Math.Abs(uv.Y - 1f) < 1e-4f); // texvert (0, 0) -> v = 1
        Assert.Contains(floor.Uv0, uv => Math.Abs(uv.X - 1f) < 1e-4f && Math.Abs(uv.Y - 0f) < 1e-4f); // texvert (8, 8) -> v = 0
        // Une cellule 16 x 8 (mur) : u = texvert / 16, v = texvert / 8.
        JkPreparedScene w = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinMaterial = 1 }, new ImportLog());
        SceneMesh wall = w.Scene.Meshes.First(m => m.Texture == "wall");
        Assert.Contains(wall.Uv0, uv => Math.Abs(uv.X - 0.5f) < 1e-4f);
        Assert.All(wall.Uv0, uv => Assert.InRange(uv.X, -1e-4f, 0.5001f));
    }

    [Fact]
    public void Positions_AreScaledByMetresPerJkUnit_AndFloorIsWalkable()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1 }, new ImportLog());
        SceneMesh floor = Assert.Single(s.Scene.Meshes);
        Assert.Equal(Scale, floor.Positions.Max(p => p.X), 3);
        SceneWalkmesh walk = Assert.Single(s.Scene.Walkmeshes);
        Assert.Equal(2, walk.Walkable.Count(f => f));
    }

    [Fact]
    public void HiddenSurface_NotRendered_ButStillSolid()
    {
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorGeo = 0 }, log);
        Assert.Empty(s.Scene.Meshes);
        Assert.Equal(2, s.Scene.Walkmeshes.Sum(w => w.Triangles.Length / 3));
        Assert.Contains(log.Warnings, w => w.Code == "JK_SURFACE_HIDDEN");
    }

    [Fact]
    public void Lighting_UsesRampLightmap_WithVertexIntensityAsU()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, Intensity = 0.5f }, new ImportLog());
        SceneMesh floor = Assert.Single(s.Scene.Meshes);
        Assert.NotNull(floor.Lightmap);
        Assert.NotNull(floor.Uv1);
        Assert.Equal(floor.Positions.Length, floor.Uv1!.Length);
        Assert.All(floor.Uv1, uv => Assert.InRange(uv.X, (127f + 0.5f) / 256f - 1e-3f, (128f + 0.5f) / 256f + 1e-3f));
        KotorTexture? ramp = s.Scene.Textures[floor.Lightmap!];
        Assert.NotNull(ramp);
        // Rampe de gris : x = 0 noir, x = 255 la moitie du blanc (le mixage JA x2 redonne la texture a pleine lumiere).
        Assert.Equal(0, ramp!.Rgba[0]);
        Assert.InRange(ramp.Rgba[(ramp.Width - 1) * 4], 126, 129);
        Assert.Equal(Vector3.Zero, s.Scene.SunDiffuseColor); // pas de soleil
        Assert.Empty(s.Scene.Lights);
    }

    [Fact]
    public void Lighting_SectorAmbientIsTheFloor_ExtraLightIsAdded()
    {
        // Intensite de sommet 0 : l'ambiance du secteur (0,1) tient lieu de plancher ; avec extra de surface la valeur monte.
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, Intensity = 0f }, new ImportLog());
        SceneMesh floor = Assert.Single(s.Scene.Meshes);
        float u = floor.Uv1![0].X;
        Assert.InRange(u, (25f + 0.5f) / 256f - 2e-3f, (26f + 0.5f) / 256f + 2e-3f); // 0,1 * 255 = 25,5
    }

    [Fact]
    public void Entry_IsFirstPlayerStart_AtFloorLevel()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1 }, new ImportLog());
        ModuleEntry e = s.Scene.Info.Entry;
        Assert.Equal(0.5f * Scale, e.Position.X, 3);
        Assert.Equal(0.25f * Scale, e.Position.Y, 3);
        Assert.Equal((0.12f - JkWorldScale.PlayerOriginAboveFloor) * Scale, e.Position.Z, 3);
        // Yaw 90 : le joueur regarde vers -X (rotation a gauche depuis +Y).
        Assert.True(e.Direction.X < -0.9f);
    }

    [Fact]
    public void WorldScale_PlayerFitsJaCapsule()
    {
        float jaUnits = JkWorldScale.MetresPerJkUnit * JkWorldScale.JaUnitsPerMetre;
        Assert.Equal(JkWorldScale.JaUnitsPerJkUnit, jaUnits, 3);
        Assert.InRange(JkWorldScale.PlayerTopAboveFloor * jaUnits, 62f, 64.01f);
        Assert.InRange(0.20f * jaUnits, JkWorldScale.JaPlayerHeight + 4f, 80f); // la plus basse ouverture franchie par le joueur JK
        Assert.InRange(0.05f * jaUnits, 0f, JkWorldScale.JaStepSize); // marche JK courante
    }

    [Fact]
    public void StairRiser_CurbAboveMinRise_GetsWalkableRamp()
    {
        // Deux planchers a 0 et 0,05 relies par une contremarche verticale ; 0,05 = 17 u JA > 14 (seuil de rampe) : une rampe de collision de secours doit apparaitre.
        string jkl = StairLevel(0.05f);
        JklLevel level = JklReader.Read(jkl);
        var log = new ImportLog();
        JkPreparedScene s = JkSceneBuilder.Build(level, JkTemplates.FromLevels(null, level), SyntheticLevel.Files(), "stairs", log, null);
        Assert.Contains(log.Warnings, w => w.Code == "JK_STAIR_RAMP");
        // Au moins un triangle marchable n'est ni horizontal ni vertical : la rampe.
        bool ramp = false;
        foreach (SceneWalkmesh w in s.Scene.Walkmeshes)
            for (int t = 0; t < w.Triangles.Length / 3; t++)
            {
                Vector3 a = w.Positions[w.Triangles[t * 3]], b = w.Positions[w.Triangles[t * 3 + 1]], c = w.Positions[w.Triangles[t * 3 + 2]];
                Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                if (w.Walkable[t] && n.Z > 0.7f && n.Z < 0.99f) ramp = true;
            }
        Assert.True(ramp);

        // Une contremarche franchissable (0,03 = 10 u) n'en recoit pas.
        var log2 = new ImportLog();
        JklLevel low = JklReader.Read(StairLevel(0.03f));
        JkSceneBuilder.Build(low, JkTemplates.FromLevels(null, low), SyntheticLevel.Files(), "stairs", log2, null);
        Assert.DoesNotContain(log2.Warnings, w => w.Code == "JK_STAIR_RAMP");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void StairRamp_AnyVertexRotation_CoversWholeRiserWithoutCrossing(int rot, bool reverse)
    {
        JklLevel level = JklReader.Read(StairLevel(0.05f, rot, reverse));
        var ramps = JkStairRamps.Compute(level, _ => true, 0.05f, out JkStairRamps.Stats rampStats);
        Assert.Equal(0, rampStats.Steep);
        JkStairRamps.Ramp r = Assert.Single(ramps);

        // Rampe attendue : largeur 1 (y 0..1), de x = 1 (haut, z = 0,05) a x = 1 - 0,06 (bas, z = 0).
        float expected = 1f * MathF.Sqrt(0.05f * 0.05f + 0.06f * 0.06f);
        Vector3 n1 = Vector3.Cross(r.B - r.A, r.C - r.A), n2 = Vector3.Cross(r.C - r.A, r.D - r.A);
        // Les deux triangles ont la meme orientation (normale vers le haut), marchable (pente 40 deg) : pas de noeud papillon.
        Assert.True(n1.Z > 0f && n2.Z > 0f, $"orientation {n1} / {n2}");
        Assert.True(Vector3.Normalize(n1).Z > 0.7f && Vector3.Normalize(n2).Z > 0.7f);
        // Couverture complete de la contremarche : l'aire totale est celle du rectangle attendu.
        Assert.Equal(expected, (n1.Length() + n2.Length()) / 2f, 4);
        // Les 4 coins : deux sur l'arete haute (x = 1, z = 0,05), deux au sol (x = 0,94, z = 0), couvrant y = 0 et y = 1.
        Vector3[] c = [r.A, r.B, r.C, r.D];
        Assert.Equal(2, c.Count(p => MathF.Abs(p.Z - 0.05f) < 1e-4f && MathF.Abs(p.X - 1f) < 1e-4f));
        Assert.Equal(2, c.Count(p => MathF.Abs(p.Z) < 1e-4f && MathF.Abs(p.X - 0.94f) < 1e-4f));
        Assert.Equal(2, c.Count(p => MathF.Abs(p.Y) < 1e-4f));
        Assert.Equal(2, c.Count(p => MathF.Abs(p.Y - 1f) < 1e-4f));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void StairRamp_InScene_AllRampTrianglesWalkableAndUpward(int rot, bool reverse)
    {
        JklLevel level = JklReader.Read(StairLevel(0.05f, rot, reverse));
        JkPreparedScene s = JkSceneBuilder.Build(level, JkTemplates.FromLevels(null, level), SyntheticLevel.Files(), "stairs", new ImportLog(), null);
        float area = 0f; int count = 0;
        var pts = new List<Vector3>();
        foreach (SceneWalkmesh w in s.Scene.Walkmeshes)
            for (int t = 0; t < w.Triangles.Length / 3; t++)
            {
                Vector3 a = w.Positions[w.Triangles[t * 3]], b = w.Positions[w.Triangles[t * 3 + 1]], c = w.Positions[w.Triangles[t * 3 + 2]];
                Vector3 cr = Vector3.Cross(b - a, c - a);
                float nz = cr.Z / cr.Length();
                if (nz > 0.7f && nz < 0.99f) { Assert.True(w.Walkable[t]); area += cr.Length() / 2f; count++; pts.AddRange([a, b, c]); }
                if (nz < -0.1f && nz > -0.99f) Assert.Fail("Downward-facing sloped triangle"); // pas de rampe retournee
            }
        Assert.Equal(2, count);
        // Aire = largeur (etendue Y) x longueur inclinee (diagonale XZ) du quad, a l'echelle de la scene.
        float width = pts.Max(p => p.Y) - pts.Min(p => p.Y);
        float length = MathF.Sqrt(MathF.Pow(pts.Max(p => p.X) - pts.Min(p => p.X), 2) + MathF.Pow(pts.Max(p => p.Z) - pts.Min(p => p.Z), 2));
        Assert.InRange(area, width * length * 0.999f, width * length * 1.001f);
    }

    // ---- Rampes : vrais escaliers seulement (correctif « comptoirs ») ----

    private static List<JkStairRamps.Ramp> Ramps(float h, int steps, float width = 1f, float minRise = 0.041f)
    {
        JklLevel level = JklReader.Read(StepsLevel(h, steps, width));
        return JkStairRamps.Compute(level, _ => true, minRise, out JkStairRamps.Stats _);
    }

    [Fact]
    public void StairRamp_FourStepStair_GetsOneRampPerRiser()
    {
        Assert.Equal(4, Ramps(0.07f, 4).Count);
        Assert.Equal(4, Ramps(0.1f, 4).Count); // meme une serie de marches hautes (>= 3) est un escalier
    }

    [Fact]
    public void StairRamp_IsolatedCounter_GetsNoRamp()
    {
        // Un comptoir / rebord isole de 0,1 (34 u JA) : le joueur JK ne le franchit pas non plus, il doit rester bloquant.
        Assert.Empty(Ramps(0.1f, 1));
        // Un ressaut isole de 0,07 (24 u, au-dessus du pas de JA) n'est pas un escalier non plus.
        Assert.Empty(Ramps(0.07f, 1));
        // Deux marches hautes seulement : un double rebord, pas un escalier.
        Assert.Empty(Ramps(0.1f, 2));
    }

    [Fact]
    public void StairRamp_TwoStepsOf007_AreAStair()
    {
        Assert.Equal(2, Ramps(0.07f, 2).Count);
    }

    [Fact]
    public void StairRamp_SmallIsolatedCurb_KeepsItsRamp()
    {
        // 0,05 = 17 u : bordure ou marche seule, franchissable par JK ; la rampe de secours (marge sur les 18 u de JA) reste.
        Assert.Single(Ramps(0.05f, 1));
    }

    [Fact]
    public void StairRamp_RiserNarrowerThanThePlayer_GetsNoRamp()
    {
        Assert.Empty(Ramps(0.05f, 1, width: 0.1f)); // 0,1 < diametre du joueur (0,13)
        Assert.Single(Ramps(0.05f, 1, width: 0.2f));
    }

    [Fact]
    public void StairRamp_Stats_CountPlacedAndIsolated()
    {
        JkStairRamps.Compute(JklReader.Read(StepsLevel(0.07f, 4, 1f)), _ => true, 0.041f, out JkStairRamps.Stats stair);
        Assert.Equal(new JkStairRamps.Stats(4, 0, 0, 0), stair);
        JkStairRamps.Compute(JklReader.Read(StepsLevel(0.1f, 1, 1f)), _ => true, 0.041f, out JkStairRamps.Stats counter);
        Assert.Equal(new JkStairRamps.Stats(0, 0, 1, 0), counter);
    }

    [Fact]
    public void StairRamp_CounterInScene_LogsIsolatedAndHasNoWalkableSlope()
    {
        JklLevel level = JklReader.Read(StepsLevel(0.1f, 1, 1f));
        var log = new ImportLog();
        JkPreparedScene s = JkSceneBuilder.Build(level, JkTemplates.FromLevels(null, level), SyntheticLevel.Files(), "counter", log, null);
        string msg = Assert.Single(log.Warnings, w => w.Code == "JK_STAIR_RAMP").Message;
        Assert.StartsWith("0 stair riser(s)", msg);
        Assert.Contains("1 isolated tall riser(s)", msg);
        foreach (SceneWalkmesh w in s.Scene.Walkmeshes)
            for (int t = 0; t < w.Triangles.Length / 3; t++)
            {
                Vector3 a = w.Positions[w.Triangles[t * 3]], b = w.Positions[w.Triangles[t * 3 + 1]], c = w.Positions[w.Triangles[t * 3 + 2]];
                Vector3 n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                Assert.False(w.Walkable[t] && n.Z > 0.7f && n.Z < 0.99f, "A counter must not get a walkable slope");
            }
    }

    /// <summary>
    /// Donnees reelles : le comptoir / bar vert de 01narshadda (materiaux 01wbar*, contremarche de 0,1 au-dessus d'un sol en caillebotis) ne recoit plus de rampe,
    /// les marches de caillebotis de 0,05 la gardent, et le nombre de rampes du niveau est borne (93 avant le correctif).
    /// </summary>
    [SkippableFact]
    public void Narshadda01_Counters_HaveNoRamp_ButRealStepsKeepTheirs()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        JkLevelRef lr = JkInstall.ListLevels(JkDir).Single(l => l.Name == "01narshadda");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, lr.EpisodeGob);
        var log = new ImportLog();
        JkPreparedScene s = JkSceneBuilder.Build(fs, lr, log);
        string msg = Assert.Single(log.Warnings, w => w.Code == "JK_STAIR_RAMP").Message;
        int placed = int.Parse(msg[..msg.IndexOf(' ')], System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(placed, 1, 25); // 93 avant le correctif
        Assert.Contains("isolated tall riser(s)", msg);

        List<JkStairRamps.Ramp> ramps = JkStairRamps.Compute(s.Level, f => f.Adjoin < 0, 14f / JkWorldScale.JaUnitsPerJkUnit, out JkStairRamps.Stats stats);
        Assert.InRange(stats.Isolated, 30, 200);
        bool HasRampOn(int surface) => s.Level.Surfaces[surface].VertexIndices.Select(i => s.Level.Vertices[i])
            .Any(p => ramps.Any(r => new[] { r.A, r.B, r.C, r.D }.Any(c => c == p)));
        // Surfaces dont le materiau est 01wbar* : comptoir, bar, balustrade.
        int[] bars = Enumerable.Range(0, s.Level.Surfaces.Count)
            .Where(i => s.Level.Surfaces[i].Material >= 0 && s.Level.Materials[s.Level.Surfaces[i].Material].StartsWith("01wbar", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(bars);
        foreach (int i in bars)
        {
            // Seule la partie haute d'une contremarche touche une rampe : on teste les sommets hauts de la face verticale.
            Vector3[] v = s.Level.Surfaces[i].VertexIndices.Select(x => s.Level.Vertices[x]).ToArray();
            if (v.Max(p => p.Z) - v.Min(p => p.Z) < 0.09f) continue;
            Assert.False(HasRampOn(i), $"surface {i} (bar riser) must not have a ramp");
        }
        // Marches de caillebotis de 0,05 (surfaces 2798 et 2799) : rampe de secours conservee.
        Assert.True(HasRampOn(2798) && HasRampOn(2799));
    }

    /// <summary>Donnees reelles : l'escalier de 8 marches de 09fuelstation (contremarches 2449..2486, pas de 5) garde ses rampes.</summary>
    [SkippableFact]
    public void FuelStation09_EightStepStair_KeepsItsRamps()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        JkLevelRef lr = JkInstall.ListLevels(JkDir).Single(l => l.Name == "09fuelstation");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, lr.EpisodeGob);
        JkPreparedScene s = JkSceneBuilder.Build(fs, lr, new ImportLog());
        List<JkStairRamps.Ramp> ramps = JkStairRamps.Compute(s.Level, f => f.Adjoin < 0, 14f / JkWorldScale.JaUnitsPerJkUnit, out JkStairRamps.Stats _);
        foreach (int i in new[] { 2449, 2456, 2461, 2466, 2471, 2476, 2481, 2486 })
        {
            Vector3 top = s.Level.Surfaces[i].VertexIndices.Select(x => s.Level.Vertices[x]).OrderByDescending(p => p.Z).First();
            Assert.Contains(ramps, r => new[] { r.A, r.B, r.C, r.D }.Contains(top));
        }
    }

    /// <summary>
    /// <paramref name="steps"/> contremarches de hauteur <paramref name="h"/> : plancher k = x de k a k+1, z = k * h ; la contremarche j est en x = j.
    /// Les sommets sont partages entre contremarches et planchers comme dans un vrai niveau.
    /// </summary>
    private static string StepsLevel(float h, int steps, float width)
    {
        string F(float v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var verts = new List<string> { "0 0 0", $"0 {F(width)} 0" };
        // Pour la contremarche j (1..steps) : bas (2 + 4(j-1), +1), haut (4 + 4(j-1), +1).
        for (int j = 1; j <= steps; j++)
        {
            verts.Add($"{j} 0 {F((j - 1) * h)}"); verts.Add($"{j} {F(width)} {F((j - 1) * h)}");
            verts.Add($"{j} 0 {F(j * h)}"); verts.Add($"{j} {F(width)} {F(j * h)}");
        }
        verts.Add($"{steps + 1} 0 {F(steps * h)}"); verts.Add($"{steps + 1} {F(width)} {F(steps * h)}");
        static int bottom(int j) => 2 + 4 * (j - 1);
        static int top(int j) => 4 + 4 * (j - 1);
        int end = 2 + 4 * steps;
        var surfaces = new List<string>(); var normals = new List<string>();
        for (int k = 0; k <= steps; k++)
        {
            (int l0, int l1) = k == 0 ? (0, 1) : (top(k), top(k) + 1);
            (int r0, int r1) = k == steps ? (end, end + 1) : (bottom(k + 1), bottom(k + 1) + 1);
            surfaces.Add($"{surfaces.Count}: 0 0x4 0x0 4 3 1 -1 0.0 4 {l0},0 {r0},1 {r1},2 {l1},3 0.5 0.5 0.5 0.5"); normals.Add($"{normals.Count}: 0 0 1");
        }
        for (int j = 1; j <= steps; j++)
        {
            surfaces.Add($"{surfaces.Count}: 0 0x4 0x0 4 3 1 -1 0.0 4 {bottom(j)},0 {top(j)},1 {top(j) + 1},2 {bottom(j) + 1},3 0.5 0.5 0.5 0.5"); normals.Add($"{normals.Count}: -1 0 0");
        }
        return $@"SECTION: HEADER
Version 1
SECTION: MATERIALS
World materials 1
0: floor.mat 1.0 1.0
SECTION: GEORESOURCE
World Colormaps 1
0: test.cmp
World vertices {verts.Count}
{string.Join("\n", verts.Select((v, i) => $"{i}: {v}"))}
World texture vertices 4
0: 0 0
1: 8 0
2: 8 8
3: 0 8
World adjoins 0
World surfaces {surfaces.Count}
{string.Join("\n", surfaces)}
#--- Surface normals ---
{string.Join("\n", normals)}
SECTION: SECTORS
World sectors 1
SECTOR 0
FLAGS 0x0
AMBIENT LIGHT 0.1
EXTRA LIGHT 0.0
COLORMAP 0
TINT 0 0 0
SURFACES 0 {surfaces.Count}
SECTION: TEMPLATES
World templates 2
_actor none type=actor
walkplayer _actor type=player
SECTION: THINGS
World things 1
0: walkplayer walkplayer 0.5 0.5 0.12 0 0 0 0 thingflags=0x20000401
";
    }

    /// <summary>Un secteur : sol bas (x 0..1, z 0), contremarche en x = 1, sol haut (x 1..2, z = h). Les sommets sont partages comme dans un vrai niveau.</summary>
    private static string StairLevel(float h, int rot = 0, bool reverse = false)
    {
        string H = h.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        // Contremarche : memes 4 sommets, mais rotation / sens d'enroulement variables comme dans les vrais fichiers.
        string[] riser = ["1,0", "4,1", "7,2", "2,3"];
        if (reverse) Array.Reverse(riser);
        riser = riser.Skip(rot).Concat(riser.Take(rot)).ToArray();
        string riserVerts = string.Join(" ", riser);
        return $@"SECTION: HEADER
Version 1
SECTION: MATERIALS
World materials 1
0: floor.mat 1.0 1.0
SECTION: GEORESOURCE
World Colormaps 1
0: test.cmp
World vertices 8
0: 0 0 0
1: 1 0 0
2: 1 1 0
3: 0 1 0
4: 1 0 {H}
5: 2 0 {H}
6: 2 1 {H}
7: 1 1 {H}
World texture vertices 4
0: 0 0
1: 8 0
2: 8 8
3: 0 8
World adjoins 0
World surfaces 3
0: 0 0x4 0x0 4 3 1 -1 0.0 4 0,0 1,1 2,2 3,3 0.5 0.5 0.5 0.5
1: 0 0x4 0x0 4 3 1 -1 0.0 4 4,0 5,1 6,2 7,3 0.5 0.5 0.5 0.5
2: 0 0x4 0x0 4 3 1 -1 0.0 4 {riserVerts} 0.5 0.5 0.5 0.5
#--- Surface normals ---
0: 0 0 1
1: 0 0 1
2: -1 0 0
SECTION: SECTORS
World sectors 1
SECTOR 0
FLAGS 0x0
AMBIENT LIGHT 0.1
EXTRA LIGHT 0.0
COLORMAP 0
TINT 0 0 0
SURFACES 0 3
SECTION: TEMPLATES
World templates 2
_actor none type=actor
walkplayer _actor type=player
SECTION: THINGS
World things 1
0: walkplayer walkplayer 0.5 0.5 0.12 0 0 0 0 thingflags=0x20000401
";
    }

    [SkippableFact]
    public void Narshadda01_BuildsAndExportsBsp()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        string work = Path.Combine(AppContext.BaseDirectory, "work_" + Guid.NewGuid().ToString("N"));
        string jaBase = Path.Combine(work, "base");
        Directory.CreateDirectory(jaBase); // faux dossier base : le vrai GameData\base n'est jamais touche
        try
        {
            JkLevelRef level = JkInstall.ListLevels(JkDir).Single(l => l.Name == "01narshadda");
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene s = JkSceneBuilder.Build(fs, level, log);

            Assert.NotEmpty(s.Scene.Meshes);
            Assert.True(s.Scene.Textures.Count > 100);
            Assert.All(s.Level.Surfaces.Where(f => f.Material >= 0 && f.Geo != 0),
                f => Assert.True(f.Material < s.Level.Materials.Count));
            // Aucun trou : toute surface visible (materiau + geometrie) a au moins un triangle de rendu (les polygones tournent en eventail).
            long expected = s.Level.Surfaces.Where(f => f.Material >= 0 && f.Geo != 0 && f.VertexIndices.Length >= 3).Sum(f => f.VertexIndices.Length - 2L);
            long rendered = s.Scene.Meshes.Sum(m => m.Triangles.Length / 3L);
            Assert.True(rendered >= expected, $"{rendered} rendered triangles for {expected} visible fan triangles");
            Assert.InRange(s.Scene.Info.Entry.Position.Z, -20f * Scale, 20f * Scale);

            ExportOptions opt = Options(jaBase, work) with { JediKnightDir = JkDir };
            ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_01narshadda", opt, log);
            Assert.True(plan.Input.Surfaces.Count > 0);
            Assert.InRange(plan.MissingTextures, 0, 3);

            ExportResult result = ModuleExporter.Export(s.Scene, "jk_01narshadda", opt, log);
            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(result.Pk3Path));
            using ZipArchive zip = ZipFile.OpenRead(result.Pk3Path!);
            Assert.Contains(zip.Entries, e => e.FullName == "maps/jk_01narshadda.bsp");
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("textures/jk/", StringComparison.Ordinal));
            Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("textures/kotor/", StringComparison.Ordinal));
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }

    private static ExportOptions Options(string jaBase, string work) =>
        new(KotorDir: "", JaBaseDir: jaBase, WorkDir: work, Game: KotorGame.JediKnight);
}

public class JkTextureOrientationTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    /// <summary>
    /// Les rangees d'un .mat sont lues telles que stockees (rangee 0 = haut) : preuve sur les donnees. <c>numbers.mat</c> (chiffres, niveaux CTF)
    /// est pose sur des murs dont le texvert v diminue en montant dans les 31 surfaces de chaque niveau ; avec v = rangee depuis le haut,
    /// les chiffres sont droits. La scene (v inversee, convention GL) doit donc avoir v croissant avec la hauteur.
    /// </summary>
    [SkippableFact]
    public void NumbersMat_OnWalls_HasVIncreasingWithHeight_SoDigitsAreUpright()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1CTF.GOB")), "Jedi Knight is not installed");
        JkLevelRef level = JkInstall.ListLevels(JkDir).Single(l => l.Name == "c1");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
        JkPreparedScene s = JkSceneBuilder.Build(fs, level, new ImportLog());
        int checkedTriangles = 0;
        foreach (SceneMesh m in s.Scene.Meshes.Where(m => m.Texture == "numbers"))
            for (int t = 0; t + 2 < m.Triangles.Length; t += 3)
            {
                int[] i = [m.Triangles[t], m.Triangles[t + 1], m.Triangles[t + 2]];
                Vector3 n = Vector3.Cross(m.Positions[i[1]] - m.Positions[i[0]], m.Positions[i[2]] - m.Positions[i[0]]);
                if (n.LengthSquared() < 1e-9f || Math.Abs(Vector3.Normalize(n).Z) > 0.1f) continue;
                int top = i.OrderByDescending(k => m.Positions[k].Z).First(), bottom = i.OrderBy(k => m.Positions[k].Z).First();
                if (m.Positions[top].Z - m.Positions[bottom].Z < 1e-3f) continue;
                checkedTriangles++;
                Assert.True(m.Uv0[top].Y >= m.Uv0[bottom].Y - 1e-4f, "v must grow with height (digits upright)");
            }
        Assert.True(checkedTriangles > 20, $"only {checkedTriangles} wall triangles with numbers.mat");
    }
}

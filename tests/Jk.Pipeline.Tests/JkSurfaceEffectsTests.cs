using System.Numerics;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Pipeline;

namespace Jk.Pipeline.Tests;

/// <summary>Matériaux spéciaux : ciel, animations, défilement, eau, lave, dégâts (tâche 5).</summary>
public class JkSurfaceEffectsTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    // ---- scripts COG (réduits à ce que lit le builder ; mêmes symboles et mêmes appels que les scripts du jeu) ----

    private const string ConveyorCog = """
        symbols
            message startup
            vector  vec0    desc=conveyor_direction
            float   speed=2.0   desc=conveyor_speed
            surface convey0 nolink
            surface convey1 nolink
            int     garbage local
            int     cnt     local
        end
        code
        startup:
            for (cnt = 0; cnt<=1; cnt=cnt+1) {
                if (convey0[cnt] >= 0) garbage=slidewall(convey0[cnt], vec0, speed);
            }
            return;
        end
        """;

    private const string MatAnimCog = """
        symbols
            message startup
            material animmat desc=animating_material
            flex     fps=5.0 desc=playback_speed
        end
        code
        startup:
            materialanim(animmat, fps, 1);
            return;
        end
        """;

    private const string SurfAnimCog = """
        symbols
            message startup
            surface sign
            surface sign1
            float   fps=5.0
            int     i local
        end
        code
        startup:
            for (i=0; i<=1; i=i+1)
            {
                if (sign[i] >= 0) SurfaceAnim(sign[i], fps, 0x3);
            }
            return;
        end
        """;

    private const string DeathFloorCog = """
        symbols
            surface surf0 mask=0x404
            surface surf1 mask=0x404
            message touched
            message entered
            int     victim local
        end
        code
        touched:
        entered:
            victim = getsourceref();
            DamageThing(victim, 1000.0, 0x1, victim);
            return;
        end
        """;

    private const string SectorDamageCog = """
        symbols
            message startup
            message pulse
            sector  damageSector
            flex    damageInterval=1.0
            flex    damage=4.0
            thing   victim local
        end
        code
        startup:
            if(damageSector > -1) SetPulse(damageInterval);
            return;
        pulse:
            victim = FirstThingInSector(damageSector);
            while (victim != -1)
            {
                DamageThing(victim, damage, 0x1, victim);
                victim = NextThingInSector(victim);
            }
            return;
        end
        """;

    private const string SlideSkiesCog = """
        symbols
            message startup
            float u=10.0
            float v=4.0
        end
        code
        startup:
            SlideCeilingSky(u, v);
            return;
        end
        """;

    private static Dictionary<string, string> Scripts(params (string File, string Text)[] scripts) =>
        scripts.ToDictionary(s => s.File, s => s.Text, StringComparer.OrdinalIgnoreCase);

    private static ExportOptions Options() =>
        new(KotorDir: "", JaBaseDir: Path.GetTempPath(), WorkDir: Path.GetTempPath(), Game: KotorGame.JediKnight);

    private static SceneMesh FloorMesh(JkPreparedScene s, string room = "s0") =>
        s.Scene.Meshes.Single(m => m.Room == room && m.Normals.All(n => n.Z > 0.9f));

    // ---- ciel ----

    [Fact]
    public void SkySurface_UsesMapSkyShader()
    {
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorMaterial = 4, FloorSurfaceFlags = 0x204, ExtraHeader = "\nHorizon Pixels per Rev 768" }, log);

        Assert.Equal("textures/jk/sky_synthetic", s.Scene.SkyShader);
        SceneMesh sky = s.Scene.Meshes.Single();
        Assert.True(sky.Hints.Sky);
        Assert.Equal("sky_synthetic", sky.Texture);
        Assert.Null(sky.Lightmap);
        Assert.NotNull(s.Scene.Textures["sky_synthetic"]);
        Assert.InRange(sky.Hints.SkyTile, 1f, 8f);

        ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_test", Options(), new ImportLog());
        Assert.Equal("textures/jk/sky_synthetic", plan.Input.SkyShader); // le compilateur entoure la carte du meme ciel
        PlannedShader planned = Assert.Single(plan.Shaders, p => p.Name == "textures/jk/sky_synthetic");
        Assert.Equal(ShaderKind.Sky, planned.Kind);
        string text = ShaderPlanner.Write(planned, KotorGame.JediKnight);
        Assert.Contains("surfaceparm sky", text);
        Assert.Contains("map textures/jk/sky_synthetic.tga", text);
        Assert.Contains(plan.Textures, t => t.RelPath == "textures/jk/sky_synthetic.tga" && t.Texture is not null);
        Assert.Contains(log.Warnings, w => w.Code == "JK_SKY_APPROX");
    }

    [Fact]
    public void LevelWithoutSky_KeepsTheDefaultSky()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2 }, new ImportLog());
        Assert.Null(s.Scene.SkyShader);
        Assert.All(s.Scene.Meshes, m => Assert.True(m.Hints.IsNone));
    }

    [Fact]
    public void SlideSkyCog_DriftsTheSkyTexture()
    {
        var level = new SyntheticLevel
        {
            Sectors = 1, FloorMaterial = 4, FloorSurfaceFlags = 0x404, Cogs = ["00_slideceiling.cog 10.0 4.0"],
            CogScripts = Scripts(("00_slideceiling.cog", SlideSkiesCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        SceneMesh sky = s.Scene.Meshes.Single();
        Assert.NotNull(sky.Hints.Scroll);
        // 10 et 4 pixels par seconde sur une texture de 64 pixels ; le contenu avance vers les u croissants et les v croissants (vers le haut dans la scene).
        Assert.Equal(10f / 64f, sky.Hints.Scroll!.Value.X, 3);
        Assert.Equal(-4f / 64f, sky.Hints.Scroll!.Value.Y, 3);
    }

    // ---- animations ----

    [Fact]
    public void MultiCelAnimated_WritesAnimMap()
    {
        var level = new SyntheticLevel
        {
            Sectors = 1, FloorMaterial = 5, Cogs = ["00_matanim.cog anim.mat 5.0"], CogScripts = Scripts(("00_matanim.cog", MatAnimCog)),
        };
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(level, log);
        SceneMesh floor = FloorMesh(s);
        Assert.Equal(["anim", "anim_c1", "anim_c2"], floor.Hints.AnimFrames);
        Assert.Equal(5f, floor.Hints.AnimFps);
        Assert.NotNull(s.Scene.Textures["anim_c1"]);
        Assert.NotEqual(s.Scene.Textures["anim"]!.Rgba[0], s.Scene.Textures["anim_c2"]!.Rgba[0]); // cellules distinctes

        ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_test", Options(), new ImportLog());
        string shaders = string.Join("\n", plan.Shaders.Select(p => ShaderPlanner.Write(p, KotorGame.JediKnight)));
        Assert.Contains("animMap 5 textures/jk/anim.tga textures/jk/anim_c1.tga textures/jk/anim_c2.tga", shaders);
        foreach (string f in new[] { "anim", "anim_c1", "anim_c2" })
            Assert.Contains(plan.Textures, t => t.RelPath == $"textures/jk/{f}.tga" && t.Texture is not null);
    }

    [Fact]
    public void MultiCelWithoutCog_UsesTheFirstCelOnly()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorMaterial = 5 }, new ImportLog());
        Assert.True(FloorMesh(s).Hints.IsNone);
        Assert.DoesNotContain("anim_c1", s.Scene.Textures.Keys);
    }

    [Fact]
    public void SurfaceAnim_AnimatesOnlyThatSurface_AndSkipsTheLastCelWithFlag2()
    {
        // La surface 0 (sol du secteur 0) est animee par SurfaceAnim(.., 0x3) : boucle, derniere image sautee.
        var level = new SyntheticLevel
        {
            Sectors = 2, FloorMaterial = 5, Cogs = ["00_surfanim.cog 0 -1 4.0"], CogScripts = Scripts(("00_surfanim.cog", SurfAnimCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        SceneMesh animated = FloorMesh(s, "s0");
        Assert.Equal(["anim", "anim_c1"], animated.Hints.AnimFrames);
        Assert.Equal(4f, animated.Hints.AnimFps);
        Assert.True(FloorMesh(s, "s1").Hints.IsNone);
    }

    // ---- défilement ----

    [Fact]
    public void ScrollingSurface_WritesTcModScroll()
    {
        // Tapis roulant le long de +X à 2 unités par seconde ; le sol 8 x 8 pixels se déroule de 8 pixels par unité : 16 pixels = 2 largeurs par seconde.
        var level = new SyntheticLevel
        {
            Sectors = 1, Cogs = ["00_conveyor.cog (1.0/0.0/0.0) 2.0 0 -1"], CogScripts = Scripts(("00_conveyor.cog", ConveyorCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        SceneMesh floor = FloorMesh(s);
        Assert.NotNull(floor.Hints.Scroll);
        Assert.Equal(2f, floor.Hints.Scroll!.Value.X, 3);
        Assert.Equal(0f, floor.Hints.Scroll!.Value.Y, 3);

        ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_test", Options(), new ImportLog());
        string shaders = string.Join("\n", plan.Shaders.Select(p => ShaderPlanner.Write(p, KotorGame.JediKnight)));
        Assert.Contains("tcMod scroll -2 0", shaders);
    }

    [Fact]
    public void ScrollAlongPositiveY_MovesTheImageDownTheTextureRows()
    {
        var level = new SyntheticLevel
        {
            Sectors = 1, Cogs = ["00_conveyor.cog (0.0/1.0/0.0) 1.0 0 -1"], CogScripts = Scripts(("00_conveyor.cog", ConveyorCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        // v de JK = rangee depuis le haut : avancer vers +Y fait croitre v (vers le bas), donc decroitre v de la scene.
        Assert.Equal(-1f, FloorMesh(s).Hints.Scroll!.Value.Y, 3);
        Assert.Equal(0f, FloorMesh(s).Hints.Scroll!.Value.X, 3);
    }

    [Fact]
    public void ScrollSpeedScale_ScalesTheScrollOption()
    {
        var level = new SyntheticLevel
        {
            Sectors = 1, Cogs = ["00_conveyor.cog (1.0/0.0/0.0) 2.0 0 -1"], CogScripts = Scripts(("00_conveyor.cog", ConveyorCog)),
        };
        JkPreparedScene half = SyntheticLevel.Build(level, new ImportLog(), new JkSceneOptions { ScrollSpeedScale = 0.5f });
        Assert.Equal(1f, FloorMesh(half).Hints.Scroll!.Value.X, 3);
    }

    [Fact]
    public void SlideAlongTheNormal_CannotScroll_AndIsLogged()
    {
        // Glissement vertical (+Z) sur un sol : perpendiculaire a la surface, la texture ne bouge pas.
        var level = new SyntheticLevel
        {
            Sectors = 1, Cogs = ["00_conveyor.cog (0.0/0.0/1.0) 2.0 0 -1"], CogScripts = Scripts(("00_conveyor.cog", ConveyorCog)),
        };
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(level, log);
        Assert.True(FloorMesh(s).Hints.IsNone);
        Assert.Contains(log.Warnings, w => w.Code == "JK_SLIDE_SKIPPED");
    }

    [Fact]
    public void ConveyorWithUnknownCogFile_KeepsTheSurfaceStatic()
    {
        // Script introuvable : pas d'effet, pas d'exception.
        var level = new SyntheticLevel { Sectors = 1, Cogs = ["00_conveyor.cog (1.0/0.0/0.0) 2.0 0 -1"] };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        Assert.True(FloorMesh(s).Hints.IsNone);
    }

    // ---- eau ----

    [Fact]
    public void UnderwaterSector_ProducesWaterVolume()
    {
        var level = new SyntheticLevel
        {
            Sectors = 2, AdjoinMaterial = 2, AdjoinFaceFlags = 0x2,
            SectorFlags = new Dictionary<int, uint> { [1] = 0x2 }, SectorTints = new Dictionary<int, string> { [1] = "0.4 0.8 1.0" },
        };
        IReadOnlyList<JkVolume> volumes = JkSurfaceEffects.Volumes(level.Read());
        JkVolume water = Assert.Single(volumes);
        Assert.Equal(JkVolumeKind.Water, water.Kind);
        Assert.Equal(1, water.Sector);
        Assert.Equal(new Vector3(1f, 0f, 0f), water.Min);
        Assert.Equal(new Vector3(2f, 1f, SyntheticLevel.Height), water.Max);
        Assert.Equal(new Vector3(0.4f, 0.8f, 1f), water.Color);

        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(level, log);
        Assert.Equal(water, Assert.Single(s.Volumes));
        // Les murs d'adjoin translucides entre l'air et l'eau (la surface de l'eau) prennent l'aspect de l'eau.
        SceneMesh[] surfaces = s.Scene.Meshes.Where(m => m.Hints.Water).ToArray();
        Assert.NotEmpty(surfaces);
        Assert.All(surfaces, m => Assert.Equal(SurfaceAlpha.Blend, m.Alpha));
        Assert.Contains(log.Warnings, w => w.Code == "JK_WATER_VOLUME");
    }

    [Fact]
    public void NoUnderwaterSector_NoWaterLook()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinMaterial = 2, AdjoinFaceFlags = 0x2 }, new ImportLog());
        Assert.Empty(s.Volumes);
        Assert.All(s.Scene.Meshes, m => Assert.False(m.Hints.Water));
    }

    [Fact]
    public void WaterVolumeShader_IsAFoggedWaterBrushShader()
    {
        string name = JkSurfaceEffects.WaterVolumeShaderName(new Vector3(0.4f, 0.8f, 1f));
        Assert.Equal("textures/jk/water_66ccff", name);
        string text = JkSurfaceEffects.WaterVolumeShader(new Vector3(0.4f, 0.8f, 1f));
        Assert.StartsWith(name + "\n{", text);
        Assert.Contains("surfaceparm water", text);
        Assert.Contains("surfaceparm nonsolid", text);
        Assert.Contains("surfaceparm fog", text);
        Assert.Contains("fogParms ( 0.4 0.8 1 )", text);
        Assert.True(name.Length <= ShaderNames.MaxFullLength);
    }

    // ---- lave, dégâts ----

    [Fact]
    public void LavaSurface_FullBrightAndHurt()
    {
        // Un sol de lave dont la surface 0 est une « deathfloor » : pleine lumiere, ondulant, et un volume mortel.
        var level = new SyntheticLevel
        {
            Sectors = 1, FloorMaterial = 3, Cogs = ["00_deathfloor.cog 0 -1"], CogScripts = Scripts(("00_deathfloor.cog", DeathFloorCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        SceneMesh lava = FloorMesh(s);
        Assert.True(lava.Hints.Lava);
        Assert.True(lava.Hints.FullBright);
        Assert.Null(lava.Lightmap);

        JkVolume kill = Assert.Single(s.Volumes);
        Assert.Equal(JkVolumeKind.Kill, kill.Kind);
        Assert.Equal(0, kill.Sector);
        Assert.Equal(0, kill.Surface);
        // La boite couvre le sol et monte au-dessus (le joueur doit la toucher).
        Assert.True(kill.Min.X <= 0f && kill.Max.X >= 1f && kill.Min.Y <= 0f && kill.Max.Y >= 1f);
        Assert.True(kill.Max.Z > 0f && kill.Max.Z < SyntheticLevel.Height);

        ExportPlan plan = ModuleExporter.BuildPlan(s.Scene, "jk_test", Options(), new ImportLog());
        PlannedShader planned = plan.Shaders.Single(p => p.Hints.Lava);
        string text = ShaderPlanner.Write(planned, KotorGame.JediKnight);
        Assert.Contains("rgbGen identity", text);
        Assert.DoesNotContain("rgbGen vertex", text);
    }

    [Fact]
    public void DeathFloorOnOrdinaryMaterial_KillsButIsNotLava()
    {
        var level = new SyntheticLevel
        {
            Sectors = 1, FloorMaterial = 0, Cogs = ["00_deathfloor.cog 0 -1"], CogScripts = Scripts(("00_deathfloor.cog", DeathFloorCog)),
        };
        JkPreparedScene s = SyntheticLevel.Build(level, new ImportLog());
        Assert.Equal(JkVolumeKind.Kill, Assert.Single(s.Volumes).Kind);
        Assert.True(FloorMesh(s).Hints.IsNone);
    }

    [Fact]
    public void SectorDamage_HurtKillAndHeal()
    {
        static JklLevel LevelWith(string instance) => new SyntheticLevel { Sectors = 2, Cogs = [instance] }.Read();
        Func<string, string?> read = f => f.Equals("00_sectordamage.cog", StringComparison.OrdinalIgnoreCase) ? SectorDamageCog : null;

        var log = new ImportLog();
        JkVolume hurt = Assert.Single(JkSurfaceEffects.Volumes(LevelWith("00_sectordamage.cog 1 1.0 4.0"), read, log));
        Assert.Equal(JkVolumeKind.Hurt, hurt.Kind);
        Assert.Equal(1, hurt.Sector);
        Assert.Equal(4f, hurt.Damage, 3); // par seconde

        JkVolume fast = Assert.Single(JkSurfaceEffects.Volumes(LevelWith("00_sectordamage.cog 0 0.25 300.0"), read, log));
        Assert.Equal(JkVolumeKind.Kill, fast.Kind);

        Assert.Empty(JkSurfaceEffects.Volumes(LevelWith("00_sectordamage.cog 1 1.0 -1.0"), read, log)); // soigne au lieu de blesser
        Assert.Contains(log.Warnings, w => w.Code == "JK_HEAL_SECTOR_SKIPPED");
        Assert.Empty(JkSurfaceEffects.Volumes(LevelWith("00_sectordamage.cog -1 1.0 4.0"), read, log)); // secteur non lie : rien a placer
    }

    [Fact]
    public void EventDrivenDamageWithoutNamedArea_IsLoggedAsUnresolved()
    {
        // Ecrasement declenche par un evenement : le script ne designe ni secteur ni surface, le comportement est ignore mais doit etre signale.
        const string script = """
            symbols
              message activated
              thing victim local
            end
            code
            activated:
                victim = GetSourceRef();
                DamageThing(victim, 1000.0, 0x1, victim);
                return;
            end
            """;
        var log = new ImportLog();
        var level = new SyntheticLevel { Sectors = 1, Cogs = ["20_crush.cog"] }.Read();
        Assert.Empty(JkSurfaceEffects.Volumes(level, _ => script, log));
        Assert.Contains(log.Warnings, w => w.Code == "JK_DAMAGE_COG_UNRESOLVED" && w.Message.Contains("20_crush.cog"));
    }

    [Fact]
    public void SectorListCheckedWithGetThingSector_HurtsEachListedSector_AtTheSetPulseRate()
    {
        // Meme forme que 12_sectordamage.cog : tableau de secteurs compare au secteur du joueur, impulsion toutes les 2 secondes, 1 point de degats.
        const string script = """
            symbols
              message startup
              message pulse
              sector sector0
              sector sector1
              int numsectors=2 local
              int plsector local
            end
            code
            startup:
                SetPulse(2);
                return;
            pulse:
                plsector=GetThingSector(jkGetLocalPlayer());
                for (i=0; i < numsectors; i=i+1)
                if (sector0[i]==plsector)
                DamageThing(player, 1, 8, 0);
                return;
            end
            """;
        var level = new SyntheticLevel { Sectors = 2, Cogs = ["12_sectordamage.cog 0 1"] }.Read();
        IReadOnlyList<JkVolume> volumes = JkSurfaceEffects.Volumes(level, _ => script);
        Assert.Equal([0, 1], volumes.Select(v => v.Sector));
        Assert.All(volumes, v => { Assert.Equal(JkVolumeKind.Hurt, v.Kind); Assert.Equal(0.5f, v.Damage, 3); });
    }

    [Fact]
    public void WithoutCogReader_OnlyTheUnderwaterSectorsAreFound()
    {
        var level = new SyntheticLevel { Sectors = 2, Cogs = ["00_sectordamage.cog 1 1.0 4.0"], SectorFlags = new Dictionary<int, uint> { [0] = 0x2 } };
        JkVolume only = Assert.Single(JkSurfaceEffects.Volumes(level.Read()));
        Assert.Equal(JkVolumeKind.Water, only.Kind);
    }

    // ---- lumière pleine ----

    [Fact]
    public void FullLightSurface_IsFullBrightWithoutRamp()
    {
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorLight = 0 }, new ImportLog());
        SceneMesh floor = FloorMesh(s);
        Assert.True(floor.Hints.FullBright);
        Assert.False(floor.Hints.Lava);
        Assert.Null(floor.Lightmap);
        JkPreparedScene lit = SyntheticLevel.Build(new SyntheticLevel { Sectors = 1, FloorLight = 3 }, new ImportLog());
        Assert.NotNull(FloorMesh(lit).Lightmap);
        Assert.True(FloorMesh(lit).Hints.IsNone);
    }

    // ---- lecteur de COG minimal ----

    [Fact]
    public void CogLite_BindsNonLocalSymbolsInOrder_AndFindsStartupCalls()
    {
        CogLite script = CogLite.Parse(ConveyorCog);
        Assert.Equal(["vec0", "speed", "convey0", "convey1"], script.Symbols.Where(s => !s.Local && s.Type != "message").Select(s => s.Name));
        var inst = new JklCogInstance(0, "00_conveyor.cog", ["(1.0/0.0/0.0)", "2.0", "5", "-1"]);
        IReadOnlyDictionary<string, string> values = script.Bind(inst);
        Assert.Equal("5", values["convey0"]);
        Assert.Equal("(1.0/0.0/0.0)", values["vec0"]);

        CogCall call = Assert.Single(script.StartupCalls(), c => c.Function.Equals("slidewall", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["convey0[cnt]", "vec0", "speed"], call.Args);
    }

    [Fact]
    public void CogLite_FollowsCallToAnotherMessage_AndSharedLabels()
    {
        const string text = """
            symbols
              message startup
              message entered
              message touched
              material animmat=00tfuel.mat local
              flex fps=5.0
            end
            code
            startup:
                call entered;
                return;
            entered:
                materialanim(animmat, fps, 1);
                return;
            touched:
            end
            """;
        CogLite script = CogLite.Parse(text);
        CogCall call = Assert.Single(script.StartupCalls());
        Assert.Equal("materialanim", call.Function, ignoreCase: true);
        // Valeur par defaut d'un symbole local.
        Assert.Equal("00tfuel.mat", script.Bind(new JklCogInstance(0, "x.cog", ["7.5"]))["animmat"]);
    }

    // ---- données réelles (sautées sans JK) ----

    private static JkPreparedScene BuildReal(string name, ImportLog log, out JkFileSystem fs)
    {
        JkLevelRef level = JkInstall.ListLevels(JkDir).Single(l => l.Name == name);
        fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
        return JkSceneBuilder.Build(fs, level, log);
    }

    [SkippableFact]
    public void Katarn03_HasMapSkyAndWater()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var log = new ImportLog();
        JkPreparedScene s = BuildReal("03katarn", log, out JkFileSystem fs);
        using (fs)
        {
            Assert.Equal("textures/jk/sky_03katarn", s.Scene.SkyShader);
            Assert.Contains(s.Scene.Meshes, m => m.Hints.Sky);
            Assert.Contains(s.Volumes, v => v.Kind == JkVolumeKind.Water);
            Assert.Contains(s.Volumes, v => v.Kind == JkVolumeKind.Hurt);
        }
    }

    [SkippableFact]
    public void Fuelstation09_HasScrollingBeltsAndAnimatedMaterials()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        var log = new ImportLog();
        JkPreparedScene s = BuildReal("09fuelstation", log, out JkFileSystem fs);
        using (fs)
        {
            Assert.Contains(s.Scene.Meshes, m => m.Hints.Scroll is not null);
            Assert.Contains(s.Scene.Meshes, m => m.Hints.AnimFrames is { Count: >= 2 });
            Assert.Contains(s.Volumes, v => v.Kind == JkVolumeKind.Kill);
        }
    }
}

using System.Globalization;
using System.Numerics;
using Jk.Core;
using Jk.Core.Gob;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Xunit;

namespace Jk.Pipeline.Tests;

// Poussee de secteur : 00_sectorthrust (SectorThrust au demarrage) et THRUST du fichier -> trigger_push RELATIVE non solide + target_position lointain.
// Classe partielle de JkThingConverterTests pour reutiliser la fabrication de niveaux et de GOB synthetiques.
public sealed partial class JkThingConverterTests
{
    /// <summary>Le script du jeu <c>00_sectorthrust.cog</c> (sans ses commentaires).</summary>
    private const string SectorThrustCog = """
        symbols
        message     startup
        vector      vec0        desc=thrust_direction
        float       speed=1.0   desc=thrust_speed
        sector      sector0
        sector      sector1
        sector      sector2
        sector      sector3
        sector      sector4
        sector      sector5
        sector      sector6
        sector      sector7
        end
        code
        startup:
            if (sector0 >= 0) SectorThrust(sector0, vec0, speed);
            if (sector1 >= 0) SectorThrust(sector1, vec0, speed);
            if (sector2 >= 0) SectorThrust(sector2, vec0, speed);
            if (sector3 >= 0) SectorThrust(sector3, vec0, speed);
            if (sector4 >= 0) SectorThrust(sector4, vec0, speed);
            if (sector5 >= 0) SectorThrust(sector5, vec0, speed);
            if (sector6 >= 0) SectorThrust(sector6, vec0, speed);
            if (sector7 >= 0) SectorThrust(sector7, vec0, speed);
            return;
        end
        """;

    /// <summary>Script dont la poussee est changee par un message (interrupteur, minuterie) : pas une poussee continue, non convertible.</summary>
    private const string SwitchedThrustCog = """
        symbols
        message     startup
        message     activate
        sector      area
        vector      push=(0.0/0.0/3.0)
        end
        code
        startup:
            SectorThrust(area, push, 1.0);
            return;
        activate:
            SectorThrust(area, push, 0.0);
            return;
        end
        """;

    private static SyntheticLevel ThrustLevel(string instance, int sectors = 3) => new()
    {
        Sectors = sectors,
        Cogs = ["00_sectorthrust.cog " + instance],
        CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = SectorThrustCog },
    };

    private const string NoSector = "-1 -1 -1 -1 -1 -1 -1";

    /// <summary>Centre du secteur <paramref name="s"/> du niveau synthetique (unites JA) : x de s a s + 1, y de 0 a 1, z de 0 a 0,5.</summary>
    private static Vector3 SectorCentre(int s) => new Vector3(s + 0.5f, 0.5f, 0.25f) * K;

    private static Vector3 Origin(IReadOnlyDictionary<string, string> e)
    {
        string[] p = e["origin"].Split(' ');
        return new Vector3(float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture), float.Parse(p[2], CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SectorThrust_BecomesARelativeTriggerPushTowardAFarTarget()
    {
        // Poussee vers le haut (0,0,1), vitesse 2, secteur 1.
        using Env e = Make(ThrustLevel("(0.0/0.0/1.0) 2.0 1 " + NoSector));
        var (converted, subModels) = Run(e);

        var push = Assert.Single(Of(converted, "trigger_push"));
        Assert.Equal("17", push["spawnflags"]);          // RELATIVE (16) | PLAYERONLY (1) : sans PLAYERONLY, trigger_push_touch donne origin2 (position monde de la cible) comme vitesse aux entites non-client en mouvement
        Assert.Equal("250", push["speed"]);             // 2 x 125 u/s par unite de poussee
        SubModelInput sub = subModels[int.Parse(push["model"][1..]) - 1];
        Assert.Equal(BspContents.Trigger, sub.Contents); // jamais solide

        var target = Assert.Single(Of(converted, "target_position"));
        Assert.Equal(push["target"], target["targetname"]);
        Vector3 dir = Vector3.Normalize(Origin(target) - SectorCentre(1));
        Assert.Equal(0f, dir.X, 0.001f);
        Assert.Equal(0f, dir.Y, 0.001f);
        Assert.Equal(1f, dir.Z, 0.001f);
        Assert.True((Origin(target) - SectorCentre(1)).Length() >= 10000f, "the target must be far enough for the push direction to stay constant");
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SECTOR_THRUST" && w.Message.Contains("1 "));
    }

    [Fact]
    public void SectorThrust_TriggerCoversTheSector()
    {
        using Env e = Make(ThrustLevel("(0.0/0.0/1.0) 1.0 1 " + NoSector));
        var (converted, subModels) = Run(e);

        var push = Assert.Single(Of(converted, "trigger_push"));
        SubModelInput sub = subModels[int.Parse(push["model"][1..]) - 1];
        var (lo, hi) = BrushBounds.FromPlanes(sub.Brushes[0])!.Value;
        Assert.Equal(1f * K, lo.X, 0.5f);
        Assert.Equal(2f * K, hi.X, 0.5f);
        Assert.Equal(0f, lo.Y, 0.5f);
        Assert.Equal(1f * K, hi.Y, 0.5f);
        Assert.Equal(0.5f * K, hi.Z, 0.5f);
    }

    [Fact]
    public void SectorThrust_SeveralSectorsOfOneInstance_ShareOneTarget()
    {
        using Env e = Make(ThrustLevel("(1.0/0.0/0.0) 1.0 0 1 2 -1 -1 -1 -1 -1"));
        var (converted, _) = Run(e);

        var pushes = Of(converted, "trigger_push");
        Assert.Equal(3, pushes.Count);
        Assert.Single(pushes.Select(p => p["target"]).Distinct());
        var target = Assert.Single(Of(converted, "target_position"));
        Vector3 dir = Vector3.Normalize(Origin(target) - SectorCentre(1)); // centre de l'ensemble
        Assert.Equal(1f, dir.X, 0.001f);
    }

    [Fact]
    public void SectorThrust_DirectionIsNormalised_AndMagnitudeMultipliesTheSpeed()
    {
        // Vecteur (0, 3, 0) de vitesse 0,5 : direction +Y, intensite 1,5.
        using Env e = Make(ThrustLevel("(0.0/3.0/0.0) 0.5 1 " + NoSector));
        var (converted, _) = Run(e);

        var push = Assert.Single(Of(converted, "trigger_push"));
        Assert.Equal("187.5", push["speed"]);            // 1,5 x 125
        Vector3 dir = Vector3.Normalize(Origin(Assert.Single(Of(converted, "target_position"))) - SectorCentre(1));
        Assert.Equal(1f, dir.Y, 0.001f);
    }

    [Fact]
    public void SectorThrust_VeryStrongPush_IsCapped()
    {
        using Env e = Make(ThrustLevel("(0.0/0.0/1.0) 10.0 1 " + NoSector));
        var (converted, _) = Run(e);

        Assert.Equal("1000", Assert.Single(Of(converted, "trigger_push"))["speed"]);
    }

    [Fact]
    public void SectorThrust_TinyForce_NeverEmitsAZeroSpeedPush()
    {
        // speed 0 : « if (self->speed) » est faux dans trigger_push_touch, la direction n'est pas normalisee et le joueur est envoye a la distance entiere de la cible.
        // Force 0,004 -> 0,5 u/s : sous le plancher, la zone est omise (et journalisee) au lieu de sortir avec « speed 0 ».
        using Env tiny = Make(ThrustLevel("(0.0/0.0/1.0) 0.004 1 " + NoSector));
        var (converted, _) = Run(tiny);
        Assert.Empty(Of(converted, "trigger_push"));
        Assert.Empty(Of(converted, "target_position"));
        Assert.Contains(tiny.Log.Warnings, w => w.Code == "JK_SECTOR_THRUST_SKIPPED");

        // Juste au-dessus du plancher : emise, avec une vitesse non nulle.
        using Env small = Make(ThrustLevel("(0.0/0.0/1.0) 0.01 1 " + NoSector));
        var push = Assert.Single(Of(Run(small).Converted, "trigger_push"));
        Assert.True(float.Parse(push["speed"], CultureInfo.InvariantCulture) >= JkSectorThrust.MinPushSpeed);
    }

    [Fact]
    public void SectorThrust_ZeroSpeedOrNoSector_PlacesNothing()
    {
        using Env zero = Make(ThrustLevel("(0.0/0.0/1.0) 0.0 1 " + NoSector));
        Assert.Empty(Of(Run(zero).Converted, "trigger_push"));
        using Env none = Make(ThrustLevel("(0.0/0.0/1.0) 1.0 -1 " + NoSector));
        var (converted, _) = Run(none);
        Assert.Empty(Of(converted, "trigger_push"));
        Assert.Empty(Of(converted, "target_position"));
        Assert.DoesNotContain(none.Log.Warnings, w => w.Code.StartsWith("JK_SECTOR_THRUST", StringComparison.Ordinal));
    }

    [Fact]
    public void SectorThrust_SectorOfTheJklFile_IsConverted()
    {
        // THRUST 0 0 2 dans le fichier du niveau : direction +Z, intensite 2.
        using Env e = Make(new SyntheticLevel { Sectors = 2, SectorSounds = new Dictionary<int, string> { [1] = "wind.wav 1\nTHRUST 0.0 0.0 2.0" } });
        var (converted, _) = Run(e);

        var push = Assert.Single(Of(converted, "trigger_push"));
        Assert.Equal("250", push["speed"]);
        Assert.Equal(push["target"], Assert.Single(Of(converted, "target_position"))["targetname"]);
    }

    [Fact]
    public void SectorThrust_SameSectorTwice_LastScriptWins()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            Cogs = ["00_sectorthrust.cog (0.0/0.0/1.0) 1.0 1 " + NoSector, "00_sectorthrust.cog (0.0/0.0/1.0) 3.0 1 " + NoSector],
            CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = SectorThrustCog },
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Equal("375", Assert.Single(Of(converted, "trigger_push"))["speed"]); // comme dans JK : le dernier SectorThrust remplace le precedent
        Assert.Single(Of(converted, "target_position"));
    }

    [Fact]
    public void SectorThrust_ChangedByEvents_IsNotConverted_AndStaysLogged()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            Cogs = ["lift_thrust.cog 1"],
            CogScripts = new Dictionary<string, string> { ["lift_thrust.cog"] = SwitchedThrustCog },
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Empty(Of(converted, "trigger_push"));
        Assert.DoesNotContain(e.Log.Warnings, w => w.Code == "JK_SECTOR_THRUST");
        // Le message de la scene (JK_SECTOR_THRUST_SKIPPED) compte cette instance : voir JkSectorThrustTests.
    }

    [Fact]
    public void SectorThrust_SubModelLimit_PlacesWhatFitsAndLogsTheRest()
    {
        using Env e = Make(ThrustLevel("(0.0/0.0/1.0) 1.0 0 1 2 -1 -1 -1 -1 -1"));
        var subs = new JkSubModels();
        for (int i = 0; i < JkObjects.MaxSubModels - 2; i++) subs.Add(new SubModelInput([JkVolumeBrushes.Box(Vector3.Zero, new Vector3(4))], JkObjects.TriggerShader));

        var converted = JkSectorThrust.Convert(e.Prepared, e.Fs, e.Opt, e.Log, subs);

        Assert.Equal(2, Of(converted, "trigger_push").Count);   // il restait deux places sur trois secteurs
        Assert.Equal(JkObjects.MaxSubModels, subs.Items.Count);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SECTOR_THRUST_SKIPPED" && w.Message.Contains("1 "));
    }

    [Fact]
    public void FullChain_SectorThrust_CompilesToTheBsp()
    {
        using Env e = Make(ThrustLevel("(0.0/0.0/1.0) 2.0 0 1 -1 -1 -1 -1 -1 -1"));
        Directory.CreateDirectory(e.Opt.JaBaseDir);
        var log = new ImportLog();
        ImportStepResult imported = PipelineSteps.Import(e.Prepared.Scene, "jkthrust", e.Opt, log, JkObjects.ForPipeline(e.Prepared, e.Fs));
        ExportStepResult exported = PipelineSteps.Export(imported, e.Opt, log);

        Assert.Equal(1 + 2, exported.Bsp.Models.Count); // monde + deux zones
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(exported.Bsp.Entities, "\"classname\" \"trigger_push\"").Count);
        Assert.Contains("\"classname\" \"target_position\"", exported.Bsp.Entities);
    }

    /// <summary>Zones de poussee par niveau (poussee du fichier ou SectorThrust du seul message startup : 00_sectorthrust, M5_ANTIGRAV), relevees sur les donnees du jeu (255 au total).</summary>
    private static readonly SortedDictionary<string, int> ExpectedThrustZones = new(StringComparer.Ordinal)
    {
        ["04escapehouse"] = 123, ["06abarons"] = 27, ["08escape88"] = 12, ["09fuelstation"] = 2, ["10cargo"] = 10, ["11gorc"] = 2, ["14tower"] = 28,
        ["16aescapeship"] = 17, ["16bescapeship"] = 17, ["18ascend"] = 10, ["19b"] = 3, ["m5"] = 4,
    };

    [SkippableFact]
    public void RealLevels_SectorThrustZonesPerLevel()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var report = new System.Text.StringBuilder();
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, subs) = JkObjects.Convert(prepared, fs, opt, log);
            Assert.True(subs.Count <= JkObjects.MaxSubModels, $"{level.Name}: {subs.Count} sub-models");
            int pushes = Of(converted, "trigger_push").Count, targets = Of(converted, "target_position").Count;
            Assert.All(Of(converted, "trigger_push"), p => Assert.Equal("17", p["spawnflags"]));
            Assert.Equal(Of(converted, "trigger_push").Select(p => p["target"]).Distinct().Count(), targets);
            Assert.DoesNotContain(log.Warnings, w => w.Code == "JK_SUBMODEL_LIMIT");
            if (pushes > 0)
            {
                found[level.Name] = pushes;
                Assert.Contains(log.Warnings, w => w.Code == "JK_SECTOR_THRUST");
            }
            foreach (var w in log.Warnings.Where(w => w.Code.StartsWith("JK_SECTOR_THRUST", StringComparison.Ordinal)))
                report.AppendLine($"{level.Name}\t{w.Code}\t{w.Message}");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jk_l2_thrust.tsv"), string.Join('\n', found.Select(kv => $"{kv.Key}\t{kv.Value}")) + "\n" + report);
        Assert.Equal(ExpectedThrustZones, found);
        Assert.Equal(255, found.Values.Sum());
    }
}

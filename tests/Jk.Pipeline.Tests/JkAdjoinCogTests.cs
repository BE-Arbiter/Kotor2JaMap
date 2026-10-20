using Kotor.Core.Diagnostics;

namespace Jk.Pipeline.Tests;

/// <summary>Adjoins ouverts par des COG sans mouvement (SetAdjoinFlags : champs de force, grilles, vitres) : le passage ne doit pas rester ferme pour toujours.</summary>
public class JkAdjoinCogTests
{
    private const float Scale = (float)JkWorldScale.MetresPerJkUnit;

    private const string GateCog = """
        symbols
            message activated
            surface front
            surface back
            int flag local
        end
        code
        activated:
            SetAdjoinFlags(front, 2);
            SetAdjoinFlags(back, 0x2);
            return;
        end
        """;

    // Secteurs 0 et 1 : surfaces 0 (sol), 1 (mur droit du secteur 0, adjoin 0), 2 (sol), 3 (mur gauche du secteur 1, adjoin 1).
    private static JkPreparedScene Build(string script, string values, uint adjoinFlags, ImportLog log) =>
        SyntheticLevel.Build(new SyntheticLevel
        {
            Sectors = 2,
            AdjoinFlags = adjoinFlags,
            Cogs = [$"gate.cog {values}"],
            CogScripts = new Dictionary<string, string> { ["gate.cog"] = script },
        }, log);

    private static bool WallIsSolid(JkPreparedScene s) =>
        s.Scene.Walkmeshes.Any(w => Enumerable.Range(0, w.Triangles.Length / 3)
            .Any(t => Enumerable.Range(0, 3).All(k => Math.Abs(w.Positions[w.Triangles[t * 3 + k]].X - Scale) < 1e-3f)));

    [Fact]
    public void SetAdjoinFlagsOnBlockedAdjoin_MakesItPassable_AndLogsCount()
    {
        var log = new ImportLog();
        JkPreparedScene s = Build(GateCog, "1 3", 0x5, log);
        Assert.False(WallIsSolid(s), "a COG opens this adjoin: the wall must not block forever");
        (string Code, string Message) w = Assert.Single(log.Warnings, x => x.Code == "JK_ADJOIN_COG_OPENED");
        Assert.Contains("2 adjoin(s)", w.Message);
    }

    [Fact]
    public void BlockedAdjoinNoCogTouches_StaysSolid_AndNoCode()
    {
        var log = new ImportLog();
        JkPreparedScene s = SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, AdjoinFlags = 0x5 }, log);
        Assert.True(WallIsSolid(s));
        Assert.DoesNotContain(log.Warnings, x => x.Code == "JK_ADJOIN_COG_OPENED");
    }

    [Fact]
    public void SetAdjoinFlagsWithoutMoveBit_DoesNotOpen()
    {
        const string noMove = """
            symbols
                message activated
                surface front
            end
            code
            activated:
                SetAdjoinFlags(front, 8);
                return;
            end
            """;
        var log = new ImportLog();
        JkPreparedScene s = Build(noMove, "1", 0x5, log);
        Assert.True(WallIsSolid(s));
        Assert.DoesNotContain(log.Warnings, x => x.Code == "JK_ADJOIN_COG_OPENED");
    }
}

/// <summary>
/// Poussee de secteur (THRUST, SetSectorThrust) : la poussee continue (fichier, ou <c>SectorThrust</c> au demarrage seulement) est convertie en <c>trigger_push</c>
/// (voir <c>JkSectorThrustConversionTests</c>) ; seule celle que des evenements changent (interrupteur, minuterie, pulse) reste journalisee <c>JK_SECTOR_THRUST_SKIPPED</c> avec les comptes.
/// </summary>
public class JkSectorThrustTests
{
    /// <summary>Poussee allumee au demarrage puis coupee par un message : pas continue.</summary>
    private const string SwitchedThrustCog = """
        symbols
            message startup
            message activate
            sector area
            vector push=(0.0/0.0/3.0)
        end
        code
        startup:
            SetSectorThrust(area, push);
            return;
        activate:
            SetSectorThrust(area, push, 0.0);
            return;
        end
        """;

    /// <summary>Poussee continue, comme 00_sectorthrust : converties, jamais comptees comme ignorees.</summary>
    private const string ContinuousThrustCog = """
        symbols
            message startup
            sector area
            vector push=(0.0/0.0/3.0)
        end
        code
        startup:
            SetSectorThrust(area, push);
            return;
        end
        """;

    [Fact]
    public void SectorThrustChangedByEvents_IsLoggedWithCounts_NotConverted()
    {
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel
        {
            Sectors = 2,
            Cogs = ["lift_thrust.cog 0", "lift_thrust.cog 1"],
            CogScripts = new Dictionary<string, string> { ["lift_thrust.cog"] = SwitchedThrustCog },
        }, log);
        (string Code, string Message) w = Assert.Single(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED");
        Assert.Contains("2 COG instance(s)", w.Message);
        Assert.Contains("1 script(s)", w.Message);
        Assert.Contains("lift_thrust.cog", w.Message);
    }

    [Fact]
    public void ContinuousSectorThrust_IsNotLoggedAsSkipped()
    {
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel
        {
            Sectors = 2,
            SectorSounds = new Dictionary<int, string> { [1] = "wind.wav 1\nTHRUST 0.0 0.0 2.0" },
            Cogs = ["00_sectorthrust.cog 0", "00_sectorthrust.cog 1"],
            CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = ContinuousThrustCog },
        }, log);
        Assert.DoesNotContain(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED");
    }

    [Fact]
    public void NoThrust_NoCode()
    {
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel { Sectors = 2 }, log);
        Assert.DoesNotContain(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED");
    }
}

public class JkSectorThrustVerbTests
{
    [Fact]
    public void SectorThrustVerb_InAnEventHandler_IsCounted()
    {
        const string cog = """
            symbols
                message startup
                message timer
                sector sector0
                vector vec0=(0.0/0.0/3.0)
            end
            code
            startup:
                SetTimer(1.0);
                return;
            timer:
                if (sector0 >= 0) SectorThrust(sector0, vec0, 1.0);
                return;
            end
            """;
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, Cogs = ["00_sectorthrust.cog 1"], CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = cog } }, log);
        Assert.Contains("1 COG instance(s)", Assert.Single(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED").Message);
    }
}

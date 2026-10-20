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

/// <summary>Poussee de secteur (THRUST, SetSectorThrust) : jamais convertie, mais journalisee avec les comptes.</summary>
public class JkSectorThrustTests
{

    private const string ThrustCog = """
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
    public void SectorThrust_IsLoggedWithCounts_NotConverted()
    {
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel
        {
            Sectors = 2,
            SectorSounds = new Dictionary<int, string> { [1] = "wind.wav 1\nTHRUST 0.0 0.0 2.0" },
            Cogs = ["00_sectorthrust.cog 0", "00_sectorthrust.cog 1"],
            CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = ThrustCog },
        }, log);
        (string Code, string Message) w = Assert.Single(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED");
        Assert.Contains("1 sector(s) carry a THRUST", w.Message);
        Assert.Contains("2 COG instance(s)", w.Message);
        Assert.Contains("1 script(s)", w.Message);
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
    public void SectorThrustVerb_AsInTheGameScripts_IsCounted()
    {
        const string cog = """
            symbols
                message startup
                sector sector0
                vector vec0=(0.0/0.0/3.0)
            end
            code
            startup:
                if (sector0 >= 0) SectorThrust(sector0, vec0, 1.0);
                return;
            end
            """;
        var log = new Kotor.Core.Diagnostics.ImportLog();
        SyntheticLevel.Build(new SyntheticLevel { Sectors = 2, Cogs = ["00_sectorthrust.cog 1"], CogScripts = new Dictionary<string, string> { ["00_sectorthrust.cog"] = cog } }, log);
        Assert.Contains("1 COG instance(s)", Assert.Single(log.Warnings, x => x.Code == "JK_SECTOR_THRUST_SKIPPED").Message);
    }
}

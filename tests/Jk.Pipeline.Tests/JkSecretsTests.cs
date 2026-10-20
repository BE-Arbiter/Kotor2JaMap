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

// Zones secretes : 00_SecretAreas.cog (secteurs lies, message « entered ») -> trigger_multiple + target_secret (decompte « secrets found » de JA).
// Classe partielle de JkThingConverterTests pour reutiliser la fabrication de niveaux et de GOB synthetiques.
public sealed partial class JkThingConverterTests
{
    private const string SecretAreasCog = """
        symbols
            message startup
            message entered
            sector  secret0   nolink local
            sector  secret1   linkid=1
            sector  secret2   linkid=2
            sector  secret3   linkid=4
            int     player    local
            int     flags=0   local
        end
        code
        startup:
            player = GetLocalPlayerThing();
            SetInv(player, 70, 0);
            Return;
        entered:
            if (flags & GetSenderID()) Return;
            flags = flags | GetSenderId();
            ChangeInv(player, 70, 1.0);
            jkPrintUNIString(player, 360);
            Return;
        end
        """;

    private static SyntheticLevel SecretLevel(string instance, string script = SecretAreasCog, string file = "00_SecretAreas.cog") => new()
    {
        Sectors = 3,
        Cogs = [file + " " + instance],
        CogScripts = new Dictionary<string, string> { [file] = script },
    };

    [Fact]
    public void SecretAreas_BecomeTriggersAndOneTargetSecretWithTheTotal()
    {
        using Env e = Make(SecretLevel("1 0 -1"));
        var (converted, subModels) = Run(e);

        var triggers = Of(converted, "trigger_multiple");
        Assert.Equal(2, triggers.Count);
        foreach (var t in triggers)
        {
            Assert.Equal("jk_secret", t["target"]);
            Assert.Equal("-1", t["wait"]);       // une seule fois : chaque zone compte une fois
            Assert.Equal("1", t["spawnflags"]);  // PLAYERONLY
            SubModelInput sub = subModels[int.Parse(t["model"][1..]) - 1];
            Assert.Equal(BspContents.Trigger, sub.Contents); // jamais solide
        }
        var secret = Assert.Single(Of(converted, "target_secret"));
        Assert.Equal("jk_secret", secret["targetname"]);
        Assert.Equal("2", secret["count"]);      // total du niveau, lu par le decompte « secrets found » de JA
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SECRET_AREAS" && w.Message.Contains("2 secret area"));
    }

    [Fact]
    public void SecretArea_TriggerCoversTheSector()
    {
        using Env e = Make(SecretLevel("1 -1 -1"));
        var (converted, subModels) = Run(e);

        var trigger = Assert.Single(Of(converted, "trigger_multiple"));
        SubModelInput sub = subModels[int.Parse(trigger["model"][1..]) - 1];
        var (lo, hi) = BrushBounds.FromPlanes(sub.Brushes[0])!.Value;
        // Le secteur 1 du niveau synthetique : x de 1 a 2, y de 0 a 1, z de 0 a 0,5.
        Assert.Equal(1f * K, lo.X, 0.5f);
        Assert.Equal(2f * K, hi.X, 0.5f);
        Assert.Equal(0f, lo.Y, 0.5f);
        Assert.Equal(1f * K, hi.Y, 0.5f);
        Assert.Equal(0.5f * K, hi.Z, 0.5f);
    }

    [Fact]
    public void SecretAreas_SameSectorTwice_CountsOnce()
    {
        using Env e = Make(SecretLevel("1 1 -1"));
        var (converted, _) = Run(e);

        Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("1", Assert.Single(Of(converted, "target_secret"))["count"]);
    }

    [Fact]
    public void SecretAreasScriptWithoutSectors_PlacesNothing()
    {
        using Env e = Make(SecretLevel("-1 -1 -1"));
        var (converted, _) = Run(e);

        Assert.Empty(Of(converted, "trigger_multiple"));
        Assert.Empty(Of(converted, "target_secret"));
        Assert.DoesNotContain(e.Log.Warnings, w => w.Code.StartsWith("JK_SECRET", StringComparison.Ordinal));
    }

    [Fact]
    public void SecretSectorOutsideTheLevel_IsSkippedAndLogged()
    {
        using Env e = Make(SecretLevel("1 99 -1"));
        var (converted, _) = Run(e);

        Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("1", Assert.Single(Of(converted, "target_secret"))["count"]);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SECRET_SKIPPED");
    }

    [Fact]
    public void ScriptWithSectorsButNoSecretCounter_IsNotASecretScript()
    {
        // Des symboles de secteur qui ne touchent pas au compteur de secrets (inventaire 70) : pas une zone secrete.
        const string Other = """
            symbols
                message entered
                sector  secret1   linkid=1
            end
            code
            entered:
                Print("hello");
                Return;
            end
            """;
        using Env e = Make(SecretLevel("1", Other, "00_other.cog"));
        var (converted, _) = Run(e);

        Assert.Empty(Of(converted, "trigger_multiple"));
        Assert.Empty(Of(converted, "target_secret"));
    }

    [Fact]
    public void FullChain_SecretAreas_CompileToTheBsp()
    {
        using Env e = Make(SecretLevel("1 0 -1"));
        Directory.CreateDirectory(e.Opt.JaBaseDir);
        var log = new ImportLog();
        ImportStepResult imported = PipelineSteps.Import(e.Prepared.Scene, "jksecret", e.Opt, log, JkObjects.ForPipeline(e.Prepared, e.Fs));
        ExportStepResult exported = PipelineSteps.Export(imported, e.Opt, log);

        Assert.Equal(1 + 2, exported.Bsp.Models.Count); // monde + deux zones
        Assert.Contains("\"classname\" \"target_secret\"", exported.Bsp.Entities);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(exported.Bsp.Entities, "\"classname\" \"trigger_multiple\"").Count);
        Assert.Equal(BspContents.Trigger, exported.Bsp.ShaderContents[exported.Bsp.Shaders.ToList().IndexOf("textures/jk/trigger_hurt")]);
    }

    /// <summary>Zones secretes par niveau, relevees sur les donnees du jeu (instances de <c>00_SecretAreas.cog</c>, secteurs lies renseignes).</summary>
    private static readonly SortedDictionary<string, int> ExpectedSecrets = new(StringComparer.Ordinal)
    {
        ["01narshadda"] = 6, ["02narshadda"] = 8, ["03katarn"] = 8, ["04escapehouse"] = 6, ["06abarons"] = 6, ["06bbarons"] = 1, ["08escape88"] = 5,
        ["09fuelstation"] = 10, ["10cargo"] = 6, ["12escape"] = 4, ["14tower"] = 7, ["16aescapeship"] = 1, ["16bescapeship"] = 1, ["18ascend"] = 6,
        ["19a"] = 7, ["19b"] = 8,
    };

    [SkippableFact]
    public void RealLevels_SecretAreasPerLevel()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, _) = JkObjects.Convert(prepared, fs, opt, log);
            int triggers = converted.Entities.Count(x => x["classname"] == "trigger_multiple" && x.GetValueOrDefault("target") == "jk_secret");
            if (triggers > 0)
            {
                found[level.Name] = triggers;
                Assert.Equal(triggers.ToString(System.Globalization.CultureInfo.InvariantCulture), Assert.Single(converted.Entities, x => x["classname"] == "target_secret")["count"]);
                Assert.DoesNotContain(log.Warnings, w => w.Code == "JK_SECRET_SKIPPED");
            }
            else Assert.DoesNotContain(converted.Entities, x => x["classname"] == "target_secret");
        }
        Assert.Equal(ExpectedSecrets, found);
        Assert.Equal(90, found.Values.Sum());
    }
}

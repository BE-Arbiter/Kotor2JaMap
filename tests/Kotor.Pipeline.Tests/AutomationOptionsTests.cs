using System.Numerics;
using Kotor.Core;
using Kotor.Pipeline.Workflow;

namespace Kotor.Pipeline.Tests;

public class AutomationOptionsTests
{
    [Fact]
    public void Automation_parse_full()
    {
        var o = AutomationParser.Parse(["--game", "kotor2", "--module", "tar_m02aa", "--run-to", "export",
            "--camera", "1 2.5 -3 90 -20", "--screenshot", "out/shot.png", "--exit"], out string? error);
        Assert.Null(error);
        Assert.NotNull(o);
        Assert.Equal(KotorGame.Kotor2, o.Game);
        Assert.Equal("tar_m02aa", o.Module);
        Assert.Equal("export", o.RunTo);
        Assert.Equal(new Vector3(1, 2.5f, -3), o.CameraPosition);
        Assert.Equal(90f, o.CameraYaw);
        Assert.Equal(-20f, o.CameraPitch);
        Assert.Equal("out/shot.png", o.ScreenshotPath);
        Assert.True(o.Exit);
        Assert.Equal(2, o.StageIndex);
    }

    [Fact]
    public void Automation_defaults()
    {
        var o = AutomationParser.Parse(["--module", "m"], out string? error);
        Assert.Null(error);
        Assert.NotNull(o);
        Assert.Equal(KotorGame.Kotor1, o.Game);
        Assert.Equal("preview", o.RunTo);
        Assert.Null(o.CameraPosition);
        Assert.Null(o.ScreenshotPath);
        Assert.False(o.Exit);
    }

    [Fact]
    public void Automation_parse_invalid_runto()
    {
        Assert.Null(AutomationParser.Parse(["--module", "m", "--run-to", "bsp"], out string? error));
        Assert.Contains("--run-to", error);
    }

    [Theory]
    [InlineData("--game", "tfu")]
    [InlineData("--camera", "1 2 3")]
    [InlineData("--camera", "a b c d e")]
    public void Automation_parse_invalid_values(string flag, string value)
    {
        Assert.Null(AutomationParser.Parse(["--module", "m", flag, value], out string? error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Automation_parse_unknown_or_incomplete_argument()
    {
        Assert.Null(AutomationParser.Parse(["--module", "m", "--bogus"], out string? e1));
        Assert.Contains("--bogus", e1);
        Assert.Null(AutomationParser.Parse(["--module"], out string? e2));
        Assert.NotNull(e2);
    }

    [Fact]
    public void Automation_none_without_module_and_error_for_options_alone()
    {
        Assert.Null(AutomationParser.Parse([], out string? error));
        Assert.Null(error);
        foreach (string[] args in new[] { new[] { "--exit" }, ["--screenshot", "a.png"], ["--game", "kotor"] })
        {
            Assert.Null(AutomationParser.Parse(args, out string? e));
            Assert.Contains("--module", e);
        }
    }
}

using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Pipeline.Assembly;

namespace Kotor.Pipeline.Tests.Assembly;

public class ModuleSelectionTests
{
    [Theory]
    [InlineData("end_m01aa", new[] { "end_m01aa" })]
    [InlineData("End_M01AA,end_m01ab", new[] { "end_m01aa", "end_m01ab" })]
    [InlineData(" a , b ,c ", new[] { "a", "b", "c" })]
    public void TryParse_splits_trims_and_lowercases(string text, string[] expected)
    {
        Assert.True(ModuleSelection.TryParse(text, out var modules, out string? error));
        Assert.Null(error);
        Assert.Equal(expected, modules);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData("a,,b")]
    [InlineData("a,")]
    public void TryParse_rejects_empty_entries(string text)
    {
        Assert.False(ModuleSelection.TryParse(text, out _, out string? error));
        Assert.Contains("empty", error);
    }

    [Fact]
    public void TryParse_rejects_duplicates()
    {
        Assert.False(ModuleSelection.TryParse("a,B,b", out _, out string? error));
        Assert.Contains("'b'", error);
        Assert.Contains("twice", error);
    }

    [Fact]
    public void Default_map_name_is_reference_and_count()
    {
        Assert.Equal("end_m01aa_2", ModuleSelection.DefaultMapName("end_m01aa", 2));
        Assert.Equal("tar_m02aa_3", ModuleSelection.DefaultMapName("Tar_M02AA", 3));
    }

    [Fact]
    public void Not_linked_message_names_the_modules()
    {
        Assert.Equal("Modules not linked: a, b. Use the application to place them manually.",
            ModuleSelection.NotLinkedMessage(["a", "b"]));
    }

    [Fact]
    public void Unlinked_modules_of_a_session()
    {
        var p = new System.Numerics.Vector3();
        AssemblyPart P(string m) => new(new Kotor.Core.Scene.ModuleScene(new Kotor.Core.Modules.ModuleInfo(m, m, m,
            new Kotor.Core.Modules.ModuleEntry(p, System.Numerics.Vector2.UnitY)), [], [], new Dictionary<string, Kotor.Core.Textures.KotorTexture?>()),
            [new ModuleEntity(EntityKind.Door, "d_" + m, "", p, 0f, null)]);
        var s = new AssemblySession(["m1", "m2", "m3"], [P("m1"), P("m2"), P("m3")], [], (_, d) => DoorFrames.DefaultFrame(d));
        Assert.Equal(["m2", "m3"], ModuleSelection.UnlinkedModules(s));
    }

    [SkippableFact]
    public void Endar_spire_modules_are_linked_by_a_door()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR not installed");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.GetTempPath(), Path.GetTempPath());
        var log = new ImportLog();
        AssemblySession s = AssemblyLoader.Load(["end_m01aa", "end_m01ab"], 0, opt, log);
        Assert.True(s.IsComplete);
        DoorLink link = Assert.Single(s.AutoLinks);
        Assert.Equal(0, link.FromPart);
        Assert.Equal(EntityKind.Door, link.FromDoor.Kind);
        Assert.Equal("end_m01ab", link.FromDoor.LinkedToModule);
        Assert.Equal("from_01a", link.ToTarget.Name, ignoreCase: true);
    }

    [SkippableFact]
    public void Loader_rejects_a_module_of_another_game()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR not installed");
        var opt = new ExportOptions(TestEnv.KotorDir!, Path.GetTempPath(), Path.GetTempPath());
        var ex = Assert.Throws<PipelineStepException>(() => AssemblyLoader.Load(["end_m01aa", "001ebo"], 0, opt, new ImportLog()));
        Assert.Equal("ASSEMBLY_MIXED_GAMES", ex.Code);
    }
}

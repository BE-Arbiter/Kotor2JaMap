using Kotor.Core.Archives;

namespace Kotor.Core.Tests.Archives;

public class KeyBifArchiveTests
{
    [SkippableFact]
    public void Real_key_lists_and_reads_models()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var key = KeyBifArchive.Open(TestEnv.KotorDir!);
        Assert.Contains(new ResourceKey("m02aa_01a", ResourceType.Mdl), key.Keys);
        Assert.True(key.TryRead(new ResourceKey("m02aa_01a", ResourceType.Mdl), out var d));
        Assert.True(d.Length > 12);
        Assert.False(key.TryRead(new ResourceKey("zz_nope", ResourceType.Mdl), out _));
    }
}

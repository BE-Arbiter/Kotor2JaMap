using Kotor.Core.Archives;

namespace Kotor.Core.Tests.Archives;

public class RimArchiveTests
{
    [Fact]
    public void Rim_reads_synthetic_entries()
    {
        string path = Path.Combine(Path.GetTempPath(), "k2j_" + Guid.NewGuid().ToString("N") + ".rim");
        try
        {
            File.WriteAllBytes(path, ArchiveBuilders.Rim(
                ("a_one", (uint)ResourceType.Lyt, new byte[] { 1, 2, 3 }),
                ("b_two", (uint)ResourceType.Are, new byte[] { 9, 8 })));
            using var rim = RimArchive.Open(path);
            Assert.Equal(2, rim.Keys.Count());
            Assert.True(rim.TryRead(new ResourceKey("a_one", ResourceType.Lyt), out var d1));
            Assert.Equal(new byte[] { 1, 2, 3 }, d1);
            Assert.True(rim.TryRead(new ResourceKey("B_TWO", ResourceType.Are), out var d2));
            Assert.Equal(new byte[] { 9, 8 }, d2);
            Assert.False(rim.TryRead(new ResourceKey("a_one", ResourceType.Are), out _));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Keys_are_lowercase()
    {
        var k = new ResourceKey("M02AA_01A", ResourceType.Mdl);
        Assert.Equal("m02aa_01a", k.ResRef);
    }
}

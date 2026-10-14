using Kotor.Core.Archives;

namespace Kotor.Core.Tests.Archives;

public class ErfArchiveTests
{
    private static void Check(string sig)
    {
        string path = Path.Combine(Path.GetTempPath(), "k2j_" + Guid.NewGuid().ToString("N") + ".erf");
        try
        {
            File.WriteAllBytes(path, ArchiveBuilders.Erf(sig,
                ("tex_a", (ushort)ResourceType.Tpc, new byte[] { 5, 6, 7, 8 }),
                ("tex_a", (ushort)ResourceType.Txi, new byte[] { 1 })));
            using var erf = ErfArchive.Open(path);
            Assert.Equal(2, erf.Keys.Count());
            Assert.True(erf.TryRead(new ResourceKey("tex_a", ResourceType.Tpc), out var d));
            Assert.Equal(new byte[] { 5, 6, 7, 8 }, d);
            Assert.True(erf.TryRead(new ResourceKey("tex_a", ResourceType.Txi), out var t));
            Assert.Equal(new byte[] { 1 }, t);
        }
        finally { File.Delete(path); }
    }

    [Fact] public void Erf_reads_synthetic_entries() => Check("ERF V1.0");

    [Fact] public void Mod_signature_is_accepted() => Check("MOD V1.0");
}

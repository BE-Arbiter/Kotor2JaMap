using System.Text;
using Kotor.Core.Tlk;

namespace Kotor.Core.Tests.Tlk;

public class TlkFileTests
{
    [Fact]
    public void Tlk_reads_synthetic_entry()
    {
        string path = Path.Combine(Path.GetTempPath(), "k2j_" + Guid.NewGuid().ToString("N") + ".tlk");
        try
        {
            byte[] t0 = Encoding.Latin1.GetBytes("zero");
            byte[] t1 = Encoding.Latin1.GetBytes("Café Taris");
            using var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("TLK V3.0"));
            w.Write(0u); w.Write(2u); w.Write(20u + 2 * 40);
            uint off = 0;
            foreach (var t in new[] { t0, t1 })
            {
                w.Write(1u); w.Write(new byte[16]); w.Write(0u); w.Write(0u);
                w.Write(off); w.Write((uint)t.Length); w.Write(0f);
                off += (uint)t.Length;
            }
            w.Write(t0); w.Write(t1);
            File.WriteAllBytes(path, ms.ToArray());
            var tlk = TlkFile.Open(path);
            Assert.Equal("zero", tlk.Get(0));
            Assert.Equal("Café Taris", tlk.Get(1));
            Assert.Null(tlk.Get(999));
            Assert.Null(tlk.Get(0xFFFFFFFF));
        }
        finally { File.Delete(path); }
    }
}

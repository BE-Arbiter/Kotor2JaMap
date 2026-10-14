using System.Text;

namespace Kotor.Core.Tests.Archives;

/// <summary>Constructeurs d'archives RIM/ERF synthetiques pour les tests.</summary>
internal static class ArchiveBuilders
{
    public static byte[] Rim(params (string resRef, uint type, byte[] data)[] entries)
    {
        int keysOff = 0x78;
        int dataOff = keysOff + entries.Length * 32;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIM V1.0"));
        w.Write(0u);
        w.Write((uint)entries.Length);
        w.Write((uint)keysOff);
        w.Write(new byte[keysOff - (int)ms.Position]);
        int off = dataOff;
        for (int i = 0; i < entries.Length; i++)
        {
            w.Write(RefBytes(entries[i].resRef));
            w.Write(entries[i].type);
            w.Write((uint)i);
            w.Write((uint)off);
            w.Write((uint)entries[i].data.Length);
            off += entries[i].data.Length;
        }
        foreach (var e in entries) w.Write(e.data);
        return ms.ToArray();
    }

    public static byte[] Erf(string sig, params (string resRef, ushort type, byte[] data)[] entries)
    {
        int keysOff = 160;
        int resOff = keysOff + entries.Length * 24;
        int dataOff = resOff + entries.Length * 8;
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes(sig));
        w.Write(0u);              // langCount
        w.Write(0u);              // locStrSize
        w.Write((uint)entries.Length);
        w.Write(160u);            // locStrOff
        w.Write((uint)keysOff);
        w.Write((uint)resOff);
        w.Write(new byte[keysOff - (int)ms.Position]);
        for (int i = 0; i < entries.Length; i++)
        {
            w.Write(RefBytes(entries[i].resRef));
            w.Write((uint)i);
            w.Write(entries[i].type);
            w.Write((ushort)0);
        }
        int off = dataOff;
        foreach (var e in entries) { w.Write((uint)off); w.Write((uint)e.data.Length); off += e.data.Length; }
        foreach (var e in entries) w.Write(e.data);
        return ms.ToArray();
    }

    private static byte[] RefBytes(string s)
    {
        var b = new byte[16];
        Encoding.ASCII.GetBytes(s, 0, Math.Min(16, s.Length), b, 0);
        return b;
    }
}

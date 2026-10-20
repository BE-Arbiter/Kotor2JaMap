using System.Text;

namespace Jk.Core.Tests;

/// <summary>
/// Outils de test : construction de GOB en memoire / sur disque.
/// Ce fichier est aussi compile (lien) par Jk.Pipeline.Tests et Kotor2Ja.Cli.Tests : il ne depend que de System.Text.
/// </summary>
internal static class GobBuilder
{
    /// <summary>GOB : "GOB ", version 0x14, offset d'index, donnees, puis index (u32 nombre, entrees offset/taille/char[128]).</summary>
    public static byte[] Build(params (string Path, string Content)[] files)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("GOB "));
        w.Write(0x14);
        w.Write(0); // offset de l'index, corrige plus bas
        var offsets = new List<(int Off, int Size)>();
        foreach (var f in files)
        {
            byte[] data = Encoding.ASCII.GetBytes(f.Content);
            offsets.Add(((int)ms.Position, data.Length));
            w.Write(data);
        }
        int indexOffset = (int)ms.Position;
        w.Write(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            w.Write(offsets[i].Off);
            w.Write(offsets[i].Size);
            byte[] name = new byte[128];
            Encoding.ASCII.GetBytes(files[i].Path).CopyTo(name, 0);
            w.Write(name);
        }
        w.Flush();
        byte[] bytes = ms.ToArray();
        BitConverter.GetBytes(indexOffset).CopyTo(bytes, 8);
        return bytes;
    }

    public static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "jk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    public static void WriteGob(string dir, string sub, string name, params (string Path, string Content)[] files)
    {
        string folder = Path.Combine(dir, sub);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name), Build(files));
    }
}

using System.IO.Compression;

namespace Kotor.Core.Export;

/// <summary>Regroupe bsp, shader et textures d'un niveau dans un pk3 (zip) pour Jedi Academy.</summary>
public static class Pk3Builder
{
    public static void Build(string workDir, string level, string pk3Path)
    {
        string bsp = Path.Combine(workDir, "maps", level + ".bsp");
        string shader = Path.Combine(workDir, "shaders", level + ".shader");
        string texDir = Path.Combine(workDir, "textures");

        foreach (string f in new[] { bsp, shader })
        {
            if (!File.Exists(f)) throw new FileNotFoundException($"Fichier attendu introuvable : {f}", f);
        }

        if (!Directory.Exists(texDir)) throw new FileNotFoundException($"Dossier de textures introuvable : {texDir}", texDir);
        // Seules les TGA referencees par le shader du niveau sont embarquees (pas les restes d'anciens exports).
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string prefix = "map textures/tfu2ja/";
        foreach (string line in File.ReadLines(shader))
        {
            string l = line.Trim();
            if (l.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && l.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                referenced.Add(l.Substring(prefix.Length));
        }

        string[] tgas = Directory.GetFiles(texDir, "*.tga").Where(t => referenced.Contains(Path.GetFileName(t))).ToArray();

        string tmp = pk3Path + ".tmp";
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                Add(zip, bsp, $"maps/{level}.bsp");
                Add(zip, shader, $"shaders/{level}.shader");
                foreach (string t in tgas.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    Add(zip, t, "textures/tfu2ja/" + Path.GetFileName(t));
                }
            }

            File.Move(tmp, pk3Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static void Add(ZipArchive zip, string file, string entryName)
    {
        zip.CreateEntryFromFile(file, entryName.Replace((char)92, '/').ToLowerInvariant(), CompressionLevel.Optimal);
    }
}

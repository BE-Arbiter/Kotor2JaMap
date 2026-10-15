using System.IO.Compression;

namespace Kotor.Core.Export;

/// <summary>Regroupe le contenu d'un dossier de preparation (bsp, shader, textures) dans un pk3 (zip) pour Jedi Academy.</summary>
public static class Pk3Builder
{
    /// <summary>
    /// Zippe tous les fichiers de <paramref name="stagingDir"/> (noms en minuscules, separateur <c>/</c>). Le pk3 est ecrit dans un
    /// fichier temporaire puis renomme : un pk3 existant n'est remplace que si tout reussit. Exige au moins un <c>maps/*.bsp</c>.
    /// </summary>
    public static void Build(string stagingDir, string pk3Path)
    {
        string mapsDir = Path.Combine(stagingDir, "maps");
        if (!Directory.Exists(mapsDir) || Directory.GetFiles(mapsDir, "*.bsp").Length == 0)
            throw new FileNotFoundException($"Expected file not found: {Path.Combine(mapsDir, "*.bsp")}", mapsDir);

        string[] files = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories);
        string tmp = pk3Path + ".tmp";
        try
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                foreach (string f in files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    Add(zip, f, Path.GetRelativePath(stagingDir, f));
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
        // Fixed date (the zip format cannot go below 1980-01-01): the pk3 is byte-for-byte reproducible.
        ZipArchiveEntry entry = zip.CreateEntry(entryName.Replace((char)92, '/').ToLowerInvariant(), CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using Stream dst = entry.Open();
        using FileStream src = File.OpenRead(file);
        src.CopyTo(dst);
    }

    private static readonly DateTimeOffset FixedTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
}

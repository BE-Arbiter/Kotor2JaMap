using System.CommandLine;
using Kotor.Core.Export;
using Swtor.Core;
using Swtor.Core.Myp;

namespace Kotor2Ja.Cli.Commands;

/// <summary>
/// swtor-extract : lit les archives .tor du dossier du jeu et extrait les ressources utiles (zones, modeles, materiaux, textures, chaines)
/// vers le dossier de ressources lu par les autres commandes SWTOR. Les noms de fichiers viennent de la liste de Jedipedia,
/// telechargee (puis gardee en cache) ou d'un fichier fourni.
/// </summary>
public static class SwtorExtractCommand
{
    public const string NamesCacheFileName = "swtor_filenames.txt";

    public static Command Create()
    {
        var fromOpt = new Option<string?>("--from") { Description = "SWTOR install folder (or its Assets / swtor\\retailclient folder). Default: SWTOR_PATH, then Steam and EA/BioWare folders." };
        var assetsOpt = new Option<string?>("--assets") { Description = "Output folder for the extracted resources (default: SWTOR_ASSETS, the swtorAssetsDir config key, then C:/jka_tor_assets/resources)." };
        var namesOpt = new Option<string?>("--names") { Description = "Use this file name list (one /resources/... path per line) instead of the Jedipedia download." };
        var offlineOpt = new Option<bool>("--no-download") { Description = "Never contact Jedipedia: use the cached list (or --names)." };
        var refreshOpt = new Option<bool>("--refresh-names") { Description = "Download the Jedipedia list again even if the cached one is up to date." };
        var onlyOpt = new Option<string[]>("--only") { Description = "Only extract paths starting with this prefix (e.g. world/areas/4611686142106770000). Repeatable. Default: " + string.Join(", ", SwtorExtractor.DefaultPrefixes) + ".", AllowMultipleArgumentsPerToken = true };
        var allOpt = new Option<bool>("--all") { Description = "Extract every named file (very large)." };
        var overwriteOpt = new Option<bool>("--overwrite") { Description = "Rewrite files that already exist with the right size." };
        var ptsOpt = new Option<bool>("--pts") { Description = "Read the public test client archives (swtor_test_*) instead of the live ones." };
        var workOpt = new Option<string?>("--work") { Description = "Work folder holding the name list cache (default: workDir of the configuration)." };
        var cfgOpt = new Option<string?>("--config") { Description = "JSON configuration file (default: kotor2ja.config.json next to the executable)." };
        var cmd = new Command("swtor-extract", "Extracts SWTOR resources from the game's .tor archives (file names from the Jedipedia list).");
        foreach (Option o in new Option[] { fromOpt, assetsOpt, namesOpt, offlineOpt, refreshOpt, onlyOpt, allOpt, overwriteOpt, ptsOpt, workOpt, cfgOpt }) cmd.Options.Add(o);
        cmd.SetAction(pr =>
        {
            string? cfg = pr.GetValue(cfgOpt) ?? GameOption.DefaultConfigPath;
            string? configured = null;
            try { configured = ConfigLoader.Load(cfg).SwtorDir; } catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException) { }
            string work = SwtorListAreasCommand.ResolveWorkDir(pr.GetValue(workOpt), cfg);
            return Run(pr.GetValue(fromOpt), pr.GetValue(assetsOpt) ?? configured, pr.GetValue(namesOpt), pr.GetValue(offlineOpt), pr.GetValue(refreshOpt),
                pr.GetValue(onlyOpt) ?? [], pr.GetValue(allOpt), pr.GetValue(overwriteOpt), pr.GetValue(ptsOpt), work, Console.Out, Console.Error);
        });
        return cmd;
    }

    public static int Run(string? from, string? assetsRoot, string? namesFile, bool offline, bool refresh, IReadOnlyList<string> only, bool all,
        bool overwrite, bool pts, string workDir, TextWriter output, TextWriter error, HttpClient? http = null)
    {
        string? game = from ?? SwtorGameLocator.Detect();
        if (game is null)
        {
            error.WriteLine("SWTOR install not found (use --from, or set SWTOR_PATH): the folder holding Assets\\*.tor and swtor\\retailclient.");
            return 2;
        }
        IReadOnlyList<string> archives = SwtorGameLocator.FindArchives(game, pts);
        if (archives.Count == 0)
        {
            error.WriteLine($"No .tor archive found under {game} (looked in the folder, Assets and swtor\\retailclient).");
            return 2;
        }

        string cache = Path.Combine(workDir, NamesCacheFileName);
        string listPath = namesFile ?? cache;
        try
        {
            if (namesFile is null && !offline)
            {
                output.WriteLine("Checking the file name list (Jedipedia)...");
                var status = MypNameList.RefreshAsync(cache, MypNameList.JedipediaUrl, refresh, http).GetAwaiter().GetResult();
                output.WriteLine(status switch
                {
                    MypNameList.RefreshStatus.Downloaded => $"Name list downloaded to {cache}.",
                    MypNameList.RefreshStatus.KeptCacheOffline => $"Jedipedia unreachable: using the cached list {cache}.",
                    _ => $"Name list up to date ({cache}).",
                });
            }
            if (!File.Exists(listPath))
            {
                error.WriteLine($"No file name list: {listPath} not found (remove --no-download, or pass --names).");
                return 2;
            }
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or InvalidDataException)
        {
            error.WriteLine("Cannot get the file name list from Jedipedia: " + e.Message + " (download it manually and pass --names).");
            return 1;
        }

        MypNameList names = MypNameList.Load(listPath);
        output.WriteLine($"{names.Count} known file name(s), {archives.Count} archive(s) in {game}.");
        string root = new SwtorAssets(assetsRoot).Root;
        IEnumerable<string>? prefixes = all ? null : only.Count > 0 ? only : SwtorExtractor.DefaultPrefixes;
        output.WriteLine($"Extracting {(prefixes is null ? "everything" : string.Join(", ", prefixes))} to {root}...");

        SwtorExtractResult r;
        try
        {
            r = SwtorExtractor.Extract(archives, names, root, prefixes, overwrite, new Progress<string>(output.WriteLine));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine("Extraction failed: " + e.Message);
            return 1;
        }
        output.WriteLine($"{r.Entries} entrie(s): {r.Selected} selected, {r.Written} written ({r.Bytes / (1024 * 1024)} MB), {r.Skipped} already present, {r.Unnamed} without a known name, {r.Failed} failed.");
        foreach (string err in r.Errors) error.WriteLine("  " + err);
        if (r.Selected == 0) error.WriteLine("Nothing matched: check the prefixes and that the name list is current.");
        return r.Failed > 0 || r.Selected == 0 ? 1 : 0;
    }
}

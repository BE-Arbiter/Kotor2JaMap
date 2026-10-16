using System.CommandLine;
using Kotor.Core.Export;
using Swtor.Core;
using Swtor.Core.Areas;

namespace Kotor2Ja.Cli.Commands;

/// <summary>swtor-list-areas : liste le catalogue des zones SWTOR (id, nom interne, categorie, nom lisible).</summary>
public static class SwtorListAreasCommand
{
    public static Command Create()
    {
        var assetsOption = new Option<string?>("--assets") { Description = "Racine des ressources SWTOR extraites (defaut : SWTOR_ASSETS, puis C:/jka_tor_assets/resources)." };
        var filterOption = new Option<string?>("--filter") { Description = "Ne garde que les zones dont l'id, le nom interne, le nom lisible, la categorie ou la planete contient ce texte." };
        var rebuildOption = new Option<bool>("--rebuild") { Description = "Reconstruit le catalogue (ouverture du GOM, quelques secondes) meme si le cache JSON existe." };
        var modelsOption = new Option<bool>("--models") { Description = "Ajoute le nombre exact d'instances de modele (lit les rooms : lent si la liste est longue, a combiner avec --filter)." };
        var workOption = new Option<string?>("--work") { Description = "Dossier de travail du cache swtor_areas.json (defaut : workDir de la configuration)." };
        var configOption = new Option<string?>("--config") { Description = "Fichier de configuration JSON (defaut : kotor2ja.config.json a cote de l'executable)." };
        var langOption = new Option<string>("--language") { Description = "Langue des noms (dossier de chaines : fr-fr, en-us).", DefaultValueFactory = _ => "fr-fr" };
        var cmd = new Command("swtor-list-areas", "Lists the SWTOR areas with their internal name, category and localised display name.");
        foreach (Option o in new Option[] { assetsOption, filterOption, rebuildOption, modelsOption, workOption, configOption, langOption }) cmd.Options.Add(o);
        cmd.SetAction(pr => Run(pr.GetValue(assetsOption), pr.GetValue(filterOption), pr.GetValue(rebuildOption), pr.GetValue(modelsOption),
            ResolveWorkDir(pr.GetValue(workOption), pr.GetValue(configOption)), pr.GetValue(langOption)!, Console.Out, Console.Error));
        return cmd;
    }

    /// <summary>Dossier de travail : option, sinon workDir de la configuration, sinon le dossier par defaut.</summary>
    public static string ResolveWorkDir(string? option, string? configPath)
    {
        if (!string.IsNullOrWhiteSpace(option)) return option;
        try { return ConfigLoader.Load(configPath ?? GameOption.DefaultConfigPath).WorkDir; }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return ConfigLoader.DefaultWorkDir;
        }
    }

    public static int Run(string? assetsRoot, string? filter, bool rebuild, bool models, string workDir, string language, TextWriter output, TextWriter error)
    {
        var assets = new SwtorAssets(assetsRoot);
        if (!Directory.Exists(Path.Combine(assets.Root, "world", "areas")))
        {
            error.WriteLine($"SWTOR resources not found: no world/areas folder under {assets.Root} (use --assets).");
            return 2;
        }
        SwtorAreaCatalog catalog;
        try
        {
            catalog = SwtorAreaCatalog.Load(assets, SwtorAreaCatalog.CachePath(workDir), rebuild, language);
        }
        catch (Exception ex) when (ex is GameFormatException or IOException)
        {
            error.WriteLine("Cannot build the area catalogue: " + ex.Message);
            return 1;
        }

        var rows = catalog.Filter(filter).OrderBy(e => e.Category, StringComparer.Ordinal).ThenBy(e => e.InternalName, StringComparer.OrdinalIgnoreCase).ToList();
        output.WriteLine(models
            ? "Id\tInternalName\tCategory\tRooms\tModels\tName"
            : "Id\tInternalName\tCategory\tRooms\tAssets\tName");
        foreach (var e in rows)
        {
            string count = models ? SwtorAreaCatalog.CountModels(assets, e.Id).ToString() : e.AssetCount.ToString();
            output.WriteLine($"{e.Id}\t{e.InternalName}\t{e.Category}\t{e.RoomCount}\t{count}\t{e.DisplayName}");
        }
        error.WriteLine($"{rows.Count} area(s) of {catalog.Entries.Count} ({(catalog.FromCache ? "cache" : "rebuilt")}, language {catalog.Language}).");
        return 0;
    }
}

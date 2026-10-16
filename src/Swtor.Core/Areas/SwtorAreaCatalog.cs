using System.Text.Json;
using System.Text.Json.Serialization;

namespace Swtor.Core.Areas;

/// <summary>Une zone du catalogue : identifiant numerique (dossier <c>world/areas/&lt;id&gt;</c>), noms et tailles approximatives.</summary>
public sealed record SwtorAreaEntry
{
    /// <summary>Identifiant numerique (nom du dossier).</summary>
    public required string Id { get; init; }

    /// <summary>Nom interne lu dans area.dat ("war_planworking") ; vide si absent.</summary>
    public string InternalName { get; init; } = "";

    /// <summary>Nom lisible (localise si trouve, sinon deduit du nom interne).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Origine du nom : "gom" (mapAreasDataProto + chaines), "gom-map" (pages de carte), "internal" (nom interne mis en forme), "id".</summary>
    public required string NameSource { get; init; }

    /// <summary>planet, story, ship, flashpoint, operation, stronghold, pvp, space, fleet, daily, event, uprising, system ou other.</summary>
    public required string Category { get; init; }

    /// <summary>Code planete : prefixe du dossier d'art dominant ("ord" pour ord_ordmantell), vide si aucun.</summary>
    public string Planet { get; init; } = "";

    /// <summary>Dossier d'art dominant sous art/static/area (hors all_all), vide si aucun.</summary>
    public string ArtFolder { get; init; } = "";

    public int RoomCount { get; init; }

    /// <summary>Taille de la table d'assets d'area.dat (types d'objets distincts, indicateur de taille de la zone).</summary>
    public int AssetCount { get; init; }

    /// <summary>Nombre de modeles gr2 distincts dans la table d'assets.</summary>
    public int ModelAssetCount { get; init; }

    /// <summary>Taille totale des fichiers room .dat en octets (proportionnelle au nombre d'objets).</summary>
    public long RoomBytes { get; init; }

    /// <summary>Nombre de pages de carte dans le GOM.</summary>
    public int MapPages { get; init; }

    /// <summary>Nombre de fichiers minimap map_*_r.dds.</summary>
    public int Minimaps { get; init; }
}

/// <summary>
/// Catalogue des zones SWTOR : identifiant numerique, nom interne, nom lisible, categorie. Calcule en quelques secondes
/// (ouverture du GOM + lecture des 267 area.dat) puis mis en cache dans un fichier JSON ; relu en quelques millisecondes.
/// </summary>
public sealed class SwtorAreaCatalog
{
    /// <summary>Change quand le format du cache ou l'algorithme de nommage change : le cache est alors reconstruit.</summary>
    public const int CacheVersion = 1;
    public const string CacheFileName = "swtor_areas.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public IReadOnlyList<SwtorAreaEntry> Entries { get; }

    /// <summary>Langue des noms (dossier de chaines utilise).</summary>
    public string Language { get; }

    /// <summary>Vrai si le catalogue vient du cache JSON.</summary>
    public bool FromCache { get; private set; }

    public SwtorAreaCatalog(IReadOnlyList<SwtorAreaEntry> entries, string language)
    {
        Entries = entries;
        Language = language;
    }

    /// <summary>Chemin du cache dans un dossier de travail.</summary>
    public static string CachePath(string workDir) => Path.Combine(workDir, CacheFileName);

    /// <summary>
    /// Charge le catalogue depuis <paramref name="cachePath"/> s'il est valide (meme version, memes ressources, meme nombre de zones),
    /// sinon le reconstruit et ecrit le cache. <paramref name="rebuild"/> force la reconstruction.
    /// </summary>
    public static SwtorAreaCatalog Load(SwtorAssets assets, string? cachePath, bool rebuild = false, string language = "fr-fr")
    {
        string areasDir = Path.Combine(assets.Root, "world", "areas");
        int folderCount = Directory.Exists(areasDir) ? Directory.GetDirectories(areasDir).Length : 0;
        if (!rebuild && cachePath is not null && TryReadCache(cachePath, assets.Root, language, folderCount) is { } cached)
        {
            cached.FromCache = true;
            return cached;
        }
        var catalog = Build(assets, language);
        if (cachePath is not null) WriteCache(cachePath, assets.Root, folderCount, catalog);
        return catalog;
    }

    /// <summary>Reconstruit le catalogue sans cache. Les sources de nom sont decrites dans <see cref="SwtorAreaEntry.NameSource"/>.</summary>
    public static SwtorAreaCatalog Build(SwtorAssets assets, string language = "fr-fr")
    {
        string areasDir = Path.Combine(assets.Root, "world", "areas");
        var strings = new StringResolver(assets.Root, language);
        Dictionary<ulong, GomAreaName> gom;
        try { gom = SwtorAreaNames.ReadFromGom(assets.Root); }
        catch (Exception ex) when (ex is IOException or GameFormatException or UnauthorizedAccessException) { gom = []; }

        var entries = new List<SwtorAreaEntry>();
        if (Directory.Exists(areasDir))
        {
            foreach (string dir in Directory.GetDirectories(areasDir).OrderBy(d => d, StringComparer.Ordinal))
            {
                string datPath = Path.Combine(dir, "area.dat");
                if (!File.Exists(datPath)) continue;
                string id = Path.GetFileName(dir);
                AreaDat? dat;
                try { dat = AreaDatReader.Read(datPath); }
                catch (Exception ex) when (ex is GameFormatException or IOException) { dat = null; }
                entries.Add(MakeEntry(id, dir, dat, ulong.TryParse(id, out ulong num) && gom.TryGetValue(num, out var g) ? g : null, strings));
            }
        }
        return new SwtorAreaCatalog(entries, strings.Language);
    }

    private static SwtorAreaEntry MakeEntry(string id, string dir, AreaDat? dat, GomAreaName? gom, StringResolver strings)
    {
        string internalName = dat?.InternalName ?? "";
        string display;
        string source;
        string? fromTable = gom is { TextId: not 0 } ? strings.Get(gom.TextId) : null;
        if (!string.IsNullOrWhiteSpace(fromTable))
        {
            display = SwtorAreaNames.CleanTitle(fromTable);
            source = "gom";
        }
        else if (gom is not null && SwtorAreaNames.PickFromPages(gom.Pages, internalName, strings.Get) is { Length: > 0 } page)
        {
            display = page;
            source = "gom-map";
        }
        else if (internalName.Length > 0)
        {
            display = Prettify(internalName);
            source = "internal";
        }
        else
        {
            display = id;
            source = "id";
        }

        // Dossier d'art dominant et nombre de modeles distincts.
        var folders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int models = 0;
        if (dat is not null)
        {
            foreach (string path in dat.Assets.Values)
            {
                if (!path.EndsWith(".gr2", StringComparison.OrdinalIgnoreCase)) continue;
                models++;
                string[] parts = path.Split('\\', '/');
                // "", "art", "static", "area", "<dossier>", ...
                if (parts.Length > 4 && parts[1].Equals("art", StringComparison.OrdinalIgnoreCase) && parts[2].Equals("static", StringComparison.OrdinalIgnoreCase)
                    && parts[3].Equals("area", StringComparison.OrdinalIgnoreCase))
                {
                    string folder = parts[4];
                    if (!folder.Equals("all_all", StringComparison.OrdinalIgnoreCase)) folders[folder] = folders.GetValueOrDefault(folder) + 1;
                }
            }
        }
        string artFolder = folders.Count == 0 ? "" : folders.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First().Key;
        string planet = artFolder.Contains('_') && !artFolder.StartsWith("spc_", StringComparison.OrdinalIgnoreCase) ? artFolder[..artFolder.IndexOf('_')] : "";

        long roomBytes = 0;
        foreach (string room in dat?.Rooms ?? [])
        {
            var fi = new FileInfo(Path.Combine(dir, room + ".dat"));
            if (fi.Exists) roomBytes += fi.Length;
        }
        int minimaps = Directory.GetFiles(dir, "map_*_r.dds").Length;

        return new SwtorAreaEntry
        {
            Id = id,
            InternalName = internalName,
            DisplayName = display,
            NameSource = source,
            Category = Classify(internalName, artFolder),
            Planet = planet,
            ArtFolder = artFolder,
            RoomCount = dat?.Rooms.Count ?? 0,
            AssetCount = dat?.Assets.Count ?? 0,
            ModelAssetCount = models,
            RoomBytes = roomBytes,
            MapPages = gom?.Pages.Count ?? 0,
            Minimaps = minimaps,
        };
    }

    /// <summary>"war_planworking" donne "War planworking" (repli quand aucun nom localise n'existe).</summary>
    internal static string Prettify(string internalName)
    {
        string t = internalName.Replace('_', ' ').Trim();
        return t.Length == 0 ? internalName : char.ToUpperInvariant(t[0]) + t[1..];
    }

    private static readonly string[] StoryPrefixes = ["war_", "inq_", "trp_", "smg_", "smug_", "bh_", "agt_", "agnt_", "ia_", "kgt_", "con_", "imp_revan_", "rep_revan_"];
    private static readonly string[] SystemNames = ["lobby", "char_create", "galaxymap", "locksystem", "housing_gallery", "balance_mindscape", "tybere", "endless_swamp", "dreamscape"];

    /// <summary>Categorie deduite du nom interne (prefixes de conventions du jeu) puis du dossier d'art. Heuristique.</summary>
    internal static string Classify(string internalName, string artFolder)
    {
        string n = internalName.ToLowerInvariant();
        if (n.Length == 0) return "other";
        if (n.StartsWith("stronghold_", StringComparison.Ordinal)) return "stronghold";
        if (n.StartsWith("regression_", StringComparison.Ordinal) || n.StartsWith("tst_", StringComparison.Ordinal) || n.StartsWith("combat_test", StringComparison.Ordinal)
            || n.StartsWith("gameplay_persistence", StringComparison.Ordinal) || SystemNames.Contains(n)) return "system";
        if (n.StartsWith("spc_ps_", StringComparison.Ordinal) || n.EndsWith("_ship", StringComparison.Ordinal) || n.Contains("repship", StringComparison.Ordinal)) return "ship";
        if (n.StartsWith("spvp_", StringComparison.Ordinal) || n.StartsWith("spc_", StringComparison.Ordinal) || n.StartsWith("daily_spc_", StringComparison.Ordinal)) return "space";
        if (n.StartsWith("pvp_", StringComparison.Ordinal) || n.Contains("_pvp", StringComparison.Ordinal)) return "pvp";
        if (n.Contains("flashpoint", StringComparison.Ordinal)) return "flashpoint";
        if (n.StartsWith("raid_", StringComparison.Ordinal)) return "operation";
        if (n.StartsWith("uprising_", StringComparison.Ordinal)) return "uprising";
        if (n.StartsWith("event_", StringComparison.Ordinal) || n.Contains("_event", StringComparison.Ordinal)) return "event";
        if (n.Contains("fleet", StringComparison.Ordinal)) return "fleet";
        if (n.Contains("daily", StringComparison.Ordinal)) return "daily";
        foreach (string p in StoryPrefixes) if (n.StartsWith(p, StringComparison.Ordinal)) return "story";
        if (n.StartsWith("gravestone", StringComparison.Ordinal) || n.StartsWith("odessen_", StringComparison.Ordinal)) return "story";
        return artFolder.Length > 0 || n.Contains("main", StringComparison.Ordinal) ? "planet" : "other";
    }

    /// <summary>Recherche par identifiant exact ou nom interne exact (insensible a la casse) ; null si absent ou ambigu.</summary>
    public SwtorAreaEntry? Find(string idOrInternalName)
    {
        var byId = Entries.FirstOrDefault(e => e.Id == idOrInternalName);
        if (byId is not null) return byId;
        var byName = Entries.Where(e => string.Equals(e.InternalName, idOrInternalName, StringComparison.OrdinalIgnoreCase)).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    /// <summary>Entrees dont l'id, le nom interne, le nom lisible, la categorie ou la planete contient le texte (insensible a la casse et aux accents).</summary>
    public IEnumerable<SwtorAreaEntry> Filter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Entries;
        string t = Fold(text);
        return Entries.Where(e => Fold(e.Id).Contains(t) || Fold(e.InternalName).Contains(t) || Fold(e.DisplayName).Contains(t)
                                  || Fold(e.Category).Contains(t) || Fold(e.Planet).Contains(t));
    }

    private static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (char c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToLowerInvariant();
    }

    /// <summary>Compte exact des instances de modele (gr2) de la zone, hors room du ciel. Lit tous les room .dat : quelques secondes pour une grande zone.</summary>
    public static int CountModels(SwtorAssets assets, string areaId)
    {
        string? folder = AreaLayout.ResolveFolder(areaId, assets);
        if (folder is null) return 0;
        var layout = AreaLayout.Load(folder);
        string? sky = layout.Area.AreaProperties.Get("skyscene_room");
        return layout.Rooms.Where(r => r.Name != sky).Sum(r => r.Room.Instances.Count(i => i.Kind == InstanceKind.Model));
    }

    // --- cache JSON ---

    private sealed record CacheFile(int Version, string AssetsRoot, string Language, int AreaFolders, DateTime BuiltUtc, List<SwtorAreaEntry> Entries);

    private static SwtorAreaCatalog? TryReadCache(string path, string root, string language, int folderCount)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), JsonOptions);
            if (file is null || file.Version != CacheVersion || file.Entries is null || file.AreaFolders != folderCount) return null;
            if (!string.Equals(file.AssetsRoot, root, StringComparison.OrdinalIgnoreCase)) return null;
            // La langue effectivement utilisee peut differer de la demandee (repli) : on accepte le cache tant que la demande est la meme.
            if (!string.Equals(file.Language, language, StringComparison.OrdinalIgnoreCase) && !string.Equals(file.Language, new StringResolver(root, language).Language, StringComparison.OrdinalIgnoreCase)) return null;
            return new SwtorAreaCatalog(file.Entries, file.Language);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteCache(string path, string root, int folderCount, SwtorAreaCatalog catalog)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var file = new CacheFile(CacheVersion, root, catalog.Language, folderCount, DateTime.UtcNow, [.. catalog.Entries]);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOptions));
            File.Move(tmp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache non ecrit : le catalogue reste utilisable, il sera simplement reconstruit la prochaine fois.
        }
    }
}

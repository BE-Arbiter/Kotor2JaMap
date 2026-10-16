using Swtor.Core.Gom;
using Swtor.Core.Stb;

namespace Swtor.Core.Areas;

/// <summary>Nom d'une zone tel que le donne le GOM : identifiant de chaine localisee et nom de page de carte.</summary>
public sealed record GomAreaName(ulong AreaId, long TextId, string? ChatKey, IReadOnlyList<GomMapPage> Pages);

/// <summary>Une page de carte d'une zone (objet <c>world.areas.&lt;id&gt;.mapdata</c>) : nom de minimap et chaine localisee.</summary>
public sealed record GomMapPage(string MapName, long TextId);

/// <summary>
/// Lit dans le GOM les noms des zones : l'objet <c>mapAreasDataProto</c> (id de zone vers identifiant de chaine) et les objets
/// <c>world.areas.&lt;id&gt;.mapdata</c> (pages de carte). Les identifiants de champ sont des hachages du nom de champ.
/// </summary>
public static class SwtorAreaNames
{
    private const string AreasTableName = "mapAreasDataProto";
    private const ulong FieldTextId = 0x400000080027BF7FUL;
    private const ulong FieldChatKey = 0x400000085CD72A38UL;
    private const ulong FieldPages = 0x40000005B5F9C772UL;
    private const ulong FieldPageName = 0x400000006225006BUL;
    private const ulong FieldPageText = 0x40000000858FC65EUL;
    private const string MapDataSuffix = ".mapdata";
    private const string MapDataPrefix = "world.areas.";

    /// <summary>Ouvre le GOM (environ 4 s : entetes de 700 000 objets) et renvoie les noms de zone par identifiant.</summary>
    public static Dictionary<ulong, GomAreaName> ReadFromGom(string assetsRoot)
    {
        var db = GomDatabase.Open(assetsRoot);
        var pages = new Dictionary<ulong, List<GomMapPage>>();
        var table = new Dictionary<ulong, (long TextId, string? ChatKey)>();

        var tableEntry = db.Find(AreasTableName);
        if (tableEntry is not null)
        {
            var node = db.Decode(tableEntry);
            var map = node.Object.Fields.Select(f => f.Value).OfType<GomMap>().FirstOrDefault();
            if (map is not null)
            {
                foreach (var kv in map.Entries)
                {
                    if (kv.Key is not ulong id || kv.Value is not GomObject o) continue;
                    long textId = o.Find(FieldTextId)?.Value is long t ? t : 0;
                    table[id] = (textId, o.Find(FieldChatKey)?.Value as string);
                }
            }
        }

        db.ForEachNode(e => e.Name.StartsWith(MapDataPrefix, StringComparison.Ordinal) && e.Name.EndsWith(MapDataSuffix, StringComparison.Ordinal), (entry, node) =>
        {
            string idText = entry.Name[MapDataPrefix.Length..^MapDataSuffix.Length];
            if (!ulong.TryParse(idText, out ulong id)) return;
            var list = new List<GomMapPage>();
            if (node.Object.Find(FieldPages)?.Value is GomList items)
            {
                foreach (var item in items.Items.OfType<GomObject>())
                {
                    string? name = item.Find(FieldPageName)?.Value as string;
                    long text = item.Find(FieldPageText)?.Value is long l ? l : 0;
                    if (name is not null) list.Add(new GomMapPage(name, text));
                }
            }
            lock (pages) pages[id] = list;
        });

        var result = new Dictionary<ulong, GomAreaName>();
        foreach (ulong id in table.Keys.Union(pages.Keys))
        {
            table.TryGetValue(id, out var row);
            result[id] = new GomAreaName(id, row.TextId, row.ChatKey, pages.TryGetValue(id, out var p) ? p : []);
        }
        return result;
    }

    /// <summary>
    /// Choisit le nom d'affichage de la zone parmi ses pages de carte (quand <c>mapAreasDataProto</c> n'a pas de texte) :
    /// page <c>map_&lt;nom interne&gt;[_main|_world]</c>, sinon l'unique page, sinon le titre le plus frequent.
    /// </summary>
    public static string? PickFromPages(IReadOnlyList<GomMapPage> pages, string? internalName, Func<long, string?> text)
    {
        if (pages.Count == 0) return null;
        if (internalName is not null)
        {
            string[] wanted = [$"map_{internalName}_main", $"map_{internalName}_world", $"map_{internalName}"];
            foreach (string w in wanted)
            {
                var hit = pages.FirstOrDefault(p => string.Equals(p.MapName, w, StringComparison.OrdinalIgnoreCase));
                if (hit is not null && text(hit.TextId) is { } t) return CleanTitle(t);
            }
        }
        var titles = pages.Select(p => text(p.TextId)).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!).ToList();
        if (titles.Count == 0) return null;
        if (titles.Count == 1) return CleanTitle(titles[0]);
        // Le titre racine (avant " : " ou "<br>") le plus frequent represente la zone.
        return titles.GroupBy(Root).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).First().Key;
    }

    private static string Root(string title)
    {
        int br = title.IndexOf("<br>", StringComparison.OrdinalIgnoreCase);
        if (br >= 0) title = title[..br];
        int colon = title.IndexOf(" : ", StringComparison.Ordinal);
        return (colon > 0 ? title[..colon] : title).Trim();
    }

    /// <summary>Remplace les retours a la ligne "&lt;br&gt;" des titres de carte par " - ".</summary>
    public static string CleanTitle(string title) => title.Replace("<br>", " - ", StringComparison.OrdinalIgnoreCase).Trim();
}

/// <summary>
/// Resout un identifiant de chaine (64 bits) dans les tables .stb d'une langue (<c>&lt;racine&gt;/&lt;langue&gt;/str</c>). Les tables utiles
/// (carte, mondes, PvP spatial) sont essayees d'abord, puis les autres tables du dossier racine, <c>sys</c>, <c>gui</c> et <c>spvp</c>.
/// </summary>
public sealed class StringResolver
{
    private readonly List<string> _files = [];
    private readonly List<StringTable> _loaded = [];
    private int _next;

    public string Language { get; }

    public StringResolver(string assetsRoot, string language = "fr-fr")
    {
        string str = Path.Combine(assetsRoot, language, "str");
        if (!Directory.Exists(str))
        {
            foreach (string fallback in new[] { "fr-fr", "en-us" })
            {
                if (Directory.Exists(Path.Combine(assetsRoot, fallback, "str"))) { language = fallback; str = Path.Combine(assetsRoot, fallback, "str"); break; }
            }
        }
        Language = language;
        if (!Directory.Exists(str)) return;
        string[] first = [Path.Combine("sys", "worldmap.stb"), Path.Combine("gui", "map.stb"), Path.Combine("spvp", "misc.stb"), "rdd.stb"];
        foreach (string f in first) if (File.Exists(Path.Combine(str, f))) _files.Add(Path.Combine(str, f));
        foreach (string dir in new[] { "", "sys", "gui", "spvp", "misc" })
        {
            string d = Path.Combine(str, dir);
            if (!Directory.Exists(d)) continue;
            foreach (string f in Directory.GetFiles(d, "*.stb").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                if (!_files.Contains(f)) _files.Add(f);
        }
    }

    /// <summary>Texte de la chaine, ou null si aucune table ne la contient.</summary>
    public string? Get(long id)
    {
        if (id == 0) return null;
        foreach (var t in _loaded) if (t.Get(id) is { } s) return s;
        while (_next < _files.Count)
        {
            StringTable table;
            try { table = StringTable.Parse(File.ReadAllBytes(_files[_next++])); }
            catch (Exception ex) when (ex is GameFormatException or IOException) { continue; }
            _loaded.Add(table);
            if (table.Get(id) is { } s) return s;
        }
        return null;
    }
}

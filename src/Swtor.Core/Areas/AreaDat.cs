using System.Globalization;
using System.Text;

namespace Swtor.Core.Areas;

/// <summary>Proprietes d'ambiance (ciel, brouillard, lumiere statique...) lues dans la chaine "~0|N|..." d'area.dat.</summary>
public sealed record AreaProperties(IReadOnlyDictionary<string, string> Values)
{
    public static readonly AreaProperties Empty = new(new Dictionary<string, string>());

    public string? Get(string key) => Values.TryGetValue(key, out string? v) ? v : null;

    /// <summary>Couleur "#r,g,b,a" (flottants 0..1) ou null.</summary>
    public (float R, float G, float B, float A)? GetColor(string key)
    {
        string? v = Get(key);
        if (v is null || !v.StartsWith('#')) return null;
        string[] p = v[1..].Split(',');
        if (p.Length < 3) return null;
        float F(int i) => i < p.Length && float.TryParse(p[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 1f;
        return (F(0), F(1), F(2), F(3));
    }

    public float? GetFloat(string key) =>
        float.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : null;

    public bool? GetBool(string key) => Get(key) switch { "true" => true, "false" => false, _ => null };

    /// <summary>
    /// Decode "~0|35|15|default_minimap|0||23|..." : version, nombre de proprietes, puis pour chacune
    /// nom et valeur precedes de leur longueur en caracteres.
    /// </summary>
    public static AreaProperties Parse(string text)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        int pos = 0;
        string? Token()
        {
            if (pos > text.Length) return null;
            int end = text.IndexOf('|', pos);
            if (end < 0) return null;
            string t = text[pos..end];
            pos = end + 1;
            return t;
        }
        string? Sized()
        {
            string? lenTok = Token();
            if (lenTok is null || !int.TryParse(lenTok, out int len) || len < 0 || pos + len > text.Length) return null;
            string s = text.Substring(pos, len);
            pos += len;
            if (pos < text.Length && text[pos] == '|') pos++;
            return s;
        }
        if (Token() is not { } ver || !ver.StartsWith('~')) return Empty;
        if (Token() is not { } countTok || !int.TryParse(countTok, out int count)) return Empty;
        for (int i = 0; i < count; i++)
        {
            string? name = Sized();
            string? value = Sized();
            if (name is null || value is null) break;
            dict[name] = value;
        }
        return new AreaProperties(dict);
    }
}

/// <summary>Point d'apparition ("arrival_sith_warrior"...) trouve dans la section 3 d'area.dat (lecture heuristique, voir swtor-formats.md).</summary>
public sealed record AreaMarker(ulong Id, string Name, int Offset);

/// <summary>Contenu d'un area.dat ("AREA_DAT_BINARY_FORMAT_").</summary>
public sealed class AreaDat
{
    /// <summary>Noms des rooms : chaque nom correspond a un fichier "nom.dat" dans le dossier de la zone.</summary>
    public required IReadOnlyList<string> Rooms { get; init; }

    /// <summary>Table des assets : identifiant 64 bits vers chemin (gr2 "\art\static\...", classes moteur "\engine\light.lit", etc.).</summary>
    public required IReadOnlyDictionary<ulong, string> Assets { get; init; }

    /// <summary>Proprietes par nom ("Area" = zone entiere, puis une entree par room ayant des reglages propres).</summary>
    public required IReadOnlyDictionary<string, AreaProperties> Properties { get; init; }

    /// <summary>Marqueurs d'arrivee (heuristique).</summary>
    public required IReadOnlyList<AreaMarker> Markers { get; init; }

    /// <summary>Offsets des 9 sections du fichier (pour diagnostic).</summary>
    public required IReadOnlyList<uint> SectionOffsets { get; init; }

    public AreaProperties AreaProperties => Properties.TryGetValue("Area", out var p) ? p : AreaProperties.Empty;

    public string? ResolveAsset(ulong id) => Assets.TryGetValue(id, out string? s) ? s : null;
}

public static class AreaDatReader
{
    public const string Magic = "AREA_DAT_BINARY_FORMAT_";

    public static AreaDat Read(string path) => Read(File.ReadAllBytes(path));

    public static AreaDat Read(byte[] data)
    {
        var r = new ByteCursor(data);
        string magic = r.LengthPrefixedAscii();
        if (magic != Magic) throw new GameFormatException($"Entete area.dat inattendu '{magic}'", 0);
        var sections = new uint[9];
        for (int i = 0; i < sections.Length; i++) sections[i] = r.U32();

        r.Seek(sections[0]);
        var rooms = new List<string>();
        int roomCount = r.Count(data.Length);
        for (int i = 0; i < roomCount; i++) rooms.Add(r.LengthPrefixedAscii());

        r.Seek(sections[1]);
        int assetCount = r.Count(data.Length);
        var assets = new Dictionary<ulong, string>(assetCount);
        for (int i = 0; i < assetCount; i++)
        {
            ulong id = r.U64();
            assets[id] = r.LengthPrefixedAscii();
        }

        var markers = ScanMarkers(data, (int)sections[2], (int)sections[3]);

        var props = new Dictionary<string, AreaProperties>(StringComparer.Ordinal);
        r.Seek(sections[3]);
        int propCount = r.Count(data.Length);
        for (int i = 0; i < propCount; i++)
        {
            string key = r.LengthPrefixedAscii();
            string value = r.LengthPrefixedAscii();
            props[key] = AreaProperties.Parse(value);
        }

        return new AreaDat
        {
            Rooms = rooms,
            Assets = assets,
            Properties = props,
            Markers = markers,
            SectionOffsets = sections,
        };
    }

    /// <summary>
    /// La section 3 (objets de zone : points d'arrivee...) n'est pas decodee champ par champ ; on cherche les motifs
    /// [id 64 bits 0x40000006xxxxxxxx][longueur][nom ASCII imprimable][0].
    /// </summary>
    private static List<AreaMarker> ScanMarkers(byte[] data, int start, int end)
    {
        var list = new List<AreaMarker>();
        if (start < 0 || end > data.Length || end <= start) return list;
        for (int p = start + 4; p + 12 < end; p++)
        {
            ulong id = BitConverter.ToUInt64(data, p);
            if ((id >> 32) != 0x40000006UL) continue;
            int len = BitConverter.ToInt32(data, p + 8);
            if (len < 3 || len > 80 || p + 12 + len > end || data[p + 12 + len - 1] != 0) continue;
            bool printable = true;
            for (int i = 0; i < len - 1; i++) if (data[p + 12 + i] < 0x20 || data[p + 12 + i] > 0x7E) { printable = false; break; }
            if (!printable) continue;
            list.Add(new AreaMarker(id, Encoding.ASCII.GetString(data, p + 12, len - 1), p));
            p += 12 + len - 1;
        }
        return list;
    }
}

using Kotor.Core.Archives;
using Kotor.Core.Gff;
using Kotor.Core.TwoDa;

namespace Kotor.Core.Entities;

/// <summary>
/// Retrouve le modele d'un objet place ou d'une porte : template (UTP/UTD) -> apparence -> 2DA -> colonne du modele.
/// Placeable : UTP.Appearance -> placeables.2da, colonne <c>modelname</c>.
/// Porte : UTD.GenericType -> genericdoors.2da (<c>modelname</c>) ; a defaut UTD.Appearance -> doortypes.2da (<c>model</c>)
/// (colonnes verifiees sur les vrais 2DA de KOTOR 1).
/// </summary>
public sealed class ObjectModelResolver(ResourceResolver res)
{
    private readonly Dictionary<string, TwoDaFile?> _tables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resref du modele (minuscules) d'un objet place, ou null avec la raison (en anglais).</summary>
    public string? ResolvePlaceable(string template, out string? reason) =>
        Resolve(template, ResourceType.Utp, "UTP", [("Appearance", "placeables", "modelname")], out reason);

    /// <summary>Resref du modele (minuscules) d'une porte, ou null avec la raison (en anglais).</summary>
    public string? ResolveDoor(string template, out string? reason) =>
        Resolve(template, ResourceType.Utd, "UTD", [("GenericType", "genericdoors", "modelname"), ("Appearance", "doortypes", "model")], out reason);

    /// <summary>Vrai si l'UTD de la porte a <c>Locked</c> = 1 (faux si l'UTD manque ou est illisible).</summary>
    public bool IsDoorLocked(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return false;
        byte[]? data = res.Find(template, ResourceType.Utd);
        if (data is null) return false;
        try { return Integer(GffReader.Read(data, out _), "Locked") is { } v && v != 0; }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException
            or EndOfStreamException or IndexOutOfRangeException)
        { return false; }
    }

    private string? Resolve(string template, ResourceType type, string kind, (string Field, string Table, string Column)[] steps, out string? reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(template)) { reason = "empty TemplateResRef"; return null; }
        byte[]? data = res.Find(template, type);
        if (data is null) { reason = $"{kind} '{template}' not found"; return null; }
        GffStruct gff;
        try { gff = GffReader.Read(data, out _); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException
            or EndOfStreamException or IndexOutOfRangeException)
        { reason = $"{kind} '{template}' unreadable ({ex.Message})"; return null; }

        string lastDetail = "";
        foreach (var (field, table, column) in steps)
        {
            long? row = Integer(gff, field);
            if (row is null) { lastDetail = $"{kind} '{template}' has no {field}"; continue; }
            TwoDaFile? t = Table(table);
            if (t is null) { reason = $"{table}.2da not found or unreadable"; return null; }
            string? cell = row < 0 || row >= t.RowCount ? null : t.Get((int)row, column);
            if (cell is null) { lastDetail = $"{field} {row} of {kind} '{template}' is outside {table}.2da ({t.RowCount} rows)"; continue; }
            cell = cell.Trim();
            if (cell.Length == 0 || cell.StartsWith("***", StringComparison.Ordinal))
            { lastDetail = $"{table}.2da row {row} has no {column}"; continue; }
            return cell.ToLowerInvariant();
        }
        reason = lastDetail.Length > 0 ? lastDetail : $"{kind} '{template}' gives no model";
        return null;
    }

    private TwoDaFile? Table(string name)
    {
        if (_tables.TryGetValue(name, out TwoDaFile? t)) return t;
        byte[]? data = res.Find(name, ResourceType.TwoDa);
        try { t = data is null ? null : TwoDaFile.Read(data); }
        catch (InvalidDataException) { t = null; }
        _tables[name] = t;
        return t;
    }

    private static long? Integer(GffStruct s, string label) =>
        s.Fields.TryGetValue(label, out object? v)
            ? v switch
            {
                byte b => b, sbyte sb => sb, ushort us => us, short sh => sh, uint ui => ui, int i => i, ulong ul => (long)ul, long l => l,
                _ => null,
            }
            : null;
}

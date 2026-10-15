using Kotor.Core.Archives;
using Kotor.Core.Gff;
using Kotor.Core.Modules;
using Kotor.Core.TwoDa;

namespace Kotor.Core.Audio;

/// <summary>Pistes d'une zone (resrefs, minuscules) : <c>MusicDay</c> -> ambientmusic.2da, <c>AmbientSndDay</c> -> ambientsound.2da (colonne <c>resource</c>).</summary>
public sealed record AreaAudio(string? Music, string? Ambient);

/// <summary>Son place (UTS) : sons du premier au dernier, boucle, intervalle (ms) et variation de l'intervalle (ms) des sons ponctuels.</summary>
public sealed record SoundObject(IReadOnlyList<string> Sounds, bool Looping, bool Active, int IntervalMs, int IntervalVariationMs);

/// <summary>Sons de porte (resrefs) : ouverture et fermeture, null si la porte n'en a pas.</summary>
public sealed record DoorSounds(string? Open, string? Close);

/// <summary>
/// Retrouve les sons d'un module. Chaines verifiees sur KOTOR 1 :
/// zone : GIT <c>AreaProperties</c> (<c>MusicDay</c>, <c>AmbientSndDay</c>) -> <c>ambientmusic.2da</c> / <c>ambientsound.2da</c> (<c>resource</c>) ;
/// son place : GIT <c>SoundList</c> -> UTS (<c>Active</c>, <c>Looping</c>, <c>Interval</c>, <c>IntervalVrtn</c>, <c>Sounds[].Sound</c>) ;
/// porte : UTD <c>GenericType</c> -> <c>genericdoors.2da</c> (<c>soundapptype</c>), a defaut <c>Appearance</c> -> <c>doortypes.2da</c>,
/// puis ligne de <c>placeableobjsnds.2da</c> (<c>opened</c>, <c>closed</c>).
/// </summary>
public sealed class AudioCatalog(ResourceResolver res)
{
    private readonly Dictionary<string, TwoDaFile?> _tables = new(StringComparer.OrdinalIgnoreCase);

    public AreaAudio ReadArea(ModuleInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        GffStruct? git = ReadGff(info.AreaResRef, ResourceType.Git);
        if (git is null || git.Fields.GetValueOrDefault("AreaProperties") is not GffStruct props) return new AreaAudio(null, null);
        return new AreaAudio(Row("ambientmusic", Integer(props, "MusicDay"), "resource"), Row("ambientsound", Integer(props, "AmbientSndDay"), "resource"));
    }

    public SoundObject? ReadSoundObject(string template)
    {
        GffStruct? uts = string.IsNullOrWhiteSpace(template) ? null : ReadGff(template, ResourceType.Uts);
        if (uts is null) return null;
        var sounds = uts.GetList("Sounds").Select(s => (s.Get<string>("Sound") ?? "").Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        return new SoundObject(sounds, Integer(uts, "Looping") is > 0, Integer(uts, "Active") is not 0,
            (int)(Integer(uts, "Interval") ?? 0), (int)(Integer(uts, "IntervalVrtn") ?? 0));
    }

    public DoorSounds ReadDoorSounds(string template)
    {
        GffStruct? utd = string.IsNullOrWhiteSpace(template) ? null : ReadGff(template, ResourceType.Utd);
        if (utd is null) return new DoorSounds(null, null);
        long? type = Integer(utd, "GenericType") is { } g ? Parse(Row("genericdoors", g, "soundapptype")) : null;
        type ??= Integer(utd, "Appearance") is { } a ? Parse(Row("doortypes", a, "soundapptype")) : null;
        return type is null ? new DoorSounds(null, null) : new DoorSounds(Row("placeableobjsnds", type, "opened"), Row("placeableobjsnds", type, "closed"));
    }

    private static long? Parse(string? cell) => long.TryParse(cell, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long v) ? v : null;

    /// <summary>Cellule (minuscules) ; null si ligne hors table, colonne absente ou cellule vide (<c>***</c> compris).</summary>
    private string? Row(string table, long? row, string column)
    {
        if (row is null) return null;
        TwoDaFile? t = Table(table);
        if (t is null || row < 0 || row >= t.RowCount) return null;
        string? cell = t.Get((int)row, column)?.Trim();
        return string.IsNullOrEmpty(cell) || cell.StartsWith("***", StringComparison.Ordinal) ? null : cell.ToLowerInvariant();
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

    private GffStruct? ReadGff(string resRef, ResourceType type)
    {
        byte[]? data = res.Find(resRef, type);
        if (data is null) return null;
        try { return GffReader.Read(data, out _); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or EndOfStreamException or IndexOutOfRangeException)
        { return null; }
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

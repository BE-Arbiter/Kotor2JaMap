namespace Kotor.Core.Gff;

/// <summary>Structure GFF : libelle -> valeur (voir <see cref="GffReader"/> pour la correspondance des types).</summary>
public sealed class GffStruct
{
    private static readonly IReadOnlyList<GffStruct> NoStructs = Array.Empty<GffStruct>();

    public uint Type { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }

    public GffStruct(uint type, IReadOnlyDictionary<string, object?> fields)
    {
        Type = type;
        Fields = fields;
    }

    /// <summary>Valeur typee, ou default si le libelle est absent ou d'un autre type.</summary>
    public T? Get<T>(string label) =>
        Fields.TryGetValue(label, out var v) && v is T t ? t : default;

    /// <summary>Liste de structures, vide si absente.</summary>
    public IReadOnlyList<GffStruct> GetList(string label) =>
        Fields.TryGetValue(label, out var v) && v is IReadOnlyList<GffStruct> l ? l : NoStructs;
}

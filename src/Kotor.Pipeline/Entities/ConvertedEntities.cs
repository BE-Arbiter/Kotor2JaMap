using System.Numerics;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Entities;

/// <summary>Un MD3 a ecrire dans le pk3. <c>Path</c> : <c>models/&lt;dossier&gt;/&lt;modele&gt;.md3</c>. <c>Textures</c> : noms des textures de ses shaders.</summary>
public sealed record Md3Asset(string Path, byte[] Data, IReadOnlyList<string> Textures);

/// <summary>
/// Entites JA produites par la conversion des objets (unites JA), leurs MD3 et leur collision.
/// <see cref="TextureData"/> : texture de modele (nom) -> image decodee (null : introuvable, damier) ;
/// <see cref="ModelShaders"/> : blocs <c>models/&lt;dossier&gt;/&lt;texture&gt;</c> a ajouter au fichier de shaders de la carte.
/// </summary>
public sealed record ConvertedEntities(IReadOnlyList<IReadOnlyDictionary<string, string>> Entities, IReadOnlyList<Md3Asset> Models,
    IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> ExtraSolid /* unites JA */)
{
    public IReadOnlyDictionary<string, KotorTexture?> TextureData { get; init; } = new Dictionary<string, KotorTexture?>();
    public IReadOnlyList<string> ModelShaders { get; init; } = [];

    public static ConvertedEntities Empty { get; } = new([], [], []);

    /// <summary>
    /// Reunit deux resultats (objets puis portes). Un MD3, une texture ou un bloc shader present des deux cotes n'est garde qu'une fois.
    /// Si <paramref name="other"/> est vide, renvoie <c>this</c> (le resultat reste identique a celui d'avant les portes).
    /// </summary>
    public ConvertedEntities Merge(ConvertedEntities other)
    {
        if (other.Entities.Count == 0 && other.Models.Count == 0 && other.ExtraSolid.Count == 0 && other.TextureData.Count == 0) return this;
        var models = Models.Concat(other.Models).GroupBy(m => m.Path, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(m => m.Path, StringComparer.Ordinal).ToList();
        var textures = new SortedDictionary<string, KotorTexture?>(StringComparer.Ordinal);
        foreach (var kv in TextureData.Concat(other.TextureData)) textures.TryAdd(kv.Key, kv.Value);
        var shaders = ModelShaders.Concat(other.ModelShaders).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        return new ConvertedEntities([.. Entities, .. other.Entities], models, [.. ExtraSolid, .. other.ExtraSolid])
        {
            TextureData = textures,
            ModelShaders = shaders,
        };
    }
}

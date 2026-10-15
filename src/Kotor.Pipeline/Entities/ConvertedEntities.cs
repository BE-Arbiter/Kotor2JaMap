using System.Numerics;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Entities;

/// <summary>Un MD3 a ecrire dans le pk3. <c>Path</c> : <c>models/&lt;dossier&gt;/&lt;modele&gt;.md3</c>. <c>Textures</c> : noms des textures de ses shaders.</summary>
public sealed record Md3Asset(string Path, byte[] Data, IReadOnlyList<string> Textures);

/// <summary>Un son ou une musique a ecrire dans le pk3. <c>Path</c> : <c>sound/&lt;dossier&gt;/&lt;nom&gt;.mp3|wav</c> ou <c>music/&lt;dossier&gt;/&lt;nom&gt;.mp3</c>.</summary>
public sealed record SoundAsset(string Path, byte[] Data);

/// <summary>
/// Entites JA produites par la conversion des objets (unites JA), leurs MD3 et leur collision.
/// <see cref="TextureData"/> : texture de modele (nom) -> image decodee (null : introuvable, damier) ;
/// <see cref="ModelShaders"/> : blocs <c>models/&lt;dossier&gt;/&lt;texture&gt;</c> a ajouter au fichier de shaders de la carte ;
/// <see cref="Sounds"/> : fichiers audio a ecrire ; <see cref="WorldKeys"/> : cles a ajouter a l'entite <c>worldspawn</c> (<c>music</c>).
/// </summary>
public sealed record ConvertedEntities(IReadOnlyList<IReadOnlyDictionary<string, string>> Entities, IReadOnlyList<Md3Asset> Models,
    IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> ExtraSolid /* unites JA */)
{
    public IReadOnlyDictionary<string, KotorTexture?> TextureData { get; init; } = new Dictionary<string, KotorTexture?>();
    public IReadOnlyList<string> ModelShaders { get; init; } = [];
    public IReadOnlyList<SoundAsset> Sounds { get; init; } = [];
    public IReadOnlyDictionary<string, string> WorldKeys { get; init; } = new Dictionary<string, string>();

    public static ConvertedEntities Empty { get; } = new([], [], []);

    /// <summary>
    /// Reunit deux resultats (objets puis portes). Un MD3, une texture, un son ou un bloc shader present des deux cotes n'est garde qu'une fois ;
    /// une cle de <see cref="WorldKeys"/> deja donnee n'est pas remplacee.
    /// Si <paramref name="other"/> est vide, renvoie <c>this</c> (le resultat reste identique a celui d'avant les portes).
    /// </summary>
    public ConvertedEntities Merge(ConvertedEntities other)
    {
        if (other.Entities.Count == 0 && other.Models.Count == 0 && other.ExtraSolid.Count == 0 && other.TextureData.Count == 0
            && other.Sounds.Count == 0 && other.WorldKeys.Count == 0) return this;
        var models = Models.Concat(other.Models).GroupBy(m => m.Path, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(m => m.Path, StringComparer.Ordinal).ToList();
        var textures = new SortedDictionary<string, KotorTexture?>(StringComparer.Ordinal);
        // Une texture introuvable d'un cote (null) ne masque pas la meme texture trouvee de l'autre (parties d'un assemblage).
        foreach (var kv in TextureData.Concat(other.TextureData))
            if (!textures.TryAdd(kv.Key, kv.Value) && textures[kv.Key] is null && kv.Value is not null) textures[kv.Key] = kv.Value;
        var shaders = ModelShaders.Concat(other.ModelShaders).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var sounds = Sounds.Concat(other.Sounds).GroupBy(s => s.Path, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(s => s.Path, StringComparer.Ordinal).ToList();
        var world = new Dictionary<string, string>(WorldKeys, StringComparer.Ordinal);
        foreach (var kv in other.WorldKeys) world.TryAdd(kv.Key, kv.Value);
        return new ConvertedEntities([.. Entities, .. other.Entities], models, [.. ExtraSolid, .. other.ExtraSolid])
        {
            TextureData = textures,
            ModelShaders = shaders,
            Sounds = sounds,
            WorldKeys = world,
        };
    }
}

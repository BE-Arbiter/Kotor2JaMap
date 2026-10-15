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
}

// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler;

/// <summary>Surface de rendu fournie au compilateur, en unités JA. <c>Uv1</c> = UV de lightmap (optionnels). <c>Room</c> : indice dans <see cref="CompileInput.Rooms"/> (-1 = aucune piece).</summary>
public sealed record SurfaceInput(string Shader, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1,
    int[] Indices, bool DoubleSided = false, int Room = -1)
{
    /// <summary>Alpha des couleurs de sommet (0 a 255, meme indice que <see cref="Positions"/>) lu par <c>alphaGen vertex</c> ; null = 255.</summary>
    public byte[]? VertexAlpha { get; init; }

    /// <summary>
    /// Texture de base de la surface (chemin sans extension, ex. <c>textures/jk/01wbolts</c>), celle que l'editeur de cartes sait afficher :
    /// le shader du jeu peut etre un shader a lightmap (<c>..__lm01</c>) qu'un editeur ne resout pas. Null : <see cref="Shader"/>.
    /// Uniquement lu par le <c>.map</c> d'edition, jamais par le BSP.
    /// </summary>
    public string? VisualTexture { get; init; }

    /// <summary>Taille en pixels de <see cref="VisualTexture"/> (les UV sont en fraction de texture, le <c>.map</c> en pixels). Defaut 128 x 128.</summary>
    public Vector2 VisualTextureSize { get; init; } = new(128f, 128f);
}

/// <summary>Scène complète en mémoire, en unités JA. <c>Entities</c>[0] est le worldspawn.</summary>
public sealed record CompileInput(string MapName, IReadOnlyList<IReadOnlyDictionary<string, string>> Entities,
    IReadOnlyList<SurfaceInput> Surfaces, ModelMesh Solid, ModelMesh Walkable, string SkyShader, VertexLighting Lighting,
    IReadOnlyList<PointLight>? Lights = null)
{
    /// <summary>Sous-modeles BSP (portes) : le i-eme devient <c>*{i+1}</c>. Vide par defaut : le BSP est alors identique a celui d'avant.</summary>
    public IReadOnlyList<SubModelInput> SubModels { get; init; } = [];

    /// <summary>Surfaces de rendu spatialement compactes (tri de Morton avant decoupage). Faux par defaut : sortie KOTOR inchangee.</summary>
    public bool CompactSurfaces { get; init; }

    /// <summary>Visibilite entre pieces (un cluster par piece, voir <see cref="SurfaceInput.Room"/>). Null par defaut : un seul cluster toujours visible (comme q3map2 -novis).</summary>
    public RoomVisibility? Rooms { get; init; }

    /// <summary>Boites ou un spectateur peut se trouver, par piece (unites JA, voir <see cref="Compiler.ViewBox"/>). Une piece sans boite n a pas de cluster : ses surfaces sont toujours visibles (ciel, pieces sans walkmesh). Null : boites deduites des surfaces de chaque piece.</summary>
    public IReadOnlyList<RoomBox>? RoomViewBoxes { get; init; }
}

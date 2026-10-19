using System.Numerics;

namespace Jk.Core.Jkl;

// Modele d'un niveau .jkl. Unites JK, Z vers le haut ; aucune conversion ici.

/// <summary>Section HEADER ; <see cref="Raw"/> garde toutes les lignes (cle en minuscules) pour les champs non modelises.</summary>
public sealed record JklHeader(
    int Version, float Gravity, float CeilingSkyZ, float HorizonDistance, float HorizonPixelsPerRev,
    Vector2 HorizonSkyOffset, Vector2 CeilingSkyOffset, float[] MipMapDistances, float[] LodDistances,
    float PerspectiveDistance, float GouraudDistance, IReadOnlyDictionary<string, string> Raw);

/// <summary>Surface d'un niveau (polygone convexe). <c>Material</c> et <c>Adjoin</c> valent -1 s'ils sont absents ; <c>TexIndices</c> vaut -1 sans coordonnee de texture.</summary>
public sealed record JklSurface(
    int Material, uint SurfaceFlags, uint FaceFlags, int Geo, int Light, int Tex, int Adjoin,
    float ExtraLight, int[] VertexIndices, int[] TexIndices, float[] Intensities, Vector3 Normal);

public sealed record JklAdjoin(uint Flags, int Mirror);

public sealed record JklSector(
    int Index, uint Flags, float AmbientLight, float ExtraLight, int Colormap, Vector3 Tint, string? Sound, float SoundVolume,
    int FirstSurface, int SurfaceCount, Vector3 Thrust);

/// <summary><c>Params</c> : paires <c>cle=valeur</c> de la ligne (cles insensibles a la casse) ; plusieurs <c>frame=</c> sont concatenes, separes par un blanc.</summary>
public sealed record JklThing(
    int Index, string Template, string Name, Vector3 Position, Vector3 PitchYawRoll, int Sector,
    IReadOnlyDictionary<string, string> Params);

public sealed record JklCogInstance(int Index, string CogFile, IReadOnlyList<string> Values);

public sealed record JklTemplate(string Name, string Parent, IReadOnlyDictionary<string, string> Params);

/// <summary>Niveau lu. <c>SkippedSections</c> : noms des sections inconnues, sautees (a journaliser en information).</summary>
public sealed record JklLevel(
    JklHeader Header, IReadOnlyList<string> Materials, IReadOnlyList<string> Colormaps, Vector3[] Vertices, Vector2[] TexVertices,
    IReadOnlyList<JklAdjoin> Adjoins, IReadOnlyList<JklSurface> Surfaces, IReadOnlyList<JklSector> Sectors, IReadOnlyList<JklThing> Things,
    IReadOnlyList<JklCogInstance> Cogs, IReadOnlyDictionary<string, JklTemplate> Templates, IReadOnlyList<string> Models, IReadOnlyList<string> Sounds)
{
    public IReadOnlyList<string> SkippedSections { get; init; } = Array.Empty<string>();
}

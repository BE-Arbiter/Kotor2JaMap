// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using Kotor.Compiler.Bsp;
using Kotor.Core.Geometry;

namespace Kotor.Compiler;

/// <summary>Surface de rendu fournie au compilateur, en unités JA. <c>Uv1</c> = UV de lightmap (optionnels).</summary>
public sealed record SurfaceInput(string Shader, Vector3[] Positions, Vector3[] Normals, Vector2[] Uv0, Vector2[]? Uv1,
    int[] Indices, bool DoubleSided = false);

/// <summary>Scène complète en mémoire, en unités JA. <c>Entities</c>[0] est le worldspawn.</summary>
public sealed record CompileInput(string MapName, IReadOnlyList<IReadOnlyDictionary<string, string>> Entities,
    IReadOnlyList<SurfaceInput> Surfaces, ModelMesh Solid, ModelMesh Walkable, string SkyShader, VertexLighting Lighting,
    IReadOnlyList<PointLight>? Lights = null)
{
    /// <summary>Sous-modeles BSP (portes) : le i-eme devient <c>*{i+1}</c>. Vide par defaut : le BSP est alors identique a celui d'avant.</summary>
    public IReadOnlyList<SubModelInput> SubModels { get; init; } = [];
}

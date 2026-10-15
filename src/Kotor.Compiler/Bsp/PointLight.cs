// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;

namespace Kotor.Compiler.Bsp;

/// <summary>Point light baked into the vertex colours. Position and radius in JA units, colour 0..1.</summary>
public sealed record PointLight(Vector3 Position, Vector3 Color, float Radius, float Multiplier);

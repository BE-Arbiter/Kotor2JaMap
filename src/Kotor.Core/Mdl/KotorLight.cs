using System.Numerics;

namespace Kotor.Core.Mdl;

/// <summary>
/// Light node values, read from the first key of the node controllers (xoreos model_kotor.cpp: kControllerTypeColor = 76,
/// kControllerTypeRadius = 88, kControllerTypeMultiplier = 140; KotorBlender reader.py uses the first key as the static value).
/// <see cref="Defaulted"/>: at least one value was missing and replaced by a default (white, 5 m, x1).
/// </summary>
public sealed record KotorLight(Vector3 Color, float Radius, float Multiplier, bool Defaulted);

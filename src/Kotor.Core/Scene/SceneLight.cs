using System.Numerics;

namespace Kotor.Core.Scene;

/// <summary>Point light of a room model. Position in module space (metres), colour 0..1, radius in metres.</summary>
public sealed record SceneLight(string Name, Vector3 Position, Vector3 Color, float Radius, float Multiplier);

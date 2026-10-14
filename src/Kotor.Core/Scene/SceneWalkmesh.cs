using System.Numerics;

namespace Kotor.Core.Scene;

/// <summary>Walkmesh d'une piece en positions monde du module ; Walkable : un booleen par triangle.</summary>
public sealed record SceneWalkmesh(string Room, Vector3[] Positions, int[] Triangles, bool[] Walkable);

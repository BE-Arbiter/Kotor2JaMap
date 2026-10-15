using System.Numerics;

namespace Kotor.Core.Mdl;

/// <summary>Cle d'un controleur : temps (secondes) et valeur.</summary>
public readonly record struct PositionKey(float Time, Vector3 Value);

/// <summary>Cle d'orientation : temps (secondes) et quaternion.</summary>
public readonly record struct OrientationKey(float Time, Quaternion Value);

/// <summary>Cles de position et d'orientation d'un noeud pendant une animation (listes vides si le noeud n'en a pas).</summary>
public sealed record AnimationTrack(string Node, int NodeNumber, IReadOnlyList<PositionKey> Positions, IReadOnlyList<OrientationKey> Orientations);

/// <summary>Animation d'un MDL (ex. <c>opening1</c>, <c>closing1</c>, <c>open1</c>) : duree en secondes et une piste par noeud anime.</summary>
public sealed record KotorAnimation(string Name, float Length, IReadOnlyList<AnimationTrack> Tracks);

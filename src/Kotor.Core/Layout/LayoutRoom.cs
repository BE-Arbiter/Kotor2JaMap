using System.Numerics;

namespace Kotor.Core.Layout;

/// <summary>Piece d'un module : modele (minuscules) et position en metres KOTOR.</summary>
public sealed record LayoutRoom(string Model, Vector3 Position);

/// <summary>Point d'ancrage de porte : nom, piece (minuscules), position en metres et orientation.</summary>
public sealed record LayoutDoorHook(string Name, string Room, Vector3 Position, Quaternion Orientation);

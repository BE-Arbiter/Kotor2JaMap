using System.Numerics;

namespace Kotor.Core.Modules;

/// <summary>Point d'entree du module (metres KOTOR).</summary>
public readonly record struct ModuleEntry(Vector3 Position, Vector2 Direction);

public sealed record ModuleInfo(string Module, string AreaResRef, string AreaName, ModuleEntry Entry);

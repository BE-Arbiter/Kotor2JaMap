using Kotor.Core.Entities;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Un module de l'assemblage : sa scene et les portes, waypoints et declencheurs de son GIT (repere du module, non transforme).
/// </summary>
public sealed record AssemblyPart(ModuleScene Scene, IReadOnlyList<ModuleEntity> Entities);

/// <summary>Transition d'une porte de la partie <paramref name="FromPart"/> vers la porte ou le waypoint <paramref name="ToTarget"/> de <paramref name="ToPart"/>.</summary>
public sealed record DoorLink(int FromPart, ModuleEntity FromDoor, int ToPart, ModuleEntity ToTarget);

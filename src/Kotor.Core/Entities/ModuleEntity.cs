using System.Numerics;

namespace Kotor.Core.Entities;

/// <summary>
/// Entite du module. Name : Tag, sinon TemplateResRef, sinon nom du noeud (lumieres) ou du type.
/// Position en metres KOTOR (repere du module). Yaw : rotation en degres autour de Z (anti-horaire, 0 = face a +Y).
/// Polygon : points absolus des declencheurs et rencontres, sinon null.
/// </summary>
public sealed record ModuleEntity(EntityKind Kind, string Name, string Template, Vector3 Position, float Yaw, Vector3[]? Polygon);

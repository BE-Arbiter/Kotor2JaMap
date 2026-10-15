using System.Numerics;

namespace Kotor.Core.Entities;

/// <summary>
/// Entite du module. Name : Tag, sinon TemplateResRef, sinon nom du noeud (lumieres) ou du type.
/// Position en metres KOTOR (repere du module). Yaw : rotation en degres autour de Z (anti-horaire, 0 = face a +Y).
/// Polygon : points absolus des declencheurs et rencontres, sinon null.
/// Model : resref du modele (minuscules) d'un objet place ou d'une porte, resolu par <see cref="ObjectModelResolver"/>, sinon null.
/// LinkedToModule (portes, GIT) : module de destination d'une transition, vide sinon. Locked (portes, UTD) : porte verrouillee.
/// </summary>
public sealed record ModuleEntity(EntityKind Kind, string Name, string Template, Vector3 Position, float Yaw, Vector3[]? Polygon,
    string? Model = null, string LinkedToModule = "", bool Locked = false, bool Static = false);

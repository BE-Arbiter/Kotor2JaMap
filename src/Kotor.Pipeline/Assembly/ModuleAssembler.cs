using System.Globalization;
using System.Numerics;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Scene fusionnee d'un assemblage. <see cref="EntitiesByPart"/> : entites de chaque partie en repere assemble, filtrees
/// (porte de destination d'une jonction retiree, porte source gardee sans transition ni verrou). <see cref="Overlaps"/> :
/// paires de parties dont les sols se recouvrent.
/// </summary>
public sealed record AssemblyResult(ModuleScene Scene, IReadOnlyDictionary<int, IReadOnlyList<ModuleEntity>> EntitiesByPart,
    IReadOnlyList<(int A, int B, string Detail)> Overlaps);

/// <summary>Fusion de plusieurs scenes de module transformees en une seule, avec un couloir de raccord par jonction.</summary>
public static class ModuleAssembler
{
    /// <summary>Tolerance (metres) pour reconnaitre une porte relue du GIT par sa position.</summary>
    private const float SameDoorTolerance = 0.01f;

    /// <summary>
    /// Assemble les parties. Info, ambiante, soleil et eclairage de zone : partie de reference ; <c>Info.AreaName</c> : noms des
    /// zones joints par « + ». Une ligne <c>ASSEMBLY_JUNCTION</c> par jonction (ecart en metres) et <c>ASSEMBLY_OVERLAP</c> par
    /// paire de parties qui se chevauchent. <paramref name="frameOf"/> : cadre d'une porte en repere du module (null = cadre par defaut).
    /// </summary>
    public static AssemblyResult Assemble(IReadOnlyList<AssemblyPart> parts, int referencePart, IReadOnlyList<RigidTransform> transforms,
        IReadOnlyList<DoorLink> junctions, Func<AssemblyPart, ModuleEntity, DoorFrame?> frameOf, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(transforms);
        ArgumentNullException.ThrowIfNull(junctions);
        ArgumentNullException.ThrowIfNull(frameOf);
        ArgumentNullException.ThrowIfNull(log);
        if (parts.Count == 0) throw new ArgumentException("At least one part is required.", nameof(parts));
        if (referencePart < 0 || referencePart >= parts.Count) throw new ArgumentOutOfRangeException(nameof(referencePart));
        if (transforms.Count != parts.Count) throw new ArgumentException("One transform per part is required.", nameof(transforms));

        var scenes = parts.Select((p, i) => p.Scene.Transformed(transforms[i])).ToList();
        var meshes = scenes.SelectMany(s => s.Meshes).ToList();
        var walkmeshes = scenes.SelectMany(s => s.Walkmeshes).ToList();
        var opened = parts.Select(_ => new List<ModuleEntity>()).ToList();
        var removed = parts.Select(_ => new List<ModuleEntity>()).ToList();

        for (int k = 0; k < junctions.Count; k++)
        {
            DoorLink j = junctions[k];
            AssemblyPart src = parts[j.FromPart], dst = parts[j.ToPart];
            ModuleEntity? dstDoor = DestinationDoor(dst, j.ToTarget);
            DoorFrame a = Place(frameOf(src, j.FromDoor) ?? DoorFrames.DefaultFrame(j.FromDoor), transforms[j.FromPart]);
            DoorFrame b = Place((dstDoor is null ? null : frameOf(dst, dstDoor)) ?? DoorFrames.DefaultFrame(dstDoor ?? j.ToTarget), transforms[j.ToPart]);
            float gap = Vector3.Distance(a.Center, b.Center);
            var (mesh, walk) = JunctionBuilder.Build(a, b, CorridorTexture(src.Scene));
            if (mesh is not null) meshes.Add(mesh with { Room = "junction_" + k.ToString(CultureInfo.InvariantCulture) });
            if (walk is not null) walkmeshes.Add(walk with { Room = "junction_" + k.ToString(CultureInfo.InvariantCulture) });
            log.Warn("ASSEMBLY_JUNCTION", string.Create(CultureInfo.InvariantCulture,
                $"{src.Scene.Info.Module} door '{j.FromDoor.Name}' -> {dst.Scene.Info.Module} '{(dstDoor ?? j.ToTarget).Name}': gap {gap:0.00} m, {(mesh is null ? "no corridor" : "corridor added")}."));
            opened[j.FromPart].Add(j.FromDoor);
            if (dstDoor is not null) removed[j.ToPart].Add(dstDoor);
        }

        var refs = parts.Select((p, i) => new AssemblyPartRef(p.Scene.Info.Module, transforms[i])
        {
            Info = p.Scene.Info,
            OpenedDoors = opened[i],
            RemovedDoors = removed[i],
        }).ToList();

        ModuleScene reference = scenes[referencePart];
        var scene = reference with
        {
            Info = reference.Info with { AreaName = string.Join(" + ", parts.Select(p => p.Scene.Info.AreaName)) },
            Meshes = meshes,
            Walkmeshes = walkmeshes,
            Textures = MergeTextures(scenes),
            Lights = scenes.SelectMany(s => s.Lights).ToList(),
            Parts = refs,
        };

        var entities = new Dictionary<int, IReadOnlyList<ModuleEntity>>();
        for (int i = 0; i < parts.Count; i++) entities[i] = ApplyPart(parts[i].Entities, refs[i]);

        var overlaps = OverlapChecker.Check(scenes);
        foreach (var (pa, pb, detail) in overlaps)
            log.Warn("ASSEMBLY_OVERLAP", $"{parts[pa].Scene.Info.Module} and {parts[pb].Scene.Info.Module} overlap: {detail}.");
        return new AssemblyResult(scene, entities, overlaps);
    }

    /// <summary>
    /// Entites d'une partie (repere du module) passees en repere assemble : portes retirees ecartees, portes de jonction
    /// gardees sans transition ni verrou, puis position, yaw et polygone transformes.
    /// </summary>
    public static IReadOnlyList<ModuleEntity> ApplyPart(IReadOnlyList<ModuleEntity> entities, AssemblyPartRef part)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(part);
        RigidTransform t = part.Transform;
        var result = new List<ModuleEntity>(entities.Count);
        foreach (ModuleEntity e in entities)
        {
            if (Matches(e, part.RemovedDoors)) continue;
            ModuleEntity f = Matches(e, part.OpenedDoors) ? e with { LinkedToModule = "", Locked = false } : e;
            result.Add(f with
            {
                Position = t.Apply(f.Position),
                Yaw = t.ApplyYaw(f.Yaw),
                Polygon = f.Polygon?.Select(t.Apply).ToArray(),
            });
        }
        return result;
    }

    /// <summary>Meme porte : tag (sans casse) et position (le GIT relu donne d'autres instances).</summary>
    private static bool Matches(ModuleEntity e, IReadOnlyList<ModuleEntity> doors)
    {
        if (e.Kind != EntityKind.Door) return false;
        foreach (ModuleEntity d in doors)
            if (string.Equals(d.Name, e.Name, StringComparison.OrdinalIgnoreCase)
                && Vector3.DistanceSquared(d.Position, e.Position) < SameDoorTolerance * SameDoorTolerance)
                return true;
        return false;
    }

    /// <summary>Porte retiree cote destination : la cible si c'est une porte, sinon la porte de la partie la plus proche du waypoint.</summary>
    private static ModuleEntity? DestinationDoor(AssemblyPart dst, ModuleEntity target)
    {
        if (target.Kind == EntityKind.Door) return target;
        return dst.Entities.Where(e => e.Kind == EntityKind.Door)
            .OrderBy(e => Vector3.DistanceSquared(e.Position, target.Position)).FirstOrDefault();
    }

    private static DoorFrame Place(DoorFrame f, RigidTransform t) => f with { Center = t.Apply(f.Center), Yaw = t.ApplyYaw(f.Yaw) };

    /// <summary>Texture du premier maillage de la partie source dont la texture est connue ; a defaut celle du premier maillage.</summary>
    private static string CorridorTexture(ModuleScene source) =>
        source.Meshes.Select(m => m.Texture).FirstOrDefault(t => source.Textures.TryGetValue(t, out KotorTexture? tex) && tex is not null)
        ?? source.Meshes.FirstOrDefault()?.Texture
        ?? "junction";

    /// <summary>Union des textures ; une entree non nulle n'est jamais remplacee par null.</summary>
    private static Dictionary<string, KotorTexture?> MergeTextures(IEnumerable<ModuleScene> scenes)
    {
        var merged = new Dictionary<string, KotorTexture?>();
        foreach (ModuleScene s in scenes)
            foreach (var (name, tex) in s.Textures)
                if (!merged.TryGetValue(name, out KotorTexture? known) || (known is null && tex is not null))
                    merged[name] = tex;
        return merged;
    }
}

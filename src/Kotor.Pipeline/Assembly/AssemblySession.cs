using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Etat d'edition d'un assemblage : parties, partie de reference, transformations (placement automatique puis manuel),
/// groupes non relies et jonctions choisies a la main. <see cref="Build"/> appelle <see cref="ModuleAssembler"/>.
/// </summary>
public sealed class AssemblySession
{
    private readonly IReadOnlyList<AssemblyPart> _parts;
    private readonly IReadOnlyList<DoorLink> _links;
    private readonly Func<AssemblyPart, ModuleEntity, DoorFrame?> _frameOf;
    private readonly List<DoorLink> _manual = [];
    private readonly List<DoorLink> _groupAuto = [];
    private RigidTransform[] _transforms = [];
    private IReadOnlyList<DoorLink> _auto = [];
    private IReadOnlyList<IReadOnlyList<int>> _groups = [];
    private bool[] _connected = [];
    private ImportLog _placementLog = new();
    private int _reference;

    /// <param name="modules">Nom de module de chaque partie (meme ordre que <paramref name="parts"/>).</param>
    /// <param name="links">Transitions decouvertes (<see cref="TransitionLinks.Find"/>).</param>
    /// <param name="frameOf">Cadre d'une porte en repere du module (null = cadre par defaut).</param>
    public AssemblySession(IReadOnlyList<string> modules, IReadOnlyList<AssemblyPart> parts, IReadOnlyList<DoorLink> links,
        Func<AssemblyPart, ModuleEntity, DoorFrame?> frameOf, int referenceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(links);
        ArgumentNullException.ThrowIfNull(frameOf);
        if (parts.Count == 0) throw new ArgumentException("At least one part is required.", nameof(parts));
        if (modules.Count != parts.Count) throw new ArgumentException("One module name per part is required.", nameof(modules));
        Modules = modules;
        _parts = parts;
        _links = links;
        _frameOf = frameOf;
        _reference = referenceIndex;
        Place();
    }

    public IReadOnlyList<string> Modules { get; }
    public IReadOnlyList<AssemblyPart> Parts => _parts;

    /// <summary>Partie de reference (restee a l'identite). La changer refait le placement automatique et oublie les reglages manuels.</summary>
    public int ReferenceIndex
    {
        get => _reference;
        set
        {
            if (value < 0 || value >= _parts.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _reference) return;
            _reference = value;
            Place();
        }
    }

    /// <summary>Compteur incremente a chaque modification (transformation, jonction, partie de reference) : permet de savoir si un assemblage construit est perime.</summary>
    public int Revision { get; private set; }

    public IReadOnlyList<RigidTransform> Transforms => _transforms;

    /// <summary>Groupes de parties sans liaison avec la reference apres le placement automatique et les jonctions manuelles.</summary>
    public IReadOnlyList<IReadOnlyList<int>> UnlinkedGroups => _groups.Where(g => !_connected[g[0]]).ToList();

    /// <summary>Transitions retenues par le placement automatique.</summary>
    public IReadOnlyList<DoorLink> AutoLinks => [.. _auto, .. _groupAuto];

    public bool IsComplete => UnlinkedGroups.Count == 0;

    /// <summary>Placement manuel d'une partie (la connectivite n'est pas modifiee).</summary>
    public void SetTransform(int part, RigidTransform t)
    {
        if (part < 0 || part >= _parts.Count) throw new ArgumentOutOfRangeException(nameof(part));
        _transforms[part] = t;
        Revision++;
    }

    /// <summary>
    /// Jonction choisie a la main entre la porte <paramref name="doorA"/> de la partie <paramref name="partA"/> et la porte
    /// <paramref name="doorB"/> de <paramref name="partB"/>. Si une seule des deux parties est deja reliee a la reference, l'autre
    /// (avec son groupe) est alignee porte contre porte sur elle.
    /// </summary>
    public void SetManualJunction(int partA, ModuleEntity doorA, int partB, ModuleEntity doorB)
    {
        ArgumentNullException.ThrowIfNull(doorA);
        ArgumentNullException.ThrowIfNull(doorB);
        if (partA < 0 || partA >= _parts.Count) throw new ArgumentOutOfRangeException(nameof(partA));
        if (partB < 0 || partB >= _parts.Count) throw new ArgumentOutOfRangeException(nameof(partB));
        if (partA == partB) throw new ArgumentException("A junction needs two different parts.");
        if (!_connected[partA] && _connected[partB])
            (partA, doorA, partB, doorB) = (partB, doorB, partA, doorA);
        if (!_connected[partA]) throw new InvalidOperationException("Neither part is linked to the reference part.");

        if (!_connected[partB])
        {
            RigidTransform ta = _transforms[partA];
            RigidTransform tb = RigidTransform.AligningDoors(ta.Apply(doorA.Position), ta.ApplyYaw(doorA.Yaw), doorB.Position, doorB.Yaw);
            PlaceGroup(partB, tb);
        }
        _manual.Add(new DoorLink(partA, doorA, partB, doorB));
        Revision++;
    }

    /// <summary>Assemble les parties avec les transformations et les jonctions courantes.</summary>
    public AssemblyResult Build(ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        foreach (var (code, message) in _placementLog.Warnings) log.Warn(code, message);
        return ModuleAssembler.Assemble(_parts, _reference, _transforms, [.. AutoLinks, .. _manual], _frameOf, log);
    }

    private void Place()
    {
        Revision++;
        _placementLog = new ImportLog();
        Placement p = AutoPlacer.Place(_parts, _reference, _links, _placementLog);
        _transforms = [.. p.Transforms];
        _auto = p.Used;
        _groups = p.UnlinkedGroups;
        _connected = Enumerable.Repeat(true, _parts.Count).ToArray();
        foreach (var g in _groups) foreach (int i in g) _connected[i] = false;
        _manual.Clear();
        _groupAuto.Clear();
    }

    /// <summary>Place le groupe de <paramref name="root"/> : la racine prend <paramref name="rootTransform"/>, les autres sont placees par leurs liens internes.</summary>
    private void PlaceGroup(int root, RigidTransform rootTransform)
    {
        IReadOnlyList<int> members = _groups.First(g => g.Contains(root));
        var local = members.Select((m, i) => (m, i)).ToDictionary(x => x.m, x => x.i);
        var subLinks = _links.Where(l => local.ContainsKey(l.FromPart) && local.ContainsKey(l.ToPart))
            .Select(l => l with { FromPart = local[l.FromPart], ToPart = local[l.ToPart] }).ToList();
        Placement sub = AutoPlacer.Place([.. members.Select(m => _parts[m])], local[root], subLinks, _placementLog);
        for (int i = 0; i < members.Count; i++)
        {
            _transforms[members[i]] = sub.Transforms[i].Then(rootTransform);
            _connected[members[i]] = true;
        }
        foreach (DoorLink l in sub.Used) _groupAuto.Add(l with { FromPart = members[l.FromPart], ToPart = members[l.ToPart] });
    }
}

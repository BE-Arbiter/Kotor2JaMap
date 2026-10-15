using System.Numerics;
using Kotor.Core.Entities;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Logique de l'editeur de placement (sans interface) : deplacement, rotation autour du centre du module, saisie numerique et
/// alignement porte contre porte, appliques directement a une <see cref="AssemblySession"/>. <see cref="Cancel"/> remet la
/// session dans l'etat ou elle etait a l'ouverture. Unites : metres KOTOR, yaw en degres autour de Z.
/// </summary>
public sealed class PlacementEditorModel
{
    private readonly AssemblySession _session;
    private readonly AssemblySessionState _initial;
    private readonly int _initialRevision;
    private readonly ModuleScene[] _floors;          // scenes reduites aux walkmeshes (calcul des chevauchements)
    private readonly Vector3[] _localCentre;
    private readonly (Vector3 Min, Vector3 Max)[] _localBounds;
    private readonly Vector3[][] _localFloor;        // triangles marchables (liste de sommets, par trois), repere du module
    private readonly IReadOnlyList<ModuleEntity>[] _doors;
    private IReadOnlyList<(int A, int B, string Detail)> _overlaps = [];
    private RigidTransform[]? _overlapsFor;          // transformations pour lesquelles _overlaps a ete calcule

    public PlacementEditorModel(AssemblySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _initial = session.CaptureState();
        _initialRevision = session.Revision;
        int n = session.Parts.Count;
        _floors = new ModuleScene[n];
        _localCentre = new Vector3[n];
        _localBounds = new (Vector3, Vector3)[n];
        _localFloor = new Vector3[n][];
        _doors = new IReadOnlyList<ModuleEntity>[n];
        for (int i = 0; i < n; i++)
        {
            AssemblyPart p = session.Parts[i];
            _floors[i] = p.Scene with { Meshes = [], Lights = [] };
            _localBounds[i] = Bounds(p.Scene);
            _localCentre[i] = (_localBounds[i].Min + _localBounds[i].Max) * 0.5f;
            _localFloor[i] = Floor(p.Scene);
            _doors[i] = p.Entities.Where(e => e.Kind == EntityKind.Door).ToList();
        }
    }

    public AssemblySession Session => _session;
    public int Count => _session.Parts.Count;

    /// <summary>Vrai si la session a change depuis l'ouverture de l'editeur.</summary>
    public bool HasChanges => _session.Revision != _initialRevision;

    public RigidTransform Transform(int part) => _session.Transforms[Check(part)];

    /// <summary>Portes de la partie (repere du module), dans l'ordre du GIT.</summary>
    public IReadOnlyList<ModuleEntity> Doors(int part) => _doors[Check(part)];

    /// <summary>Centre de la boite englobante de la partie (walkmeshes, a defaut maillages), en repere du module.</summary>
    public Vector3 LocalCentre(int part) => _localCentre[Check(part)];

    /// <summary>Boite englobante de la partie en repere du module (walkmeshes, a defaut maillages ; zero si la scene est vide).</summary>
    public (Vector3 Min, Vector3 Max) LocalBounds(int part) => _localBounds[Check(part)];

    /// <summary>Triangles marchables de la partie en repere du module (sommets par trois).</summary>
    public IReadOnlyList<Vector3> LocalFloor(int part) => _localFloor[Check(part)];

    /// <summary>Centre de la partie en repere assemble.</summary>
    public Vector3 Centre(int part) => Transform(part).Apply(_localCentre[part]);

    /// <summary>Deplace la partie (translation ajoutee, en metres).</summary>
    public void Nudge(int part, Vector3 deltaMeters)
    {
        RigidTransform t = Transform(part);
        _session.SetTransform(part, t with { Translation = t.Translation + deltaMeters });
    }

    /// <summary>Tourne la partie autour de son centre (le centre ne bouge pas).</summary>
    public void Rotate(int part, float deltaDegrees)
    {
        RigidTransform t = Transform(part);
        Vector3 c = _localCentre[part];
        var r = new RigidTransform(t.ApplyYaw(deltaDegrees), Vector3.Zero);
        _session.SetTransform(part, r with { Translation = t.Apply(c) - r.ApplyDirection(c) });
    }

    /// <summary>Saisie numerique : translation (origine du module en repere assemble) et yaw de la partie.</summary>
    public void SetNumeric(int part, Vector3 position, float yawDegrees)
    {
        Check(part);
        _session.SetTransform(part, new RigidTransform(RigidTransform.Identity.ApplyYaw(yawDegrees), position));
    }

    /// <summary>
    /// Jonction choisie a la main : la porte <paramref name="doorB"/> de <paramref name="partB"/> est posee dos a dos contre
    /// <paramref name="doorA"/> de <paramref name="partA"/> (<see cref="RigidTransform.AligningDoors"/>), comme
    /// <see cref="AssemblySession.SetManualJunction"/>. Si une seule des deux parties est reliee a la reference, c'est l'autre
    /// (avec son groupe) qui bouge ; si les deux le sont deja, la partie B est realignee (la partie A si B est la reference).
    /// </summary>
    public void SnapDoors(int partA, ModuleEntity doorA, int partB, ModuleEntity doorB)
    {
        ArgumentNullException.ThrowIfNull(doorA);
        ArgumentNullException.ThrowIfNull(doorB);
        Check(partA);
        Check(partB);
        if (partA == partB) throw new ArgumentException("A junction needs two different modules.");
        bool linkedA = _session.IsLinked(partA), linkedB = _session.IsLinked(partB);
        if (!linkedA && !linkedB)
            throw new InvalidOperationException("Neither module is linked to the reference module: link one of them first.");
        if (linkedA && linkedB)
        {
            if (partB == _session.ReferenceIndex) (partA, doorA, partB, doorB) = (partB, doorB, partA, doorA);
            RigidTransform ta = Transform(partA);
            _session.SetTransform(partB, RigidTransform.AligningDoors(ta.Apply(doorA.Position), ta.ApplyYaw(doorA.Yaw), doorB.Position, doorB.Yaw));
        }
        _session.SetManualJunction(partA, doorA, partB, doorB);
    }

    /// <summary>Paires de parties dont les sols se recouvrent (<see cref="OverlapChecker"/>), recalculees apres chaque modification.</summary>
    public IReadOnlyList<(int A, int B, string Detail)> Overlaps
    {
        get
        {
            IReadOnlyList<RigidTransform> current = _session.Transforms;
            if (_overlapsFor is null || !_overlapsFor.SequenceEqual(current))
            {
                _overlaps = OverlapChecker.Check(_floors.Select((f, i) => f.Transformed(current[i])).ToList());
                _overlapsFor = [.. current];
            }
            return _overlaps;
        }
    }

    /// <summary>Partie dont un triangle marchable (a defaut la boite) contient le point de la vue de dessus ; null sinon.</summary>
    public int? PartAt(Vector2 point)
    {
        for (int i = Count - 1; i >= 0; i--)
        {
            RigidTransform t = Transform(i);
            // Point ramene dans le repere du module : rotation inverse de (point - translation).
            var inv = new RigidTransform(-t.YawDegrees, Vector3.Zero);
            Vector3 l = inv.ApplyDirection(new Vector3(point.X - t.Translation.X, point.Y - t.Translation.Y, 0f));
            var p = new Vector2(l.X, l.Y);
            Vector3[] floor = _localFloor[i];
            if (floor.Length == 0)
            {
                var (min, max) = _localBounds[i];
                if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && max.X > min.X) return i;
                continue;
            }
            for (int k = 0; k + 2 < floor.Length; k += 3)
                if (InTriangle(p, Xy(floor[k]), Xy(floor[k + 1]), Xy(floor[k + 2]))) return i;
        }
        return null;
    }

    /// <summary>Remet la session dans l'etat de l'ouverture de l'editeur.</summary>
    public void Cancel()
    {
        _session.RestoreState(_initial);
        _overlapsFor = null;
    }

    private int Check(int part)
    {
        if (part < 0 || part >= Count) throw new ArgumentOutOfRangeException(nameof(part));
        return part;
    }

    private static Vector2 Xy(Vector3 v) => new(v.X, v.Y);

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        static float Cross(Vector2 o, Vector2 u, Vector2 v) => (u.X - o.X) * (v.Y - o.Y) - (u.Y - o.Y) * (v.X - o.X);
        if (Cross(a, b, c) == 0f) return false; // triangle degenere
        float d1 = Cross(a, b, p), d2 = Cross(b, c, p), d3 = Cross(c, a, p);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static (Vector3 Min, Vector3 Max) Bounds(ModuleScene s)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        // Walkmeshes d'abord (les maillages d'un module peuvent deborder tres loin des sols : decor lointain) ; a defaut, maillages.
        IEnumerable<Vector3> points = s.Walkmeshes.Any(w => w.Positions.Length > 0)
            ? s.Walkmeshes.SelectMany(w => w.Positions)
            : s.Meshes.SelectMany(m => m.Positions);
        foreach (Vector3 p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            any = true;
        }
        return any ? (min, max) : (Vector3.Zero, Vector3.Zero);
    }

    private static Vector3[] Floor(ModuleScene s)
    {
        var list = new List<Vector3>();
        foreach (SceneWalkmesh w in s.Walkmeshes)
            for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
            {
                if (t / 3 >= w.Walkable.Length || !w.Walkable[t / 3]) continue;
                list.Add(w.Positions[w.Triangles[t]]);
                list.Add(w.Positions[w.Triangles[t + 1]]);
                list.Add(w.Positions[w.Triangles[t + 2]]);
            }
        return [.. list];
    }
}

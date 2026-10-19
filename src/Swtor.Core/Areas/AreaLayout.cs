using System.Numerics;

namespace Swtor.Core.Areas;

/// <summary>Transformations des instances SWTOR (unite = metre, Y vers le haut).</summary>
public static class AreaTransforms
{
    /// <summary>
    /// Rotation depuis les angles d'Euler en degres tels que stockes (X, Y, Z). Convention validee (T7 Euler) : angles positifs au sens
    /// mathematique direct (+90 sur Y envoie +X vers -Z) et ordre X d'abord, puis Z, puis Y : tangage X et roulis Z dans le repere
    /// propre de l'objet, puis lacet Y autour de la verticale du parent (matrice colonne <c>Ry * Rz * Rx</c>, soit <c>qy * qz * qx</c> avec
    /// le produit de Hamilton de System.Numerics). Preuve : 12 appliques <c>spc_item_ss_wall_light01</c> disposees en cercle ne different
    /// entre elles que d'un lacet pur avec cet ordre seulement (ecart 0,0 deg, contre 79,9 deg pour l'ancien ordre Z-Y-X) ; voir swtor-formats.md.
    /// Les rotations a un seul axe, ou sans X, n'en dependent pas.
    /// </summary>
    public static Quaternion RotationFromEuler(Vector3 degrees)
    {
        const float d2r = MathF.PI / 180f;
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, degrees.X * d2r);
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, degrees.Y * d2r);
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees.Z * d2r);
        return qy * qz * qx;
    }

    /// <summary>
    /// Matrice locale (convention System.Numerics, vecteur ligne : v' = v * M) = Echelle * Rotation * Translation.
    /// Le monde d'un enfant est <c>local * monde(parent)</c>.
    /// </summary>
    public static Matrix4x4 Local(RoomInstance i) =>
        Matrix4x4.CreateScale(i.Scale) * Matrix4x4.CreateFromQuaternion(RotationFromEuler(i.EulerDegrees)) * Matrix4x4.CreateTranslation(i.Position);
}

/// <summary>Une zone complete : area.dat plus toutes ses rooms chargees, avec composition des transformations parent/enfant.</summary>
public sealed class AreaLayout
{
    private readonly Dictionary<ulong, RoomInstance> _byId = new();
    private readonly Dictionary<ulong, Matrix4x4> _worldCache = new();
    private readonly Dictionary<ulong, string> _roomOf = new();

    public required string Folder { get; init; }
    public required AreaDat Area { get; init; }

    /// <summary>Rooms lues, dans l'ordre d'area.dat (nom, contenu).</summary>
    public required IReadOnlyList<(string Name, RoomDat Room)> Rooms { get; init; }

    /// <summary>Rooms listees dans area.dat mais sans fichier .dat sur le disque.</summary>
    public required IReadOnlyList<string> MissingRooms { get; init; }

    public IEnumerable<RoomInstance> AllInstances => Rooms.SelectMany(r => r.Room.Instances);

    public RoomInstance? Find(ulong id) => _byId.TryGetValue(id, out var i) ? i : null;

    public string? RoomOf(ulong id) => _roomOf.TryGetValue(id, out var n) ? n : null;

    /// <summary>
    /// Dossier d'une zone : accepte un chemin de dossier ou un identifiant numerique (cherche dans
    /// <c>&lt;racine&gt;/world/areas/&lt;id&gt;</c>).
    /// </summary>
    public static string? ResolveFolder(string areaIdOrFolder, SwtorAssets assets)
    {
        if (Directory.Exists(areaIdOrFolder) && File.Exists(Path.Combine(areaIdOrFolder, "area.dat"))) return areaIdOrFolder;
        if (areaIdOrFolder.IndexOfAny(['/', '\\']) >= 0 || areaIdOrFolder.Contains("..")) return null;
        string p = Path.Combine(assets.Root, "world", "areas", areaIdOrFolder);
        return File.Exists(Path.Combine(p, "area.dat")) ? p : null;
    }

    public static AreaLayout Load(string folder)
    {
        AreaDat area = AreaDatReader.Read(Path.Combine(folder, "area.dat"));
        var rooms = new List<(string, RoomDat)>();
        var missing = new List<string>();
        foreach (string name in area.Rooms)
        {
            string file = Path.Combine(folder, name + ".dat");
            if (!File.Exists(file)) { missing.Add(name); continue; }
            rooms.Add((name, RoomDatReader.Read(file, area)));
        }
        var layout = new AreaLayout { Folder = folder, Area = area, Rooms = rooms, MissingRooms = missing };
        foreach (var (name, room) in rooms)
        {
            foreach (var inst in room.Instances)
            {
                layout._byId[inst.Id] = inst;
                layout._roomOf[inst.Id] = name;
            }
        }
        return layout;
    }

    /// <summary>Matrice monde (v * M) : transformation locale composee avec celles des parents (cycles et chaines trop longues ignores).</summary>
    public Matrix4x4 World(RoomInstance inst)
    {
        if (_worldCache.TryGetValue(inst.Id, out var cached)) return cached;
        Matrix4x4 m = AreaTransforms.Local(inst);
        var seen = new HashSet<ulong> { inst.Id };
        RoomInstance cur = inst;
        while (cur.ParentId != 0 && _byId.TryGetValue(cur.ParentId, out var parent) && seen.Add(parent.Id) && seen.Count < 64)
        {
            m *= AreaTransforms.Local(parent);
            cur = parent;
        }
        _worldCache[inst.Id] = m;
        return m;
    }

    public Vector3 WorldPosition(RoomInstance inst) => Vector3.Transform(Vector3.Zero, World(inst));
}

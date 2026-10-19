using System.Numerics;
using System.Text;

namespace Swtor.Core.Tests;

/// <summary>Fabrique des area.dat / room .dat synthetiques (octets) pour les tests des lecteurs, sans ressources du jeu.</summary>
public static class SyntheticArea
{
    public const ulong GroundTile = 0x4000000000000001UL;
    public const ulong LightClass = 0x4000000000000002UL;
    public const ulong FxClass = 0x4000000000000003UL;
    public const ulong UnlistedType = 0x40000000000000FFUL;

    public const string GroundTilePath = "\\art\\static\\area\\test\\arch\\floor_tile.gr2";

    public sealed record Obj(ulong Id, ulong TypeId, byte Version = 0, ulong Parent = 0, Vector3? Position = null,
        Vector3? Euler = null, Vector3? Scale = null, Action<List<byte>>? Extra = null, int ExtraFields = 0);

    public static byte[] Area(string[] rooms, Dictionary<ulong, string> assets, string areaProperties = "~0|1|10|is_outdoor|5|false|",
        string[]? markers = null, Dictionary<ulong, string>? terrainLayers = null)
    {
        var body = new List<byte>();
        var offsets = new int[9];
        int headerSize = 4 + 24 + 36 + 22; // longueur + magique + 9 offsets + 11 mots 16 bits
        offsets[0] = headerSize + body.Count;
        Int(body, rooms.Length);
        foreach (string r in rooms) Ascii(body, r);
        offsets[1] = headerSize + body.Count;
        Int(body, assets.Count);
        foreach (var (id, path) in assets) { Long(body, id); Ascii(body, path); }
        offsets[2] = headerSize + body.Count;
        var marks = markers ?? [];
        Int(body, marks.Length);
        ulong mid = 0x4000000600000100UL;
        foreach (string m in marks)
        {
            Long(body, mid++);
            Ascii(body, m);
            body.AddRange(new byte[10]); // contenu non decode
        }
        offsets[3] = headerSize + body.Count;
        Int(body, 1);
        Ascii(body, "Area");
        Ascii(body, areaProperties);
        offsets[4] = headerSize + body.Count;
        Int(body, terrainLayers?.Count ?? 0);
        foreach (var (id, name) in terrainLayers ?? []) { Long(body, id); Ascii(body, name); }
        for (int i = 5; i < 9; i++) { offsets[i] = headerSize + body.Count; Int(body, 0); }

        var file = new List<byte>();
        Ascii(file, "AREA_DAT_BINARY_FORMAT_");
        foreach (int o in offsets) Int(file, o);
        for (int i = 0; i < 11; i++) file.AddRange([1, 0]);
        file.AddRange(body);
        return [.. file];
    }

    public static byte[] Room(string internalPath, Obj[] objects, string[]? linked = null, string? propKey = "Area", string propValue = "1")
    {
        var path = new List<byte>();
        Ascii(path, internalPath);
        int headerSize = 4 + 24 + 12 + 8 + path.Count;
        var objs = new List<byte>();
        Int(objs, objects.Length);
        foreach (var o in objects) WriteObject(objs, o);
        int a = headerSize;
        int b = a + objs.Count;
        var linkedBytes = new List<byte>();
        var l = linked ?? [];
        Int(linkedBytes, l.Length);
        foreach (string s in l) Utf16(linkedBytes, s);
        int c = b + linkedBytes.Count;
        var tail = new List<byte>();
        if (propKey is not null) { Utf16(tail, propKey); Utf16(tail, propValue); }
        Int(tail, 2);
        tail.AddRange(new byte[40]);

        var file = new List<byte>();
        Ascii(file, "ROOM_DAT_BINARY_FORMAT_");
        Int(file, a); Int(file, b); Int(file, c);
        file.AddRange([3, 0, 1, 0, 1, 0, 1, 0]);
        file.AddRange(path);
        file.AddRange(objs);
        file.AddRange(linkedBytes);
        file.AddRange(tail);
        return [.. file];
    }

    /// <summary>Ecrit area.dat et les rooms dans un dossier temporaire et renvoie son chemin.</summary>
    public static string WriteFolder(string[] roomNames, Dictionary<ulong, string> assets, Func<string, Obj[]> objectsOf)
    {
        string dir = Path.Combine(Path.GetTempPath(), "swtor_area_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "area.dat"), Area(roomNames, assets, markers: ["arrival_test"]));
        foreach (string r in roomNames)
            File.WriteAllBytes(Path.Combine(dir, r + ".dat"), Room("/world/areas/1/" + r + ".dat", objectsOf(r), linked: roomNames.Where(x => x != r).ToArray()));
        return dir;
    }

    /// <summary>Area.dat synthetique suivi du bloc final [id 64 bits][longueur][nom interne][0] lu par <see cref="AreaDatReader"/>.</summary>
    public static byte[] AreaWithInternalName(string internalName, string[] rooms, Dictionary<ulong, string> assets)
    {
        var tail = new List<byte>();
        tail.AddRange(new byte[16]);
        Long(tail, 0x4000000000012345UL);
        Int(tail, internalName.Length + 1);
        tail.AddRange(Encoding.ASCII.GetBytes(internalName));
        tail.Add(0);
        tail.AddRange(new byte[8]);
        return [.. Area(rooms, assets), .. tail];
    }

    /// <summary>Racine de ressources synthetique : <c>world/areas/&lt;id&gt;/area.dat</c> et un room .dat par zone (sans GOM ni chaines).</summary>
    public static string WriteRoot(params (string Id, string InternalName, string[] Rooms)[] areas)
    {
        string root = Path.Combine(Path.GetTempPath(), "swtor_root_" + Guid.NewGuid().ToString("N"));
        foreach (var (id, name, rooms) in areas)
        {
            string dir = Path.Combine(root, "world", "areas", id);
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "area.dat"), AreaWithInternalName(name, rooms, StandardAssets()));
            foreach (string r in rooms)
                File.WriteAllBytes(Path.Combine(dir, r + ".dat"), Room("/world/areas/" + id + "/" + r + ".dat", [new Obj(1, GroundTile), new Obj(2, GroundTile), new Obj(3, GroundTile)]));
        }
        return root;
    }

    public static Dictionary<ulong, string> StandardAssets() => new()
    {
        [GroundTile] = GroundTilePath,
        [LightClass] = "\\engine\\light.lit",
        [FxClass] = "\\engine\\fxplaceable.fxp",
    };

    private static void WriteObject(List<byte> o, Obj obj)
    {
        var fields = new List<byte>();
        int count = 0;
        void Vec(uint hash, Vector3 v) { fields.Add(6); UInt(fields, hash); Float(fields, v.X); Float(fields, v.Y); Float(fields, v.Z); count++; }
        fields.Add(5); UInt(fields, 0x40865E7F); Long(fields, obj.Parent); count++;
        Vec(0x4F77E269, obj.Position ?? Vector3.Zero);
        Vec(0xB181622A, obj.Scale ?? Vector3.One);
        Vec(0x8C64AF1E, obj.Euler ?? Vector3.Zero);
        fields.Add(0); UInt(fields, 0xE3D8F636); fields.Add(1); count++;
        fields.Add(8); UInt(fields, 0xC2676F38); Utf16(fields, "environments/test/fx"); count++;
        if (obj.Extra is not null) { obj.Extra(fields); count += obj.ExtraFields; }

        UInt(o, 0xABCD1234);
        o.Add(obj.Version);
        Long(o, obj.Id);
        Long(o, obj.TypeId);
        o.Add(0);
        Int(o, count);
        Int(o, fields.Count + 1);
        o.Add(obj.Version);
        o.AddRange(fields);
    }

    public static void Vec4Field(List<byte> f, uint hash, Vector4 v)
    {
        f.Add(7); UInt(f, hash); Float(f, v.X); Float(f, v.Y); Float(f, v.Z); Float(f, v.W);
    }

    public static void BlobField(List<byte> f, uint hash, byte[] data)
    {
        f.Add(9); UInt(f, hash); Int(f, data.Length); f.AddRange(data);
    }

    public static void RawField(List<byte> f, byte type, uint hash, params byte[] value)
    {
        f.Add(type); UInt(f, hash); f.AddRange(value);
    }

    private static void Int(List<byte> l, int v) => l.AddRange(BitConverter.GetBytes(v));
    private static void UInt(List<byte> l, uint v) => l.AddRange(BitConverter.GetBytes(v));
    private static void Long(List<byte> l, ulong v) => l.AddRange(BitConverter.GetBytes(v));
    private static void Float(List<byte> l, float v) => l.AddRange(BitConverter.GetBytes(v));

    private static void Ascii(List<byte> l, string s)
    {
        Int(l, s.Length + 1);
        l.AddRange(Encoding.UTF8.GetBytes(s));
        l.Add(0);
    }

    private static void Utf16(List<byte> l, string s)
    {
        Int(l, (s.Length + 1) * 2);
        l.AddRange(Encoding.Unicode.GetBytes(s));
        l.AddRange([0, 0]);
    }
}

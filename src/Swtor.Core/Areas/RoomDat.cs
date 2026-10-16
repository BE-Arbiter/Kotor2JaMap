using System.Numerics;

namespace Swtor.Core.Areas;

/// <summary>Famille d'un objet de room, deduite de l'extension de son asset (table d'assets d'area.dat).</summary>
public enum InstanceKind
{
    /// <summary>Type absent de la table d'assets (ou table non fournie) : l'objet est conserve tel quel.</summary>
    Unknown,
    /// <summary>Modele statique (.gr2), architecture ou objet decoratif.</summary>
    Model,
    /// <summary>Lumiere (\engine\light.lit).</summary>
    Light,
    /// <summary>Effet place (\engine\fxplaceable.fxp) : le nom du fxspec est dans un champ texte.</summary>
    FxPlaceable,
    /// <summary>Portail de visibilite (\engine\portal.p).</summary>
    Portal,
    /// <summary>Declencheur (\engine\trigger.trg).</summary>
    Trigger,
    /// <summary>Region (\engine\region.rgn, enviroregion.env).</summary>
    Region,
    /// <summary>Volume de room (\engine\roombound.rbd).</summary>
    RoomBound,
    /// <summary>Terrain (\engine\heightmap.hms).</summary>
    Heightmap,
    /// <summary>Point de couverture (cover_point.cvr, cover_dirty_region.cdr).</summary>
    Cover,
    /// <summary>Points de spawn, mouvement, rally (.spn*, .spn_c...).</summary>
    Spawn,
    /// <summary>Boite generique (\engine\genericbox.box), camera, projecteur, eau, son (.wws), etc.</summary>
    Engine,
    /// <summary>Contenu de jeu (.mpn, .stg, .enc, .apl ...), sans geometrie.</summary>
    GameData,
    /// <summary>Asset reconnu dans la table mais d'une extension non classee.</summary>
    Other,
}

/// <summary>Un champ brut d'un objet : type (octet), hash de nom (32 bits) et valeur.</summary>
/// <remarks>Types : 0 booleen (byte), 1 entier/flottant 32 bits non determine (<see cref="uint"/>), 3 int32, 4 float,
/// 5 reference d'objet (ulong), 6 vecteur 3 floats, 7 vecteur 4 floats, 8 chaine UTF-16, 9 blob d'octets.</remarks>
public sealed record RoomField(byte Type, uint Hash, object Value);

/// <summary>Objet place dans une room.</summary>
public sealed class RoomInstance
{
    /// <summary>Identifiant 64 bits unique dans l'area (utilise par les references de parent).</summary>
    public required ulong Id { get; init; }

    /// <summary>Identifiant de l'asset (cle de la table d'assets d'area.dat) : le "type" de l'objet.</summary>
    public required ulong TypeId { get; init; }

    /// <summary>Chemin resolu de l'asset ("\art\static\...gr2", "\engine\light.lit"...) ou null si inconnu.</summary>
    public string? AssetPath { get; init; }

    public required InstanceKind Kind { get; init; }

    /// <summary>Octet de version de l'enregistrement (0 = modele, 2 = fxplaceable, 3 = lumiere... selon la classe).</summary>
    public byte Version { get; init; }

    /// <summary>Id de l'objet parent (0 = aucun). La position/rotation/echelle sont alors relatives a ce parent.</summary>
    public ulong ParentId { get; init; }

    public Vector3 Position { get; init; }

    /// <summary>Echelle par axe (1,1,1 par defaut).</summary>
    public Vector3 Scale { get; init; } = Vector3.One;

    /// <summary>Rotation en degres (X, Y, Z) telle que stockee. Convention d'application : voir <see cref="AreaTransforms"/>.</summary>
    public Vector3 EulerDegrees { get; init; }

    /// <summary>Pour les lumieres : couleur RGBA (champ de type 7), sinon null.</summary>
    public Vector4? Color { get; init; }

    /// <summary>Tous les champs, dans l'ordre du fichier.</summary>
    public required IReadOnlyList<RoomField> Fields { get; init; }

    /// <summary>Premier champ chaine non vide d'un hash donne.</summary>
    public string? GetString(uint hash) =>
        Fields.FirstOrDefault(f => f.Hash == hash && f.Type == 8 && f.Value is string s && s.Length > 0)?.Value as string;

    /// <summary>Nom de fxspec d'un FxPlaceable.</summary>
    public string? FxName => GetString(RoomFieldHashes.FxSpec);

    /// <summary>Extension minuscule de l'asset sans le point ("gr2", "lit", "spn_c"...).</summary>
    public string? Extension
    {
        get
        {
            if (AssetPath is null) return null;
            int dot = AssetPath.LastIndexOf('.');
            int sep = AssetPath.LastIndexOfAny(['\\', '/']);
            return dot > sep ? AssetPath[(dot + 1)..].ToLowerInvariant() : null;
        }
    }
}

/// <summary>Hash 32 bits des noms de champs connus (nom d'origine inconnu : le hash est stocke tel quel dans le fichier).</summary>
public static class RoomFieldHashes
{
    public const uint Parent = 0x40865E7F;
    public const uint Position = 0x4F77E269;
    public const uint Scale = 0xB181622A;
    public const uint Rotation = 0x8C64AF1E;
    /// <summary>Couleur RGBA d'une lumiere.</summary>
    public const uint LightColor = 0xA67AE663;
    /// <summary>Nom du fxspec d'un fxplaceable (ex. "environments/all/env_all_radiator_steam").</summary>
    public const uint FxSpec = 0xC2676F38;
}

/// <summary>Contenu d'un fichier de room (.dat, "ROOM_DAT_BINARY_FORMAT_").</summary>
public sealed class RoomDat
{
    /// <summary>Chemin interne du fichier ("/world/areas/&lt;id&gt;/&lt;room&gt;.dat").</summary>
    public required string InternalPath { get; init; }

    public required IReadOnlyList<RoomInstance> Instances { get; init; }

    /// <summary>Liste de noms de rooms en fin de fichier (rooms liees / voisines, hypothese).</summary>
    public required IReadOnlyList<string> LinkedRooms { get; init; }

    /// <summary>Paires cle/valeur textuelles de fin de fichier (ex. "Area" = "1").</summary>
    public required IReadOnlyDictionary<string, string> Properties { get; init; }
}

public static class RoomDatReader
{
    public const string Magic = "ROOM_DAT_BINARY_FORMAT_";
    private const uint ObjectMagic = 0xABCD1234;

    public static RoomDat Read(string path, AreaDat? area = null) => Read(File.ReadAllBytes(path), area?.Assets);

    public static RoomDat Read(byte[] data, AreaDat? area) => Read(data, area?.Assets);

    /// <param name="assets">Table d'assets (id vers chemin) d'area.dat ; null = tous les objets sont <see cref="InstanceKind.Unknown"/>.</param>
    public static RoomDat Read(byte[] data, IReadOnlyDictionary<ulong, string>? assets)
    {
        var r = new ByteCursor(data);
        string magic = r.LengthPrefixedAscii();
        if (magic != Magic) throw new GameFormatException($"Entete room.dat inattendu '{magic}'", 0);
        uint objectsOffset = r.U32();
        uint linkedOffset = r.U32();
        uint propsOffset = r.U32();
        r.Skip(8); // 4 mots de 16 bits (3,1,1,1) : versions de sections, non exploitees
        string internalPath = r.LengthPrefixedAscii();

        r.Seek(objectsOffset);
        int count = r.Count(data.Length);
        var instances = new List<RoomInstance>(count);
        for (int i = 0; i < count; i++) instances.Add(ReadObject(r, assets));

        var linked = new List<string>();
        var props = new Dictionary<string, string>(StringComparer.Ordinal);
        if (linkedOffset == r.Position)
        {
            int n = r.Count(data.Length);
            for (int i = 0; i < n; i++) linked.Add(r.LengthPrefixedUtf16());
        }
        // Fin de fichier : une paire de chaines UTF-16 optionnelle (cle, valeur) sans compteur, puis un bloc non decode.
        if (propsOffset < data.Length && propsOffset >= r.Position)
        {
            r.Seek(propsOffset);
            if (TryReadPrintableUtf16(r, out string? k) && TryReadPrintableUtf16(r, out string? v)) props[k!] = v!;
        }

        return new RoomDat { InternalPath = internalPath, Instances = instances, LinkedRooms = linked, Properties = props };
    }

    /// <summary>Lit une chaine UTF-16 prefixee si elle est plausible (ASCII imprimable, nul final), sinon ne consomme rien.</summary>
    private static bool TryReadPrintableUtf16(ByteCursor r, out string? value)
    {
        value = null;
        int save = r.Position;
        byte[] d = r.Data;
        if (save + 4 > d.Length) return false;
        int len = BitConverter.ToInt32(d, save);
        if (len < 4 || len > 512 || (len & 1) != 0 || save + 4 + len > d.Length) return false;
        for (int i = 0; i < len - 2; i += 2)
        {
            if (d[save + 4 + i + 1] != 0 || d[save + 4 + i] < 0x20 || d[save + 4 + i] > 0x7E) return false;
        }
        if (d[save + 4 + len - 2] != 0 || d[save + 4 + len - 1] != 0) return false;
        value = r.LengthPrefixedUtf16();
        return true;
    }

    private static RoomInstance ReadObject(ByteCursor r, IReadOnlyDictionary<ulong, string>? assets)
    {
        int start = r.Position;
        if (r.U32() != ObjectMagic) throw new GameFormatException("Magique d'objet 0xABCD1234 attendu", start);
        byte version = r.U8();
        ulong id = r.U64();
        ulong typeId = r.U64();
        r.U8(); // drapeau (toujours 0 observe)
        int fieldCount = r.Count(r.Data.Length);
        int sizePos = r.Position;
        int size = r.I32();
        r.U8(); // repetition de l'octet de version
        int end = sizePos + 4 + size;

        var fields = new List<RoomField>(fieldCount);
        ulong parent = 0;
        Vector3 pos = Vector3.Zero, scale = Vector3.One, rot = Vector3.Zero;
        Vector4? color = null;
        for (int k = 0; k < fieldCount; k++)
        {
            byte type = r.U8();
            uint hash = r.U32();
            object value;
            switch (type)
            {
                case 0: value = r.U8(); break;
                case 1: value = r.U32(); break;
                case 3: value = r.I32(); break;
                case 4: value = r.F32(); break;
                case 5: value = r.U64(); break;
                case 6: value = new Vector3(r.F32(), r.F32(), r.F32()); break;
                case 7: value = new Vector4(r.F32(), r.F32(), r.F32(), r.F32()); break;
                case 8: value = r.LengthPrefixedUtf16(); break;
                case 9:
                {
                    int n = r.Count(r.Data.Length);
                    byte[] blob = new byte[n];
                    Buffer.BlockCopy(r.Data, r.Position, blob, 0, n);
                    r.Skip(n);
                    value = blob;
                    break;
                }
                default: throw new GameFormatException($"Type de champ inconnu {type} (hash 0x{hash:X8})", r.Position - 5);
            }
            fields.Add(new RoomField(type, hash, value));
            switch (hash)
            {
                case RoomFieldHashes.Parent when value is ulong u: parent = u; break;
                case RoomFieldHashes.Position when value is Vector3 v: pos = v; break;
                case RoomFieldHashes.Scale when value is Vector3 v: scale = v; break;
                case RoomFieldHashes.Rotation when value is Vector3 v: rot = v; break;
                case RoomFieldHashes.LightColor when value is Vector4 c: color = c; break;
            }
        }
        if (r.Position != end) throw new GameFormatException($"Taille d'objet incoherente (attendu fin a 0x{end:X})", r.Position);

        string? path = null;
        if (assets is not null && assets.TryGetValue(typeId, out string? p)) path = p;
        return new RoomInstance
        {
            Id = id,
            TypeId = typeId,
            AssetPath = path,
            Kind = Classify(path),
            Version = version,
            ParentId = parent,
            Position = pos,
            Scale = scale,
            EulerDegrees = rot,
            Color = color,
            Fields = fields,
        };
    }

    /// <summary>Famille d'un asset d'apres son extension.</summary>
    public static InstanceKind Classify(string? assetPath)
    {
        if (assetPath is null) return InstanceKind.Unknown;
        int dot = assetPath.LastIndexOf('.');
        int sep = assetPath.LastIndexOfAny(['\\', '/']);
        if (dot < 0 || dot < sep) return InstanceKind.Other;
        string ext = assetPath[(dot + 1)..].ToLowerInvariant();
        return ext switch
        {
            "gr2" => InstanceKind.Model,
            "lit" => InstanceKind.Light,
            "fxp" => InstanceKind.FxPlaceable,
            "p" => InstanceKind.Portal,
            "trg" => InstanceKind.Trigger,
            "rgn" or "env" => InstanceKind.Region,
            "rbd" => InstanceKind.RoomBound,
            "hms" => InstanceKind.Heightmap,
            "cvr" or "cdr" => InstanceKind.Cover,
            _ when ext.StartsWith("spn", StringComparison.Ordinal) => InstanceKind.Spawn,
            "box" or "cam" or "ext" or "wtr" or "wws" or "mir" or "fla" or "bil" or "bon" or "zzp" or "fol" or "phy" or "phj"
                or "amk" or "pfn" or "sfq" or "mpn" => InstanceKind.Engine,
            "stg" or "enc" or "apl" => InstanceKind.GameData,
            _ => InstanceKind.Other,
        };
    }
}

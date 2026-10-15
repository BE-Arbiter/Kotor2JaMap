using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Kotor.Core.Mdl;

/// <summary>
/// Lecteur MDL/MDX binaire KOTOR 1 PC. Tous les offsets internes du MDL sont relatifs a l'octet 12 ; les offsets MDX
/// ne le sont pas. Layouts : xoreos model_kotor.cpp (Model_KotOR::load, ModelNode_KotOR::load/readMesh/
/// readNodeControllers) et KotorBlender io_scene_kotor/format/mdl/reader.py (+ types.py), recoupes sur les fichiers
/// reels. Les tailles et offsets sont documentes dans les constantes ci-dessous.
/// </summary>
public static class MdlReader
{
    // Fichier
    private const int FileHeader = 12;              // 0, mdlDataSize, mdxDataSize ; offsets internes relatifs a 12
    // En-tete de geometrie + de modele (relatifs a 12)
    private const int GeoFnPtr = 0;                 // pointeur de fonction 1
    private const int GeoName = 8;                  // 32 octets
    private const int GeoRootNode = 40;
    private const int ModelBBoxMin = 104;           // 6 floats
    private const int ModelSuperName = 136;         // 32 octets
    private const int ModelNameArray = 184;         // offset, count
    private const int ModelHeaderSize = 192;
    // First function pointer of the geometry header: K1 PC 4273776, K2 PC 4285200 (KotorBlender types.py MODEL_FN_PTR_1_*; measured on
    // real K2 models: 4285200 and, second pointer, 4216320 -- K1 PC second pointer is 4216096).
    private const uint FnPtrK1Pc = 4273776, FnPtrK2Pc = 4285200;
    // KOTOR 2 mesh header: 8 more bytes (dirt fields) before the MDX data offset and the vertex array offset (xoreos readMesh: "if kotor2 skip 8").
    // Measured on 001ebo room models: with +8 the MDL positions equal the MDX positions for every mesh, with +0 for none.
    private const int K2MeshExtra = 8;
    // Noeud (relatifs au debut du noeud)
    private const int NodeFlags = 0, NodeNameIndex = 6, NodePosition = 16, NodeOrientation = 28; // quaternion w,x,y,z
    private const int NodeChildren = 44, NodeCtrlKeys = 56, NodeCtrlData = 68, NodeHeaderSize = 80;
    // En-tete de maillage (relatifs au debut de l'en-tete de maillage = noeud + 80)
    private const int MeshFaces = 8;                // array def (offset, count, count) -> faces de 32 octets
    private const int MeshTransparency = 84;
    private const int MeshTexture0 = 88, MeshTexture1 = 120;  // 32 octets chacune
    private const int MeshMdxVertexSize = 252;
    private const int MeshMdxOffNormals = 264, MeshMdxOffUv0 = 272, MeshMdxOffUv1 = 276;
    private const int MeshVertexCount = 304, MeshTextureCount = 306, MeshHasLightmap = 308, MeshRender = 313;
    private const int MeshMdxDataOffset = 324, MeshVertexArrayOffset = 328;
    private const int MeshHeaderSize = 332;         // K1 PC
    private const int FaceSize = 32, FaceIndices = 26;  // normal 12, distance 4, materiau 4, adjacents 6, indices 3 x u16

    private const int MaxDepth = 512;

    public static KotorModel Read(byte[] mdl, byte[] mdx)
    {
        ArgumentNullException.ThrowIfNull(mdl);
        ArgumentNullException.ThrowIfNull(mdx);
        var ctx = new Ctx(mdl, mdx);
        ctx.Need(0, FileHeader + ModelHeaderSize, "header");
        if (ctx.U32(0) != 0) throw ctx.Bad("ASCII MDL is not supported");

        uint fn = ctx.U32(FileHeader + GeoFnPtr);
        if (fn != FnPtrK1Pc && fn != FnPtrK2Pc)
            throw new NotSupportedException($"Unknown MDL function pointer ({fn}): only KOTOR 1 PC and KOTOR 2 PC models are read.");
        ctx.K2 = fn == FnPtrK2Pc;

        ctx.ModelName = ctx.FixedString(FileHeader + GeoName, 32);
        int root = ctx.Off(FileHeader + GeoRootNode, "root node");
        var bmin = ctx.Vec3(FileHeader + ModelBBoxMin);
        var bmax = ctx.Vec3(FileHeader + ModelBBoxMin + 12);
        string super = ctx.FixedString(FileHeader + ModelSuperName, 32);

        int nameOff = ctx.Off(FileHeader + ModelNameArray, "name table");
        int nameCount = ctx.Count(FileHeader + ModelNameArray + 4, "name count");
        ctx.Need(FileHeader + nameOff, nameCount * 4L, "name table");
        ctx.Names = new string[nameCount];
        for (int i = 0; i < nameCount; i++)
            ctx.Names[i] = ctx.CString(FileHeader + ctx.Off(FileHeader + nameOff + i * 4, "name"));

        var rootNode = ReadNode(ctx, root, 0);
        string? superName = string.IsNullOrEmpty(super) || super.Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null : super.ToLowerInvariant();
        return new KotorModel(ctx.ModelName, superName, rootNode, bmin, bmax);
    }

    private static KotorNode ReadNode(Ctx c, int nodeOff, int depth)
    {
        if (depth > MaxDepth) throw c.Bad("hierarchy too deep");
        int b = FileHeader + nodeOff;
        c.Need(b, NodeHeaderSize, "node @" + nodeOff);
        ushort flags = c.U16(b + NodeFlags);
        int nameIdx = c.U16(b + NodeNameIndex);
        if (nameIdx >= c.Names.Length) throw c.Bad($"node @{nodeOff}: name index {nameIdx} out of range");
        string name = c.Names[nameIdx];
        c.NodeName = name;
        var kind = (NodeKind)flags;

        Vector3 pos = c.Vec3(b + NodePosition);
        float qw = c.F32(b + NodeOrientation), qx = c.F32(b + NodeOrientation + 4),
              qy = c.F32(b + NodeOrientation + 8), qz = c.F32(b + NodeOrientation + 12);
        var rot = new Quaternion(qx, qy, qz, qw);

        int childOff = c.Off(b + NodeChildren, "children");
        int childCount = c.Count(b + NodeChildren + 4, "child count");
        int keyOff = c.Off(b + NodeCtrlKeys, "controller keys");
        int keyCount = c.Count(b + NodeCtrlKeys + 4, "controller count");
        int dataOff = c.Off(b + NodeCtrlData, "controller data");
        int dataCount = c.Count(b + NodeCtrlData + 4, "controller data count");

        var lv = (kind & NodeKind.Light) != 0 ? new LightValues() : null;
        ReadControllers(c, keyOff, keyCount, dataOff, dataCount, ref pos, ref rot, lv);

        KotorMesh? mesh = null;
        if ((kind & NodeKind.Mesh) != 0)
        {
            // Light/Emitter/Reference ne se combinent pas avec Mesh dans les fichiers du jeu : l'en-tete de maillage
            // suit alors directement l'en-tete de noeud. Une combinaison inconnue serait mal lue, donc refusee.
            if ((kind & (NodeKind.Light | NodeKind.Emitter | NodeKind.Reference)) != 0)
                throw c.Bad($"unsupported flag combination ({flags:X4})");
            mesh = ReadMesh(c, b + NodeHeaderSize);
        }

        KotorLight? light = null;
        if (lv is not null)
        {
            // Light header (92 bytes, right after the node header; KotorBlender reader.py): flare_radius f32, 5 array definitions
            // of 12 bytes, then 7 u32: priority, ambient_only, dynamic_type, affect_dynamic, shadow, flare, fading_light.
            c.Need(b + NodeHeaderSize, LightHeaderSize, "light header of " + name);
            bool ambientOnly = c.U32(b + NodeHeaderSize + LightAmbientOnly) != 0;
            light = new KotorLight(lv.Color ?? Vector3.One, lv.Radius ?? DefaultLightRadius, lv.Multiplier ?? 1f,
                lv.Color is null || lv.Radius is null || lv.Multiplier is null, ambientOnly, lv.Invalid);
        }
        var node = new KotorNode(name, kind, pos, rot, mesh, light);
        c.Need(FileHeader + childOff, childCount * 4L, "children of " + name);
        for (int i = 0; i < childCount; i++)
        {
            int co = c.Off(FileHeader + childOff + i * 4, "child");
            node.AddChild(ReadNode(c, co, depth + 1));
        }
        return node;
    }

    /// <summary>Default radius (metres) of a light node without a radius controller.</summary>
    public const float DefaultLightRadius = 5f;
    // Light controllers (xoreos kControllerTypeColor/Radius/Multiplier). Only meaningful on Light nodes (88 and 140 collide with emitter controllers).
    private const int LightHeaderSize = 92, LightAmbientOnly = 4 + 5 * 12 + 4;   // flare_radius, 5 array defs, then priority
    private const uint CtrlLightColor = 76, CtrlLightRadius = 88, CtrlLightMultiplier = 140;

    private sealed class LightValues { public Vector3? Color; public float? Radius; public float? Multiplier; public bool Invalid; }

    private static void ReadControllers(Ctx c, int keyOff, int keyCount, int dataOff, int dataCount,
        ref Vector3 pos, ref Quaternion rot, LightValues? light)
    {
        if (keyCount == 0) return;
        c.Need(FileHeader + keyOff, keyCount * 16L, "controllers");
        c.Need(FileHeader + dataOff, dataCount * 4L, "controller data");
        int d0 = FileHeader + dataOff;
        for (int i = 0; i < keyCount; i++)
        {
            int k = FileHeader + keyOff + i * 16;
            uint type = c.U32(k);
            int rows = c.U16(k + 6), timeIdx = c.U16(k + 8), valIdx = c.U16(k + 10);
            int cols = c.Bytes[k + 12];
            if (rows == 0) continue;
            if (light is not null && type is CtrlLightColor or CtrlLightRadius or CtrlLightMultiplier)
            {
                int n = type == CtrlLightColor ? 3 : 1;
                if ((cols & 0xF) < n) continue;
                if (valIdx + (long)n > dataCount) { light.Invalid = true; continue; }   // only this light is skipped (LIGHT_INVALID)
                int o = d0 + valIdx * 4;
                if (type == CtrlLightColor) light.Color = new Vector3(c.F32(o), c.F32(o + 4), c.F32(o + 8));
                else if (type == CtrlLightRadius) light.Radius = c.F32(o);
                else light.Multiplier = c.F32(o);
                continue;
            }
            if (type != 8 && type != 20) continue;
            // Premiere image cle uniquement.
            int nVals = type == 20 && cols == 2 ? 1 : (cols & 0xF);
            if (valIdx + (long)nVals > dataCount || timeIdx + 1L > dataCount)
                throw c.Bad($"controller {type} out of range");
            if (type == 8)
            {
                if ((cols & 0xF) != 3) continue;      // xoreos : colonnes 3 ou 19 (bezier)
                pos = c.Vec3(d0 + valIdx * 4);
            }
            else if (cols == 2)
                rot = DecodeCompressedQuaternion(c.U32(d0 + valIdx * 4));
            else if (cols == 4)
            {
                int o = d0 + valIdx * 4;
                rot = new Quaternion(c.F32(o), c.F32(o + 4), c.F32(o + 8), c.F32(o + 12));
            }
        }
    }

    /// <summary>
    /// Decodage 32 bits (xoreos readOrientationController, PyKotor Vector4.from_compressed) : x, y sur 11 bits,
    /// z sur 10 bits ; w = -sqrt(1 - |v|^2). Les deux references fixent w negatif ; le quaternion est ensuite
    /// canonicalise (w >= 0, en niant les 4 composantes, ce qui represente la meme rotation).
    /// </summary>
    public static Quaternion DecodeCompressedQuaternion(uint t)
    {
        float x = 1f - (t & 0x7FF) / 1023f;
        float y = 1f - ((t >> 11) & 0x7FF) / 1023f;
        float z = 1f - (t >> 22) / 511f;
        float w;
        float m2 = x * x + y * y + z * z;
        if (m2 < 1f) w = -MathF.Sqrt(1f - m2);
        else
        {
            float m = MathF.Sqrt(m2);
            x /= m; y /= m; z /= m; w = 0f;
        }
        if (w < 0f) { x = -x; y = -y; z = -z; w = -w; }
        return new Quaternion(x, y, z, w);
    }

    private static KotorMesh ReadMesh(Ctx c, int m)
    {
        int k2 = c.K2 ? K2MeshExtra : 0;
        c.Need(m, MeshHeaderSize + k2, "mesh header of " + c.NodeName);
        int faceOff = c.Off(m + MeshFaces, "faces");
        int faceCount = c.Count(m + MeshFaces + 4, "face count");
        int transparency = (int)c.U32(m + MeshTransparency);
        string? tex0 = Tex(c.FixedString(m + MeshTexture0, 32));
        string? tex1 = Tex(c.FixedString(m + MeshTexture1, 32));
        // 0xFFFFFFFF = pas de donnees MDX pour ce maillage (observe sur 35 modeles de models.bif) : positions du MDL seules.
        uint mdxStrideRaw = c.U32(m + MeshMdxVertexSize);
        bool hasMdx = mdxStrideRaw != uint.MaxValue && mdxStrideRaw != 0;
        if (hasMdx && mdxStrideRaw > 4096) throw c.Bad($"invalid MDX vertex size ({mdxStrideRaw})");
        int mdxStride = hasMdx ? (int)mdxStrideRaw : 0;
        int offNormals = c.I32(m + MeshMdxOffNormals);
        int offUv0 = c.I32(m + MeshMdxOffUv0);
        int offUv1 = c.I32(m + MeshMdxOffUv1);
        int vertCount = c.U16(m + MeshVertexCount);
        bool hasLightmap = c.Bytes[m + MeshHasLightmap] != 0;
        bool render = c.Bytes[m + MeshRender] != 0;
        long mdxOff = c.U32(m + MeshMdxDataOffset + k2);
        int vertArr = c.Off(m + MeshVertexArrayOffset + k2, "vertices");

        var tris = new int[faceCount * 3];
        if (faceCount > 0)
        {
            c.Need(FileHeader + faceOff, faceCount * (long)FaceSize, "faces of " + c.NodeName);
            for (int i = 0; i < faceCount; i++)
            {
                int f = FileHeader + faceOff + i * FaceSize + FaceIndices;
                for (int j = 0; j < 3; j++)
                {
                    int idx = c.U16(f + j * 2);
                    if (idx >= vertCount) throw c.Bad($"vertex index {idx} >= {vertCount}");
                    tris[i * 3 + j] = idx;
                }
            }
        }

        var positions = new Vector3[vertCount];
        Vector3[]? normals = null;
        Vector2[]? uv0 = null, uv1 = null;
        if (hasMdx) { if (offNormals >= 0) normals = new Vector3[vertCount]; if (offUv0 >= 0) uv0 = new Vector2[vertCount]; if (offUv1 >= 0) uv1 = new Vector2[vertCount]; }
        if (vertCount > 0)
        {
            c.Need(FileHeader + vertArr, vertCount * 12L, "positions of " + c.NodeName);
            for (int i = 0; i < vertCount; i++) positions[i] = c.Vec3(FileHeader + vertArr + i * 12);

            if (hasMdx) c.NeedMdx(mdxOff, (long)vertCount * mdxStride, "MDX data");
            if ((normals is not null && offNormals + 12 > mdxStride) || (uv0 is not null && offUv0 + 8 > mdxStride)
                || (uv1 is not null && offUv1 + 8 > mdxStride))
                throw c.Bad("MDX offset outside the vertex size");
            for (int i = 0; i < vertCount; i++)
            {
                long v = mdxOff + (long)i * mdxStride;
                if (normals is not null)
                    normals[i] = new Vector3(c.MdxF32(v + offNormals), c.MdxF32(v + offNormals + 4), c.MdxF32(v + offNormals + 8));
                if (uv0 is not null) uv0[i] = new Vector2(c.MdxF32(v + offUv0), c.MdxF32(v + offUv0 + 4));
                if (uv1 is not null) uv1[i] = new Vector2(c.MdxF32(v + offUv1), c.MdxF32(v + offUv1 + 4));
            }
        }
        if (hasLightmap && uv1 is null) hasLightmap = false;   // drapeau present mais pas de donnees UV de lightmap
        return new KotorMesh(tex0, tex1, render, hasLightmap, transparency, positions, normals, uv0, uv1, tris);
    }

    private static string? Tex(string s)
    {
        s = s.Trim().ToLowerInvariant();
        return s.Length == 0 || s == "null" ? null : s;
    }

    private sealed class Ctx
    {
        public readonly byte[] Bytes;
        public readonly byte[] Mdx;
        public string ModelName = "?";
        public string NodeName = "?";
        public string[] Names = Array.Empty<string>();
        public bool K2;

        public Ctx(byte[] mdl, byte[] mdx) { Bytes = mdl; Mdx = mdx; }

        public InvalidDataException Bad(string msg) =>
            new($"MDL '{ModelName}', node '{NodeName}': {msg}");

        public void Need(long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > Bytes.Length)
                throw Bad($"{what} outside the file (offset {offset}, size {length}, file {Bytes.Length})");
        }

        public void NeedMdx(long offset, long length, string what)
        {
            if (offset < 0 || length < 0 || offset + length > Mdx.Length)
                throw Bad($"{what} outside the MDX (offset {offset}, size {length}, MDX {Mdx.Length})");
        }

        public ushort U16(long o) { Need(o, 2, "u16"); return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan((int)o)); }
        public uint U32(long o) { Need(o, 4, "u32"); return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan((int)o)); }
        public int I32(long o) { Need(o, 4, "i32"); return BinaryPrimitives.ReadInt32LittleEndian(Bytes.AsSpan((int)o)); }
        public float F32(long o) { Need(o, 4, "f32"); return BinaryPrimitives.ReadSingleLittleEndian(Bytes.AsSpan((int)o)); }
        public float MdxF32(long o) { NeedMdx(o, 4, "f32"); return BinaryPrimitives.ReadSingleLittleEndian(Mdx.AsSpan((int)o)); }
        public Vector3 Vec3(long o) => new(F32(o), F32(o + 4), F32(o + 8));

        /// <summary>Offset (relatif a 12) stocke en u32 ; refuse les valeurs hors du fichier.</summary>
        public int Off(long at, string what)
        {
            uint v = U32(at);
            if (v > Bytes.Length) throw Bad($"{what}: offset {v} outside the file");
            return (int)v;
        }

        public int Count(long at, string what)
        {
            uint v = U32(at);
            if (v > int.MaxValue / 64) throw Bad($"{what}: invalid count {(int)v}");
            return (int)v;
        }

        public string FixedString(long o, int len)
        {
            Need(o, len, "string");
            int n = Array.IndexOf(Bytes, (byte)0, (int)o, len);
            int end = n < 0 ? (int)o + len : n;
            return Encoding.ASCII.GetString(Bytes, (int)o, end - (int)o);
        }

        public string CString(long o)
        {
            Need(o, 1, "string");
            int n = Array.IndexOf(Bytes, (byte)0, (int)o);
            if (n < 0) throw Bad("unterminated string");
            return Encoding.ASCII.GetString(Bytes, (int)o, n - (int)o);
        }
    }
}

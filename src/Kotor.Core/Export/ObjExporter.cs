using System.Globalization;
using System.Text;
using Kotor.Core.Scene;

namespace Kotor.Core.Export;

/// <summary>
/// OBJ de controle visuel : metres KOTOR, Z vers le haut, un groupe par Room/Node, UV0 seulement (v conserve : OBJ et KOTOR
/// partagent la convention GL), un materiau par texture. Le walkmesh va dans &lt;module&gt;_walkmesh.obj.
/// </summary>
public static class ObjExporter
{
    public static void Export(ModuleScene scene, string outDir)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Directory.CreateDirectory(outDir);
        string texDir = Path.Combine(outDir, "textures");
        Directory.CreateDirectory(texDir);
        string name = scene.Info.Module;

        var used = scene.Meshes.Select(m => m.Texture).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        foreach (string t in used)
        {
            scene.Textures.TryGetValue(t, out var tex);
            using var fs = File.Create(Path.Combine(texDir, Safe(t) + ".png"));
            if (tex is null) PngWriter.WriteRgba(fs, 8, 8, Magenta());
            else PngWriter.WriteRgba(fs, tex.Width, tex.Height, tex.Rgba);
        }

        var mtl = new StringBuilder();
        foreach (string t in used)
            mtl.Append("newmtl ").Append(Safe(t)).Append("\nKd 1 1 1\nmap_Kd textures/").Append(Safe(t)).Append(".png\n\n");
        File.WriteAllText(Path.Combine(outDir, name + ".mtl"), mtl.ToString());

        var obj = new StringBuilder();
        obj.Append("# KOTOR ").Append(name).Append(" (metres, Z up)\nmtllib ").Append(name).Append(".mtl\n");
        int baseIdx = 1;
        foreach (var m in scene.Meshes)
        {
            obj.Append("g ").Append(Safe(m.Room)).Append('/').Append(Safe(m.Node)).Append('\n');
            obj.Append("usemtl ").Append(Safe(m.Texture)).Append('\n');
            foreach (var p in m.Positions) obj.Append("v ").Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append('\n');
            foreach (var u in m.Uv0) obj.Append("vt ").Append(F(u.X)).Append(' ').Append(F(u.Y)).Append('\n');
            for (int i = 0; i + 2 < m.Triangles.Length; i += 3)
            {
                obj.Append('f');
                for (int k = 0; k < 3; k++) { int ix = m.Triangles[i + k] + baseIdx; obj.Append(' ').Append(ix).Append('/').Append(ix); }
                obj.Append('\n');
            }
            baseIdx += m.Positions.Length;
        }
        File.WriteAllText(Path.Combine(outDir, name + ".obj"), obj.ToString());

        if (scene.Walkmeshes.Count == 0) return;
        var w = new StringBuilder("# Walkmesh (one group per room; usemtl walk = walkable, nowalk = not walkable)\n");
        int wb = 1;
        foreach (var wm in scene.Walkmeshes)
        {
            w.Append("g ").Append(Safe(wm.Room)).Append('\n');
            foreach (var p in wm.Positions) w.Append("v ").Append(F(p.X)).Append(' ').Append(F(p.Y)).Append(' ').Append(F(p.Z)).Append('\n');
            for (int i = 0; i < wm.Triangles.Length / 3; i++)
            {
                w.Append("usemtl ").Append(wm.Walkable[i] ? "walk" : "nowalk").Append('\n');
                w.Append("f ").Append(wm.Triangles[i * 3] + wb).Append(' ').Append(wm.Triangles[i * 3 + 1] + wb)
                 .Append(' ').Append(wm.Triangles[i * 3 + 2] + wb).Append('\n');
            }
            wb += wm.Positions.Length;
        }
        File.WriteAllText(Path.Combine(outDir, name + "_walkmesh.obj"), w.ToString());
    }

    private static string F(float v) => v.ToString("0.#####", CultureInfo.InvariantCulture);

    private static string Safe(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    private static byte[] Magenta()
    {
        var b = new byte[8 * 8 * 4];
        for (int i = 0; i < b.Length; i += 4) { b[i] = 255; b[i + 2] = 255; b[i + 3] = 255; }
        return b;
    }
}

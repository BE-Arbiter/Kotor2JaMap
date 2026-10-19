using System.Numerics;
using System.Text;
using Jk.Core.Text;

namespace Jk.Core.Models;

/// <summary>
/// Image cle d un noeud : numero d image, drapeaux, position et pitch/yaw/roll (degres) ABSOLUS (locaux au parent), puis la variation PAR IMAGE
/// (deuxieme ligne de l entree : dx dy dz dp dy dr). Constat sur les donnees : variation x nombre d images jusqu a l entree suivante = difference
/// des deux entrees (modulo 360 pour les angles) ; elle donne le sens de rotation quand l ecart depasse 180 degres.
/// </summary>
public readonly record struct KeyEntry(int Frame, uint Flags, Vector3 Position, Vector3 PitchYawRoll,
    Vector3 DeltaPosition = default, Vector3 DeltaPitchYawRoll = default);

/// <summary>Piste d'un noeud (identifie par le nom de son maillage / noeud du 3DO) : images cles triees par numero d'image.</summary>
public sealed record KeyNode(int Index, string MeshName, IReadOnlyList<KeyEntry> Entries)
{
    /// <summary>
    /// Pose a l image <paramref name="frame"/> : position interpolee lineairement ; angles interpoles lineairement par le plus court chemin
    /// (350 -> 10 passe par 0), sauf si la variation par image de l entree indique des tours supplementaires (0 -> 270 en 3 images a 90 par image). Avant la premiere image cle et apres la derniere, la pose du bord est maintenue.
    /// </summary>
    public (Vector3 Position, Vector3 PitchYawRoll) PoseAt(float frame)
    {
        if (Entries.Count == 0) return (Vector3.Zero, Vector3.Zero);
        if (frame <= Entries[0].Frame) return (Entries[0].Position, Entries[0].PitchYawRoll);
        KeyEntry last = Entries[^1];
        if (frame >= last.Frame) return (last.Position, last.PitchYawRoll);
        int i = 1;
        while (Entries[i].Frame < frame) i++;
        KeyEntry a = Entries[i - 1], b = Entries[i];
        float span = b.Frame - a.Frame, t = (frame - a.Frame) / span;
        return (Vector3.Lerp(a.Position, b.Position, t),
            new Vector3(LerpAngle(a.PitchYawRoll.X, b.PitchYawRoll.X, a.DeltaPitchYawRoll.X * span, t),
                LerpAngle(a.PitchYawRoll.Y, b.PitchYawRoll.Y, a.DeltaPitchYawRoll.Y * span, t),
                LerpAngle(a.PitchYawRoll.Z, b.PitchYawRoll.Z, a.DeltaPitchYawRoll.Z * span, t)));
    }

    private static float LerpAngle(float a, float b, float expectedTotal, float t)
    {
        float d = b - a;
        d -= 360f * MathF.Round(d / 360f);
        // Tours entiers suggeres par la variation de l entree (nombre entier de tours seulement : les extremites restent exactes).
        d += 360f * MathF.Round((expectedTotal - d) / 360f);
        return a + d * t;
    }
}

/// <summary>Animation <c>.key</c> : nombre d'images, images par seconde, une piste par noeud.</summary>
public sealed record KeyAnimation(uint Flags, float Fps, int Frames, IReadOnlyList<KeyNode> Nodes)
{
    /// <summary>Marqueurs (section <c>MARKERS</c>) : temps en images et type (pas de pas, impacts...). Non utilises par la conversion.</summary>
    public IReadOnlyList<(float Time, int Type)> Markers { get; init; } = [];
}

/// <summary>
/// Lecteur de <c>.key</c> (texte Latin-1). <c>HEADER</c> : <c>FLAGS</c>, <c>TYPE</c>, <c>FRAMES</c>, <c>FPS</c>, <c>JOINTS</c> ;
/// <c>KEYFRAME NODES</c> : par <c>NODE n</c>, <c>MESH NAME</c>, <c>ENTRIES k</c>, puis par entree deux lignes : <c>idx: image drapeaux x y z p y r</c>
/// et leurs variations par image (<c>dx dy dz dp dy dr</c>, lues : elles donnent le sens et le nombre de tours des rotations). Voir <c>docs/superpowers/specs/jk-formats.md</c>.
/// </summary>
public static class KeyReader
{
    public static string Decode(byte[] data) => Encoding.Latin1.GetString(data);

    public static KeyAnimation Read(string text)
    {
        uint flags = 0;
        float fps = 0;
        int frames = -1;
        var nodes = new List<KeyNode>();
        string? meshName = null;
        int nodeIndex = -1, declaredEntries = -1;
        var entries = new List<KeyEntry>();
        bool sawHeader = false, inMarkers = false;
        var markers = new List<(float, int)>();

        void Close()
        {
            if (nodeIndex < 0) return;
            if (declaredEntries >= 0 && entries.Count != declaredEntries)
                throw new InvalidDataException($"KEY node {nodeIndex}: {entries.Count} entries read, {declaredEntries} declared.");
            nodes.Add(new KeyNode(nodeIndex, meshName ?? "", [.. entries.OrderBy(e => e.Frame)]));
            nodeIndex = -1; entries.Clear();
        }

        foreach (var (number, line) in JkTokenizer.Lines(text))
        {
            try
            {
                string[] t = JkTokenizer.Split(line);
                if (JkTokenizer.TryStripIndex(line, out _, out string rest))
                {
                    string[] v = JkTokenizer.Split(rest);
                    if (nodeIndex < 0 || v.Length < 8) throw new InvalidDataException("entry outside a NODE or with fewer than 8 fields");
                    entries.Add(new KeyEntry(JkTokenizer.ParseInt(v[0]), JkTokenizer.ParseFlags(v[1]),
                        new Vector3(JkTokenizer.ParseFloat(v[2]), JkTokenizer.ParseFloat(v[3]), JkTokenizer.ParseFloat(v[4])),
                        new Vector3(JkTokenizer.ParseFloat(v[5]), JkTokenizer.ParseFloat(v[6]), JkTokenizer.ParseFloat(v[7]))));
                    continue;
                }
                switch (t[0].ToUpperInvariant())
                {
                    case "SECTION:":
                        if (t.Length > 1 && t[1].Equals("HEADER", StringComparison.OrdinalIgnoreCase)) sawHeader = true;
                        inMarkers = t.Length > 1 && t[1].Equals("MARKERS", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "MARKERS": break;
                    case "FLAGS": flags = JkTokenizer.ParseFlags(t[1]); break;
                    case "TYPE": case "JOINTS": case "NODES": break;
                    case "FRAMES": frames = JkTokenizer.ParseInt(t[1]); break;
                    case "FPS": fps = JkTokenizer.ParseFloat(t[1]); break;
                    case "NODE": Close(); nodeIndex = JkTokenizer.ParseInt(t[1]); meshName = null; declaredEntries = -1; break;
                    case "MESH": meshName = t.Length > 2 ? string.Join(' ', t.Skip(2)) : ""; break;
                    case "ENTRIES": declaredEntries = JkTokenizer.ParseInt(t[1]); break;
                    default:
                        if (inMarkers && t.Length == 2) { markers.Add((JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseInt(t[1]))); break; }
                        // Ligne de variations (dx dy dz dp dy dr) : six nombres, sans prefixe d'index.
                        if (t.Length == 6 && nodeIndex >= 0 && entries.Count > 0 && float.TryParse(t[0], System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out _))
                        {
                            KeyEntry e = entries[^1];
                            entries[^1] = e with
                            {
                                DeltaPosition = new Vector3(JkTokenizer.ParseFloat(t[0]), JkTokenizer.ParseFloat(t[1]), JkTokenizer.ParseFloat(t[2])),
                                DeltaPitchYawRoll = new Vector3(JkTokenizer.ParseFloat(t[3]), JkTokenizer.ParseFloat(t[4]), JkTokenizer.ParseFloat(t[5])),
                            };
                            break;
                        }
                        throw new InvalidDataException("unexpected line");
                }
            }
            catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException or InvalidDataException)
            {
                throw new InvalidDataException($"KEY line {number}: cannot parse '{line}' ({ex.Message}).", ex);
            }
        }
        Close();
        if (!sawHeader || frames < 0 || fps <= 0) throw new InvalidDataException("Not a KEY file (missing HEADER, FRAMES or FPS).");
        if (nodes.Count == 0) throw new InvalidDataException("KEY file without nodes.");
        return new KeyAnimation(flags, fps, frames, nodes) { Markers = markers };
    }
}

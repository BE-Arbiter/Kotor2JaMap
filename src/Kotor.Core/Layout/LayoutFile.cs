using System.Globalization;
using System.Numerics;

namespace Kotor.Core.Layout;

/// <summary>
/// Fichier LYT (texte). Les pieces et les doorhooks sont lus ; les sections track et obstacle sont ignorees.
/// Ligne de doorhook : <c>room name 0 x y z qw qx qy qz</c> : w en PREMIER, constate sur le vrai m02aa.lyt (identite = "1 0 0 0", porte a 45 degres = "0.9239 0 0 0.3827" et Bearing GIT 0.785).
/// </summary>
public sealed class LayoutFile
{
    public IReadOnlyList<LayoutRoom> Rooms { get; }
    public IReadOnlyList<LayoutDoorHook> DoorHooks { get; }

    private LayoutFile(List<LayoutRoom> rooms, List<LayoutDoorHook> hooks)
    {
        Rooms = rooms;
        DoorHooks = hooks;
    }

    public static LayoutFile Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var rooms = new List<LayoutRoom>();
        var hooks = new List<LayoutDoorHook>();
        string section = "";
        int remaining = 0;
        foreach (string raw in text.Split('\n'))
        {
            string[] t = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length == 0) continue;
            string head = t[0].ToLowerInvariant();
            if (head is "roomcount" or "trackcount" or "obstaclecount" or "doorhookcount")
            {
                section = head;
                remaining = t.Length > 1 ? int.Parse(t[1], CultureInfo.InvariantCulture) : 0;
                continue;
            }
            if (head is "beginlayout" or "donelayout") { section = ""; continue; }
            if (remaining <= 0) continue;
            remaining--;
            if (section == "roomcount")
            {
                if (t.Length < 4) throw new InvalidDataException($"Invalid LYT room line: '{raw.Trim()}'.");
                rooms.Add(new LayoutRoom(head, new Vector3(F(t[1]), F(t[2]), F(t[3]))));
            }
            else if (section == "doorhookcount")
            {
                if (t.Length < 10) throw new InvalidDataException($"Invalid LYT doorhook line: '{raw.Trim()}'.");
                hooks.Add(new LayoutDoorHook(
                    t[1].ToLowerInvariant(), head,
                    new Vector3(F(t[3]), F(t[4]), F(t[5])),
                    new Quaternion(F(t[7]), F(t[8]), F(t[9]), F(t[6]))));  // fichier : w x y z
            }
        }
        return new LayoutFile(rooms, hooks);
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

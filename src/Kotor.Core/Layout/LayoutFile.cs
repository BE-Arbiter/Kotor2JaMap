using System.Globalization;
using System.Numerics;

namespace Kotor.Core.Layout;

/// <summary>
/// Fichier LYT (texte). Les pieces et les doorhooks sont lus ; les sections track et obstacle sont ignorees.
/// Ligne de doorhook : <c>room name 0 x y z qx qy qz qw</c> (ordre confirme par PyKotor io_lyt.py).
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
                if (t.Length < 4) throw new InvalidDataException($"Ligne de piece LYT invalide : '{raw.Trim()}'.");
                rooms.Add(new LayoutRoom(head, new Vector3(F(t[1]), F(t[2]), F(t[3]))));
            }
            else if (section == "doorhookcount")
            {
                if (t.Length < 10) throw new InvalidDataException($"Ligne de doorhook LYT invalide : '{raw.Trim()}'.");
                hooks.Add(new LayoutDoorHook(
                    t[1].ToLowerInvariant(), head,
                    new Vector3(F(t[3]), F(t[4]), F(t[5])),
                    new Quaternion(F(t[6]), F(t[7]), F(t[8]), F(t[9]))));
            }
        }
        return new LayoutFile(rooms, hooks);
    }

    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}

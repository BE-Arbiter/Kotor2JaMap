using System.Globalization;
using System.Numerics;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>Detection des parties d'un assemblage dont les sols se recouvrent (repere assemble).</summary>
public static class OverlapChecker
{
    /// <summary>
    /// Une entree par paire de scenes (A &lt; B) dont deux walkmeshes (pieces) ont des boites englobantes de triangles marchables
    /// qui se recoupent. Les boites sont reduites de <paramref name="shrinkMeters"/> sur X et Y seulement (un sol est plat :
    /// le reduire en Z le viderait), ce qui ignore le simple contact a la jonction ; en Z, des intervalles fermes
    /// (deux sols a la meme hauteur se recoupent, deux etages distincts non).
    /// </summary>
    public static IReadOnlyList<(int A, int B, string Detail)> Check(IReadOnlyList<ModuleScene> transformedScenes, float shrinkMeters = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(transformedScenes);
        var boxes = transformedScenes.Select(s => Boxes(s, shrinkMeters)).ToList();
        var result = new List<(int, int, string)>();
        for (int a = 0; a < boxes.Count; a++)
            for (int b = a + 1; b < boxes.Count; b++)
            {
                // Premier recouvrement seulement : une ligne par paire de scenes.
                foreach (var h in Hits(boxes[a], boxes[b]))
                {
                    result.Add((a, b, string.Create(CultureInfo.InvariantCulture,
                        $"{transformedScenes[a].Info.Module} room {h.RoomA} overlaps {transformedScenes[b].Info.Module} room {h.RoomB} ({h.Size.X:0.00} x {h.Size.Y:0.00} m)")));
                    break;
                }
            }
        return result;
    }

    /// <summary>
    /// Surface totale (m², somme sur toutes les paires de pieces) des recouvrements en XY des boites marchables de deux scenes
    /// (repere assemble), avec les memes regles que <see cref="Check"/> (reduction XY, intervalles Z fermes). 0 = aucun recouvrement.
    /// </summary>
    public static float OverlapArea(ModuleScene a, ModuleScene b, float shrinkMeters = 0.5f)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return Hits(Boxes(a, shrinkMeters), Boxes(b, shrinkMeters)).Sum(h => h.Size.X * h.Size.Y);
    }

    private static List<(string Room, (Vector3 Min, Vector3 Max) Box)> Boxes(ModuleScene s, float shrink) => s.Walkmeshes
        .Select(w => (w.Room, Box: WalkableBox(w, shrink)))
        .Where(b => b.Box is not null)
        .Select(b => (b.Room, Box: b.Box!.Value))
        .ToList();

    /// <summary>Paires de pieces (A, B) dont les boites se recoupent, avec la taille XY du recouvrement.</summary>
    private static IEnumerable<(string RoomA, string RoomB, Vector2 Size)> Hits(List<(string Room, (Vector3 Min, Vector3 Max) Box)> a,
        List<(string Room, (Vector3 Min, Vector3 Max) Box)> b)
    {
        foreach (var (ra, ba) in a)
            foreach (var (rb, bb) in b)
            {
                Vector3 min = Vector3.Max(ba.Min, bb.Min), max = Vector3.Min(ba.Max, bb.Max);
                // XY : recouvrement strict ; Z : intervalles fermes.
                if (min.X < max.X && min.Y < max.Y && min.Z <= max.Z) yield return (ra, rb, new Vector2(max.X - min.X, max.Y - min.Y));
            }
    }

    /// <summary>Boite des triangles marchables reduite en XY ; null si aucun triangle marchable ou si la reduction la vide.</summary>
    private static (Vector3 Min, Vector3 Max)? WalkableBox(SceneWalkmesh w, float shrink)
    {
        Vector3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        for (int t = 0; t + 2 < w.Triangles.Length; t += 3)
        {
            if (t / 3 >= w.Walkable.Length || !w.Walkable[t / 3]) continue;
            for (int k = 0; k < 3; k++)
            {
                Vector3 p = w.Positions[w.Triangles[t + k]];
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            any = true;
        }
        if (!any) return null;
        var s = new Vector3(shrink, shrink, 0f);
        min += s;
        max -= s;
        if (min.X >= max.X || min.Y >= max.Y) return null;
        return (min, max);
    }
}

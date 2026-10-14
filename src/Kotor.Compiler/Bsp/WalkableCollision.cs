// Inspiré de la démarche de q3map2 / NetRadiant-custom (brushes de sol à partir du maillage visible, GPL-2.0) : aucun code copié.
// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
using System.Numerics;
using Kotor.Core.Geometry;

namespace Kotor.Compiler.Bsp;

/// <param name="Brushes">Brushes prismatiques du terrain.</param>
/// <param name="WalkableTriangles">Triangles marchables recus.</param>
/// <param name="TrianglesUsed">Triangles restants apres reduction eventuelle.</param>
/// <param name="Reduced">Vrai si les triangles ont du etre simplifies pour passer sous la limite.</param>
/// <param name="LimitReached">Vrai si la limite n'a pas pu etre respectee (les brushes sont alors tronques).</param>
public sealed record WalkableCollisionResult(IReadOnlyList<Brush> Brushes, int WalkableTriangles, int TrianglesUsed,
    bool Reduced, bool LimitReached);

/// <summary>Collision du terrain : brushes prismatiques sur les triangles marchables visibles (positions en unites Quake).</summary>
public static class WalkableCollision
{
    /// <summary>Nombre de brushes maximal compatible avec la limite des cotes de brush (11 cotes par brush), moins ceux deja presents.</summary>
    public static int MaxBrushesFor(Limits limits, int existingBrushes, int margin = 50) =>
        Math.Max(0, Math.Min(limits.MaxBrushes, Limits.MaxBrushSides / 11) - existingBrushes - margin);

    public static WalkableCollisionResult Build(ModelMesh walkable, int maxBrushes, float thickness = BrushBuilder.DefaultThickness,
        Action<string>? log = null)
    {
        int tris = walkable.SubMeshes.Sum(s => s.Triangles.Length / 3);
        if (tris == 0)
        {
            log?.Invoke("Terrain : 0 triangles marchables, 0 brush de collision ajoute.");
            return new WalkableCollisionResult([], 0, 0, false, false);
        }

        ModelMesh work = Weld(walkable);
        int used = work.SubMeshes.Sum(s => s.Triangles.Length / 3);
        bool reduced = false;
        var brushes = BrushBuilder.FromCollision(work, thickness);
        for (int attempt = 0; attempt < 8 && brushes.Count > maxBrushes && maxBrushes > 0; attempt++)
        {
            int target = (int)Math.Max(1, Math.Floor(used * 0.97 * maxBrushes / brushes.Count));
            if (target >= used) target = used - 1;
            work = MeshSimplifier.Reduce(work, target);
            used = work.SubMeshes.Sum(s => s.Triangles.Length / 3);
            reduced = true;
            brushes = BrushBuilder.FromCollision(work, thickness);
        }

        bool limit = brushes.Count > maxBrushes;
        if (limit) brushes = brushes.Take(maxBrushes).ToList();

        log?.Invoke($"Terrain : {tris} triangles marchables, {brushes.Count} brushes de collision ajoutes (epaisseur {thickness}, limite {maxBrushes})"
            + (reduced ? $", terrain reduit de {tris} a {used} triangles pour passer sous la limite" : ", aucune reduction")
            + (limit ? ", LIMITE ATTEINTE : brushes tronques." : "."));
        return new WalkableCollisionResult(brushes, tris, used, reduced, limit);
    }

    /// <summary>Soude les sommets confondus (quantification a 0,01) en un seul sous-maillage, sans triangle degenere.</summary>
    internal static ModelMesh Weld(ModelMesh m)
    {
        var map = new Dictionary<(long, long, long), int>();
        var pos = new List<Vector3>();
        var tri = new List<int>();
        foreach (SubMesh sm in m.SubMeshes)
        {
            var remap = new int[sm.Positions.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                Vector3 p = sm.Positions[i];
                var key = ((long)MathF.Round(p.X * 100f), (long)MathF.Round(p.Y * 100f), (long)MathF.Round(p.Z * 100f));
                if (!map.TryGetValue(key, out int id)) { id = pos.Count; map[key] = id; pos.Add(p); }
                remap[i] = id;
            }
            for (int t = 0; t + 2 < sm.Triangles.Length; t += 3)
            {
                int a = remap[sm.Triangles[t]], b = remap[sm.Triangles[t + 1]], c = remap[sm.Triangles[t + 2]];
                if (a == b || b == c || a == c) continue;
                tri.Add(a); tri.Add(b); tri.Add(c);
            }
        }
        return new ModelMesh
        {
            Name = m.Name,
            SubMeshes = [new SubMesh { MaterialName = "walkable", Positions = pos.ToArray(), Triangles = tri.ToArray() }],
        };
    }
}

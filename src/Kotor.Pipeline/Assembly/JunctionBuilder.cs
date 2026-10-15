using System.Numerics;
using Kotor.Core.Scene;
using Kotor.Core.Textures;

namespace Kotor.Pipeline.Assembly;

/// <summary>Couloir de raccord entre deux cadres de porte face a face (repere assemble).</summary>
public static class JunctionBuilder
{
    public const float MinGap = 0.01f;

    /// <summary>
    /// Prisme entre <paramref name="a"/> et <paramref name="b"/> : sol, deux murs, plafond, normales vers l'interieur, une seule texture,
    /// sans lightmap. Largeur et hauteur = minimum des deux cadres. Renvoie <c>(null, null)</c> si les cadres sont confondus.
    /// Le walkmesh porte un booleen par triangle : le sol est marchable, murs et plafond solides (restent en collision).
    /// </summary>
    public static (SceneMesh? Mesh, SceneWalkmesh? Walk) Build(DoorFrame a, DoorFrame b, string wallTexture)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(wallTexture);
        if (Vector3.Distance(a.Center, b.Center) < MinGap) return (null, null);

        var dir = new Vector3(b.Center.X - a.Center.X, b.Center.Y - a.Center.Y, 0);
        if (dir.LengthSquared() < MinGap * MinGap) return (null, null); // cadres l'un au-dessus de l'autre : pas de couloir horizontal
        dir = Vector3.Normalize(dir);
        var side = new Vector3(-dir.Y, dir.X, 0) * (MathF.Min(a.Width, b.Width) / 2f);
        float h = MathF.Min(a.Height, b.Height);
        var up = new Vector3(0, 0, h);

        Vector3 aL = a.Center - side, aR = a.Center + side, bL = b.Center - side, bR = b.Center + side;
        var pos = new List<Vector3>();
        var nor = new List<Vector3>();
        var uv = new List<Vector2>();
        var tri = new List<int>();
        var walkable = new List<bool>();

        float length = Vector3.Distance(a.Center, b.Center), width = side.Length() * 2f;
        // Chaque face a ses propres sommets (normales franches). Les normales pointent vers l'axe central du prisme.
        Quad(aL, aR, bR, bL, Vector3.UnitZ, width, length, true);                     // sol
        Quad(aL + up, bL + up, bR + up, aR + up, -Vector3.UnitZ, length, width, false); // plafond
        Quad(aL, bL, bL + up, aL + up, Vector3.Normalize(side), length, h, false);     // mur cote gauche (normale vers la droite)
        Quad(aR, aR + up, bR + up, bR, -Vector3.Normalize(side), length, h, false);    // mur cote droit

        var mesh = new SceneMesh("junction", "junction", wallTexture, null, TxiInfo.Empty, 0,
            [.. pos], [.. nor], [.. uv], null, [.. tri]);
        var walk = new SceneWalkmesh("junction", [.. pos], [.. tri], [.. walkable]);
        return (mesh, walk);

        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, float u, float v, bool walk)
        {
            int i = pos.Count;
            pos.AddRange([p0, p1, p2, p3]);
            nor.AddRange([normal, normal, normal, normal]);
            // Une unite d'UV par metre.
            uv.AddRange([new(0, 0), new(u, 0), new(u, v), new(0, v)]);
            // Enroulement choisi pour que la normale geometrique (sens anti-horaire) soit celle demandee.
            bool ccw = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), normal) > 0;
            if (ccw) tri.AddRange([i, i + 1, i + 2, i, i + 2, i + 3]);
            else tri.AddRange([i, i + 2, i + 1, i, i + 3, i + 2]);
            walkable.AddRange([walk, walk]);
        }
    }
}

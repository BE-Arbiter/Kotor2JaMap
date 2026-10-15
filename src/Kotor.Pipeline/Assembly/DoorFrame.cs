using System.Numerics;
using Kotor.Core.Export;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Pipeline.Entities;

namespace Kotor.Pipeline.Assembly;

/// <summary>
/// Cadre d'une porte (metres, repere du module). <paramref name="Center"/> = milieu du bas de la porte, a la hauteur du sol ;
/// <paramref name="Width"/> selon l'axe lateral de la porte (perpendiculaire a sa direction de passage), <paramref name="Height"/> selon Z.
/// </summary>
public sealed record DoorFrame(Vector3 Center, float Yaw, float Width, float Height);

public static class DoorFrames
{
    /// <summary>Taille par defaut d'un cadre quand le modele de porte est introuvable.</summary>
    public const float DefaultWidth = 2f, DefaultHeight = 3f;

    /// <summary>Cadre par defaut (2 m x 3 m) centre sur la position de la porte.</summary>
    public static DoorFrame DefaultFrame(ModuleEntity door)
    {
        ArgumentNullException.ThrowIfNull(door);
        return new DoorFrame(door.Position, door.Yaw, DefaultWidth, DefaultHeight);
    }

    /// <summary>
    /// Cadre d'une porte, dimensionne sur la boite du modele de porte (meme source que <see cref="DoorConverter"/>).
    /// <c>null</c> avec <c>LINK_DOOR_MODEL_MISSING</c> si le modele est introuvable ou sans maillage.
    /// </summary>
    public static DoorFrame? Of(ResourceResolver res, KotorGame game, ModuleEntity door, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        ArgumentNullException.ThrowIfNull(door);
        ArgumentNullException.ThrowIfNull(log);
        var assets = new PlaceableConverter.ResolverAssets(res);
        string? why = null;
        string? model = door.Model ?? assets.ModelOf(door, out why);
        if (model is null) return Missing(why ?? "no model");
        PlaceableConverter.ModelInfo info = PlaceableConverter.BuildModel(assets, model, game, 1f, Md3ShaderWriter.ModelPrefix(game), log, loadPwk: false);
        if (info.Assets.Count == 0) return Missing(info.Failure ?? "model not converted");
        return FromBox(door, info.Min, info.Max);

        DoorFrame? Missing(string reason)
        {
            log.Warn("LINK_DOOR_MODEL_MISSING", $"Door '{door.Name}': {reason}; no door frame can be derived (default size is {DefaultWidth} x {DefaultHeight} m).");
            return null;
        }
    }

    /// <summary>Cadre issu de la boite <paramref name="min"/>..<paramref name="max"/> du modele (repere du modele, metres), tournee du yaw de la porte.</summary>
    internal static DoorFrame FromBox(ModuleEntity door, Vector3 min, Vector3 max)
    {
        // Le modele est plat : l'epaisseur est le plus petit des deux axes horizontaux, la largeur le plus grand.
        float width = MathF.Max(max.X - min.X, max.Y - min.Y);
        float height = max.Z - min.Z;
        float rad = door.Yaw * MathF.PI / 180f, c = MathF.Cos(rad), s = MathF.Sin(rad);
        Vector2 mid = new((min.X + max.X) / 2f, (min.Y + max.Y) / 2f);
        var offset = new Vector3(mid.X * c - mid.Y * s, mid.X * s + mid.Y * c, min.Z);
        return new DoorFrame(door.Position + offset, door.Yaw, width, height);
    }
}

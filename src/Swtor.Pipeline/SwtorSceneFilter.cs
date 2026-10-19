using Kotor.Core.Scene;

namespace Swtor.Pipeline;

/// <summary>Garde une partie des rooms d'une scene deja construite (selection sur la carte apres l'apercu : pas de nouvelle construction).</summary>
public static class SwtorSceneFilter
{
    /// <summary>
    /// Copie de la scene limitee aux rooms de <paramref name="rooms"/> : maillages, walkmeshes et visibilite (les rooms retirees disparaissent
    /// des listes de rooms visibles). Les lumieres, textures et l'entree sont gardees telles quelles.
    /// </summary>
    public static ModuleScene KeepRooms(ModuleScene scene, IReadOnlyCollection<string> rooms)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(rooms);
        var keep = new HashSet<string>(rooms, StringComparer.OrdinalIgnoreCase);
        var visibility = scene.RoomVisibility?
            .Where(kv => keep.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => (IReadOnlyCollection<string>)[.. kv.Value.Where(keep.Contains)], StringComparer.OrdinalIgnoreCase);
        return scene with
        {
            Meshes = [.. scene.Meshes.Where(m => keep.Contains(m.Room))],
            Walkmeshes = [.. scene.Walkmeshes.Where(w => keep.Contains(w.Room))],
            ModelInstances = [.. scene.ModelInstances.Where(i => keep.Contains(i.Room))],
            RoomVisibility = visibility,
        };
    }
}

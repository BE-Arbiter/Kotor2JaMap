using System.Numerics;
using System.Text;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Entities;
using Kotor.Core.Export;
using Kotor.Core.Layout;
using Kotor.Core.Modules;
using Kotor.Core.Scene;

namespace Kotor.Pipeline.Assembly;

/// <summary>Charge les modules installes d'un assemblage (scenes, entites, transitions), pour l'application et la CLI.</summary>
public static class AssemblyLoader
{
    /// <summary>
    /// Lit chaque module (scene et entites du GIT) puis decouvre les transitions. Module d'un autre jeu : <c>ASSEMBLY_MIXED_GAMES</c> ;
    /// module en double : <c>ASSEMBLY_DUPLICATE_MODULE</c> ; module illisible : <c>MODULE_UNREADABLE</c>.
    /// </summary>
    public static (IReadOnlyList<AssemblyPart> Parts, IReadOnlyList<DoorLink> Links) LoadParts(IReadOnlyList<string> modules, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        if (modules.Count == 0) throw new ArgumentException("At least one module is required.", nameof(modules));
        var known = ModuleCatalog.ListModuleNames(opt.KotorDir).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string m in modules)
            if (!known.Contains(m))
                throw new PipelineStepException("ASSEMBLY_MIXED_GAMES",
                    $"Module '{m}' does not belong to {KotorGames.DisplayName(opt.Game)}: modules of different games cannot be assembled.");
        if (modules.Select(m => m.ToLowerInvariant()).Distinct().Count() != modules.Count)
            throw new PipelineStepException("ASSEMBLY_DUPLICATE_MODULE", "The same module cannot be selected twice.");

        var parts = new List<AssemblyPart>(modules.Count);
        foreach (string m in modules)
        {
            ModuleScene scene = PipelineSteps.Preview(m, opt, log);
            parts.Add(new AssemblyPart(scene, ReadEntities(opt, scene, m, log)));
        }
        return (parts, TransitionLinks.Find(parts, log));
    }

    /// <summary>
    /// Charge les modules et fait la liaison automatique autour de la partie <paramref name="referenceIndex"/>. Les cadres de porte
    /// sont lus a la demande (mis en cache) et leurs avertissements vont dans <paramref name="log"/>.
    /// </summary>
    public static AssemblySession Load(IReadOnlyList<string> modules, int referenceIndex, ExportOptions opt, ImportLog log)
    {
        var (parts, links) = LoadParts(modules, opt, log);
        if (referenceIndex < 0 || referenceIndex >= parts.Count) throw new ArgumentOutOfRangeException(nameof(referenceIndex));
        var cache = new Dictionary<(string, string, Vector3), DoorFrame?>();
        DoorFrame? Frame(AssemblyPart part, ModuleEntity door)
        {
            var key = (part.Scene.Info.Module.ToLowerInvariant(), door.Name, door.Position);
            if (!cache.TryGetValue(key, out DoorFrame? f)) cache[key] = f = FrameOf(opt, part, door, log);
            return f;
        }
        return new AssemblySession([.. modules], parts, links, Frame, referenceIndex);
    }

    /// <summary>Cadre d'une porte (modele de porte du jeu) ; null (cadre par defaut) si le modele est introuvable.</summary>
    public static DoorFrame? FrameOf(ExportOptions opt, AssemblyPart part, ModuleEntity door, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(door);
        string module = part.Scene.Info.Module.ToLowerInvariant();
        try
        {
            using var res = ResourceResolver.ForModule(opt.KotorDir, module, opt.Game);
            return DoorFrames.Of(res, opt.Game, door, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("LINK_DOOR_MODEL_MISSING", $"Door '{door.Name}': {ex.Message}; the default door size is used.");
            return null;
        }
    }

    /// <summary>Entites du GIT d'un module (repere du module) ; GIT ou LYT illisible : <c>ENTITIES_UNREADABLE</c> et liste vide.</summary>
    public static IReadOnlyList<ModuleEntity> ReadEntities(ExportOptions opt, ModuleScene scene, string module, ImportLog log)
    {
        try
        {
            using var res = ResourceResolver.ForModule(opt.KotorDir, module.ToLowerInvariant(), opt.Game);
            byte[] lyt = res.Find(scene.Info.AreaResRef, ResourceType.Lyt)
                ?? throw new InvalidDataException($"{scene.Info.AreaResRef}.lyt not found.");
            LayoutFile layout = LayoutFile.Parse(Encoding.ASCII.GetString(lyt));
            return ModuleEntitiesReader.Read(res, scene.Info, layout, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("ENTITIES_UNREADABLE", $"Module entities unreadable ({ex.Message}): no markers.");
            return [];
        }
    }
}

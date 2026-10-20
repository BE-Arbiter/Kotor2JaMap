using Jk.Core;
using Jk.Core.Gob;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;

namespace Jk.Pipeline;

/// <summary>
/// Export d'un niveau JK en une passe (commande <c>export-ja --game jk</c>) : le meme enchainement que l'application
/// (scene, objets et mobiles, import, export, pk3), sans interface. Le jeu JK n'est que lu.
/// </summary>
public static class JkExporter
{
    /// <param name="level">Niveau (voir <see cref="JkInstall.ListLevels"/>).</param>
    /// <param name="mapName">Nom de carte ; null : <c>jk_&lt;niveau&gt;</c>.</param>
    /// <param name="opt">Options (<c>JediKnightDir</c> donne le dossier du jeu, <c>JaBaseDir</c> recoit le pk3).</param>
    public static ExportResult Export(JkLevelRef level, string? mapName, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        opt = opt.ForGame(Kotor.Core.KotorGame.JediKnight);
        if (opt.Validate() is { } invalid) return new ExportResult(false, null, invalid, 0, 0, 0);
        string name = MapNames.Normalize(string.IsNullOrWhiteSpace(mapName) ? JkWorkflowSteps.DefaultMapName(level.Name) : mapName);
        if (MapNames.Validate(name) is { } badName)
        {
            log.Warn("MAP_NAME_INVALID", badName);
            return new ExportResult(false, null, badName, 0, 0, 0);
        }
        try
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(opt.JediKnightDir ?? "", level.EpisodeGob);
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            return ModuleExporter.Export(prepared.Scene, name, opt, log, JkObjects.ForPipeline(prepared, fs));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new ExportResult(false, null, $"Cannot read the level '{level.Name}': {ex.Message}", 0, 0, 0);
        }
    }
}

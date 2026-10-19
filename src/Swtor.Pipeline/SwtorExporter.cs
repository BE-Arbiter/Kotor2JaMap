using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Swtor.Core;
using Swtor.Core.Areas;

namespace Swtor.Pipeline;

/// <summary>Zone demandee : <c>&lt;id | nom interne&gt;[:room1,room2]</c>.</summary>
public sealed record SwtorAreaSpec(string Area, IReadOnlyList<string> Rooms)
{
    public static SwtorAreaSpec Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        int colon = text.IndexOf(':');
        // Un chemin Windows (C:\...) contient un ':' en deuxieme position : ce n'est pas un separateur de rooms.
        if (colon == 1 && text.Length > 2 && text[2] is '\\' or '/') colon = text.IndexOf(':', 2);
        if (colon < 0) return new SwtorAreaSpec(text.Trim(), []);
        var rooms = text[(colon + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new SwtorAreaSpec(text[..colon].Trim(), rooms);
    }
}

/// <summary>Chiffres de l'export SWTOR, pour la CLI et le rapport.</summary>
public sealed record SwtorExportResult(ExportResult Export, string MapName, string InternalName, SwtorSceneStats? Stats, BspFile? Bsp, string? BspPath);

/// <summary>Zone chargee et scene construite, prete pour l'export (CLI) ou les etapes de l'application.</summary>
public sealed record SwtorPreparedScene(Kotor.Core.Scene.ModuleScene Scene, SwtorSceneStats Stats, string MapName, string InternalName);

/// <summary>Zone SWTOR vers pk3 : scene (<see cref="SwtorSceneBuilder"/>) puis chaine existante (<see cref="ModuleExporter"/>, compilateur, <c>Pk3Builder</c>).</summary>
public static class SwtorExporter
{
    /// <summary>Dossier de la zone : dossier, id numerique ou nom interne (catalogue en cache dans <paramref name="workDir"/>).</summary>
    public static string? ResolveFolder(string area, SwtorAssets assets, string workDir)
    {
        string? folder = AreaLayout.ResolveFolder(area, assets);
        if (folder is not null) return folder;
        if (area.Length == 0 || area.IndexOfAny(['/', '\\']) >= 0 || !Directory.Exists(Path.Combine(assets.Root, "world", "areas"))) return null;
        try
        {
            var catalog = SwtorAreaCatalog.Load(assets, SwtorAreaCatalog.CachePath(workDir));
            var entry = catalog.Find(area);
            return entry is null ? null : AreaLayout.ResolveFolder(entry.Id, assets);
        }
        catch (Exception ex) when (ex is GameFormatException or IOException)
        {
            return null;
        }
    }


    /// <summary>
    /// Charge la zone, valide les rooms demandees et construit la scene. Echec : <paramref name="failure"/> (resultat d'export deja rempli,
    /// avertissement journalise). Chemin commun a la CLI (<see cref="Export"/>) et a l'application (<see cref="SwtorWorkflowSteps"/>).
    /// </summary>
    public static SwtorPreparedScene? Prepare(string areaFolder, SwtorAreaSpec spec, string? mapName, SwtorAssets assets, SwtorBuildOptions? build,
        ImportLog log, out SwtorExportResult? failure)
    {
        failure = null;
        AreaLayout layout;
        try { layout = AreaLayout.Load(areaFolder); }
        catch (Exception ex) when (ex is GameFormatException or IOException or UnauthorizedAccessException)
        {
            log.Warn("SWTOR_AREA_UNREADABLE", ex.Message);
            failure = new SwtorExportResult(new ExportResult(false, null, "Cannot read the area: " + ex.Message, 0, 0, 0), mapName ?? "", "", null, null, null);
            return null;
        }
        string internalName = layout.Area.InternalName ?? Path.GetFileName(areaFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var rooms = spec.Rooms.Count > 0 ? spec.Rooms : null;
        string name = MapNames.Normalize(mapName ?? SwtorMapNames.FromInternalName(internalName, rooms));

        if (rooms is not null)
        {
            var known = layout.Rooms.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] unknown = rooms.Where(r => !known.Contains(r)).ToArray();
            if (unknown.Length > 0)
            {
                string msg = $"Unknown room(s): {string.Join(", ", unknown)} (rooms: {string.Join(", ", layout.Rooms.Select(r => r.Name))}).";
                log.Warn("SWTOR_ROOM_UNKNOWN", msg);
                failure = new SwtorExportResult(new ExportResult(false, null, msg, 0, 0, 0), name, internalName, null, null, null);
                return null;
            }
        }

        build = (build ?? new SwtorBuildOptions()) with { Rooms = rooms };
        SwtorSceneResult result = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(assets), internalName, build, log);
        return new SwtorPreparedScene(result.Scene, result.Stats, name, internalName);
    }

    /// <summary>Charge la zone, construit la scene et l'exporte. <paramref name="opt"/> doit avoir <c>Game = Swtor</c> (fait ici si besoin).</summary>
    public static SwtorExportResult Export(string areaFolder, SwtorAreaSpec spec, string? mapName, SwtorAssets assets, ExportOptions opt,
        SwtorBuildOptions? build, ImportLog log)
    {
        opt = opt.ForGame(KotorGame.Swtor);
        SwtorPreparedScene? prepared = Prepare(areaFolder, spec, mapName, assets, build, log, out SwtorExportResult? failure);
        if (prepared is null) return failure!;
        string name = prepared.MapName;
        ExportResult export = ModuleExporter.Export(prepared.Scene, name, opt, log);

        BspFile? bsp = null;
        string? bspPath = Path.Combine(opt.WorkDir, name, "pk3", "maps", name + ".bsp");
        try { if (File.Exists(bspPath)) bsp = BspReader.Read(File.ReadAllBytes(bspPath)); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { bsp = null; }
        return new SwtorExportResult(export, name, prepared.InternalName, prepared.Stats, bsp, bspPath);
    }
}

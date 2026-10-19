using Kotor.Core.Diagnostics;
using Swtor.Core;
using Swtor.Core.Areas;

namespace Swtor.Pipeline;

/// <summary>Rooms d'une zone avec leurs triangles de rendu (SWTOR : choix des rooms avant l'apercu).</summary>
public sealed record SwtorAreaAnalysis(string AreaId, string InternalName, IReadOnlyList<SwtorRoomInfo> Rooms)
{
    public SwtorRoomSelection NewSelection() => new(AreaId, InternalName, Rooms);
}

public static class SwtorAreaAnalyzer
{
    /// <summary>
    /// Construit la zone une fois (sans collision, toutes les rooms dont l'espace) pour compter les triangles de rendu par room.
    /// La room du ciel (<c>skyscene</c>) n'est jamais exportee : elle n'est pas listee.
    /// </summary>
    public static SwtorAreaAnalysis? Analyze(string areaFolder, SwtorAssets assets)
    {
        AreaLayout layout;
        try { layout = AreaLayout.Load(areaFolder); }
        catch (Exception ex) when (ex is GameFormatException or IOException or UnauthorizedAccessException) { return null; }
        string internalName = layout.Area.InternalName ?? Path.GetFileName(areaFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var build = new SwtorBuildOptions { IncludeSpaceRoom = true, UseCollisionMeshes = false };
        SwtorSceneResult r = SwtorSceneBuilder.Build(layout, new SwtorAssetsModelSource(assets), internalName, build, new ImportLog());
        string sky = layout.Area.AreaProperties.Get("skyscene_room") is { Length: > 0 } s ? s : "skyscene_room";
        var rooms = layout.Rooms
            .Where(x => !x.Name.Equals(sky, StringComparison.OrdinalIgnoreCase) && !x.Name.Equals("skyscene", StringComparison.OrdinalIgnoreCase))
            .Select(x => new SwtorRoomInfo(x.Name, r.Stats.RoomTriangles.GetValueOrDefault(x.Name), SwtorSceneBuilder.IsSpaceRoom(x.Name)))
            .OrderByDescending(x => x.Triangles).ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();
        return new SwtorAreaAnalysis(Path.GetFileName(areaFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), internalName, rooms);
    }
}

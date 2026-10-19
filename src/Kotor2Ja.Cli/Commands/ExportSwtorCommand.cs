using System.CommandLine;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Swtor.Core;
using Swtor.Pipeline;

namespace Kotor2Ja.Cli.Commands;

/// <summary>export-swtor : exporte une zone SWTOR (id, nom interne, rooms facultatives) en pk3 pour Jedi Academy. Equivalent de <c>export-ja --game swtor</c>.</summary>
public static class ExportSwtorCommand
{
    public static Command Create()
    {
        var cmd = new Command("export-swtor", "Exports a SWTOR area (id or internal name, optionally ':room1,room2') as a pk3 for Jedi Academy (BSP, shaders, textures).");
        var area = new Argument<string>("area") { Description = "Area id (world/areas/<id>) or internal name (see swtor-list-areas, e.g. war_planworking), optionally followed by ':room1,room2'." };
        var nameOpt = new Option<string?>("--name") { Description = "Map name (default: the sanitized internal name, e.g. war_planworking)." };
        var assetsOpt = new Option<string?>("--assets") { Description = "Root of the extracted SWTOR resources (default: SWTOR_ASSETS, then C:\\jka_tor_assets\\resources)." };
        var scaleOpt = new Option<float?>("--scale") { Description = "Scale from SWTOR metres to JA units (default: config, 40)." };
        var limOpt = new Option<string?>("--bsp-limits") { Description = "BSP limits profile: swgl (default) or vanilla." };
        var cfgOpt = new Option<string?>("--config") { Description = "JSON configuration file (default: kotor2ja.config.json next to the executable)." };
        var texOpt = new Option<int?>("--max-texture") { Description = "Largest exported texture side in pixels (default 512, 0 = no reduction)." };
        var spaceOpt = new Option<bool>("--include-space") { Description = "Keep the SPACE room (ship exterior hull, windows) when all rooms are exported; left out by default for interiors." };
        var wsOpt = new Option<float?>("--world-scale") { Description = "Manual multiplier on top of the derived SWTOR world scale (250 JA units per GR2 unit, calibrated on JA stair height, seat height and door proportions; default 1)." };
        var axesOpt = new Option<string?>("--axes") { Description = "Axis conversion: rotation (default, right-handed) or mirror (left-handed source)." };
        cmd.Arguments.Add(area);
        foreach (Option o in new Option[] { nameOpt, assetsOpt, scaleOpt, limOpt, cfgOpt, texOpt, spaceOpt, axesOpt, wsOpt }) cmd.Options.Add(o);
        cmd.SetAction(pr =>
        {
            string? cfgPath = pr.GetValue(cfgOpt) ?? GameOption.DefaultConfigPath;
            if (!ExportJaCommand.TryLoadConfig(cfgPath, Console.Error, out ExportOptions opt)) return 1;
            return Run(opt, pr.GetValue(area)!, pr.GetValue(nameOpt), pr.GetValue(assetsOpt), pr.GetValue(scaleOpt), pr.GetValue(limOpt),
                pr.GetValue(texOpt), pr.GetValue(spaceOpt), pr.GetValue(axesOpt), Console.Out, Console.Error, pr.GetValue(wsOpt));
        });
        return cmd;
    }

    /// <summary>Code de sortie : 0 si OK, 1 si echec (options, compilation, limite), 2 si la zone ou les ressources sont introuvables.</summary>
    public static int Run(ExportOptions opt, string areaText, string? mapName, string? assetsRoot, float? scale, string? bspLimits, int? maxTexture,
        bool includeSpace, string? axes, TextWriter output, TextWriter error, float? worldScale = null)
    {
        opt = opt.ForGame(Kotor.Core.KotorGame.Swtor);
        if (scale is { } s) opt = opt with { Scale = s };
        if (!string.IsNullOrWhiteSpace(bspLimits)) opt = opt with { BspLimits = bspLimits };
        if (opt.Validate() is { } invalid)
        {
            error.WriteLine($"Invalid options: {invalid}");
            return 1;
        }
        if (mapName is not null && MapNames.Validate(MapNames.Normalize(mapName)) is { } badName)
        {
            error.WriteLine(badName);
            return 1;
        }
        AxisConvention convention = SwtorAxes.Default;
        if (!string.IsNullOrWhiteSpace(axes) && !Enum.TryParse(axes, ignoreCase: true, out convention))
        {
            error.WriteLine($"Unknown axis convention: {axes} (expected rotation or mirror).");
            return 1;
        }
        if (maxTexture is < 0)
        {
            error.WriteLine($"Invalid texture size: {maxTexture}.");
            return 1;
        }

        var assets = new SwtorAssets(assetsRoot ?? opt.SwtorDir);
        var spec = SwtorAreaSpec.Parse(areaText);
        string? folder = SwtorExporter.ResolveFolder(spec.Area, assets, opt.WorkDir);
        if (folder is null)
        {
            error.WriteLine($"SWTOR area '{spec.Area}' not found under {assets.Root} (use --assets, or swtor-list-areas to look for an id or internal name).");
            return 2;
        }

        var build = new SwtorBuildOptions { IncludeSpaceRoom = includeSpace, Axes = convention, WorldScale = worldScale is > 0f ? worldScale.Value : 0f };
        if (maxTexture is { } mt) build = build with { MaxTextureSize = mt };
        var log = new ImportLog();
        SwtorExportResult r = SwtorExporter.Export(folder, spec, mapName, assets, opt, build, log);
        output.WriteLine($"Area {r.InternalName} ({Path.GetFileName(folder)}) -> map {r.MapName}");
        return Report(r, log, output, error);
    }

    private static int Report(SwtorExportResult r, ImportLog log, TextWriter output, TextWriter error)
    {
        if (r.Stats is { } st)
        {
            output.WriteLine($"Rooms: {st.Rooms}  model instances: {st.ModelInstances}  distinct models: {st.DistinctModels} (missing {st.MissingModels})");
            output.WriteLine($"Collision meshes skipped: {st.SkippedCollisionMeshes}  lights: {st.Lights}  textures: {st.Textures}");
            output.WriteLine($"Walkable triangles: {st.WalkableTriangles}  solid triangles: {st.SolidTriangles}");
            output.WriteLine($"Collision: {st.CollisionInstances} instance(s) from collision meshes ({st.OverrideInstances} override), {st.FallbackInstances} from visible geometry");
        }
        ExportResult e = r.Export;
        output.WriteLine($"Meshes: {e.Meshes}");
        output.WriteLine($"Triangles: {e.Triangles}");
        output.WriteLine($"Missing textures: {e.MissingTextures}");
        if (r.Bsp is { } bsp)
        {
            output.WriteLine($"BSP: {bsp.Shaders.Count} shaders, {bsp.Surfaces.Count} surfaces, {bsp.Surfaces.Sum(x => x.Positions.Length)} draw verts, " +
                             $"{bsp.Surfaces.Sum(x => x.Indices.Length)} draw indexes, {bsp.Collision.BrushCount} brushes, {bsp.Collision.BrushSideCount} brush sides, {bsp.Collision.PlaneCount} planes");
        }
        output.WriteLine("Log by code:");
        foreach (var g in log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal))
            output.WriteLine($"  {g.Key}: {g.Count()}");
        var limitCodes = new[] { "COLLISION_CAPPED", "WALKABLE_CAPPED", "BSP_ERROR" };
        var limitHits = log.Warnings.Where(w => limitCodes.Contains(w.Code)).ToList();
        if (limitHits.Count > 0 && r.Stats is { RoomTriangles.Count: > 0 } rs)
            output.WriteLine("Render triangles per room: " + string.Join(", ", rs.RoomTriangles.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}")));
        if (limitHits.Count > 0)
        {
            output.WriteLine("BSP LIMITS EXCEEDED (collision reduced or build failed); consider exporting a subset of rooms (area:room1,room2):");
            foreach (var w in limitHits) output.WriteLine($"  {w.Code}: {w.Message}");
        }
        if (e.JournalError is not null) error.WriteLine($"Warning: {e.JournalError}");
        if (!e.Ok)
        {
            error.WriteLine($"Failed: {e.Error}");
            return 1;
        }
        output.WriteLine($"pk3: {e.Pk3Path}");
        return 0;
    }
}

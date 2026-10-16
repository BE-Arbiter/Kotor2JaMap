using System.CommandLine;
using System.Numerics;
using Swtor.Core;
using Swtor.Core.Areas;

namespace Kotor2Ja.Cli.Commands;

/// <summary>swtor-dump-area : affiche les rooms, les comptes par type d'instance et les premieres instances d'une zone SWTOR.</summary>
public static class SwtorDumpAreaCommand
{
    public static Command Create()
    {
        var areaArg = new Argument<string>("area") { Description = "Identifiant numerique de la zone (world/areas/<id>), nom interne (voir swtor-list-areas, ex. war_planworking) ou chemin du dossier contenant area.dat." };
        var assetsOption = new Option<string?>("--assets")
        {
            Description = "Racine des ressources SWTOR extraites (defaut : SWTOR_ASSETS, puis C:\\jka_tor_assets\\resources).",
        };
        var maxOption = new Option<int>("--max")
        {
            Description = "Nombre d'instances detaillees par room.",
            DefaultValueFactory = _ => 10,
        };
        var cmd = new Command("swtor-dump-area", "Dumps the rooms, instance counts and first instances of a SWTOR area.");
        cmd.Arguments.Add(areaArg);
        cmd.Options.Add(assetsOption);
        cmd.Options.Add(maxOption);
        cmd.SetAction(pr => Run(pr.GetValue(areaArg)!, pr.GetValue(assetsOption), pr.GetValue(maxOption), Console.Out, Console.Error));
        return cmd;
    }

    /// <summary>Cherche une zone par nom interne dans le catalogue (cache JSON du dossier de travail ; reconstruit s'il manque).</summary>
    private static string? ResolveByInternalName(string area, SwtorAssets assets, string? workDir)
    {
        if (area.Length == 0 || area.IndexOfAny(['/', '\\']) >= 0 || !Directory.Exists(Path.Combine(assets.Root, "world", "areas"))) return null;
        try
        {
            var catalog = SwtorAreaCatalog.Load(assets, SwtorAreaCatalog.CachePath(workDir ?? SwtorListAreasCommand.ResolveWorkDir(null, null)));
            var entry = catalog.Find(area);
            return entry is null ? null : AreaLayout.ResolveFolder(entry.Id, assets);
        }
        catch (Exception ex) when (ex is GameFormatException or IOException)
        {
            return null;
        }
    }

    public static int Run(string area, string? assetsRoot, int maxInstances, TextWriter output, TextWriter error, string? workDir = null)
    {
        var assets = new SwtorAssets(assetsRoot);
        string? folder = AreaLayout.ResolveFolder(area, assets) ?? ResolveByInternalName(area, assets, workDir);
        if (folder is null)
        {
            error.WriteLine($"SWTOR area '{area}' not found (looked for a folder with area.dat, an internal name in the catalogue, then {Path.Combine(assets.Root, "world", "areas", area)}).");
            return 2;
        }

        AreaLayout layout;
        try
        {
            layout = AreaLayout.Load(folder);
        }
        catch (Exception ex) when (ex is GameFormatException or IOException)
        {
            error.WriteLine("Cannot read the area: " + ex.Message);
            return 1;
        }

        output.WriteLine($"Area folder: {folder}");
        output.WriteLine($"Rooms ({layout.Area.Rooms.Count}): {string.Join(", ", layout.Area.Rooms)}");
        if (layout.MissingRooms.Count > 0) output.WriteLine($"Rooms without .dat file: {string.Join(", ", layout.MissingRooms)}");
        output.WriteLine($"Assets in table: {layout.Area.Assets.Count}");
        if (layout.Area.Markers.Count > 0) output.WriteLine("Markers: " + string.Join(", ", layout.Area.Markers.Select(m => m.Name)));
        AreaProperties props = layout.Area.AreaProperties;
        if (props.Values.Count > 0)
        {
            output.WriteLine($"Area properties: is_outdoor={props.Get("is_outdoor")}, skyscene_room={props.Get("skyscene_room")}, " +
                             $"environment_map={props.Get("environment_map")}, illumination_map={props.Get("illumination_map")}");
        }

        // Comptes par type et resolution des modeles sur disque.
        var kinds = layout.AllInstances.GroupBy(i => i.Kind).OrderByDescending(g => g.Count());
        output.WriteLine("Instances by kind:");
        foreach (var g in kinds) output.WriteLine($"  {g.Key,-12} {g.Count()}");

        string[] modelPaths = layout.AllInstances.Where(i => i.Kind == InstanceKind.Model).Select(i => i.AssetPath!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        int found = modelPaths.Count(p => assets.Resolve(p) is not null);
        if (modelPaths.Length > 0)
            output.WriteLine($"Distinct models (gr2): {modelPaths.Length}, found on disk: {found} ({100.0 * found / modelPaths.Length:F1}%)");

        string? skyRoom = props.Get("skyscene_room");
        var models = layout.Rooms.Where(r => r.Name != skyRoom).SelectMany(r => r.Room.Instances).Where(i => i.Kind == InstanceKind.Model).ToList();
        if (models.Count > 0)
        {
            Vector3 mn = new(float.MaxValue), mx = new(float.MinValue);
            foreach (var m in models)
            {
                Vector3 p = layout.WorldPosition(m);
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }
            output.WriteLine(FormattableString.Invariant($"Model origins bbox (m, sky room excluded): min ({mn.X:F2}, {mn.Y:F2}, {mn.Z:F2}) max ({mx.X:F2}, {mx.Y:F2}, {mx.Z:F2}) size ({mx.X - mn.X:F1} x {mx.Y - mn.Y:F1} x {mx.Z - mn.Z:F1})"));
        }

        foreach (var (name, room) in layout.Rooms)
        {
            output.WriteLine();
            output.WriteLine($"== {name}: {room.Instances.Count} instances, linked rooms [{string.Join(", ", room.LinkedRooms)}]");
            foreach (var i in room.Instances.Take(Math.Max(0, maxInstances)))
            {
                Vector3 w = layout.WorldPosition(i);
                string label = i.AssetPath ?? $"(unknown type 0x{i.TypeId:X})";
                if (i.FxName is { } fx) label += " [" + fx + "]";
                string line = FormattableString.Invariant($"  {i.Kind,-11} {label}  pos ({i.Position.X:F2}, {i.Position.Y:F2}, {i.Position.Z:F2})  rot ({i.EulerDegrees.X:F0}, {i.EulerDegrees.Y:F0}, {i.EulerDegrees.Z:F0})  scale ({i.Scale.X:F2}, {i.Scale.Y:F2}, {i.Scale.Z:F2})");
                if (i.ParentId != 0) line += FormattableString.Invariant($"  parent 0x{i.ParentId:X} world ({w.X:F2}, {w.Y:F2}, {w.Z:F2})");
                if (i.Color is { } c) line += FormattableString.Invariant($"  color ({c.X:F2}, {c.Y:F2}, {c.Z:F2})");
                output.WriteLine(line);
            }
        }
        return 0;
    }
}

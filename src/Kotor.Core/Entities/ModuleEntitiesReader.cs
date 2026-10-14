using System.Numerics;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Gff;
using Kotor.Core.Layout;
using Kotor.Core.Mdl;
using Kotor.Core.Modules;

namespace Kotor.Core.Entities;

/// <summary>
/// Liste les entites d'un module : listes du GIT et noeuds lumiere des modeles de piece.
/// Libelles (verifies sur le vrai m02aa.git) : Placeable/Door = X,Y,Z + Bearing ; Waypoint/Trigger/Creature/Sound/Store =
/// XPosition,YPosition,ZPosition (+ XOrientation,YOrientation) ; Trigger.Geometry = PointX/PointY/PointZ relatifs ;
/// Encounter.Geometry = X/Y/Z ou PointX/PointY/PointZ (PyKotor git.py ; liste vide dans m02aa, non verifie) ;
/// Camera = Position (vecteur) + Orientation (quaternion).
/// </summary>
public static class ModuleEntitiesReader
{
    private readonly record struct ListSpec(string List, EntityKind Kind, bool BearingStyle, bool HasGeometry);

    private static readonly ListSpec[] Specs =
    [
        new("Placeable List", EntityKind.Placeable, true, false),
        new("Door List", EntityKind.Door, true, false),
        new("WaypointList", EntityKind.Waypoint, false, false),
        new("TriggerList", EntityKind.Trigger, false, true),
        new("Creature List", EntityKind.Creature, false, false),
        new("SoundList", EntityKind.Sound, false, false),
        new("StoreList", EntityKind.Store, false, false),
        new("Encounter List", EntityKind.Encounter, false, true),
        new("CameraList", EntityKind.Camera, false, false),
    ];

    public static IReadOnlyList<ModuleEntity> Read(ResourceResolver res, ModuleInfo info, LayoutFile layout, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        var result = new List<ModuleEntity>();
        ReadGit(res, info, result, log);
        ReadLights(res, layout, result, log);

        var counts = Enum.GetValues<EntityKind>().Select(k => $"{k}={result.Count(e => e.Kind == k)}");
        log.Warn("ENTITIES", $"{info.AreaResRef} : {result.Count} entite(s) ({string.Join(", ", counts)}).");
        return result;
    }

    private static void ReadGit(ResourceResolver res, ModuleInfo info, List<ModuleEntity> result, ImportLog log)
    {
        byte[]? data = res.Find(info.AreaResRef, ResourceType.Git);
        if (data is null) { log.Warn("GIT_MISSING", $"{info.AreaResRef}.git introuvable : aucune entite du GIT."); return; }
        GffStruct git;
        try { git = GffReader.Read(data, out _); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException)
        {
            log.Warn("GIT_MISSING", $"{info.AreaResRef}.git illisible ({ex.Message}) : aucune entite du GIT.");
            return;
        }
        foreach (var spec in Specs)
        {
            var list = git.GetList(spec.List);
            for (int i = 0; i < list.Count; i++)
            {
                var e = ReadEntry(list[i], spec, out string? error);
                if (e is null) log.Warn("GIT_ENTRY_INVALID", $"{spec.List}[{i}] : {error}, entree sautee.");
                else result.Add(e);
            }
        }
    }

    private static ModuleEntity? ReadEntry(GffStruct s, ListSpec spec, out string? error)
    {
        error = null;
        Vector3 pos;
        float yaw;
        if (spec.Kind == EntityKind.Camera)
        {
            if (s.Get<Vector3?>("Position") is not { } p || s.Get<Quaternion?>("Orientation") is not { } q)
            { error = "Position ou Orientation absente"; return null; }
            pos = p;
            // Rotation autour de Z du quaternion (GFF : w,x,y,z ; w domine pour une petite rotation, constate sur m02aa).
            yaw = Normalize(MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180f / MathF.PI);
        }
        else
        {
            string x = spec.BearingStyle ? "X" : "XPosition", y = spec.BearingStyle ? "Y" : "YPosition", z = spec.BearingStyle ? "Z" : "ZPosition";
            if (!s.Fields.ContainsKey(x) || !s.Fields.ContainsKey(y) || !s.Fields.ContainsKey(z))
            { error = $"champs {x}/{y}/{z} absents"; return null; }
            pos = new Vector3(s.Get<float>(x), s.Get<float>(y), s.Get<float>(z));
            if (spec.BearingStyle) yaw = Normalize(s.Get<float>("Bearing") * 180f / MathF.PI);
            else
            {
                float ox = s.Get<float>("XOrientation"), oy = s.Get<float>("YOrientation");
                // Vecteur d'orientation -> rotation autour de Z, meme convention que Bearing (0 = face a +Y).
                yaw = ox == 0 && oy == 0 ? 0 : Normalize(MathF.Atan2(-ox, oy) * 180f / MathF.PI);
            }
        }
        if (!IsFinite(pos) || !float.IsFinite(yaw)) { error = "position non finie"; return null; }

        Vector3[]? polygon = null;
        if (spec.HasGeometry && s.Fields.ContainsKey("Geometry"))
        {
            var pts = s.GetList("Geometry");
            polygon = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var g = pts[i];
                bool pointStyle = g.Fields.ContainsKey("PointX");
                string px = pointStyle ? "PointX" : "X", py = pointStyle ? "PointY" : "Y", pz = pointStyle ? "PointZ" : "Z";
                if (!g.Fields.ContainsKey(px) || !g.Fields.ContainsKey(py) || !g.Fields.ContainsKey(pz))
                { error = $"point de geometrie {i} incomplet"; return null; }
                var rel = new Vector3(g.Get<float>(px), g.Get<float>(py), g.Get<float>(pz));
                if (!IsFinite(rel)) { error = $"point de geometrie {i} non fini"; return null; }
                polygon[i] = pos + rel;
            }
        }

        string template = (spec.Kind == EntityKind.Store ? s.Get<string>("ResRef") : s.Get<string>("TemplateResRef")) ?? "";
        string tag = s.Get<string>("Tag") ?? "";
        string name = tag.Length > 0 ? tag : template.Length > 0 ? template
            : spec.Kind == EntityKind.Camera && s.Get<int?>("CameraID") is { } id ? $"camera{id}" : spec.Kind.ToString();
        return new ModuleEntity(spec.Kind, name, template, pos, yaw, polygon);
    }

    private static void ReadLights(ResourceResolver res, LayoutFile layout, List<ModuleEntity> result, ImportLog log)
    {
        foreach (var room in layout.Rooms)
        {
            byte[]? mdl = res.Find(room.Model, ResourceType.Mdl);
            byte[]? mdx = res.Find(room.Model, ResourceType.Mdx);
            if (mdl is null || mdx is null)
            {
                log.Warn("ROOM_MODEL_MISSING", $"{room.Model} : {(mdl is null ? "MDL" : "MDX")} introuvable, lumieres de la piece sautees.");
                continue;
            }
            KotorModel model;
            try { model = MdlReader.Read(mdl, mdx); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                log.Warn("ROOM_MODEL_UNREADABLE", $"{room.Model} : {ex.Message}");
                continue;
            }
            foreach (var (node, transform) in model.Walk())
            {
                if (!node.Kind.HasFlag(NodeKind.Light)) continue;
                var p = Vector3.Transform(Vector3.Zero, transform) + room.Position;
                result.Add(new ModuleEntity(EntityKind.Light, node.Name, room.Model, p, 0f, null));
            }
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static float Normalize(float deg)
    {
        if (!float.IsFinite(deg)) return deg;
        deg %= 360f;
        if (deg > 180f) deg -= 360f;
        if (deg <= -180f) deg += 360f;
        return deg == 0f ? 0f : deg; // evite -0
    }
}

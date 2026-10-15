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
/// Libelles (verifies sur le vrai m02aa.git) : Placeable/Door = X,Y,Z + Bearing ; Waypoint/Trigger/Sound/Store =
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
        new("SoundList", EntityKind.Sound, false, false),
        new("StoreList", EntityKind.Store, false, false),
        new("CameraList", EntityKind.Camera, false, false),
    ];

    public static IReadOnlyList<ModuleEntity> Read(ResourceResolver res, ModuleInfo info, LayoutFile layout, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(log);
        var result = new List<ModuleEntity>();
        ReadGit(res, info, result, log, null);
        ReadLights(res, layout, result, log);

        // Les PNJ (creatures, rencontres) ne sont ni lus ni comptes : voir NPC_SKIPPED.
        var counts = Enum.GetValues<EntityKind>().Where(k => k is not (EntityKind.Creature or EntityKind.Encounter)).Select(k => $"{k}={result.Count(e => e.Kind == k)}");
        log.Warn("ENTITIES", $"{info.AreaResRef}: {result.Count} entity(ies) ({string.Join(", ", counts)}).");
        return result;
    }

    /// <summary>
    /// Objets places et portes du GIT seulement (avec leur modele), sans lire les pieces ni ecrire le bilan <c>ENTITIES</c>.
    /// Utilise par l'etape Importer pour convertir les objets.
    /// </summary>
    public static IReadOnlyList<ModuleEntity> ReadObjects(ResourceResolver res, ModuleInfo info, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(res);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(log);
        var result = new List<ModuleEntity>();
        ReadGit(res, info, result, log, [EntityKind.Placeable, EntityKind.Door]);
        return result;
    }

    private static void ReadGit(ResourceResolver res, ModuleInfo info, List<ModuleEntity> result, ImportLog log, EntityKind[]? only)
    {
        byte[]? data = res.Find(info.AreaResRef, ResourceType.Git);
        if (data is null) { log.Warn("GIT_MISSING", $"{info.AreaResRef}.git not found: no GIT entities."); return; }
        GffStruct git;
        try { git = GffReader.Read(data, out _); }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException
            or EndOfStreamException or IndexOutOfRangeException)
        {
            // ArgumentOutOfRangeException est une ArgumentException.
            log.Warn("GIT_UNREADABLE", $"{info.AreaResRef}.git unreadable ({ex.Message}): no GIT entities.");
            return;
        }
        if (only is null) WarnNpcs(git, info, log);
        var models = new ObjectModelResolver(res);
        foreach (var spec in Specs)
        {
            if (only is not null && !only.Contains(spec.Kind)) continue;
            if (!git.Fields.TryGetValue(spec.List, out var raw) || raw is not IReadOnlyList<GffStruct> list)
            {
                log.Warn("GIT_LIST_MISSING", git.Fields.ContainsKey(spec.List)
                    ? $"{spec.List} is present but is not a list: no {spec.Kind} entities."
                    : $"{spec.List} is missing from the GIT: no {spec.Kind} entities.");
                continue;
            }
            int noOrientation = 0;
            for (int i = 0; i < list.Count; i++)
            {
                var e = ReadEntry(list[i], spec, out string? error, out bool orientMissing);
                if (e is not null && spec.Kind is EntityKind.Placeable or EntityKind.Door && e.Template.Length > 0)
                {
                    // Resolution silencieuse : l'etape Importer journalise la raison (PLACEABLE_SKIPPED) si le modele manque.
                    string? model = spec.Kind == EntityKind.Door ? models.ResolveDoor(e.Template, out _) : models.ResolvePlaceable(e.Template, out _);
                    if (model is not null) e = e with { Model = model };
                    if (spec.Kind == EntityKind.Door)
                        e = e with { Locked = models.IsDoorLocked(e.Template), Static = models.IsDoorStatic(e.Template), LinkedToModule = (list[i].Get<string>("LinkedToModule") ?? "").Trim() };
                }
                if (e is null) log.Warn("GIT_ENTRY_INVALID", $"{spec.List}[{i}]: {error}, entry skipped.");
                else { result.Add(e); if (orientMissing) noOrientation++; }
            }
            if (noOrientation > 0)
                log.Warn("GIT_ORIENTATION_MISSING", $"{spec.List}: {noOrientation} entry(ies) without a usable orientation, Yaw = 0.");
        }
    }

    /// <summary>Les PNJ ne sont pas convertis : une ligne d'information donne leur nombre par type (rien si le GIT n'en contient pas).</summary>
    private static void WarnNpcs(GffStruct git, ModuleInfo info, ImportLog log)
    {
        int creatures = git.Fields.TryGetValue("Creature List", out var c) && c is IReadOnlyList<GffStruct> cl ? cl.Count : 0;
        int encounters = git.Fields.TryGetValue("Encounter List", out var e) && e is IReadOnlyList<GffStruct> el ? el.Count : 0;
        if (creatures + encounters > 0)
            log.Warn("NPC_SKIPPED", $"{info.AreaResRef}: NPCs are not converted ({creatures} creature(s), {encounters} encounter(s) skipped).");
    }

    private static ModuleEntity? ReadEntry(GffStruct s, ListSpec spec, out string? error, out bool orientMissing)
    {
        error = null;
        orientMissing = false;
        Vector3 pos;
        float yaw;
        if (spec.Kind == EntityKind.Camera)
        {
            if (s.Get<Vector3?>("Position") is not { } p || s.Get<Quaternion?>("Orientation") is not { } q)
            { error = "Position or Orientation missing"; return null; }
            pos = p;
            // Rotation autour de Z du quaternion (GFF : w,x,y,z ; w domine pour une petite rotation, constate sur m02aa).
            yaw = Normalize(MathF.Atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z)) * 180f / MathF.PI);
        }
        else
        {
            string x = spec.BearingStyle ? "X" : "XPosition", y = spec.BearingStyle ? "Y" : "YPosition", z = spec.BearingStyle ? "Z" : "ZPosition";
            if (!s.Fields.ContainsKey(x) || !s.Fields.ContainsKey(y) || !s.Fields.ContainsKey(z))
            { error = $"fields {x}/{y}/{z} missing"; return null; }
            if (s.Fields[x] is not float fx || s.Fields[y] is not float fy || s.Fields[z] is not float fz)
            { error = $"fields {x}/{y}/{z} have an unexpected type (float expected)"; return null; }
            pos = new Vector3(fx, fy, fz);
            if (spec.BearingStyle)
            {
                if (s.Fields.TryGetValue("Bearing", out var b) && b is float bf) yaw = Normalize(bf * 180f / MathF.PI);
                else { yaw = 0f; orientMissing = true; }
            }
            else
            {
                float ox = 0, oy = 0;
                if (s.Fields.TryGetValue("XOrientation", out var oxv) && oxv is float oxf
                    && s.Fields.TryGetValue("YOrientation", out var oyv) && oyv is float oyf) { ox = oxf; oy = oyf; }
                else orientMissing = true;
                // Vecteur d'orientation -> rotation autour de Z, meme convention que Bearing (0 = face a +Y).
                yaw = ox == 0 && oy == 0 ? 0 : Normalize(MathF.Atan2(-ox, oy) * 180f / MathF.PI);
            }
        }
        if (!IsFinite(pos) || !float.IsFinite(yaw)) { error = "position is not finite"; return null; }

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
                { error = $"geometry point {i} incomplete"; return null; }
                var rel = new Vector3(g.Get<float>(px), g.Get<float>(py), g.Get<float>(pz));
                if (!IsFinite(rel)) { error = $"geometry point {i} is not finite"; return null; }
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
                log.Warn("ROOM_MODEL_MISSING", $"{room.Model}: {(mdl is null ? "MDL" : "MDX")} not found, room lights skipped.");
                continue;
            }
            KotorModel model;
            try { model = MdlReader.Read(mdl, mdx); }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException
                or OverflowException or EndOfStreamException or IndexOutOfRangeException)
            {
                log.Warn("ROOM_MODEL_UNREADABLE", $"{room.Model}: {ex.Message}");
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

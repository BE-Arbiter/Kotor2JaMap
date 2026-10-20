using System.Globalization;
using System.Numerics;
using System.Text;
using Jk.Core.Cog;
using Jk.Core.Jkl;
using Jk.Core.Text;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;

namespace Jk.Pipeline;

public enum JkVolumeKind { Water, Hurt, Kill }

/// <summary>
/// Volume d'un niveau JK, en unites JK (Z vers le haut, non mises a l'echelle) : boite englobante d'un secteur ou d'une surface.
/// <see cref="Water"/> : secteur sous l'eau (drapeau 0x2), <see cref="Color"/> = TINT du secteur (couleur du brouillard). <see cref="Hurt"/> / <see cref="Kill"/> :
/// zone qui blesse (<see cref="Damage"/> par seconde ; a partir de 100 elle tue) ou tue ; <see cref="Surface"/> >= 0 quand le volume vient d'une surface
/// (boite fine au-dessus d'elle) plutot que d'un secteur entier.
/// </summary>
public sealed record JkVolume(JkVolumeKind Kind, int Sector, Vector3 Min, Vector3 Max)
{
    public int Surface { get; init; } = -1;
    public float Damage { get; init; }
    public Vector3 Color { get; init; }
}

/// <summary>Resultat de l'analyse des effets de surface d'un niveau (volumes, defilements, animations, derive du ciel).</summary>
internal sealed class JkLevelEffects
{
    internal readonly record struct Slide(Vector3 Direction, float Speed);
    internal readonly record struct Animation(float Fps, uint Flags);

    public List<JkVolume> Volumes { get; } = [];
    /// <summary>Surface -> glissement de sa texture (<c>SlideWall</c> / <c>SlideSurface</c> du message <c>startup</c>).</summary>
    public Dictionary<int, Slide> Slides { get; } = [];
    /// <summary>Surface -> animation de ses cellules (<c>SurfaceAnim</c>).</summary>
    public Dictionary<int, Animation> SurfaceAnims { get; } = [];
    /// <summary>Nom du fichier materiau (minuscules) -> animation de ses cellules (<c>MaterialAnim</c>) ; vaut pour toutes les surfaces qui l'utilisent.</summary>
    public Dictionary<string, Animation> MaterialAnims { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Vector2? HorizonSlide { get; set; }
    public Vector2? CeilingSlide { get; set; }
    /// <summary>Surfaces qui blessent ou tuent au contact, et secteurs qui blessent leurs occupants.</summary>
    public HashSet<int> HazardSurfaces { get; } = [];
    public HashSet<int> HazardSectors { get; } = [];
    /// <summary>Adjoins bloquants dans le fichier que des COG peuvent ouvrir (<c>SetAdjoinFlags</c> avec le bit de passage) : rendus franchissables.</summary>
    public HashSet<int> OpenedAdjoins { get; } = [];
}

/// <summary>
/// Effets de surface d'un niveau JK : eau, zones qui blessent ou tuent, textures qui defilent ou s'animent, derive du ciel.
/// Les drapeaux de secteur viennent du fichier ; les effets de COG viennent d'une lecture minimale des scripts (<see cref="CogReader"/>) :
/// seuls les appels du message <c>startup</c> comptent, les mouvements declenches par evenement sont ceux de la tache 8.
/// </summary>
public static class JkSurfaceEffects
{
    private const uint SectorUnderwater = 0x2;
    /// <summary>Couleur du brouillard d'un secteur sous l'eau sans TINT.</summary>
    public static readonly Vector3 DefaultWaterColor = new(0.2f, 0.4f, 0.6f);
    /// <summary>Distance (unites JA) a laquelle le brouillard de l'eau est opaque.</summary>
    public const float WaterFogDistance = 512f;
    /// <summary>Degats d'une application a partir desquels la zone tue (la sante maximale de Kyle est de 100).</summary>
    private const float KillThreshold = 100f;
    /// <summary>Marge (unites JK) dont on gonfle la boite d'une surface qui blesse : le joueur doit la toucher.</summary>
    private const float SurfaceVolumeMargin = 0.05f;

    /// <summary>
    /// Volumes du niveau. Sans <paramref name="readCog"/> (nom du fichier COG -> texte, null si absent) seuls les secteurs sous l'eau sont trouves ;
    /// avec, les zones qui blessent ou tuent le sont aussi.
    /// </summary>
    public static IReadOnlyList<JkVolume> Volumes(JklLevel level, Func<string, string?>? readCog = null, ImportLog? log = null) =>
        Analyze(level, readCog, log ?? new ImportLog()).Volumes;

    /// <summary>Nom complet du shader de la boite d'eau d'une couleur de brouillard (<c>textures/jk/water_rrggbb</c>).</summary>
    public static string WaterVolumeShaderName(Vector3 color) =>
        ShaderNames.PrefixFor(KotorGame.JediKnight) + "water_" + Byte(color.X).ToString("x2") + Byte(color.Y).ToString("x2") + Byte(color.Z).ToString("x2");

    /// <summary>
    /// Shader des brushes d'eau d'une couleur : contenu eau, traversable, brouillard colore (fogParms). Les brushes sont poses par
    /// <c>JkObjects</c> (tache 7), qui ecrit ce shader dans le fichier de shaders de la carte.
    /// </summary>
    public static string WaterVolumeShader(Vector3 color)
    {
        static string C(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);
        return WaterVolumeShaderName(color) + "\n{\n"
            + "\tsurfaceparm water\n\tsurfaceparm nonsolid\n\tsurfaceparm nonopaque\n\tsurfaceparm trans\n\tsurfaceparm fog\n\tsurfaceparm nolightmap\n"
            + "\tfogParms ( " + C(color.X) + " " + C(color.Y) + " " + C(color.Z) + " ) " + C(WaterFogDistance) + "\n}\n";
    }

    private static int Byte(float c) => Math.Clamp((int)MathF.Round(c * 255f), 0, 255);

    internal static JkLevelEffects Analyze(JklLevel level, Func<string, string?>? readCog, ImportLog log)
    {
        var result = new JkLevelEffects();
        int[] sectorOfSurface = SectorOfSurface(level);

        // Eau : tout secteur marque « sous l'eau ».
        int water = 0;
        for (int s = 0; s < level.Sectors.Count; s++)
        {
            JklSector sec = level.Sectors[s];
            if ((sec.Flags & SectorUnderwater) == 0 || !SectorBounds(level, s, out Vector3 min, out Vector3 max)) continue;
            result.Volumes.Add(new JkVolume(JkVolumeKind.Water, s, min, max) { Color = sec.Tint != Vector3.Zero ? sec.Tint : DefaultWaterColor });
            water++;
        }
        if (water > 0)
            log.Warn("JK_WATER_VOLUME", $"{water} underwater sector(s) found: the scene converts the water look (translucent surface, tint); the swimmable volume (water contents) is created with the placed objects (JK_WATER_BRUSH), without underwater fog.");

        if (readCog is null) return result; // sans scripts, seule la poussee du fichier existe : elle est convertie avec les objets (JkSectorThrust)

        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        int deferred = 0, heal = 0;
        var unresolvedDamage = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenHazard = new HashSet<(JkVolumeKind, int, int)>();
        var adjoinCogs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JklCogInstance inst in level.Cogs)
        {
            if (!scripts.TryGetValue(inst.CogFile, out CogScript? script))
            {
                string? text = readCog(inst.CogFile);
                scripts[inst.CogFile] = script = text is null ? null : CogReader.Read(text);
            }
            if (script is null) continue;
            IReadOnlyDictionary<string, string> values = script.Bind(inst);
            if (ReadOpenedAdjoins(level, script, values, result)) adjoinCogs.Add(inst.CogFile);

            IReadOnlyList<CogCallSite> startup = script.StartupCalls();
            ReadStartupEffects(level, script, values, startup, result);
            deferred += script.AllCalls().Count(IsEffectCall) - startup.Count(IsEffectCall);

            if (script.CodeContains("damagething("))
            {
                if (!ReadHazards(level, script, values, sectorOfSurface, result, seenHazard, ref heal)) unresolvedDamage.Add(inst.CogFile);
            }
            else if (script.CodeContains("healthing(") && script.CodeContains("firstthinginsector("))
                heal += script.Symbols.Count(s => s.Type == "sector" && !s.Local && values.TryGetValue(s.Name, out string? v) && v != "-1");
        }

        LogThrust(log, JkSectorThrust.Analyze(level, file => scripts.GetValueOrDefault(file)));
        if (result.OpenedAdjoins.Count > 0)
            log.Warn("JK_ADJOIN_COG_OPENED", $"{result.OpenedAdjoins.Count} adjoin(s) blocked in the level file can be opened by COG scripts (force fields, grates, breakable glass, doors without a moving part): they are made passable so that no way stays closed for good; the barrier stays visible but no longer blocks. Scripts: "
                + string.Join(", ", adjoinCogs.Take(12)) + (adjoinCogs.Count > 12 ? ", ..." : "."));
        int hurt = result.Volumes.Count(v => v.Kind == JkVolumeKind.Hurt), kill = result.Volumes.Count(v => v.Kind == JkVolumeKind.Kill);
        if (hurt + kill > 0)
            log.Warn("JK_HURT_VOLUME", $"{hurt} hurting and {kill} killing zone(s) (sectors or touched surfaces) become trigger_hurt volumes.");
        if (heal > 0)
            log.Warn("JK_HEAL_SECTOR_SKIPPED", $"{heal} healing sector(s) are not converted (no JA equivalent of a healing zone).");
        if (unresolvedDamage.Count > 0)
            log.Warn("JK_DAMAGE_COG_UNRESOLVED", $"COG script(s) call DamageThing but no sector or surface became a hurt volume (event-driven damage, no sector watch or touch message, or unbound areas); this behavior is ignored: {string.Join(", ", unresolvedDamage)}.");
        if (deferred > 0)
            log.Warn("JK_SURFACE_EFFECT_DEFERRED", $"{deferred} texture slide or animation call(s) run by events (switches, timers) rather than at level start are not converted as surface effects.");
        return result;
    }

    private static bool IsEffectCall(CogCallSite c) => c.Function.ToLowerInvariant() is "slidewall" or "slidesurface" or "surfaceanim" or "materialanim";

    // ---- effets au demarrage ----

    private static void ReadStartupEffects(JklLevel level, CogScript script, IReadOnlyDictionary<string, string> values, IReadOnlyList<CogCallSite> calls, JkLevelEffects result)
    {
        var stopped = new HashSet<int>();
        var started = new List<int>();
        foreach (CogCallSite call in calls)
        {
            switch (call.Function.ToLowerInvariant())
            {
                case "slidewall" or "slidesurface" when call.Args.Length >= 2:
                {
                    List<string?> surfaces = ResolveList(call.Args[0], script, values), vectors = ResolveList(call.Args[1], script, values);
                    float? speed = call.Args.Length >= 3 ? Number(Resolve(call.Args[2], values)) : null;
                    for (int k = 0; k < surfaces.Count; k++)
                    {
                        int surface = Index(surfaces[k]);
                        string? vec = vectors.Count == 0 ? null : vectors[vectors.Count == 1 ? 0 : Math.Min(k, vectors.Count - 1)];
                        if (surface < 0 || surface >= level.Surfaces.Count || vec is null || Vector(vec) is not { } v || v.LengthSquared() < 1e-12f) continue;
                        result.Slides[surface] = new JkLevelEffects.Slide(Vector3.Normalize(v), speed ?? v.Length());
                        started.Add(surface);
                    }
                    break;
                }
                case "surfaceanim" when call.Args.Length >= 2:
                {
                    float? fps = Number(Resolve(call.Args[1], values));
                    uint flags = call.Args.Length >= 3 && Flags(Resolve(call.Args[2], values)) is { } f ? f : 1u;
                    if (fps is null or <= 0f) break;
                    foreach (string? s in ResolveList(call.Args[0], script, values))
                        if (Index(s) is >= 0 and int surface && surface < level.Surfaces.Count)
                        {
                            result.SurfaceAnims[surface] = new JkLevelEffects.Animation(fps.Value, flags);
                            started.Add(surface);
                        }
                    break;
                }
                case "materialanim" when call.Args.Length >= 2:
                {
                    float? fps = Number(Resolve(call.Args[1], values));
                    uint flags = call.Args.Length >= 3 && Flags(Resolve(call.Args[2], values)) is { } f ? f : 1u;
                    if (fps is null or <= 0f) break;
                    string? file = Resolve(call.Args[0], values);
                    if (!string.IsNullOrEmpty(file) && file != "-1") result.MaterialAnims[Path.GetFileName(file)] = new JkLevelEffects.Animation(fps.Value, flags);
                    break;
                }
                case "stopsurfaceanim" when call.Args.Length >= 1:
                    foreach (string? s in ResolveList(call.Args[0], script, values))
                        if (Index(s) is >= 0 and int surface) stopped.Add(surface);
                    break;
                case "slidehorizonsky" when call.Args.Length >= 2:
                    if (Number(Resolve(call.Args[0], values)) is { } hu && Number(Resolve(call.Args[1], values)) is { } hv) result.HorizonSlide = new Vector2(hu, hv);
                    break;
                case "slideceilingsky" when call.Args.Length >= 2:
                    if (Number(Resolve(call.Args[0], values)) is { } cu && Number(Resolve(call.Args[1], values)) is { } cv) result.CeilingSlide = new Vector2(cu, cv);
                    break;
            }
        }
        // Une surface arretee plus loin dans le meme message (apres un delai) ne tourne que quelques secondes : pas un effet permanent.
        foreach (int s in stopped)
        {
            result.Slides.Remove(s);
            result.SurfaceAnims.Remove(s);
        }
    }

    /// <summary>
    /// Poussee de secteur que la conversion ne sait pas faire : celle que des evenements changent (interrupteurs, minuteries, pulses : vent, ventilateurs, geysers, ascenseurs d'air) ou dont les arguments
    /// ne se resolvent pas. La poussee continue (fichier du niveau, <c>SectorThrust</c> du seul message <c>startup</c>) est convertie par <see cref="JkSectorThrust"/>. Un seul message avec les comptes.
    /// </summary>
    private static void LogThrust(ImportLog log, JkSectorThrust.Plan plan)
    {
        if (plan.SkippedInstances == 0) return;
        log.Warn("JK_SECTOR_THRUST_SKIPPED", $"{plan.SkippedInstances} COG instance(s) ({plan.SkippedScripts.Count} script(s)) set a sector thrust that events change at run time (switches, timers, pulses: wind, fans, geysers, lifts): "
            + "the push is not converted, the sectors stay walkable and still."
            + (plan.SkippedScripts.Count > 0 ? " Scripts: " + string.Join(", ", plan.SkippedScripts.Take(12)) + (plan.SkippedScripts.Count > 12 ? ", ..." : ".") : ""));
    }

    // ---- adjoins ouverts par des COG ----

    /// <summary>Bit « franchissable » d'un adjoin (voir <c>jk-formats.md</c>).</summary>
    private const uint AdjoinMoveBit = 0x2;

    /// <summary>
    /// Cherche les <c>SetAdjoinFlags(surface, drapeaux)</c> qui peuvent ouvrir (bit 0x2, ou drapeaux illisibles par prudence) un adjoin bloquant dans le fichier.
    /// Tous les messages comptent (interrupteur, degats, entree dans un secteur) : un COG qui ne deplace rien ne peut sinon jamais ouvrir le passage.
    /// Renvoie vrai si ce script ouvre au moins un nouvel adjoin.
    /// </summary>
    private static bool ReadOpenedAdjoins(JklLevel level, CogScript script, IReadOnlyDictionary<string, string> values, JkLevelEffects result)
    {
        bool any = false;
        foreach (CogCallSite call in script.AllCalls())
        {
            if (!call.Function.Equals("SetAdjoinFlags", StringComparison.OrdinalIgnoreCase) || call.Args.Length < 2) continue;
            uint? flags = Flags(Resolve(call.Args[1], values));
            if (flags is { } f && (f & AdjoinMoveBit) == 0) continue;
            foreach (string? s in ResolveList(call.Args[0], script, values))
            {
                if (Index(s) is not (>= 0 and int surface) || surface >= level.Surfaces.Count) continue;
                int adjoin = level.Surfaces[surface].Adjoin;
                if (adjoin < 0 || adjoin >= level.Adjoins.Count || (level.Adjoins[adjoin].Flags & AdjoinMoveBit) != 0) continue;
                if (result.OpenedAdjoins.Add(adjoin)) any = true;
            }
        }
        return any;
    }

    // ---- zones qui blessent ----

    /// <summary>Lit un script qui appelle <c>DamageThing</c> ; faux si aucun secteur ni aucune surface n'est converti : script declenche par evenement sans zone designee, ou zones non liees (cas a journaliser).</summary>
    private static bool ReadHazards(JklLevel level, CogScript script, IReadOnlyDictionary<string, string> values, int[] sectorOfSurface, JkLevelEffects result,
        HashSet<(JkVolumeKind, int, int)> seen, ref int heal)
    {
        CogCallSite? damageCall = script.AllCalls().FirstOrDefault(c => c.Function.Equals("DamageThing", StringComparison.OrdinalIgnoreCase));
        float amount = 10f;
        if (damageCall is { Args.Length: >= 2 })
        {
            float? a = Number(Resolve(damageCall.Args[1], values));
            if (a is null && values.TryGetValue("maxDamage", out string? max)) a = Number(max);
            if (a is not null) amount = a.Value;
        }
        // Intervalle entre deux impulsions : argument du premier SetPulse du script (valeur du symbole ou nombre), sinon une seconde.
        float interval = 1f;
        CogCallSite? pulse = script.AllCalls().FirstOrDefault(c => c.Function.Equals("SetPulse", StringComparison.OrdinalIgnoreCase) && c.Args.Length >= 1 && Number(Resolve(c.Args[0], values)) is > 0f);
        if (pulse is not null) interval = Number(Resolve(pulse.Args[0], values))!.Value;

        bool found = false;
        // Secteurs : le script regarde ce qui s'y trouve a chaque impulsion (FirstThingInSector), ou compare le secteur du joueur a sa liste (GetThingSector).
        bool watchesSectors = script.CodeContains("firstthinginsector(") || script.CodeContains("getthingsector(");
        if (watchesSectors)
            foreach (CogSymbol sym in script.Symbols.Where(s => s.Type == "sector" && !s.Local))
            {
                if (!values.TryGetValue(sym.Name, out string? v) || Index(v) is not (>= 0 and int sector) || sector >= level.Sectors.Count) continue;
                found = true;
                if (amount <= 0f) { heal++; continue; }
                if (!SectorBounds(level, sector, out Vector3 min, out Vector3 max)) continue;
                result.HazardSectors.Add(sector);
                AddHazard(result, seen, amount, interval, sector, -1, min, max);
            }
        // Surfaces : le script reagit au contact (messages touched / entered).
        if (script.HasMessage("touched") || script.HasMessage("entered"))
            foreach (CogSymbol sym in script.Symbols.Where(s => s.Type == "surface" && !s.Local))
            {
                if (!values.TryGetValue(sym.Name, out string? v) || Index(v) is not (>= 0 and int surface) || surface >= level.Surfaces.Count) continue;
                found = true;
                if (amount <= 0f) continue;
                if (!SurfaceBounds(level, surface, out Vector3 min, out Vector3 max)) continue;
                result.HazardSurfaces.Add(surface);
                int sector = sectorOfSurface[surface];
                Vector3 pad = new(SurfaceVolumeMargin);
                AddHazard(result, seen, amount, interval, Math.Max(sector, 0), surface, min - pad, max + pad);
            }
        return found;
    }

    private static void AddHazard(JkLevelEffects result, HashSet<(JkVolumeKind, int, int)> seen, float amount, float interval, int sector, int surface, Vector3 min, Vector3 max)
    {
        // Une zone qui blesse une seule fois au contact (surface) compte une application par seconde ; une zone a impulsions, amount / intervalle.
        float perSecond = surface >= 0 ? amount : amount / interval;
        JkVolumeKind kind = amount >= KillThreshold ? JkVolumeKind.Kill : JkVolumeKind.Hurt;
        if (!seen.Add((kind, sector, surface))) return;
        result.Volumes.Add(new JkVolume(kind, sector, min, max) { Surface = surface, Damage = perSecond });
    }

    // ---- geometrie ----

    private static int[] SectorOfSurface(JklLevel level)
    {
        var owner = new int[level.Surfaces.Count];
        Array.Fill(owner, -1);
        for (int s = 0; s < level.Sectors.Count; s++)
        {
            JklSector sec = level.Sectors[s];
            for (int k = sec.FirstSurface; k < sec.FirstSurface + sec.SurfaceCount && k < level.Surfaces.Count; k++)
                if (k >= 0) owner[k] = s;
        }
        return owner;
    }

    internal static bool SurfaceBounds(JklLevel level, int surface, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue);
        bool any = false;
        foreach (int v in level.Surfaces[surface].VertexIndices)
        {
            if (v < 0 || v >= level.Vertices.Length) continue;
            min = Vector3.Min(min, level.Vertices[v]); max = Vector3.Max(max, level.Vertices[v]);
            any = true;
        }
        return any;
    }

    internal static bool SectorBounds(JklLevel level, int sector, out Vector3 min, out Vector3 max)
    {
        min = new Vector3(float.MaxValue); max = new Vector3(float.MinValue);
        JklSector sec = level.Sectors[sector];
        bool any = false;
        for (int k = Math.Max(sec.FirstSurface, 0); k < sec.FirstSurface + sec.SurfaceCount && k < level.Surfaces.Count; k++)
            if (SurfaceBounds(level, k, out Vector3 lo, out Vector3 hi)) { min = Vector3.Min(min, lo); max = Vector3.Max(max, hi); any = true; }
        return any;
    }

    // ---- arguments d'appel ----

    /// <summary>Un identifiant prend la valeur de son symbole ; sinon le texte (nombre, vecteur) tel quel.</summary>
    internal static string Resolve(string expr, IReadOnlyDictionary<string, string> values)
    {
        expr = expr.Trim();
        return values.TryGetValue(expr, out string? v) ? v : expr;
    }

    /// <summary>Valeurs d'un argument : un acces <c>nom[i]</c> parcourt la famille de symboles <c>nom, nom1...</c> (null = symbole sans valeur) ; sinon une seule valeur.</summary>
    private static List<string?> ResolveList(string expr, CogScript script, IReadOnlyDictionary<string, string> values)
    {
        expr = expr.Trim();
        int open = expr.IndexOf('[');
        if (open > 0 && expr.EndsWith(']'))
            return script.Family(expr[..open].Trim()).Select(n => values.TryGetValue(n.Name, out string? v) ? v : null).ToList();
        return [Resolve(expr, values)];
    }

    internal static int Index(string? s) => s is not null && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : -1;

    internal static float? Number(string? s) =>
        s is not null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && float.IsFinite(f) ? f : null;

    private static uint? Flags(string s)
    {
        try { return JkTokenizer.ParseFlags(s.Trim()); }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }

    internal static Vector3? Vector(string s)
    {
        s = s.Trim();
        if (s.StartsWith('(')) return JklReader.ParseTriple(s);
        string[] p = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 3 && Number(p[0]) is { } x && Number(p[1]) is { } y && Number(p[2]) is { } z) return new Vector3(x, y, z);
        return null;
    }
}

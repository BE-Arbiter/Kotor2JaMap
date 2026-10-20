using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Jk.Core.Cog;
using Jk.Core.Jkl;

namespace Jk.Pipeline.Movers;

// Alias dans l espace de noms : CogLite (Jk.Pipeline) declare ses propres CogSymbol et CogCall internes.
using CogSymbol = Jk.Core.Cog.CogSymbol;
using CogCallSite = Jk.Core.Cog.CogCallSite;

/// <summary>Instance de COG du niveau et comportements deduits.</summary>
public sealed record ClassifiedCog(JklCogInstance Instance, IReadOnlyList<MoverBehavior> Behaviors);

/// <summary>
/// Bilan d'un niveau. <c>Cogs</c> : un element par instance. <c>Unclaimed</c> : things a images qu'aucun COG ne deplace (approximation).
/// <c>Movers</c> : things mobiles (a images, hors acteurs, ou deplaces par un COG). <c>All</c> : tous les comportements, doublons retires.
/// </summary>
public sealed record LevelMovers(IReadOnlyList<ClassifiedCog> Cogs, IReadOnlyList<MoverBehavior> Unclaimed, IReadOnlySet<int> Movers)
{
    /// <summary>Fichiers COG cites par le niveau mais absents des GOB du jeu (effets inconnus ; journal <c>JK_COG_MISSING</c>).</summary>
    public IReadOnlyList<string> MissingScripts { get; init; } = [];

    /// <summary>Scripts lus avec des anomalies (tronques, parentheses ou accolades non fermees...) : fichier -> anomalies (journal <c>JK_COG_MALFORMED</c>).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> MalformedScripts { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<MoverBehavior> All { get; } =
        Cogs.SelectMany(c => c.Behaviors).Concat(Unclaimed).DistinctBy(b => b.Key).ToList();
}

/// <summary>
/// Classement des comportements de mouvement d'un COG (tache 8). Pas d'execution du script : on lit les appels de verbes de chaque message,
/// avec les conditions englobantes, et on en tire :
/// <list type="bullet">
/// <item><c>killed</c> : <see cref="ActorTriggered"/> (l'acteur est absent : declenche a l'approche) ;</item>
/// <item><c>activate/activated/touched</c> avec une surface citee (cellule changee, condition sur l'emetteur) : <see cref="SwitchMove"/>, sinon <see cref="TouchDoor"/> ;</item>
/// <item><c>entered/crossed</c> : <see cref="TouchDoor"/> si le COG ouvre/ferme les adjoins d'un secteur, sinon <see cref="TwoStopLift"/> ;</item>
/// <item>trois images distinctes ou plus : <see cref="MultiStepPath"/> ;</item>
/// <item>verbes de mouvement autres que <c>MoveToFrame</c> (<c>RotatePivot</c>, <c>SetThingVel</c>...) ou declencheurs inconnus : <see cref="Approximate"/>
///   (le thing a des images) ou <see cref="Unsupported"/> (il n'en a pas).</item>
/// </list>
/// Un thing mobile reste toujours classe : jamais de disparition (la tache 9 le place en position « passage ouvert » pour les cas non reconnus).
/// </summary>
public static class CogClassifier
{
    private static readonly HashSet<string> FrameVerbs = new(StringComparer.OrdinalIgnoreCase) { "MoveToFrame", "JumpToFrame", "SkipToFrame" };

    /// <summary>Verbes qui deplacent un thing sans passer par ses images ; sans images on ne peut pas les reconstruire.</summary>
    private static readonly HashSet<string> OtherMotionVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "RotatePivot", "Rotate", "SetThingRotVel", "MoveThing", "SetPathMoveFlags",
    };

    /// <summary>Verbes de deplacement a l execution (poussees, teleportation du drapeau CTF) : ils ne comptent que pour un thing a images, jamais pour un objet ordinaire.</summary>
    private static readonly HashSet<string> RuntimeMotionVerbs = new(StringComparer.OrdinalIgnoreCase) { "SetThingVel", "SetThingPos", "SetThingLook" };

    private static readonly HashSet<string> TimerVerbs = new(StringComparer.OrdinalIgnoreCase) { "SetTimer", "SetTimerEx" };

    private enum Trigger { Use, Zone, Killed, Damage, User, FollowUp, Startup, Other }

    private static Trigger KindOf(string message) => message.ToLowerInvariant() switch
    {
        "activate" or "activated" or "touched" => Trigger.Use,
        "entered" or "crossed" => Trigger.Zone,
        "killed" => Trigger.Killed,
        "damaged" => Trigger.Damage,
        var m when m.StartsWith("user", StringComparison.Ordinal) => Trigger.User,
        "arrived" or "timer" or "pulse" or "blocked" => Trigger.FollowUp,
        "startup" => Trigger.Startup,
        _ => Trigger.Other,
    };

    // ---- infos de niveau (mises en cache par niveau) ----

    private sealed class LevelInfo
    {
        public Dictionary<int, JklThing> Things { get; } = [];
        public HashSet<int> Actors { get; } = [];
        public Dictionary<int, int> Frames { get; } = [];
    }

    private static readonly ConditionalWeakTable<JklLevel, LevelInfo> Infos = new();

    private static LevelInfo Info(JklLevel lvl) => Infos.GetValue(lvl, l =>
    {
        var info = new LevelInfo();
        var templates = JkTemplates.FromLevels(null, l);
        foreach (JklThing t in l.Things)
        {
            info.Things[t.Index] = t;
            var merged = new Dictionary<string, string>(templates.Resolve(t.Template), StringComparer.OrdinalIgnoreCase);
            foreach (var kv in t.Params) merged[kv.Key] = kv.Value;
            if (merged.TryGetValue("type", out string? type) && type.Equals("actor", StringComparison.OrdinalIgnoreCase)) info.Actors.Add(t.Index);
            int frames = merged.TryGetValue("frame", out string? f) ? JklReader.ReadFrames(f).Count : 0;
            if (frames == 0 && merged.TryGetValue("numframes", out string? nf) && int.TryParse(nf, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) frames = n;
            info.Frames[t.Index] = frames;
        }
        return info;
    });

    /// <summary>Things mobiles par leurs images : au moins deux images, hors acteurs (ils ne sont pas places).</summary>
    public static IEnumerable<int> FramedThings(JklLevel lvl)
    {
        LevelInfo info = Info(lvl);
        return info.Things.Keys.Where(i => info.Frames[i] >= 2 && !info.Actors.Contains(i)).Order();
    }

    // ---- niveau ----

    /// <summary>Classe tous les COG d'un niveau. <paramref name="readCog"/> lit le texte d'un fichier COG (<c>null</c> : introuvable).</summary>
    public static LevelMovers ClassifyLevel(JklLevel lvl, Func<string, string?> readCog)
    {
        LevelInfo info = Info(lvl);
        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        var cogs = new List<ClassifiedCog>();
        var referencedBy = new Dictionary<int, SortedSet<string>>();
        var movers = new HashSet<int>(FramedThings(lvl));
        var missing = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var malformed = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (JklCogInstance inst in lvl.Cogs)
        {
            if (!scripts.TryGetValue(inst.CogFile, out CogScript? script))
            {
                string? text = readCog(inst.CogFile);
                scripts[inst.CogFile] = script = text is null ? null : CogReader.Read(text);
                if (script is { Warnings.Count: > 0 }) malformed[inst.CogFile] = script.Warnings;
            }
            if (script is null)
            {
                missing.Add(inst.CogFile);
                cogs.Add(new ClassifiedCog(inst, []));
                continue;
            }
            BoundCog bound = CogBinding.Bind(script, inst, lvl);
            IReadOnlyList<MoverBehavior> found = Classify(bound, lvl);
            cogs.Add(new ClassifiedCog(inst, found));
            foreach (MoverBehavior b in found) if (b.Thing >= 0) movers.Add(b.Thing);
            foreach (CogSymbol s in script.Symbols.Where(s => s.Type == "thing"))
                if (bound.Int(s.Name) is int t && info.Things.ContainsKey(t))
                {
                    if (!referencedBy.TryGetValue(t, out var set)) referencedBy[t] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(inst.CogFile);
                }
        }

        var claimed = cogs.SelectMany(c => c.Behaviors).Where(b => b.Thing >= 0).Select(b => b.Thing).ToHashSet();
        var unclaimed = new List<MoverBehavior>();
        foreach (int t in FramedThings(lvl))
        {
            if (claimed.Contains(t)) continue;
            unclaimed.Add(referencedBy.TryGetValue(t, out var by)
                ? new Approximate(t, $"COG {string.Join(", ", by)} references this thing but none of its messages moves it with a known verb; the frames are kept as a path.")
                : new Approximate(t, "no COG moves this thing; the frames are kept as a path."));
        }
        return new LevelMovers(cogs, unclaimed, movers) { MissingScripts = [.. missing], MalformedScripts = malformed };
    }

    // ---- une instance ----

    private sealed record Move(string Message, Trigger Kind, CogCallSite Site, IReadOnlyList<int> Things, int? Frame, float? Speed, IReadOnlyList<CogCallSite> Context, bool Chained = false);

    public static IReadOnlyList<MoverBehavior> Classify(BoundCog cog, JklLevel lvl)
    {
        LevelInfo info = Info(lvl);
        CogScript script = cog.Script;
        var contexts = script.MessageNames.ToDictionary(n => n, n => script.Expand(n), StringComparer.OrdinalIgnoreCase);

        var moves = new List<Move>();
        var others = new Dictionary<int, SortedSet<string>>();
        foreach (var (message, sites) in contexts)
        {
            Trigger kind = KindOf(message);
            foreach (CogCallSite site in sites)
            {
                if (site.Args.Length == 0) continue;
                bool frameVerb = FrameVerbs.Contains(site.Function);
                bool runtime = RuntimeMotionVerbs.Contains(site.Function);
                if (!frameVerb && !runtime && !OtherMotionVerbs.Contains(site.Function)) continue;
                IReadOnlyList<int> things = Resolve(cog, site.Args[0], "thing").Where(info.Things.ContainsKey).Where(t => !info.Actors.Contains(t)).ToList();
                if (runtime) things = things.Where(t => info.Frames[t] >= 2).ToList();
                if (things.Count == 0) continue;
                if (frameVerb)
                {
                    int? frame = site.Args.Length > 1 && cog.Number(site.Args[1]) is double f ? (int)Math.Round(f) : null;
                    float? speed = site.Args.Length > 2 && cog.Number(site.Args[2]) is double s ? (float)s : null;
                    moves.Add(new Move(message, kind, site, things, frame, speed, sites));
                }
                else foreach (int t in things) { if (!others.TryGetValue(t, out var verbs)) others[t] = verbs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase); verbs.Add(site.Function); }
            }
        }

        var result = new List<MoverBehavior>();
        var thingsMoved = moves.SelectMany(m => m.Things).Distinct().Order().ToList();
        foreach (int t in thingsMoved)
            ClassifyThing(cog, lvl, info, t, moves.Where(m => m.Things.Contains(t)).ToList(), moves, contexts, result);

        foreach (var (t, verbSet) in others.Where(o => !thingsMoved.Contains(o.Key)).OrderBy(o => o.Key))
        {
            string verbs = string.Join(", ", verbSet);
            result.Add(info.Frames[t] >= 2
                ? new Approximate(t, $"{cog.File} moves this thing with {verbs}, which is not reproduced; its frames are kept as a path.") { Cog = cog.File }
                : new Unsupported(t, $"{cog.File} moves this thing with {verbs}, which is not reproduced, and the thing has no frames to rebuild the motion from.") { Cog = cog.File });
        }

        if (!result.Any(b => b.Thing >= 0))
            foreach (int surface in CelSurfaces(cog, contexts))
                result.Add(new SurfaceCelSwitch(surface) { Cog = cog.File });
        return [.. result.DistinctBy(b => b.Key)];
    }

    private static void ClassifyThing(BoundCog cog, JklLevel lvl, LevelInfo info, int thing, List<Move> moves, List<Move> allMoves,
        Dictionary<string, IReadOnlyList<CogCallSite>> contexts, List<MoverBehavior> result)
    {
        string file = cog.File;
        var sequence = moves.Where(m => m.Frame is not null).Select(m => m.Frame!.Value).ToList();
        int[] distinct = sequence.Distinct().ToArray();
        float? speed = moves.Select(m => m.Speed).FirstOrDefault(s => s is not null);
        (float? delay, string? delayNote) = Delay(cog, thing, moves, allMoves, contexts);
        IReadOnlyList<string> sounds = Sounds(cog, moves);

        MoverBehavior With(MoverBehavior b) => b with { Cog = file, Speed = speed, Delay = delay, DelayNote = delayNote, Sounds = sounds, FrameSequence = distinct };

        if (info.Frames[thing] < 2)
        {
            result.Add(new Unsupported(thing, $"{file} moves this thing with {string.Join(", ", moves.Select(m => m.Site.Function).Distinct())}, but the thing has no frames to move between.") { Cog = file });
            return;
        }

        var triggers = moves.Where(m => m.Kind is not (Trigger.FollowUp or Trigger.Startup)).ToList();

        // Mouvement qui ne vient que de messages de suite (arrived, timer, pulse...) : il est lance par les messages declencheurs du COG
        // (qui arment la minuterie, envoient un message ou deplacent autre chose).
        if (triggers.Count == 0)
        {
            var chained = Chain(cog, moves, contexts);
            if (chained.Count > 0) { moves = [.. moves, .. chained]; triggers = chained; }
        }

        // Mort d'un acteur : l'acteur est absent, on declenche a l'approche.
        if (triggers.Any(m => m.Kind == Trigger.Killed))
        {
            result.Add(With(new ActorTriggered(thing, info.Things[thing].Sector)));
            triggers = triggers.Where(m => m.Kind != Trigger.Killed).ToList();
        }

        if (triggers.Count == 0)
        {
            if (moves.Any(m => m.Kind == Trigger.Killed)) return;
            // Aucun declencheur : mouvement lance par minuterie, pulsation ou au demarrage seulement.
            // Va-et-vient lance par le COG lui-meme (ascenseur automatique) : boucle entre ses positions.
            if (distinct.Length >= 2)
                result.Add(With(new MultiStepPath(thing, distinct, delay ?? 0f, true)));
            else
                result.Add(With(new Approximate(thing, $"{file} moves this thing once, to frame {(distinct.Length == 1 ? distinct[0].ToString() : "?")}, from a startup, timer or pulse message (no player trigger); its frames are kept as a path.")));
            return;
        }

        var switches = SwitchMoves(cog, thing, moves, triggers, contexts);
        bool added = false;
        if (switches.Count > 0)
        {
            foreach (var (surface, frames) in switches)
                result.Add(With(new SwitchMove(thing, surface, frames)));
            added = true;
        }

        // Zone (adjoin, secteur) ou utilisation sans interrupteur nomme.
        var automatic = triggers.Where(m => m.Kind == Trigger.Zone).ToList();
        var used = triggers.Where(m => m.Kind == Trigger.Use && switches.Count == 0).ToList();
        if (switches.Count == 0 && (automatic.Count > 0 || used.Count > 0) && IsSequential(moves))
        {
            // Garde : sans image resolue (MoveToFrame vers une variable d'execution) il n'y a pas de suite a reconstruire.
            if (sequence.Count > 0)
            {
                result.Add(With(new MultiStepPath(thing, [.. sequence.Distinct()], delay ?? 0f, sequence.Skip(1).Contains(0) || sequence.Skip(1).Contains(sequence[0]))));
                return;
            }
        }
        if (automatic.Count > 0)
        {
            bool togglesAdjoins = cog.Script.Handlers.SelectMany(h => h.Calls).Any(c => c.Function.Equals("SectorAdjoins", StringComparison.OrdinalIgnoreCase)
                || c.Function.Equals("SetSectorAdjoins", StringComparison.OrdinalIgnoreCase));
            result.Add(togglesAdjoins ? With(new TouchDoor(thing)) : With(new TwoStopLift(thing)));
            added = true;
        }
        if (used.Count > 0) { result.Add(With(new TouchDoor(thing))); added = true; }

        var unknown = triggers.Where(m => m.Kind is Trigger.Damage or Trigger.User or Trigger.Other).ToList();
        if (unknown.Count > 0 && !added)
        {
            string messages = string.Join(", ", unknown.Select(m => m.Message).Distinct());
            result.Add(With(new Approximate(thing, $"{file} moves this thing on the '{messages}' message ({(unknown.Any(m => m.Kind == Trigger.Damage) ? "damage" : "script event")}), which has no direct Jedi Academy trigger; its frames are kept as a path.")));
        }
        else if (!added && unknown.Count == 0)
            result.Add(With(new Approximate(thing, $"{file} moves this thing in a way that is not recognized; its frames are kept as a path.")));
    }

    /// <summary>
    /// Etapes successives (et non deux destinations au choix) : un message de suite deplace vers une image nouvelle (ni 0, ni une image deja demandee
    /// par le declencheur), ou deux deplacements sans condition vers des images differentes dans un meme message.
    /// </summary>
    private static bool IsSequential(List<Move> moves)
    {
        var real = moves.Where(m => !m.Chained && m.Frame is not null).ToList();
        var triggerFrames = real.Where(m => m.Kind is not (Trigger.FollowUp or Trigger.Startup)).Select(m => m.Frame!.Value).ToHashSet();
        if (real.Any(m => m.Kind == Trigger.FollowUp && m.Frame != 0 && !triggerFrames.Contains(m.Frame!.Value))) return true;
        return real.GroupBy(m => m.Message).Any(g => g.Where(m => m.Site.Conditions.Count == 0).Select(m => m.Frame!.Value).Distinct().Count() >= 2);
    }

    /// <summary>Deplacements de suite d un thing rattaches aux messages declencheurs du COG (copies marquees <c>Chained</c>).</summary>
    private static List<Move> Chain(BoundCog cog, List<Move> moves, Dictionary<string, IReadOnlyList<CogCallSite>> contexts)
    {
        var follow = moves.Where(m => m.Kind == Trigger.FollowUp).ToList();
        if (follow.Count == 0) return [];
        var causal = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SetTimer", "SetTimerEx", "SetPulse", "MoveToFrame", "JumpToFrame", "SendMessage", "SendMessageEx", "SetWallCel" };
        var candidates = contexts.Where(kv => KindOf(kv.Key) is Trigger.Use or Trigger.Zone or Trigger.Killed or Trigger.Damage or Trigger.User or Trigger.Other && kv.Value.Count > 0).ToList();
        var chosen = candidates.Where(kv => kv.Value.Any(c => causal.Contains(c.Function))).ToList();
        if (chosen.Count == 0) chosen = candidates;
        return [.. chosen.SelectMany(kv => follow.Select(m => m with { Message = kv.Key, Kind = KindOf(kv.Key), Context = kv.Value, Chained = true }))];
    }

    /// <summary>Interrupteurs d'un thing : surfaces citees par les messages d'utilisation, avec les images que chacune envoie, puis les retours.</summary>
    private static List<(int Surface, int[] Frames)> SwitchMoves(BoundCog cog, int thing, List<Move> all, List<Move> triggers,
        Dictionary<string, IReadOnlyList<CogCallSite>> contexts)
    {
        var bySurface = new Dictionary<int, List<int>>();
        foreach (Move m in triggers.Where(m => m.Kind is Trigger.Use or Trigger.User))
        {
            var surfaces = SurfacesIn(cog, m.Site.Conditions.Where(c => !c.StartsWith("!(", StringComparison.Ordinal))).ToList();
            if (surfaces.Count == 0) surfaces = [.. SurfacesInCalls(cog, m.Context)];
            foreach (int s in surfaces)
            {
                if (!bySurface.TryGetValue(s, out var list)) bySurface[s] = list = [];
                if (m.Frame is int f) list.Add(f);
            }
        }
        var followUps = all.Where(m => m.Kind == Trigger.FollowUp && m.Frame is not null).Select(m => m.Frame!.Value).ToList();
        var result = new List<(int, int[])>();
        foreach (var (surface, frames) in bySurface.OrderBy(kv => kv.Key))
            result.Add((surface, [.. frames.Concat(followUps).Distinct()]));
        return result;
    }

    private static IEnumerable<int> SurfacesIn(BoundCog cog, IEnumerable<string> texts)
    {
        foreach (CogSymbol s in cog.Script.Symbols.Where(s => s.Type == "surface"))
            if (texts.Any(t => Regex.IsMatch(t, @"\b" + Regex.Escape(s.Name) + @"\b(?!\s*\[)", RegexOptions.IgnoreCase)) && cog.Int(s.Name) is int v && v >= 0)
                yield return v;
    }

    /// <summary>Surfaces dont le message change la cellule ou lit l'etat (<c>SetWallCel</c>, <c>GetWallCel</c>) : ce sont les interrupteurs.</summary>
    private static IEnumerable<int> SurfacesInCalls(BoundCog cog, IEnumerable<CogCallSite> sites)
    {
        foreach (CogCallSite c in sites)
        {
            if (c.Args.Length == 0) continue;
            if (!c.Function.Equals("SetWallCel", StringComparison.OrdinalIgnoreCase) && !c.Function.Equals("GetWallCel", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (int s in Resolve(cog, c.Args[0], "surface")) yield return s;
        }
    }

    private static IEnumerable<int> CelSurfaces(BoundCog cog, Dictionary<string, IReadOnlyList<CogCallSite>> contexts)
    {
        var seen = new HashSet<int>();
        foreach (var (message, sites) in contexts)
        {
            if (KindOf(message) == Trigger.Startup) continue;
            foreach (int s in SurfacesInCalls(cog, sites.Where(c => c.Function.Equals("SetWallCel", StringComparison.OrdinalIgnoreCase))))
                if (seen.Add(s)) yield return s;
        }
    }

    /// <summary>
    /// Delai avant le retour : premier <c>SetTimer</c> / <c>SetTimerEx</c> hors demarrage ; sinon un <c>Sleep</c> place apres un
    /// <c>MoveToFrame</c> du meme message. <c>null</c> si absent ou negatif (la porte reste ouverte).
    /// </summary>
    private static (float? Delay, string? Note) Delay(BoundCog cog, int thing, List<Move> moves, List<Move> allMoves,
        Dictionary<string, IReadOnlyList<CogCallSite>> contexts)
    {
        // Things relies entre eux par un meme deplacement (une porte a plusieurs pieces) : ils partagent la minuterie.
        HashSet<int> group = [thing];
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (Move m in allMoves)
                if (m.Things.Any(group.Contains)) foreach (int t in m.Things) grew |= group.Add(t);
        }
        // Un seul groupe de things dans le COG : toutes ses minuteries sont les siennes. Sinon une minuterie ne compte que si son message
        // deplace ce groupe ou cite l'un de ses things.
        bool single = allMoves.All(m => m.Things.Any(group.Contains));
        bool Relevant(string message, IReadOnlyList<CogCallSite> sites) =>
            single || allMoves.Any(m => m.Message.Equals(message, StringComparison.OrdinalIgnoreCase) && m.Things.Any(group.Contains)) || Mentions(cog, sites, group);

        double? foreign = null;
        foreach (var (message, sites) in contexts)
        {
            if (KindOf(message) == Trigger.Startup) continue;
            bool relevant = Relevant(message, sites);
            foreach (CogCallSite c in sites)
                if (TimerVerbs.Contains(c.Function) && c.Args.Length > 0 && cog.Number(c.Args[0]) is double d)
                {
                    if (relevant) return (d >= 0 ? (float)d : null, null);
                    foreign ??= d;
                }
        }
        foreach (var (message, sites) in contexts)
        {
            if (KindOf(message) != Trigger.FollowUp || !Relevant(message, sites)) continue;
            foreach (CogCallSite c in sites)
                if (c.Function.Equals("Sleep", StringComparison.OrdinalIgnoreCase) && c.Args.Length > 0 && cog.Number(c.Args[0]) is double d)
                    return (d >= 0 ? (float)d : null, null);
        }
        foreach (Move m in moves)
        {
            bool afterMove = false;
            foreach (CogCallSite c in m.Context)
            {
                if (ReferenceEquals(c, m.Site)) afterMove = true;
                else if (afterMove && c.Function.Equals("Sleep", StringComparison.OrdinalIgnoreCase) && c.Args.Length > 0 && cog.Number(c.Args[0]) is double d)
                    return (d >= 0 ? (float)d : null, null);
            }
        }
        return (null, foreign is double f
            ? FormattableString.Invariant($"{cog.File} arms a timer ({f:0.##} s) that cannot be tied to this thing (the COG moves several independent things); the delay is left unset.")
            : null);
    }

    /// <summary>Vrai si un appel cite un thing du groupe (symbole <c>nom</c>, ou <c>nom[i]</c> : toute la famille) dans ses arguments ou ses conditions.</summary>
    private static bool Mentions(BoundCog cog, IEnumerable<CogCallSite> sites, HashSet<int> group)
    {
        foreach (CogCallSite c in sites)
            foreach (string text in c.Args.Concat(c.Conditions))
                foreach (Match m in Regex.Matches(text, @"\b([A-Za-z_]\w*)\b(\s*\[)?"))
                {
                    CogSymbol? sym = cog.Script.Symbol(m.Groups[1].Value);
                    if (sym is not { Type: "thing" }) continue;
                    IEnumerable<CogSymbol> members = m.Groups[2].Success ? cog.Script.Family(sym.Name) : [sym];
                    if (members.Any(s => cog.Int(s.Name) is int v && group.Contains(v))) return true;
                }
        return false;
    }

    private static IReadOnlyList<string> Sounds(BoundCog cog, List<Move> moves)
    {
        var sounds = new List<string>();
        foreach (Move m in moves)
            foreach (CogCallSite c in m.Context)
            {
                if (!c.Function.StartsWith("PlaySound", StringComparison.OrdinalIgnoreCase) || c.Args.Length == 0) continue;
                string arg = c.Args[0].Trim();
                string? name = cog.Script.Symbol(arg)?.Type == "sound" ? cog.Get(arg) : null;
                if (!string.IsNullOrEmpty(name) && !sounds.Contains(name, StringComparer.OrdinalIgnoreCase)) sounds.Add(name);
            }
        return sounds;
    }

    /// <summary>
    /// Valeurs entieres designees par une expression : un symbole du type demande, un nombre, ou <c>nom[i]</c> (famille <c>nom</c>, <c>nom1</c>...,
    /// tous les membres si l'indice n'est pas un nombre). Liste vide si l'expression n'est pas resolvable (variable d'execution, appel).
    /// </summary>
    private static IReadOnlyList<int> Resolve(BoundCog cog, string expr, string type)
    {
        expr = expr.Trim();
        if (Regex.Match(expr, @"^(\w+)\s*\[(.*)\]$", RegexOptions.Singleline) is { Success: true } idx)
        {
            IReadOnlyList<CogSymbol> family = cog.Script.Family(idx.Groups[1].Value);
            if (family.Count == 0 || family[0].Type != type) return [];
            if (cog.Number(idx.Groups[2].Value) is double n)
                return family.ElementAtOrDefault((int)n) is { } one && cog.Int(one.Name) is int v && v >= 0 ? [v] : [];
            return [.. family.Select(s => cog.Int(s.Name)).Where(v => v is >= 0).Select(v => v!.Value)];
        }
        if (!Regex.IsMatch(expr, @"^\w+$")) return [];
        CogSymbol? sym = cog.Script.Symbol(expr);
        if (sym is not null) return sym.Type == type && cog.Int(sym.Name) is int value && value >= 0 ? [value] : [];
        return cog.Number(expr) is double lit && lit >= 0 ? [(int)lit] : [];
    }
}

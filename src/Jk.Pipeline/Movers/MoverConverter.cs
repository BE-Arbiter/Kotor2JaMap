using System.Globalization;
using System.Numerics;
using Jk.Core.Jkl;
using Jk.Core.Models;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline.Movers;

/// <summary>Constantes de vitesse et de delai des elements mobiles.</summary>
public static class JkMoverSpeed
{
    /// <summary>
    /// Unites JK par seconde pour une unite de vitesse d'un <c>MoveToFrame</c>. Non verifie en jeu (le jeu n'est jamais lance) : mesure de vraisemblance sur les COG,
    /// une porte de 0,25 u ouverte a la vitesse 8 (<c>00_door</c>) met 0,3 s et un ascenseur de 2,4 u a la vitesse 4 (<c>00_elev_switch</c>) 6 s ; lue comme des unites JK par seconde
    /// elle durerait 0,03 s, ce qui n'est pas ce qu'on voit dans le jeu.
    /// </summary>
    public const float JkUnitsPerSecondPerCogSpeed = 0.1f;

    /// <summary>Vitesse d'un COG qui n'en donne pas.</summary>
    public const float DefaultCogSpeed = 4f;

    /// <summary>Delai (secondes) avant le retour d'une porte ou d'un ascenseur dont le COG ne donne pas de delai : le passage doit rester franchissable.</summary>
    public const float DefaultWait = 3f;
}

/// <summary>
/// Elements mobiles d'un niveau JK -> entites JA (spec §5.5). Chaque thing mobile devient une entite deplacable avec un sous-modele BSP de collision
/// (boite orientee du modele, relative a l'<c>origin</c> de l'entite, comme <c>DoorConverter</c>) et <c>model2</c> = son MD3.
/// <list type="bullet">
/// <item><see cref="TouchDoor"/>, <see cref="TwoStopLift"/>, <see cref="MultiStepPath"/> a deux positions, <see cref="Approximate"/> a deux images : <c>func_door</c> qui se declenche seule
///   (declencheur de proximite du moteur) ; un ascenseur qui ne fait que monter : <c>func_plat</c>.</item>
/// <item><see cref="SwitchMove"/> : <c>func_door</c> a nom (<c>jkm&lt;thing&gt;</c>) tire par un <c>trigger_multiple</c> USE_BUTTON autour de la surface interrupteur (un relais par piece
///   quand la meme surface en commande plusieurs) ; sans retour automatique, la porte bascule (TOGGLE). Un meme thing a la fois <see cref="TwoStopLift"/> et <see cref="SwitchMove"/>
///   (<c>00_elev_switch</c>) : un seul mobile, declenche par le passage ET par le bouton.</item>
/// <item><see cref="ActorTriggered"/> : porte tiree par un <c>trigger_multiple</c> unique sur le secteur du thing, qui reste ouverte ; journal <c>JK_COG_ACTOR_TRIGGER</c>.</item>
/// <item>Trois positions ou plus (<see cref="MultiStepPath"/>, <see cref="Approximate"/>, ascenseur a plusieurs destinations) : <c>func_train</c> + un <c>path_corner</c> par image,
///   lance une fois a l'approche (un train ne repart pas : il boucle si le COG boucle).</item>
/// <item>Rotation seule : SWGL n'a pas de <c>func_door_rotating</c> (g_spawn.cpp) et un <c>func_rotating</c> arrete revient a son angle de depart (g_mover.cpp, <c>func_rotating_use</c>) :
///   le thing reste visible dans la position de l'image qui laisse le passage ouvert (<c>func_static</c> sans collision) ; journal <c>JK_COG_APPROX</c>.</item>
/// <item><see cref="Unsupported"/> : decor fige dans la position la plus eloignee du repos (passage ouvert), <c>JK_COG_UNSUPPORTED</c>.</item>
/// <item><see cref="SurfaceCelSwitch"/> : aucun mouvement, la surface garde sa premiere cellule (<c>JK_COG_CEL_STATIC</c>, compte).</item>
/// </list>
/// Un thing mobile ne disparait jamais : chaque cas aboutit a une entite visible ou a un message du journal (modele absent : <c>JK_MOVER_NO_MODEL</c>).
/// </summary>
public static class MoverConverter
{
    /// <summary>Rotation (degres) en dessous de laquelle l'ecart d'orientation entre images est du bruit d'editeur (0,1 a 0,8 degre observes sur les portes).</summary>
    private const float MinRotationDegrees = 2f;

    /// <summary>Course (unites JK) en dessous de laquelle un mouvement qui tourne est considere comme une rotation seule (portes de tuyau : 0,006 a 0,02).</summary>
    private const float RotationOnlyTravelJku = 0.05f;

    /// <summary>Course (unites JA) en dessous de laquelle deux images sont la meme position.</summary>
    private const float MinTravel = 1f;

    /// <summary>Marge (unites JA) du declencheur de proximite autour du mobile au repos : autour, au-dessus (debout sur un plateau), en dessous.</summary>
    private const float ApproachMargin = 48f, ApproachAbove = 80f, ApproachBelow = 16f;

    /// <summary>Portee (unites JA) du bouton « utiliser » autour d'une surface interrupteur, et marge autour du secteur d'un mobile declenche par un acteur.</summary>
    private const float UseReach = 40f, ActorMargin = 16f;

    private const float MinWait = 0.1f, MinSpeed = 10f;

    /// <summary>Deux ascenseurs sont « purement verticaux » quand le deplacement horizontal reste sous cette part de la course.</summary>
    private const float VerticalTolerance = 0.02f;

    private const int SpawnToggle = 8, TriggerUseButton = 4;

    /// <summary>Nombre de caracteres des listes de things dans les messages du journal.</summary>
    private const int ListedThings = 12;

    /// <param name="behaviors">Comportements classes par <see cref="CogClassifier"/> (une entree par comportement ; un thing peut en avoir plusieurs).</param>
    /// <param name="models">MD3 de chaque thing mobile (indice du thing) ; un thing absent est journalise <c>JK_MOVER_NO_MODEL</c>.</param>
    /// <param name="scale">Unites JA par metre de la scene (<c>ExportOptions.Scale</c>).</param>
    /// <param name="firstSubModel">Indice (<c>*n</c>) du premier sous-modele rendu : les suivants se numerotent a la suite.</param>
    public static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Convert(IReadOnlyList<MoverBehavior> behaviors, JkPreparedScene p,
        IReadOnlyDictionary<int, JkMd3> models, float scale, int firstSubModel, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(behaviors);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(log);
        if (!(scale > 0) || !float.IsFinite(scale)) throw new ArgumentOutOfRangeException(nameof(scale));
        var builder = new Builder(p, models, scale, Math.Max(1, firstSubModel), log);
        foreach (var group in behaviors.Where(b => b.Thing >= 0).GroupBy(b => b.Thing).OrderBy(g => g.Key))
            builder.Thing(group.Key, [.. group]);
        builder.Finish(behaviors.OfType<SurfaceCelSwitch>().Select(b => b.Surface).Distinct().Count());
        return builder.Result();
    }

    // ---------------------------------------------------------------- construction

    private sealed class Builder
    {
        private readonly JkPreparedScene _p;
        private readonly JklLevel _level;
        private readonly IReadOnlyDictionary<int, JkMd3> _models;
        private readonly Dictionary<int, JklThing> _things = [];
        private readonly float _scale, _k;
        private readonly int _first;
        private readonly ImportLog _log;
        private readonly List<IReadOnlyDictionary<string, string>> _entities = [];
        private readonly List<SubModelInput> _subs = [];
        private readonly Dictionary<string, Md3Asset> _assets = new(StringComparer.Ordinal);
        /// <summary>Surface interrupteur -> targetname des mobiles qu'elle commande.</summary>
        private readonly SortedDictionary<int, SortedSet<string>> _switches = [];
        private readonly SortedDictionary<string, int> _counts = new(StringComparer.Ordinal);
        private readonly SortedSet<int> _noModel = [];
        private readonly SortedDictionary<int, string> _scaled = [];
        private int _static, _switchTriggers, _approachTriggers;
        /// <summary>Things degrades en decor sans sous-modele (limite du moteur atteinte) et nombre de declencheurs non places pour la meme raison.</summary>
        private readonly SortedSet<int> _overLimit = [];
        private int _skippedTriggers;

        public Builder(JkPreparedScene p, IReadOnlyDictionary<int, JkMd3> models, float scale, int first, ImportLog log)
        {
            _p = p; _level = p.Level; _models = models; _scale = scale; _first = first; _log = log;
            _k = JkWorldScale.MetresPerJkUnit * scale;
            foreach (JklThing t in _level.Things) _things.TryAdd(t.Index, t);
        }

        public (ConvertedEntities, IReadOnlyList<SubModelInput>) Result() =>
            (new ConvertedEntities(_entities, [.. _assets.Values.OrderBy(a => a.Path, StringComparer.Ordinal)], []), _subs);

        // ---- un thing mobile ----

        /// <summary>Un thing mobile : position, images, boite orientee.</summary>
        private sealed record Ctx(JklThing Thing, JkMd3 Md3, string Name, string Cog, IReadOnlyList<Vector3> Positions /* JA, par image */,
            IReadOnlyList<Vector3> Rotations /* degres, ecart a l'image 0 */, Matrix4x4 Rot, Vector3 BoxMin, Vector3 BoxMax);

        public void Thing(int id, List<MoverBehavior> g)
        {
            if (!_things.TryGetValue(id, out JklThing? t)) { _log.Warn("JK_MOVER_NO_THING", $"Mover behavior refers to thing {id}, which the level does not contain."); return; }
            if (!_models.TryGetValue(id, out JkMd3? md3)) { _noModel.Add(id); return; }
            _assets[md3.Asset.Path] = md3.Asset;
            if (md3.ModelScale > 1) _scaled[id] = $"{t.Template} (model {md3.Asset.Path} x{md3.ModelScale})";

            var frames = FramesOf(t);
            Vector3 shift = frames.Count > 0 ? t.Position - frames[0].Pos : Vector3.Zero;
            Vector3 origin = t.Position;
            var positions = new List<Vector3>();
            var rotations = new List<Vector3>();
            if (frames.Count >= 2)
                for (int i = 0; i < frames.Count; i++)
                {
                    positions.Add((frames[i].Pos + shift) * _k);
                    rotations.Add(Wrap(frames[i].Pyr - frames[0].Pyr));
                }
            else { positions.Add(origin * _k); rotations.Add(Vector3.Zero); }

            string cog = string.Join(", ", g.Select(b => b.Cog).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var ctx = new Ctx(t, md3, $"Thing {id} ({t.Template})", cog, positions, rotations, ThreeDoModel.Rotation(t.PitchYawRoll),
                md3.Min * _scale, md3.Max * _scale);

            // Limite de sous-modeles du moteur : le corps du mobile et au moins un declencheur doivent tenir ; sinon le thing reste visible, sans mouvement ni collision.
            if (_first + _subs.Count + 1 > JkObjects.MaxSubModels)
            {
                _overLimit.Add(id);
                var fallback = new Dictionary<string, string>
                {
                    ["classname"] = "misc_model_static", ["model"] = md3.Asset.Path, ["origin"] = Vec(positions[0]), ["angles"] = Angles(t.PitchYawRoll),
                };
                if (md3.ModelScale > 1) fallback["modelscale"] = md3.ModelScale.ToString(CultureInfo.InvariantCulture);
                _entities.Add(fallback);
                Count("misc_model_static");
                return;
            }

            var unsupported = g.OfType<Unsupported>().FirstOrDefault();
            var real = g.Where(b => b is not (Unsupported or Approximate or SurfaceCelSwitch)).ToList();
            var approx = g.OfType<Approximate>().FirstOrDefault();

            if (positions.Count < 2)
            {
                // Aucune image : rien a deplacer. Le thing reste visible a sa place, sans collision (rien ne bloque le passage).
                EmitStatic(ctx, 0, solid: false);
                _log.Warn(unsupported is not null ? "JK_COG_UNSUPPORTED" : "JK_COG_APPROX",
                    $"{ctx.Name}: its movement ({(unsupported?.Reason ?? approx?.Reason ?? "no known behavior")}) cannot be rebuilt because the thing has no frames{Via(ctx)}; it stays visible where it is, without collision.");
                return;
            }

            if (real.Count == 0)
            {
                if (unsupported is not null && approx is null)
                {
                    int pose = Farthest(ctx);
                    EmitStatic(ctx, pose, solid: true);
                    _log.Warn("JK_COG_UNSUPPORTED", $"{ctx.Name}: {unsupported.Reason}{Via(ctx)}; it stays visible in frame {pose}, the pose farthest from its rest position (open passage).");
                    return;
                }
                if (approx is not null && approx.Reason.StartsWith("no COG moves", StringComparison.OrdinalIgnoreCase))
                {
                    // Aucun COG ne deplace ce thing (point d'apparition de caisses, lumiere cachee...) : il ne bouge pas dans JK non plus.
                    EmitStatic(ctx, 0, solid: true);
                    _static++;
                    return;
                }
            }

            // Une partie du script n'est pas reproduite alors que d'autres comportements le sont : le journal doit le dire.
            if (unsupported is not null && real.Count > 0)
                _log.Warn("JK_COG_UNSUPPORTED", $"{ctx.Name}: part of its movement is not reproduced ({unsupported.Reason}){Via(ctx)}; only the other movements of this thing are rebuilt.");

            // Images atteintes (hors repos), dans l'ordre d'apparition.
            var cand = new List<int>();
            void Add(IEnumerable<int> fs) { foreach (int f in fs) if (f > 0 && f < positions.Count && !cand.Contains(f)) cand.Add(f); }
            foreach (MoverBehavior b in g)
            {
                Add(b.FrameSequence);
                if (b is SwitchMove sw) Add(sw.Frames);
                if (b is MultiStepPath mp) Add(mp.Frames);
            }
            var multi = g.OfType<MultiStepPath>().FirstOrDefault();
            var actor = g.OfType<ActorTriggered>().FirstOrDefault();
            var switches = g.OfType<SwitchMove>().ToList();
            var lift = g.OfType<TwoStopLift>().FirstOrDefault();
            bool autoTouch = g.Any(b => b is TouchDoor or TwoStopLift) || (multi is not null && switches.Count == 0 && actor is null)
                || (real.Count == 0 && approx is not null);
            if (cand.Count == 0) cand.Add(Farthest(ctx));

            // Rotation seule : pas d'entite JA pour un mobile qui tourne et s'arrete (voir le resume de la classe).
            int open = cand[0];
            float travelJku = (positions[open] - positions[0]).Length() / _k;
            float rot = RotationOf(ctx, open);
            if (rot >= MinRotationDegrees && travelJku < RotationOnlyTravelJku)
            {
                EmitStatic(ctx, open, solid: false);
                _log.Warn("JK_COG_APPROX", $"{ctx.Name}: it only rotates ({rot:0.#} degrees between its frames) and Jedi Academy has no door that rotates and stops (no func_door_rotating in SWGL, a stopped func_rotating snaps back); "
                    + $"it stays visible in its open pose (frame {open}) without collision{Via(ctx)}.");
                return;
            }
            if (rot >= MinRotationDegrees)
                _log.Warn("JK_COG_APPROX", $"{ctx.Name}: the movement also includes a rotation of {rot:0.#} degrees; only the translation is reproduced{Via(ctx)}.");

            // Deux positions : porte ou plateau. Trois ou plus : train.
            List<int> order = TrainOrder(ctx, multi, cand, approx is not null && real.Count == 0, positions.Count);
            bool train = order.Count >= 3;
            float speed = Math.Max(MinSpeed, (g.Select(b => b.Speed).FirstOrDefault(s => s is not null) ?? JkMoverSpeed.DefaultCogSpeed) * JkMoverSpeed.JkUnitsPerSecondPerCogSpeed * _k);

            if (approx is not null && real.Count == 0)
                _log.Warn("JK_COG_APPROX", $"{ctx.Name}: {approx.Reason.TrimEnd('.')}. It is rebuilt from its {positions.Count} frames as a {(train ? "func_train" : "func_door")} that starts when the player approaches{Via(ctx)}.");

            if (train) EmitTrain(ctx, id, order, multi, switches, actor, autoTouch, speed, g, cycle: multi is null && actor is null && real.Count > 0);
            else if (travelJku * _k < MinTravel && (positions[open] - positions[0]).Length() < MinTravel)
            {
                EmitStatic(ctx, 0, solid: true);
                _log.Warn("JK_COG_APPROX", $"{ctx.Name}: its frames are at the same position; it stays where it is{Via(ctx)}.");
            }
            else EmitDoor(ctx, id, open, multi, switches, actor, autoTouch, lift, speed, g);
        }

        // ---- porte et ascenseur ----

        private void EmitDoor(Ctx c, int id, int open, MultiStepPath? multi, List<SwitchMove> switches, ActorTriggered? actor, bool autoTouch, TwoStopLift? lift, float speed, List<MoverBehavior> g)
        {
            Vector3 move = c.Positions[open] - c.Positions[0];
            float distance = move.Length();
            string name = $"jkm{id}";
            bool needsName = switches.Count > 0 || actor is not null;
            bool hasTouch = g.Any(b => b is TouchDoor);

            float? delay = g.Select(b => b.Delay).FirstOrDefault(d => d is not null);
            float wait = actor is not null ? -1f
                : Math.Max(MinWait, delay ?? (multi is { Wait: > 0 } ? multi.Wait : JkMoverSpeed.DefaultWait));
            bool returns = switches.Count > 0 && switches.All(s => s.Frames.Length >= 2 && s.Frames[^1] == 0 && s.Delay is not null);
            bool toggle = switches.Count > 0 && actor is null && !autoTouch && !returns;

            // func_plat : plateau qui ne fait que monter et se declenche tout seul (sa plaque de declenchement est celle du moteur).
            bool vertical = move.Z > 0 && MathF.Abs(move.X) < VerticalTolerance * move.Z && MathF.Abs(move.Y) < VerticalTolerance * move.Z;
            if (!needsName && lift is not null && !hasTouch && vertical)
            {
                int pi = AddBody(c, solid: true);
                _entities.Add(new Dictionary<string, string>
                {
                    ["classname"] = "func_plat", ["model"] = $"*{pi}", ["model2"] = c.Md3.Asset.Path,
                    ["origin"] = Vec(c.Positions[0] + new Vector3(0, 0, move.Z)), ["modelAngles"] = Angles(c.Thing.PitchYawRoll),
                    ["speed"] = JkObjects.Num(speed), ["height"] = JkObjects.Num(move.Z),
                });
                Count("func_plat");
                return;
            }

            Vector3 dir = move / distance;
            // Residus de virgule flottante (1e-6) : sans eux un deplacement vertical aurait un cap quelconque.
            dir = new Vector3(MathF.Abs(dir.X) < 1e-4f ? 0f : dir.X, MathF.Abs(dir.Y) < 1e-4f ? 0f : dir.Y, MathF.Abs(dir.Z) < 1e-4f ? 0f : dir.Z);
            // Etendue de la boite alignee sur les axes (celle du sous-modele du BSP) le long de la direction : distance = etendue - lip (g_mover.cpp, SP_func_door).
            Vector3 size = AabbSize(c);
            float extent = MathF.Abs(dir.X) * size.X + MathF.Abs(dir.Y) * size.Y + MathF.Abs(dir.Z) * size.Z;
            float pitch = -MathF.Asin(Math.Clamp(dir.Z, -1f, 1f)) * 180f / MathF.PI;
            float heading = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
            // G_SetMovedir lit (0, -1, 0) comme « haut » et (0, -2, 0) comme « bas » : on s'en ecarte de quelques centiemes de degre.
            if (MathF.Abs(pitch) < 0.0005f && (MathF.Abs(heading + 1f) < 0.0005f || MathF.Abs(heading + 2f) < 0.0005f)) heading += 0.01f;

            int index = AddBody(c, solid: true);
            var ent = new Dictionary<string, string>
            {
                ["classname"] = "func_door", ["model"] = $"*{index}", ["model2"] = c.Md3.Asset.Path,
                ["origin"] = Vec(c.Positions[0]), ["modelAngles"] = Angles(c.Thing.PitchYawRoll),
                ["angles"] = $"{JkObjects.Num(pitch)} {JkObjects.Num(heading)} 0",
                ["lip"] = JkObjects.Num(extent - distance), ["speed"] = JkObjects.Num(speed), ["wait"] = JkObjects.Num(wait),
            };
            if (needsName) ent["targetname"] = name;
            if (toggle) ent["spawnflags"] = SpawnToggle.ToString(CultureInfo.InvariantCulture);
            _entities.Add(ent);
            Count("func_door");

            if (needsName)
            {
                if (actor is not null) AddActorTrigger(c, actor, name);
                if (autoTouch) AddApproachTrigger(c, name, once: false);
                AddSwitches(c, switches, name);
            }
        }

        // ---- train ----

        /// <summary>Suite des images du train (la premiere est le repos, image 0).</summary>
        private static List<int> TrainOrder(Ctx c, MultiStepPath? multi, List<int> cand, bool approximate, int frameCount)
        {
            if (approximate && frameCount >= 3) return [.. Enumerable.Range(0, frameCount)];
            if (multi is not null)
            {
                var seq = multi.Frames.Where(f => f >= 0 && f < frameCount).Distinct().ToList();
                int zero = seq.IndexOf(0);
                if (zero > 0) seq = [.. seq.Skip(zero), .. seq.Take(zero)];
                else if (zero < 0) seq.Insert(0, 0);
                return seq;
            }
            return [0, .. cand];
        }

        /// <summary>
        /// Train. <paramref name="cycle"/> (ascenseur a plusieurs destinations choisies par le script, appele par des boutons) : demarre tout seul (START_ON) et boucle en s'arretant
        /// <c>Delay</c> a chaque etage, car un train relance au milieu de sa course saute au coin suivant et un bouton ne peut pas le mener a un etage precis.
        /// Sinon (chemin en plusieurs etapes, approximation) : il part une fois, quand le joueur approche (ou par son interrupteur / son acteur).
        /// </summary>
        private void EmitTrain(Ctx c, int id, List<int> order, MultiStepPath? multi, List<SwitchMove> switches, ActorTriggered? actor, bool autoTouch, float speed, List<MoverBehavior> g, bool cycle)
        {
            string name = $"jkm{id}";
            bool loop = cycle || (multi?.Loop ?? true);
            float? delay = g.Select(b => b.Delay).FirstOrDefault(d => d is not null);
            float wait = multi is { Wait: > 0 } ? multi.Wait : cycle ? Math.Max(MinWait, delay ?? JkMoverSpeed.DefaultWait) : 0f;
            int index = AddBody(c, solid: true);
            var train = new Dictionary<string, string>
            {
                ["classname"] = "func_train", ["model"] = $"*{index}", ["model2"] = c.Md3.Asset.Path,
                ["origin"] = Vec(c.Positions[0]), ["modelAngles"] = Angles(c.Thing.PitchYawRoll),
                ["target"] = $"{name}_c0", ["speed"] = JkObjects.Num(speed),
            };
            if (cycle) train["spawnflags"] = "1"; // START_ON : sans nom, il part des le chargement
            else train["targetname"] = name;
            _entities.Add(train);
            Count("func_train");
            for (int i = 0; i < order.Count; i++)
            {
                var corner = new Dictionary<string, string>
                {
                    ["classname"] = "path_corner", ["targetname"] = $"{name}_c{i}", ["origin"] = Vec(c.Positions[order[i]]),
                };
                if (i + 1 < order.Count) corner["target"] = $"{name}_c{i + 1}";
                else if (loop) corner["target"] = $"{name}_c0";
                if (wait > 0) corner["wait"] = JkObjects.Num(wait);
                _entities.Add(corner);
            }
            Count("path_corner", order.Count);
            if (cycle)
                _log.Warn("JK_COG_APPROX", $"{c.Name}: the script sends it to {order.Count} positions on demand (frames {string.Join(">", order)}); Jedi Academy cannot call a train to a given stop, so it starts at once and keeps visiting them in a loop, pausing {JkObjects.Num(wait)} s at each{Via(c)}.");
            else if (switches.Count > 0)
                _log.Warn("JK_COG_APPROX", $"{c.Name}: it has {order.Count} positions chosen by switches (frames {string.Join(">", order)}); it is rebuilt as a func_train started by the same switches, then it follows its path{Via(c)}.");
            if (cycle) return;
            if (actor is not null) AddActorTrigger(c, actor, name);
            AddSwitches(c, switches, name);
            if (autoTouch || (switches.Count == 0 && actor is null)) AddApproachTrigger(c, name, once: true);
        }

        // ---- declencheurs ----

        private void AddActorTrigger(Ctx c, ActorTriggered actor, string target)
        {
            Vector3 min, max;
            if (actor.Sector >= 0 && actor.Sector < _level.Sectors.Count && JkSurfaceEffects.SectorBounds(_level, actor.Sector, out Vector3 lo, out Vector3 hi))
            { min = lo * _k - new Vector3(ActorMargin); max = hi * _k + new Vector3(ActorMargin); }
            else (min, max) = ApproachBox(c);
            if (AddTrigger(min, max, target, flags: 0, wait: -1f)) _approachTriggers++;
            _log.Warn("JK_COG_ACTOR_TRIGGER", $"{c.Name}: COG {(actor.Cog.Length > 0 ? actor.Cog : c.Cog)} opens it when an actor dies, and actors are not placed: it opens when the player enters sector {actor.Sector}.");
        }

        private void AddApproachTrigger(Ctx c, string target, bool once)
        {
            var (min, max) = ApproachBox(c);
            if (AddTrigger(min, max, target, flags: 0, wait: once ? -1f : 1f)) _approachTriggers++;
        }

        private (Vector3 Min, Vector3 Max) ApproachBox(Ctx c)
        {
            Vector3 min = new(float.MaxValue), max = new(float.MinValue);
            foreach (Vector3 corner in Corners(c)) { min = Vector3.Min(min, corner + c.Positions[0]); max = Vector3.Max(max, corner + c.Positions[0]); }
            return (min - new Vector3(ApproachMargin, ApproachMargin, ApproachBelow), max + new Vector3(ApproachMargin, ApproachMargin, ApproachAbove));
        }

        private void AddSwitches(Ctx c, List<SwitchMove> switches, string target)
        {
            foreach (SwitchMove s in switches)
            {
                bool valid = s.SwitchIsThing ? _things.ContainsKey(s.SwitchSurfaceOrThing) : s.SwitchSurfaceOrThing >= 0 && s.SwitchSurfaceOrThing < _level.Surfaces.Count;
                if (!valid)
                {
                    // Repli : surface inconnue, le mobile reagit a l'approche du joueur.
                    AddApproachTrigger(c, target, once: false);
                    _log.Warn("JK_COG_APPROX", $"{c.Name}: its switch ({(s.SwitchIsThing ? "thing" : "surface")} {s.SwitchSurfaceOrThing}) does not exist; it reacts to the player's approach instead{Via(c)}.");
                    continue;
                }
                // La cle est (-1 - thing) pour un interrupteur-thing : meme table que les surfaces.
                int key = s.SwitchIsThing ? -1 - s.SwitchSurfaceOrThing : s.SwitchSurfaceOrThing;
                if (!_switches.TryGetValue(key, out var targets)) _switches[key] = targets = new SortedSet<string>(StringComparer.Ordinal);
                targets.Add(target);
            }
        }

        /// <summary>Ajoute un declencheur ; faux (compte et journalise a la fin) quand la limite de sous-modeles est atteinte.</summary>
        private bool AddTrigger(Vector3 min, Vector3 max, string target, int flags, float wait)
        {
            int index = _first + _subs.Count;
            if (index > JkObjects.MaxSubModels) { _skippedTriggers++; return false; }
            _subs.Add(new SubModelInput([JkVolumeBrushes.Box(min, max)], JkObjects.TriggerShader) { Contents = BspContents.Trigger });
            _entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "trigger_multiple", ["model"] = $"*{index}", ["origin"] = "0 0 0", ["target"] = target,
                ["spawnflags"] = flags.ToString(CultureInfo.InvariantCulture), ["wait"] = JkObjects.Num(wait),
            });
            Count("trigger_multiple");
            return true;
        }

        /// <summary>Fin : declencheurs des surfaces interrupteurs (un par surface), relais pour les surfaces qui commandent plusieurs mobiles, bilan du journal.</summary>
        public void Finish(int celSwitches)
        {
            foreach (var (key, targets) in _switches)
            {
                Vector3 min, max;
                if (key >= 0) { JkSurfaceEffects.SurfaceBounds(_level, key, out Vector3 lo, out Vector3 hi); min = lo * _k; max = hi * _k; }
                else { Vector3 at = _things[-1 - key].Position * _k; min = max = at; }
                Vector3 reach = new(UseReach);
                string target = targets.Count == 1 ? targets.First() : (key >= 0 ? $"jks{key}" : $"jkt{-1 - key}");
                if (targets.Count > 1)
                    foreach (string t in targets)
                    {
                        _entities.Add(new Dictionary<string, string> { ["classname"] = "target_relay", ["targetname"] = target, ["target"] = t });
                        Count("target_relay");
                    }
                if (AddTrigger(min - reach, max + reach, target, TriggerUseButton, 1f)) _switchTriggers++;
            }

            if (_overLimit.Count > 0 || _skippedTriggers > 0)
                _log.Warn("JK_SUBMODEL_LIMIT", $"The engine accepts at most {JkObjects.MaxSubModels} BSP sub-models: {_overLimit.Count} moving thing(s) are placed as plain models without movement or collision (things {List(_overLimit)}) "
                    + $"and {_skippedTriggers} trigger(s) are not placed.");
            if (celSwitches > 0)
                _log.Warn("JK_COG_CEL_STATIC", $"{celSwitches} surface(s) change their texture cell under a script (lamps, panels, cut grates): they keep their first cell; the surfaces and passages are converted as the level defines them.");
            if (_noModel.Count > 0)
                _log.Warn("JK_MOVER_NO_MODEL", $"{_noModel.Count} moving thing(s) have no 3D model (invisible markers) and are not placed: things {List(_noModel)}.");
            if (_scaled.Count > 0)
                _log.Warn("JK_MOVER_MODEL_SCALE", $"{_scaled.Count} moving thing(s) have a model too large for one MD3 (it was scaled down; a mover cannot rescale its model): the collision box has the real size, "
                    + $"the drawn model is smaller. Things: {string.Join(", ", _scaled.Take(ListedThings).Select(kv => $"{kv.Key} {kv.Value}"))}{(_scaled.Count > ListedThings ? ", ..." : "")}.");
            if (_entities.Count > 0 || _static > 0)
                _log.Warn("JK_MOVERS", "Moving things converted: " + string.Join(", ", _counts.Select(kv => $"{kv.Value} {kv.Key}")) + (_static > 0 ? $", {_static} fixed (no COG moves them)" : "")
                    + $"; {_switchTriggers} switch trigger(s), {_approachTriggers} approach trigger(s). Sounds of movers are not converted.");
        }

        // ---- corps statique ----

        private void EmitStatic(Ctx c, int pose, bool solid)
        {
            int index = AddBody(c, solid);
            Vector3 angles = c.Thing.PitchYawRoll + c.Rotations[Math.Clamp(pose, 0, c.Rotations.Count - 1)];
            _entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "func_static", ["model"] = $"*{index}", ["model2"] = c.Md3.Asset.Path,
                ["origin"] = Vec(c.Positions[Math.Clamp(pose, 0, c.Positions.Count - 1)]), ["modelAngles"] = Angles(angles),
            });
            Count("func_static");
        }

        /// <summary>Ajoute le sous-modele (boite orientee du modele) et rend son indice.</summary>
        private int AddBody(Ctx c, bool solid)
        {
            int index = _first + _subs.Count;
            _subs.Add(new SubModelInput([OrientedBox(c)], solid ? DoorConverter.ClipShader : JkObjects.NonSolidShader)
            { Contents = solid ? BspContents.Solid : BspContents.None });
            return index;
        }

        private static IReadOnlyList<BspPlane> OrientedBox(Ctx c)
        {
            Vector3 min = c.BoxMin, max = c.BoxMax;
            for (int axis = 0; axis < 3; axis++)
                if (max[axis] - min[axis] < DoorConverter.MinThickness) { float mid = (min[axis] + max[axis]) / 2f; min[axis] = mid - DoorConverter.MinThickness / 2f; max[axis] = mid + DoorConverter.MinThickness / 2f; }
            Vector3 ex = Vector3.TransformNormal(Vector3.UnitX, c.Rot), ey = Vector3.TransformNormal(Vector3.UnitY, c.Rot), ez = Vector3.TransformNormal(Vector3.UnitZ, c.Rot);
            return
            [
                new BspPlane(ex, max.X), new BspPlane(-ex, -min.X),
                new BspPlane(ey, max.Y), new BspPlane(-ey, -min.Y),
                new BspPlane(ez, max.Z), new BspPlane(-ez, -min.Z),
            ];
        }

        private static IEnumerable<Vector3> Corners(Ctx c)
        {
            Vector3 min = c.BoxMin, max = c.BoxMax;
            for (int i = 0; i < 8; i++)
                yield return Vector3.Transform(new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z), c.Rot);
        }

        private static Vector3 AabbSize(Ctx c)
        {
            Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
            foreach (Vector3 corner in Corners(c)) { lo = Vector3.Min(lo, corner); hi = Vector3.Max(hi, corner); }
            return hi - lo;
        }

        // ---- aides ----

        private IReadOnlyList<(Vector3 Pos, Vector3 Pyr)> FramesOf(JklThing t)
        {
            string? text = t.Params.TryGetValue("frame", out string? own) ? own : _p.Templates.Resolve(t.Template).GetValueOrDefault("frame");
            return string.IsNullOrWhiteSpace(text) ? [] : JklReader.ReadFrames(text);
        }

        /// <summary>Image la plus eloignee du repos (translation, plus rotation ponderee) ; 0 sans image.</summary>
        private int Farthest(Ctx c)
        {
            int best = 0;
            float bestScore = -1f;
            for (int i = 1; i < c.Positions.Count; i++)
            {
                float score = (c.Positions[i] - c.Positions[0]).Length() / _k + RotationOf(c, i) / 90f;
                if (score > bestScore + 1e-6f) { bestScore = score; best = i; }
            }
            return best;
        }

        private static float RotationOf(Ctx c, int frame) =>
            frame < 0 || frame >= c.Rotations.Count ? 0f : MathF.Max(MathF.Abs(c.Rotations[frame].X), MathF.Max(MathF.Abs(c.Rotations[frame].Y), MathF.Abs(c.Rotations[frame].Z)));

        private static Vector3 Wrap(Vector3 degrees)
        {
            static float W(float a) => a - 360f * MathF.Round(a / 360f);
            return new Vector3(W(degrees.X), W(degrees.Y), W(degrees.Z));
        }

        private static string Angles(Vector3 jkPitchYawRoll)
        {
            Vector3 a = JkAngles.ToJa(jkPitchYawRoll);
            return $"{JkObjects.Num(a.X)} {JkObjects.Num(a.Y)} {JkObjects.Num(a.Z)}";
        }

        /// <summary>Fin de phrase « (COG x) » quand le thing est lie a un COG.</summary>
        private static string Via(Ctx c) => c.Cog.Length > 0 ? $" (COG {c.Cog})" : "";

        private static string Vec(Vector3 v) => $"{JkObjects.Num(v.X)} {JkObjects.Num(v.Y)} {JkObjects.Num(v.Z)}";

        private void Count(string classname, int n = 1) => _counts[classname] = _counts.GetValueOrDefault(classname) + n;

        private static string List(IEnumerable<int> ids)
        {
            var all = ids.ToList();
            return string.Join(", ", all.Take(ListedThings)) + (all.Count > ListedThings ? ", ..." : "");
        }
    }
}

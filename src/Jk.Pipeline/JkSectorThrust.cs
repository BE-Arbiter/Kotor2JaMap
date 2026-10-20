using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Jk.Core.Cog;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline.Entities;

namespace Jk.Pipeline;

/// <summary>
/// Poussee de secteur (ventilateurs, courants d'air, conduits). Dans Jedi Knight un secteur pousse ce qui s'y trouve : <c>THRUST x y z</c> dans le fichier du niveau, ou
/// <c>SectorThrust(secteur, vecteur, vitesse)</c> d'un COG (<c>00_sectorthrust.cog</c> : « les poussees tournent en continu », jusqu'a 8 secteurs de meme vecteur et de meme vitesse).
/// <para>Jedi Academy (SWGL, <c>g_trigger.cpp</c>) n'a que <c>trigger_push</c>, qui <b>affecte</b> la vitesse du joueur (il ne l'additionne pas) a chaque image ou il touche le volume.
/// Trois modes : en arc vers un <c>target_position</c> (vitesse imposee par la hauteur de la cible), <c>LINEAR</c> (vitesse forcee a 1000 par <c>SP_trigger_push</c>, la cle <c>speed</c> est ecrasee),
/// et <c>RELATIVE</c> (16) : « pousse le joueur vers la cible, a la vitesse <c>speed</c> ». Seul RELATIVE donne une vitesse reglable ; la direction etant celle du joueur vers la cible, la cible est
/// posee tres loin dans la direction de la poussee (la direction reste constante a un a deux degres pres). Un secteur devient donc un <c>trigger_push</c> RELATIVE (sous-modele non solide, comme
/// les zones de blessure) et chaque groupe de meme poussee un <c>target_position</c> lointain.</para>
/// <para>Seule la poussee <b>continue</b> est convertie : un script dont tous les <c>SectorThrust</c> sont dans le message <c>startup</c> (aucun autre message ne change la poussee), avec des arguments
/// resolubles (symboles ou nombres). Une poussee que des evenements allument, eteignent ou changent (interrupteurs, minuteries, pulses) reste journalisee <c>JK_SECTOR_THRUST_SKIPPED</c>.</para>
/// </summary>
internal static class JkSectorThrust
{
    /// <summary>Prefixe du nom des <c>target_position</c> (un par groupe de meme poussee).</summary>
    public const string TargetPrefix = "jkthrust";

    /// <summary>
    /// Unites JA par seconde pour une unite de poussee JK, a l'echelle par defaut (40 unites JA par metre). Non verifie en jeu : le joueur JK a <c>maxthrust=2.0</c> (unites JK par seconde,
    /// <c>walkplayer</c>) et le joueur JA court a 250 u/s (<c>g_speed</c>), d'ou 125 u/s par unite de poussee : un courant d'air de 1,0 est la moitie de la vitesse de course, comme dans JK.
    /// L'echelle du monde (344 u par unite JK) donnerait 344 u/s pour 1,0, plus que la course du joueur JA : un vent qu'on ne pourrait plus remonter.
    /// </summary>
    public const float JaSpeedPerThrust = 125f;

    /// <summary>Vitesse de poussee maximale (unites JA par seconde) : celle des plaques d'elan de JA ; les ventilateurs verticaux de JK (vitesse 8 a 10) enverraient le joueur a plus de 2000 u/s, et a travers le plafond.</summary>
    public const float MaxPushSpeed = 1000f;

    /// <summary>Vitesse minimale emise (u/s). Sous ce seuil la zone est omise : « speed 0 » (arrondi de <c>Num</c>) ferait envoyer le joueur a la distance entiere de la cible (<c>if (self->speed)</c> faux), et une vitesse de 1 u/s figerait le joueur.</summary>
    public const float MinPushSpeed = 1f;

    /// <summary>Drapeau <c>RELATIVE</c> de <c>trigger_push</c> (g_trigger.cpp).</summary>
    private const int Relative = 16, PlayerOnly = 1;

    private const string StartupMessage = "startup";

    /// <summary>Distance minimale (unites JA) de la cible, et nombre de fois la taille du groupe : l'erreur de direction est au plus (taille / distance).</summary>
    private const float MinTargetDistance = 10000f, TargetSizeFactor = 40f, MaxTargetDistance = 60000f;

    /// <summary>Secteurs de meme poussee : direction (unitaire) et intensite (unites JK par seconde).</summary>
    internal sealed record Group(IReadOnlyList<int> Sectors, Vector3 Direction, float Strength);

    /// <summary>Poussees continues du niveau, et ce que la conversion ne peut pas faire (a journaliser).</summary>
    internal sealed record Plan(IReadOnlyList<Group> Groups, int SkippedInstances, IReadOnlyCollection<string> SkippedScripts);

    /// <summary>
    /// Analyse du niveau : les poussees continues, dans l'ordre (fichier du niveau, puis instances de COG). <paramref name="script"/> rend le script d'un fichier COG, ou null s'il est absent.
    /// </summary>
    public static Plan Analyze(JklLevel level, Func<string, CogScript?> script)
    {
        var groups = new List<Group>();
        // Fichier du niveau : un groupe par vecteur.
        foreach (var byVector in level.Sectors.Where(s => s.Thrust != Vector3.Zero).GroupBy(s => s.Thrust))
        {
            float length = byVector.Key.Length();
            if (length > 1e-6f && float.IsFinite(length)) groups.Add(new Group([.. byVector.Select(s => s.Index)], byVector.Key / length, length));
        }

        int skipped = 0;
        var skippedScripts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JklCogInstance inst in level.Cogs)
        {
            CogScript? cog = script(inst.CogFile);
            if (cog is null || !HasThrustCall(cog)) continue;
            if (TryRead(cog, inst, out List<Group> read)) groups.AddRange(read);
            else { skipped++; skippedScripts.Add(inst.CogFile); }
        }
        return new Plan(groups, skipped, skippedScripts);
    }

    private static bool IsThrustVerb(CogCallSite c) =>
        c.Function.Equals("SectorThrust", StringComparison.OrdinalIgnoreCase) || c.Function.Equals("SetSectorThrust", StringComparison.OrdinalIgnoreCase);

    private static bool HasThrustCall(CogScript cog) => cog.AllCalls().Any(IsThrustVerb);

    /// <summary>
    /// Lit une instance dont la poussee est continue ; faux si un autre message que <c>startup</c> touche a la poussee, si un appel est dans une boucle ou une condition qu'on ne sait pas evaluer,
    /// ou si ses arguments ne se resolvent pas. Un secteur absent (-1), une vitesse nulle ou un vecteur nul ne poussent rien : aucun groupe, et ce n'est pas un echec.
    /// </summary>
    private static bool TryRead(CogScript cog, JklCogInstance inst, out List<Group> groups)
    {
        groups = [];
        if (cog.MessageNames.Any(m => !m.Equals(StartupMessage, StringComparison.OrdinalIgnoreCase) && cog.Expand(m).Any(IsThrustVerb))) return false;
        IReadOnlyList<CogCallSite> calls = [.. cog.StartupCalls().Where(IsThrustVerb)];
        if (calls.Count == 0) return false;

        IReadOnlyDictionary<string, string> values = cog.Bind(inst);
        var bySetting = new List<(Vector3 Direction, float Strength, List<int> Sectors)>();
        foreach (CogCallSite call in calls)
        {
            if (call.InLoop || call.Args.Length < 2) return false;
            bool active = true;
            foreach (string condition in call.Conditions)
            {
                // Seule condition connue : « symbole >= 0 » (secteur lie), comme dans 00_sectorthrust.
                Match m = Regex.Match(condition, @"^\s*(\w+)\s*>=\s*0\s*$");
                if (!m.Success || !values.TryGetValue(m.Groups[1].Value, out string? conditionValue)) return false;
                if (JkSurfaceEffects.Index(conditionValue) < 0) active = false;
            }

            string sectorText = JkSurfaceEffects.Resolve(call.Args[0], values);
            if (!int.TryParse(sectorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sector)) return false;
            if (JkSurfaceEffects.Vector(JkSurfaceEffects.Resolve(call.Args[1], values).Trim('\'', '"')) is not { } vector) return false;
            float speed = 1f;
            if (call.Args.Length >= 3)
            {
                if (JkSurfaceEffects.Number(JkSurfaceEffects.Resolve(call.Args[2], values)) is not { } s) return false;
                speed = s;
            }
            float strength = vector.Length() * speed;
            if (!active || sector < 0 || !(strength > 1e-6f) || !float.IsFinite(strength)) continue;
            Vector3 direction = vector / vector.Length();
            var slot = bySetting.FindIndex(g => g.Direction == direction && g.Strength == strength);
            if (slot < 0) bySetting.Add((direction, strength, [sector]));
            else if (!bySetting[slot].Sectors.Contains(sector)) bySetting[slot].Sectors.Add(sector);
        }
        groups = [.. bySetting.Select(g => new Group(g.Sectors, g.Direction, g.Strength))];
        return true;
    }

    /// <summary>Lecteur des scripts COG d'un systeme de fichiers (avec memoire) : meme lecture que <c>JkSecrets</c>.</summary>
    private static Func<string, CogScript?> Scripts(JkFileSystem fs)
    {
        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        return file =>
        {
            if (!scripts.TryGetValue(file, out CogScript? s))
            {
                byte[]? raw = fs.TryRead(@"cog\" + file);
                scripts[file] = s = raw is null ? null : CogReader.Read(Encoding.Latin1.GetString(raw));
            }
            return s;
        };
    }

    /// <summary>Convertit les poussees ; les sous-modeles BSP des zones sont ajoutes a <paramref name="subs"/> (apres les mobiles).</summary>
    public static ConvertedEntities Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log, JkSubModels subs)
    {
        JklLevel level = prepared.Level;
        Plan plan = Analyze(level, Scripts(fs));
        if (plan.Groups.Count == 0) return ConvertedEntities.Empty;

        // Un secteur ne pousse que d'une facon : le dernier SectorThrust remplace le precedent (le fichier du niveau passe d'abord).
        var owner = new Dictionary<int, int>();
        for (int g = 0; g < plan.Groups.Count; g++)
            foreach (int sector in plan.Groups[g].Sectors) owner[sector] = g;

        float k = JkWorldScale.MetresPerJkUnit * opt.Scale;
        float speedScale = JaSpeedPerThrust * opt.Scale / ExportOptions.DefaultScale;
        var entities = new List<IReadOnlyDictionary<string, string>>();
        int zones = 0, exact = 0, boxes = 0, skipped = 0, capped = 0, targets = 0, tooWeak = 0;
        for (int g = 0; g < plan.Groups.Count; g++)
        {
            Group group = plan.Groups[g];
            var placed = new List<(IReadOnlyList<BspPlane> Planes, Vector3 Centre, float Radius)>();
            foreach (int sector in group.Sectors.Order())
            {
                if (owner[sector] != g) continue;
                if (sector < 0 || sector >= level.Sectors.Count || subs.Items.Count + placed.Count + 1 > JkObjects.MaxSubModels) { skipped++; continue; }
                IReadOnlyList<BspPlane>? planes = JkVolumeBrushes.SectorPlanes(level, sector, k);
                bool isBox = planes is null;
                if (!JkSurfaceEffects.SectorBounds(level, sector, out Vector3 min, out Vector3 max)) { skipped++; continue; }
                planes ??= JkVolumeBrushes.Box(min * k, max * k);
                if (BrushBounds.FromPlanes(planes) is null) { skipped++; continue; }
                placed.Add((planes, (min + max) * 0.5f * k, (max - min).Length() * 0.5f * k));
                if (isBox) boxes++; else exact++;
            }
            if (placed.Count == 0) continue;

            // Vitesse trop faible : Num l'arrondirait a 0 et « speed 0 » enverrait le joueur a la distance entiere de la cible ; on omet la zone (journalisee).
            float raw = group.Strength * speedScale;
            if (!(raw >= MinPushSpeed)) { skipped += placed.Count; tooWeak += placed.Count; continue; }

            // Cible commune : au centre de l'ensemble, tres loin dans la direction de la poussee.
            Vector3 centre = placed.Aggregate(Vector3.Zero, (a, p) => a + p.Centre) / placed.Count;
            float size = placed.Max(p => (p.Centre - centre).Length() + p.Radius);
            float distance = Math.Clamp(size * TargetSizeFactor, MinTargetDistance, MaxTargetDistance);
            Vector3 targetAt = centre + group.Direction * distance;
            string name = TargetPrefix + targets++.ToString(CultureInfo.InvariantCulture);
            if (raw > MaxPushSpeed) capped += placed.Count;
            string speed = JkObjects.Num(MathF.Min(raw, MaxPushSpeed));

            foreach (var zone in placed)
            {
                int index = subs.Add(new SubModelInput([zone.Planes], JkObjects.TriggerShader) { Contents = BspContents.Trigger });
                entities.Add(new Dictionary<string, string>
                {
                    ["classname"] = "trigger_push",
                    ["model"] = $"*{index}",
                    ["origin"] = "0 0 0",
                    ["target"] = name,
                    ["spawnflags"] = (Relative | PlayerOnly).ToString(CultureInfo.InvariantCulture), // PLAYERONLY : sinon toute entite non-client en mouvement recoit origin2 (position monde de la cible) comme vitesse
                    ["speed"] = speed,
                });
                zones++;
            }
            entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "target_position",
                ["targetname"] = name,
                ["origin"] = $"{JkObjects.Num(targetAt.X)} {JkObjects.Num(targetAt.Y)} {JkObjects.Num(targetAt.Z)}",
            });
        }

        if (zones > 0)
            log.Warn("JK_SECTOR_THRUST", $"{zones} sector thrust zone(s) converted (fans, drafts, ducts; the JK THRUST of the level file and the continuous SectorThrust of the COG scripts): one non-solid trigger_push per sector "
                + $"(RELATIVE: it sets the player's velocity toward a distant target_position along the thrust direction, {targets} target(s)), {exact} follow their sector planes, {boxes} use the sector bounding box. "
                + $"Speed: {JaSpeedPerThrust:0.#} JA units/s per unit of JK thrust, at most {MaxPushSpeed:0.#}" + (capped > 0 ? $" ({capped} zone(s) are capped)" : "")
                + ". A JA push overrides the player's velocity instead of adding to it, so it cannot be walked against; not verified in game.");
        if (skipped > 0)
            log.Warn("JK_SECTOR_THRUST_SKIPPED", $"{skipped} sector thrust zone(s) are not placed (sector missing from the level, degenerate sector, or no sub-model left)" + (tooWeak > 0 ? $"; {tooWeak} of them are too weak (under {MinPushSpeed:0.#} JA unit/s: a trigger_push with speed 0 would throw the player the whole distance to its target)" : "") + ".");
        return new ConvertedEntities(entities, [], []);
    }
}

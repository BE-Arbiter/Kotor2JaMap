using System.Globalization;
using System.Numerics;
using System.Text;
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
/// Zones secretes. Dans Jedi Knight, un COG (<c>00_SecretAreas.cog</c>) lie jusqu'a 20 secteurs (<c>secret1</c>..<c>secret20</c>, <c>linkid</c> en puissances de deux) ;
/// au message <c>entered</c> d'un de ces secteurs, il ajoute 1 au compteur de secrets du joueur (<c>ChangeInv(player, 70, 1.0)</c>, 71 = total), affiche un texte
/// et joue un son. Chaque secteur ne compte qu'une fois (le script retient les <c>linkid</c> deja vus).
/// <para>Jedi Academy (SWGL) a l'entite equivalente : <c>target_secret</c> (<c>g_target.cpp</c>) incremente <c>secretsFound</c> du joueur, joue
/// <c>sound/interface/secret_area</c>, affiche « Secret Area! » (<c>SP_INGAME_SECRET_AREA</c>) ; sa cle <c>count</c> donne le total du niveau
/// (« secrets found: n of N »). Chaque secteur secret devient donc un <c>trigger_multiple</c> (brush non solide, une seule fois, joueur seulement) qui vise un unique
/// <c>target_secret</c> de nom <see cref="TargetName"/>.</para>
/// <para>Le script est reconnu par sa structure, pas par son nom : un message <c>entered</c> qui appelle <c>ChangeInv</c> sur l'objet d'inventaire <see cref="SecretsFoundItem"/>,
/// et ses symboles de secteur lies (ni <c>local</c> ni <c>nolink</c>) donnent les secteurs.</para>
/// </summary>
internal static class JkSecrets
{
    /// <summary>Nom partage par le <c>target_secret</c> et les <c>trigger_multiple</c> des zones secretes.</summary>
    public const string TargetName = "jk_secret";

    /// <summary>Objet d'inventaire JK qui compte les secrets trouves (71 : total du niveau).</summary>
    public const string SecretsFoundItem = "70";

    /// <summary>Drapeau <c>PLAYERONLY</c> de <c>trigger_multiple</c> : seul le joueur declenche.</summary>
    private const int PlayerOnly = 1;

    /// <summary>Secteurs secrets du niveau, dans l'ordre des instances puis des symboles, sans doublon.</summary>
    public static IReadOnlyList<int> Find(JklLevel level, Func<string, CogScript?> script)
    {
        var sectors = new List<int>();
        foreach (JklCogInstance inst in level.Cogs)
        {
            CogScript? cog = script(inst.CogFile);
            if (cog is null || !CountsSecrets(cog)) continue;
            BoundCog bound = CogBinding.Bind(cog, inst);
            foreach (CogSymbol s in cog.Symbols)
            {
                if (s.Type != "sector" || s.Local || s.NoLink) continue;
                if (bound.Int(s.Name) is { } sector && sector >= 0 && !sectors.Contains(sector)) sectors.Add(sector);
            }
        }
        return sectors;
    }

    /// <summary>Vrai si le message <c>entered</c> du script ajoute au compteur de secrets du joueur.</summary>
    private static bool CountsSecrets(CogScript cog) =>
        cog.HasMessage("entered")
        && cog.Expand("entered").Any(c => c.Function.Equals("ChangeInv", StringComparison.OrdinalIgnoreCase) && c.Args.Length >= 2 && c.Args[1].Trim() == SecretsFoundItem);

    /// <summary>Convertit les zones secretes ; les sous-modeles BSP des declencheurs sont ajoutes a <paramref name="subs"/> (apres tous les autres).</summary>
    public static ConvertedEntities Convert(JkPreparedScene prepared, JkFileSystem fs, ExportOptions opt, ImportLog log, JkSubModels subs)
    {
        JklLevel level = prepared.Level;
        var scripts = new Dictionary<string, CogScript?>(StringComparer.OrdinalIgnoreCase);
        CogScript? Script(string file)
        {
            if (!scripts.TryGetValue(file, out CogScript? s))
            {
                byte[]? raw = fs.TryRead(@"cog\" + file);
                scripts[file] = s = raw is null ? null : CogReader.Read(Encoding.Latin1.GetString(raw));
            }
            return s;
        }

        IReadOnlyList<int> sectors = Find(level, Script);
        if (sectors.Count == 0) return ConvertedEntities.Empty;

        float k = JkWorldScale.MetresPerJkUnit * opt.Scale;
        var entities = new List<IReadOnlyDictionary<string, string>>();
        int placed = 0, exact = 0, boxes = 0, skipped = 0;
        foreach (int sector in sectors)
        {
            if (sector >= level.Sectors.Count || subs.Items.Count + 1 > JkObjects.MaxSubModels) { skipped++; continue; }
            IReadOnlyList<BspPlane>? planes = JkVolumeBrushes.SectorPlanes(level, sector, k);
            bool isBox = planes is null;
            if (planes is null)
            {
                if (!JkSurfaceEffects.SectorBounds(level, sector, out Vector3 min, out Vector3 max)) { skipped++; continue; }
                planes = JkVolumeBrushes.Box(min * k, max * k);
            }
            if (BrushBounds.FromPlanes(planes) is null) { skipped++; continue; }

            int index = subs.Add(new SubModelInput([planes], JkObjects.TriggerShader) { Contents = BspContents.Trigger });
            entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "trigger_multiple",
                ["model"] = $"*{index}",
                ["origin"] = "0 0 0",
                ["target"] = TargetName,
                ["spawnflags"] = PlayerOnly.ToString(CultureInfo.InvariantCulture),
                ["wait"] = "-1", // une fois : chaque zone se compte une seule fois, comme le COG
            });
            placed++;
            if (isBox) boxes++; else exact++;
        }

        if (placed > 0)
        {
            // Un seul target_secret : chaque declenchement ajoute 1 au compteur ; count = total du niveau (lu par « secrets found : n sur N »).
            entities.Add(new Dictionary<string, string>
            {
                ["classname"] = "target_secret",
                ["targetname"] = TargetName,
                ["origin"] = "0 0 0",
                ["count"] = placed.ToString(CultureInfo.InvariantCulture),
            });
            log.Warn("JK_SECRET_AREAS", $"{placed} secret area(s) converted: one non-solid trigger_multiple per secret sector (player only, once) aiming at a target_secret "
                + $"(count {placed}), which counts it as a secret found, plays the secret sound and shows \"Secret Area!\"; {exact} follow their sector planes, {boxes} use the sector bounding box. "
                + "The JK text and sound are replaced by the JA ones; the force star given at the end of the level for finding every secret is not converted.");
        }
        if (skipped > 0)
            log.Warn("JK_SECRET_SKIPPED", $"{skipped} secret area(s) are not placed (sector missing from the level, degenerate sector, or no sub-model left).");
        return new ConvertedEntities(entities, [], []);
    }
}

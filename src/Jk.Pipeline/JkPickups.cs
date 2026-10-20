namespace Jk.Pipeline;

/// <summary>
/// Objets a ramasser de Jedi Knight -> classe d'objet JA (spec §5.6). La cle est le nom du gabarit du thing (celui de la ligne <c>THINGS</c>) ; la table
/// couvre les 49 gabarits de type <c>item</c> reellement presents dans les 34 niveaux (liste produite par <c>jk-dump-level</c>, voir le rapport T7).
/// Les classes JA citees existent dans le jeu solo de SWGL (OpenJK-SWGL <c>code/game/bg_misc.cpp</c>, <c>g_items.cpp</c>).
/// <list type="bullet">
/// <item>Armes : pistolet Bryar -> <c>weapon_bryar_pistol</c> (le pistolet de Greedo donne le meme objet JK, <c>pow_bryar.cog</c>), fusil de stormtrooper ->
/// <c>weapon_blaster</c>, arbalete -> <c>weapon_bowcaster</c>, repeteur, detonateur a rail -> <c>weapon_rocket_launcher</c>, charge sequenceur ->
/// <c>weapon_trip_mine</c>, fusil a concussion, sabre laser, detonateur thermique -> <c>weapon_thermal</c> (la charge isolee donne <c>ammo_thermal</c>).</item>
/// <item>Munitions : cellule d'energie -> <c>ammo_blaster</c>, cellule de puissance -> <c>ammo_powercell</c>, charges de rail -> <c>ammo_rockets</c>, batterie -> <c>item_battery</c>.</item>
/// <item>Sante et boucliers : sante et reservoir de bacta -> <c>item_medpak_instant</c>, recharge de bouclier -> <c>item_shield_sm_instant</c>, gilet et
/// super-bouclier -> <c>item_shield_lrg_instant</c>. Appoint de Force (<c>pow_mana.cog</c>) -> <c>ammo_force</c>.</item>
/// </list>
/// Sans equivalent (decor + <c>JK_ITEM_NO_EQUIVALENT</c>) : cles, cles a molette, disquette, lunettes IR, sacs a dos, kit de reanimation, surtensions de Force
/// claire et sombre, surcharge de puissance, mines posees, cles de CTF.
/// </summary>
public static class JkPickups
{
    private static readonly Dictionary<string, string> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // Armes
        ["bryarpistol"] = "weapon_bryar_pistol",
        ["greedopistol"] = "weapon_bryar_pistol",
        ["strifle"] = "weapon_blaster",
        ["detonator"] = "weapon_thermal",
        ["singledetonator"] = "ammo_thermal",
        ["crossbow"] = "weapon_bowcaster",
        ["repeatergun"] = "weapon_repeater",
        ["railgun"] = "weapon_rocket_launcher",
        ["m5railgun"] = "weapon_rocket_launcher",
        ["seqcharge"] = "weapon_trip_mine",
        ["concrifle"] = "weapon_concussion_rifle",
        ["m5concrifle"] = "weapon_concussion_rifle",
        ["lightsaber"] = "weapon_saber",
        // Munitions
        ["energycell"] = "ammo_blaster",
        ["powercell"] = "ammo_powercell",
        ["railcharges"] = "ammo_rockets",
        ["battery"] = "item_battery",
        // Sante, boucliers, Force
        ["healthpack"] = "item_medpak_instant",
        ["rhealth"] = "item_medpak_instant",
        ["bactatank"] = "item_medpak_instant",
        ["rbacta"] = "item_medpak_instant",
        ["shieldrecharge"] = "item_shield_sm_instant",
        ["fullshield"] = "item_shield_lrg_instant",
        ["supershield"] = "item_shield_lrg_instant",
        ["manaboost"] = "ammo_force",
        ["rmanaboost"] = "ammo_force",
    };

    /// <summary>Classe JA de l'objet a ramasser dont le gabarit s'appelle <paramref name="resolvedTemplateName"/> (insensible a la casse), ou null s'il n'a pas d'equivalent.</summary>
    public static string? Map(string resolvedTemplateName) =>
        !string.IsNullOrEmpty(resolvedTemplateName) && Table.TryGetValue(resolvedTemplateName.Trim(), out string? ja) ? ja : null;
}

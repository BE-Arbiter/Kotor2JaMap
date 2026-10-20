namespace Jk.Pipeline.Movers;

/// <summary>
/// Comportement de mouvement reconnu pour un thing mobile (porte, ascenseur, pont...), deduit du COG qui le commande.
/// <c>Thing</c> = index du thing dans le niveau (-1 : pas de thing, cas de <see cref="SurfaceCelSwitch"/> et d'un COG manquant).
/// Les champs facultatifs sont remplis quand le COG les donne : fichier COG, vitesse du <c>MoveToFrame</c>, delai avant le retour
/// (<c>null</c> : le mouvement ne revient pas), sons joues, suite des images atteintes (dans l'ordre du script).
/// </summary>
public abstract record MoverBehavior(int Thing)
{
    public string Cog { get; init; } = "";
    public float? Speed { get; init; }
    public float? Delay { get; init; }
    public IReadOnlyList<string> Sounds { get; init; } = [];
    public IReadOnlyList<int> FrameSequence { get; init; } = [];

    /// <summary>Texte court pour <c>jk-dump-level</c> et le catalogue : type, thing, details donnes par le COG.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Thing >= 0) parts.Add($"thing {Thing}");
        if (this is SwitchMove sw) parts.Add($"{(sw.SwitchIsThing ? "switch thing" : "switch surface")} {sw.SwitchSurfaceOrThing}");
        if (this is ActorTriggered at) parts.Add($"sector {at.Sector}");
        if (this is SurfaceCelSwitch cs) parts.Add($"surface {cs.Surface}");
        if (this is MultiStepPath mp) parts.Add(FormattableString.Invariant($"frames {string.Join(">", mp.Frames)}, wait {mp.Wait:0.##}, {(mp.Loop ? "loop" : "once")}"));
        else if (FrameSequence.Count > 0) parts.Add($"frames {string.Join(">", FrameSequence)}");
        if (Speed is float s) parts.Add(FormattableString.Invariant($"speed {s:0.##}"));
        if (Delay is float d) parts.Add(FormattableString.Invariant($"delay {d:0.##}"));
        if (Sounds.Count > 0) parts.Add("sounds " + string.Join(" ", Sounds));
        if (this is Approximate a) parts.Add("reason: " + a.Reason);
        if (this is Unsupported u) parts.Add("reason: " + u.Reason);
        return GetType().Name + " (" + string.Join(", ", parts) + ")";
    }

    /// <summary>Cle de comparaison (les listes ne se comparent pas par valeur dans un <c>record</c>) : sert a retirer les doublons d'un meme niveau.</summary>
    public virtual string Key =>
        $"{GetType().Name}|{Thing}|{Speed}|{Delay}|{string.Join(',', Sounds)}|{string.Join(',', FrameSequence)}";
}

/// <summary>Porte (ou plateau) qui s'ouvre au toucher, a l'utilisation ou a l'approche, puis se referme apres <c>Delay</c>.</summary>
public sealed record TouchDoor(int Thing) : MoverBehavior(Thing);

/// <summary>Ascenseur a deux positions, lance par le passage du joueur (adjoin ou secteur), revient apres <c>Delay</c>.</summary>
public sealed record TwoStopLift(int Thing) : MoverBehavior(Thing);

/// <summary>Mouvement commande par un interrupteur : une surface (ou un thing si <c>SwitchIsThing</c>) ; <c>Frames</c> = images atteintes.</summary>
public sealed record SwitchMove(int Thing, int SwitchSurfaceOrThing, int[] Frames) : MoverBehavior(Thing)
{
    public bool SwitchIsThing { get; init; }
    public override string Key => base.Key + $"|{SwitchSurfaceOrThing}|{SwitchIsThing}|{string.Join(',', Frames)}";
}

/// <summary>Mouvement en plusieurs etapes (3 images ou plus) : <c>Wait</c> = pause entre deux etapes (secondes), <c>Loop</c> = revient a la position de repos.</summary>
public sealed record MultiStepPath(int Thing, int[] Frames, float Wait, bool Loop) : MoverBehavior(Thing)
{
    public override string Key => base.Key + $"|{string.Join(',', Frames)}|{Wait}|{Loop}";
}

/// <summary>Mouvement declenche par la mort d'un acteur (message <c>killed</c>) : <c>Sector</c> = secteur du thing, a declencher a l'approche.</summary>
public sealed record ActorTriggered(int Thing, int Sector) : MoverBehavior(Thing)
{
    public override string Key => base.Key + $"|{Sector}";
}

/// <summary>Interrupteur qui ne change que la cellule de texture d'une surface (voyant, panneau) : aucun thing ne bouge.</summary>
public sealed record SurfaceCelSwitch(int Surface) : MoverBehavior(-1)
{
    public override string Key => base.Key + $"|{Surface}";
}

/// <summary>Comportement reconstruit par approximation (a partir des images du thing) ; <c>Reason</c> dit pourquoi (journal <c>JK_COG_APPROX</c>).</summary>
public sealed record Approximate(int Thing, string Reason) : MoverBehavior(Thing)
{
    public override string Key => base.Key + $"|{Reason}";
}

/// <summary>Mouvement qu'on ne peut pas reproduire du tout (journal <c>JK_COG_UNSUPPORTED</c>) : le thing reste en position « passage ouvert » (tache 9).</summary>
public sealed record Unsupported(int Thing, string Reason) : MoverBehavior(Thing)
{
    public override string Key => base.Key + $"|{Reason}";
}

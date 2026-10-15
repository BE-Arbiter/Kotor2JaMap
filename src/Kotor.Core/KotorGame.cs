namespace Kotor.Core;

/// <summary>The supported games: Knights of the Old Republic and its sequel.</summary>
public enum KotorGame { Kotor1, Kotor2 }

public static class KotorGames
{
    /// <summary>Prefix of the produced files: pk3 <c>&lt;prefix&gt;_&lt;map&gt;.pk3</c>, shaders <c>shaders/&lt;prefix&gt;_&lt;map&gt;.shader</c>.</summary>
    public static string MapPrefix(KotorGame g) => g == KotorGame.Kotor2 ? "kotor2" : "kotor";

    /// <summary>Folder under <c>textures/</c> and <c>models/</c> in the pk3.</summary>
    public static string AssetFolder(KotorGame g) => g == KotorGame.Kotor2 ? "kotor2" : "kotor";

    public static string DisplayName(KotorGame g) => g == KotorGame.Kotor2 ? "KOTOR 2" : "KOTOR";
}

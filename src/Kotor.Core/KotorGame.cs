namespace Kotor.Core;

/// <summary>
/// The supported game sources: Knights of the Old Republic and its sequel, plus Star Wars: The Old Republic (SWTOR), whose areas
/// go through the same downstream pipeline (scene, compiler, pk3). Existing values keep their numbers.
/// </summary>
public enum KotorGame { Kotor1, Kotor2, Swtor, JediKnight }

public static class KotorGames
{
    /// <summary>Prefix of the produced files: pk3 <c>&lt;prefix&gt;_&lt;map&gt;.pk3</c>, shaders <c>shaders/&lt;prefix&gt;_&lt;map&gt;.shader</c>.</summary>
    public static string MapPrefix(KotorGame g) => g switch { KotorGame.Kotor2 => "kotor2", KotorGame.Swtor => "swtor", KotorGame.JediKnight => "jk", _ => "kotor" };

    /// <summary>Folder under <c>textures/</c> and <c>models/</c> in the pk3.</summary>
    public static string AssetFolder(KotorGame g) => MapPrefix(g);

    public static string DisplayName(KotorGame g) => g switch { KotorGame.Kotor2 => "KOTOR 2", KotorGame.Swtor => "SWTOR", KotorGame.JediKnight => "Jedi Knight", _ => "KOTOR" };
}

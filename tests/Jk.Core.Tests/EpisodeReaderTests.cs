using System.Text;
using Jk.Core.Text;

namespace Jk.Core.Tests;

public class EpisodeReaderTests
{
    private const string Uni = """
        MSGS 3
        # "<key>"  <unused>  "<string>"
        "01NARSHADDA"     0  "1 - Betrayal on Nar Shaddaa"   # commentaire
        "20ABOC"          0  "20 - BOC - Cruelty
        "TITLE_KEY"       0  "Episode # one"
        END
        """;

    [Fact]
    public void UniStrings_ReadsKeysCaseInsensitive_ToleratesMissingClosingQuote()
    {
        var uni = UniStrings.Parse(Uni);
        Assert.Equal("1 - Betrayal on Nar Shaddaa", uni.Get("01narshadda"));
        Assert.Equal("20 - BOC - Cruelty", uni.Get("20ABOC"));
        Assert.Equal("Episode # one", uni.Get("title_key"));   // un # dans la chaine n'est pas un commentaire
        Assert.Null(uni.Get("absent"));
        Assert.Equal(3, uni.Count);
    }

    [Fact]
    public void UniStrings_EmptyText_IsEmpty() => Assert.Equal(0, UniStrings.Parse("").Count);

    [Fact]
    public void Episode_TitleTypeAndLevelsInOrder()
    {
        const string jk = """
            "TITLE_KEY"
            # Title must be first line.
            TYPE  1  # single-player
            SEQ   3
            10:      1     1        CUT      03-04a.smk        0           0           11    -1
            11:      1     1        LEVEL    01narshadda.jkl   0           0           20    -1    # Level 1
            20:      1     2        LEVEL    04EscapeHouse.jkl 0           0           30    -1
            30:      0     3        LEVEL    m_boss15.jkl      0           0           -1    -1
            end
            """;
        EpisodeInfo e = EpisodeReader.Read(jk, UniStrings.Parse(Uni));
        Assert.Equal("Episode # one", e.Title);
        Assert.Equal(1, e.Type);
        Assert.Equal(["01narshadda", "04escapehouse", "m_boss15"], e.Levels);
    }

    [Fact]
    public void Episode_TitleWithoutUniEntry_IsKeptAsWritten()
    {
        EpisodeInfo e = EpisodeReader.Read("\"Capture the Flag\"\nTYPE\t8\n10:\t0\t1\tLEVEL\tc1.jkl\t0\t0\t20\t-1\n", null);
        Assert.Equal("Capture the Flag", e.Title);
        Assert.Equal(8, e.Type);
        Assert.Equal(["c1"], e.Levels);
    }

    [Fact]
    public void Episode_GarbageInput_DoesNotThrow()
    {
        EpisodeInfo e = EpisodeReader.Read("\u0001\u0002 LEVEL\n::: LEVEL\n7: x y LEVEL\n", null);
        Assert.Empty(e.Levels);
        Assert.Equal("", e.Title);
        Assert.Equal(EpisodeInfo.Empty.Levels.Count, e.Levels.Count);
    }

    [Fact]
    public void LevelName_UsesUniThenFileName()
    {
        var uni = UniStrings.Parse(Uni);
        Assert.Equal("1 - Betrayal on Nar Shaddaa", EpisodeReader.LevelName("01narshadda", uni));
        Assert.Equal("m_unknown", EpisodeReader.LevelName("m_unknown", uni));
        Assert.Equal("m_unknown", EpisodeReader.LevelName("m_unknown", null));
    }

    [Fact]
    public void Uni_DecodedAsLatin1_KeepsAccents()
    {
        byte[] raw = Encoding.Latin1.GetBytes("\"C1\" 0 \"Le Duel été\"");
        Assert.Equal("Le Duel été", UniStrings.Parse(Encoding.Latin1.GetString(raw)).Get("c1"));
    }
}

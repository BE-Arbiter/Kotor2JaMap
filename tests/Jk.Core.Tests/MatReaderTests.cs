using Jk.Core.Gob;
using Jk.Core.Textures;
using Kotor.Core.Textures;

namespace Jk.Core.Tests;

public class MatReaderTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    // --- Fabrication de fichiers synthetiques ---

    private static byte[] BuildCmp(Func<int, (byte R, byte G, byte B)> color, bool transparency = false)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write("CMP "u8.ToArray());
        w.Write(0x1E);
        w.Write(transparency ? 1 : 0);
        w.Write(new byte[52]);
        for (int i = 0; i < 256; i++) { var c = color(i); w.Write(c.R); w.Write(c.G); w.Write(c.B); }
        for (int level = 0; level < 64; level++)
            for (int i = 0; i < 256; i++) w.Write((byte)(i * (63 - level) / 63)); // niveau 0 = pleine lumiere
        if (transparency) w.Write(new byte[256 * 256]);
        return ms.ToArray();
    }

    private static void WriteMatHeader(BinaryWriter w, int type, int cels, int dataBlocks, int bpp, int mode = 0,
        int[]? channels = null)
    {
        w.Write("MAT "u8.ToArray());
        w.Write(0x32);
        w.Write(type);
        w.Write(cels);
        w.Write(dataBlocks);
        w.Write(mode);
        w.Write(bpp);
        // rouge/vert/bleu : bits, decalage gauche, decalage droit ; puis alpha : bits, decalage gauche, decalage droit
        // Ordre du fichier : bits R,G,B ; decalages gauche R,G,B ; decalages droite R,G,B ; alpha bits, gauche, droite.
        int[] ch = channels ?? new int[12];
        for (int i = 0; i < 12; i++) w.Write(ch[i]);
    }

    private static byte[] BuildColorMat(params int[] colorIndexes)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        WriteMatHeader(w, 0, colorIndexes.Length, 0, 8);
        foreach (int c in colorIndexes)
        {
            w.Write(0); w.Write(c);
            for (int i = 0; i < 4; i++) w.Write(1.0f);
            w.Write(0);
        }
        return ms.ToArray();
    }

    /// <summary>MAT texture : un bloc de donnees par cellule, avec un second niveau de mipmap (a ignorer).</summary>
    private static byte[] BuildTexMat(int width, int height, int bpp, bool transparent, int[]? channels, int mode,
        params byte[][] celPixels)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        WriteMatHeader(w, 2, celPixels.Length, celPixels.Length, bpp, mode, channels);
        for (int c = 0; c < celPixels.Length; c++)
        {
            w.Write(8); w.Write(c);
            for (int i = 0; i < 4; i++) w.Write(1.0f);
            w.Write(0); w.Write(0xBFF78482u); w.Write(0); w.Write(0);
        }
        foreach (byte[] px in celPixels)
        {
            w.Write(width); w.Write(height); w.Write(transparent ? 1 : 0); w.Write(0); w.Write(0);
            w.Write(2);
            w.Write(px);
            w.Write(new byte[Math.Max(1, width / 2) * Math.Max(1, height / 2) * (bpp / 8)]);
        }
        return ms.ToArray();
    }

    private static CmpPalette RedGreenPalette() => CmpPalette.Read(BuildCmp(i => i switch
    {
        1 => ((byte)255, (byte)0, (byte)0),
        2 => ((byte)0, (byte)255, (byte)0),
        3 => ((byte)0, (byte)0, (byte)255),
        _ => ((byte)10, (byte)20, (byte)30),
    }));

    private static (byte R, byte G, byte B, byte A) Px(KotorTexture t, int x, int y)
    {
        int o = (y * t.Width + x) * 4;
        return (t.Rgba[o], t.Rgba[o + 1], t.Rgba[o + 2], t.Rgba[o + 3]);
    }

    private static (byte R, byte G, byte B, byte A)[] AllPx(KotorTexture t) =>
        Enumerable.Range(0, t.Width * t.Height).Select(i => Px(t, i % t.Width, i / t.Width)).ToArray();

    // --- Tests ---

    [Fact]
    public void Mat8Bit_DecodesWithPalette()
    {
        // Image 2x2 : indices 1 2 / 3 1, sans transparence.
        byte[] mat = BuildTexMat(2, 2, 8, false, null, 0, new byte[] { 1, 2, 3, 1 });
        JkMaterial m = MatReader.Read("test.mat", mat, RedGreenPalette());
        Assert.Equal("test.mat", m.Name);
        Assert.False(m.IsSolidColor);
        Assert.False(m.HasTransparency);
        KotorTexture t = Assert.Single(m.Cels);
        Assert.Equal((2, 2), (t.Width, t.Height));
        Assert.False(t.HasAlpha);
        Assert.Equal(2 * 2 * 4, t.Rgba.Length);
        var all = AllPx(t);
        Assert.Equal(2, all.Count(p => p == ((byte)255, (byte)0, (byte)0, (byte)255)));
        Assert.Equal(1, all.Count(p => p == ((byte)0, (byte)255, (byte)0, (byte)255)));
        Assert.Equal(1, all.Count(p => p == ((byte)0, (byte)0, (byte)255, (byte)255)));
    }

    [Fact]
    public void Mat8Bit_IndexZeroIsTransparentWhenDeclared()
    {
        byte[] mat = BuildTexMat(2, 2, 8, true, null, 0, new byte[] { 0, 1, 1, 0 });
        JkMaterial m = MatReader.Read("t.mat", mat, RedGreenPalette());
        Assert.True(m.HasTransparency);
        KotorTexture t = Assert.Single(m.Cels);
        Assert.True(t.HasAlpha);
        var all = AllPx(t);
        Assert.Equal(2, all.Count(p => p.A == 0));
        Assert.Equal(2, all.Count(p => p.A == 255 && p.R == 255));
    }

    [Fact]
    public void Mat8Bit_IndexZeroOpaqueWhenNotDeclared()
    {
        byte[] mat = BuildTexMat(2, 2, 8, false, null, 0, new byte[] { 0, 1, 1, 0 });
        KotorTexture t = Assert.Single(MatReader.Read("t.mat", mat, RedGreenPalette()).Cels);
        Assert.All(AllPx(t), p => Assert.Equal(255, p.A));
    }

    [Fact]
    public void Mat16Bit_Rgb565_Decodes()
    {
        // RGB565 : bits 5/6/5, decalages gauche 11/5/0, droite 3/2/3.
        int[] ch = { 5, 6, 5, 11, 5, 0, 3, 2, 3, 0, 0, 0 };
        ushort red = 0xF800, green = 0x07E0, blue = 0x001F, black = 0;
        byte[] px = new[] { red, green, blue, black }.SelectMany(BitConverter.GetBytes).ToArray();
        JkMaterial m = MatReader.Read("c.mat", BuildTexMat(2, 2, 16, false, ch, 1, px), CmpPalette.Default);
        KotorTexture t = Assert.Single(m.Cels);
        var all = AllPx(t);
        Assert.Contains(((byte)255, (byte)0, (byte)0, (byte)255), all);
        Assert.Contains(((byte)0, (byte)255, (byte)0, (byte)255), all);
        Assert.Contains(((byte)0, (byte)0, (byte)255, (byte)255), all);
        Assert.Contains(((byte)0, (byte)0, (byte)0, (byte)255), all);
    }

    [Fact]
    public void Mat16Bit_Rgba4444_KeepsAlpha()
    {
        // RGBA 4-4-4-4 : decalages gauche rouge 12, vert 8, bleu 4 ; alpha 4 bits a 0.
        int[] ch = { 4, 4, 4, 12, 8, 4, 4, 4, 4, 4, 0, 4 };
        ushort opaqueRed = 0xF00F, clear = 0x0F00;
        byte[] px = new[] { opaqueRed, clear, opaqueRed, clear }.SelectMany(BitConverter.GetBytes).ToArray();
        JkMaterial m = MatReader.Read("a.mat", BuildTexMat(2, 2, 16, false, ch, 2, px), CmpPalette.Default);
        KotorTexture t = Assert.Single(m.Cels);
        Assert.True(t.HasAlpha);
        Assert.True(m.HasTransparency);
        var all = AllPx(t);
        Assert.Equal(2, all.Count(p => p.A == 255 && p.R == 255 && p.G == 0));
        Assert.Equal(2, all.Count(p => p.A == 0));
    }

    [Fact]
    public void MatColorOnly_ProducesSolidTexture()
    {
        JkMaterial m = MatReader.Read("solid.mat", BuildColorMat(2), RedGreenPalette());
        Assert.True(m.IsSolidColor);
        KotorTexture t = Assert.Single(m.Cels);
        Assert.True(t.Width >= 1 && t.Height >= 1);
        Assert.All(AllPx(t), p => Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), p));
    }

    [Fact]
    public void MatMultiCel_KeepsAllCels()
    {
        byte[] mat = BuildTexMat(2, 2, 8, false, null, 0,
            new byte[] { 1, 1, 1, 1 }, new byte[] { 2, 2, 2, 2 }, new byte[] { 3, 3, 3, 3 });
        JkMaterial m = MatReader.Read("anim.mat", mat, RedGreenPalette());
        Assert.Equal(3, m.Cels.Count);
        Assert.Equal(((byte)255, (byte)0, (byte)0, (byte)255), Px(m.Cels[0], 0, 0)); // cellule 0 = depart
        Assert.Equal(((byte)0, (byte)255, (byte)0, (byte)255), Px(m.Cels[1], 0, 0));
        Assert.Equal(((byte)0, (byte)0, (byte)255, (byte)255), Px(m.Cels[2], 1, 1));

        JkMaterial colors = MatReader.Read("c.mat", BuildColorMat(1, 2, 3), RedGreenPalette());
        Assert.Equal(3, colors.Cels.Count);
    }

    [Fact]
    public void MissingCmp_FallsBackToDefaultPalette()
    {
        byte[] mat = BuildTexMat(2, 1, 8, false, null, 0, new byte[] { 10, 200 });
        JkMaterial m = MatReader.Read("g.mat", mat, CmpPalette.Default);
        KotorTexture t = Assert.Single(m.Cels);
        var all = AllPx(t);
        Assert.Contains(((byte)10, (byte)10, (byte)10, (byte)255), all); // palette par defaut : gris
        Assert.Contains(((byte)200, (byte)200, (byte)200, (byte)255), all);

        Assert.Equal(((byte)7, (byte)7, (byte)7), CmpPalette.Default.Rgb(7));
        Assert.False(CmpPalette.Default.HasLightTable);
        Assert.Throws<InvalidDataException>(() => CmpPalette.Read(new byte[10]));
    }

    [Fact]
    public void CmpPalette_ReadsColorsAndLightTables()
    {
        CmpPalette p = CmpPalette.Read(BuildCmp(i => ((byte)i, (byte)(255 - i), (byte)0), transparency: true));
        Assert.Equal(((byte)5, (byte)250, (byte)0), p.Rgb(5));
        Assert.True(p.HasLightTable);
        Assert.Equal(64, p.LightLevels);
        Assert.Equal(200, p.LightedIndex(0, 200));  // pleine lumiere
        Assert.Equal(0, p.LightedIndex(63, 200));   // noir
    }

    [Fact]
    public void MatTruncated_Throws()
    {
        Assert.Throws<InvalidDataException>(() => MatReader.Read("x.mat", new byte[20], CmpPalette.Default));
        byte[] good = BuildTexMat(2, 2, 8, false, null, 0, new byte[] { 1, 2, 3, 4 });
        Assert.Throws<InvalidDataException>(() => MatReader.Read("x.mat", good[..(good.Length - 20)], CmpPalette.Default));
    }

    [SkippableFact]
    public void AllMatsInRes2_Decode()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed");
        using var fs = JkFileSystem.ForEpisode(JkDir, "JK1.GOB");
        byte[]? cmpBytes = fs.TryRead(@"misc\cmp\01narsh.cmp");
        Skip.If(cmpBytes is null, "01narsh.cmp not found");
        CmpPalette pal = CmpPalette.Read(cmpBytes!);
        int count = 0;
        foreach (string path in fs.List("mat", ".mat").Concat(fs.List(@"3do\mat", ".mat")))
        {
            JkMaterial m = MatReader.Read(path, fs.TryRead(path)!, pal);
            Assert.NotEmpty(m.Cels);
            foreach (KotorTexture t in m.Cels)
            {
                Assert.True(t.Width > 0 && t.Height > 0, path);
                Assert.Equal(t.Width * t.Height * 4, t.Rgba.Length);
            }
            count++;
        }
        Assert.True(count > 1900, $"only {count} materials found");
    }
}

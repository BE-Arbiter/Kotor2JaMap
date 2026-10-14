using Kotor.Core.Archives;
using Kotor.Core.Export;
using Kotor.Core.Tests.Archives;
using Kotor.Core.Textures;

namespace Kotor.Core.Tests.Textures;

public class TextureLookupTests
{
    [Fact]
    public void Lookup_prefers_tpc_over_tga_for_diffuse()
    {
        string dir = Path.Combine(Path.GetTempPath(), "k2j_kotor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Override"));
        Directory.CreateDirectory(Path.Combine(dir, "modules"));
        Directory.CreateDirectory(Path.Combine(dir, "TexturePacks"));
        try
        {
            using (var ms = new MemoryStream())
            {
                TgaWriter.Write(ms, 1, 1, [1, 2, 3, 255]);
                File.WriteAllBytes(Path.Combine(dir, "Override", "lta_x.tga"), ms.ToArray());
            }
            byte[] tpc = new byte[128 + 3];
            tpc[8] = 1; tpc[10] = 1; tpc[12] = 2; tpc[13] = 1;
            tpc[128] = 9; tpc[129] = 8; tpc[130] = 7;
            File.WriteAllBytes(Path.Combine(dir, "TexturePacks", "swpc_tex_tpa.erf"),
                ArchiveBuilders.Erf("ERF V1.0", ("lta_x", (ushort)ResourceType.Tpc, tpc)));
            using var r = ResourceResolver.ForModule(dir, "tar_x");
            var d = TextureLookup.Find(r, "LTA_X", TextureKind.Diffuse);
            Assert.NotNull(d);
            Assert.Equal(new byte[] { 9, 8, 7, 255 }, d!.Rgba);
            var l = TextureLookup.Find(r, "LTA_X", TextureKind.Lightmap);
            Assert.NotNull(l);
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, l!.Rgba);
            Assert.Null(TextureLookup.Find(r, "nope", TextureKind.Diffuse));
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void Dump(string name, KotorTexture t)
    {
        string? d = AppContext.BaseDirectory;
        while (d is not null && !File.Exists(Path.Combine(d, "Kotor2Ja.sln"))) d = Path.GetDirectoryName(d);
        string outDir = Path.Combine(d ?? AppContext.BaseDirectory, "out", "test-textures");
        Directory.CreateDirectory(outDir);
        using var f = File.Create(Path.Combine(outDir, name + ".png"));
        PngWriter.WriteRgba(f, t.Width, t.Height, t.Rgba);
    }

    private static bool Pot(int v) => v > 0 && (v & (v - 1)) == 0;

    [SkippableFact]
    public void Real_lightmap_and_diffuse_decode()
    {
        Skip.If(TestEnv.KotorDir is null, "KOTOR absent");
        using var r = ResourceResolver.ForModule(TestEnv.KotorDir!, "tar_m02aa");
        var lm = TextureLookup.Find(r, "m02aa_01a_lm0", TextureKind.Lightmap);
        Assert.NotNull(lm);
        Assert.True(Pot(lm!.Width) && Pot(lm.Height));
        Dump("m02aa_01a_lm0", lm);
        var diff = TextureLookup.Find(r, "lta_cli01", TextureKind.Diffuse);
        Assert.NotNull(diff);
        Assert.True(Pot(diff!.Width) && Pot(diff.Height));
        Dump("lta_cli01", diff);
    }
}

using System.Numerics;
using Jk.Core.Gob;
using Jk.Core.Models;

namespace Jk.Core.Tests;

public class KeyReaderTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    /// <summary>Animation synthetique : un noeud, deux images cles (0 et 10), lacet de 350 a 10 degres (passage par 0).</summary>
    internal const string TwoFrames = """
        # KEYFRAME 'two.key'
        SECTION: HEADER
        FLAGS  0x0000
        TYPE   0xFFFF
        FRAMES 11
        FPS    15.000
        JOINTS 1
        SECTION: KEYFRAME NODES
        NODES 1
        NODE    0
        MESH NAME fan
        ENTRIES 2
        # num:   frame:   flags:           x:           y:           z:           p:           y:           r:
        #                                 dx:          dy:          dz:          dp:          dy:          dr:
           0:        0   0x0001   0.00000000   0.00000000   0.00000000   0.00000000 350.00000000   0.00000000
                                  1.0   2.0   0.0   0.0   2.0   0.0
           1:       10   0x0001  10.00000000  20.00000000   0.00000000   0.00000000  10.00000000   0.00000000
                                  0.0   0.0   0.0   0.0   0.0   0.0
        """;

    [Fact]
    public void Key_TwoFrames_Interpolated()
    {
        KeyAnimation k = KeyReader.Read(TwoFrames);

        Assert.Equal(11, k.Frames);
        Assert.Equal(15f, k.Fps);
        Assert.Single(k.Nodes);
        Assert.Equal("fan", k.Nodes[0].MeshName);
        Assert.Equal(2, k.Nodes[0].Entries.Count);
        Assert.Equal(10, k.Nodes[0].Entries[1].Frame);

        var (p0, r0) = k.Nodes[0].PoseAt(0);
        Assert.Equal(Vector3.Zero, p0);
        Assert.Equal(350f, r0.Y, 3);

        var (p5, r5) = k.Nodes[0].PoseAt(5);
        Assert.Equal(5f, p5.X, 3); Assert.Equal(10f, p5.Y, 3);
        // 350 -> 10 par le plus court chemin : milieu a 0 (ou 360), jamais 180.
        Assert.True(Math.Abs(Math.IEEERemainder(r5.Y, 360.0)) < 1e-3, $"yaw {r5.Y}");

        // Au-dela de la derniere image cle : la pose est maintenue.
        var (p20, r20) = k.Nodes[0].PoseAt(20);
        Assert.Equal(10f, p20.X, 3);
        Assert.Equal(10f, r20.Y, 3);
    }

    [Fact]
    public void Key_FastSpin_FollowsPerFrameDelta()
    {
        // 0 -> 270 en 3 images avec +90 par image : le plus court chemin dirait -90, la variation par image impose +90 par image.
        const string text = """
            SECTION: HEADER
            FRAMES 4
            FPS 10
            SECTION: KEYFRAME NODES
            NODES 1
            NODE 0
            MESH NAME fan
            ENTRIES 2
            0: 0 0x0003 0 0 0 0 0 0
               0 0 0 0 90 0
            1: 3 0x0003 0 0 0 0 270 0
               0 0 0 0 0 0
            """;
        KeyAnimation k = KeyReader.Read(text);
        Assert.Equal(90f, k.Nodes[0].PoseAt(1).PitchYawRoll.Y, 3);
        Assert.Equal(180f, k.Nodes[0].PoseAt(2).PitchYawRoll.Y, 3);
        Assert.Equal(270f, k.Nodes[0].PoseAt(3).PitchYawRoll.Y, 3);
    }

    [Fact]
    public void Key_Malformed_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => KeyReader.Read("nothing here"));
        Assert.Throws<InvalidDataException>(() => KeyReader.Read("SECTION: HEADER\nFRAMES 3\nFPS 15\nSECTION: KEYFRAME NODES\nNODES 1\nNODE 0\nMESH NAME a\nENTRIES 2\n0: 0 0x1 0 0 0 0 0 0\n0 0 0 0 0 0\n"));
    }

    [SkippableFact]
    public void AllKeysInRes2_Read()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, "JK1.GOB");
        string[] files = fs.List(@"3do\key", ".key").ToArray();
        Assert.Equal(504, files.Length);
        foreach (string path in files)
        {
            KeyAnimation k = KeyReader.Read(KeyReader.Decode(fs.TryRead(path)!));
            Assert.True(k.Frames > 0 && k.Fps > 0, path);
            Assert.NotEmpty(k.Nodes);
            foreach (KeyNode n in k.Nodes) Assert.NotEmpty(n.Entries);
        }
    }
}

using System.Numerics;
using Jk.Core.Gob;
using Jk.Core.Models;

namespace Jk.Core.Tests;

public class ThreeDoReaderTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    /// <summary>3DO synthetique : deux noeuds (racine, enfant decale de (1, 2, 3) et tourne de 90 degres de lacet), un triangle par maillage, deux geosets.</summary>
    internal const string TwoNodes = """
        # MODEL 'two.3do' created by hand
        SECTION: HEADER
        3DO 2.1
        SECTION: MODELRESOURCE
        MATERIALS 2
        0: a.mat
        1: b.mat
        SECTION: GEOMETRYDEF
        RADIUS 1.0
        INSERT OFFSET 0.0 0.0 0.5
        GEOSETS 2
        GEOSET 0
        MESHES 2
        MESH 0
        NAME base
        RADIUS 1.0
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 3
        0: 0.0 0.0 0.0 0.0
        1: 1.0 0.0 0.0 0.0
        2: 0.0 1.0 0.0 0.0
        TEXTURE VERTICES 3
        0: 0.0 0.0
        1: 64.0 0.0
        2: 0.0 -64.0
        VERTEX NORMALS
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        FACES 1
        0: 0 0x0000 4 3 1 0.0 3 0,0 1,1 2,2
        FACE NORMALS
        0: 0.0 0.0 1.0
        MESH 1
        NAME arm
        RADIUS 0.5
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 3
        0: 0.0 0.0 0.0 0.0
        1: 0.5 0.0 0.0 0.0
        2: 0.0 0.5 0.0 0.0
        TEXTURE VERTICES 3
        0: 0.0 0.0
        1: 32.0 0.0
        2: 0.0 -32.0
        VERTEX NORMALS
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        FACES 1
        0: 1 0x0001 4 3 1 0.0 3 0,0 1,1 2,2
        FACE NORMALS
        0: 0.0 0.0 1.0
        GEOSET 1
        MESHES 1
        MESH 0
        NAME lod
        RADIUS 0.1
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 0
        TEXTURE VERTICES 0
        VERTEX NORMALS
        FACES 0
        FACE NORMALS
        SECTION: HIERARCHYDEF
        HIERARCHY NODES 2
        0: 0x0000 0x00001 0 -1 1 -1 1 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 Base
        1: 0x0000 0x00001 1 0 -1 -1 0 1.0 2.0 3.0 0.0 90.0 0.0 0.1 0.2 0.3 Arm
        """;

    [Fact]
    public void ThreeDo_HierarchyApplied()
    {
        ThreeDoModel m = ThreeDoReader.Read(TwoNodes);

        Assert.Equal(["a.mat", "b.mat"], m.Materials);
        Assert.Equal(2, m.Geosets.Count);
        Assert.Equal(2, m.Geosets[0].Meshes.Count);
        Assert.Equal(2, m.Nodes.Count);
        Assert.Equal(-1, m.Nodes[0].Parent);
        Assert.Equal(0, m.Nodes[1].Parent);
        Assert.Equal("Arm", m.Nodes[1].Name);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), m.Nodes[1].Pivot);
        Assert.Equal(new Vector3(0, 0, 0.5f), m.InsertOffset);

        ThreeDoMesh arm = m.Geosets[0].Meshes[1];
        Assert.Equal("arm", arm.Name);
        Assert.Equal(3, arm.Vertices.Length);
        Assert.Equal(new Vector2(0, -32), arm.TexVertices[2]);
        ThreeDoFace f = arm.Faces[0];
        Assert.Equal(1, f.Material);
        Assert.Equal(1u, f.Type);
        Assert.Equal([(0, 0), (1, 1), (2, 2)], f.Corners.Select(c => (c.Vertex, c.TexVertex)).ToArray());

        // Poses de repos : l'enfant est decale de (1, 2, 3) et tourne de 90 degres de lacet autour de Z (sens trigonometrique).
        Matrix4x4[] world = m.NodeMatrices();
        Vector3 childOrigin = Vector3.Transform(Vector3.Zero, world[1]);
        Assert.Equal(1f, childOrigin.X, 4); Assert.Equal(2f, childOrigin.Y, 4); Assert.Equal(3f, childOrigin.Z, 4);
        Vector3 childX = Vector3.Transform(Vector3.UnitX, world[1]);   // +X local devient +Y apres un lacet de 90 degres
        Assert.Equal(1f, childX.X, 4); Assert.Equal(3f, childX.Y, 4); Assert.Equal(3f, childX.Z, 4);
        Assert.Equal(Vector3.Zero, Vector3.Transform(Vector3.Zero, world[0]));
    }

    [Fact]
    public void ThreeDo_PoseOverride_ReplacesNodeTransform()
    {
        ThreeDoModel m = ThreeDoReader.Read(TwoNodes);
        Matrix4x4[] world = m.NodeMatrices((i, rest) => i == 1 ? (new Vector3(10, 0, 0), Vector3.Zero) : rest);
        Vector3 p = Vector3.Transform(Vector3.Zero, world[1]);
        Assert.Equal(10f, p.X, 4); Assert.Equal(0f, p.Y, 4);
    }

    [Fact]
    public void ThreeDo_Malformed_ThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => ThreeDoReader.Read("hello"));
        Assert.Throws<InvalidDataException>(() => ThreeDoReader.Read("SECTION: HEADER\n3DO 2.1\nSECTION: GEOMETRYDEF\nGEOSETS 1\nGEOSET 0\nMESHES 1\nMESH 0\nVERTICES 2\n0: 1 2 3 0\n"));
    }

    [SkippableFact]
    public void All3doInRes2_Read()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, "JK1.GOB");
        string[] files = fs.List("3do", ".3do").ToArray();
        Assert.Equal(477, files.Length);
        foreach (string path in files)
        {
            ThreeDoModel m = ThreeDoReader.Read(ThreeDoReader.Decode(fs.TryRead(path)!));
            Assert.NotEmpty(m.Geosets);
            Assert.NotEmpty(m.Nodes);
            Matrix4x4[] world = m.NodeMatrices();
            Assert.Equal(m.Nodes.Count, world.Length);
            foreach (ThreeDoNode n in m.Nodes)
                Assert.True(n.Mesh < m.Geosets[0].Meshes.Count, $"{path}: node {n.Name} references mesh {n.Mesh}");
            foreach (ThreeDoMesh mesh in m.Geosets[0].Meshes)
                foreach (ThreeDoFace face in mesh.Faces)
                    foreach (var (v, t) in face.Corners)
                    {
                        Assert.InRange(v, 0, mesh.Vertices.Length - 1);
                        if (face.Geo >= 4) Assert.InRange(t, 0, mesh.TexVertices.Length - 1); // faces en couleur pleine : indice de texture sans objet
                    }
        }
    }
}

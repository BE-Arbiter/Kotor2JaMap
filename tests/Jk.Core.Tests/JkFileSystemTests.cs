using System.Text;
using Jk.Core.Gob;

namespace Jk.Core.Tests;

public class JkFileSystemTests
{
    private static string Text(byte[]? b) => b is null ? "<null>" : Encoding.ASCII.GetString(b);

    [Fact]
    public void JkFileSystem_EpisodeOverridesResource()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            GobBuilder.WriteGob(dir, "Episode", "JK1.GOB", (@"mat\dflt.mat", "episode"));
            GobBuilder.WriteGob(dir, "Resource", "Res2.gob", (@"mat\dflt.mat", "res2"), (@"mat\only2.mat", "two"));
            GobBuilder.WriteGob(dir, "Resource", "Res1hi.gob", (@"mat\dflt.mat", "res1"), (@"mat\only1.mat", "one"));
            using var fs = JkFileSystem.ForEpisode(dir, "JK1.GOB");
            Assert.Equal("episode", Text(fs.TryRead(@"mat\dflt.mat")));
            Assert.Equal("two", Text(fs.TryRead(@"mat\only2.mat")));
            Assert.Equal("one", Text(fs.TryRead(@"mat\only1.mat")));
            Assert.Null(fs.TryRead(@"mat\missing.mat"));
            Assert.Equal(3, fs.List("mat", ".mat").Count());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void JkFileSystem_CaseInsensitive()
    {
        string dir = GobBuilder.TempDir();
        try
        {
            GobBuilder.WriteGob(dir, "Episode", "JK1.GOB", (@"mat\dflt.mat", "abc"));
            using var fs = JkFileSystem.ForEpisode(dir, "JK1.GOB");
            Assert.Equal("abc", Text(fs.TryRead(@"MAT\DFLT.MAT")));
            Assert.True(fs.Exists(@"Mat\Dflt.Mat"));
        }
        finally { Directory.Delete(dir, true); }
    }
}

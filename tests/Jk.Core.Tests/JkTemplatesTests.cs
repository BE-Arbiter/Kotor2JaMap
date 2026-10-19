using Jk.Core.Jkl;

namespace Jk.Core.Tests;

public class JkTemplatesTests
{
    private static JklTemplate T(string name, string parent, params (string K, string V)[] p) =>
        new(name, parent, p.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Template_ChildOverridesParent()
    {
        var tpl = new JkTemplates(new[]
        {
            T("_base", "none", ("type", "cog"), ("collide", "1")),
            T("mid", "_base", ("collide", "3"), ("size", "0.1")),
            T("leaf", "mid", ("size", "0.5")),
        });
        var r = tpl.Resolve("leaf");
        Assert.Equal("cog", r["type"]);
        Assert.Equal("3", r["collide"]);
        Assert.Equal("0.5", r["size"]);
        Assert.Empty(tpl.Unresolved);
    }

    [Fact]
    public void Template_LaterSourceReplacesEarlier()
    {
        var tpl = new JkTemplates(new[] { T("x", "none", ("a", "1")) }, new[] { T("X", "none", ("a", "2")) });
        Assert.Equal("2", tpl.Resolve("x")["a"]);
    }

    [Fact]
    public void Template_CircularOrMissingParent_DoesNotThrow()
    {
        var tpl = new JkTemplates(new[]
        {
            T("a", "b", ("pa", "1")),
            T("b", "a", ("pb", "2")),
            T("orphan", "ghost", ("po", "3")),
        });
        var cyc = tpl.Resolve("a");
        Assert.Equal("1", cyc["pa"]);
        Assert.Equal("2", cyc["pb"]);
        var orphan = tpl.Resolve("orphan");
        Assert.Equal("3", orphan["po"]);
        Assert.Contains("ghost", tpl.Unresolved);
        Assert.NotEmpty(tpl.Unresolved);
        Assert.Empty(tpl.Resolve("unknown_name"));
    }
}

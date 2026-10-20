using Kotor.Core.Export;

namespace Kotor.Core.Tests.Export;

public class SettingsProbeTests
{
    private static string Temp() => Path.Combine(Path.GetTempPath(), "probe_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Check_reports_each_path()
    {
        string game = Temp(), ja = Temp();
        try
        {
            Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "chitin.key"), "");
            Directory.CreateDirectory(ja);
            var s = SettingsProbe.Check(game, "", ja, Path.Combine(Path.GetTempPath(), "newwork"));
            Assert.Equal(new PathStatus(true, true, true, true), s);
            s = SettingsProbe.Check(ja, game + "_missing", ja + "_missing", "");   // no chitin.key, missing, missing, empty
            Assert.Equal(new PathStatus(false, false, false, false), s);
        }
        finally { foreach (var d in new[] { game, ja }) if (Directory.Exists(d)) Directory.Delete(d, true); }
    }


    [Fact]
    public void Check_reports_the_jedi_knight_folder_which_is_optional()
    {
        string jk = Temp();
        try
        {
            Directory.CreateDirectory(Path.Combine(jk, "Episode"));
            Assert.False(SettingsProbe.Check("", "", "", "", jk).JediKnight);                      // no JK1.GOB
            File.WriteAllText(Path.Combine(jk, "Episode", "JK1.GOB"), "");
            Assert.True(SettingsProbe.Check("", "", "", "", jk).JediKnight);
            Assert.False(SettingsProbe.Check("", "", "", "", jk + "_missing").JediKnight);
            Assert.True(SettingsProbe.Check("", "", "", "", "").JediKnight);                       // empty: not configured, not an error
            Assert.True(SettingsProbe.Check("", "", "", "").JediKnight);
        }
        finally { Directory.Delete(jk, true); }
    }

    [Fact]
    public void ApplyDetected_fills_or_keeps_the_jedi_knight_folder()
    {
        var found = new GamePaths(null, null, null, @"F:\w", @"F:\steam\jk");
        var empty = SettingsProbe.ApplyDetected(new ExportOptions("", "", ""), found, new PathStatus(false, true, false, false, JediKnight: true));
        Assert.Equal(@"F:\steam\jk", empty.JediKnightDir);                                        // empty: filled
        var bad = SettingsProbe.ApplyDetected(new ExportOptions("", "", "") { JediKnightDir = @"C:\bad" }, found, new PathStatus(false, true, false, false, JediKnight: false));
        Assert.Equal(@"F:\steam\jk", bad.JediKnightDir);                                          // invalid and found: replaced
        var keep = SettingsProbe.ApplyDetected(new ExportOptions("", "", "") { JediKnightDir = @"C:\mine\jk" }, found, new PathStatus(false, true, false, false, JediKnight: true));
        Assert.Equal(@"C:\mine\jk", keep.JediKnightDir);                                          // valid: kept
        var none = SettingsProbe.ApplyDetected(new ExportOptions("", "", "") { JediKnightDir = @"C:\bad" }, new GamePaths(null, null, null, "w"), new PathStatus(false, true, false, false, JediKnight: false));
        Assert.Equal(@"C:\bad", none.JediKnightDir);                                              // nothing found: not erased
    }
    [Fact]
    public void ApplyDetected_keeps_valid_fields_and_unfound_games()
    {
        var current = new ExportOptions(@"C:\mine\k1", @"C:\bad\ja", @"C:\mywork", Kotor2Dir: @"C:\bad\k2");
        var found = new GamePaths(@"F:\steam\k1", null, @"F:\steam\ja", @"F:\def\work");
        var status = new PathStatus(Kotor: true, Kotor2: false, Ja: false, Work: true);
        var r = SettingsProbe.ApplyDetected(current, found, status);
        Assert.Equal(@"C:\mine\k1", r.KotorDir);        // valid: kept
        Assert.Equal(@"C:\bad\k2", r.Kotor2Dir);        // invalid but nothing found: not erased
        Assert.Equal(@"F:\steam\ja", r.JaBaseDir);      // invalid and found: replaced
        Assert.Equal(@"C:\mywork", r.WorkDir);          // valid: kept
    }

    [Fact]
    public void ApplyDetected_fills_empty_fields()
    {
        var r = SettingsProbe.ApplyDetected(new ExportOptions("", "", "", Kotor2Dir: ""),
            new GamePaths(@"F:\k1", @"F:\k2", @"F:\ja", @"F:\w"), new PathStatus(false, true, false, false));
        Assert.Equal((@"F:\k1", @"F:\k2", @"F:\ja", @"F:\w"), (r.KotorDir, r.Kotor2Dir, r.JaBaseDir, r.WorkDir));
    }

    [Fact]
    public async Task BackgroundJob_does_not_restart_while_running_and_delivers_result()
    {
        var job = new BackgroundJob<int>();
        using var gate = new ManualResetEventSlim();
        int calls = 0;
        Assert.True(job.TryStart(() => { Interlocked.Increment(ref calls); gate.Wait(); return 42; }));
        Assert.True(job.IsRunning);
        Assert.False(job.TryStart(() => { Interlocked.Increment(ref calls); return 0; }));
        Assert.False(job.TryTake(out _));
        gate.Set();
        for (int i = 0; i < 200 && job.IsRunning; i++) await Task.Delay(10);
        Assert.True(job.TryTake(out int r));
        Assert.Equal(42, r);
        Assert.False(job.TryTake(out _));
        Assert.Equal(1, calls);
        Assert.True(job.TryStart(() => 1));   // free again
    }

    [Fact]
    public async Task BackgroundJob_survives_an_exception()
    {
        var job = new BackgroundJob<int>();
        job.TryStart(() => throw new IOException("boom"));
        for (int i = 0; i < 200 && job.IsRunning; i++) await Task.Delay(10);
        Assert.False(job.IsRunning);
        Assert.False(job.TryTake(out _));
    }
}

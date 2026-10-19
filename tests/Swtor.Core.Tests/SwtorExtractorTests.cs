using System.Net;
using System.Text;
using Swtor.Core.Myp;

namespace Swtor.Core.Tests;

public class SwtorExtractorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swtor_extract_" + Guid.NewGuid().ToString("N"));

    public SwtorExtractorTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static MypNameList Names(params string[] lines) => MypNameList.Parse(new StringReader(string.Join('\n', lines)));

    private string Tor(string file, params (string Name, string Text)[] files)
    {
        string path = Path.Combine(_dir, file);
        File.WriteAllBytes(path, MypTests.Build([.. files.Select(f =>
        {
            byte[] raw = Encoding.ASCII.GetBytes(f.Text);
            return (f.Name, raw, raw.Length, (ushort)0);
        })]));
        return path;
    }

    [Fact]
    public void NameList_hashes_each_path_and_ignores_noise()
    {
        var n = Names("/resources/art/dynamic/head/index.xml", "", "# comment", "no-slash.txt", "  /resources/a/b.gr2  ");
        Assert.Equal(2, n.Count);
        Assert.Equal("/resources/art/dynamic/head/index.xml", n.NameOf(0x58C216D26EAF2B8AUL));
        Assert.Equal("/resources/a/b.gr2", n.NameOf(MypHash.Compute("/resources/a/b.gr2")));
        Assert.Null(n.NameOf(1));
    }

    [Fact]
    public void Extract_writes_named_files_under_the_prefixes_only()
    {
        string tor = Tor("a.tor", ("/resources/world/areas/1/area.dat", "AREA"), ("/resources/art/x.gr2", "GR2"),
            ("/resources/sound/s.wav", "WAV"), ("/resources/world/areas/1/unknown.dat", "?"));
        var names = Names("/resources/world/areas/1/area.dat", "/resources/art/x.gr2", "/resources/sound/s.wav");
        string outRoot = Path.Combine(_dir, "out");

        SwtorExtractResult r = SwtorExtractor.Extract([tor], names, outRoot, ["world/", "/resources/art/"]);

        Assert.Equal(4, r.Entries);
        Assert.Equal(1, r.Unnamed);
        Assert.Equal(2, r.Written);
        Assert.Equal("AREA", File.ReadAllText(Path.Combine(outRoot, "world", "areas", "1", "area.dat")));
        Assert.Equal("GR2", File.ReadAllText(Path.Combine(outRoot, "art", "x.gr2")));
        Assert.False(File.Exists(Path.Combine(outRoot, "sound", "s.wav")));
        // Le dossier extrait est lisible par SwtorAssets.
        Assert.NotNull(new SwtorAssets(outRoot).Resolve("/resources/art/x.gr2"));
    }

    [Fact]
    public void Extract_second_run_skips_and_later_archive_wins()
    {
        var names = Names("/resources/art/x.gr2");
        string t1 = Tor("a_1.tor", ("/resources/art/x.gr2", "OLD"));
        string t2 = Tor("a_2.tor", ("/resources/art/x.gr2", "NEWER"));
        string outRoot = Path.Combine(_dir, "out");

        SwtorExtractResult r1 = SwtorExtractor.Extract([t1, t2], names, outRoot);
        Assert.Equal(1, r1.Written);
        Assert.Equal("NEWER", File.ReadAllText(Path.Combine(outRoot, "art", "x.gr2")));

        SwtorExtractResult r2 = SwtorExtractor.Extract([t1, t2], names, outRoot);
        Assert.Equal(0, r2.Written);
        Assert.Equal(1, r2.Skipped);
        Assert.Equal(1, SwtorExtractor.Extract([t1, t2], names, outRoot, overwrite: true).Written);
    }

    [Theory]
    [InlineData("/resources/../evil.txt")]
    [InlineData("/resources/a/../../evil.txt")]
    [InlineData("/resources/C:/evil.txt")]
    [InlineData("/resources/a\\b.txt")]
    [InlineData("/elsewhere/a.txt")]
    public void Extract_refuses_dangerous_names(string name)
    {
        string tor = Tor("a.tor", (name, "X"));
        string outRoot = Path.Combine(_dir, "out");
        SwtorExtractResult r = SwtorExtractor.Extract([tor], Names(name), outRoot);
        Assert.Equal(0, r.Written);
        Assert.False(File.Exists(Path.Combine(_dir, "evil.txt")));
    }

    [Fact]
    public void FindArchives_scans_game_root_assets_and_retailclient_and_skips_pts()
    {
        string root = Path.Combine(_dir, "game");
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "swtor", "retailclient"));
        File.WriteAllText(Path.Combine(root, "Assets", "swtor_main_art_1.tor"), "x");
        File.WriteAllText(Path.Combine(root, "Assets", "swtor_test_main_art_1.tor"), "x");
        File.WriteAllText(Path.Combine(root, "swtor", "retailclient", "main_gfx_1.tor"), "x");

        foreach (string start in new[] { root, Path.Combine(root, "Assets"), Path.Combine(root, "swtor", "retailclient") })
        {
            var names = SwtorGameLocator.FindArchives(start).Select(Path.GetFileName).ToList();
            Assert.Equal(["main_gfx_1.tor", "swtor_main_art_1.tor"], names);
        }
        Assert.Equal(["swtor_test_main_art_1.tor"], SwtorGameLocator.FindArchives(root, pts: true).Select(Path.GetFileName));
    }

    [Fact]
    public void Detect_uses_SWTOR_PATH_then_steam_libraries()
    {
        string game = Path.Combine(_dir, "lib", "steamapps", "common", "Star Wars - The Old Republic");
        Directory.CreateDirectory(Path.Combine(game, "Assets"));
        File.WriteAllText(Path.Combine(game, "Assets", "swtor_main_1.tor"), "x");
        string vdf = "\"libraryfolders\" { \"1\" { \"path\" \"" + Path.Combine(_dir, "lib").Replace(@"\", @"\\") + "\" } }";

        Assert.Equal(game, SwtorGameLocator.Detect(k => k == "SWTOR_PATH" ? game : null, _ => null));
        Assert.Equal(game, SwtorGameLocator.Detect(k => k == "ProgramFiles(x86)" ? Path.Combine(_dir, "pf") : null,
            p => p.EndsWith("libraryfolders.vdf") ? vdf : null));
        Assert.Null(SwtorGameLocator.Detect(k => k.StartsWith("Program") ? Path.Combine(_dir, "none") : null, _ => null));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(respond(r));
    }

    private static HttpResponseMessage Ok(string body, DateTimeOffset modified)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        resp.Content.Headers.LastModified = modified;
        return resp;
    }

    [Fact]
    public async Task Refresh_downloads_then_reuses_cache_then_survives_offline()
    {
        string cache = Path.Combine(_dir, "names.txt");
        DateTimeOffset stamp = new(2026, 10, 16, 6, 0, 0, TimeSpan.Zero);
        var h = new FakeHandler(r => Ok(r.Method == HttpMethod.Head ? "" : "/resources/a/b.gr2\n", stamp));
        using var http = new HttpClient(h);

        Assert.Equal(MypNameList.RefreshStatus.Downloaded, await MypNameList.RefreshAsync(cache, "https://x/list", client: http));
        Assert.Equal(1, MypNameList.Load(cache).Count);
        Assert.False(File.Exists(cache + ".part"));

        Assert.Equal(MypNameList.RefreshStatus.UpToDate, await MypNameList.RefreshAsync(cache, "https://x/list", client: http));
        Assert.Equal(MypNameList.RefreshStatus.Downloaded, await MypNameList.RefreshAsync(cache, "https://x/list", force: true, client: http));

        using var offline = new HttpClient(new FakeHandler(_ => throw new HttpRequestException("offline")));
        Assert.Equal(MypNameList.RefreshStatus.KeptCacheOffline, await MypNameList.RefreshAsync(cache, "https://x/list", client: offline));
        await Assert.ThrowsAsync<HttpRequestException>(() => MypNameList.RefreshAsync(Path.Combine(_dir, "none.txt"), "https://x/list", client: offline));
    }
}

public class SwtorGameDataTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "swtor_gamedata_" + Guid.NewGuid().ToString("N"));

    public SwtorGameDataTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static readonly string[] Files =
    [
        "/resources/systemgenerated/prototypes/1.node", "/resources/fr-fr/str/sys/worldmap.stb",
        "/resources/world/areas/42/area.dat", "/resources/world/areas/42/room_01.dat", "/resources/world/areas/42/map_x_r.dds",
        "/resources/world/areas/43/area.dat", "/resources/world/areas/43/room_01.dat",
        "/resources/art/dynamic/a.gr2", "/resources/sound/s.wav",
    ];

    private (SwtorGameData Data, string Root, string Tor) Make()
    {
        string tor = Path.Combine(_dir, "swtor_main_1.tor");
        File.WriteAllBytes(tor, MypTests.Build(Files.Take(4).Select(f => (f, Encoding.ASCII.GetBytes(f), f.Length, (ushort)0)).ToArray()));
        // Build() ne tient que 4 entrees par table : le reste va dans une seconde archive.
        string tor2 = Path.Combine(_dir, "swtor_main_2.tor");
        File.WriteAllBytes(tor2, MypTests.Build(Files.Skip(4).Take(4).Select(f => (f, Encoding.ASCII.GetBytes(f), f.Length, (ushort)0)).ToArray()));
        string tor3 = Path.Combine(_dir, "swtor_main_3.tor");
        File.WriteAllBytes(tor3, MypTests.Build(Files.Skip(8).Select(f => (f, Encoding.ASCII.GetBytes(f), f.Length, (ushort)0)).ToArray()));
        string names = Path.Combine(_dir, "names.txt");
        File.WriteAllLines(names, Files);
        string root = Path.Combine(_dir, "cache", "resources");
        var src = new SwtorGameSource([tor, tor2, tor3]);
        return (new SwtorGameData(src, root, names, offline: true), root, tor);
    }

    [Fact]
    public void EnsureBase_extracts_gom_strings_and_area_dat_only_then_is_a_noop()
    {
        var (data, root, _) = Make();
        using (data)
        {
            var log = new List<string>();
            data.EnsureBase(new Progress<string>(log.Add));
            Assert.True(File.Exists(Path.Combine(root, "systemgenerated", "prototypes", "1.node")));
            Assert.True(File.Exists(Path.Combine(root, "fr-fr", "str", "sys", "worldmap.stb")));
            Assert.True(File.Exists(Path.Combine(root, "world", "areas", "42", "area.dat")));
            Assert.True(File.Exists(Path.Combine(root, "world", "areas", "43", "area.dat")));
            Assert.False(File.Exists(Path.Combine(root, "world", "areas", "42", "room_01.dat")));
            Assert.False(File.Exists(Path.Combine(root, "art", "dynamic", "a.gr2")));
            Assert.True(File.Exists(Path.Combine(root, SwtorGameData.StampFileName)));
            // Deuxieme appel : rien a refaire.
            int before = log.Count;
            data.EnsureBase(new Progress<string>(log.Add));
            Assert.Equal(before, log.Count);
        }
    }

    [Fact]
    public void EnsureArea_extracts_the_rooms_without_map_images_and_only_that_area()
    {
        var (data, root, _) = Make();
        using (data)
        {
            data.EnsureArea("42");
            Assert.True(File.Exists(Path.Combine(root, "world", "areas", "42", "room_01.dat")));
            Assert.False(File.Exists(Path.Combine(root, "world", "areas", "42", "map_x_r.dds")));
            Assert.False(File.Exists(Path.Combine(root, "world", "areas", "43", "room_01.dat")));
            Assert.True(File.Exists(Path.Combine(root, "world", "areas", "42", ".extracted")));
            data.EnsureArea("..\evil"); // ignore
            Assert.False(Directory.Exists(Path.Combine(_dir, "evil")));
        }
    }

    [Fact]
    public void Assets_extract_a_missing_file_on_demand_from_the_game()
    {
        var (data, root, _) = Make();
        using (data)
        {
            Assert.False(File.Exists(Path.Combine(root, "art", "dynamic", "a.gr2")));
            byte[]? bytes = data.Assets.ReadBytes("/resources/ART/Dynamic/A.gr2"); // casse du jeu : hash en minuscules
            Assert.NotNull(bytes);
            Assert.Equal("/resources/art/dynamic/a.gr2", Encoding.ASCII.GetString(bytes!));
            Assert.True(File.Exists(Path.Combine(root, "art", "dynamic", "a.gr2")));
            Assert.Null(data.Assets.Resolve("art/dynamic/absent.gr2"));
            Assert.Null(data.Assets.Resolve("../outside.txt"));
        }
    }

    [Fact]
    public void Concurrent_reads_of_the_same_missing_file_never_fail()
    {
        var (data, root, _) = Make();
        using (data)
        {
            var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
            Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            {
                try { Assert.NotNull(data.Assets.ReadBytes("art/dynamic/a.gr2")); }
                catch (Exception e) { errors.Add(e); }
            });
            Assert.Empty(errors);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "art", "dynamic"), "*.part"));
        }
    }

    [Fact]
    public void A_game_update_re_extracts_the_base()
    {
        var (data, root, tor) = Make();
        using (data) data.EnsureBase();
        File.WriteAllText(Path.Combine(root, "world", "areas", "42", "area.dat"), "stale");
        File.SetLastWriteTimeUtc(tor, DateTime.UtcNow.AddDays(1)); // empreinte differente
        var src = new SwtorGameSource(SwtorGameLocator.FindArchives(_dir));
        using var again = new SwtorGameData(src, root, Path.Combine(_dir, "names.txt"), offline: true);
        again.EnsureBase();
        Assert.Equal("/resources/world/areas/42/area.dat", File.ReadAllText(Path.Combine(root, "world", "areas", "42", "area.dat")));
    }
}

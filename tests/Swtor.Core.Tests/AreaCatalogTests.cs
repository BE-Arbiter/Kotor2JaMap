using Swtor.Core.Areas;
using static Swtor.Core.Tests.SyntheticArea;

namespace Swtor.Core.Tests;

public class AreaInternalNameTests
{
    [Fact]
    public void Internal_name_is_read_from_the_last_block()
    {
        AreaDat area = AreaDatReader.Read(AreaWithInternalName("war_planworking", ["room_01"], StandardAssets()));
        Assert.Equal("war_planworking", area.InternalName);
    }

    [Fact]
    public void Internal_name_is_null_without_the_final_block()
    {
        Assert.Null(AreaDatReader.Read(Area(["r"], StandardAssets())).InternalName);
    }
}

public class AreaClassifierTests
{
    [Theory]
    [InlineData("stronghold_tatooine", "", "stronghold")]
    [InlineData("spc_ps_sith_warrior", "spc_space", "ship")]
    [InlineData("heta_ship", "", "ship")]
    [InlineData("spc_m01", "spc_space", "space")]
    [InlineData("spvp_002tdm", "", "space")]
    [InlineData("pvp_voidstar", "", "pvp")]
    [InlineData("imp_transition_flashpoint", "", "flashpoint")]
    [InlineData("mini_flashpoint_athiss", "", "flashpoint")]
    [InlineData("raid_denova", "", "operation")]
    [InlineData("uprising_works", "", "uprising")]
    [InlineData("imperial_fleet", "", "fleet")]
    [InlineData("ord_daily", "ord_ordmantell", "daily")]
    [InlineData("war_planworking", "spc_space", "story")]
    [InlineData("kgt_leehass", "", "story")]
    [InlineData("tat_main", "tat_tatooine", "planet")]
    [InlineData("lobby", "", "system")]
    [InlineData("regression_botworld1", "", "system")]
    [InlineData("", "", "other")]
    [InlineData("weirdname", "", "other")]
    public void Category_follows_naming_conventions(string internalName, string artFolder, string expected) =>
        Assert.Equal(expected, SwtorAreaCatalog.Classify(internalName, artFolder));

    [Fact]
    public void Prettify_makes_a_readable_fallback() => Assert.Equal("War planworking", SwtorAreaCatalog.Prettify("war_planworking"));
}

public class AreaNamePagesTests
{
    private static string? Text(long id) => id switch
    {
        1 => "Serre Noire<br>Soute",
        2 => "Serre Noire : hangar",
        3 => "Étoile de Brentaal : pont",
        4 => "La Serre Noire",
        _ => null,
    };

    [Fact]
    public void Page_named_after_the_internal_name_wins()
    {
        var pages = new[] { new GomMapPage("map_a", 3), new GomMapPage("map_x_main", 4), new GomMapPage("map_b", 1) };
        Assert.Equal("La Serre Noire", SwtorAreaNames.PickFromPages(pages, "x", Text));
    }

    [Fact]
    public void Single_page_is_used_and_breaks_are_cleaned()
    {
        Assert.Equal("Serre Noire - Soute", SwtorAreaNames.PickFromPages([new GomMapPage("map_a", 1)], "zzz", Text));
    }

    [Fact]
    public void Most_frequent_title_root_is_used_for_several_pages()
    {
        var pages = new[] { new GomMapPage("a", 1), new GomMapPage("b", 2), new GomMapPage("c", 3) };
        Assert.Equal("Serre Noire", SwtorAreaNames.PickFromPages(pages, null, Text));
    }

    [Fact]
    public void No_text_gives_null()
    {
        Assert.Null(SwtorAreaNames.PickFromPages([new GomMapPage("a", 99)], null, Text));
        Assert.Null(SwtorAreaNames.PickFromPages([], "x", Text));
    }
}

public class AreaCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly string _work = Path.Combine(Path.GetTempPath(), "swtor_work_" + Guid.NewGuid().ToString("N"));

    public AreaCatalogTests()
    {
        _root = WriteRoot(("4611686000000000001", "war_planworking", ["_everywhere_", "room_01"]), ("4611686000000000002", "tat_main", ["main"]));
    }

    public void Dispose()
    {
        Directory.Delete(_root, true);
        if (Directory.Exists(_work)) Directory.Delete(_work, true);
    }

    [Fact]
    public void Build_without_gom_falls_back_to_the_internal_name()
    {
        var catalog = SwtorAreaCatalog.Build(new SwtorAssets(_root));
        Assert.Equal(2, catalog.Entries.Count);
        var e = catalog.Entries.Single(x => x.Id == "4611686000000000001");
        Assert.Equal("war_planworking", e.InternalName);
        Assert.Equal("War planworking", e.DisplayName);
        Assert.Equal("internal", e.NameSource);
        Assert.Equal("story", e.Category);
        Assert.Equal(2, e.RoomCount);
        Assert.Equal(StandardAssets().Count, e.AssetCount);
        Assert.Equal(1, e.ModelAssetCount);
        Assert.True(e.RoomBytes > 0);
    }

    [Fact]
    public void Cache_is_written_then_reused_and_rebuild_forces_a_new_build()
    {
        var assets = new SwtorAssets(_root);
        string cache = SwtorAreaCatalog.CachePath(_work);
        var first = SwtorAreaCatalog.Load(assets, cache);
        Assert.False(first.FromCache);
        Assert.True(File.Exists(cache));

        var second = SwtorAreaCatalog.Load(assets, cache);
        Assert.True(second.FromCache);
        Assert.Equal(first.Entries, second.Entries);

        Assert.False(SwtorAreaCatalog.Load(assets, cache, rebuild: true).FromCache);
    }

    [Fact]
    public void Cache_is_ignored_when_the_number_of_areas_changes_or_the_file_is_corrupt()
    {
        var assets = new SwtorAssets(_root);
        string cache = SwtorAreaCatalog.CachePath(_work);
        SwtorAreaCatalog.Load(assets, cache);

        string extra = Path.Combine(_root, "world", "areas", "4611686000000000003");
        Directory.CreateDirectory(extra);
        File.Copy(Path.Combine(_root, "world", "areas", "4611686000000000002", "area.dat"), Path.Combine(extra, "area.dat"));
        var grown = SwtorAreaCatalog.Load(assets, cache);
        Assert.False(grown.FromCache);
        Assert.Equal(3, grown.Entries.Count);

        File.WriteAllText(cache, "{ not json");
        Assert.False(SwtorAreaCatalog.Load(assets, cache).FromCache);
    }

    [Fact]
    public void Find_accepts_an_id_or_a_case_insensitive_internal_name()
    {
        var catalog = SwtorAreaCatalog.Build(new SwtorAssets(_root));
        Assert.Equal("4611686000000000002", catalog.Find("4611686000000000002")!.Id);
        Assert.Equal("4611686000000000001", catalog.Find("WAR_planworking")!.Id);
        Assert.Null(catalog.Find("nope"));
    }

    [Fact]
    public void Filter_matches_name_id_and_category_ignoring_case_and_accents()
    {
        var catalog = new SwtorAreaCatalog(
        [
            new SwtorAreaEntry { Id = "1", InternalName = "dro_main", DisplayName = "Dromund Kaas", NameSource = "gom", Category = "planet", Planet = "dro" },
            new SwtorAreaEntry { Id = "2", InternalName = "bhm", DisplayName = "Étoile de Coruscant", NameSource = "gom", Category = "ship" },
        ], "fr-fr");
        Assert.Equal(["1"], catalog.Filter("KAAS").Select(e => e.Id));
        Assert.Equal(["2"], catalog.Filter("etoile").Select(e => e.Id));
        Assert.Equal(["2"], catalog.Filter("ship").Select(e => e.Id));
        Assert.Equal(2, catalog.Filter("").Count());
    }

    [Fact]
    public void CountModels_counts_gr2_instances_of_the_area()
    {
        // Chaque room synthetique a 3 instances de modele ; la zone a 2 rooms.
        Assert.Equal(6, SwtorAreaCatalog.CountModels(new SwtorAssets(_root), "4611686000000000001"));
        Assert.Equal(0, SwtorAreaCatalog.CountModels(new SwtorAssets(_root), "does_not_exist"));
    }
}

public class RealAreaCatalogTests
{
    [SkippableFact]
    public void Real_catalogue_names_most_areas_and_finds_the_known_ship()
    {
        string? root = TestEnv.SwtorAssetsDir;
        Skip.If(root is null || !Directory.Exists(Path.Combine(root, "systemgenerated", "buckets")), "Ressources SWTOR absentes");
        string work = Path.Combine(Path.GetTempPath(), "swtor_work_" + Guid.NewGuid().ToString("N"));
        try
        {
            var assets = new SwtorAssets(root);
            var catalog = SwtorAreaCatalog.Load(assets, SwtorAreaCatalog.CachePath(work));
            Assert.True(catalog.Entries.Count >= 200);
            Assert.All(catalog.Entries, e => Assert.False(string.IsNullOrWhiteSpace(e.DisplayName)));
            Assert.True(catalog.Entries.Count(e => e.NameSource.StartsWith("gom")) >= 0.8 * catalog.Entries.Count);
            Assert.All(catalog.Entries, e => Assert.NotEqual("", e.InternalName));

            var ship = catalog.Find("4611686045100172646");
            Assert.NotNull(ship);
            Assert.Equal("war_planworking", ship!.InternalName);
            Assert.Equal("story", ship.Category);
            Assert.Equal(ship.Id, catalog.Find("war_planworking")!.Id);
            Assert.Contains("Serre Noire", catalog.Entries.Single(e => e.InternalName == "imp_transition_flashpoint").DisplayName);

            Assert.True(SwtorAreaCatalog.Load(assets, SwtorAreaCatalog.CachePath(work)).FromCache);
        }
        finally { if (Directory.Exists(work)) Directory.Delete(work, true); }
    }
}

using System.Numerics;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Core.Models;
using Kotor.Compiler;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Jk.Pipeline.Tests;

/// <summary>Objets du niveau (tache 7) : departs, objets a ramasser, decors, mobiles de rotation, sons, volumes d'eau et de blessure.</summary>
public sealed partial class JkThingConverterTests : IDisposable
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";
    private const float K = JkWorldScale.MetresPerJkUnit * ExportOptions.DefaultScale; // 344 unites JA par unite JK

    private const int SpinStartOn = 1; // drapeau START_ON de func_rotating

    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (string d in _dirs) { try { Directory.Delete(d, true); } catch (IOException) { } }
    }

    // --- Fabrication de fichiers synthetiques ---

    private static byte[] Text(string s) => Encoding.Latin1.GetBytes(s);

    private static string F(float v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static byte[] Gob(params (string Path, byte[] Data)[] files)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("GOB "));
        w.Write(0x14);
        w.Write(0);
        var offsets = new List<(int, int)>();
        foreach (var f in files) { offsets.Add(((int)ms.Position, f.Data.Length)); w.Write(f.Data); }
        int index = (int)ms.Position;
        w.Write(files.Length);
        for (int i = 0; i < files.Length; i++)
        {
            w.Write(offsets[i].Item1); w.Write(offsets[i].Item2);
            byte[] name = new byte[128];
            Encoding.ASCII.GetBytes(files[i].Path).CopyTo(name, 0);
            w.Write(name);
        }
        w.Flush();
        byte[] bytes = ms.ToArray();
        BitConverter.GetBytes(index).CopyTo(bytes, 8);
        return bytes;
    }

    /// <summary>3DO a un noeud : un carre (x de 0 a 1, y de 0 a 1, z = 0), texture <c>fan.mat</c>.</summary>
    private const string Quad3do = """
        SECTION: HEADER
        3DO 2.1
        SECTION: MODELRESOURCE
        MATERIALS 1
        0: fan.mat
        SECTION: GEOMETRYDEF
        RADIUS 2.0
        INSERT OFFSET 0.0 0.0 0.0
        GEOSETS 1
        GEOSET 0
        MESHES 1
        MESH 0
        NAME blade
        RADIUS 2.0
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 4
        0: 0.0 0.0 0.0 0.0
        1: 1.0 0.0 0.0 0.0
        2: 1.0 1.0 0.0 0.0
        3: 0.0 1.0 0.0 0.0
        TEXTURE VERTICES 4
        0: 0.0 0.0
        1: 8.0 0.0
        2: 8.0 -8.0
        3: 0.0 -8.0
        VERTEX NORMALS
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        3: 0.0 0.0 1.0
        FACES 1
        0: 0 0x0000 4 3 1 0.0 4 0,0 1,1 2,2 3,3
        FACE NORMALS
        0: 0.0 0.0 1.0
        SECTION: HIERARCHYDEF
        HIERARCHY NODES 1
        0: 0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 blade
        """;

    /// <summary>3DO d'un cube plein de 1 unite JK de cote (6 faces carrees), pour la collision des decors.</summary>
    private const string Cube3do = """
        SECTION: HEADER
        3DO 2.1
        SECTION: MODELRESOURCE
        MATERIALS 1
        0: fan.mat
        SECTION: GEOMETRYDEF
        RADIUS 2.0
        INSERT OFFSET 0.0 0.0 0.0
        GEOSETS 1
        GEOSET 0
        MESHES 1
        MESH 0
        NAME cube
        RADIUS 2.0
        GEOMETRYMODE 4
        LIGHTINGMODE 3
        TEXTUREMODE 1
        VERTICES 8
        0: 0.0 0.0 0.0 0.0
        1: 1.0 0.0 0.0 0.0
        2: 1.0 1.0 0.0 0.0
        3: 0.0 1.0 0.0 0.0
        4: 0.0 0.0 1.0 0.0
        5: 1.0 0.0 1.0 0.0
        6: 1.0 1.0 1.0 0.0
        7: 0.0 1.0 1.0 0.0
        TEXTURE VERTICES 4
        0: 0.0 0.0
        1: 8.0 0.0
        2: 8.0 -8.0
        3: 0.0 -8.0
        VERTEX NORMALS
        0: 0.0 0.0 1.0
        1: 0.0 0.0 1.0
        2: 0.0 0.0 1.0
        3: 0.0 0.0 1.0
        4: 0.0 0.0 1.0
        5: 0.0 0.0 1.0
        6: 0.0 0.0 1.0
        7: 0.0 0.0 1.0
        FACES 6
        0: 0 0x0000 4 3 1 0.0 4 0,0 3,1 2,2 1,3
        1: 0 0x0000 4 3 1 0.0 4 4,0 5,1 6,2 7,3
        2: 0 0x0000 4 3 1 0.0 4 0,0 1,1 5,2 4,3
        3: 0 0x0000 4 3 1 0.0 4 3,0 7,1 6,2 2,3
        4: 0 0x0000 4 3 1 0.0 4 0,0 4,1 7,2 3,3
        5: 0 0x0000 4 3 1 0.0 4 1,0 2,1 6,2 5,3
        FACE NORMALS
        0: 0.0 0.0 -1.0
        1: 0.0 0.0 1.0
        2: 0.0 -1.0 0.0
        3: 0.0 1.0 0.0
        4: -1.0 0.0 0.0
        5: 1.0 0.0 0.0
        SECTION: HIERARCHYDEF
        HIERARCHY NODES 1
        0: 0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 cube
        """;

    /// <summary>Animation de deux images : le noeud <c>blade</c> glisse de 0,5 le long de X (pas une rotation pure).</summary>
    private const string SlideKey = """
        SECTION: HEADER
        FLAGS 0x0000
        TYPE 0xFFFF
        FRAMES 2
        FPS 15
        JOINTS 1
        SECTION: KEYFRAME NODES
        NODES 1
        NODE 0
        MESH NAME blade
        ENTRIES 2
        0: 0 0x0001 0.0 0.0 0.0 0.0 0.0 0.0
        0 0 0 0 0 0
        1: 1 0x0001 0.5 0.0 0.0 0.0 0.0 0.0
        0 0 0 0 0 0
        """;

    private const string SectorDamageCog = """
        symbols
            message startup
            message pulse
            sector  damageSector
            flex    damageInterval=1.0
            flex    damage=4.0
            thing   victim local
        end
        code
        startup:
            if(damageSector > -1) SetPulse(damageInterval);
            return;
        pulse:
            victim = FirstThingInSector(damageSector);
            while (victim != -1)
            {
                DamageThing(victim, damage, 0x1, victim);
                victim = NextThingInSector(victim);
            }
            return;
        end
        """;

    private const string TwoOneBeeCog = """
        symbols
            message startup
            thing twoonebee local
            keyframe idleKey=fanidle.key local
        end
        code
        startup:
            PlayKey(twoonebee, idleKey, 2, 0);
            return;
        end
        """;

    private const string BarrelCog = """
        symbols
            message created
            message damaged
            template barrel_exp=+xtank1_exp local
        end
        code
        created:
            return;
        damaged:
            CreateThing(barrel_exp, GetSenderRef());
            DestroyThing(GetSenderRef());
            return;
        end
        """;

    private static byte[] Wav(int channels, int bits, int frames)
    {
        int block = channels * bits / 8;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + frames * block); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(22050); w.Write(22050 * block); w.Write((short)block); w.Write((short)bits);
        w.Write("data"u8.ToArray()); w.Write(frames * block);
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < channels; c++)
            {
                int v = c == 0 ? 1000 : 3000; // gauche 1000, droite 3000 : moyenne 2000
                if (bits == 16) w.Write((short)v); else w.Write((byte)(128 + v / 100));
            }
        w.Flush();
        return ms.ToArray();
    }

    private sealed record Env(JkPreparedScene Prepared, JkFileSystem Fs, ExportOptions Opt, ImportLog Log) : IDisposable
    {
        public void Dispose() => Fs.Dispose();
    }

    private Env Make(SyntheticLevel level, JkLevelKind kind = JkLevelKind.SinglePlayer, params (string Path, byte[] Data)[] extra)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jkobjects_" + Guid.NewGuid().ToString("N"));
        _dirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        var files = new List<(string, byte[])>
        {
            (@"3do\fan.3do", Text(Quad3do)),
            (@"3do\cube.3do", Text(Cube3do)),
            (@"3do\key\fanidle.key", Text(SlideKey)),
            (@"mat\fan.mat", SyntheticLevel.TexMat(8, 8, 10)),
            (@"misc\cmp\test.cmp", SyntheticLevel.Cmp()),
        };
        foreach (var f in level.CogScripts) files.Add((@"cog\" + f.Key, Text(f.Value)));
        files.AddRange(extra);
        File.WriteAllBytes(Path.Combine(dir, "Episode", "JK1.GOB"), Gob([.. files]));
        var fs = JkFileSystem.ForEpisode(dir, "JK1.GOB");
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, log) with { Kind = kind };
        var opt = new ExportOptions("", Path.Combine(dir, "base"), dir, Game: KotorGame.JediKnight);
        return new Env(prepared, fs, opt, log);
    }

    private static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels) Run(Env e) =>
        JkObjects.Convert(e.Prepared, e.Fs, e.Opt, e.Log);

    private static List<IReadOnlyDictionary<string, string>> Of(ConvertedEntities c, string classname) =>
        [.. c.Entities.Where(x => x["classname"] == classname)];

    private const string PlayerLine = "walkplayer walkplayer";

    // --- Tests demandes par le plan ---

    [Fact]
    public void Actor_Skipped_Counted()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["stormtrooper _actor type=actor model3d=fan.3do puppet=st.pup"],
            ExtraThings = ["1: stormtrooper st 0.5 0.5 0.12 0 0 0 0", "2: stormtrooper st 0.6 0.5 0.12 0 0 0 0", "3: stormtrooper st 0.7 0.5 0.12 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.DoesNotContain(converted.Entities, x => x.GetValueOrDefault("model", "").Contains("fan"));
        var warn = Assert.Single(e.Log.Warnings, w => w.Code == "JK_ACTOR_SKIPPED");
        Assert.Contains("3", warn.Message);
        Assert.Empty(converted.Models);
    }

    [Fact]
    public void Solo_OnlyThePrimaryStart_IsEmitted_AndMultiplayerStartsAreLogged()
    {
        var level = new SyntheticLevel
        {
            // Le depart du solo n'a pas le bit 0x80000 ; les autres sont des departs multijoueur des niveaux solo (aleatoires dans SWGL : non places).
            ExtraThings = ["1: walkplayer walkplayer 1.5 0.5 0.12 0 0 0 1 thingflags=0x20080501"],
        };
        using Env e = Make(level, JkLevelKind.SinglePlayer);
        var (converted, _) = Run(e);

        var start = Assert.Single(Of(converted, "info_player_start"));
        Assert.Empty(Of(converted, "info_player_deathmatch"));
        Assert.Equal($"{F(0.5f * K)} {F(0.25f * K)} {F(0.12f * K)}", start["origin"]);
        Assert.Equal("180", start["angle"]); // lacet JK 90 (regard vers -X) = angle JA 180
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_START_MULTI_SKIPPED");
    }

    [Fact]
    public void Multiplayer_FirstStartIsPlayerStart()
    {
        var level = new SyntheticLevel
        {
            ExtraThings =
            [
                "1: walkplayer walkplayer 1.5 0.5 0.12 0 0 0 1 thingflags=0x20080501",
                "2: walkplayer walkplayer 0.2 0.5 0.12 0 -90 0 0 thingflags=0x20080501",
            ],
        };
        using Env e = Make(level, JkLevelKind.Multiplayer);
        var (converted, _) = Run(e);

        var starts = converted.Entities.Where(x => x["classname"] is "info_player_start" or "info_player_deathmatch").ToList();
        Assert.Equal(["info_player_start", "info_player_deathmatch", "info_player_deathmatch"], starts.Select(x => x["classname"]));
        Assert.Equal("0", starts[2]["angle"]); // lacet JK -90 (regard vers +X) = angle JA 0
        Assert.DoesNotContain(e.Log.Warnings, w => w.Code == "JK_START_MULTI_SKIPPED");
    }

    [Fact]
    public void Ctf_TeamSpawnsAndFlags()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates =
            [
                "ctf_redflag none type=item model3d=fan.3do collide=1 typeflags=0x8",
                "ctf_goldflag none type=item model3d=fan.3do collide=1 typeflags=0x8",
            ],
            ExtraThings =
            [
                "1: walkplayer walkplayer 1.6 0.5 0.12 0 0 0 1 thingflags=0x20080501",   // pres du drapeau rouge
                "2: walkplayer walkplayer 0.3 0.5 0.12 0 0 0 0 thingflags=0x20080501",   // pres du drapeau dore
                "3: ctf_redflag ctf_redflag 1.8 0.5 0.3 0 0 0 1 thingflags=0x400",
                "4: ctf_goldflag ctf_goldflag 0.2 0.5 0.3 0 180 0 0 thingflags=0x400",
            ],
        };
        using Env e = Make(level, JkLevelKind.Ctf);
        var (converted, _) = Run(e);

        Assert.Single(Of(converted, "info_player_start"));
        var red = Assert.Single(Of(converted, "team_CTF_redplayer"));
        var blue = Assert.Single(Of(converted, "team_CTF_blueplayer"));
        Assert.StartsWith($"{F(1.6f * K)} ", red["origin"]);
        Assert.StartsWith($"{F(0.3f * K)} ", blue["origin"]);
        Assert.Empty(Of(converted, "info_player_deathmatch"));

        var redFlag = Assert.Single(Of(converted, "team_CTF_redflag"));
        var blueFlag = Assert.Single(Of(converted, "team_CTF_blueflag"));
        Assert.StartsWith($"{F(1.8f * K)} ", redFlag["origin"]);
        Assert.StartsWith($"{F(0.2f * K)} ", blueFlag["origin"]);
        // Le drapeau reste visible dans SWGL (les entites team_CTF_* n'y existent pas) : son modele est pose en decor.
        Assert.Equal(2, Of(converted, "misc_model_static").Count);
    }

    [Fact]
    public void BryarPickup_MapsToWeaponBryarPistol()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["bryarpistol none type=item model3d=fan.3do collide=1 cog=pow_bryar.cog"],
            ExtraThings = ["1: bryarpistol bryarpistol 0.7 0.5 0.17 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        var pistol = Assert.Single(Of(converted, "weapon_bryar_pistol"));
        Assert.Equal($"{F(0.7f * K)} {F(0.5f * K)} {F(0.17f * K)}", pistol["origin"]);
        Assert.Empty(Of(converted, "misc_model_static")); // l'objet JA a son propre modele
        Assert.DoesNotContain(e.Log.Warnings, w => w.Code == "JK_ITEM_NO_EQUIVALENT");
    }

    [Fact]
    public void UnknownItem_PlacedAsDecor()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["keyred none type=item model3d=fan.3do collide=1"],
            ExtraThings = ["1: keyred keyred 0.7 0.5 0.17 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        var model = Assert.Single(Of(converted, "misc_model_static"));
        Assert.Equal("models/jk/fan.md3", model["model"]);
        Assert.Contains(converted.Models, m => m.Path == "models/jk/fan.md3");
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_ITEM_NO_EQUIVALENT" && w.Message.Contains("keyred"));
        Assert.DoesNotContain(converted.Entities, x => x["classname"].StartsWith("weapon_") || x["classname"].StartsWith("item_") || x["classname"].StartsWith("ammo_"));
        Assert.Contains("fan", converted.TextureData.Keys); // la texture du modele est exportee
        Assert.NotNull(converted.TextureData["fan"]);
        Assert.Contains(converted.ModelShaders, s => s.StartsWith("models/jk/fan\n"));
    }

    [Fact]
    public void SpinningFan_FuncRotatingWithModel2()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["fan0 none type=cog collide=3 move=physics angvel=(400.000000/0.000000/0.000000) model3d=fan.3do"],
            ExtraThings = ["1: fan0 fan0 1.5 0.5 0.3 0 90 0 1"],
        };
        using Env e = Make(level);
        var (converted, subModels) = Run(e);

        var fan = Assert.Single(Of(converted, "func_rotating"));
        Assert.Equal("*1", fan["model"]);
        Assert.Equal("models/jk/fan.md3", fan["model2"]);
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.3f * K)}", fan["origin"]);
        Assert.Equal("400", fan["speed"]);
        Assert.Equal("5", fan["spawnflags"]);          // START_ON (1) | X_AXIS (4) : le tangage JK tourne autour de l'axe X du modele
        Assert.Equal("0 90 0", fan["modelAngles"]);    // orientation de base, en angles JA
        Assert.Empty(Of(converted, "misc_model_static")); // pas de doublon fixe
        Assert.Contains(converted.Models, m => m.Path == "models/jk/fan.md3");

        // Le sous-modele est un volume sans effet : il ne doit pas bloquer le joueur.
        SubModelInput sub = Assert.Single(subModels);
        Assert.NotEqual(BspContents.Solid, sub.Contents & BspContents.Solid);
        Assert.Single(sub.Brushes);
    }

    [Fact]
    public void SpinningFan_NeverGetsFaceCollision()
    {
        // Un ventilateur est traversable : meme avec collide=3 (collision de face pour un decor ordinaire), il n'ajoute aucun triangle solide.
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["fan0 none type=cog collide=3 move=physics angvel=(400.000000/0.000000/0.000000) model3d=cube.3do"],
            ExtraThings = ["1: fan0 fan0 1.5 0.5 0.3 0 0 0 1"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Single(Of(converted, "func_rotating"));
        Assert.Empty(converted.ExtraSolid);
        Assert.DoesNotContain(e.Log.Warnings, w => w.Code == "JK_DECOR_COLLISION");
    }

    [Fact]
    public void SpinningFan_TooLargeToSpin_StaysVisibleWithoutCollision()
    {
        // Un modele de 100 unites JK est divise (modelscale) : un func_rotating ne sait pas l'agrandir, le ventilateur est donc pose sans tourner.
        // Il ne doit pas pour autant devenir un obstacle : visible (misc_model_static), sans collision de face.
        string big = Cube3do.Replace("1.0", "100.0");
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["fanbig none type=cog collide=3 move=physics angvel=(0.000000/360.000000/0.000000) model3d=bigcube.3do"],
            ExtraThings = ["1: fanbig fanbig 1.5 0.5 0.3 0 0 0 1"],
        };
        using Env e = Make(level, JkLevelKind.SinglePlayer, (@"3do\bigcube.3do", Text(big)));
        var (converted, _) = Run(e);

        var model = Assert.Single(Of(converted, "misc_model_static"));
        Assert.True(int.Parse(model["modelscale"]) > 1);
        Assert.Empty(Of(converted, "func_rotating"));
        Assert.Empty(converted.ExtraSolid);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_ROTATION_APPROX");
    }

    [Fact]
    public void SectorSound_TargetSpeaker()
    {
        var level = new SyntheticLevel { SectorSounds = new Dictionary<int, string> { [1] = "ambient.wav 0.8" } };
        using Env e = Make(level, JkLevelKind.SinglePlayer, (@"sound\ambient.wav", Wav(1, 16, 100)));
        var (converted, _) = Run(e);

        var speaker = Assert.Single(Of(converted, "target_speaker"));
        Assert.Equal("sound/jk/ambient.wav", speaker["noise"]);
        Assert.Equal("1", speaker["spawnflags"]); // en boucle
        // Centre du secteur 1 (x de 1 a 2, y de 0 a 1, z de 0 a 0,5).
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.25f * K)}", speaker["origin"]);
        SoundAsset wav = Assert.Single(converted.Sounds);
        Assert.Equal("sound/jk/ambient.wav", wav.Path);
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SECTOR_SOUND");
    }

    [Fact]
    public void SectorSound_StereoWav_IsDownmixedToMono()
    {
        var level = new SyntheticLevel { SectorSounds = new Dictionary<int, string> { [0] = "wind.wav 1.0" } };
        using Env e = Make(level, JkLevelKind.SinglePlayer, (@"sound\wind.wav", Wav(2, 16, 50)));
        var (converted, _) = Run(e);

        byte[] data = Assert.Single(converted.Sounds).Data;
        Assert.Equal(1, BitConverter.ToInt16(data, 22));            // un canal : JA refuse les wav stereo
        Assert.Equal(50 * 2, BitConverter.ToInt32(data, 40));        // 50 echantillons de 16 bits
        Assert.Equal(2000, BitConverter.ToInt16(data, 44));          // moyenne de 1000 et 3000
    }

    [Fact]
    public void SectorSound_MissingFile_IsLoggedAndNoSpeaker()
    {
        var level = new SyntheticLevel { SectorSounds = new Dictionary<int, string> { [0] = "gone.wav 1.0" } };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Empty(Of(converted, "target_speaker"));
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SOUND_MISSING" && w.Message.Contains("gone.wav"));
    }

    [Fact]
    public void SectorSounds_NeighbourSectorsWithTheSameSound_ShareASpeaker()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            SectorSounds = new Dictionary<int, string> { [0] = "a.wav 1.0", [1] = "a.wav 1.0", [2] = "b.wav 1.0" },
        };
        using Env e = Make(level, JkLevelKind.SinglePlayer, (@"sound\a.wav", Wav(1, 16, 10)), (@"sound\b.wav", Wav(1, 16, 10)));
        var (converted, _) = Run(e);

        Assert.Equal(["sound/jk/a.wav", "sound/jk/b.wav"], Of(converted, "target_speaker").Select(x => x["noise"]).Order());
        Assert.Equal(2, converted.Sounds.Count);
    }

    [Fact]
    public void NullObjects_KotorImportUnchanged()
    {
        // Sans le crochet, l'etape Importer fait exactement ce qu'elle faisait : objets KOTOR lus dans le dossier du jeu (ici aucun dossier).
        using Env e = Make(new SyntheticLevel());
        var kotor = new ExportOptions("", e.Opt.JaBaseDir, e.Opt.WorkDir, Game: KotorGame.Kotor1);

        var plainLog = new ImportLog();
        ImportStepResult plain = PipelineSteps.Import(e.Prepared.Scene, "nullobj", kotor, plainLog);
        var nullLog = new ImportLog();
        ImportStepResult withNull = PipelineSteps.Import(e.Prepared.Scene, "nullobj", kotor, nullLog, objects: null);

        Assert.Equal(plain.Plan.Input.Entities.Select(Flatten), withNull.Plan.Input.Entities.Select(Flatten));
        Assert.Equal(plain.Plan.Input.SubModels.Count, withNull.Plan.Input.SubModels.Count);
        Assert.Equal(plain.Plan.Shaders.Count, withNull.Plan.Shaders.Count);
        Assert.Equal(plainLog.Warnings, nullLog.Warnings);
        Assert.Contains(plainLog.Warnings, w => w.Code == "PLACEABLES_NO_GAME_DIR"); // le chemin KOTOR a bien tourne

        // Avec un crochet, il remplace la conversion des objets KOTOR.
        var hookLog = new ImportLog();
        var hooked = PipelineSteps.Import(e.Prepared.Scene, "nullobj", kotor, hookLog, objects: (scene, opt, log) =>
            (new ConvertedEntities([new Dictionary<string, string> { ["classname"] = "info_player_start", ["origin"] = "1 2 3" }], [], []),
                [new SubModelInput([[new BspPlane(Vector3.UnitX, 1)]], "x") { Contents = BspContents.Water }]));
        Assert.Contains(hooked.Plan.Input.Entities, x => x["classname"] == "info_player_start" && x["origin"] == "1 2 3");
        Assert.Equal(BspContents.Water, Assert.Single(hooked.Plan.Input.SubModels).Contents);
        Assert.DoesNotContain(hookLog.Warnings, w => w.Code == "PLACEABLES_NO_GAME_DIR");
    }

    private static string Flatten(IReadOnlyDictionary<string, string> d) => string.Join(";", d.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key}={k.Value}"));

    // --- Table des objets a ramasser ---

    [Theory]
    [InlineData("bryarpistol", "weapon_bryar_pistol")]
    [InlineData("greedopistol", "weapon_bryar_pistol")]
    [InlineData("strifle", "weapon_blaster")]
    [InlineData("detonator", "weapon_thermal")]
    [InlineData("singledetonator", "ammo_thermal")]
    [InlineData("crossbow", "weapon_bowcaster")]
    [InlineData("repeatergun", "weapon_repeater")]
    [InlineData("railgun", "weapon_rocket_launcher")]
    [InlineData("m5railgun", "weapon_rocket_launcher")]
    [InlineData("seqcharge", "weapon_trip_mine")]
    [InlineData("concrifle", "weapon_concussion_rifle")]
    [InlineData("lightsaber", "weapon_saber")]
    [InlineData("energycell", "ammo_blaster")]
    [InlineData("powercell", "ammo_powercell")]
    [InlineData("railcharges", "ammo_rockets")]
    [InlineData("battery", "item_battery")]
    [InlineData("healthpack", "item_medpak_instant")]
    [InlineData("bactatank", "item_medpak_instant")]
    [InlineData("shieldrecharge", "item_shield_sm_instant")]
    [InlineData("supershield", "item_shield_lrg_instant")]
    [InlineData("manaboost", "ammo_force")]
    [InlineData("BryarPistol", "weapon_bryar_pistol")] // insensible a la casse
    public void Pickups_Map(string template, string expected) => Assert.Equal(expected, JkPickups.Map(template));

    [Theory]
    [InlineData("keyred")]
    [InlineData("goggles")]
    [InlineData("smbp")]
    [InlineData("no_such_template")]
    [InlineData("")]
    public void Pickups_WithoutEquivalent_AreNull(string template) => Assert.Null(JkPickups.Map(template));

    // --- Decors ---

    [Fact]
    public void Decor_MiscModelStatic_KeepsPositionAndRotationOfTheJkThing()
    {
        // Lacet 30, tangage 20, roulis 10 (JK). Le MD3 reste dans le repere JK : l'entite doit porter la meme rotation en angles JA.
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["crate none type=cog model3d=fan.3do collide=0"],
            ExtraThings = ["1: crate crate 0.7 0.5 0.17 20 30 10 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        var model = Assert.Single(Of(converted, "misc_model_static"));
        var angles = model["angles"].Split(' ').Select(s => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(3, angles.Length);
        // Rotation JK d'un vecteur (repere du modele) = rotation JA (AnglesToAxis) du meme vecteur.
        foreach (Vector3 v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, -0.8f, 0.5f) })
            AssertSameDirection(Vector3.Transform(v, ThreeDoModel.Rotation(new Vector3(20, 30, 10))), JaAxis(angles[0], angles[1], angles[2], v));
        Assert.Equal($"{F(0.7f * K)} {F(0.5f * K)} {F(0.17f * K)}", model["origin"]);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 90, 0)]
    [InlineData(0, -120, 180)]
    [InlineData(30, 45, -60)]
    [InlineData(-70, 170, 12)]
    [InlineData(0, 90, 180)]
    public void JkAngles_ToJa_GivesTheSameRotation(float pitch, float yaw, float roll)
    {
        Vector3 ja = JkAngles.ToJa(new Vector3(pitch, yaw, roll));
        foreach (Vector3 v in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, new Vector3(0.3f, -0.8f, 0.5f) })
            AssertSameDirection(Vector3.Transform(v, ThreeDoModel.Rotation(new Vector3(pitch, yaw, roll))), JaAxis(ja.X, ja.Y, ja.Z, v));
    }

    [Fact]
    public void JkAngles_PlainYaw_IsTheSameYaw_AndStartAngleAddsNinety()
    {
        Assert.Equal(new Vector3(0, 33, 0), JkAngles.ToJa(new Vector3(0, 33, 0)));
        Assert.Equal(123f, JkAngles.StartAngle(33f), 3);
        Assert.Equal(270f, JkAngles.StartAngle(180f), 3);
        Assert.Equal(0f, JkAngles.StartAngle(-90f), 3);
    }

    /// <summary>Axes de AnglesToAxis (q_math.c) : image du vecteur local <c>v</c> (x avant, y gauche, z haut) pour les angles JA.</summary>
    private static Vector3 JaAxis(float pitch, float yaw, float roll, Vector3 v)
    {
        double d = Math.PI / 180;
        double sy = Math.Sin(yaw * d), cy = Math.Cos(yaw * d), sp = Math.Sin(pitch * d), cp = Math.Cos(pitch * d), sr = Math.Sin(roll * d), cr = Math.Cos(roll * d);
        var forward = new Vector3((float)(cp * cy), (float)(cp * sy), (float)-sp);
        var right = new Vector3((float)(-1 * sr * sp * cy + -1 * cr * -sy), (float)(-1 * sr * sp * sy + -1 * cr * cy), (float)(-1 * sr * cp));
        var up = new Vector3((float)(cr * sp * cy + -sr * -sy), (float)(cr * sp * sy + -sr * cy), (float)(cr * cp));
        return forward * v.X + -right * v.Y + up * v.Z;
    }

    private static void AssertSameDirection(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 2); Assert.Equal(expected.Y, actual.Y, 2); Assert.Equal(expected.Z, actual.Z, 2);
    }

    [Fact]
    public void AnimatedDecor_UsesBreakableModelWithAutoAnimate()
    {
        // Le COG attache au gabarit joue un .key : modele a plusieurs images, entite JA qui boucle l'animation.
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["twoonebee none type=cog model3d=fan.3do collide=1 cog=00_twoonebee.cog"],
            ExtraThings = ["1: twoonebee twoonebee 0.7 0.5 0.17 0 90 0 0"],
            CogScripts = new Dictionary<string, string> { ["00_twoonebee.cog"] = TwoOneBeeCog },
        };
        using Env e = Make(level);
        var (converted, subModels) = Run(e);

        var anim = Assert.Single(Of(converted, "misc_model_breakable"));
        Assert.Equal("models/jk/fan_fanidle.md3", anim["model"]);
        Assert.Equal("2", anim["spawnflags"]); // AUTOANIMATE, ni SOLID ni vie : le joueur passe a travers
        Assert.Equal("0 90 0", anim["angles"]);
        Assert.Empty(subModels);
        Assert.Empty(Of(converted, "misc_model_static"));
        Assert.Equal(2, BitConverter.ToInt32(converted.Models.Single(m => m.Path == "models/jk/fan_fanidle.md3").Data, 76)); // deux images
    }

    [Fact]
    public void ExplosiveBarrel_PlacedAsStaticDecor_WithoutCollision()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["xtank1a none type=cog model3d=cube.3do collide=3 cog=xtank1.cog"],
            ExtraThings = ["1: xtank1a xtank1a 0.7 0.5 0.17 0 0 0 0"],
            CogScripts = new Dictionary<string, string> { ["xtank1.cog"] = BarrelCog },
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Single(Of(converted, "misc_model_static"));
        Assert.Empty(converted.ExtraSolid); // jamais de collision : on ne peut pas la detruire, elle pourrait bloquer le passage
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_EXPLOSIVE_STATIC");
    }

    [Fact]
    public void SolidDecor_GetsFaceCollisionFromItsModel()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["crate none type=cog model3d=cube.3do collide=3"],
            ExtraThings = ["1: crate crate 0.4 0.3 0.1 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Equal(12, converted.ExtraSolid.Count); // 6 faces carrees = 12 triangles
        float lo = 0.4f * K, hi = lo + K;
        foreach (var (a, b, c) in converted.ExtraSolid)
            foreach (Vector3 p in new[] { a, b, c })
            {
                Assert.InRange(p.X, lo - 0.01f, hi + 0.01f);
                Assert.InRange(p.Y, 0.3f * K - 0.01f, 0.3f * K + K + 0.01f);
                Assert.InRange(p.Z, 0.1f * K - 0.01f, 0.1f * K + K + 0.01f);
            }
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_DECOR_COLLISION");
    }

    [Fact]
    public void DecorWithSphereCollision_HasNoCollision()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["bottle none type=cog model3d=cube.3do collide=1"],
            ExtraThings = ["1: bottle bottle 0.4 0.3 0.1 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        Assert.Empty(converted.ExtraSolid);
        Assert.Single(Of(converted, "misc_model_static"));
    }

    [Fact]
    public void ThingLight_BecomesLightEntity()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["ceilinglight none type=cog model3d=fan.3do light=0.5 thingflags=0x9"],
            ExtraThings = ["1: ceilinglight ceilinglight 0.7 0.5 0.4 0 0 0 0"],
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        var light = Assert.Single(Of(converted, "light"));
        Assert.Equal($"{F(0.7f * K)} {F(0.5f * K)} {F(0.4f * K)}", light["origin"]);
        Assert.True(float.Parse(light["light"], System.Globalization.CultureInfo.InvariantCulture) > 0);
        Assert.Single(Of(converted, "misc_model_static")); // le luminaire reste visible
    }

    [Fact]
    public void CogCreatedThings_AreLoggedAsRuntime()
    {
        var level = new SyntheticLevel
        {
            Cogs = ["spawner.cog"],
            CogScripts = new Dictionary<string, string>
            {
                ["spawner.cog"] = "symbols\n message startup\n template t=+crate local\nend\ncode\nstartup:\n CreateThing(t, 0);\n return;\nend\n",
            },
        };
        using Env e = Make(level);
        Run(e);

        var warn = Assert.Single(e.Log.Warnings, w => w.Code == "JK_RUNTIME_THING");
        Assert.Contains("spawner.cog", warn.Message);
    }

    [Fact]
    public void ParticleAndSpriteThings_AreLoggedNotSilentlyDropped()
    {
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["sparks none type=particle particle=spark.par", "flare none type=cog sprite=flare.spr"],
            ExtraThings = ["1: sparks sparks 0.5 0.5 0.3 0 0 0 0", "2: flare flare 0.6 0.5 0.3 0 0 0 0"],
        };
        using Env e = Make(level);
        Run(e);

        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_PARTICLE_SKIPPED");
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_SPRITE_SKIPPED");
    }

    [Fact]
    public void ModelScale_IsCopiedFromTheMd3()
    {
        // Un modele de 100 unites JK depasse largement la limite MD3 : il est divise et l'entite porte modelscale.
        string big = Quad3do.Replace("1: 1.0 0.0 0.0 0.0", "1: 100.0 0.0 0.0 0.0").Replace("2: 1.0 1.0 0.0 0.0", "2: 100.0 1.0 0.0 0.0");
        var level = new SyntheticLevel
        {
            ExtraTemplates = ["girder none type=cog model3d=big.3do collide=0"],
            ExtraThings = ["1: girder girder 0.5 0.5 0.3 0 0 0 0"],
        };
        using Env e = Make(level, JkLevelKind.SinglePlayer, (@"3do\big.3do", Text(big)));
        var (converted, _) = Run(e);

        var model = Assert.Single(Of(converted, "misc_model_static"));
        Assert.True(int.Parse(model["modelscale"]) > 1);
    }

    // --- Volumes ---

    [Fact]
    public void WaterSector_BecomesNonSolidWaterSubModel()
    {
        var level = new SyntheticLevel
        {
            Sectors = 2,
            SectorFlags = new Dictionary<int, uint> { [0] = 0x2 },
            SectorTints = new Dictionary<int, string> { [0] = "0.4 0.8 1" },
        };
        using Env e = Make(level);
        var (converted, subModels) = Run(e);

        SubModelInput water = Assert.Single(subModels);
        Assert.Equal(BspContents.Water, water.Contents);
        Assert.Equal(JkSurfaceEffects.WaterVolumeShaderName(new Vector3(0.4f, 0.8f, 1f)), water.Shader);
        var bounds = BrushBounds.FromPlanes(Assert.Single(water.Brushes))!.Value;
        Assert.Equal(0f, bounds.Min.X, 1); Assert.Equal(K, bounds.Max.X, 1);
        Assert.Equal(0.5f * K, bounds.Max.Z, 1);

        var ent = Assert.Single(Of(converted, "func_static"));
        Assert.Equal("*1", ent["model"]);
        Assert.Equal("0 0 0", ent["origin"]);
        Assert.Contains(converted.ModelShaders, s => s.StartsWith(water.Shader + "\n") && s.Contains("surfaceparm water"));
        Assert.Contains(e.Log.Warnings, w => w.Code == "JK_WATER_BRUSH");
    }

    [Fact]
    public void HurtSector_BecomesTriggerHurt_NeverSolid()
    {
        var level = new SyntheticLevel
        {
            Sectors = 2,
            Cogs = ["00_sectordamage.cog 1 1.0 4.0"],
            CogScripts = new Dictionary<string, string> { ["00_sectordamage.cog"] = SectorDamageCog },
        };
        using Env e = Make(level);
        var (converted, subModels) = Run(e);

        SubModelInput trigger = Assert.Single(subModels);
        Assert.Equal(BspContents.Trigger, trigger.Contents); // contact d'entite seulement : ni solide, ni eau
        Assert.Equal(0, trigger.Contents & BspContents.Solid);
        var hurt = Assert.Single(Of(converted, "trigger_hurt"));
        Assert.Equal("*1", hurt["model"]);
        Assert.Equal("4", hurt["dmg"]);
        Assert.Equal("1", hurt["wait"]);
        Assert.Equal("0 0 0", hurt["origin"]);
    }

    [Fact]
    public void KillSector_BecomesLethalTriggerHurt()
    {
        var level = new SyntheticLevel
        {
            Sectors = 2,
            Cogs = ["00_sectordamage.cog 0 0.25 300.0"],
            CogScripts = new Dictionary<string, string> { ["00_sectordamage.cog"] = SectorDamageCog },
        };
        using Env e = Make(level);
        var (converted, _) = Run(e);

        var kill = Assert.Single(Of(converted, "trigger_hurt"));
        Assert.True(int.Parse(kill["dmg"]) >= 1000);
        Assert.Equal("8", kill["spawnflags"]); // NO_PROTECTION : ni bouclier ni armure n'arretent la mort
    }

    [Fact]
    public void ClosedConvexSector_GivesABrushFromItsOwnPlanes()
    {
        // Cube ferme de 1 unite JK de cote, en 6 surfaces : le brush est fait des 6 plans du secteur (pas de la boite englobante).
        JklLevel level = JklReaderForCube();
        var planes = JkVolumeBrushes.SectorPlanes(level, 0, K);
        Assert.NotNull(planes);
        Assert.Equal(6, planes!.Count);
        var b = BrushBounds.FromPlanes(planes)!.Value;
        Assert.Equal(0f, b.Min.X, 1); Assert.Equal(K, b.Max.X, 1);
        Assert.Equal(0f, b.Min.Z, 1); Assert.Equal(K, b.Max.Z, 1);
        // Les normales sortent du secteur.
        foreach (BspPlane p in planes) Assert.True(Vector3.Dot(p.Normal, new Vector3(0.5f, 0.5f, 0.5f) * K) < p.Dist);
    }

    private static JklLevel JklReaderForCube()
    {
        var sb = new StringBuilder();
        sb.AppendLine("SECTION: HEADER\nVersion 1\nWorld Gravity 4.0");
        sb.AppendLine("SECTION: GEORESOURCE\nWorld Colormaps 1\n0: test.cmp\nWorld vertices 8");
        string[] v = ["0 0 0", "1 0 0", "1 1 0", "0 1 0", "0 0 1", "1 0 1", "1 1 1", "0 1 1"];
        for (int i = 0; i < 8; i++) sb.AppendLine($"{i}: {v[i]}");
        sb.AppendLine("World texture vertices 1\n0: 0 0\nWorld adjoins 0");
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [3, 7, 6, 2], [0, 4, 7, 3], [1, 2, 6, 5]];
        string[] normals = ["0 0 1", "0 0 -1", "0 1 0", "0 -1 0", "1 0 0", "-1 0 0"]; // vers l'interieur, comme JK
        sb.AppendLine("World surfaces 6");
        for (int i = 0; i < 6; i++)
            sb.AppendLine($"{i}: -1 0x4 0x0 4 3 1 -1 0.0 4 " + string.Join(' ', faces[i].Select(x => $"{x},0")) + " 0.5 0.5 0.5 0.5");
        sb.AppendLine("#--- Surface normals ---");
        for (int i = 0; i < 6; i++) sb.AppendLine($"{i}: {normals[i]}");
        sb.AppendLine("SECTION: SECTORS\nWorld sectors 1\nSECTOR 0\nFLAGS 0x2\nAMBIENT LIGHT 0.1\nEXTRA LIGHT 0.0\nCOLORMAP 0\nTINT 0 0 0\nSURFACES 0 6");
        return Jk.Core.Jkl.JklReader.Read(sb.ToString());
    }

    // --- Chaine complete : BSP ---

    [Fact]
    public void FullChain_WaterTriggerAndFan_CompileToTheBsp()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            SectorFlags = new Dictionary<int, uint> { [0] = 0x2 },
            Cogs = ["00_sectordamage.cog 1 1.0 4.0"],
            CogScripts = new Dictionary<string, string> { ["00_sectordamage.cog"] = SectorDamageCog },
            ExtraTemplates = ["fan0 none type=cog collide=3 move=physics angvel=(0.0/90.0/0.0) model3d=fan.3do"],
            ExtraThings = ["1: fan0 fan0 2.5 0.5 0.3 0 0 0 2"],
        };
        using Env e = Make(level);
        Directory.CreateDirectory(e.Opt.JaBaseDir);
        var log = new ImportLog();
        ImportStepResult imported = PipelineSteps.Import(e.Prepared.Scene, "jkchain", e.Opt, log, JkObjects.ForPipeline(e.Prepared, e.Fs));
        ExportStepResult exported = PipelineSteps.Export(imported, e.Opt, log);

        BspFile bsp = exported.Bsp;
        Assert.Equal(1 + 3, bsp.Models.Count); // monde + eau + blessure + ventilateur
        string waterShader = JkSurfaceEffects.WaterVolumeShaderName(JkSurfaceEffects.DefaultWaterColor);
        int ContentsOf(string shader) => bsp.ShaderContents[bsp.Shaders.ToList().IndexOf(shader)];
        Assert.Equal(BspContents.Water, ContentsOf(waterShader));
        Assert.Equal(BspContents.Trigger, ContentsOf("textures/jk/trigger_hurt"));
        Assert.Equal(BspContents.None, ContentsOf("textures/jk/mover_nonsolid"));
        Assert.Contains("\"classname\" \"func_rotating\"", bsp.Entities);
        Assert.Contains("\"classname\" \"trigger_hurt\"", bsp.Entities);
        string shaderFile = File.ReadAllText(Path.Combine(exported.StagingDir, "shaders", "jkchain.shader"));
        Assert.Contains(waterShader, shaderFile);
        Assert.True(File.Exists(Path.Combine(exported.StagingDir, "models", "jk", "fan.md3")));
    }

    // --- Donnees reelles ---

    [SkippableFact]
    public void RealLevels_AllBuildTheirObjects()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        var summary = new StringBuilder();
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, subModels) = JkObjects.Convert(prepared, fs, opt, log);

            Assert.Single(converted.Entities, x => x["classname"] == "info_player_start");
            Assert.DoesNotContain(log.Warnings, w => w.Code is "JK_MODEL_MISSING" or "JK_MODEL_UNREADABLE" or "JK_MODEL_MD3_FAILED");
            Assert.DoesNotContain(log.Warnings, w => w.Code == "JK_THING_SKIPPED");
            Assert.True(converted.Entities.Count(x => x["classname"] == "misc_model_static") <= PlaceableConverter.MaxMiscModels);
            if (level.Kind == JkLevelKind.Ctf)
            {
                Assert.Single(converted.Entities, x => x["classname"] == "team_CTF_redflag");
                Assert.Single(converted.Entities, x => x["classname"] == "team_CTF_blueflag");
            }
            summary.AppendLine($"{level.Name}: {converted.Entities.Count} entities, {converted.Models.Count} md3, {subModels.Count} sub-models, {converted.Sounds.Count} sounds");
        }
        Assert.NotEmpty(summary.ToString());
    }

    [SkippableFact]
    public void RealFans_AreSpinningTraversableAndVisible()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        int fans = 0;
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, subModels) = JkObjects.Convert(prepared, fs, opt, log);

            foreach (var fan in converted.Entities.Where(x => x["classname"] == "func_rotating" && x["model2"].Contains("/fan", StringComparison.Ordinal)))
            {
                fans++;
                // Visible et anime : modele + rotation en marche. Traversable : sous-modele sans contenu, aucun triangle solide dans sa boite.
                Assert.True((int.Parse(fan["spawnflags"]) & SpinStartOn) != 0);
                SubModelInput sub = subModels[int.Parse(fan["model"][1..]) - 1];
                Assert.Equal(BspContents.None, sub.Contents);
                var (lo, hi) = BrushBounds.FromPlanes(sub.Brushes[0])!.Value;
                string[] o = fan["origin"].Split(' ');
                var origin = new Vector3(float.Parse(o[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(o[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(o[2], System.Globalization.CultureInfo.InvariantCulture));
                Assert.DoesNotContain(converted.ExtraSolid, t => new[] { t.A, t.B, t.C }.Any(p => p.X >= origin.X + lo.X && p.X <= origin.X + hi.X
                    && p.Y >= origin.Y + lo.Y && p.Y <= origin.Y + hi.Y && p.Z >= origin.Z + lo.Z && p.Z <= origin.Z + hi.Z));
            }
            // Aucun ventilateur ne doit rester un decor fixe : tous les modeles « fan* » sont des func_rotating.
            Assert.DoesNotContain(converted.Entities, x => x["classname"] == "misc_model_static" && x["model"].Contains("/fan", StringComparison.Ordinal));
        }
        Assert.Equal(13, fans); // 01narshadda 4, 10cargo 1, 16a et 16b 2 chacun, m5 1, c3 3
    }
}

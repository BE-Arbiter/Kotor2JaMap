using System.Globalization;
using System.Numerics;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Pipeline.Movers;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Jk.Pipeline.Tests;

/// <summary>
/// Sons des mobiles (portes, ascenseurs). Dans JK ils viennent de la classe de sons du gabarit (<c>soundclass=med_door.snd</c> : <c>startmove</c>, <c>stopmove</c>, <c>moving</c> en boucle).
/// JA n'a pas de cle de son sur ses portes (seulement <c>soundSet</c>, un nom de groupe defini dans <c>sound/sound.txt</c> du jeu, qui ne contient pas les sons de JK, et un nom inconnu fait planter le chargement) :
/// repli par <c>target_speaker</c> declenches par les cles <c>target</c> (au depart), <c>opentarget</c> et <c>closetarget</c> (a l'arrivee) de la porte (<c>g_mover.cpp</c>).
/// </summary>
public sealed class MoverSoundTests : IDisposable
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";
    private const float K = JkWorldScale.MetresPerJkUnit * ExportOptions.DefaultScale; // 344 unites JA par unite JK
    private const float Scale = ExportOptions.DefaultScale;

    private readonly List<string> _dirs = [];

    public void Dispose()
    {
        foreach (string d in _dirs) { try { Directory.Delete(d, true); } catch (IOException) { } }
    }

    // ---- aides ----

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Fr(float x, float y, float z) => $"frame=({F(x)}/{F(y)}/{F(z)}:0/0/0)";

    private static JkMd3 Md3(string name = "door") =>
        new(new Md3Asset($"models/jk/{name}.md3", [1, 2, 3], ["tex"]),
            new Vector3(-0.2f, -0.1f, 0) * JkWorldScale.MetresPerJkUnit, new Vector3(0.2f, 0.1f, 0.3f) * JkWorldScale.MetresPerJkUnit, 1, 0f, false, Vector3.Zero, 0f);

    private static readonly SoundAsset Start = new("sound/jk/df_door2-1.wav", [1, 1, 1]);
    private static readonly SoundAsset Stop = new("sound/jk/df_door2-3.wav", [3, 3, 3]);

    private static MoverSoundSet FullSet() => new("med_door.snd", Start, Stop, StartFromLoop: false, DropsMovingLoop: true);

    /// <summary>Niveau de 3 secteurs avec un ou deux mobiles « door » (things 1 et 2) montant de 0,3 unite JK.</summary>
    private static SyntheticLevel Level(params (int Id, float X)[] things) => new()
    {
        Sectors = 3,
        ExtraTemplates = ["door none type=cog model3d=door.3do soundclass=med_door.snd"],
        ExtraThings = [.. things.Select(t => $"{t.Id}: door door {F(t.X)} 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes=2 {Fr(t.X, 0.5f, 0.25f)} {Fr(t.X, 0.5f, 0.55f)}")],
    };

    private static (ConvertedEntities Converted, ImportLog Log) Run(SyntheticLevel level, IReadOnlyList<MoverBehavior> behaviors, Func<int, MoverSoundSet?>? sounds, params int[] things)
    {
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, new ImportLog());
        var models = (things.Length == 0 ? [1] : things).ToDictionary(t => t, _ => Md3());
        var (converted, _) = MoverConverter.Convert(behaviors, prepared, models, Scale, 1, log, sounds);
        return (converted, log);
    }

    private static List<IReadOnlyDictionary<string, string>> Of(ConvertedEntities c, string classname) =>
        [.. c.Entities.Where(x => x["classname"] == classname)];

    private static IReadOnlyDictionary<string, string> Speaker(ConvertedEntities c, string name) =>
        Assert.Single(Of(c, "target_speaker"), s => s["targetname"] == name);

    // ---- mobiles ----

    [Fact]
    public void TouchDoor_WithASoundClass_FiresAStartSpeakerAndAStopSpeaker()
    {
        var (converted, log) = Run(Level((1, 1.5f)), [new TouchDoor(1) { Speed = 8f, Delay = 2f }], _ => FullSet());

        var door = Assert.Single(Of(converted, "func_door"));
        var start = Speaker(converted, door["target"]);                  // « target » : le mobile le tire quand il part
        Assert.Equal(Start.Path, start["noise"]);
        var stop = Speaker(converted, door["opentarget"]);               // arrivee en position ouverte...
        Assert.Equal(Stop.Path, stop["noise"]);
        Assert.Equal(door["opentarget"], door["closetarget"]);           // ... et en position fermee
        Assert.Equal(2, Of(converted, "target_speaker").Count);
        Assert.DoesNotContain("spawnflags", start.Keys);                 // un son qui joue une fois, jamais en boucle
        Assert.Equal(Start.Data, converted.Sounds.Single(s => s.Path == Start.Path).Data);
        Assert.Contains(converted.Sounds, s => s.Path == Stop.Path);
        // Les speakers sont au centre du mobile (origine + milieu de sa boite).
        var (ox, oy, oz) = (1.5f * K, 0.5f * K, 0.25f * K + 0.15f * K);
        string[] at = start["origin"].Split(' ');
        Assert.Equal(ox, float.Parse(at[0], CultureInfo.InvariantCulture), 1f);
        Assert.Equal(oy, float.Parse(at[1], CultureInfo.InvariantCulture), 1f);
        Assert.Equal(oz, float.Parse(at[2], CultureInfo.InvariantCulture), 1f);
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_SOUND" && w.Message.Contains("1 mover"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_SOUND_SKIPPED" && w.Message.Contains("looping"));
    }

    [Fact]
    public void Plat_GetsTheSameKeys()
    {
        // Ascenseur qui ne fait que monter, sans bouton : func_plat (Use_BinaryMover : memes cles target / opentarget / closetarget).
        var (converted, _) = Run(Level((1, 1.5f)), [new TwoStopLift(1) { Speed = 4f, Delay = 2f }], _ => FullSet());

        var plat = Assert.Single(Of(converted, "func_plat"));
        Assert.Equal(Start.Path, Speaker(converted, plat["target"])["noise"]);
        Assert.Equal(Stop.Path, Speaker(converted, plat["opentarget"])["noise"]);
        Assert.Equal(plat["opentarget"], plat["closetarget"]);
    }

    [Fact]
    public void SwitchDoor_KeepsItsNameAndGetsTheSounds()
    {
        var (converted, _) = Run(Level((1, 1.5f)), [new SwitchMove(1, 0, [1, 0]) { Speed = 4f, Delay = 2f }], _ => FullSet());

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal("jkm1", door["targetname"]);
        Assert.Equal(Start.Path, Speaker(converted, door["target"])["noise"]);
        Assert.Equal("jkm1", Assert.Single(Of(converted, "trigger_multiple"))["target"]); // le bouton tire toujours la porte, pas le son
    }

    [Fact]
    public void OnlyAStartSound_NoStopKeys()
    {
        var set = new MoverSoundSet("03wood_door.snd", Start, null, StartFromLoop: false, DropsMovingLoop: false);
        var (converted, _) = Run(Level((1, 1.5f)), [new TouchDoor(1) { Delay = 2f }], _ => set);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Contains("target", door.Keys);
        Assert.DoesNotContain("opentarget", door.Keys);
        Assert.DoesNotContain("closetarget", door.Keys);
        Assert.Single(Of(converted, "target_speaker"));
    }

    [Fact]
    public void PiecesOfOneDoor_ShareTheSpeakers_AndOnlyTheFirstPieceFiresTheStartSound()
    {
        // Deux pieces voisines de la meme classe (door0 / door1 d'un meme COG) : un seul son, pas deux qui se superposent.
        var level = Level((1, 1.5f), (2, 1.6f));
        var (converted, log) = Run(level, [new TouchDoor(1) { Delay = 2f }, new TouchDoor(2) { Delay = 2f }], _ => FullSet(), 1, 2);

        var doors = Of(converted, "func_door");
        Assert.Equal(2, doors.Count);
        Assert.Equal(2, Of(converted, "target_speaker").Count);                  // un depart, un arret, pour les deux pieces
        var first = Assert.Single(doors, d => d.ContainsKey("target"));         // seule la premiere piece tire les sons (depart et arret)
        Assert.Equal(Stop.Path, Speaker(converted, first["opentarget"])["noise"]);
        Assert.Single(doors, d => d.ContainsKey("opentarget"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_SOUND" && w.Message.Contains("2 mover"));
    }

    [Fact]
    public void FarApartDoors_GetTheirOwnSpeakers()
    {
        var level = Level((1, 0.5f), (2, 2.5f));
        var (converted, _) = Run(level, [new TouchDoor(1) { Delay = 2f }, new TouchDoor(2) { Delay = 2f }], _ => FullSet(), 1, 2);

        Assert.Equal(4, Of(converted, "target_speaker").Count);
        Assert.All(Of(converted, "func_door"), d => Assert.Contains("target", d.Keys));
    }

    [Fact]
    public void Train_IsNotWired_AndLogged()
    {
        // 3 images : func_train. Un train de JA rejoue son « noise » sans jamais l'arreter, et sa cle « target » est le coin suivant : aucun son.
        var level = new SyntheticLevel
        {
            Sectors = 3,
            ExtraTemplates = ["door none type=cog model3d=door.3do soundclass=med_door.snd"],
            ExtraThings = [$"1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes=3 {Fr(1.5f, 0.5f, 0.25f)} {Fr(1.5f, 0.5f, 0.55f)} {Fr(1.5f, 0.5f, 0.85f)}"],
        };
        var (converted, log) = Run(level, [new MultiStepPath(1, [0, 1, 2], 1f, true) { Delay = 1f }], _ => FullSet());

        var train = Assert.Single(Of(converted, "func_train"));
        Assert.DoesNotContain("noise", train.Keys);
        Assert.Empty(Of(converted, "target_speaker"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_SOUND_SKIPPED" && w.Message.Contains("train"));
    }

    [Fact]
    public void MoverWithoutSoundClass_HasNoSpeaker_AndNoCode()
    {
        var (converted, log) = Run(Level((1, 1.5f)), [new TouchDoor(1) { Delay = 2f }], _ => null);

        Assert.Empty(Of(converted, "target_speaker"));
        Assert.DoesNotContain("target", Assert.Single(Of(converted, "func_door")).Keys);
        Assert.DoesNotContain(log.Warnings, w => w.Code.StartsWith("JK_MOVER_SOUND", StringComparison.Ordinal));
    }

    [Fact]
    public void CogPlayedSounds_AreCountedAsNotConverted()
    {
        // Les sons que le COG joue lui-meme (clic d'interrupteur, alarme) ne sont pas ceux du mobile : journalises.
        var (converted, log) = Run(Level((1, 1.5f)), [new SwitchMove(1, 0, [1, 0]) { Speed = 4f, Delay = 2f, Sounds = ["beep1.wav", "lgclick1.wav"] }], _ => FullSet());

        Assert.Contains("target", Assert.Single(Of(converted, "func_door")).Keys);
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_SOUND_SKIPPED" && w.Message.Contains("played by their COG"));
    }

    // ---- classe de sons (.snd) ----

    private const string MedDoorSnd = """
        # Foley description for medium doors
        startmove	df_door2-1.wav 0x0 	0.1 	1.0
        stopmove	df_door2-3.wav 0x0 	0.1 	1.0
        moving		df_door2-2.wav 0x0001 	0.1  	1.0   0.5
        """;

    [Fact]
    public void SoundClass_StartStopAndMoving_AreRead()
    {
        var modes = JkMoverSounds.ParseClass(MedDoorSnd);

        Assert.Equal("df_door2-1.wav", modes["startmove"]);
        Assert.Equal("df_door2-3.wav", modes["stopmove"]);
        Assert.Equal("df_door2-2.wav", modes["moving"]);
        Assert.Equal(3, modes.Count);
    }

    [Fact]
    public void SoundClass_IgnoresCommentsAndBlankLines_AndKeepsTheFirstLinePerMode()
    {
        var modes = JkMoverSounds.ParseClass("# comment\r\n\r\n#stopmove\r\nmoving\tFunicularMove01.WAV 0x0001\r\nmoving other.wav\r\n\tstartmove\tFunicularStart01.wav\r\n");

        Assert.Equal("FunicularMove01.WAV", modes["moving"]);
        Assert.Equal("FunicularStart01.wav", modes["startmove"]);
        Assert.False(modes.ContainsKey("stopmove"));
    }

    private JkFileSystem Archive(params (string Path, byte[] Data)[] files)
    {
        string dir = Path.Combine(Path.GetTempPath(), "jkmoversnd_" + Guid.NewGuid().ToString("N"));
        _dirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        File.WriteAllBytes(Path.Combine(dir, "Episode", "JK1.GOB"), Gob(files));
        return JkFileSystem.ForEpisode(dir, "JK1.GOB");
    }

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

    /// <summary>WAV PCM mono 8 bits minimal.</summary>
    private static byte[] Wav(byte fill)
    {
        byte[] data = new byte[16];
        Array.Fill(data, fill);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(11025); w.Write(11025); w.Write((short)1); w.Write((short)8);
        w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data);
        w.Flush();
        return ms.ToArray();
    }

    private static SyntheticLevel ClassLevel(string soundclass) => new()
    {
        Sectors = 3,
        ExtraTemplates = [$"door none type=cog model3d=door.3do soundclass={soundclass}"],
        ExtraThings = [$"1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes=2 {Fr(1.5f, 0.5f, 0.25f)} {Fr(1.5f, 0.5f, 0.55f)}"],
    };

    [Fact]
    public void Resolver_ReadsTheClassAndTheWavsFromTheArchives()
    {
        using JkFileSystem fs = Archive((@"misc\snd\med_door.snd", Encoding.Latin1.GetBytes(MedDoorSnd)), (@"sound\df_door2-1.wav", Wav(1)), (@"sound\df_door2-3.wav", Wav(3)), (@"sound\df_door2-2.wav", Wav(2)));
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(ClassLevel("med_door.snd"), new ImportLog());

        MoverSoundSet? set = new JkMoverSounds(prepared, fs, log).For(1);

        Assert.NotNull(set);
        Assert.Equal("sound/jk/df_door2-1.wav", set!.Start!.Path);
        Assert.Equal("sound/jk/df_door2-3.wav", set.Stop!.Path);
        Assert.False(set.StartFromLoop);
        Assert.True(set.DropsMovingLoop);                    // un `moving` existe mais une porte JA ne sait pas l'arreter
        Assert.Empty(log.Warnings);
    }

    [Fact]
    public void Resolver_ClassWithOnlyAMovingLoop_PlaysItOnceAtTheStart()
    {
        using JkFileSystem fs = Archive((@"misc\snd\06stone_door.snd", Encoding.Latin1.GetBytes("# stone\n#stopmove\nmoving 19StoneDoor01.WAV 0x0001 1.0 4.0 1.0\n")), (@"sound\19StoneDoor01.WAV", Wav(5)));
        JkPreparedScene prepared = SyntheticLevel.Build(ClassLevel("06stone_door.snd"), new ImportLog());

        MoverSoundSet set = new JkMoverSounds(prepared, fs, new ImportLog()).For(1)!;

        Assert.Equal("sound/jk/19stonedoor01.wav", set.Start!.Path);
        Assert.True(set.StartFromLoop);
        Assert.Null(set.Stop);
        Assert.False(set.DropsMovingLoop);
    }

    [Fact]
    public void Resolver_MissingClassFile_IsLoggedOnceAndSkipped()
    {
        using JkFileSystem fs = Archive((@"misc\cmp\test.cmp", SyntheticLevel.Cmp()));
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(ClassLevel("ghost.snd"), new ImportLog());
        var resolver = new JkMoverSounds(prepared, fs, log);

        Assert.Null(resolver.For(1));
        Assert.Null(resolver.For(1));
        Assert.Single(log.Warnings, w => w.Code == "JK_MOVER_SOUND_SKIPPED" && w.Message.Contains("ghost.snd"));
    }

    [Fact]
    public void Resolver_MissingWav_IsLoggedBySounds_AndTheRestIsKept()
    {
        using JkFileSystem fs = Archive((@"misc\snd\med_door.snd", Encoding.Latin1.GetBytes(MedDoorSnd)), (@"sound\df_door2-3.wav", Wav(3)));
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(ClassLevel("med_door.snd"), new ImportLog());

        MoverSoundSet set = new JkMoverSounds(prepared, fs, log).For(1)!;

        Assert.Null(set.Start);
        Assert.NotNull(set.Stop);
        Assert.Contains(log.Warnings, w => w.Code == "JK_SOUND_MISSING" && w.Message.Contains("df_door2-1.wav"));
    }

    [Fact]
    public void Resolver_ThingWithoutSoundClass_IsNull()
    {
        using JkFileSystem fs = Archive((@"misc\cmp\test.cmp", SyntheticLevel.Cmp()));
        var level = new SyntheticLevel { Sectors = 3, ExtraTemplates = ["door none type=cog model3d=door.3do"], ExtraThings = ["1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448"] };
        JkPreparedScene prepared = SyntheticLevel.Build(level, new ImportLog());

        Assert.Null(new JkMoverSounds(prepared, fs, new ImportLog()).For(1));
    }

    // ---- chaine complete : JkObjects ----

    [Fact]
    public void JkObjects_WiresTheSoundClassOfAMover()
    {
        const string DoorCog = """
            symbols
                message startup
                message activate
                thing   door0 linkid=0
                flex    moveSpeed=8.0
            end
            code
            startup:
                return;
            activate:
                MoveToFrame(door0, 1, moveSpeed);
                return;
            end
            """;
        var level = ClassLevel("med_door.snd");
        level = new SyntheticLevel { Sectors = 3, Cogs = ["00_door.cog 1"], CogScripts = new Dictionary<string, string> { ["00_door.cog"] = DoorCog }, ExtraTemplates = level.ExtraTemplates, ExtraThings = level.ExtraThings };
        using JkFileSystem fs = Archive((@"3do\door.3do", Encoding.Latin1.GetBytes(Door3do)), (@"mat\fan.mat", SyntheticLevel.TexMat(8, 8, 10)), (@"misc\cmp\test.cmp", SyntheticLevel.Cmp()),
            (@"cog\00_door.cog", Encoding.Latin1.GetBytes(DoorCog)), (@"misc\snd\med_door.snd", Encoding.Latin1.GetBytes(MedDoorSnd)),
            (@"sound\df_door2-1.wav", Wav(1)), (@"sound\df_door2-3.wav", Wav(3)));
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, log);
        var opt = new ExportOptions("", Path.Combine(Path.GetTempPath(), "jkmsbase"), Path.GetTempPath(), Game: KotorGame.JediKnight);

        var (converted, _) = JkObjects.Convert(prepared, fs, opt, log);

        var door = Assert.Single(converted.Entities, x => x["classname"] == "func_door");
        Assert.Equal("sound/jk/df_door2-1.wav", Assert.Single(converted.Entities, x => x["classname"] == "target_speaker" && x["targetname"] == door["target"])["noise"]);
        Assert.Contains(converted.Sounds, s => s.Path == "sound/jk/df_door2-1.wav");
        Assert.Contains(converted.Sounds, s => s.Path == "sound/jk/df_door2-3.wav");
        Assert.DoesNotContain(converted.Sounds, s => s.Path.Contains("df_door2-2"));   // le son en boucle n'est pas copie
    }

    private const string Door3do = """
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
        VERTICES 4
        0: 0.0 0.0 0.0 0.0
        1: 0.3 0.0 0.0 0.0
        2: 0.3 0.2 0.0 0.0
        3: 0.0 0.2 0.0 0.0
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
        0: 0 0x0000 4 3 1 0.0 4 0,0 3,1 2,2 1,3
        FACE NORMALS
        0: 0.0 0.0 -1.0
        SECTION: HIERARCHYDEF
        HIERARCHY NODES 1
        0: 0x0000 0x00001 0 -1 -1 -1 0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 0.0 cube
        """;

    // ---- donnees reelles ----

    /// <summary>Mobiles qui tirent un son de depart, par niveau, et speakers poses (releves sur les donnees du jeu).</summary>
    private static readonly SortedDictionary<string, (int Wired, int Speakers)> ExpectedSounds = new(StringComparer.Ordinal)
    {
        ["01narshadda"] = (20, 40), ["02narshadda"] = (19, 38), ["03katarn"] = (21, 21), ["04escapehouse"] = (11, 22), ["06abarons"] = (8, 8), ["06bbarons"]
        = (20, 34), ["07yun"] = (4, 8), ["08escape88"] = (14, 28), ["09fuelstation"] = (27, 54), ["10cargo"] = (31, 62), ["12escape"] = (36, 72), ["14tower"]
        = (10, 20), ["15maw"] = (3, 6), ["16aescapeship"] = (13, 26), ["16bescapeship"] = (13, 26), ["18ascend"] = (24, 48), ["19a"] = (39, 78), ["19b"] =
        (12, 22), ["21ajerec"] = (2, 2), ["21bjerec"] = (2, 2), ["c1"] = (1, 2), ["c2"] = (3, 6), ["c3"] = (32, 64), ["m2"] = (7, 14), ["m4"] = (18, 36),
        ["m5"] = (8, 16), ["m_boss15"] = (3, 6),
    };

    [SkippableFact]
    public void RealLevels_MoverSoundsPerLevel()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        var found = new SortedDictionary<string, (int Wired, int Speakers)>(StringComparer.Ordinal);
        var report = new StringBuilder();
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, subs) = JkObjects.Convert(prepared, fs, opt, log);
            Assert.True(subs.Count <= JkObjects.MaxSubModels);

            var speakers = Of(converted, "target_speaker").Where(s => s.GetValueOrDefault("targetname", "").StartsWith("jksnd", StringComparison.Ordinal)).ToList();
            var movers = converted.Entities.Where(e => (e["classname"] is "func_door" or "func_plat") && (e.ContainsKey("target") || e.ContainsKey("opentarget"))).ToList();
            // Chaque cle de son vise un speaker pose ; jamais un train ni un mobile sans classe de sons.
            foreach (var m in movers)
                foreach (string key in new[] { "target", "opentarget", "closetarget" })
                    if (m.TryGetValue(key, out string? t)) Assert.Contains(speakers, s => s["targetname"] == t);
            Assert.DoesNotContain(converted.Entities, e => e["classname"] == "func_train" && (e.GetValueOrDefault("noise") is not null));
            // Chaque speaker a son wav dans les sons rendus.
            Assert.All(speakers, s => Assert.Contains(converted.Sounds, a => a.Path == s["noise"]));
            if (speakers.Count > 0) found[level.Name] = (movers.Count, speakers.Count);
            foreach (var w in log.Warnings.Where(w => w.Code.StartsWith("JK_MOVER_SOUND", StringComparison.Ordinal)))
                report.AppendLine($"{level.Name}\t{w.Code}\t{w.Message}");
        }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jk_l2_moversounds.tsv"), string.Join('\n', found.Select(kv => $"{kv.Key}\t{kv.Value.Wired}\t{kv.Value.Speakers}")) + "\n" + report);
        Assert.Equal(ExpectedSounds, found);
        Assert.Equal(401, found.Values.Sum(v => v.Wired));       // mobiles qui tirent au moins un son
        Assert.Equal(761, found.Values.Sum(v => v.Speakers));
    }
}

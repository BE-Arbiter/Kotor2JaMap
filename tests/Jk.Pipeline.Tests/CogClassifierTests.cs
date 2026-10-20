using System.Text;
using Jk.Core;
using Jk.Core.Cog;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Pipeline.Movers;

namespace Jk.Pipeline.Tests;

/// <summary>Lecture des COG et classement des comportements de mouvement (tâche 8).</summary>
public class CogClassifierTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    // ---- aides ----

    private const string FramesTwo = "numframes=2 frame=(0/0/0:0/0/0) frame=(0/0/1:0/0/0)";
    private const string FramesThree = "numframes=3 frame=(0/0/0:0/0/0) frame=(0/0/1:0/0/0) frame=(0/0/2:0/0/0)";

    /// <summary>Niveau de 3 secteurs : le thing 0 est le joueur ; <paramref name="movers"/> sont les things 1, 2... (gabarit « door », type cog) avec leurs images.</summary>
    private static JklLevel Level(string frames, int movers = 1, params string[] extraThings)
    {
        var things = new List<string>();
        for (int i = 1; i <= movers; i++) things.Add(FormattableString.Invariant($"{i}: door door {0.1f * i} 0.5 0.12 0 0 0 {i % 3} {frames}"));
        things.AddRange(extraThings);
        return new SyntheticLevel { Sectors = 3, ExtraTemplates = ["door none type=cog"], ExtraThings = things }.Read();
    }

    private static BoundCog Bind(string cogText, string instanceValues, JklLevel lvl)
    {
        CogScript script = CogReader.Read(cogText);
        var inst = new JklCogInstance(0, "test.cog", instanceValues.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return CogBinding.Bind(script, inst, lvl);
    }

    private static IReadOnlyList<MoverBehavior> Classify(string cogText, string instanceValues, JklLevel lvl) =>
        CogClassifier.Classify(Bind(cogText, instanceValues, lvl), lvl);

    private const string DoorCog = """
        symbols
            message startup
            message activated
            message arrived
            message timer
            thing   door
            flex    movespeed=8.0
            flex    sleeptime=2.0
            sound   opensnd=door_open.wav
            sector  doorsector local
        end
        code
        startup:
            doorsector = GetThingSector(door);
            SetSectorAdjoins(doorsector, 0);
            return;
        activated:
            SetSectorAdjoins(doorsector, 1);
            PlaySoundThing(opensnd, door, 0.6, -1, -1, 0);
            MoveToFrame(door, 1, movespeed);
            return;
        arrived:
            if (GetCurFrame(door) == 1) SetTimer(sleeptime);
            return;
        timer:
            MoveToFrame(door, 0, movespeed);
            return;
        end
        """;

    // ---- lecture ----

    [Fact]
    public void Reader_ReadsSymbolsAndMessages()
    {
        CogScript s = CogReader.Read(DoorCog);

        Jk.Core.Cog.CogSymbol speed = s.Symbol("movespeed")!;
        Assert.Equal("flex", speed.Type);
        Assert.Equal("8.0", speed.Default);
        Assert.True(s.Symbol("doorsector")!.Local);
        Assert.True(s.HasMessage("activated"));
        Assert.Equal(["startup", "activated", "arrived", "timer"], s.MessageNames);

        IReadOnlyList<CogCallSite> activated = s.Expand("activated");
        Assert.Equal(["SetSectorAdjoins", "PlaySoundThing", "MoveToFrame", "return"], activated.Select(c => c.Function));
        Assert.Equal(["door", "1", "movespeed"], activated[2].Args);
    }

    [Fact]
    public void Reader_KeepsConditions_AndFollowsCallsAndFallThrough()
    {
        const string text = """
            symbols
                message crossed
                message activate
                thing   t
                surface button linkid=3 mask=0x448
            end
            code
            crossed:
                Sleep(0.25);
            activate:
                if (GetWallCel(button) == 1) return;
                call Go;
                return;
            Go:
                if (a) MoveToFrame(t, 1, 2); else MoveToFrame(t, 0, 2);
                return;
            end
            """;
        CogScript s = CogReader.Read(text);

        Assert.Equal(3, s.Symbol("button")!.LinkId);
        Assert.Equal("0x448", s.Symbol("button")!.Mask);

        // crossed n'a pas de return : il tombe dans activate, qui appelle Go.
        IReadOnlyList<CogCallSite> sites = s.Expand("crossed");
        Assert.Equal(["Sleep", "return", "MoveToFrame", "MoveToFrame", "return", "return"], sites.Select(c => c.Function));
        CogCallSite open = sites.Single(c => c.Function == "MoveToFrame" && c.Args[1] == "1");
        CogCallSite close = sites.Single(c => c.Function == "MoveToFrame" && c.Args[1] == "0");
        Assert.Equal(["a"], open.Conditions);
        Assert.Equal(["!(a)"], close.Conditions);
    }

    [Fact]
    public void Binding_FillsNonLocalSymbolsInOrder_AndEvaluatesExpressions()
    {
        JklLevel lvl = Level(FramesTwo);
        BoundCog cog = Bind(DoorCog, "1 4.5", lvl);

        Assert.Equal(1, cog.Int("door"));
        Assert.Equal(4.5, cog.Number("movespeed"));
        Assert.Equal(2.0, cog.Number("sleeptime")); // defaut
        Assert.Equal("door_open.wav", cog.Get("opensnd"));
        Assert.Equal(12.0, cog.Number("(movespeed + 1.5) * 2 - 0"));
        Assert.Null(cog.Number("GetCurFrame(door)"));
    }

    // ---- classement ----

    [Fact]
    public void DoorCog_TouchDoor()
    {
        JklLevel lvl = Level(FramesTwo);

        var found = Assert.Single(Classify(DoorCog, "1 6.0 3.0", lvl));

        var door = Assert.IsType<TouchDoor>(found);
        Assert.Equal(1, door.Thing);
        Assert.Equal(6.0f, door.Speed);
        Assert.Equal(3.0f, door.Delay);
        Assert.Equal("door_open.wav", Assert.Single(door.Sounds));
        Assert.Equal([1, 0], door.FrameSequence);
    }

    [Fact]
    public void MultiPieceDoor_OneBehaviorPerPiece()
    {
        const string text = """
            symbols
                message activate
                message timer
                thing   door0 mask=0x405
                thing   door1 mask=0x405
                float   moveSpeed=8.0
            end
            code
            activate:
                for (i=0; i<=1; i=i+1)
                    if (door0[i] >= 0) MoveToFrame(door0[i], 1, moveSpeed);
                return;
            timer:
                for (i=0; i<=1; i=i+1)
                    if (door0[i] >= 0) MoveToFrame(door0[i], 0, moveSpeed);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo, 2);

        var found = Classify(text, "1 2", lvl);

        Assert.Equal([1, 2], found.OfType<TouchDoor>().Select(d => d.Thing).Order());
    }

    private const string ElevSwitchCog = """
        symbols
            message crossed
            message activate
            message arrived
            message timer
            surface lower_adjoin0 linkid=1
            surface lower_adjoin1 linkid=1
            surface lower_adjoin2 linkid=1
            surface button        linkid=1
            thing   elevator      linkid=2
            flex    start_wait=0.25
            flex    sleeptime=2.0
            flex    speed=4.0
            sound   wav0=Activate02.wav
        end
        code
        crossed:
            Sleep(start_wait);
        activate:
            if (GetSenderId() != 1) return;
            if (GetWallCel(button) == 1) return;
            SetWallCel(button, 1);
            PlaySoundPos(wav0, SurfaceCenter(button), 0.6, -1, -1, 0);
            MoveToFrame(elevator, 1, speed);
            return;
        arrived:
            if (GetCurFrame(elevator) == 0) {
                SetWallCel(button, 0);
            } else {
                SetTimer(sleeptime);
            }
            return;
        timer:
            MoveToFrame(elevator, 0, speed);
            return;
        end
        """;

    [Fact]
    public void ElevatorSwitch_SwitchMove()
    {
        JklLevel lvl = Level(FramesTwo);

        var found = Classify(ElevSwitchCog, "0 -1 -1 3 1", lvl);

        var sw = Assert.Single(found.OfType<SwitchMove>());
        Assert.Equal(1, sw.Thing);
        Assert.Equal(3, sw.SwitchSurfaceOrThing);
        Assert.False(sw.SwitchIsThing);
        Assert.Equal([1, 0], sw.Frames);
        Assert.Equal(4.0f, sw.Speed);
        Assert.Equal(2.0f, sw.Delay);
        // Le même COG déclenche aussi l'ascenseur au passage des adjoins (message crossed) : deux positions.
        Assert.Single(found.OfType<TwoStopLift>(), l => l.Thing == 1);
    }

    [Fact]
    public void CallButtons_EachSwitchSendsTheElevatorToItsFloor()
    {
        const string text = """
            symbols
                message activated
                surface callbutton
                surface upbutton
                thing   elevator
                flex    speed=5.0
                flex    floorlevel=0.0
            end
            code
            activated:
                switch = GetSenderRef();
                if (switch == callbutton) {
                    MoveToFrame(elevator, floorlevel, speed);
                } else if (switch == upbutton) {
                    MoveToFrame(elevator, (floorlevel+1), speed);
                }
                return;
            end
            """;
        JklLevel lvl = Level(FramesThree);

        var found = Classify(text, "5 6 1 5.0 1", lvl).OfType<SwitchMove>().OrderBy(s => s.SwitchSurfaceOrThing).ToList();

        Assert.Equal(2, found.Count);
        Assert.Equal(5, found[0].SwitchSurfaceOrThing);
        Assert.Equal([1], found[0].Frames);
        Assert.Equal(6, found[1].SwitchSurfaceOrThing);
        Assert.Equal([2], found[1].Frames);
    }

    [Fact]
    public void SectorTriggeredLift_TwoStopLift()
    {
        const string text = """
            symbols
                message crossed
                message arrived
                message timer
                surface lower_adjoin0
                thing   elevator
                float   start_wait=0.25
                float   sleeptime=2.0
                float   speed=4.0
            end
            code
            crossed:
                if (IsThingMoving(elevator)) return;
                Sleep(start_wait);
                MoveToFrame(elevator, 1, speed);
                return;
            arrived:
                if (GetCurFrame(elevator) == 0) return;
                SetTimer(sleeptime);
                return;
            timer:
                MoveToFrame(elevator, 0, speed);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo);

        var lift = Assert.IsType<TwoStopLift>(Assert.Single(Classify(text, "1 1", lvl)));

        Assert.Equal(1, lift.Thing);
        Assert.Equal(4.0f, lift.Speed);
        Assert.Equal(2.0f, lift.Delay);
    }

    [Fact]
    public void KilledMessage_ActorTriggered()
    {
        const string text = """
            symbols
                message startup
                message killed
                thing   boss mask=0xfff
                thing   door
                sector  doorsector
                flex    speed=3.0
            end
            code
            startup:
                MoveToFrame(door, 0, speed);
                SectorAdjoins(doorsector, 0);
                return;
            killed:
                Sleep(3.0);
                SectorAdjoins(doorsector, 1);
                MoveToFrame(door, 1, speed);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo); // le thing 1 est la porte (secteur 1)

        var found = Classify(text, "9 1 2", lvl);

        var trig = Assert.IsType<ActorTriggered>(Assert.Single(found));
        Assert.Equal(1, trig.Thing);
        Assert.Equal(lvl.Things[1].Sector, trig.Sector);
    }

    [Fact]
    public void ThreeFrames_MultiStepPath()
    {
        const string text = """
            symbols
                message activated
                message arrived
                thing   mover
                float   speed=2.0
                float   pause=1.5
                int     step local
            end
            code
            activated:
                step = 1;
                MoveToFrame(mover, 1, speed);
                return;
            arrived:
                Sleep(pause);
                if (GetCurFrame(mover) == 1) MoveToFrame(mover, 2, speed);
                else MoveToFrame(mover, 0, speed);
                return;
            end
            """;
        JklLevel lvl = Level(FramesThree);

        var path = Assert.IsType<MultiStepPath>(Assert.Single(Classify(text, "1", lvl)));

        Assert.Equal([1, 2, 0], path.Frames);
        Assert.Equal(1.5f, path.Wait);
        Assert.True(path.Loop); // revient a la premiere position
        Assert.Equal(2.0f, path.Speed);
    }

    [Fact]
    public void SwitchCog_WithoutMovement_SurfaceCelSwitch()
    {
        const string text = """
            symbols
                message activated
                surface lamp
            end
            code
            activated:
                SetWallCel(lamp, 1);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo);

        var cel = Assert.IsType<SurfaceCelSwitch>(Assert.Single(Classify(text, "4", lvl)));

        Assert.Equal(4, cel.Surface);
        Assert.Equal(-1, cel.Thing);
    }

    [Fact]
    public void RotatePivot_OnThingWithFrames_Approximate_WithoutFrames_Unsupported()
    {
        const string text = """
            symbols
                message activated
                thing   hatch
            end
            code
            activated:
                RotatePivot(hatch, '0 0 1', 90.0);
                return;
            end
            """;
        JklLevel withFrames = Level(FramesTwo);
        JklLevel noFrames = Level("");

        var approx = Assert.IsType<Approximate>(Assert.Single(Classify(text, "1", withFrames)));
        var unsupported = Assert.IsType<Unsupported>(Assert.Single(Classify(text, "1", noFrames)));

        Assert.Contains("RotatePivot", approx.Reason);
        Assert.Contains("RotatePivot", unsupported.Reason);
        Assert.Equal(1, unsupported.Thing);
    }

    [Fact]
    public void UnknownVerbs_Approximate()
    {
        const string text = """
            symbols
                message activated
                thing   bridge
            end
            code
            activated:
                WeirdCustomVerb(bridge, 3);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["weird.cog"] = text };

        LevelMovers report = CogClassifier.ClassifyLevel(lvl with { Cogs = [new JklCogInstance(0, "weird.cog", ["1"])] }, f => texts.GetValueOrDefault(f));

        var approx = Assert.IsType<Approximate>(Assert.Single(report.All));
        Assert.Equal(1, approx.Thing);
        Assert.Contains("weird.cog", approx.Reason);
    }

    [Fact]
    public void ThingWithFrames_NoCog_KeptAsApproximate()
    {
        JklLevel lvl = Level(FramesTwo);

        LevelMovers report = CogClassifier.ClassifyLevel(lvl, _ => null);

        var approx = Assert.IsType<Approximate>(Assert.Single(report.All));
        Assert.Equal(1, approx.Thing);
        Assert.Contains("no COG", approx.Reason);
    }

    [Fact]
    public void Actors_AreNeverMovers()
    {
        // Un acteur avec des images (patrouille) n'est pas place : il n'a pas de comportement.
        JklLevel lvl = new SyntheticLevel
        {
            Sectors = 3,
            ExtraTemplates = ["trooper _actor type=actor"],
            ExtraThings = [$"1: trooper t 0.2 0.5 0.12 0 0 0 0 {FramesTwo}"],
        }.Read();

        LevelMovers report = CogClassifier.ClassifyLevel(lvl, _ => null);

        Assert.Empty(report.All);
    }

    [Fact]
    public void MissingScript_IsReportedNotThrown()
    {
        JklLevel lvl = Level(FramesTwo) with { Cogs = [new JklCogInstance(0, "gone.cog", ["1"])] };

        LevelMovers report = CogClassifier.ClassifyLevel(lvl, _ => null);

        Assert.Equal(["gone.cog"], report.MissingScripts);
        Assert.Contains(report.All, b => b is Approximate a && a.Thing == 1);
    }

    [Fact]
    public void MalformedScript_DoesNotThrow_AndLeavesADiagnostic()
    {
        JklLevel lvl = Level(FramesTwo);
        foreach (string text in new[] { "symbols\nthing t\n", "code\nactivated: if ( MoveToFrame(", "symbols\nmessage a\nend\ncode\na: { { if else ;;; }\nend", "code\na: MoveToFrame(t, 1 /* never closed" })
        {
            Assert.NotNull(Classify(text, "1", lvl));
            Assert.NotEmpty(CogReader.Read(text).Warnings);
        }
        Assert.Empty(CogReader.Read("").Warnings);
        Assert.Empty(CogReader.Read(DoorCog).Warnings);
    }

    [Fact]
    public void MalformedScript_IsReportedByTheLevelClassification()
    {
        JklLevel lvl = Level(FramesTwo) with { Cogs = [new JklCogInstance(0, "cut.cog", ["1"])] };
        const string cut = "symbols\nmessage activated\nthing door\nend\ncode\nactivated:\n  MoveToFrame(door, 1, 2";

        LevelMovers report = CogClassifier.ClassifyLevel(lvl, _ => cut);

        Assert.NotEmpty(report.MalformedScripts["cut.cog"]);
        Assert.Empty(report.MissingScripts);
    }

    [Fact]
    public void Reader_KeepsCommentMarkersInsideQuotedLiterals()
    {
        const string text = """
            symbols
                message activated
                string  url='http://x//y#z'  // commentaire final
                thing   t desc='Door #1'
            end
            code
            activated:
                PlaySong('a//b#c', 1); // commentaire
                MoveToFrame(t, 1, 2); /* bloc */
                return;
            end
            """;

        CogScript s = CogReader.Read(text);

        Assert.Equal("http://x//y#z", s.Symbol("url")!.Default);
        Assert.Contains("#1", s.Symbol("t")!.Description);
        IReadOnlyList<CogCallSite> sites = s.Expand("activated");
        Assert.Equal(["PlaySong", "MoveToFrame", "return"], sites.Select(c => c.Function));
        Assert.Equal("'a//b#c'", sites[0].Args[0]);
        Assert.Empty(s.Warnings);
    }

    private const string TwoDoorHeader = """
        symbols
            message activated
            message touched
            message arrived
            thing   a
            thing   b
            flex    sp=4.0
        end
        code
        """;

    [Fact]
    public void Delay_IsTheTimerOfTheMessageThatMovesThatThing()
    {
        string text = TwoDoorHeader + """

            activated:
                MoveToFrame(a, 1, sp);
                SetTimer(7.0);
                return;
            touched:
                MoveToFrame(b, 1, sp);
                SetTimer(3.0);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo, 2);

        var found = Classify(text, "1 2", lvl).OfType<TouchDoor>().ToDictionary(d => d.Thing);

        Assert.Equal(7.0f, found[1].Delay);
        Assert.Equal(3.0f, found[2].Delay);
    }

    [Fact]
    public void Delay_IsLeftEmptyWithANote_WhenTheTimerCannotBeAttributed()
    {
        string text = TwoDoorHeader + """

            activated:
                MoveToFrame(a, 1, sp);
                return;
            touched:
                MoveToFrame(b, 1, sp);
                return;
            arrived:
                SetTimer(2.0);
                return;
            end
            """;
        JklLevel lvl = Level(FramesTwo, 2);

        var found = Classify(text, "1 2", lvl).OfType<TouchDoor>().OrderBy(d => d.Thing).ToList();

        Assert.Equal(2, found.Count);
        foreach (TouchDoor d in found)
        {
            Assert.Null(d.Delay);
            Assert.Contains("timer", d.DelayNote);
        }
    }

    // ---- donnees reelles ----

    [SkippableFact]
    public void All34Levels_EveryMoverClassified()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        int levels = 0, movers = 0, unsupported = 0, approximate = 0;
        var unsupportedList = new List<string>();
        foreach (JkLevelRef level in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
            JklLevel jkl = JklReader.Read(Encoding.Latin1.GetString(fs.TryRead($@"jkl\{level.Name}.jkl")!));
            LevelMovers report = CogClassifier.ClassifyLevel(jkl, f => fs.TryRead(@"cog\" + f) is { } b ? Encoding.Latin1.GetString(b) : null);

            var classified = report.All.Where(b => b.Thing >= 0).Select(b => b.Thing).ToHashSet();
            foreach (int thing in CogClassifier.FramedThings(jkl).Concat(report.Movers).Distinct())
            {
                movers++;
                Assert.True(classified.Contains(thing), $"{level.Name}: thing {thing} moves but has no behavior");
            }
            unsupported += report.All.Count(b => b is Unsupported);
            approximate += report.All.Count(b => b is Approximate);
            foreach (string m in report.MissingScripts) Console.WriteLine($"MISSING {level.Name}: {m}");
            foreach (var (file, warnings) in report.MalformedScripts) Console.WriteLine($"MALFORMED {level.Name}: {file}: {string.Join("; ", warnings)}");
            unsupportedList.AddRange(report.All.OfType<Unsupported>().Select(u => $"{level.Name}: {u.Reason}"));
            levels++;
        }
        Assert.Equal(34, levels);
        Assert.True(movers > 100, $"only {movers} movers found");
        // Le bilan est ecrit dans le rapport de la tache ; on en garde la trace dans la sortie du test.
        Console.WriteLine($"movers={movers} approximate={approximate} unsupported={unsupported}");
        foreach (string u in unsupportedList) Console.WriteLine("UNSUPPORTED " + u);
    }
}

using System.Globalization;
using System.Numerics;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Pipeline.Movers;
using Kotor.Compiler;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;
using Kotor.Pipeline.Entities;
using Xunit;

namespace Jk.Pipeline.Tests;

/// <summary>Elements mobiles (tache 9) : portes, ascenseurs, interrupteurs, chemins en plusieurs etapes -> entites JA.</summary>
public sealed class MoverConverterTests : IDisposable
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

    private static string Fr(float x, float y, float z, float pitch = 0, float yaw = 0, float roll = 0) =>
        $"frame=({F(x)}/{F(y)}/{F(z)}:{F(pitch)}/{F(yaw)}/{F(roll)})";

    /// <summary>Niveau de 3 secteurs ; thing 1 = mobile « door » (modele door.3do) place en (1.5, 0.5, 0.25), ses images en parametre.</summary>
    private static SyntheticLevel Level(params string[] frames)
    {
        string line = $"1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes={frames.Length} {string.Join(' ', frames)}";
        return new SyntheticLevel { Sectors = 3, ExtraTemplates = ["door none type=cog model3d=door.3do"], ExtraThings = [line] };
    }

    /// <summary>MD3 de test : boite de 0,4 x 0,2 x 0,3 unites JK centree sur l'origine en X et Y, posee sur Z = 0 (metres de la scene = unites JK x 8,6).</summary>
    private static JkMd3 Md3(string name = "door", float w = 0.4f, float d = 0.2f, float h = 0.3f) =>
        new(new Md3Asset($"models/jk/{name}.md3", [1, 2, 3], ["tex"]),
            new Vector3(-w / 2, -d / 2, 0) * JkWorldScale.MetresPerJkUnit, new Vector3(w / 2, d / 2, h) * JkWorldScale.MetresPerJkUnit,
            1, 0f, false, Vector3.Zero, 0f);

    private static (ConvertedEntities Converted, IReadOnlyList<SubModelInput> SubModels, ImportLog Log) Run(SyntheticLevel level, IReadOnlyList<MoverBehavior> behaviors,
        JkMd3? md3 = null, int firstSubModel = 1)
    {
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, new ImportLog());
        var models = new Dictionary<int, JkMd3> { [1] = md3 ?? Md3() };
        var (converted, subs) = MoverConverter.Convert(behaviors, prepared, models, Scale, firstSubModel, log);
        return (converted, subs, log);
    }

    private static List<IReadOnlyDictionary<string, string>> Of(ConvertedEntities c, string classname) =>
        [.. c.Entities.Where(x => x["classname"] == classname)];

    // ---- tests demandes par le plan ----

    [Fact]
    public void TouchDoor_FuncDoorWithModel2AndSubModel()
    {
        // Porte qui monte de 0,3 unite JK (image 0 -> image 1), vitesse 8, retour apres 2 s.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, subs, log) = Run(level, [new TouchDoor(1) { Cog = "00_door.cog", Speed = 8f, Delay = 2f }], firstSubModel: 3);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal("*3", door["model"]);                    // numerote a partir de firstSubModel
        Assert.Equal("models/jk/door.md3", door["model2"]);
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.25f * K)}", door["origin"]);
        Assert.Equal("-90 0 0", door["angles"]);              // vers le haut (G_SetMovedir)
        Assert.Equal("0 0 0", door["modelAngles"]);
        Assert.Equal("2", door["wait"]);
        Assert.Equal(F(8f * JkMoverSpeed.JkUnitsPerSecondPerCogSpeed * K), door["speed"]);
        // Course 0,3 u JK ; boite haute de 0,3 u JK : lip = hauteur - course = 0.
        Assert.Equal(0f, float.Parse(door["lip"], CultureInfo.InvariantCulture), 0.01f);
        Assert.DoesNotContain("targetname", door.Keys);       // se declenche seule (declencheur de proximite du moteur)
        Assert.Empty(Of(converted, "trigger_multiple"));

        SubModelInput sub = Assert.Single(subs);
        Assert.Equal(BspContents.Solid, sub.Contents);
        Assert.Equal(DoorConverter.ClipShader, sub.Shader);
        Assert.Equal(6, Assert.Single(sub.Brushes).Count);
        Assert.Contains(converted.Models, m => m.Path == "models/jk/door.md3");
        Assert.DoesNotContain(log.Warnings, w => w.Code is "JK_COG_UNSUPPORTED" or "JK_COG_APPROX");
    }

    [Fact]
    public void TouchDoor_WithoutDelay_ReturnsAfterTheDefaultWait_AndThingIsPlacedWithItsRotation()
    {
        // Retard inconnu (null) : le passage reste franchissable, la porte se ferme apres le delai par defaut.
        var level = new SyntheticLevel
        {
            Sectors = 3, ExtraTemplates = ["door none type=cog model3d=door.3do"],
            ExtraThings = ["1: door door 1.5 0.5 0.25 0 90 0 1 numframes=2 " + Fr(1.5f, 0.5f, 0.25f, 0, 90) + " " + Fr(1.8f, 0.5f, 0.25f, 0, 90)],
        };
        var (converted, _, _) = Run(level, [new TouchDoor(1) { Delay = null }]);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal(F(JkMoverSpeed.DefaultWait), door["wait"]);
        Assert.Equal("0 90 0", door["modelAngles"]);        // lacet JK conserve (le MD3 reste dans le repere JK)
        Assert.Equal("0 0 0", door["angles"]);              // course vers +X : cap 0, a plat
    }

    [Fact]
    public void SwitchLift_ButtonTargetsMover()
    {
        // L'ascenseur part quand le joueur utilise la surface 0 (interrupteur) ; il revient apres 2 s.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.85f));
        var (converted, subs, _) = Run(level, [new SwitchMove(1, 0, [1, 0]) { Cog = "00_elev_switch.cog", Speed = 4f, Delay = 2f }]);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal("jkm1", door["targetname"]);
        Assert.Equal("2", door["wait"]);
        Assert.False((int.Parse(door.GetValueOrDefault("spawnflags", "0")) & 8) != 0, "returns by itself : not a toggle");

        var trigger = Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("jkm1", trigger["target"]);
        Assert.Equal("4", trigger["spawnflags"]);             // USE_BUTTON : le joueur appuie sur « utiliser »
        Assert.Equal("*2", trigger["model"]);                 // apres le sous-modele de l'ascenseur
        Assert.Equal(2, subs.Count);
        Assert.Equal(BspContents.Trigger, subs[1].Contents);
        Assert.Equal(JkObjects.TriggerShader, subs[1].Shader);
        Assert.Contains(subs[1].Brushes[0], p => p.Normal == Vector3.UnitZ); // boite alignee sur les axes
    }

    // ---- limite de sous-modeles et comportements perdus (correctif tour 1) ----

    [Fact]
    public void SubModelLimit_MoverBecomesAPlainModel_AndIsLogged()
    {
        // Plus de place pour le corps et un declencheur : le thing reste visible (decor sans sous-modele), pas d'exception.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, subs, log) = Run(level, [new TouchDoor(1) { Speed = 8f, Delay = 2f }], firstSubModel: JkObjects.MaxSubModels);

        Assert.Empty(subs);
        Assert.Empty(Of(converted, "func_door"));
        var stat = Assert.Single(Of(converted, "misc_model_static"));
        Assert.Equal("models/jk/door.md3", stat["model"]);
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.25f * K)}", stat["origin"]);
        Assert.Contains(converted.Models, m => m.Path == "models/jk/door.md3");
        Assert.Contains(log.Warnings, w => w.Code == "JK_SUBMODEL_LIMIT" && w.Message.Contains("thing"));
    }

    [Fact]
    public void SubModelLimit_LastTwoSlots_StillBuildTheMoverAndItsTrigger()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, subs, log) = Run(level, [new ActorTriggered(1, 1) { Speed = 4f }], firstSubModel: JkObjects.MaxSubModels - 1);

        Assert.Equal(2, subs.Count);
        Assert.Equal($"*{JkObjects.MaxSubModels - 1}", Assert.Single(Of(converted, "func_door"))["model"]);
        Assert.Equal($"*{JkObjects.MaxSubModels}", Assert.Single(Of(converted, "trigger_multiple"))["model"]);
        Assert.DoesNotContain(log.Warnings, w => w.Code == "JK_SUBMODEL_LIMIT");
    }

    [Fact]
    public void UnsupportedAlongsideRealBehavior_IsLoggedForTheLostPart()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, _, log) = Run(level, [new TouchDoor(1) { Cog = "mix.cog", Speed = 8f, Delay = 2f }, new Unsupported(1, "script also uses SetThingVel") { Cog = "mix.cog" }]);

        Assert.Single(Of(converted, "func_door"));   // la partie reproductible est conservee
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_UNSUPPORTED" && w.Message.Contains("SetThingVel") && w.Message.Contains("mix.cog"));
    }

    [Fact]
    public void OversizedModel_IsLoggedAsModelScale()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, _, log) = Run(level, [new TouchDoor(1) { Speed = 8f, Delay = 2f }], Md3() with { ModelScale = 2 });

        Assert.Single(Of(converted, "func_door"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_MODEL_SCALE" && w.Message.Contains("x2") && w.Message.Contains("models/jk/door.md3"));
    }

    [Fact]
    public void SwitchWithoutReturn_IsAToggleDoor()
    {
        // Bouton d'appel : envoie le mobile a l'image 1 sans retour automatique -> il bascule a chaque pression.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.85f));
        var (converted, _, _) = Run(level, [new SwitchMove(1, 0, [1]) { Delay = null }]);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal(8, int.Parse(door["spawnflags"]) & 8);   // TOGGLE
        Assert.Equal("jkm1", door["targetname"]);
    }

    [Fact]
    public void LiftWithCrossingAndButton_BothTriggerTheSameMover()
    {
        // 00_elev_switch donne TwoStopLift (passage) ET SwitchMove (bouton) pour le meme thing : un seul mobile, deux sources de declenchement.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.85f));
        var (converted, subs, _) = Run(level,
        [
            new TwoStopLift(1) { Cog = "00_elev_switch.cog", Speed = 4f, Delay = 2f },
            new SwitchMove(1, 0, [1, 0]) { Cog = "00_elev_switch.cog", Speed = 4f, Delay = 2f },
        ]);

        Assert.Single(Of(converted, "func_door"));
        Assert.Empty(Of(converted, "func_plat"));
        var triggers = Of(converted, "trigger_multiple");
        Assert.Equal(2, triggers.Count);
        Assert.All(triggers, t => Assert.Equal("jkm1", t["target"]));
        Assert.Contains(triggers, t => t["spawnflags"] == "4");   // bouton
        Assert.Contains(triggers, t => t["spawnflags"] == "0");   // passage / approche
        Assert.Equal(3, subs.Count);
    }

    [Fact]
    public void PureLiftGoingUp_IsAFuncPlat()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.85f));
        var (converted, _, _) = Run(level, [new TwoStopLift(1) { Speed = 4f, Delay = 2f }]);

        var plat = Assert.Single(Of(converted, "func_plat"));
        Assert.Empty(Of(converted, "func_door"));
        // func_plat : l'origine est la position haute, repos en bas de « height ».
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.85f * K)}", plat["origin"]);
        Assert.Equal(F(0.6f * K), plat["height"]);
    }

    [Fact]
    public void ThreeStepPath_TrainWithPathCorners()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f), Fr(1.9f, 0.5f, 0.55f));
        var (converted, subs, _) = Run(level, [new MultiStepPath(1, [0, 1, 2], 0.5f, true) { Speed = 5f, FrameSequence = [0, 1, 2] }]);

        var train = Assert.Single(Of(converted, "func_train"));
        Assert.Equal("*1", train["model"]);
        Assert.Equal("models/jk/door.md3", train["model2"]);
        Assert.Equal("jkm1", train["targetname"]);            // lance par le declencheur de proximite
        Assert.Equal("jkm1_c0", train["target"]);
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.25f * K)}", train["origin"]);

        var corners = Of(converted, "path_corner");
        Assert.Equal(3, corners.Count);
        Assert.Equal(["jkm1_c0", "jkm1_c1", "jkm1_c2"], corners.Select(c => c["targetname"]));
        Assert.Equal(["jkm1_c1", "jkm1_c2", "jkm1_c0"], corners.Select(c => c["target"]));   // boucle : retour a la position de repos
        Assert.Equal($"{F(1.9f * K)} {F(0.5f * K)} {F(0.55f * K)}", corners[2]["origin"]);
        Assert.All(corners, c => Assert.Equal("0.5", c["wait"]));

        var start = Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("jkm1", start["target"]);
        Assert.Equal("-1", start["wait"]);                    // une seule fois : relancer le train au milieu du trajet le fausserait
        Assert.Equal(2, subs.Count);
    }


    [Fact]
    public void LiftWithThreeStops_IsAStartOnTrainThatPausesAtEachStop()
    {
        // Ascenseur appele a trois etages par des boutons : un train ne se laisse pas appeler a un etage precis, il demarre seul et fait le tour.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f), Fr(1.5f, 0.5f, 0.85f));
        var (converted, subs, log) = Run(level,
        [
            new SwitchMove(1, 0, [1]) { Cog = "00_elevcall.cog", Speed = 4f, Delay = 2f },
            new SwitchMove(1, 2, [2]) { Cog = "00_elevcall.cog", Speed = 4f, Delay = 2f },
        ]);

        var train = Assert.Single(Of(converted, "func_train"));
        Assert.Equal("1", train["spawnflags"]);               // START_ON
        Assert.DoesNotContain("targetname", train.Keys);
        var corners = Of(converted, "path_corner");
        Assert.Equal(3, corners.Count);
        Assert.All(corners, c => Assert.Equal("2", c["wait"]));
        Assert.Equal("jkm1_c0", corners[2]["target"]);        // boucle
        Assert.Empty(Of(converted, "trigger_multiple"));
        Assert.Single(subs);
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_APPROX" && w.Message.Contains("00_elevcall.cog"));
    }

    [Fact]
    public void ThreeStepPath_WithoutLoop_LastCornerHasNoTarget()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f), Fr(1.9f, 0.5f, 0.55f));
        var (converted, _, _) = Run(level, [new MultiStepPath(1, [0, 1, 2], 0f, false)]);

        var corners = Of(converted, "path_corner");
        Assert.False(corners[2].ContainsKey("target"));
        Assert.DoesNotContain(corners, c => c.ContainsKey("wait"));
    }

    [Fact]
    public void TwoPositionMultiStepPath_IsADoor_NotATrain()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, _, _) = Run(level, [new MultiStepPath(1, [1, 0], 1f, true)]);

        Assert.Single(Of(converted, "func_door"));
        Assert.Empty(Of(converted, "func_train"));
    }

    [Fact]
    public void ActorDeathCog_BecomesProximityTrigger()
    {
        // La porte s'ouvrait a la mort d'un acteur (absent) : le joueur l'ouvre en approchant du secteur 1.
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, subs, log) = Run(level, [new ActorTriggered(1, 1) { Cog = "boss_door.cog", Speed = 4f }]);

        var door = Assert.Single(Of(converted, "func_door"));
        Assert.Equal("jkm1", door["targetname"]);
        Assert.Equal("-1", door["wait"]);                     // reste ouverte : le passage ne se referme pas
        var trigger = Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("jkm1", trigger["target"]);
        Assert.Equal("0", trigger["spawnflags"]);             // simple contact
        Assert.Equal("-1", trigger["wait"]);
        Assert.Equal(BspContents.Trigger, subs[1].Contents);

        // La boite couvre le secteur 1 (x de 1 a 2, y de 0 a 1, z de 0 a 0,5), agrandie.
        IReadOnlyList<Kotor.Compiler.Bsp.BspPlane> planes = subs[1].Brushes[0];
        float maxX = planes.Single(p => p.Normal == Vector3.UnitX).Dist, minX = -planes.Single(p => p.Normal == -Vector3.UnitX).Dist;
        Assert.True(minX <= 1f * K && maxX >= 2f * K, $"x range {minX}..{maxX}");
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_ACTOR_TRIGGER" && w.Message.Contains("boss_door.cog"));
    }

    [Fact]
    public void UnknownCog_KeepsThingVisible_OpenPose()
    {
        // Mouvement non reproduisible, le thing a deux images : il reste visible, fige dans la position la plus eloignee du repos (passage ouvert).
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.95f));
        var (converted, subs, log) = Run(level, [new Unsupported(1, "script moves it with SetThingVel") { Cog = "weird.cog" }]);

        var stat = Assert.Single(Of(converted, "func_static"));
        Assert.Equal("models/jk/door.md3", stat["model2"]);
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.95f * K)}", stat["origin"]);
        Assert.Single(subs);
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_UNSUPPORTED" && w.Message.Contains("weird.cog"));
        Assert.Contains(converted.Models, m => m.Path == "models/jk/door.md3");
    }

    [Fact]
    public void UnknownCog_ThingWithoutFrames_IsPlacedNonSolid()
    {
        var level = new SyntheticLevel { Sectors = 3, ExtraTemplates = ["door none type=cog model3d=door.3do"], ExtraThings = ["1: door door 1.5 0.5 0.25 0 0 0 1"] };
        var (converted, subs, log) = Run(level, [new Unsupported(1, "no frames") { Cog = "weird.cog" }]);

        var stat = Assert.Single(Of(converted, "func_static"));
        Assert.Equal($"{F(1.5f * K)} {F(0.5f * K)} {F(0.25f * K)}", stat["origin"]);
        Assert.NotEqual(BspContents.Solid, subs[0].Contents & BspContents.Solid);   // rien ne bloque le passage
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_UNSUPPORTED");
    }

    [Fact]
    public void Approximate_TwoFrames_IsADoorOnApproach_AndLogged()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.9f, 0.5f, 0.25f));
        var (converted, _, log) = Run(level, [new Approximate(1, "COG x moves it on user1") { Cog = "x.cog" }]);

        Assert.Single(Of(converted, "func_door"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_APPROX" && w.Message.Contains("x.cog"));
    }

    [Fact]
    public void Approximate_ManyFrames_IsATrainOnApproach()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.9f, 0.5f, 0.25f), Fr(1.9f, 0.5f, 0.55f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, _, log) = Run(level, [new Approximate(1, "COG x moves it with RotatePivot") { Cog = "x.cog" }]);

        Assert.Single(Of(converted, "func_train"));
        Assert.Equal(4, Of(converted, "path_corner").Count);
        Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_APPROX");
    }

    [Fact]
    public void RotatingFrames_StayInTheirOpenPoseAsAStaticModel()
    {
        // SWGL n'a pas de func_door_rotating (g_spawn.cpp) et un func_rotating arrete revient a son angle de depart :
        // une porte qui ne fait que tourner reste donc visible, dans la position de l'image qui laisse le passage ouvert, sans collision.
        var level = Level(Fr(1.5f, 0.5f, 0.25f, 0, 0, 0), Fr(1.5f, 0.5f, 0.25f, 0, 90, 0));
        var (converted, subs, log) = Run(level, [new TouchDoor(1) { Cog = "hatch.cog", Delay = 2f }]);

        var stat = Assert.Single(Of(converted, "func_static"));
        Assert.Equal("0 90 0", stat["modelAngles"]);
        Assert.Equal("models/jk/door.md3", stat["model2"]);
        Assert.Empty(Of(converted, "func_door"));
        Assert.NotEqual(BspContents.Solid, subs[0].Contents & BspContents.Solid);
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_APPROX" && w.Message.Contains("rotat"));
    }

    [Fact]
    public void MovementWithSmallRotation_TranslatesWithoutWarning_AndBigRotationIsLogged()
    {
        var small = Level(Fr(1.5f, 0.5f, 0.25f, 0, 0, 0), Fr(1.5f, 0.5f, 0.55f, 0, 0.8f, 0));
        var (c1, _, log1) = Run(small, [new TouchDoor(1)]);
        Assert.Single(Of(c1, "func_door"));
        Assert.DoesNotContain(log1.Warnings, w => w.Code == "JK_COG_APPROX");

        var big = Level(Fr(1.5f, 0.5f, 0.25f, 0, 0, 0), Fr(1.5f, 0.5f, 0.85f, 0, 45, 0));
        var (c2, _, log2) = Run(big, [new TouchDoor(1)]);
        Assert.Single(Of(c2, "func_door"));
        Assert.Contains(log2.Warnings, w => w.Code == "JK_COG_APPROX" && w.Message.Contains("rotation"));
    }

    [Fact]
    public void SurfaceCelSwitch_IsCountedNotDropped()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var (converted, _, log) = Run(level, [new SurfaceCelSwitch(0), new SurfaceCelSwitch(2), new TouchDoor(1)]);

        Assert.Single(Of(converted, "func_door"));
        var warn = Assert.Single(log.Warnings, w => w.Code == "JK_COG_CEL_STATIC");
        Assert.Contains("2", warn.Message);
    }

    [Fact]
    public void SeveralSwitchSurfaces_ShareOneRelayPerSurface()
    {
        // Deux pieces (things 1 et 2) commandees par la meme surface : un seul declencheur, un relais par piece.
        var level = new SyntheticLevel
        {
            Sectors = 3,
            ExtraTemplates = ["door none type=cog model3d=door.3do"],
            ExtraThings =
            [
                "1: door door 1.5 0.5 0.25 0 0 0 1 numframes=2 " + Fr(1.5f, 0.5f, 0.25f) + " " + Fr(1.5f, 0.5f, 0.55f),
                "2: door door 1.7 0.5 0.25 0 0 0 1 numframes=2 " + Fr(1.7f, 0.5f, 0.25f) + " " + Fr(1.7f, 0.5f, 0.55f),
            ],
        };
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, new ImportLog());
        var models = new Dictionary<int, JkMd3> { [1] = Md3(), [2] = Md3() };
        var (converted, subs) = MoverConverter.Convert([new SwitchMove(1, 0, [1, 0]) { Delay = 2f }, new SwitchMove(2, 0, [1, 0]) { Delay = 2f }], prepared, models, Scale, 1, log);

        Assert.Equal(2, Of(converted, "func_door").Count);
        var trigger = Assert.Single(Of(converted, "trigger_multiple"));
        Assert.Equal("jks0", trigger["target"]);
        var relays = Of(converted, "target_relay");
        Assert.Equal(2, relays.Count);
        Assert.All(relays, r => Assert.Equal("jks0", r["targetname"]));
        Assert.Equal(["jkm1", "jkm2"], relays.Select(r => r["target"]).Order());
        Assert.Equal(3, subs.Count);
    }

    [Fact]
    public void ThingWithoutModel_IsLoggedNotPlaced()
    {
        var level = Level(Fr(1.5f, 0.5f, 0.25f), Fr(1.5f, 0.5f, 0.55f));
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, new ImportLog());
        var (converted, subs) = MoverConverter.Convert([new TouchDoor(1)], prepared, new Dictionary<int, JkMd3>(), Scale, 1, log);

        Assert.Empty(converted.Entities);
        Assert.Empty(subs);
        Assert.Contains(log.Warnings, w => w.Code == "JK_MOVER_NO_MODEL");
    }

    // ---- branchement dans JkObjects ----

    private const string CubeDoor3do = """
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
        1: 0.3 0.0 0.0 0.0
        2: 0.3 0.2 0.0 0.0
        3: 0.0 0.2 0.0 0.0
        4: 0.0 0.0 0.3 0.0
        5: 0.3 0.0 0.3 0.0
        6: 0.3 0.2 0.3 0.0
        7: 0.0 0.2 0.3 0.0
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

    private const string DoorCog = """
        symbols
            message startup
            message activate
            message arrived
            message timer
            thing   door0 linkid=0
            flex    moveSpeed=8.0
            flex    sleepTime=2.0
        end
        code
        startup:
            return;
        activate:
            MoveToFrame(door0, 1, moveSpeed);
            return;
        arrived:
            if (GetCurFrame(door0) == 1) SetTimer(sleepTime);
            return;
        timer:
            MoveToFrame(door0, 0, moveSpeed);
            return;
        end
        """;

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

    [Fact]
    public void JkObjects_PlacesTheMover_NumbersItsSubModelAfterTheVolumes_AndDoesNotDuplicateIt()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            SectorFlags = new Dictionary<int, uint> { [0] = 0x2 }, // sous l'eau : un volume d'eau prend le sous-modele *1
            Cogs = ["00_door.cog 1"],
            CogScripts = new Dictionary<string, string> { ["00_door.cog"] = DoorCog },
            ExtraTemplates = ["door none type=cog model3d=cube.3do"],
            ExtraThings = ["1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes=2 " + Fr(1.5f, 0.5f, 0.25f) + " " + Fr(1.5f, 0.5f, 0.55f)],
        };
        string dir = Path.Combine(Path.GetTempPath(), "jkmovers_" + Guid.NewGuid().ToString("N"));
        _dirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        var files = new List<(string, byte[])>
        {
            (@"3do\cube.3do", Encoding.Latin1.GetBytes(CubeDoor3do)),
            (@"mat\fan.mat", SyntheticLevel.TexMat(8, 8, 10)),
            (@"misc\cmp\test.cmp", SyntheticLevel.Cmp()),
            (@"cog\00_door.cog", Encoding.Latin1.GetBytes(DoorCog)),
        };
        File.WriteAllBytes(Path.Combine(dir, "Episode", "JK1.GOB"), Gob([.. files]));
        using JkFileSystem fs = JkFileSystem.ForEpisode(dir, "JK1.GOB");
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, log);
        var opt = new ExportOptions("", Path.Combine(dir, "base"), dir, Game: KotorGame.JediKnight);

        var (converted, subs) = JkObjects.Convert(prepared, fs, opt, log);

        var door = Assert.Single(converted.Entities, x => x["classname"] == "func_door");
        Assert.Equal("*2", door["model"]);                    // le volume d'eau a pris *1
        Assert.Equal(2, subs.Count);
        Assert.Equal(BspContents.Water, subs[0].Contents);
        Assert.Equal(BspContents.Solid, subs[1].Contents);
        Assert.DoesNotContain(converted.Entities, x => x["classname"] == "misc_model_static"); // pas de doublon fixe du mobile
        Assert.Contains(converted.Models, m => m.Path == "models/jk/cube.md3");
        Assert.Equal("2", door["wait"]);                      // delai lu dans le COG (sleepTime)
    }


    private const string KilledDoorCog = """
        symbols
            message startup
            message killed
            message arrived
            message timer
            thing   door0 linkid=0
            flex    moveSpeed=8.0
        end
        code
        startup:
            return;
        killed:
            MoveToFrame(door0, 1, moveSpeed);
            return;
        end
        """;

    [Fact]
    public void FullChain_DoorAndActorTrigger_CompileToTheBsp()
    {
        var level = new SyntheticLevel
        {
            Sectors = 3,
            Cogs = ["boss_door.cog 1"],
            CogScripts = new Dictionary<string, string> { ["boss_door.cog"] = KilledDoorCog },
            ExtraTemplates = ["door none type=cog model3d=cube.3do"],
            ExtraThings = ["1: door door 1.5 0.5 0.25 0 0 0 1 thingflags=0x400448 numframes=2 " + Fr(1.5f, 0.5f, 0.25f) + " " + Fr(1.5f, 0.5f, 0.55f)],
        };
        string dir = Path.Combine(Path.GetTempPath(), "jkmoverchain_" + Guid.NewGuid().ToString("N"));
        _dirs.Add(dir);
        Directory.CreateDirectory(Path.Combine(dir, "Episode"));
        File.WriteAllBytes(Path.Combine(dir, "Episode", "JK1.GOB"), Gob(
            (@"3do\cube.3do", Encoding.Latin1.GetBytes(CubeDoor3do)), (@"mat\fan.mat", SyntheticLevel.TexMat(8, 8, 10)),
            (@"misc\cmp\test.cmp", SyntheticLevel.Cmp()), (@"cog\boss_door.cog", Encoding.Latin1.GetBytes(KilledDoorCog))));
        using JkFileSystem fs = JkFileSystem.ForEpisode(dir, "JK1.GOB");
        var log = new ImportLog();
        JkPreparedScene prepared = SyntheticLevel.Build(level, log);
        var opt = new ExportOptions("", Path.Combine(dir, "base"), dir, Game: KotorGame.JediKnight);
        Directory.CreateDirectory(opt.JaBaseDir);

        ImportStepResult imported = Kotor.Pipeline.PipelineSteps.Import(prepared.Scene, "jkmover", opt, log, JkObjects.ForPipeline(prepared, fs));
        ExportStepResult exported = Kotor.Pipeline.PipelineSteps.Export(imported, opt, log);

        Kotor.Compiler.Bsp.BspFile bsp = exported.Bsp;
        Assert.Equal(1 + 2, bsp.Models.Count); // monde + porte + declencheur
        int ContentsOf(string shader) => bsp.ShaderContents[bsp.Shaders.ToList().IndexOf(shader)];
        Assert.Equal(BspContents.Solid, ContentsOf(DoorConverter.ClipShader));
        Assert.Equal(BspContents.Trigger, ContentsOf("textures/jk/trigger_hurt"));
        Assert.Contains("\"classname\" \"func_door\"", bsp.Entities);
        Assert.Contains("\"classname\" \"trigger_multiple\"", bsp.Entities);
        Assert.Contains(log.Warnings, w => w.Code == "JK_COG_ACTOR_TRIGGER");
        Assert.True(File.Exists(Path.Combine(exported.StagingDir, "models", "jk", "cube.md3")));
    }

    // ---- donnees reelles ----

    [SkippableFact]
    public void RealLevels_AllMoversAreBuilt()
    {
        Skip.IfNot(Directory.Exists(JkDir), "Jedi Knight is not installed.");
        var perEntity = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var codes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int levels = 0, maxSubs = 0, totalMovers = 0, noModel = 0, unsupportedThings = 0, unsupportedLogged = 0;
        foreach (JkLevelRef lr in JkInstall.ListLevels(JkDir))
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, lr.EpisodeGob);
            var log = new ImportLog();
            JkPreparedScene prepared = JkSceneBuilder.Build(fs, lr, log);
            var opt = new ExportOptions("", "", Path.GetTempPath(), Game: KotorGame.JediKnight);
            var (converted, subs) = JkObjects.Convert(prepared, fs, opt, log);

            // Chaque thing mobile place avec un modele est represente par une entite de mouvement (ou un decor), jamais perdu.
            LevelMovers movers = CogClassifier.ClassifyLevel(prepared.Level, f => fs.TryRead(@"cog\" + f) is { } b ? Encoding.Latin1.GetString(b) : null);
            var thingModels = prepared.Level.Things.ToDictionary(t => t.Index, t => prepared.Templates.Resolve(t.Template).GetValueOrDefault("model3d"));
            foreach (int t in movers.Movers)
            {
                string? model = thingModels[t];
                if (string.IsNullOrWhiteSpace(model) || model.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
                string asset = "models/jk/" + Path.GetFileNameWithoutExtension(model).ToLowerInvariant() + ".md3";
                Assert.True(converted.Entities.Any(e => e.GetValueOrDefault("model2") == asset || e.GetValueOrDefault("model") == asset),
                    $"{lr.Name}: mover thing {t} ({model}) has no entity");
            }
            maxSubs = Math.Max(maxSubs, subs.Count);
            Assert.True(subs.Count <= JkObjects.MaxSubModels, $"{lr.Name}: {subs.Count} sub-models (MAX_SUBMODELS is 512)");
            Assert.DoesNotContain(log.Warnings, w => w.Code == "JK_SUBMODEL_LIMIT");

            // Bilan : chaque thing mobile place est soit construit (avec modele), soit un marqueur journalise JK_MOVER_NO_MODEL.
            totalMovers += movers.All.Where(b => b.Thing >= 0).Select(b => b.Thing).Distinct().Count();
            foreach (var w in log.Warnings.Where(w => w.Code == "JK_MOVER_NO_MODEL"))
                noModel += int.Parse(w.Message.Split(' ')[0], CultureInfo.InvariantCulture);
            // Chaque Unsupported (avec modele) doit apparaitre dans le journal, et JK_COG_UNSUPPORTED ne vient que d'eux.
            unsupportedThings += movers.All.OfType<Unsupported>().Where(b => b.Thing >= 0 && thingModels.TryGetValue(b.Thing, out string? m) && !string.IsNullOrWhiteSpace(m)
                && !m.Equals("none", StringComparison.OrdinalIgnoreCase)).Select(b => b.Thing).Distinct().Count();
            unsupportedLogged += log.Warnings.Count(w => w.Code == "JK_COG_UNSUPPORTED");
            foreach (var e in converted.Entities) perEntity[e["classname"]] = perEntity.GetValueOrDefault(e["classname"]) + 1;
            foreach (var w in log.Warnings.Where(w => w.Code.StartsWith("JK_COG", StringComparison.Ordinal) || w.Code.StartsWith("JK_MOVER", StringComparison.Ordinal)))
                codes[w.Code] = codes.GetValueOrDefault(w.Code) + 1;
            levels++;
        }
        Assert.Equal(34, levels);
        Assert.Equal(564, totalMovers);
        Assert.Equal(20, noModel);                       // marqueurs sans modele, journalises JK_MOVER_NO_MODEL
        Assert.Equal(544, totalMovers - noModel);        // tous les autres sont construits avec leur modele
        Assert.True(codes.GetValueOrDefault("JK_MOVER_NO_MODEL") > 0);
        Assert.True(codes.GetValueOrDefault("JK_MOVER_MODEL_SCALE") > 0, "oversized mover models are reported");
        Assert.Equal(unsupportedThings == 0, unsupportedLogged == 0);   // 0 aujourd'hui ; si le chemin est exerce, il est journalise
        Assert.True(unsupportedLogged >= unsupportedThings);
        Console.WriteLine(string.Join(", ", perEntity.Select(kv => $"{kv.Key} {kv.Value}")));
        Console.WriteLine($"max sub-models in a level: {maxSubs}");
        Console.WriteLine(string.Join(", ", codes.Select(kv => $"{kv.Key} x{kv.Value}")));
    }
}

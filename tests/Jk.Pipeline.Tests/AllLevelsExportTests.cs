using System.Diagnostics;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Pipeline;

namespace Jk.Pipeline.Tests;

/// <summary>
/// Passe complete sur les 34 niveaux du jeu installe (lourde : plusieurs minutes). Export vers un faux dossier <c>base</c> dans le dossier de travail
/// du test, jamais vers le <c>base</c> du jeu. Le bilan par niveau est ecrit dans <c>jk_t11_results.tsv</c> (dossier de sortie du test).
/// </summary>
[Trait("Category", "Slow")]
public class AllLevelsExportTests
{
    private const string JkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    [SkippableFact]
    public void AllLevels_ExportToStaging()
    {
        Skip.IfNot(File.Exists(Path.Combine(JkDir, "Episode", "JK1.GOB")), "Jedi Knight is not installed");
        string work = Path.Combine(AppContext.BaseDirectory, "work_all_" + Guid.NewGuid().ToString("N"));
        string jaBase = Path.Combine(work, "base");
        Directory.CreateDirectory(jaBase);   // faux dossier base : le vrai GameData\base n'est jamais touche
        var msgs = new StringBuilder();
        var tsv = new StringBuilder("level\tkind\tms\tentities\tsubmodels\ttriangles\tsurfaces\tclusters\tleaves\tleafsurfaces\tpk3_bytes\tcodes\terror\n");
        var failures = new List<string>();
        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        try
        {
            IReadOnlyList<JkLevelRef> levels = JkInstall.ListLevels(JkDir);
            Assert.Equal(34, levels.Count);
            foreach (JkLevelRef level in levels)
            {
                var sw = Stopwatch.StartNew();
                var log = new ImportLog();
                string name = JkWorkflowSteps.DefaultMapName(level.Name);
                string error = "";
                int entities = 0, subs = 0, tris = 0, surfs = 0, clusters = 0, leaves = 0, leafSurfs = 0;
                long pk3Bytes = 0;
                try
                {
                    ExportOptions opt = new(KotorDir: "", JaBaseDir: jaBase, WorkDir: Path.Combine(work, "w"), Game: KotorGame.JediKnight) { JediKnightDir = JkDir };
                    using JkFileSystem fs = JkFileSystem.ForEpisode(JkDir, level.EpisodeGob);
                    JkPreparedScene prepared = JkSceneBuilder.Build(fs, level, log);
                    ImportStepResult imported = PipelineSteps.Import(prepared.Scene, name, opt, log, JkObjects.ForPipeline(prepared, fs));
                    entities = imported.Plan.Input.Entities.Count;
                    subs = imported.Plan.Input.SubModels.Count;
                    ExportStepResult exported = PipelineSteps.Export(imported, opt, log);
                    BspFile bsp = exported.Bsp;
                    surfs = bsp.Surfaces.Count;
                    tris = (int)(bsp.Surfaces.Sum(s => (long)s.Indices.Length) / 3);
                    clusters = bsp.NumClusters;
                    leaves = bsp.Leaves.Count;
                    leafSurfs = bsp.LeafSurfaces.Count;
                    Pk3StepResult pk3 = PipelineSteps.CreatePk3(exported, imported, opt, log);
                    pk3Bytes = new FileInfo(pk3.Pk3Path).Length;
                    File.Delete(pk3.Pk3Path);
                    Directory.Delete(Path.Combine(opt.WorkDir, name), true);
                }
                catch (Exception ex) when (ex is PipelineStepException or InvalidDataException or IOException or BspLimitException)
                {
                    error = ex.GetType().Name + ": " + ex.Message.ReplaceLineEndings(" ");
                    failures.Add($"{level.Name}: {error}");
                }
                sw.Stop();
                var codes = log.Warnings.GroupBy(w => w.Code).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                foreach (var g in codes) totals[g.Key] = totals.GetValueOrDefault(g.Key) + g.Count();
                foreach (var g in codes) foreach (var w in g.Take(3)) msgs.Append($"{level.Name}\t{w.Code}\t{w.Message.ReplaceLineEndings(" ")}\n");
                string codeText = string.Join(" ", codes.Select(g => $"{g.Key}={g.Count()}"));
                tsv.Append($"{level.Name}\t{level.Kind}\t{sw.ElapsedMilliseconds}\t{entities}\t{subs}\t{tris}\t{surfs}\t{clusters}\t{leaves}\t{leafSurfs}\t{pk3Bytes}\t{codeText}\t{error}\n");
            }
            tsv.Append("# totals\t" + string.Join(" ", totals.Select(t => $"{t.Key}={t.Value}")) + "\n");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jk_t11_results.tsv"), tsv.ToString());
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jk_t11_messages.tsv"), msgs.ToString());

            Assert.Empty(failures);
            Assert.False(totals.ContainsKey("JK_COG_UNSUPPORTED"), "JK_COG_UNSUPPORTED must be 0");
            Assert.False(totals.ContainsKey("TEXTURE_MISSING"), "no material may be missing");
        }
        finally
        {
            try { Directory.Delete(work, true); } catch (IOException) { }
        }
    }
}

using System.CommandLine;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;
using Jk.Pipeline.Movers;

namespace Kotor2Ja.Cli.Commands;

/// <summary>jk-dump-level : affiche les comptes d'un niveau Jedi Knight (sommets, surfaces, secteurs, things par type resolu, COG par fichier).</summary>
public static class JkDumpLevelCommand
{
    public const string DefaultJkDir = @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight";

    public static Command Create()
    {
        var levelArg = new Argument<string>("level") { Description = "Level name without extension (e.g. 01narshadda, m10, c1)." };
        var jkOption = new Option<string?>("--jk")
        {
            Description = "Jedi Knight install folder (default: JK_DIR, then JK_PATH, then the Steam path).",
        };
        var cmd = new Command("jk-dump-level", "Dumps the counts of a Jedi Knight level: vertices, surfaces, sectors, things by resolved type, COGs by file.");
        cmd.Arguments.Add(levelArg);
        cmd.Options.Add(jkOption);
        cmd.SetAction(pr => Run(pr.GetValue(levelArg)!, pr.GetValue(jkOption), Console.Out, Console.Error));
        return cmd;
    }

    /// <summary>Dossier JK : l'option --jk, puis JK_DIR (comme le reste de l'outil), puis JK_PATH (ancien nom), puis le chemin Steam par defaut.</summary>
    public static string ResolveDir(string? fromOption, Func<string, string?> getEnv)
    {
        if (!string.IsNullOrWhiteSpace(fromOption)) return fromOption;
        foreach (string name in new[] { "JK_DIR", "JK_PATH" })
            if (getEnv(name) is { } v && !string.IsNullOrWhiteSpace(v)) return v;
        return DefaultJkDir;
    }

    public static int Run(string levelName, string? jkDir, TextWriter output, TextWriter error)
    {
        jkDir = ResolveDir(jkDir, Environment.GetEnvironmentVariable);

        JkLevelRef? level = JkInstall.ListLevels(jkDir)
            .FirstOrDefault(l => string.Equals(l.Name, levelName, StringComparison.OrdinalIgnoreCase));
        if (level is null)
        {
            error.WriteLine($"Jedi Knight level '{levelName}' not found in {jkDir} (use a .jkl name such as 01narshadda, m10 or c1).");
            return 2;
        }

        JklLevel jkl;
        JklLevel? staticLevel = null;
        try
        {
            using JkFileSystem fs = JkFileSystem.ForEpisode(jkDir, level.EpisodeGob);
            byte[]? data = fs.TryRead($@"jkl\{level.Name}.jkl");
            if (data is null)
            {
                error.WriteLine($"{level.Name}.jkl is missing from {level.EpisodeGob}.");
                return 2;
            }
            jkl = JklReader.Read(Encoding.Latin1.GetString(data));
            byte[]? st = fs.TryRead(@"jkl\static.jkl");
            if (st is not null) staticLevel = JklReader.Read(Encoding.Latin1.GetString(st));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            error.WriteLine("Cannot read the level: " + ex.Message);
            return 1;
        }

        var templates = JkTemplates.FromLevels(staticLevel, jkl);
        output.WriteLine($"Level: {level.Name} ({level.Kind}, {level.EpisodeGob})");
        output.WriteLine($"Vertices: {jkl.Vertices.Length}, texture vertices: {jkl.TexVertices.Length}, adjoins: {jkl.Adjoins.Count}");
        output.WriteLine($"Surfaces: {jkl.Surfaces.Count}, sectors: {jkl.Sectors.Count}");
        output.WriteLine($"Materials: {jkl.Materials.Count}, colormaps: {string.Join(", ", jkl.Colormaps)}, models: {jkl.Models.Count}, sounds: {jkl.Sounds.Count}");
        output.WriteLine($"Things: {jkl.Things.Count}, templates: {jkl.Templates.Count}, COGs: {jkl.Cogs.Count}");

        output.WriteLine("Things by resolved type:");
        foreach (var g in jkl.Things
                     .GroupBy(t => templates.Resolve(t.Template).TryGetValue("type", out string? ty) ? ty : "(none)")
                     .OrderByDescending(g => g.Count()).ThenBy(g => g.Key))
            output.WriteLine($"  {g.Key,-12} {g.Count()}");

        output.WriteLine("COGs by file:");
        foreach (var g in jkl.Cogs.GroupBy(c => c.CogFile, StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
            output.WriteLine($"  {g.Key,-28} {g.Count()}");

        WriteCogBehaviors(jkl, level, jkDir, output);

        if (templates.Unresolved.Count > 0) output.WriteLine("JK_TEMPLATE_UNRESOLVED: " + string.Join(", ", templates.Unresolved));
        if (jkl.SkippedSections.Count > 0) output.WriteLine("Skipped unknown sections: " + string.Join(", ", jkl.SkippedSections));
        return 0;
    }

    /// <summary>Liste des COG avec le comportement reconnu (tache 8) : un bloc par instance, puis le bilan.</summary>
    private static void WriteCogBehaviors(JklLevel jkl, JkLevelRef level, string jkDir, TextWriter output)
    {
        using JkFileSystem fs = JkFileSystem.ForEpisode(jkDir, level.EpisodeGob);
        LevelMovers report = CogClassifier.ClassifyLevel(jkl, f => fs.TryRead(@"cog\" + f) is { } b ? Encoding.Latin1.GetString(b) : null);
        output.WriteLine("COG behaviors:");
        foreach (ClassifiedCog c in report.Cogs)
        {
            string text = c.Behaviors.Count == 0 ? "no mover" : string.Join("; ", c.Behaviors.Select(b => b.Describe()));
            output.WriteLine($"  #{c.Instance.Index} {c.Instance.CogFile}: {text}");
        }
        foreach (MoverBehavior b in report.Unclaimed) output.WriteLine("  (no COG) " + b.Describe());
        if (report.MissingScripts.Count > 0) output.WriteLine("JK_COG_MISSING: " + string.Join(", ", report.MissingScripts));
        foreach (var (file, warnings) in report.MalformedScripts) output.WriteLine($"JK_COG_MALFORMED: {file}: {string.Join("; ", warnings)}");
        output.WriteLine($"Movers: {report.Movers.Count} (approximate {report.All.Count(b => b is Approximate)}, unsupported {report.All.Count(b => b is Unsupported)})");
    }
}

using System.CommandLine;
using System.Text;
using Jk.Core;
using Jk.Core.Gob;
using Jk.Core.Jkl;

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
            Description = "Jedi Knight install folder (default: JK_PATH, then the Steam path).",
        };
        var cmd = new Command("jk-dump-level", "Dumps the counts of a Jedi Knight level: vertices, surfaces, sectors, things by resolved type, COGs by file.");
        cmd.Arguments.Add(levelArg);
        cmd.Options.Add(jkOption);
        cmd.SetAction(pr => Run(pr.GetValue(levelArg)!, pr.GetValue(jkOption), Console.Out, Console.Error));
        return cmd;
    }

    public static int Run(string levelName, string? jkDir, TextWriter output, TextWriter error)
    {
        jkDir ??= Environment.GetEnvironmentVariable("JK_PATH");
        if (string.IsNullOrWhiteSpace(jkDir)) jkDir = DefaultJkDir;

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

        if (templates.Unresolved.Count > 0) output.WriteLine("JK_TEMPLATE_UNRESOLVED: " + string.Join(", ", templates.Unresolved));
        if (jkl.SkippedSections.Count > 0) output.WriteLine("Skipped unknown sections: " + string.Join(", ", jkl.SkippedSections));
        return 0;
    }
}

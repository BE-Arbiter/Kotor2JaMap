using Kotor.Compiler.Bsp;
using Kotor.Core;
using Kotor.Core.Archives;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Scene;
using Kotor.Core.Textures;
using Kotor.Core.Tlk;

namespace Kotor.Pipeline;

/// <summary>Les quatre etapes (Previsualiser, Importer, Exporter, Creer PK3), utilisables separement par la CLI et l'application.</summary>
public static class PipelineSteps
{
    /// <summary>Charge le module KOTOR installe (resolveur, TLK, scene). Echec : <c>MODULE_UNREADABLE</c>.</summary>
    public static ModuleScene Preview(string module, ExportOptions opt, ImportLog log)
    {
        ArgumentException.ThrowIfNullOrEmpty(module);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(log);
        module = module.ToLowerInvariant();
        try
        {
            TlkFile? tlk = null;
            string tlkPath = Path.Combine(opt.KotorDir, "dialog.tlk");
            if (File.Exists(tlkPath))
            {
                try { tlk = TlkFile.Open(tlkPath); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    log.Warn("TLK_UNREADABLE", $"dialog.tlk illisible ({ex.Message}), noms de module non traduits.");
                }
            }
            using var res = ResourceResolver.ForModule(opt.KotorDir, module, opt.Game);
            return ModuleSceneBuilder.Build(res, module, tlk, log);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            log.Warn("MODULE_UNREADABLE", ex.Message);
            throw new PipelineStepException("MODULE_UNREADABLE", $"Module illisible : {ex.Message}");
        }
    }

    /// <summary>Convertit la scene en plan d'export (unites JA, shaders, textures). N'ecrit rien.</summary>
    public static ImportStepResult Import(ModuleScene scene, string? mapName, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(opt);
        if (opt.Validate() is { } invalid)
        {
            log.Warn("OPTIONS_INVALID", invalid);
            throw new PipelineStepException("OPTIONS_INVALID", invalid);
        }
        string name = MapNames.Normalize(mapName ?? scene.Info.Module);
        if (MapNames.Validate(name) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);
        return new ImportStepResult(scene, ModuleExporter.BuildPlan(scene, mapName, opt, log), name);
    }

    /// <summary>Ecrit shader et textures dans le staging, compile le .bsp puis le relit.</summary>
    public static ExportStepResult Export(ImportStepResult imported, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(opt);
        ExportPlan plan = imported.Plan;
        string name = imported.MapName;
        if (MapNames.Validate(name) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);

        if (plan.Shaders.Count > ModuleExporter.MaxShaders)
            throw Fail(log, "SHADER_LIMIT", $"{plan.Shaders.Count} shaders distincts : depasse MAX_MAP_SHADERS ({ModuleExporter.MaxShaders}).");
        RequireJaBaseDir(opt, log);

        try
        {
            string stage = Path.Combine(opt.WorkDir, name, "pk3");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);

            string bsp = Path.Combine(stage, "maps", name + ".bsp");
            var cr = global::Kotor.Compiler.Compiler.Compile(plan.Input, bsp, Limits.FromName(opt.BspLimits), log);
            if (!cr.Ok) throw new PipelineStepException("BSP_ERROR", cr.Error ?? "Echec de compilation."); // deja journalise par le compilateur

            Directory.CreateDirectory(Path.Combine(stage, "shaders")); // le moteur SWGL ne liste que shaders/*.shader (tr_shader.cpp)
            File.WriteAllText(Path.Combine(stage, "shaders", KotorGames.MapPrefix(opt.Game) + "_" + name + ".shader"),
                string.Join("\n", plan.Shaders.Select(s => ShaderPlanner.Write(s, opt.Game))));

            foreach (ExportTexture t in plan.Textures)
            {
                string path = Path.Combine(stage, t.RelPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var fs = File.Create(path);
                if (t.Texture is null) TgaWriter.Write(fs, 8, 8, ModuleExporter.Checker());
                else TgaWriter.Write(fs, t.Texture.Width, t.Texture.Height,
                    t.Lightmap ? ModuleExporter.Brighten(t.Texture.Rgba, opt.LightmapBrightness) : t.Texture.Rgba);
            }

            return new ExportStepResult(bsp, ReadBack(File.ReadAllBytes(bsp), plan, log), stage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>Relit le .bsp ecrit et verifie qu'il correspond au plan (au moins une surface, meme nombre total d'index).</summary>
    internal static BspFile ReadBack(byte[] bspBytes, ExportPlan plan, ImportLog log)
    {
        BspFile parsed;
        try { parsed = BspReader.Read(bspBytes); }
        catch (InvalidDataException ex) { throw Fail(log, "BSP_READBACK_FAILED", $"Relecture du .bsp impossible : {ex.Message}"); }

        long planned = plan.Input.Surfaces.Sum(s => (long)s.Indices.Length);
        long read = parsed.Surfaces.Sum(s => (long)s.Indices.Length);
        // Le compilateur ajoute la boite de ciel (SkyBox.IndexCount index) a la geometrie du plan, sans rien retirer.
        if (parsed.Surfaces.Count == 0 || (read != planned && read != planned + SkyBox.IndexCount))
            throw Fail(log, "BSP_READBACK_FAILED",
                $"Le .bsp relu ne correspond pas au plan : {parsed.Surfaces.Count} surface(s), {read} index relus pour {planned} prevus (+{SkyBox.IndexCount} de ciel).");
        return parsed;
    }

    /// <summary>Zippe le staging en <c>&lt;JaBaseDir&gt;\&lt;prefix&gt;_&lt;carte&gt;.pk3</c> (le pk3 existant n'est remplace que si tout reussit).</summary>
    public static Pk3StepResult CreatePk3(ExportStepResult exported, ImportStepResult imported, ExportOptions opt, ImportLog log)
    {
        ArgumentNullException.ThrowIfNull(exported);
        ArgumentNullException.ThrowIfNull(imported);
        ArgumentNullException.ThrowIfNull(opt);
        if (MapNames.Validate(imported.MapName) is { } badName) throw Fail(log, "MAP_NAME_INVALID", badName);
        RequireJaBaseDir(opt, log);
        try
        {
            string pk3 = Path.Combine(opt.JaBaseDir, KotorGames.MapPrefix(opt.Game) + "_" + imported.MapName + ".pk3");
            Pk3Builder.Build(exported.StagingDir, pk3);
            return new Pk3StepResult(pk3);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw Fail(log, "EXPORT_IO_ERROR", ex.Message);
        }
    }

    /// <summary>Le dossier <c>base</c> du jeu doit exister : on ne cree jamais d'arborescence fantome.</summary>
    private static void RequireJaBaseDir(ExportOptions opt, ImportLog log)
    {
        if (!Directory.Exists(opt.JaBaseDir))
            throw Fail(log, "JA_BASE_MISSING", $"Dossier base de Jedi Academy introuvable : {opt.JaBaseDir}");
    }

    private static PipelineStepException Fail(ImportLog log, string code, string message)
    {
        log.Warn(code, message);
        return new PipelineStepException(code, message);
    }
}

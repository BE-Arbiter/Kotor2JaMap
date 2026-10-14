using Kotor.Compiler.Bsp;
using Kotor.Core.Scene;

namespace Kotor.Pipeline;

/// <summary>Resultat de l'etape Importer : scene, plan d'export (rien n'est ecrit) et nom de carte (minuscules).</summary>
public sealed record ImportStepResult(ModuleScene Scene, ExportPlan Plan, string MapName);

/// <summary>Resultat de l'etape Exporter : .bsp compile, relu, et dossier de preparation (staging) du pk3.</summary>
public sealed record ExportStepResult(string BspPath, BspFile Bsp, string StagingDir);

public sealed record Pk3StepResult(string Pk3Path);

/// <summary>Echec d'une etape. <see cref="Code"/> est deja ecrit au journal.</summary>
public sealed class PipelineStepException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

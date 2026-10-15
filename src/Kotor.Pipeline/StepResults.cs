using Kotor.Compiler.Bsp;
using Kotor.Core.Scene;
using Kotor.Pipeline.Entities;

namespace Kotor.Pipeline;

/// <summary>Resultat de l etape Importer : scene, plan d export (entites et collision des objets deja inclus ; rien n est ecrit), nom de carte (minuscules) et objets convertis (MD3).</summary>
public sealed record ImportStepResult(ModuleScene Scene, ExportPlan Plan, string MapName, ConvertedEntities? Converted = null);

/// <summary>Resultat de l'etape Exporter : .bsp compile, relu, et dossier de preparation (staging) du pk3.</summary>
public sealed record ExportStepResult(string BspPath, BspFile Bsp, string StagingDir);

public sealed record Pk3StepResult(string Pk3Path);

/// <summary>Echec d'une etape. <see cref="Code"/> est deja ecrit au journal.</summary>
public sealed class PipelineStepException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

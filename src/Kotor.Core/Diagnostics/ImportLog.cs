namespace Kotor.Core.Diagnostics;

/// <summary>Journal d'avertissements avec codes stables (missing-file, bad-xml, marker-no-actor, actor-no-gto, bad-mesh, skipped-markerset...).</summary>
public sealed class ImportLog
{
    private readonly List<(string Code, string Message)> _warnings = new();

    public void Warn(string code, string message) => _warnings.Add((code, message));

    public IReadOnlyList<(string Code, string Message)> Warnings => _warnings;
}

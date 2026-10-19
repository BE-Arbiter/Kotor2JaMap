namespace Kotor.Core.Diagnostics;

/// <summary>
/// Journal d'avertissements avec codes stables (missing-file, bad-xml, marker-no-actor, actor-no-gto, bad-mesh, skipped-markerset...).
/// Porte aussi, pour les longues constructions (cartes SWTOR), un canal d'information (<see cref="Info"/>) et un canal d'avancement
/// (<see cref="Progress"/>) que l'appelant branche sur le journal et la barre de progression de l'application.
/// </summary>
public sealed class ImportLog
{
    private readonly List<(string Code, string Message)> _warnings = new();

    public void Warn(string code, string message) => _warnings.Add((code, message));

    public IReadOnlyList<(string Code, string Message)> Warnings => _warnings;

    /// <summary>Destinataire des messages d'information (une ligne par evenement) ; null = ignores.</summary>
    public Action<string>? InfoSink { get; set; }

    /// <summary>Destinataire de l'avancement : fraction 0..1 de l'etape en cours et son libelle ; null = ignore.</summary>
    public Action<double, string>? ProgressSink { get; set; }

    /// <summary>Message d'avancement (pas un avertissement : n'apparait pas dans <see cref="Warnings"/>).</summary>
    public void Info(string message) => InfoSink?.Invoke(message);

    /// <summary>Avancement de l'etape en cours (<paramref name="fraction"/> bornee a 0..1).</summary>
    public void Progress(double fraction, string label) => ProgressSink?.Invoke(Math.Clamp(fraction, 0d, 1d), label);
}

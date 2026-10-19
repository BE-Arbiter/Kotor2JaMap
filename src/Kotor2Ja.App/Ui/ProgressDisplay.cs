using System.Numerics;
using ImGuiNET;

namespace Kotor2Ja.App.Ui;

/// <summary>Barre de progression + libelle, pour les etapes longues (chargement d'une grande carte) : evite l'impression d'un gel.</summary>
internal static class ProgressDisplay
{
    /// <summary>
    /// <paramref name="fraction"/> 0..1 : barre avec pourcentage ; negative : barre animee (duree inconnue). Le libelle est le detail de l'etape en cours.
    /// </summary>
    public static void Draw(double fraction, string label)
    {
        if (fraction < 0d) ImGui.ProgressBar(-1f * (float)ImGui.GetTime(), new Vector2(-1, 0), "Working...");
        else ImGui.ProgressBar((float)Math.Min(fraction, 1d), new Vector2(-1, 0), $"{fraction * 100d:0}%");
        if (label.Length > 0) ImGui.TextWrapped(label);
    }
}

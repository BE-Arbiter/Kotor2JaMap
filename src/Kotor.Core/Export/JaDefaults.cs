using System.Numerics;

namespace Kotor.Core.Export;

/// <summary>Valeurs par defaut partagees entre l'export et le compilateur (minimum repris du projet TFU).</summary>
public static class JaDefaults
{
    /// <summary>Shader de ciel par defaut.</summary>
    public const string SkyShader = "textures/skies/hevil";

    /// <summary>Direction de propagation du soleil (axes Quake).</summary>
    public static readonly Vector3 SunDir = new(-0.4f, -0.3f, -1f);

    /// <summary>Intensite du soleil.</summary>
    public const double Sun = 0.75;

    /// <summary>Intensite de l'ambiante.</summary>
    public const double Ambient = 0.45;

    /// <summary>Teinte RGB du soleil.</summary>
    public static readonly Vector3 SunColor = new(0.654f, 1f, 1f);

    /// <summary>Teinte RGB de l'ambiante.</summary>
    public static readonly Vector3 AmbientColor = new(0.80f, 0.90f, 1.0f);
}

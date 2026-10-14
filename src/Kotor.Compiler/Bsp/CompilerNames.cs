// Licence : GPL-2.0, voir THIRD-PARTY-NOTICES.md.
namespace Kotor.Compiler.Bsp;

/// <summary>Littéraux partagés par le compilateur : shader de collision et clés du worldspawn.</summary>
public static class CompilerNames
{
    /// <summary>Shader (non rendu, solide) porté par les côtés de brush de collision.</summary>
    public const string CollisionShader = "textures/kotor/collision";

    public const string SkyKey = "_kotor_sky";
    public const string FogKey = "_kotor_fog";
    public const string SunDirKey = "_kotor_sun_dir";
    public const string SunKey = "_kotor_sun";
    public const string AmbientKey = "_kotor_ambient";
}

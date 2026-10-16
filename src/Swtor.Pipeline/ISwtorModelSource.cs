using Swtor.Core;
using Swtor.Core.Dds;
using Swtor.Core.Gr2;
using Swtor.Core.Xml;

namespace Swtor.Pipeline;

/// <summary>Acces aux ressources de rendu SWTOR (modeles, materiaux, textures) : abstrait pour tester le builder sans le jeu.</summary>
public interface ISwtorModelSource
{
    /// <summary>Modele .gr2 ou null s'il est absent. Une erreur de format leve <see cref="GameFormatException"/>.</summary>
    Gr2Model? LoadModel(string resourcePath);

    /// <summary>Materiau <c>art/shaders/materials/&lt;nom&gt;.mat</c> ou null s'il est absent.</summary>
    MaterialDef? LoadMaterial(string materialName);

    /// <summary>Image DDS (chemin avec ou sans extension) decodee en RGBA, ou null si absente. Format non gere : <see cref="GameFormatException"/>.</summary>
    DdsImage? LoadTexture(string resourcePath);
}

/// <summary>Source reelle : dossier de ressources extraites (<see cref="SwtorAssets"/>).</summary>
public sealed class SwtorAssetsModelSource(SwtorAssets assets) : ISwtorModelSource
{
    public Gr2Model? LoadModel(string resourcePath) =>
        assets.ReadBytes(resourcePath) is { } b ? Gr2Reader.Parse(b) : null;

    public MaterialDef? LoadMaterial(string materialName) =>
        assets.ReadBytes("art/shaders/materials/" + materialName + ".mat") is { } b
            ? MaterialReader.Parse(System.Text.Encoding.UTF8.GetString(b))
            : null;

    public DdsImage? LoadTexture(string resourcePath)
    {
        string p = resourcePath.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? resourcePath : resourcePath + ".dds";
        return assets.ReadBytes(p) is { } b ? DdsReader.Decode(b) : null;
    }
}

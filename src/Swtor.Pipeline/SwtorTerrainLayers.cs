using System.Numerics;
using Kotor.Core;
using Kotor.Core.Diagnostics;
using Kotor.Core.Export;
using Kotor.Core.Textures;
using Swtor.Core.Terrain;

namespace Swtor.Pipeline;

/// <summary>
/// Couches de sol d'une zone : les identifiants des emplacements de la carte de melange des dalles (<see cref="HeightmapTile.Layers"/>) resolus par la
/// table d'area.dat (<c>AreaDat.TerrainLayers</c>) en materiaux <c>.mat</c> ordinaires (<c>ter_tyt_grass_01</c>, <c>ter_rep_metal_gray01</c>...).
/// L'indice 0 est reserve au sol de repli (dalles sans carte, couches introuvables). Chaque couche a sa propre texture (cle <c>tl_&lt;materiau&gt;</c>),
/// copie de celle du materiau, pour pouvoir l'eclaircir sans toucher aux modeles qui partageraient le materiau.
/// </summary>
internal sealed class SwtorTerrainLayers(SwtorMaterials materials, IReadOnlyDictionary<ulong, string> names, ImportLog log)
{
    public const int Fallback = 0;

    private readonly List<string> _keys = ["(fallback)"];
    private readonly List<string> _materials = ["(fallback)"];
    private readonly List<Vector2> _scales = [Vector2.One];
    private readonly Dictionary<string, int> _byMaterial = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ulong> _unknownIds = [];

    /// <summary>Couches resolues, repli compris.</summary>
    public int Count => _keys.Count;

    /// <summary>Cle de texture de scene de la couche.</summary>
    public string Key(int layer) => _keys[layer];

    /// <summary>Nom du materiau SWTOR de la couche.</summary>
    public string Material(int layer) => _materials[layer];

    /// <summary>Repetition de la couche (<c>UvScaling</c> du materiau).</summary>
    public Vector2 Scale(int layer) => _scales[layer];

    /// <summary>Couche de chaque emplacement de la carte de melange de la dalle (-1 : vide, inconnu ou sans texture) ; null sans carte.</summary>
    public int[]? Slots(HeightmapTile tile)
    {
        if (tile.BlendWeights is null) return null;
        var slots = new int[tile.Layers.Length];
        for (int s = 0; s < slots.Length; s++)
        {
            byte id = tile.Layers[s];
            if (id == 0xFF) { slots[s] = -1; continue; }
            if (!names.TryGetValue(id, out string? name)) { _unknownIds.Add(id); slots[s] = -1; continue; }
            slots[s] = Resolve(name);
        }
        return slots;
    }

    private int Resolve(string name)
    {
        if (_byMaterial.TryGetValue(name, out int known)) return known;
        int index = -1;
        SwtorMaterialInfo info = materials.Resolve(name);
        if (!info.Hidden && materials.Textures.TryGetValue(info.Key, out KotorTexture? tex) && tex is not null)
        {
            string key = ShaderNames.Short("tl_" + info.Key, KotorGame.Swtor);
            materials.Textures[key] = tex with { Rgba = (byte[])tex.Rgba.Clone(), HasAlpha = false };
            index = _keys.Count;
            _keys.Add(key);
            _materials.Add(name);
            _scales.Add(info.UvScale);
        }
        else log.Warn("SWTOR_TERRAIN_LAYER", $"Ground layer '{name}' has no usable texture: its share of the blend map goes to the other layers.");
        _byMaterial[name] = index;
        return index;
    }

    /// <summary>
    /// Les textures de couche sont sombres (moyenne ~70/255 : le jeu les module par la carte de teinte, non decodee) : un meme gain pour toutes
    /// (contraste entre couches garde) ramene leur moyenne vers ~110, au plus x1,8. Journalise aussi les identifiants absents de la table.
    /// </summary>
    public void Finish()
    {
        if (_unknownIds.Count > 0)
            log.Warn("SWTOR_TERRAIN_LAYER", $"{_unknownIds.Count} ground layer id(s) missing from the area layer table ({string.Join(", ", _unknownIds.Order().Select(i => "0x" + i.ToString("X2")))}): ignored.");
        if (_keys.Count <= 1) return;
        double meanSum = 0;
        for (int l = 1; l < _keys.Count; l++) meanSum += Mean(materials.Textures[_keys[l]]!.Rgba);
        double mean = meanSum / (_keys.Count - 1);
        double gain = mean > 1 ? Math.Clamp(110.0 / mean, 1.0, 1.8) : 1.0;
        for (int l = 1; l < _keys.Count; l++)
        {
            byte[] rgba = materials.Textures[_keys[l]]!.Rgba;
            for (int i = 0; i + 3 < rgba.Length; i += 4)
            {
                for (int c = 0; c < 3; c++) rgba[i + c] = (byte)Math.Min(255, (int)(rgba[i + c] * gain));
                rgba[i + 3] = 255;
            }
        }
        log.Info($"Terrain: {_keys.Count - 1} ground layer(s) from the blend map ({string.Join(", ", _materials.Skip(1))}), brightness x{gain:0.00}.");
    }

    private static double Mean(byte[] rgba)
    {
        long sum = 0;
        for (int i = 0; i + 2 < rgba.Length; i += 4) sum += rgba[i] + rgba[i + 1] + rgba[i + 2];
        return rgba.Length < 4 ? 0 : sum / (3.0 * (rgba.Length / 4));
    }
}

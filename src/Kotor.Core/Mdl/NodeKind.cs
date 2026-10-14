namespace Kotor.Core.Mdl;

/// <summary>Drapeaux de type de noeud MDL (xoreos model_kotor.cpp kNodeFlag*, KotorBlender types.py NODE_*).</summary>
[Flags]
public enum NodeKind : ushort
{
    Header = 0x001, Light = 0x002, Emitter = 0x004, Camera = 0x008, Reference = 0x010,
    Mesh = 0x020, Skin = 0x040, Anim = 0x080, Dangly = 0x100, Aabb = 0x200, Saber = 0x800,
}

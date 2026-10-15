using System.Numerics;
using Kotor.Core.Archives;
using Kotor.Core.Gff;
using Kotor.Core.Tlk;

namespace Kotor.Core.Modules;

public static class ModuleCatalog
{
    /// <summary>Modules (modules\*.rim hors *_s.rim), en minuscules, tries.</summary>
    public static IReadOnlyList<string> ListModuleNames(string kotorDir)
    {
        string dir = Path.Combine(kotorDir, "modules");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        // A module is a .rim (its _s.rim companion is not a module) or a .mod, which replaces the .rim of the same name.
        return Directory.EnumerateFiles(dir, "*.rim").Concat(Directory.EnumerateFiles(dir, "*.mod"))
            .Where(p => Path.GetExtension(p).Equals(".rim", StringComparison.OrdinalIgnoreCase)
                     || Path.GetExtension(p).Equals(".mod", StringComparison.OrdinalIgnoreCase))
            .Select(p => Path.GetFileNameWithoutExtension(p).ToLowerInvariant())
            .Where(n => !n.EndsWith("_s", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public static ModuleInfo Load(ResourceResolver res, string module, TlkFile? tlk)
    {
        byte[] ifoData = res.Find("module", ResourceType.Ifo)
            ?? throw new InvalidDataException($"module.ifo introuvable pour {module}.");
        var ifo = GffReader.Read(ifoData, out _);
        string area = (ifo.GetList("Mod_Area_list").FirstOrDefault()?.Get<string>("Area_Name") ?? "").ToLowerInvariant();
        if (area.Length == 0) throw new InvalidDataException($"Mod_Area_list vide dans {module}.");

        var entry = new ModuleEntry(
            new Vector3(ifo.Get<float>("Mod_Entry_X"), ifo.Get<float>("Mod_Entry_Y"), ifo.Get<float>("Mod_Entry_Z")),
            new Vector2(ifo.Get<float>("Mod_Entry_Dir_X"), ifo.Get<float>("Mod_Entry_Dir_Y")));

        string name = area;
        byte[]? areData = res.Find(area, ResourceType.Are);
        if (areData is not null)
        {
            var loc = GffReader.Read(areData, out _).Get<GffLocString>("Name");
            if (loc is not null)
            {
                string? text = tlk?.Get(loc.StrRef);
                if (string.IsNullOrWhiteSpace(text))
                    text = loc.Strings.OrderBy(k => k.Key).Select(k => k.Value).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                if (!string.IsNullOrWhiteSpace(text)) name = text;
            }
        }
        return new ModuleInfo(module, area, name, entry);
    }
}

namespace Kotor.Core.Archives;

/// <summary>Types de ressources Aurora. Une valeur non nommee reste valide (valeur numerique brute).</summary>
public enum ResourceType : ushort
{
    Tga = 3,
    Wav = 4,
    TwoDa = 2017,
    Mdl = 2002,
    Are = 2012,
    Ifo = 2014,
    Wok = 2016,
    Txi = 2022,
    Git = 2023,
    Nss = 2009,
    Ncs = 2010,
    Uts = 2035,
    Utd = 2042,
    Utp = 2044,
    Dwk = 2052,
    Pwk = 2053,
    Lyt = 3000,
    Vis = 3001,
    Tpc = 3007,
    Mdx = 3008,
}

/// <summary>Cle de ressource. Le resref est normalise en minuscules a la construction.</summary>
public readonly record struct ResourceKey
{
    public string ResRef { get; }
    public ResourceType Type { get; }

    public ResourceKey(string resRef, ResourceType type)
    {
        ResRef = (resRef ?? string.Empty).ToLowerInvariant();
        Type = type;
    }
}

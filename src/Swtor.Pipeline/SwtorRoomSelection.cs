using Kotor.Compiler.Bsp;

namespace Swtor.Pipeline;

/// <summary>Une room d'une zone : nom, triangles de rendu et room « espace » (coque exterieure, ciel de vaisseau : decochee par defaut).</summary>
public sealed record SwtorRoomInfo(string Name, int Triangles, bool IsSpace);

public enum SwtorBudgetLevel { Ok, Near, Exceeded }

/// <summary>
/// Budget de rendu d'une carte JA (profil swgl) : <c>MAX_MAP_DRAW_INDEXES</c> = 524288 index, soit environ 174 000 triangles de rendu
/// (mesure en T5 : index = 3 x triangles + la boite de ciel). Estimation rapide, avant toute compilation ; le compilateur reste juge.
/// </summary>
public static class SwtorBudget
{
    public const int MaxDrawIndexes = 524288;

    /// <summary>Triangles de rendu au plus par carte : (524288 - boite de ciel) / 3.</summary>
    public const int MaxRenderTriangles = (MaxDrawIndexes - SkyBox.IndexCount) / 3;

    /// <summary>Au-dela de cette part du budget, un avertissement « proche de la limite » s'affiche.</summary>
    public const double NearFraction = 0.85;

    public static long EstimateDrawIndexes(long triangles) => triangles * 3 + SkyBox.IndexCount;

    public static SwtorBudgetLevel Level(long triangles) =>
        triangles > MaxRenderTriangles ? SwtorBudgetLevel.Exceeded
        : triangles >= MaxRenderTriangles * NearFraction ? SwtorBudgetLevel.Near
        : SwtorBudgetLevel.Ok;

    /// <summary>Message (anglais) pour un total de triangles ; null si le budget est respecte avec de la marge.</summary>
    public static string? Message(long triangles) => Level(triangles) switch
    {
        SwtorBudgetLevel.Exceeded =>
            $"Too many triangles: {triangles:N0} render triangles (~{EstimateDrawIndexes(triangles):N0} draw indexes, limit {MaxDrawIndexes:N0} = ~{MaxRenderTriangles:N0} triangles). "
            + "This is the vanilla BSP limit: the swgl profile does not enforce it (not tested in game for maps this large); uncheck rooms if the game loads slowly or crashes.",
        SwtorBudgetLevel.Near =>
            $"Close to the limit: {triangles:N0} of ~{MaxRenderTriangles:N0} render triangles (~{EstimateDrawIndexes(triangles):N0} of {MaxDrawIndexes:N0} draw indexes).",
        _ => null,
    };
}

/// <summary>Cases a cocher des rooms d'une zone (sans interface) : selection, total de triangles, jauge du budget BSP, texte de la selection.</summary>
public sealed class SwtorRoomSelection
{
    private readonly Dictionary<string, bool> _checked;

    public SwtorRoomSelection(string areaId, string internalName, IEnumerable<SwtorRoomInfo> rooms)
    {
        AreaId = areaId;
        InternalName = internalName;
        Rooms = [.. rooms];
        _checked = Rooms.ToDictionary(r => r.Name, r => !r.IsSpace, StringComparer.OrdinalIgnoreCase);
    }

    public string AreaId { get; }
    public string InternalName { get; }
    public IReadOnlyList<SwtorRoomInfo> Rooms { get; }

    public bool IsChecked(string room) => _checked.TryGetValue(room, out bool v) && v;

    public void Set(string room, bool value)
    {
        if (_checked.ContainsKey(room)) _checked[room] = value;
    }

    public void SelectDefault()
    {
        foreach (SwtorRoomInfo r in Rooms) _checked[r.Name] = !r.IsSpace;
    }

    public void SelectAll(bool value)
    {
        foreach (SwtorRoomInfo r in Rooms) _checked[r.Name] = value;
    }

    public IReadOnlyList<string> SelectedRooms => [.. Rooms.Where(r => IsChecked(r.Name)).Select(r => r.Name)];

    public SwtorSelectionFile ToFile(string? mapName) => new(AreaId, InternalName, mapName, SelectedRooms);

    /// <summary>Remplace la selection par celle du fichier ; renvoie les rooms du fichier absentes de cette zone (ignorees).</summary>
    public IReadOnlyList<string> Apply(SwtorSelectionFile file)
    {
        SelectAll(false);
        var unknown = new List<string>();
        foreach (string r in file.Rooms)
        {
            if (_checked.ContainsKey(r)) _checked[r] = true;
            else unknown.Add(r);
        }
        return unknown;
    }

    public int SelectedCount =>Rooms.Count(r => IsChecked(r.Name));

    /// <summary>Total des triangles de rendu des rooms cochees.</summary>
    public long TotalTriangles => Rooms.Where(r => IsChecked(r.Name)).Sum(r => (long)r.Triangles);

    public long EstimatedDrawIndexes => SwtorBudget.EstimateDrawIndexes(TotalTriangles);

    public SwtorBudgetLevel Level => SwtorBudget.Level(TotalTriangles);

    public string? BudgetMessage => SwtorBudget.Message(TotalTriangles);

    /// <summary>Vrai si la selection est celle de l'export par defaut de la CLI (toutes les rooms sauf espace) : la zone s'exporte entiere.</summary>
    public bool IsDefault => Rooms.All(r => IsChecked(r.Name) == !r.IsSpace);

    /// <summary>Nom de carte propose : celui de la CLI (nom interne, suffixe de rooms si la selection n'est pas celle par defaut).</summary>
    public string DefaultMapName => SwtorMapNames.FromInternalName(InternalName.Length > 0 ? InternalName : AreaId, IsDefault ? null : SelectedRooms);

    /// <summary>Texte de module du flux de travail : <c>id</c> (zone entiere) ou <c>id:room1,room2</c> ; null si aucune room n'est cochee.</summary>
    public string? ModuleSpec => SelectedCount == 0 ? null : IsDefault ? AreaId : $"{AreaId}:{string.Join(",", SelectedRooms)}";
}

/// <summary>Sauvegarde JSON de la selection de rooms d'une zone (export / import : on ne perd pas son travail si l'application plante ou change).</summary>
public sealed record SwtorSelectionFile(string Area, string InternalName, string? MapName, IReadOnlyList<string> Rooms)
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

    public string ToJson() => System.Text.Json.JsonSerializer.Serialize(this, Options);

    public static SwtorSelectionFile? FromJson(string json) => System.Text.Json.JsonSerializer.Deserialize<SwtorSelectionFile>(json, Options);

    /// <summary>Chemin par defaut dans le dossier de travail : <c>swtor_rooms_&lt;zone&gt;.json</c>.</summary>
    public static string DefaultPath(string workDir, string area) =>
        Path.Combine(workDir, "swtor_rooms_" + string.Concat(area.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_')) + ".json");
}

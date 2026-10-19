using System.Numerics;
using Kotor.Core.Entities;
using Kotor.Core.Modules;
using Kotor.Core.Textures;

namespace Kotor.Core.Scene;

/// <summary>
/// Scene assemblee d'un module. Textures : cle = nom de texture ou de lightmap ; null = introuvable.
/// <see cref="Lights"/> : noeuds lumiere des pieces ; <see cref="AmbientColor"/> : ambiante de la zone (ARE), 0..1.
/// </summary>
public sealed record ModuleScene(ModuleInfo Info, IReadOnlyList<SceneMesh> Meshes, IReadOnlyList<SceneWalkmesh> Walkmeshes,
    IReadOnlyDictionary<string, KotorTexture?> Textures)
{
    /// <summary>Ambiante par defaut quand l'ARE n'en donne pas.</summary>
    public static readonly Vector3 DefaultAmbient = new(0.2f, 0.2f, 0.2f);

    public IReadOnlyList<SceneLight> Lights { get; init; } = Array.Empty<SceneLight>();
    public Vector3 AmbientColor { get; init; } = DefaultAmbient;

    /// <summary>ARE <c>SunDiffuseColor</c> (0..1); only meaningful when <see cref="AreaLightingKnown"/>.</summary>
    public Vector3 SunDiffuseColor { get; init; }

    /// <summary>True when the ARE was found and readable: the area ambient and sun colour then replace the faux sun of a module without light node.</summary>
    public bool AreaLightingKnown { get; init; }

    /// <summary>Faux (SWTOR) : les maillages de rendu ne servent pas de collision, seuls les walkmeshes (marchables ou solides) en fournissent. Vrai (KOTOR, defaut) : la geometrie visible bloque aussi.</summary>
    public bool RenderMeshesAreSolid { get; init; } = true;

    /// <summary>Vrai (SWTOR) : surfaces de rendu regroupees par proximite (tri de Morton) pour limiter les feuilles recoupees par surface. Faux (KOTOR, defaut) : ordre des entrees, sortie inchangee.</summary>
    public bool CompactRenderSurfaces { get; init; }

    /// <summary>Shader de ciel de la carte ; null = defaut (<c>JaDefaults.SkyShader</c>).</summary>
    public string? SkyShader { get; init; }

    /// <summary>Direction de propagation du soleil (repere de la scene, Z haut) ; null = direction par defaut de JA.</summary>
    public Vector3? SunDirection { get; init; }

    /// <summary>Vrai : le soleil (<see cref="SunDiffuseColor"/>) s'ajoute aux lumieres ponctuelles (zones exterieures SWTOR). Faux (KOTOR) : avec des lumieres, pas de soleil.</summary>
    public bool SunWithLights { get; init; }

    /// <summary>Modules d'une carte assemblee avec leur transformation ; null pour la scene d'un seul module.</summary>
    public IReadOnlyList<AssemblyPartRef>? Parts { get; init; } = null;

    /// <summary>Visibilite par piece : nom de piece (<see cref="SceneMesh.Room"/>) -> pieces visibles depuis elle. Null = pas de donnees (un seul cluster toujours visible). Une piece absente du dictionnaire voit tout et est vue de tout.</summary>
    public IReadOnlyDictionary<string, IReadOnlyCollection<string>>? RoomVisibility { get; init; }

    /// <summary>Modeles places plusieurs fois (SWTOR), hors de <see cref="Meshes"/> : exportes en MD3 + <c>misc_model_static</c> (voir <see cref="SceneModel"/>).</summary>
    public IReadOnlyList<SceneModel> Models { get; init; } = [];

    /// <summary>Instances de <see cref="Models"/>.</summary>
    public IReadOnlyList<SceneModelInstance> ModelInstances { get; init; } = [];

    /// <summary>
    /// <see cref="Meshes"/> plus les instances de modeles remises en repere de scene (apercu), fusionnees par piece et materiau
    /// comme le sont les maillages de la scene.
    /// </summary>
    public IEnumerable<SceneMesh> AllMeshes()
    {
        if (ModelInstances.Count == 0) return Meshes;
        var merged = ModelInstances
            .SelectMany(i => Models[i.Model].Bake(i.Transform, i.Room))
            .GroupBy(m => (m.Room, m.Texture, m.Alpha))
            .Select(g =>
            {
                SceneMesh first = g.First();
                var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                foreach (SceneMesh m in g)
                {
                    int b = pos.Count;
                    pos.AddRange(m.Positions); nrm.AddRange(m.Normals); uv.AddRange(m.Uv0);
                    foreach (int t in m.Triangles) tri.Add(b + t);
                }
                return first with { Positions = [.. pos], Normals = [.. nrm], Uv0 = [.. uv], Uv1 = null, Triangles = [.. tri] };
            });
        return Meshes.Concat(merged);
    }

    /// <summary>Copie de la scene transformee : positions, normales (rotation seule), lumieres et entree ; textures partagees.</summary>
    public ModuleScene Transformed(RigidTransform t)
    {
        var meshes = Meshes.Select(m => m with
        {
            Positions = m.Positions.Select(t.Apply).ToArray(),
            Normals = m.Normals.Select(t.ApplyDirection).ToArray(),
        }).ToList();
        var walkmeshes = Walkmeshes.Select(w => w with { Positions = w.Positions.Select(t.Apply).ToArray() }).ToList();
        var dir = Info.Entry.Direction;
        var d3 = t.ApplyDirection(new Vector3(dir.X, dir.Y, 0f));
        var info = Info with { Entry = new ModuleEntry(t.Apply(Info.Entry.Position), new Vector2(d3.X, d3.Y)) };
        return this with
        {
            Info = info,
            Meshes = meshes,
            Walkmeshes = walkmeshes,
            Lights = Lights.Select(l => l with { Position = t.Apply(l.Position) }).ToList(),
            ModelInstances = ModelInstances.Select(i => i with
            {
                Transform = i.Transform * Matrix4x4.CreateRotationZ(t.YawDegrees * MathF.PI / 180f) * Matrix4x4.CreateTranslation(t.Translation),
            }).ToList(),
        };
    }
}

/// <summary>
/// Module d'une carte assemblee et transformation qui lui a ete appliquee.
/// <see cref="Info"/> : informations du module dans son propre repere (lecture du GIT a l'import) ; null = deduites du nom.
/// <see cref="OpenedDoors"/> : portes source des jonctions (gardees, deverrouillees) ; <see cref="RemovedDoors"/> : portes de
/// destination des jonctions (retirees). Toutes en repere du module, reconnues par tag et position.
/// </summary>
public sealed record AssemblyPartRef(string Module, RigidTransform Transform)
{
    public ModuleInfo? Info { get; init; }
    public IReadOnlyList<ModuleEntity> OpenedDoors { get; init; } = Array.Empty<ModuleEntity>();
    public IReadOnlyList<ModuleEntity> RemovedDoors { get; init; } = Array.Empty<ModuleEntity>();
}

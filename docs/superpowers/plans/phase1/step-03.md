# Étape 03 — GFF, TLK, catalogue des modules et commande `list-modules`

**But :** lister les modules avec le nom de leur zone, et lire le point d'entrée du module.

**Fichiers :**
- Créer : `src/Kotor.Core/Gff/{GffReader,GffStruct,GffLocString}.cs`, `src/Kotor.Core/Tlk/TlkFile.cs`, `src/Kotor.Core/Modules/{ModuleCatalog,ModuleInfo}.cs`
- Créer : `src/Kotor2Ja.Cli/Commands/ListModulesCommand.cs`. Modifier `Program.cs` pour l'enregistrer.
- Tester : `tests/Kotor.Core.Tests/Gff/GffReaderTests.cs`, `Tlk/TlkFileTests.cs`, `Modules/ModuleCatalogTests.cs`

**Interfaces :**
- Consomme : `ResourceResolver`, `ResourceType`, `RimArchive` (étape 02).
- Produit :
```csharp
public sealed record GffLocString(uint StrRef, IReadOnlyDictionary<int, string> Strings);
public sealed class GffStruct
{
    public uint Type { get; }
    public IReadOnlyDictionary<string, object?> Fields { get; }   // libellé -> valeur
    public T? Get<T>(string label);                              // null/default si absent ou d'un autre type
    public IReadOnlyList<GffStruct> GetList(string label);       // vide si absent
}
public static class GffReader { public static GffStruct Read(byte[] data, out string fileType); } // fileType ex. "IFO "
public sealed class TlkFile { public static TlkFile Open(string path); public string? Get(uint strRef); }
public readonly record struct ModuleEntry(Vector3 Position, Vector2 Direction);       // mètres KOTOR
public sealed record ModuleInfo(string Module, string AreaResRef, string AreaName, ModuleEntry Entry);
public static class ModuleCatalog
{
    public static IReadOnlyList<string> ListModuleNames(string kotorDir); // modules\*.rim sans les *_s.rim, tri alphabétique, minuscules
    public static ModuleInfo Load(ResourceResolver res, string module, TlkFile? tlk);
}
```
**Ce qui est fixé :**
- Correspondance des types GFF V3.2 :
  - 0 à 9 et 8 : types numériques C# (`byte`, `sbyte`, `ushort`, `short`, `uint`, `int`, `ulong`, `long`, `float`, `double`) ;
  - 10 CExoString → `string` ;
  - 11 ResRef → `string` ;
  - 12 CExoLocString → `GffLocString` ;
  - 13 Void → `byte[]` ;
  - 14 Struct → `GffStruct` ;
  - 15 List → `IReadOnlyList<GffStruct>` ;
  - 16 Orientation → `Quaternion` (stocké w,x,y,z dans le fichier : vérifier dans les références) ;
  - 17 Vector → `Vector3`.
- IFO :
  - `Mod_Area_list[0].Area_Name` donne la resref de l'ARE ;
  - `Mod_Entry_X/Y/Z` donne la position d'entrée, `Mod_Entry_Dir_X/Y` sa direction.
- ARE : `Name` (CExoLocString). Nom affiché : la chaîne TLK de `StrRef`, sinon la première chaîne locale, sinon la resref.
- TLK V3.0 : `langId` u32 @8, `count` u32 @12, `entriesOff` u32 @16. Une entrée fait 40 octets : `flags` u32, `sound[16]`, `volVar` u32, `pitchVar` u32, `offset` u32, `size` u32, `soundLen` f32. Le texte se trouve à `entriesOff + offset`. On lit en Windows-1252 (`CodePagesEncodingProvider`).
- `kotor2ja list-modules [--kotor <dir>]` affiche une ligne par module, `<module>\t<nom de zone>`. Code de sortie 0, ou 2 si le dossier KOTOR est introuvable.

- [ ] **Étape 1 : tests qui échouent**
  - `Gff_reads_synthetic_struct` : un GFF construit en mémoire avec un champ de chaque famille (int, float, CExoString, ResRef, LocString, List de 2 structs, Vector). Toutes les valeurs sont relues.
  - `Get_returns_default_for_missing_label`.
  - `Tlk_reads_synthetic_entry` : `Get(1)` renvoie le texte, `Get(999)` renvoie null.
  - `ListModuleNames_excludes_s_rims` : dans un dossier temporaire avec `a.rim`, `a_s.rim` et `b.rim`, on obtient `["a","b"]`.
  - `[SkippableFact] Real_tar_m02aa_info` :
    - `AreaResRef == "m02aa"` ;
    - `AreaName` n'est pas vide et est différent de `"m02aa"` ;
    - `Entry.Position` est fini, et la direction est de norme supérieure à 0.
  - `[SkippableFact] Real_catalog_has_23_taris_modules` : 23 noms commencent par `tar_`.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter** le lecteur GFF, le lecteur TLK, le catalogue et la commande.
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert, puis `dotnet run --project src/Kotor2Ja.Cli -- list-modules`. Copier dans le rapport les lignes `tar_*`, avec les noms de zones. Elles serviront à confirmer le nom réel de `tar_m02aa`.
- [ ] **Étape 5 : rapport** `reports/step-03-impl.md`.

# Étape 02 — Archives KOTOR et résolution des ressources

**But :** lire n'importe quelle ressource d'un module par son nom et son type, dans l'ordre de priorité du jeu.

**Fichiers :**
- Créer : `src/Kotor.Core/Archives/{ResourceType,ResourceKey,IResourceArchive,KeyBifArchive,RimArchive,ErfArchive,ResourceResolver}.cs`
- Tester : `tests/Kotor.Core.Tests/Archives/{KeyBifArchiveTests,RimArchiveTests,ErfArchiveTests,ResourceResolverTests}.cs`

**Interfaces :**
- Consomme : `TestEnv.KotorDir` (étape 01).
- Produit :
```csharp
public enum ResourceType : ushort { Tga = 3, TwoDa = 2017, Mdl = 2002, Are = 2012, Ifo = 2014, Wok = 2016, Txi = 2022,
    Git = 2023, Utd = 2042, Utp = 2044, Dwk = 2052, Pwk = 2053, Lyt = 3000, Vis = 3001, Tpc = 3007, Mdx = 3008 }
public readonly record struct ResourceKey(string ResRef, ResourceType Type);  // ResRef en minuscules (normalisé à la construction)
public interface IResourceArchive : IDisposable
{
    string SourceName { get; }                  // ex. "modules\\tar_m02aa.rim", pour le journal
    IEnumerable<ResourceKey> Keys { get; }
    bool TryRead(ResourceKey key, out byte[] data);
}
public sealed class KeyBifArchive : IResourceArchive { public static KeyBifArchive Open(string kotorDir); }
public sealed class RimArchive   : IResourceArchive { public static RimArchive Open(string path); }
public sealed class ErfArchive   : IResourceArchive { public static ErfArchive Open(string path); } // "ERF V1.0" et "MOD V1.0"
public sealed class ResourceResolver : IDisposable
{
    public static ResourceResolver ForModule(string kotorDir, string module);
    public byte[]? Find(string resRef, ResourceType type);
    public string? FindSource(string resRef, ResourceType type); // nom de l'archive qui a fourni la ressource
}
```
Les types de ressources inconnus sont conservés comme valeurs numériques brutes : un `ResourceType` non nommé reste valide.

**Ce qui est fixé (vérifié sur le disque, offsets en octets, little-endian) :**
- **KEY** : `"KEY V1  "`, puis `bifCount` u32 @8, `keyCount` @12, `fileTableOff` @16, `keyTableOff` @20.
  - Une entrée de fichier fait 12 octets : `size` u32, `nameOff` u32, `nameLen` u16, `drives` u16. Le nom est relatif au dossier KOTOR, par exemple `data\models.bif`.
  - Une clé fait 22 octets : `resref[16]`, `type` u16, `id` u32. Le numéro de BIF est `id >> 20`, l'index dans le BIF est `id & 0xFFFFF`.
- **BIF** : `"BIFFV1  "`, puis `varCount` u32 @8, `fixCount` @12, `varTableOff` @16. Une entrée fait 16 octets : `id` u32, `offset` u32, `size` u32, `type` u32.
  - On ouvre chaque BIF à la demande, et on lit les données sans charger tout le BIF en mémoire.
- **RIM** : `"RIM V1.0"`, puis `entryCount` u32 @0x0C, `keysOff` u32 @0x10. Une clé fait 32 octets : `resref[16]`, `type` **u32**, `id` u32, `offset` u32, `size` u32.
- **ERF/MOD** : `"ERF V1.0"`/`"MOD V1.0"`, puis `langCount` @8, `locStrSize` @12, `entryCount` @16, `locStrOff` @20, `keysOff` @24, `resOff` @28.
  - Une clé fait 24 octets : `resref[16]`, `resId` u32, `type` u16, `unused` u16.
  - Une entrée de ressource fait 8 octets : `offset` u32, `size` u32.
- **Ordre de `ResourceResolver`** :
  1. `Override\<resref>.<ext>` (fichier libre) ;
  2. `modules\<m>.rim` ;
  3. `modules\<m>_s.rim` ;
  4. pour `Tpc` et `Txi` seulement : `TexturePacks\swpc_tex_tpa.erf` ;
  5. `chitin.key` + BIF.

  La première trouvée gagne. Les archives absentes sont ignorées sans erreur.

- [ ] **Étape 1 : tests qui échouent**
  - `Rim_reads_synthetic_entries` : un RIM construit en mémoire avec 2 entrées. `Keys` en contient 2 et `TryRead` renvoie les bons octets.
  - `Erf_reads_synthetic_entries` : même chose en ERF.
  - `Mod_signature_is_accepted` : `"MOD V1.0"` s'ouvre comme un ERF.
  - `Keys_are_lowercase` : `"M02AA_01A"` devient `"m02aa_01a"`.
  - `Resolver_prefers_override` : dans un faux dossier KOTOR temporaire avec `Override\m02aa.lyt` et un RIM qui contient aussi `m02aa.lyt`, on obtient le contenu de l'Override.
  - `Resolver_missing_returns_null` : une ressource inexistante donne null, sans exception.
  - `[SkippableFact] Real_install_finds_tar_m02aa_resources` :
    - `module.ifo`, `m02aa.are`, `m02aa.lyt`, `m02aa_01a.mdl`, `m02aa_01a.mdx`, `m02aa_01a.wok` et `m02aa_01a_lm0.tga` sont trouvés ;
    - `FindSource("m02aa_01a", Mdl)` contient `models.bif`.
- [ ] **Étape 2 : lancer** `dotnet test tests/Kotor.Core.Tests --filter Archives`. Attendu : échec (types absents).
- [ ] **Étape 3 : implémenter** les archives et le résolveur.
- [ ] **Étape 4 : relancer** `dotnet test Kotor2Ja.sln`. Attendu : tout vert. Le test d'intégration n'est pas ignoré sur cette machine.
- [ ] **Étape 5 : rapport** `reports/step-02-impl.md`.

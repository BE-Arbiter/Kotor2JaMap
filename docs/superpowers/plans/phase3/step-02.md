# Étape 02 — KOTOR 2

**But :** les modules de KOTOR 2 (`Knights of the Old Republic II`) s'affichent sous un nœud « KOTOR 2 » et passent par les quatre étapes, comme ceux de KOTOR.

**Fichiers :**
- Créer : `src/Kotor.Core/KotorGame.cs`
- Modifier :
  - `Archives/ResourceResolver.cs` (`.mod`, `_dlg.erf`) ;
  - `Modules/ModuleCatalog.cs` (listes des `.mod` et des `.rim`) ;
  - `Mdl/MdlReader.cs` (détection K1/K2 et décalages du maillage K2) ;
  - `Export/ExportOptions.cs`, `ConfigLoader.cs`, `MapNames.cs`, `ShaderNames.cs` (préfixes par jeu) ;
  - `Kotor.Pipeline` : `PipelineSteps`, `ModuleExporter`, `Workflow/*` (le jeu fait partie de la sélection) ;
  - la CLI (`--game kotor|kotor2`, par défaut `kotor`) ;
  - l'application (`MainScreen` : deux nœuds) ;
  - `kotor2ja.config.json` (`kotor2Dir`) ;
  - `tests/*/TestEnv.cs` (`Kotor2Dir`).
- Tester : `tests/Kotor.Core.Tests/Kotor2Tests.cs`, plus des ajouts dans `tests/Kotor.Pipeline.Tests/WorkflowControllerTests.cs`.

**Interfaces produites :**
```csharp
namespace Kotor.Core;
public enum KotorGame { Kotor1, Kotor2 }
public static class KotorGames
{
    public static string MapPrefix(KotorGame g);      // "kotor" | "kotor2" : pk3 <prefix>_<carte>.pk3, shaders/<prefix>_<carte>.shader
    public static string AssetFolder(KotorGame g);    // "kotor" | "kotor2" : textures/<f>/, models/<f>/
    public static string DisplayName(KotorGame g);    // "KOTOR" | "KOTOR 2"
}
// ExportOptions : + KotorGame Game = KotorGame.Kotor1 ; KotorDir est le dossier du jeu choisi. Ajouter « string? Kotor2Dir » à la config chargée.
// ResourceResolver.ForModule(string gameDir, string module, KotorGame game = KotorGame.Kotor1)
// ModuleListItem(KotorGame Game, string Module, string AreaName) ; IWorkflowSteps.Modules() renvoie les deux jeux ;
// WorkflowController.SelectModule(KotorGame game, string module).
```

**Ce qui est fixé :**
- Ordre de résolution de KOTOR 2 : `Override`, `modules\<m>.mod` (s'il existe, il remplace les `.rim`), sinon `<m>.rim`, `<m>_s.rim`, puis `<m>_dlg.erf`, `TexturePacks\swpc_tex_tpa.erf` et `chitin.key`. KOTOR 1 reste inchangé, mais prend aussi un `.mod` s'il existe.
- MDL de KOTOR 2 :
  - détection par le premier pointeur de fonction de l'en-tête de géométrie (K1 PC : 4273776 et 4216096 ; K2 PC : 4285200 et 4216320, **valeurs à vérifier** dans xoreos `src/graphics/aurora/model_kotor.cpp`) ;
  - le maillage K2 a 8 octets de plus avant les champs qui suivent la lightmap ; tous les décalages concernés sont à vérifier et à citer ;
  - test sur au moins trois modèles de pièce réels de KOTOR 2.
- Si les décalages de KOTOR 2 ne peuvent pas être établis avec certitude, arrêter et répondre `BLOQUE` avec les mesures faites. Ne pas deviner.
- Catalogue de KOTOR 2 : nom de zone via `dialog.tlk` du dossier KOTOR 2. TLK absent : nom = nom du module.
- Noms produits (spec) :
  - KOTOR 1 inchangé : `kotor_<carte>.pk3`, `textures/kotor/` ;
  - KOTOR 2 : `kotor2_<carte>.pk3`, `shaders/kotor2_<carte>.shader`, `textures/kotor2/` ;
  - le nom de carte BSP reste `<carte>`.
- Application : nœud « KOTOR » puis nœud « KOTOR 2 ». Un jeu dont le dossier est absent ou non configuré : nœud grisé, libellé `KOTOR 2 (not found)`, sans plantage.
- La CLI `list-modules --game kotor2` liste les modules de KOTOR 2.

- [ ] **Étape 1 : tests qui échouent**
  - `MapPrefix_and_folders` : valeurs ci-dessus.
  - `Resolver_prefers_mod_over_rim` : dossier temporaire synthétique avec un `.mod` (ERF) et un `.rim` qui contiennent la même ressource avec des contenus différents. On obtient le contenu du `.mod`.
  - `[SkippableFact] Real_k2_catalog_lists_modules` : plus de 50 modules, et `001ebo` est présent.
  - `[SkippableFact] Real_k2_room_models_read` : les modèles de pièce du LYT de `001ebo` (ou d'un autre module confirmé) se lisent tous, avec au moins un maillage texturé et des UV de lightmap.
  - `[SkippableFact] Real_k2_preview_and_export` : `Preview → Import → Export → CreatePk3` sur un module de KOTOR 2, avec `JaBaseDir` temporaire. Le pk3 s'appelle `kotor2_<m>.pk3` et contient `shaders/kotor2_<m>.shader`.
  - `Select_takes_game` (`WorkflowControllerTests`) : la sélection porte le jeu.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` / `dotnet test` verts.** Exporter un module de KOTOR 2 vers un dossier temporaire, et recopier dans le rapport le bilan du journal.
- [ ] **Étape 5 : rapport** `reports/step-02-impl.md` : décalages et pointeurs vérifiés avec leurs sources, module de KOTOR 2 testé, résultat de l'export.

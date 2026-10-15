# Étape 07 — Exécutable portable, détection des jeux, panneau Settings, workflow GitHub

**But :** un zip Windows autonome (l'application et la CLI, sans .NET installé), produit en local par `scripts/publish.ps1` et sur GitHub par `.github/workflows/build-latest.yml` (release `latest`).

**Fichiers :**
- Créer : `src/Kotor.Core/Export/GameLocator.cs`, `scripts/publish.ps1`, `.github/workflows/build-latest.yml`
- Modifier :
  - `ConfigLoader` (configuration absente : détection ; enregistrement possible) ;
  - les `.csproj` de l'application et de la CLI (propriétés de publication) ;
  - `src/Kotor2Ja.App/Ui/MainScreen.cs` (panneau `Settings`) ;
  - `README.md` (section Download / Portable build), `.gitignore` (`publish/`, `artifacts/`).
- Tester : `tests/Kotor.Core.Tests/Export/GameLocatorTests.cs`, `ConfigLoaderTests` (ajouts)

**Interfaces produites :**
```csharp
namespace Kotor.Core.Export;
public sealed record GamePaths(string? KotorDir, string? Kotor2Dir, string? JaBaseDir, string WorkDir);
public static class GameLocator
{
    /// Lit steamapps\libraryfolders.vdf (chemin Steam du registre HKCU\Software\Valve\Steam\SteamPath, sinon
    /// C:\Program Files (x86)\Steam). Cherche, dans chaque bibliothèque, steamapps\common\swkotor (chitin.key),
    /// steamapps\common\Knights of the Old Republic II (chitin.key) et steamapps\common\Jedi Academy*\GameData\base.
    public static GamePaths Detect(string? steamRootOverride = null);
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText);  // chemins "path"
}
// ConfigLoader : Load(path) inchangé si le fichier existe ; LoadOrDetect(exeDir) = fichier à côté de l'exécutable, sinon Detect ;
// Save(path, ...) écrit le JSON (mêmes clés, + kotor2Dir). WorkDir par défaut : %LOCALAPPDATA%\Kotor2Ja\work.
```

**Ce qui est fixé :**
- Publication (application et CLI) : `-c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none`.
  - Vérifier que MonoGame Vulkan et les bibliothèques natives (cimgui, SDL2, Vulkan loader) fonctionnent bien en fichier unique, en lançant l'exécutable publié avec les options de l'étape 05 (`--module tar_m02aa --run-to preview --screenshot … --exit`).
  - Si le fichier unique ne fonctionne pas, publier en dossier autonome (non unique) et le noter dans le rapport.
- Zip `artifacts/Kotor2Ja-win-x64.zip` avec `Kotor2Ja.App.exe`, `kotor2ja.exe` (nom actuel de la CLI, à garder), `kotor2ja.config.example.json`, `README.md` et `THIRD-PARTY-NOTICES.md`, plus les fichiers natifs si le mode non unique a été retenu.
- Le contenu (`Content/`, polices…) est inclus s'il existe.
- Panneau `Settings` (repliable, en bas de la colonne de gauche ou dans une fenêtre) :
  - quatre champs de texte : KOTOR, KOTOR 2, JA base, Work dir ;
  - un bouton `Detect` et un bouton `Save` ;
  - `Save` écrit `kotor2ja.config.json` à côté de l'exécutable et recharge la liste des modules ;
  - un chemin invalide est signalé en rouge, sans plantage.
- Workflow `build-latest.yml` :
  - `on: push: branches: [master]` et `workflow_dispatch` ;
  - `runs-on: windows-latest`, `permissions: contents: write`, `actions/checkout@v4`, `actions/setup-dotnet@v4` avec `dotnet-version: 10.0.x` ;
  - `dotnet build -c Release`, `dotnet test -c Release` (les tests sur jeu se sautent seuls), `pwsh scripts/publish.ps1` ;
  - remplacement de la release `latest` : `gh release delete latest --yes --cleanup-tag` (sans échec si elle n'existe pas), puis `gh release create latest artifacts/Kotor2Ja-win-x64.zip --prerelease --title "Latest build" --notes "Automated build of <sha>"`, avec `GH_TOKEN: ${{ github.token }}`.
- Ne pas pousser et ne créer aucun dépôt distant. Vérifier la syntaxe YAML avec un analyseur (`py -c "import yaml"` si disponible, sinon une revue manuelle notée dans le rapport).

- [ ] **Étape 1 : tests qui échouent**
  - `ParseLibraryFolders_reads_paths` : un VDF d'exemple à 2 bibliothèques (avec `\\` échappés) donne 2 chemins.
  - `Detect_finds_games_in_fake_library` : une arborescence temporaire (Steam factice avec `libraryfolders.vdf` et les dossiers des jeux avec `chitin.key`) donne les trois chemins.
  - `Detect_missing_games_null` : bibliothèque vide, tout à null sauf `WorkDir`.
  - `Config_save_roundtrip`.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet build` / `dotnet test` verts.** Puis :
  - lancer `pwsh scripts/publish.ps1` (ou `powershell -File`) ;
  - extraire le zip dans un dossier temporaire **sans** `kotor2ja.config.json` ;
  - lancer `Kotor2Ja.App.exe --game kotor --module tar_m02aa --run-to preview --screenshot <TEMP>\portable.png --exit` : la détection doit trouver KOTOR et produire une capture non noire ;
  - lancer `kotor2ja.exe list-modules`.
  - Recopier dans le rapport les tailles et les résultats.
- [ ] **Étape 5 : rapport** `reports/step-07-impl.md`.

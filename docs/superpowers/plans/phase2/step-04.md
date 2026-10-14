# Étape 04 — Application `Kotor2Ja.App` (MonoGame Vulkan + ImGui)

**But :** l'application de bureau décrite par l'utilisateur. Elle affiche le `PreviewModel` de l'étape en cours.

**Fichiers :**
- Copier puis adapter depuis `C:\Users\lambe\source\repos\SWGLFU-Map-importer\src\Tfu2Ja.App\` : `Tfu2Ja.App.csproj` (devient `src/Kotor2Ja.App/Kotor2Ja.App.csproj`, mêmes paquets et versions), `Program.cs`, `AppGame.cs`, `ImGuiRenderer.cs` (en gardant la mention de licence MIT de son origine), ainsi que la caméra et le rendu de la vue s'ils existent dans `AppGame` ou à côté.
- Créer : `src/Kotor2Ja.App/Ui/MainScreen.cs`, `src/Kotor2Ja.App/Rendering/{PreviewRenderer,FreeCamera}.cs`
- Modifier : `Kotor2Ja.sln`, `THIRD-PARTY-NOTICES.md` (MonoGame, ImGui.NET, origine de `ImGuiRenderer`), `README.md` (lancer l'application)
- Pas de projet de test : la logique est testée à l'étape 03. Cette étape se vérifie à la main.

**Interfaces :**
- Consomme : `WorkflowController`, `KotorWorkflowSteps`, `ModuleListItem`, `PreviewModel`, `PreviewBatch`, `PreviewTexture`, `PreviewMarker`, `PreviewBlend`, `EntityKind` (étapes 02 et 03) ; `ConfigLoader.Load` (même `kotor2ja.config.json` que la CLI, copié à côté de l'exécutable).

**Ce qui est fixé (disposition imposée par l'utilisateur) :**
- **Colonne de gauche**, sur toute la hauteur, de haut en bas :
  1. un `TreeNode` **« KOTOR »**, ouvert par défaut, avec un élément sélectionnable par module, au format `tar_m02aa — Taris - appartements sud` ;
  2. sous la liste, le bouton **« Prévisualiser »**, actif si `CanPreview` ;
  3. sous le bouton, le **journal**, une zone qui défile vers le bas, alimentée par `DrainLog()`. Les erreurs (`LastError`) s'affichent en rouge.
- **Fenêtre principale** : la vue 3D de `controller.Preview`. **En bas à droite**, un seul bouton dont le texte est `NextLabel`. Il est masqué si `NextLabel` est null, et inactif si `!CanNext`. Pendant le travail, il affiche « … » ou un indicateur.
- **Cases à cocher** discrètes dans la vue (en haut à droite) :
  - « Collisions » (fil de fer) ;
  - une case par `EntityKind` présent dans les repères, visible seulement si la vue a des repères.
- **Rendu** :
  - une texture GPU par clé de `Textures` ;
  - lots avec lightmap : `DualTextureEffect` (diffuse × lightmap) ;
  - sans lightmap : `BasicEffect` texturé, sans éclairage ;
  - `Cutout` : alpha test ; `Additive` et `Alpha` : blend, sans écriture de profondeur, dessinés après les opaques ;
  - `DoubleSided` : pas de culling ;
  - repères : un petit cube de couleur par type (taille fixe en unités JA, par exemple 16), avec le nom (`Label`) écrit par ImGui à la position projetée quand la caméra est à moins d'une certaine distance ;
  - point d'apparition : repère distinct, avec la direction du regard.
- **Caméra** :
  - libre, clic droit maintenu pour regarder ;
  - ZQSD et WASD pour avancer et se déplacer, E/A ou Espace/Ctrl pour monter et descendre ;
  - Maj pour aller plus vite, molette pour la vitesse ;
  - à chaque nouvelle vue, la caméra est placée pour voir les bornes, ou sur le point d'apparition s'il existe.
  - Les entrées clavier sont ignorées quand ImGui capture le clavier ou la souris.
- **Repère** : Z vers le haut, unités JA. La caméra et la projection en tiennent compte.
- L'interface ne gèle pas : les étapes passent par `PreviewAsync`/`NextAsync`, et l'upload GPU d'un nouveau `PreviewModel` se fait dans le fil de rendu quand la référence `Preview` change.
- Titre de la fenêtre : « KOTOR → Jedi Academy ». Taille initiale : 1600×900, redimensionnable.

- [ ] **Étape 1 : copier et adapter le squelette**, puis faire compiler `dotnet build Kotor2Ja.sln` (0 avertissement).
- [ ] **Étape 2 : implémenter `MainScreen`, `PreviewRenderer` et `FreeCamera`** selon la disposition ci-dessus.
- [ ] **Étape 3 : `dotnet test Kotor2Ja.sln`**, toujours vert.
- [ ] **Étape 4 : essai de démarrage.**
  - Lancer `dotnet run -c Release --project src/Kotor2Ja.App` en arrière-plan, attendre environ 10 secondes, vérifier que le processus tourne toujours (pas de plantage au démarrage), puis l'arrêter.
  - Si l'application écrit un journal ou une sortie console, la joindre au rapport.
  - Ne pas cliquer : l'utilisateur fera la vérification complète.
- [ ] **Étape 5 : rapport** `reports/step-04-impl.md` : fichiers copiés et adaptés, choix de rendu, résultat de l'essai de démarrage, et une **liste de contrôle manuelle** pour l'utilisateur (quoi cliquer, quoi doit apparaître).

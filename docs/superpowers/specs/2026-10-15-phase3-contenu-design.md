# Phase 3 — Contenu des cartes, KOTOR 2, anglais et build portable : conception

Date : 2026-10-15. Demande de l'utilisateur (absent pendant l'exécution, qui a demandé de tester l'application puis le jeu) :

1. petits soucis de texture (verte) et de lumière (incohérente) en jeu ;
2. ajouter « Kotor 2 » à la liste (`C:\Program Files (x86)\Steam\steamapps\common\Knights of the Old Republic II\`) ;
3. une action « build latest » qui fournit un exécutable portable tout compris ;
4. langue en anglais ;
5. les portes ne sont pas converties ;
6. les objets ne sont pas convertis : en faire des MD3 et les ajouter à la carte ;
7. nettoyer les PNJ.

Pas de séance de questions possible : chaque choix est tranché ici et listé dans le rapport final.

## 1. Diagnostic du vert et de la lumière (fait par l'orchestrateur)

Vérifié en jeu (captures dans le rapport final) :

- **Vert.** Les atlas de lightmaps KOTOR (`lightmaps*.bif`, TGA 32 bits) contiennent des zones inutilisées remplies de bruit, marquées par **alpha ≠ 255** (les zones utiles ont alpha = 255). Les UV de lightmap tombent à 99,99 % sur des texels valides (mesuré sur ebo_m41aa, danm16, tar_m02aa). Mais le moteur échantillonne les texels voisins via le filtrage bilinéaire, les mipmaps et `r_picmip 1` (réglage de l'utilisateur) : sur des lightmaps de 4×4 à 8×8, le bruit (souvent vert saturé) déborde. Preuve : en peignant en magenta tous les texels alpha ≠ 255, la grille et les pupitres verts de l'Ebon Hawk deviennent magenta.
- **Découpe abusive.** `ShaderPlanner` classe en `LightmappedCutout` toute texture avec alpha. Or dans KOTOR, quand le TXI a `envmaptexture` ou `bumpyshinytexture`, l'alpha est le **masque de reflet**, pas une transparence. Les murs et sols concernés reçoivent `alphaFunc GE128` (trous possibles), `cull none` et la passe `depthFunc equal`. De plus, tout DXT5 est déclaré « avec alpha », même entièrement opaque.
- **Lumière incohérente.** À Taris, une zone apparaît surexposée (blanc rosé) à côté d'une zone normale, à la frontière entre deux pièces. Les surfaces sans lightmap reçoivent un faux soleil de Jedi Academy (`rgbGen vertex`, soleil DAG_1 ×4 de surbrillance), sans rapport avec les lumières KOTOR. La cause exacte de la zone blanche est à confirmer par l'agent de l'étape 01 (shader concerné, lightmap ou éclairage par sommet), puis à corriger à la racine.

## 2. Décisions

### Rendu (étape 01)
- **Dilatation des lightmaps.** Les texels alpha ≠ 255 sont remplacés par la couleur du texel valide le plus proche (dilatation itérative sur 8 voisins), et l'alpha est mis à 255. Une lightmap sans aucun texel valide devient un gris neutre (128,128,128), avec le code `LIGHTMAP_NO_VALID_TEXEL` au journal. Cela s'applique à l'export (TGA) et à la vue.
- **Pas de réduction des lightmaps.** Les shaders à lightmap reçoivent `nopicmip` et `nomipmaps`.
- **Alpha = reflet.** Quand le TXI déclare un reflet, la texture est traitée comme opaque (`Lightmapped` ou `Unlit`). Le reflet lui-même n'est pas rendu dans cette phase (code `TEXTURE_ENVMAP` inchangé). `HasAlpha` n'est vrai que si au moins un pixel a alpha < 255, y compris pour le DXT5.
- **Éclairage par sommet KOTOR.** Le faux soleil est remplacé, pour les surfaces sans lightmap, par l'ambiante de la zone (ARE : `DynAmbientColor`, sinon `SunAmbientColor`) plus les nœuds lumière des pièces : couleur, rayon et multiplicateur du nœud, atténuation linéaire jusqu'au rayon, sans ombre. Les valeurs sont stockées divisées par 2^`OverbrightShift` comme aujourd'hui. Les lumières sont transmises au compilateur en unités JA.

### KOTOR 2 (étape 02)
- Un enum `KotorGame { Kotor1, Kotor2 }`. La configuration gagne `kotor2Dir`. L'arbre de gauche affiche deux nœuds, « KOTOR » et « KOTOR 2 ». Un nœud dont le dossier est absent est affiché grisé, avec « (not found) ».
- Résolution KOTOR 2 : `Override`, `modules\<m>.rim`, `<m>_s.rim`, `<m>_dlg.erf` et `<m>.mod` s'il existe (le `.mod` prime sur les `.rim`, comme dans le jeu), `TexturePacks\swpc_tex_tpa.erf`, puis `chitin.key`. Le TLK est `dialog.tlk`.
- MDL de KOTOR 2 : l'en-tête de maillage est plus long. La détection se fait par les pointeurs de fonction de l'en-tête de géométrie (référence xoreos `model_kotor.cpp`). Les décalages exacts doivent être vérifiés sur de vrais fichiers de KOTOR 2.
- Noms produits pour KOTOR 2 : pk3 `kotor2_<carte>.pk3`, shader `shaders/kotor2_<carte>.shader`, textures `textures/kotor2/…`, modèles `models/kotor2/…`. Cela évite les collisions avec les textures de même nom de KOTOR 1, puisque tous les pk3 de `base` sont chargés ensemble. **Écriture autorisée dans `GameData\base` : `kotor_<carte>.pk3` et `kotor2_<carte>.pk3` uniquement.**

### Objets → MD3 (étape 03)
- Les objets placés (`Placeable List` du GIT) deviennent des MD3. Le modèle vient de l'UTP (`Appearance`), puis de `placeables.2da`, colonne `modelname`.
- Un fichier MD3 par modèle KOTOR (`models/kotor/<modele>.md3`), avec une surface par couple (texture, lightmap ignorée), une seule image et les transformations de nœud appliquées. L'échelle est intégrée aux sommets. Le MD3 limite les coordonnées à ±512 unités : au-delà, on réduit les sommets d'un facteur et on compense par `modelscale` sur l'entité.
- Shader MD3 : `models/kotor/<texture>` avec `rgbGen lightingDiffuse`. On n'utilise pas de lightmap (les objets n'en ont pas dans KOTOR).
- Entité : `misc_model_static` avec `model`, `origin` (en unités JA) et `angles "0 <yaw> 0"`. Le yaw passe de KOTOR (0 = face à +Y) à Quake (0 = face à +X), soit +90° ; l'agent doit le vérifier sur une capture de la vue.
- Collision : la PWK de l'objet, transformée, est ajoutée à la collision solide de la carte. Sans PWK, il n'y a pas de collision (code `PLACEABLE_NO_PWK`).
- Les objets sans modèle, avec un modèle introuvable ou illisible sont journalisés et sautés. Il n'y a aucun abandon silencieux.

### Portes (étape 04)
- Corriger d'abord l'ordre des quaternions des doorhooks du LYT (w,x,y,z).
- Chaque porte du GIT devient un `func_door` avec :
  - un modèle de brush inline (`*N`), à créer dans le compilateur : sous-modèles BSP avec leurs propres brushes de collision, sans surface visible ;
  - un brush invisible et solide, qui est la boîte englobante orientée du modèle de porte, relative à l'origine de la porte ;
  - `model2` = le MD3 de la porte (même générateur que l'étape 03), avec `origin` = la position de la porte.
- Le modèle de la porte vient de l'UTD (`GenericType`, puis `genericdoors.2da`, colonne `modelname`). Si `GenericType` est absent, on prend `Appearance` et `doortypes.2da`.
- Ouverture : la porte monte (`angle -1`), avec `lip 8`, `speed 200` et `wait 3`. Elle s'ouvre au toucher, sans `targetname`.
- Une porte qui mène à un autre module (`LinkedToModule` non vide) ou verrouillée (`Locked` = 1) reçoit le drapeau `LOCKED` (16) : elle reste fermée.

### PNJ (étape 05)
- Les créatures (`Creature List`) et les rencontres (`Encounter List`) ne sont plus ni lues pour la vue ni converties. La vue Importer ne les affiche plus, et le bilan `ENTITIES` du journal ne les compte plus. Une ligne d'information `NPC_SKIPPED` donne leur nombre.
- La vue Importer affiche les objets et les portes avec leur maillage (et non plus un cube). La vue Exporter affiche le BSP et les MD3 du staging, placés comme dans les entités.
- L'application gagne des options en ligne de commande pour les essais visuels sans clic : `--game kotor|kotor2 --module <m> --run-to preview|import|export|pk3 --camera "x y z yaw pitch" --screenshot <png> --exit`.

### Anglais (étape 06)
- Tout texte visible par l'utilisateur passe en anglais : interface, journal (les codes restent identiques), CLI, `README.md`, messages d'exception. Les libellés de l'interface deviennent `Preview`, `Import`, `Export`, `Create PK3`, les nœuds `KOTOR` et `KOTOR 2`, et le titre de la fenêtre `KOTOR → Jedi Academy`.
- Les commentaires de code, les specs, les plans et les rapports restent en français. Les noms de zones viennent du TLK du jeu installé, et restent donc dans la langue de l'installation.
- Dès l'étape 01, tout **nouveau** texte visible par l'utilisateur est écrit en anglais.

### Build portable (étape 07)
- `dotnet publish` en win-x64, autonome et en fichier unique (`PublishSingleFile`, `SelfContained`, `IncludeNativeLibrariesForSelfExtract`), pour l'application et la CLI. Un zip `Kotor2Ja-win-x64.zip` contient les deux exécutables, un `kotor2ja.config.json` d'exemple, le `README.md` et `THIRD-PARTY-NOTICES.md`.
- Chemins des jeux : la configuration est lue à côté de l'exécutable. À défaut, les jeux sont détectés via Steam (`libraryfolders.vdf`, dossiers `swkotor`, `Knights of the Old Republic II` et `Jedi Academy*\GameData\base`). Un panneau `Settings` de l'application permet de corriger et d'enregistrer les chemins. `workDir` vaut par défaut `%LOCALAPPDATA%\Kotor2Ja\work`.
- GitHub Actions : `.github/workflows/build-latest.yml`, déclenché par un push sur `master` et par `workflow_dispatch`, sur `windows-latest`. Il installe .NET 10, lance la compilation, les tests (les tests sur les jeux sont sautés) et la publication. Il remplace ensuite la release pré-publiée `latest` et son zip, via `gh release`.
- Un script local `scripts/publish.ps1` produit le même zip. Le dépôt n'a pas de remote GitHub : l'action n'est pas exécutée ici, on vérifie seulement le script local et la syntaxe du workflow.

## 3. Hors périmètre

- Reflets (cubemaps), animations, ciel animé, assemblage de modules.
- Portes pivotantes : toutes les portes montent.
- Placement des PNJ.
- Lightgrid pour l'éclairage des MD3 : le moteur se rabat sur une ambiante par défaut.

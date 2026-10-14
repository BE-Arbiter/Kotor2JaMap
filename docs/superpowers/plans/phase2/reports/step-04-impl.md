# Étape 04 — Application Kotor2Ja.App : rapport d'implémentation

## Fichiers
Copiés/adaptés depuis `SWGLFU-Map-importer\src\Tfu2Ja.App` :
- `src/Kotor2Ja.App/Kotor2Ja.App.csproj` : mêmes paquets (MonoGame.Framework.Native 3.8.5.1, MonoGame.Runtime.Windows.Vulkan 3.8.5.1, ImGui.NET 1.91.6.1) ; références Kotor.Core et Kotor.Pipeline ; `kotor2ja.config.json` de la CLI lié et copié à côté de l'exe.
- `ImGuiRenderer.cs` : copie, seul le namespace change (`Kotor2Ja.App`) ; en-tête MIT/origine conservé.
- `Program.cs`, `AppGame.cs` : adaptés. Le projet TFU n'avait ni caméra ni rendu 3D (réservés à son étape 05) : `FreeCamera`, `PreviewRenderer` et la vue sont écrits ici. Les réglages/PakSet de TFU sont supprimés.
Créés : `Ui/MainScreen.cs`, `Rendering/PreviewRenderer.cs`, `Rendering/FreeCamera.cs`.
Modifiés : `Kotor2Ja.sln` (projet ajouté), `THIRD-PARTY-NOTICES.md`, `README.md`.

## Choix de rendu
- Une `Texture2D` par clé de `Textures` (avec mipmaps calculés à la main, moyenne 2x2) ; clé absente -> texture « missing » du modèle, sinon damier magenta.
- Lot avec lightmap : `DualTextureEffect` (sommet maison `VertexDual`, deux UV). Sans lightmap : `BasicEffect` texturé, sans éclairage.
- `Cutout` : `AlphaTestEffect` (seuil alpha > 128). Limite : un lot Cutout AVEC lightmap est dessiné sans lightmap (DualTextureEffect n'a pas d'alpha test).
- `Additive` / `Alpha` : `BlendState.Additive` / `NonPremultiplied`, lecture de profondeur seule, dessinés après les opaques. `DoubleSided` : pas de culling.
- Culling des autres lots : `CullClockwiseFace` (faces avant = sens anti-horaire, repère droit KOTOR/GL). Je n'ai pas pu vérifier l'orientation à l'écran : case « Double face » ajoutée (désactive le culling) si des murs apparaissent manquants.
- Ciel (`IsSky`) : aplat bleu sombre (18,28,70), sans texture, sans culling, derrière la case « Ciel » décochée par défaut.
- Collisions : lignes (3 arêtes par triangle), vert, test de profondeur sans écriture + léger biais ; case « Collisions » décochée par défaut. (FillMode.WireFrame évité par prudence avec Vulkan.)
- Repères : cube de 16 unités par type (couleur par `EntityKind`), contour des polygones (déclencheurs) en lignes, étiquette `Label` via ImGui (liste de fond) à moins de 900 unités (300 max). Case par type présent. Apparition : cube jaune 1,5x + flèche de direction + étiquette « Apparition ».
- Viewport 3D = fenêtre moins la colonne gauche (330 px) ; l'upload GPU se fait dans `Update` quand la référence `Preview` change ; exception d'upload -> journal + vue vide.
- Caméra : repère JA, Z haut (`CreateLookAt` avec UnitZ), FOV 70, near 4, far 200000. Clic droit : souris recentrée et cachée pendant la rotation. Ignorée si ImGui capture souris/clavier. Vitesse de base 600 u/s, Maj x4, molette x1,2 par cran. Cadrage : sur l'apparition (yaw de `SpawnYaw`), sinon en retrait sur la diagonale des bornes. Une étape suivante du même module aux mêmes bornes garde la position de la caméra.
- Écarts à la spec (conflit de touches) : A = gauche (WASD) donc descendre = Ctrl ou C ; monter = Espace ou E ; gauche = Q ou A. Ajout de la case « Double face ».
- Liste des modules chargée en tâche de fond (lecture des noms de zone) ; « Chargement des modules... » en attendant.
- Le bouton d'étape suivante affiche « … » pendant le travail et est inactif si `!CanNext` ; masqué si `NextLabel` est null. `LastError` en rouge sous le journal.

## Build / tests
`dotnet build Kotor2Ja.sln` : 0 avertissement, 0 erreur. `dotnet test Kotor2Ja.sln` : tout vert (Cli 5, Compiler 68, Core 108, Pipeline 20). MonoGame Vulkan 3.8.5.1 compile et démarre sur cette machine (pas de repli DesktopGL).

## Essai de démarrage
`Kotor2Ja.App.exe` (Release) lancé en arrière-plan : toujours vivant après 10 s, aucune erreur ; la console n'affiche que l'initialisation Vulkan (volk, extensions d'instance). Processus ensuite arrêté. Aucun clic, rien écrit dans le dossier du jeu. Le rendu 3D réel n'a pas été vérifié.

## Liste de contrôle manuelle
1. `dotnet run -c Release --project src/Kotor2Ja.App` : fenêtre « KOTOR → Jedi Academy » 1600x900, redimensionnable. Colonne gauche : nœud « KOTOR » ouvert, liste `tar_m02aa — Taris - ...` apparaît après un court chargement, journal indiquant le nombre de modules.
2. Cliquer un module, puis « Prévisualiser » : le journal défile, la vue affiche la géométrie texturée avec lightmaps. Bouton « Importer » en bas à droite.
3. Caméra : clic droit maintenu + souris, ZQSD/WASD, Espace/Ctrl, Maj, molette ; elle ne bouge pas quand on tape dans l'interface.
4. Cases en haut à droite : « Collisions » (fil de fer vert), « Double face », cases par type (Placeable, Door, ...) avec cubes colorés et noms à proximité. « Ciel » n'apparaît qu'aux étapes Importer/Exporter (s'il y a un lot ciel) : cochée, boîte bleu sombre autour de la carte.
5. « Importer » puis « Exporter » : la vue change (nouveau modèle), bouton suivant devient « Exporter » puis « Créer PK3 » ; une erreur s'affiche en rouge.
6. Vérifier : murs non manquants (sinon cocher « Double face » et me le signaler : orientation des faces), transparences/cutouts corrects, lightmaps pas trop sombres/claires, point d'apparition jaune avec flèche.
7. « Créer PK3 » écrit `base\kotor_<carte>.pk3` dans le dossier du jeu (le jeu doit être fermé).

# Étape 02 — Revue (e10c285..1fcaad0)

## Conformité
- ✅ Signatures `EntityKind`, `ModuleEntity`, `ModuleEntitiesReader.Read` identiques au plan.
- ✅ Neuf listes du GIT lues avec les bons noms (`Placeable List`, `Door List`, `WaypointList`, `TriggerList`, `Creature List`, `SoundList`, `StoreList`, `Encounter List`, `CameraList`).
- ✅ Libellés cohérents avec les références que je connais (vérifications de mémoire, pas de relecture en ligne) : xoreos kotorbase / PyKotor `git.py` : `X/Y/Z`+`Bearing` pour placeables et portes ; `XPosition/YPosition/ZPosition`+`XOrientation/YOrientation` pour waypoint, creature, store ; `ResRef` pour le store ; `Position`+`Orientation` (quaternion) et `CameraID` pour la caméra ; `Geometry` avec `PointX/PointY/PointZ` pour les déclencheurs.
- ✅ Polygone converti en absolu (`pos + rel`), testé en synthétique.
- ✅ Lumières : nœuds `Light` via `Walk` + position de pièce, nom = nom du nœud.
- ✅ `GIT_MISSING`, `GIT_ENTRY_INVALID`, `ROOM_MODEL_UNREADABLE`, ligne `ENTITIES` présents ; `ROOM_MODEL_MISSING` ajouté (cohérent avec « rien en silence »).
- ✅ Tests demandés présents (`Reads_synthetic_git`, `Missing_git_is_logged`, `Real_tar_m02aa_entities` avec 71 lumières = somme `NODE_LIGHT`, boîte +5 m, sortie `out/entities-tar_m02aa.txt`) + `Invalid_entry_is_skipped_and_logged`.
- ❌ Point de revue imposé : « GIT incomplet (listes absentes) : le journal le signale » — non respecté (voir Important 1).
- ⚠️ Même point de revue : « aucune entité affichée » sans GIT — le lecteur renvoie quand même les lumières des pièces (conforme au plan, qui les tire du LYT, mais pas à la lettre de la consigne).

## Points forts
- Lecture défensive : champs de position absents, non finis, points de géométrie incomplets sont sautés avec un code.
- Conventions de Yaw documentées et cohérentes entre `Bearing` et vecteur d'orientation (`atan2(-x, y)`) ; vérification des 8 portes contre les doorhooks du LYT.
- Rapport honnête sur ce qui n'est pas vérifié (vecteur, rencontres) et détection d'un bug probable en phase 1 (`LayoutFile` quaternion w,x,y,z lu comme x,y,z,w).
- Test réel robuste (comptage croisé avec le journal de scène, boîte englobante).

## Problèmes

### Critique
Aucun.

### Important
1. `src/Kotor.Core/Entities/ModuleEntitiesReader.cs:136` — `git.GetList(spec.List)` renvoie une liste vide sans rien journaliser quand la liste est absente (ou présente mais d'un autre type que liste de structures). Un GIT incomplet (listes absentes, tronqué, ou libellé mal orthographié) produit donc des entités manquantes sans aucun code au journal ; seule la ligne `ENTITIES` montre des zéros, indiscernables d'une vraie liste vide. Viole le point de revue de l'étape et la règle « rien n'est ignoré en silence ». Correction : tester `git.Fields.ContainsKey(spec.List)` et journaliser un code (p. ex. `GIT_LIST_MISSING`, niveau info/avertissement) ; journaliser aussi le cas « présent mais pas une liste » ; ajouter un test « GIT sans listes » vérifiant ce code et l'absence d'exception. (Le plan dit « sans erreur » : aucune exception, mais un message de journal reste requis par la consigne de revue.)

### Mineur
1. `ModuleEntitiesReader.cs:162-164` — présence testée par `ContainsKey` mais lue par `Get<float>` : un champ de position d'un autre type (int, double) donne silencieusement 0 → entité placée à l'origine sans journal. Vérifier le type (`Fields[x] is float`) et lever `GIT_ENTRY_INVALID`.
2. `ModuleEntitiesReader.cs:165,168` — `Bearing` / `XOrientation` / `YOrientation` absents donnent Yaw 0 sans signal ; acceptable pour des champs facultatifs, mais à documenter ou journaliser.
3. `ModuleEntitiesReader.cs:129` — seules `InvalidDataException`, `ArgumentException`, `OverflowException` sont capturées pour un GIT illisible ; un GIT tronqué peut lever `EndOfStreamException` / `ArgumentOutOfRangeException` / `IndexOutOfRangeException` (selon `GffReader`) et faire échouer la lecture du module. Même remarque ligne 213 pour `MdlReader`.
4. `ModuleEntitiesReader.cs:122-143` — si le GIT est absent, les lumières sont tout de même renvoyées : à confirmer avec l'orchestrateur que c'est voulu pour l'étape Importer (le plan l'implique, la consigne de revue dit le contraire).
5. Tests : pas de test pour les lumières en synthétique ni pour `ROOM_MODEL_UNREADABLE` / `ROOM_MODEL_MISSING` ; Camera, Creature, Store, Sound, Encounter non couverts en synthétique (seul le test réel les touche indirectement, sans assertion sur eux). Conversion du vecteur d'orientation vérifiée seulement par un cas (-1,0) et non sur des données réelles.
6. Rapport : la table des libellés cite PyKotor « de mémoire » pour les rencontres ; la convention Yaw vecteur reste non vérifiée (déclaré). Le bug du quaternion de doorhook dans `LayoutFile` (phase 1) est à traiter avant d'utiliser les doorhooks (hors périmètre de cette étape).
7. `ENTITIES` est journalisé via `Warn` : informatif, mais pollue le décompte d'avertissements ; à confirmer côté UI.

## Évaluation
**Corrections nécessaires** — une correction Important (liste absente non signalée au journal), le reste est mineur.

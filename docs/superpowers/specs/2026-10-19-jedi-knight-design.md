# Jedi Knight : Dark Forces II → Jedi Academy (SWGL) — conception

Date : 2026-10-19. Statut : à valider par l'utilisateur.

## 1. Intention

Ajouter une troisième source à l'application et à la CLI : **Star Wars Jedi Knight: Dark Forces II** (Steam, app 32380),
installé chez l'utilisateur dans `C:\Program Files (x86)\Steam\steamapps\common\Star Wars Jedi Knight\`.
Le parcours est **le même que pour KOTOR et KOTOR 2** : on choisit un niveau dans l'arbre, `Preview`, puis `Import`,
`Export` et `Create PK3`. On obtient une map jouable dans SWGL.

Consigne de l'utilisateur : **« je veux TOUT voir »**. Tout ce qui est visible dans un niveau JK doit se retrouver dans la map JA.
On ne laisse rien de côté parce que c'est compliqué : ce qui n'est pas simple est converti quand même, au mieux.

## 2. Objectifs, non-objectifs, anti-objectifs

**Objectifs**
- Les **34 niveaux** : les 25 solo (`Episode\JK1.GOB`, `01narshadda` à `21bjerec`), les 6 multi (`JK1MP.GOB`) et les 3 CTF (`JK1CTF.GOB`).
- Toute la géométrie : secteurs et surfaces, textures et textures animées, surfaces transparentes ou translucides, ciel et horizon,
  eau, lave et zones mortelles.
- Tous les objets placés (*things*) qui ont un modèle 3D (`.3do`) ou un sprite, convertis en MD3 (ou en sprite), comme les objets KOTOR.
- **Tous les éléments mobiles** (portes, ascenseurs, ponts, plateformes, pièges, interrupteurs) deviennent des entités JA qui bougent.
  Les COG simples donnent des `func_door` ou `func_plat`. Les COG complexes (plusieurs étapes, déclenchés par un interrupteur, etc.)
  donnent une combinaison d'entités JA (`func_train` + `path_corner`, `trigger_*`, `target_*`, `func_button`…) qui reproduit le mouvement.
  Rien n'est figé faute de mieux sans être journalisé.
- **Objets à ramasser** (armes, munitions, soins, boucliers…) : convertis en leur équivalent JA quand il existe (§5.6).
- Le point d'apparition du joueur, et les points de départ multi et CTF.
- Les sons d'ambiance, comme pour KOTOR (sons de secteurs → `target_speaker`).

**Non-objectifs (pour le moment)**
- **Pas d'ennemis ni de PNJ** : les *things* de type `actor` (stormtroopers, Gran, Jedi noirs, boss…) ne sont pas placés.
  Une ligne d'information `JK_ACTOR_SKIPPED` donne leur nombre, comme `NPC_SKIPPED` pour KOTOR.
- Scénario, cinématiques, objectifs, dialogues, enchaînement des niveaux, sauvegardes.
- Mysteries of the Sith (non installé).
- Les niveaux de mods ou de GOB tiers.

**Anti-objectifs (échec même si « ça marche »)**
- Des morceaux qui manquent : un mur, une porte, un objet ou une texture visibles dans JK et absents dans JA.
  C'est le critère d'échec principal de tout le projet (voir mémoire projet).
- Une porte ou un ascenseur figé sans que le journal le signale.
- Un niveau où le joueur ne peut pas avancer (passage bloqué par une porte qui ne s'ouvre jamais, ascenseur absent).
- Une échelle fausse : marches infranchissables, portes trop basses.

## 3. Contraintes

- Même architecture et même workflow que KOTOR et SWTOR : `IWorkflowSteps`, `ModuleScene`, `CompileInput`, compilateur BSP maison,
  `Pk3Builder`. On réutilise ce qui existe (MD3, shaders, sous-modèles BSP de porte, visibilité par pièce, sons).
- Le jeu JK est **lu seulement**, jamais modifié. Seul `Create PK3` écrit dans `GameData\base`.
- Sortie : `jk_<niveau>.pk3` (préfixe `jk`, dossier d'assets `textures/jk/`, `models/jk/`). `<niveau>` = nom du `.jkl` sans extension
  (`jk_01narshadda.pk3`, `jk_m10.pk3`, `jk_c1.pk3`).
- Texte visible (UI, journal, CLI, README) en anglais ; commentaires, specs, plans et rapports en français (règle existante).
- Code original. Les formats JK sont documentés publiquement par la communauté (spécifications JKL/3DO/MAT/COG) ; on peut s'en servir
  comme référence, on ne copie pas de code sous licence incompatible. Les sources consultées sont citées dans un document de formats.
- Workflow TFU habituel : plan (`writing-plans`), agents frais par étape, rapports dans des fichiers, l'orchestrateur commite,
  fusion locale après accord de l'utilisateur. **Les agents ne lancent pas le jeu** : l'utilisateur teste en jeu.
- Les tests sur le vrai jeu sont sautés quand JK n'est pas installé ; les tests n'écrivent jamais dans le dossier JA.

## 4. Ce que l'utilisateur a décidé

- Même parcours et même rendu que pour KOTOR et KOTOR 2.
- Les 34 niveaux, solo, multi et CTF.
- Tout convertir, y compris ce qui n'est pas simple (mouvements complexes).
- Pas d'ennemis pour le moment.
- Objets à ramasser convertis en équivalent JA quand c'est possible.

## 5. Conception

### 5.1 Sources et lecture des archives

Contenu constaté de l'installation :

| Fichier | Contenu |
|---|---|
| `Episode\JK1.GOB` | 25 `.jkl` solo, 269 `.cog`, `episode.jk`, un `.uni` |
| `Episode\JK1MP.GOB` | 6 `.jkl` multi, 14 `.cog`, `episode.jk`, un `.uni` |
| `Episode\JK1CTF.GOB` | 3 `.jkl` CTF, 42 `.cog`, `episode.jk`, un `.uni` |
| `Resource\Res2.gob` | ~1 980 `.mat`, 477 `.3do`, 504 `.key`, 21 `.cmp`, 215 `.cog`, 102 `.snd`, 20 `.spr`, 49 `.pup`, `.ai*`, `jkl\static.jkl`, `.dat`, `.par` |
| `Resource\Res1hi.gob` | 644 `.wav`, 146 `.bm`, 27 `.sft`, 3 `.uni` |

- Format GOB : `"GOB "`, `u32` version, `u32` offset de l'index à l'octet 8 ; index = `u32` nombre puis, par entrée,
  `u32` offset, `u32` taille, `char[128]` nom (chemin avec `\`). Vérifié sur les cinq fichiers.
- Résolution d'une ressource (comme le moteur) : GOB de l'épisode du niveau d'abord, puis `Res2.gob`, puis `Res1hi.gob`.
  Recherche insensible à la casse, par nom de fichier dans son dossier conventionnel (`mat\`, `3do\`, `cog\`, `misc\`…).
- `static.jkl` (dans `Res2.gob`) fournit les gabarits et ressources communs à tous les niveaux.

### 5.2 Liste des niveaux et noms

- Nouveau `KotorGame.JediKnight` (préfixe `jk`, nom affiché `Jedi Knight`). L'arbre de l'application gagne un nœud `Jedi Knight`
  avec trois sous-groupes `Single player`, `Multiplayer`, `CTF`.
- Les noms lisibles viennent du jeu (`episode.jk` et chaînes `.uni`) quand ils existent, sinon du nom du fichier.
- Détection du dossier : via Steam (même mécanisme que les autres jeux, dossier `Star Wars Jedi Knight`), clé de config
  `jediKnightDir`, champ dans le panneau `Settings`. Jeu absent : nœud grisé, comme les autres.
- CLI : `list-modules` liste aussi JK ; `export-ja` accepte `--game jk`.

### 5.3 Géométrie (`.jkl`)

Le `.jkl` est un texte en sections (`SECTION: GEORESOURCE`, `SECTORS`, `THINGS`, `COGS`, `TEMPLATES`, `MATERIALS`, `MODELS`…).
- **Surfaces** : polygones convexes indexés sur la liste de sommets du monde, avec matériau, coordonnées de texture, drapeaux
  (surface, face, géométrie, éclairage, texture), *adjoin* éventuel, lumière additionnelle et intensité par sommet.
- **Rendu** : chaque surface avec un matériau et une géométrie visible donne une surface de rendu. Une surface d'*adjoin* sans matériau
  (portail invisible) ne donne rien. Une surface d'*adjoin* avec matériau (grille, champ de force, vitre) est rendue des deux côtés,
  avec la transparence de son matériau.
- **Collision** : une surface est solide sauf si son *adjoin* est franchissable. Les *adjoins* bloquants (drapeaux) restent solides.
- **Éclairage** : JK éclaire par intensité de sommet, lumière de secteur et `extralight`. On garde cet éclairage d'origine (couleurs de
  sommets ou lightmap équivalente) plutôt que de le recalculer, pour que l'ambiance ressemble à JK. Le choix technique exact est au builder.
- **Visibilité** : un secteur JK = une pièce au sens de `ModuleScene.Room` ; les *adjoins* donnent les portails. On réutilise
  `RoomVisibility` (cluster par secteur ou par groupe de secteurs), dans les limites de `Limits`.
- **Ciel** : les surfaces aux drapeaux « horizon sky » et « ceiling sky » deviennent un shader de ciel JA construit à partir du matériau
  de ciel du niveau (paramètres du `HEADER` : distance d'horizon, décalage du ciel de plafond…).
- **Eau, lave, zones mortelles** : les secteurs sous-marins deviennent des volumes d'eau JA (`surfaceparm water`, brouillard de couleur
  du secteur) ; les surfaces ou secteurs qui tuent ou blessent deviennent `trigger_hurt` ; la lave a son shader lumineux.
- **Échelle** : calée sur le joueur. La taille de Kyle dans JK correspond à celle du joueur JA, puis on vérifie sur les marches (≤ 18 u)
  et les portes. On garde la leçon de SWTOR : une constante unique et justifiée, ajustée après le test en jeu de l'utilisateur.

### 5.4 Textures (`.mat`, `.cmp`)

- Les `.mat` sont en couleurs indexées 8 bits (et parfois 16 bits), avec une palette `.cmp` choisie par le niveau (section `MATERIALS`
  et colormaps de `GEORESOURCE`). Conversion en TGA ou PNG 32 bits dans `textures/jk/`.
- Un `.mat` à plusieurs cellules (*cels*) donne un shader `animMap` quand le niveau l'anime, et sinon la cellule de départ.
  Les cellules changées par un COG (interrupteurs, voyants) suivent la même règle (§5.5).
- Les matériaux de couleur unie donnent une texture de couleur unie.
- La transparence (index 0 sur les matériaux transparents, drapeaux de face translucide) donne `alphaFunc` ou `blendFunc`.
- La lumière émise (matériaux pleine luminosité, `extralight`) donne une passe sans éclairage, comme les additifs SWTOR.

### 5.5 Objets placés et éléments mobiles (`THINGS`, `TEMPLATES`, `COGS`)

**Gabarits.** Un *thing* hérite d'un gabarit, qui hérite d'un autre (`static.jkl`, puis le gabarit maître). On résout toute la chaîne
pour connaître son type, son modèle `.3do`, son sprite, ses drapeaux, son mouvement et ses images clés (*frames*).

**Par type de thing :**

| Type JK | Conversion |
|---|---|
| `player` (départ du joueur) | `info_player_start` (solo), `info_player_deathmatch` (multi), points d'équipe (CTF) |
| `actor` | **ignoré** (non-objectif), compté dans `JK_ACTOR_SKIPPED` |
| `item`, `weapon` à ramasser | équivalent JA (§5.6) ; sinon modèle JK posé en décor |
| décor avec `.3do` (`cog`, `ghost`, `corpse`, `debris`…) | MD3 `misc_model_static`, collision selon les drapeaux du thing |
| sprite (`.spr`) | sprite JA (`autosprite`) ou petit MD3 face caméra |
| lumières, générateurs de particules, sons | lumière ponctuelle, effet le plus proche, `target_speaker` |
| explosifs (barils…) | MD3 décor (pas de destruction pour le moment, journalisé) |

**Modèles `.3do` → MD3.** Texte : géosets, maillages, nœuds hiérarchiques. On prend le géoset de plus haut détail, on applique la
hiérarchie, une surface MD3 par matériau, mêmes règles d'échelle et de limite ±512 que les objets KOTOR.

**Éléments mobiles.** Dans JK, une porte, un ascenseur ou un pont est un *thing* avec un modèle et des positions (*frames*),
déplacé par un script COG (`MoveToFrame`, interrupteurs, capteurs de secteur, minuteries…). Chaque instance de COG du niveau est lue
avec ses paramètres (things, secteurs, surfaces, sons, vitesses, délais).
- Chaque thing mobile devient une entité JA déplaçable avec un sous-modèle BSP de collision (le mécanisme des portes KOTOR) et
  `model2` = son MD3.
- **Comportements reconnus** (catalogue construit à partir des COG réellement utilisés par les 34 niveaux) : porte qui s'ouvre au
  toucher → `func_door` ; ascenseur à deux positions → `func_plat` ou `func_door` ; ascenseur ou pont appelé par un interrupteur →
  `func_train` / `func_door` ciblé par un `func_button` ou `trigger_multiple` ; mouvement en plusieurs étapes → `func_train` +
  `path_corner` ; interrupteur qui change une texture → cellule du shader ou `target_*` équivalent ; porte verrouillée par une clé →
  ouverte ou débloquée (pas de clés à trouver, la porte doit rester franchissable).
- **Comportement non reconnu** : on reconstruit quand même le mouvement à partir des *frames* du thing (`func_train` entre ses positions,
  déclenché à l'approche ou en boucle selon ce que le COG semble faire) et on journalise `JK_COG_APPROX` avec le nom du COG.
- Un mouvement qu'on ne peut pas reproduire du tout donne `JK_COG_UNSUPPORTED` avec le thing figé **dans la position qui laisse le
  passage ouvert**. Le bilan du journal compte ces cas : l'objectif est zéro sur les 34 niveaux.
- Les surfaces mobiles de la géométrie (textures qui défilent, `SlideSurface`) donnent un `tcMod scroll`.

### 5.6 Objets à ramasser → JA

Table de départ (le builder la complète en listant les gabarits réellement présents dans les niveaux) :

| JK | JA |
|---|---|
| Bryar pistol | `weapon_bryar_pistol` |
| Stormtrooper rifle | `weapon_blaster` |
| Thermal detonator | `weapon_thermal` |
| Bowcaster | `weapon_bowcaster` |
| Repeater | `weapon_repeater` |
| Rail detonator | `weapon_rocket_launcher` |
| Sequencer charge | `weapon_trip_mine` |
| Concussion rifle | `weapon_concussion_rifle` |
| Lightsaber | `weapon_saber` |
| Energy cell / power cell / raildets / détonateurs | `ammo_blaster` / `ammo_powercell` / `ammo_rockets` / `ammo_thermal` |
| Bacta, santé | `item_medpak_instant` |
| Bouclier | `item_shield_sm_instant` ou `item_shield_lrg_instant` |
| Clés, lunettes, sac de ravitaillement, objets sans équivalent | modèle JK en décor (`misc_model_static`), journal `JK_ITEM_NO_EQUIVALENT` |

### 5.7 Aperçu, journal, CLI

- `Preview` montre la géométrie texturée, les marqueurs de départ, les objets et les éléments mobiles avec leur vrai modèle,
  comme pour KOTOR. `Import` et `Export` affichent les mêmes vues que KOTOR (scène importée, BSP + MD3 du staging).
- Chaque cas sauté ou approché a un code stable (`JK_*`) dans le journal et dans `<map>.journal.txt`.
- CLI : `--game jk` sur les commandes existantes, et une commande de diagnostic `jk-dump-level <niveau>` (secteurs, things, COG
  avec leur comportement reconnu ou non), utile pour construire le catalogue de COG.

### 5.8 Organisation du code (proposition)

- `src/Jk.Core` : lecture GOB, JKL, MAT/CMP, 3DO, `.key`, COG (instances et symboles), gabarits, `.uni`/`episode.jk`.
  Pas de dépendance au pipeline.
- `src/Jk.Pipeline` : construction de la `ModuleScene`, conversion des things et des COG en entités, workflow (`JkWorkflowSteps`),
  branché dans l'application comme SWTOR via le composeur `GameWorkflowSteps`.
- Tests : `tests/Jk.Core.Tests`, `tests/Jk.Pipeline.Tests`, avec données synthétiques et tests d'intégration sur le jeu installé
  (sautés sans JK), dont un test qui charge les 34 niveaux sans erreur.

## 6. Critères de réussite

1. Les 34 niveaux s'exportent en `jk_<niveau>.pk3` sans erreur, depuis l'application et la CLI.
2. Bilan du journal par niveau : aucun mur, objet ou texture visible sauté sans code ; `JK_COG_UNSUPPORTED` = 0 visé.
3. L'utilisateur parcourt en jeu au moins `01narshadda`, un niveau avec ascenseurs et un niveau multi, et juge l'échelle, les
   portes, les ascenseurs et la complétude.

## 7. Laissé au builder

- Le découpage en étapes du plan et l'ordre (on conseille : formats → géométrie et textures → objets → éléments mobiles → objets à
  ramasser → application).
- Le moyen technique de garder l'éclairage JK, la représentation des sprites, le choix exact des entités JA pour chaque COG reconnu.
- La valeur de l'échelle (calée sur le joueur, puis ajustée après test en jeu).
- La branche : `feature/jk-maps` depuis `feature/swtor-maps` (§9).

## 8. Choix faits par l'orchestrateur (à vérifier par l'utilisateur)

Ces règles ne viennent pas de l'utilisateur ; ce sont des choix de conception, à corriger si besoin.

- **Événements liés aux ennemis absents.** Un COG qui ouvre une porte ou lance un ascenseur à la mort d'un ennemi ou à l'arrivée d'un
  acteur scripté est déclenché **à l'approche du joueur** (`trigger_multiple` sur le secteur concerné). Journal `JK_COG_ACTOR_TRIGGER`.
  But : le joueur peut toujours avancer.
- **Pouvoirs de la Force.** On ne change rien au gameplay de SWGL ; les passages qui demandent un saut de Force restent tels quels.
  Si l'utilisateur bloque en jeu, on avisera.
- **Niveaux multi et CTF dans SWGL (solo).** On écrit un `info_player_start` au premier point de départ, **et** les entités multi
  (`info_player_deathmatch`, `team_CTF_*`) pour tous les autres. La map est donc jouable en solo et reste correcte pour le multi.
- **État initial.** On convertit le niveau tel qu'il est au chargement, plus tous les éléments mobiles. Les things créés en cours de
  partie par un COG (`CreateThing`) ne sont pas placés (journal `JK_RUNTIME_THING`). La géométrie cachée au départ puis révélée par un COG
  est convertie (visible), journal `JK_COG_REVEAL`.
- **Limites du BSP.** Si un niveau dépasse `Limits`, on peut fusionner des secteurs pour la visibilité ou découper les surfaces. On ne
  supprime jamais de géométrie visible.
- **Portes à clé.** Elles s'ouvrent sans clé (pas de clés à trouver), pour que le passage reste franchissable.
- **Sons.** Les sons d'ambiance des secteurs sont convertis comme pour KOTOR. **La musique n'est plus hors périmètre** (demande de l'utilisateur, étude `plans/jk/reports/M1.md`) : JK joue des pistes CD (`MUSIC\TrackNN.ogg`) choisies par un `PlaySong(début, fin, retour)` du COG du niveau ; JA ne lit que le MP3 et le wav PCM, jamais l'ogg. Chaque niveau qui a une musique reçoit donc un wav stéréo 22 050 Hz 16 bits `music/jk/<niveau>.wav` (pistes du cycle décodées par NVorbis et mises bout à bout dans l'ordre de JK), joué en boucle par la clé `music` du worldspawn (ni `.mus` ni entité). Sans musique exploitable (pistes absentes de l'édition Steam : niveaux 06a et 09 ; aucun script : multijoueur et CTF), le journal le dit (`JK_MUSIC_NONE`). `SetMusicVol` (fondus, baisse pendant les dialogues, musique du bar du niveau 01) n'a pas d'équivalent : volume constant (`JK_MUSIC_VOLUME_SCRIPT`).

## 9. Réponses de l'utilisateur aux questions ouvertes

- **Branche** : `feature/jk-maps`, créée depuis `feature/swtor-maps` une fois les changements en cours de cette branche commités.
- **Objets animés** : ils bougent dans JA. Les `.3do` animés par des `.key` (ventilateurs, machines, tourelles…) donnent des MD3 à
  plusieurs images, posés avec une entité JA qui joue l'animation en boucle (ou `func_rotating` pour une rotation pure). C'est une
  étape à part du plan.

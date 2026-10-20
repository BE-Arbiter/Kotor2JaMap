# Codes du journal Jedi Knight (`JK_*`)

Chaque conversion d'un niveau JK écrit ses messages dans le journal d'import (`<carte>.journal.txt`, fenêtre de l'application, sortie de `export-ja`).
Les codes sont stables : un code ne change pas de sens. Les textes des messages sont en anglais ; une entrée est le plus souvent agrégée par niveau (un message avec un nombre), sauf mention contraire.

Niveaux :

- **info** : bilan de ce qui a été converti, rien de perdu ;
- **avertissement** : approximation ou élément non converti (le niveau reste jouable) ;
- **erreur** : l'étape s'arrête (`PipelineStepException`, code dans le message d'erreur).

Les codes communs à tous les jeux (`TEXTURE_MISSING`, `SPAWN_FALLBACK`, `BSP_WARNING`, `COLLISION_DEGENERATE`, `LEGACY_PK3`...) ne sont pas listés ici.
La colonne « Perte » dit ce qui manque en jeu quand le code apparaît. Variables d'environnement (pas des codes) : `JK_DIR` (dossier du jeu), `JK_PATH` (ancien nom, repli de `jk-dump-level`).

## Scène, géométrie, lumière

| Code | Niveau | Sens | Perte |
|---|---|---|---|
| `JK_SCENE` | info | Bilan de la scène : secteurs, pièces, maillages, triangles, surfaces solides et franchissables, échelle. | aucune |
| `JK_SECTION_SKIPPED` | avertissement | Une section du JKL (nom donné) n'est pas convertie. | la section |
| `JK_TEMPLATE_UNRESOLVED` | avertissement | Gabarit absent ou circulaire ; les choses qui l'utilisent gardent ce qui a pu être résolu. | propriétés du gabarit |
| `JK_CMP_MISSING` | avertissement | Colormap absente ou illisible ; la palette grise par défaut sert aux textures. | couleurs des textures |
| `JK_STATIC_UNREADABLE` | avertissement | `static.jkl` illisible ; ses gabarits sont ignorés. | gabarits communs |
| `JK_VIS_MERGED` | avertissement | Trop de clusters ou de pièces : des secteurs voisins sont fusionnés en groupes (aucune géométrie retirée). | précision de la visibilité |
| `JK_SURFACE_ORPHAN` | avertissement | Surfaces sans secteur, placées dans la pièce `orphan`. | aucune |
| `JK_SURFACE_DEGENERATE` | avertissement | Surfaces dégénérées ou invalides ignorées. | ces surfaces |
| `JK_SURFACE_HIDDEN` | info | Surfaces sans matériau ou en mode géométrique 0 : non dessinées (portails invisibles), elles collent sauf adjoin franchissable. | aucune |
| `JK_GEO_APPROX` | avertissement | Mode géométrique 1 à 3 (points, filaire, couleur unie) : dessiné texturé. | apparence |
| `JK_SKY_APPROX` | avertissement | Ciel approché par un seul shader par niveau (horizon et plafond). | détail du ciel |
| `JK_TRANSLUCENT_APPROX` | avertissement | Surfaces translucides : mélange fixe à 50 %. | niveau de transparence |
| `JK_STAIR_RAMP` | info | Bilan des rampes de marche : nombre de rampes de collision marchables ajoutées sur les vraies marches trop hautes pour le pas de JA (le rendu ne change pas), dont les « seuils de passage » (contremarche isolée de plus de 19 u JA sous un passage franchissable, avec un palier d'au moins 0,6 unité JK au-dessus : sans rampe elle murerait le passage), nombre de contremarches hautes isolées (comptoirs, rebords, caisses) laissées bloquantes, nombre de contremarches plus étroites que le joueur, nombre de rampes trop raides. | aucune |
| `JK_ADJOIN_COG_OPENED` | avertissement | Adjoins bloquants dans le fichier que des COG peuvent ouvrir (`SetAdjoinFlags` : champs de force, grilles, vitres cassables, portes sans mouvement) : rendus franchissables pour qu'aucun passage ne reste fermé. | la barrière ne bloque plus (reste visible) |
| `JK_NO_PLAYER_START` | avertissement | Aucun départ du joueur : entrée à l'origine. | position d'entrée |

## Effets de surface

| Code | Niveau | Sens | Perte |
|---|---|---|---|
| `JK_ANIM_MATERIAL` | info | Surfaces dont les cellules du matériau tournent en `animMap` (`SurfaceAnim`/`MaterialAnim` au démarrage). | aucune |
| `JK_SCROLL_SURFACE` | info | Surfaces dont la texture défile (`SlideWall`/`SlideSurface` au démarrage). | aucune |
| `JK_SLIDE_SKIPPED` | avertissement | Surface qui glisse sans pouvoir défiler (axes de texture indéfinis, ou glissement perpendiculaire). | le défilement |
| `JK_SURFACE_EFFECT_DEFERRED` | avertissement | Glissements ou animations lancés par événement (interrupteur, minuterie), non convertis. | ces effets |
| `JK_WATER_SURFACE` | info | Surfaces translucides entre air et eau : aspect de l'eau ondulante. | aucune |
| `JK_WATER_VOLUME` | info | Secteurs sous l'eau trouvés (aspect, teinte) ; le volume nageable vient de `JK_WATER_BRUSH`, sans brouillard sous-marin. | brouillard sous l'eau |
| `JK_WATER_BRUSH` | info | Volumes d'eau posés (contenu eau, on y nage). | brouillard sous l'eau |
| `JK_LAVA_SURFACE` | info | Surfaces de liquide dangereux : pleine lumière et ondulation. | aucune |
| `JK_HURT_VOLUME` | info | Zones qui blessent ou tuent (secteurs ou surfaces touchées) devenues des volumes `trigger_hurt`. | aucune |
| `JK_HURT_TRIGGER` | info | Volumes de dégâts posés (brushes de déclenchement non solides). | aucune |
| `JK_VOLUME_DEGENERATE` | avertissement | Volumes d'eau ou de dégâts à brush dégénéré : non posés. | ces zones |
| `JK_HEAL_SECTOR_SKIPPED` | avertissement | Secteurs de soin non convertis (pas d'équivalent JA). | le soin |
| `JK_DAMAGE_COG_UNRESOLVED` | avertissement | COG qui appelle `DamageThing` sans secteur ni surface liée (dégâts pilotés par événements : boss, vitres) : ignoré. | ces dégâts |
| `JK_SECTOR_THRUST_SKIPPED` | avertissement | Poussée de secteur non convertie : celle que des événements changent (`SectorThrust` hors du message `startup` : interrupteurs, minuteries, pulses ; arguments non résolubles), avec les comptes d'instances et de scripts ; ou, pour la poussée convertie (`JK_SECTOR_THRUST`), des zones non posées (secteur absent ou dégénéré, plus de sous-modèle, ou poussée trop faible : moins de 1 u/s, car `speed 0` enverrait le joueur à la distance entière de la cible). | la poussée |

## Choses et objets

| Code | Niveau | Sens | Perte |
|---|---|---|---|
| `JK_OBJECTS` | info | Bilan des choses placées (ramassables, décors, boucles, toupies, lumières...). | aucune |
| `JK_ACTOR_SKIPPED` | avertissement | Acteurs (ennemis, PNJ, boss) non placés : cette version n'a pas d'ennemis. | tous les ennemis |
| `JK_ITEM_NO_EQUIVALENT` | avertissement | Objets sans équivalent JA, placés comme modèles seuls. | l'effet de ramassage |
| `JK_EXPLOSIVE_STATIC` | avertissement | Objets explosifs ou cassables (barils, caisses) placés en modèles statiques, sans collision. | la destruction |
| `JK_DECOR_COLLISION` | info | Décors solides (`collide=3`) avec collision par les faces de leur modèle. Les ventilateurs (choses qui tournent : `angvel`, rotation pure) n'en ont jamais, même posés sans tourner. | aucune |
| `JK_MISC_MODEL_LIMIT` | avertissement | Plus de `misc_model_static` que le moteur n'en accepte : modèles en trop non placés. | ces modèles |
| `JK_PARTICLE_SKIPPED` | avertissement | Particules non converties (aucune dans les 34 niveaux). | les particules |
| `JK_SPRITE_SKIPPED` | avertissement | Sprites non convertis (aucun dans les 34 niveaux). | les sprites |
| `JK_ROTATION_APPROX` | avertissement | Rotation qui ne suit pas un axe du modèle, ou modèle trop grand pour un `func_rotating` : axe le plus proche, ou sans rotation. Le modèle reste visible et traversable. | la rotation exacte |
| `JK_RUNTIME_THING` | avertissement | Scripts qui créent des choses en cours de jeu (explosions, débris, bonus, ennemis) : non placées. | ces apparitions |
| `JK_SECRET_AREAS` | info | Zones secretes (`00_SecretAreas.cog`, secteurs lies `secret1`..`secret20`) converties : un `trigger_multiple` non solide par secteur (joueur seul, une fois) qui vise un unique `target_secret` (`count` = total du niveau : compteur « secrets found » de JA, son et « Secret Area! »). Texte et son JK remplaces par ceux de JA. | l'étoile de Force de fin de niveau (tous les secrets trouvés) |
| `JK_SECRET_SKIPPED` | avertissement | Zones secretes non posées : secteur absent du niveau, secteur dégénéré ou plus de sous-modèle disponible. | ces zones |
| `JK_SECTOR_THRUST` | info | Poussée continue de secteur convertie (ventilateurs, courants d'air, conduits : `THRUST` du fichier et `SectorThrust` du seul message `startup`, dont `00_sectorthrust`) : un `trigger_push` RELATIVE et PLAYERONLY (spawnflags 17 : sans PLAYERONLY, les entités non-client en mouvement recevraient la position monde de la cible comme vitesse) non solide par secteur (sous-modèle `CONTENTS_TRIGGER`) vers un `target_position` lointain posé dans la direction de la poussée (un par groupe de même poussée). Vitesse JA = intensité JK × 125 u/s, plafonnée à 1000 ; la poussée de JA remplace la vitesse du joueur au lieu de s'y ajouter. Non vérifié en jeu. | la précision de la poussée (on ne peut pas la remonter) |
| `JK_START_MULTI_SKIPPED` | info | Départs multijoueur d'un niveau solo non placés. | aucune |
| `JK_CTF_TEAMS` | info | Départs de capture du drapeau par équipe (équipe du drapeau le plus proche). | aucune |
| `JK_SECTOR_SOUND` | info | Secteurs avec son : haut-parleurs en boucle placés au centre des secteurs voisins fusionnés. | aucune |
| `JK_SOUND_SKIPPED` | avertissement | Groupes de sons non placés : plus de haut-parleurs que le plafond, même après fusion. | ces sons |
| `JK_SOUND_MISSING` | avertissement | Son absent des archives du jeu. | ce son |
| `JK_SOUND_UNREADABLE` | avertissement | Son illisible : le fichier est copié tel quel. | peut-être le son |
| `JK_MUSIC` | info | Musique du niveau convertie : `PlaySong(début, fin, retour)` du COG, pistes CD (`MUSIC\TrackNN.ogg`) dans l'ordre de lecture, un wav 22 050 Hz stéréo 16 bits `music/jk/<niveau>.wav` (durée, taille) joué en boucle par la clé `music` du worldspawn. Non vérifié en jeu. | aucune |
| `JK_MUSIC_NONE` | info / avertissement | Pas de musique : aucun `PlaySong` dans le niveau (multijoueur, CTF), ou aucune des pistes demandées n'est lisible (niveaux 06a et 09 : pistes 6-7 et 2-4 absentes de l'installation Steam). | la musique |
| `JK_MUSIC_TRACK_MISSING` | avertissement | Pistes demandées par le COG sans fichier dans `MUSIC` : elles sont omises de la boucle. | ces pistes |
| `JK_MUSIC_UNREADABLE` | avertissement | Piste ogg inutilisable : lecture ou décodage en erreur (toute exception, jamais fatale), ou piste qui ferait dépasser la limite RIFF de 2 Go du wav (« too long ») : omise de la boucle. | cette piste |
| `JK_MUSIC_LOOP_APPROX` | avertissement | `PlaySong` dont le début n'est pas dans le cycle (aucun niveau réel) : les pistes `début..fin` sont jouées dans l'ordre en boucle. | l'intro distincte |
| `JK_MUSIC_VOLUME_SCRIPT` | avertissement | Des COG changent le volume de la musique (`SetMusicVol` : fondus, baisse pendant les dialogues ; niveau 01 : muette sauf dans le bar) ; JA n'a pas d'équivalent, la musique joue à volume constant partout. | les fondus et la musique localisée |

## Modèles et animations

| Code | Niveau | Sens | Perte |
|---|---|---|---|
| `JK_MODEL_MISSING` | avertissement | Modèle 3DO absent des archives ; la chose est placée sans modèle. | le modèle |
| `JK_MODEL_UNREADABLE` | avertissement | Modèle illisible ou à coordonnées non finies. | le modèle |
| `JK_MODEL_EMPTY` | avertissement | Modèle sans géométrie ou sans face dessinable. | le modèle |
| `JK_MODEL_MD3_FAILED` | avertissement | MD3 non écrit (message de l'erreur). | le modèle |
| `JK_MODEL_FACES_HIDDEN` | avertissement | Faces en mode non dessiné (invisible, points, filaire) non exportées. | ces faces |
| `JK_MODEL_FACE_MATERIAL` | avertissement | Faces sans matériau valide non exportées. | ces faces |
| `JK_KEY_MISSING` | avertissement | Animation `.key` introuvable ; modèle sans animation. | l'animation |
| `JK_KEY_UNREADABLE` | avertissement | Animation illisible ; modèle sans animation. | l'animation |
| `JK_KEY_TRUNCATED` | avertissement | Animation de trop d'images : seules les premières sont gardées. | la fin de l'animation |
| `JK_KEY_NODE_UNMATCHED` | avertissement | Noeuds du modèle sans piste d'animation : pose de repos. | ces noeuds immobiles |

## Éléments mobiles et COG

| Code | Niveau | Sens | Perte |
|---|---|---|---|
| `JK_MOVERS` | info | Bilan des choses mobiles converties, par type, et des choses fixes. | aucune |
| `JK_COG_APPROX` | avertissement | Mouvement de COG approché (rotation ignorée, train au lieu d'arrêts à la demande, interrupteur absent...) ; le mobile reste visible et utilisable. Une entrée par chose. | fidélité du mouvement |
| `JK_COG_UNSUPPORTED` | avertissement | Mouvement non reproduit : la chose reste visible en pose ouverte (passage ouvert). Une entrée par chose. | le mouvement |
| `JK_COG_ACTOR_TRIGGER` | avertissement | Porte ouverte à la mort d'un acteur (absent) : s'ouvre à l'entrée du joueur dans le secteur. Une entrée par chose. | le déclencheur |
| `JK_COG_CEL_STATIC` | info | Surfaces dont la cellule change sous un script (voyants, panneaux, grilles coupées) : première cellule gardée. | l'animation d'état |
| `JK_COG_MISSING` | avertissement | Scripts COG absents des archives du jeu : comportement inconnu. | ce comportement |
| `JK_COG_MALFORMED` | avertissement | Script COG mal formé ou tronqué : ce qui est lisible est utilisé. | une partie du script |
| `JK_MOVER_MODEL_SCALE` | avertissement | Modèle trop grand pour un MD3 : réduit, la boîte de collision garde la taille réelle. | taille visuelle |
| `JK_MOVER_NO_MODEL` | info | Choses mobiles sans modèle 3D (repères invisibles) : non placées. | aucune |
| `JK_MOVER_NO_THING` | avertissement | Un comportement cite une chose que le niveau ne contient pas : ignoré. | ce comportement |
| `JK_SUBMODEL_LIMIT` | avertissement | Plus de sous-modèles BSP que le moteur n'en accepte : les choses en trop sont placées en modèles simples, sans mouvement ni collision. | le mouvement de ces choses |
| `JK_MOVER_SOUND` | info | Sons des mobiles branchés : la classe de sons du gabarit (`soundclass=med_door.snd` : `startmove`, `stopmove`) devient des `target_speaker` joués une fois, tirés par les clés `target` (le mobile part) et `opentarget` / `closetarget` (il arrive) du `func_door` ou `func_plat` ; les pièces voisines d'une même porte partagent leurs haut-parleurs (seule la première les tire) ; une classe qui n'a qu'un `moving` en boucle le joue une fois au départ. Avec les nombres de mobiles, de haut-parleurs et de wav copiés sous `sound/jk/`. | aucune |
| `JK_MOVER_SOUND_SKIPPED` | avertissement | Sons de mobiles non convertis : classe de sons absente des archives ; boucle `moving` d'une classe qui a aussi un `startmove` (rien n'avertit une porte JA du début du retour, une boucle ne pourrait pas s'arrêter) ; trains (un train JA rejoue son `noise` sans l'arrêter, et sa clé `target` est le coin suivant) ; sons que le COG joue lui-même (clic d'interrupteur, alarme) ; mobiles qui ont déjà une clé `target` / `opentarget` / `closetarget` (non écrasée : leur son de départ ou d'arrivée est abandonné). Les wav absents sont journalisés `JK_SOUND_MISSING`. | ces sons |

## Erreurs (arrêt de l'étape)

| Code | Niveau | Sens |
|---|---|---|
| `JK_LEVELS_UNREADABLE` | erreur | Impossible de lister les niveaux du dossier du jeu. |
| `JK_LEVEL_NOT_FOUND` | erreur | Le niveau demandé n'existe pas (noms : `01narshadda`, `m10`, `c1`...). |
| `JK_LEVEL_UNREADABLE` | erreur | Le niveau ne peut pas être lu. |
| `JK_ASSEMBLY_UNSUPPORTED` | erreur | L'assemblage de plusieurs niveaux n'existe pas pour JK : choisir un seul niveau. |

## Non implémentés

`JK_COG_REVEAL` (spec §8, `SetSurfaceGeoMode` : aucun COG du jeu ne l'utilise) et `JK_PARTICLE_APPROX` (spec §5.5, particules approchées) ne sont pas émis : les particules et sprites sont seulement signalés par `JK_PARTICLE_SKIPPED` et `JK_SPRITE_SKIPPED`.

# Catalogue des COG des 34 niveaux Jedi Knight (tâche 8)

Date : 2026-10-20. Généré par le classifieur (`CogClassifier.ClassifyLevel`, `src/Jk.Pipeline/Movers`) sur les 34 niveaux installés ; la commande
`kotor2ja jk-dump-level <niveau>` donne la même liste par niveau (instances numérotées, comportement retenu).

## Méthode

- Les fichiers COG sont ceux des instances de la section `COGS` de chaque `.jkl`, lus dans le GOB de l'épisode puis `Res2.gob` (`cog\<fichier>`).
- Une instance est liée à son script (valeurs positionnelles, symboles `local` et `message` exclus), les messages sont développés (`call`, chute dans l'étiquette suivante) et chaque `MoveToFrame` est rattaché au message qui le lance, avec ses conditions.
- Règles du classifieur (écrites à partir des COG les plus fréquents : `00_door`, `00_elevcall`, `00_elev_switch`, `00_doorswitch`, `00_std_elev`, `C3_MTRIGELEV`, `C3_AUTODOOR`...) :
  - `killed` : `ActorTriggered` (l'acteur est absent : déclenché à l'approche, `JK_COG_ACTOR_TRIGGER`) ;
  - `activate`/`activated`/`touched` avec une surface citée (`SetWallCel`, condition sur l'émetteur) : `SwitchMove` (une par surface, avec les images envoyées puis le retour) ; sans surface citée : `TouchDoor` ;
  - `entered`/`crossed` : `TouchDoor` si le COG ouvre et ferme les adjoins d'un secteur (`SectorAdjoins`), sinon `TwoStopLift` ;
  - étapes successives (un message de suite va vers une image nouvelle, ou deux déplacements sans condition vers deux images) : `MultiStepPath` ; va-et-vient lancé par le COG seul (`startup`, `pulse`) : `MultiStepPath` en boucle ;
  - un mouvement lancé seulement par `timer`/`pulse`/`arrived` prend le déclencheur des messages qui arment la minuterie ou déplacent autre chose ;
  - `RotatePivot`/`SetThingRotVel`, déclencheurs `damaged`/`userN`/autres : `Approximate` (images du thing gardées comme trajet) ; sans images : `Unsupported` ;
  - COG qui ne fait que changer la cellule d'une surface (voyant, grille coupée) : `SurfaceCelSwitch` ;
  - thing à images qu'aucun COG ne déplace, ou que son COG cite sans verbe connu : `Approximate` (jamais de disparition, la tâche 9 place la position « passage ouvert »).
- Les acteurs ne sont jamais des éléments mobiles (`JK_ACTOR_SKIPPED`), même avec des images (patrouilles `00_AIFramePatrol`).
- Verbes de déplacement à l'exécution (`SetThingVel`, `SetThingPos`, `SetThingLook`) : comptés seulement pour un thing à images (le drapeau CTF téléporté par `CTF_MAIN` n'est pas un élément mobile).

## Bilan

- 354 fichiers COG distincts, 1436 instances (158 fichiers ont au moins un comportement de mouvement).
- Éléments mobiles (things à images, hors acteurs, ou déplacés par un COG) : 564, tous classés.
- Comportements par type (doublons d'un même niveau retirés) : `SwitchMove` 380, `SurfaceCelSwitch` 244, `TouchDoor` 186, `TwoStopLift` 128, `Approximate` 65, `MultiStepPath` 43, `ActorTriggered` 9.
- `Unsupported` : 0. Script absent des GOB : `17a_Stream.cog`, uniquement pour `m_boss17` (1 instance sur les 3 ; les 2 autres niveaux le trouvent ; effets inconnus, journal `JK_COG_MISSING`). Scripts tronqués (`JK_COG_MALFORMED`) : `06_thirddoor.cog` (niveau `06bbarons`) et `08_vents2a.cog` (niveau `08escape88`), sans `end` final.
- Things à images sans COG qui les déplace : 25 (`Approximate`, ghosts qui servent de points d'apparition de caisses, lumières cachées, etc.).

## Fichiers

Triés par nombre d'instances. Colonne comportement : nombre de comportements produits (une porte à 4 pièces donne 4 `TouchDoor` pour 1 instance) ; « no mover » = aucun thing déplacé, avec la nature du COG.

| Fichier COG | Instances | Niveaux | Comportement retenu | Note |
|---|---:|---:|---|---|
| `00_door.cog` | 122 | 18 | TouchDoor x135 |  |
| `00_sectorthrust.cog` | 115 | 11 | no mover: sector thrust (wind, push) |  |
| `00_conveyor.cog` | 108 | 10 | no mover: surface or sky effect (T5) |  |
| `00_surfanim.cog` | 61 | 9 | no mover: surface or sky effect (T5) |  |
| `16a_redlight.cog` | 60 | 2 | no mover: light or surface state only |  |
| `00_slashgrate.cog` | 50 | 13 | SurfaceCelSwitch x100 |  |
| `00_sectordamage.cog` | 35 | 7 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_elevcall.cog` | 34 | 3 | SwitchMove x71 |  |
| `00_elev_switch.cog` | 31 | 8 | SurfaceCelSwitch x1, SwitchMove x30, TwoStopLift x30 |  |
| `00_deathfloor.cog` | 26 | 9 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_SecretAreas.cog` | 25 | 25 | no mover: sound or music only |  |
| `09_gasdamage.cog` | 23 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_soundtrack.cog` | 22 | 22 | no mover: music only |  |
| `00_PlayAmbient.cog` | 19 | 8 | no mover: sound or music only |  |
| `00_doorswitch.cog` | 16 | 8 | SwitchMove x32 |  |
| `09_fuelexplode.cog` | 16 | 3 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `00_AIFramePatrol.cog` | 15 | 3 | no mover: actor AI (actors are skipped) |  |
| `00_forcefield.cog` | 15 | 5 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_AIFrameMove.cog` | 12 | 3 | no mover: actor AI (actors are skipped) |  |
| `C3_MTRIGELEV.COG` | 11 | 1 | TwoStopLift x11 |  |
| `00_BREAKINGLAMP.COG` | 10 | 2 | no mover: thing light animation |  |
| `00_playdialog.cog` | 10 | 8 | no mover: sound or music only |  |
| `00_std_elev.cog` | 10 | 3 | TwoStopLift x10 |  |
| `03_explowall.cog` | 10 | 3 | SurfaceCelSwitch x20 |  |
| `C3_AUTODOOR.COG` | 10 | 1 | MultiStepPath x10 |  |
| `00_slideceiling.cog` | 9 | 9 | no mover: surface or sky effect (T5) |  |
| `00_REPLACEENEMY.COG` | 8 | 2 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `08_slashgrate2.cog` | 8 | 3 | SurfaceCelSwitch x12 |  |
| `16a_apocalypse.cog` | 8 | 2 | TwoStopLift x8 |  |
| `16a_calamity.cog` | 8 | 2 | MultiStepPath x8 |  |
| `M5_AUTODOOR.COG` | 8 | 1 | MultiStepPath x8 |  |
| `03_vapexplode.cog` | 7 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `09_stoppipedoors.cog` | 7 | 1 | no mover: sound or music only |  |
| `00_AIPatrol.cog` | 6 | 2 | no mover: actor AI (actors are skipped) |  |
| `00_BREAKINGLAMPX.COG` | 6 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `00_doorkey.cog` | 6 | 3 | TouchDoor x8 |  |
| `00_elev_switch2.cog` | 6 | 3 | SwitchMove x16, TwoStopLift x6 |  |
| `10_thinglight.cog` | 6 | 3 | no mover: thing light animation |  |
| `C2_MTRIGELEV.COG` | 6 | 1 | TwoStopLift x6 |  |
| `00_AITurnCoat.cog` | 5 | 1 | no mover: actor AI (actors are skipped) |  |
| `00_matanim.cog` | 5 | 5 | no mover: surface or sky effect (T5) |  |
| `06_generator.cog` | 5 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `06_hidethings1.cog` | 5 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_sleepydroids.cog` | 5 | 1 | no mover: actor AI (actors are skipped) |  |
| `19_doortoggle.cog` | 5 | 1 | SwitchMove x5 |  |
| `00_BREAKINGLAMPS.COG` | 4 | 2 | no mover: thing light animation |  |
| `00_damagewall.cog` | 4 | 2 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_doorlocked1side.cog` | 4 | 2 | TouchDoor x6 |  |
| `00_elev_doors.cog` | 4 | 2 | SwitchMove x48 |  |
| `01_neonsign.cog` | 4 | 1 | SurfaceCelSwitch x4 |  |
| `09_elevdamage.cog` | 4 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `09_pipedoor2.cog` | 4 | 1 | SwitchMove x12 |  |
| `12_conveyor.cog` | 4 | 1 | no mover: surface or sky effect (T5) |  |
| `12_slantelev.cog` | 4 | 1 | MultiStepPath x4 |  |
| `18_cratepuzz.cog` | 4 | 1 | SurfaceCelSwitch x4 |  |
| `18_pipethrust.cog` | 4 | 1 | no mover: sector thrust (wind, push) |  |
| `C2_BREAKINGGLASS.COG` | 4 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `C3_KILLINGFLOOR.COG` | 4 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `M2_dettrap.cog` | 4 | 1 | SurfaceCelSwitch x4 |  |
| `M5_HEALINGSECTOR.COG` | 4 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `00_AINotBeMoved.cog` | 3 | 2 | no mover: scripting, messages or objectives only |  |
| `00_lightanim.cog` | 3 | 1 | no mover: thing light animation |  |
| `02_SkywayCrates.cog` | 3 | 1 | TwoStopLift x3 |  |
| `06_doorlight.cog` | 3 | 1 | TouchDoor x3 |  |
| `06_pedtorment.cog` | 3 | 1 | no mover: actor AI (actors are skipped) |  |
| `08_elev4.cog` | 3 | 1 | MultiStepPath x3 |  |
| `09_damagefloor.cog` | 3 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `10_damagefloor.cog` | 3 | 2 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `10_FFieldswitch.cog` | 3 | 1 | SurfaceCelSwitch x3 |  |
| `10_sectorthrust.cog` | 3 | 1 | no mover: sector thrust (wind, push) |  |
| `14_oopstroop.cog` | 3 | 2 | TwoStopLift x3 |  |
| `17a_Stream.cog` | 3 | 3 | no mover: surface or sky effect (T5) | **MISSING from GOB** for `m_boss17` only (1 of the 3 instances: not in `JK1MP.GOB` nor `Res2.gob`, journal `JK_COG_MISSING`); the other 2 instances (2 other levels) read the script normally |
| `CTF_DISPLAYSCORE.COG` | 3 | 3 | SurfaceCelSwitch x18 |  |
| `CTF_ICONSCLIENT.COG` | 3 | 3 | no mover: no visible movement |  |
| `CTF_MAIN.cog` | 3 | 3 | SurfaceCelSwitch x21 |  |
| `CTF_RESPAWN.COG` | 3 | 3 | no mover: no visible movement |  |
| `CTF_SAFEENTRY.COG` | 3 | 3 | no mover: teleports things (CTF) |  |
| `00_1waydoor.cog` | 2 | 2 | TouchDoor x2 |  |
| `00_doubledoor.cog` | 2 | 1 | TouchDoor x4 |  |
| `00_elev_switch3.cog` | 2 | 2 | SwitchMove x1, TouchDoor x1, TwoStopLift x2 |  |
| `00_GENERATOR.COG` | 2 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `00_randomambient.cog` | 2 | 2 | no mover: sound or music only |  |
| `00_sectorliteanim.cog` | 2 | 1 | no mover: light or surface state only |  |
| `02_end_elevator.cog` | 2 | 1 | TwoStopLift x2 |  |
| `03_glight.cog` | 2 | 1 | Approximate x2 | moves this thing on the 'damaged' message (damage), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `04_dam_elev.cog` | 2 | 1 | SwitchMove x2, TwoStopLift x2 |  |
| `06_bigdoordamage.cog` | 2 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `06_controldoor.cog` | 2 | 1 | SwitchMove x2 |  |
| `06_drawbridge.cog` | 2 | 1 | SwitchMove x3 |  |
| `06_ruinsdroids.cog` | 2 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_troopertalk.cog` | 2 | 1 | no mover: sound or music only |  |
| `06_turrettrap.cog` | 2 | 1 | TwoStopLift x2 |  |
| `09_accessdoor.cog` | 2 | 1 | SwitchMove x2 |  |
| `09_gasdrainsounds.cog` | 2 | 1 | no mover: sound or music only |  |
| `10_conduit.cog` | 2 | 1 | Approximate x4 | moves this thing on the 'damaged' message (damage), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `10_doorlocked1side.cog` | 2 | 1 | no mover: opens or closes adjoins (passage) |  |
| `12_KeyedCatwalk.cog` | 2 | 1 | SwitchMove x8 |  |
| `16a_catwalk.cog` | 2 | 2 | SwitchMove x4 |  |
| `16a_chase.cog` | 2 | 2 | TwoStopLift x2 |  |
| `16a_crowadjoin.cog` | 2 | 2 | no mover: opens or closes adjoins (passage) |  |
| `16a_crowarm.cog` | 2 | 2 | SwitchMove x2 |  |
| `16a_endlevel.cog` | 2 | 2 | no mover: level end |  |
| `16a_fallingwall.cog` | 2 | 2 | no mover: surface or sky effect (T5) |  |
| `16a_outerdoors.cog` | 2 | 2 | Approximate x8 | moves this thing once, to frame 1, from a startup, timer or pulse message (no player trigger); its frames are kept as a path. |
| `16a_RandExplosions.cog` | 2 | 2 | no mover: scripting, messages or objectives only |  |
| `16a_randomambient.cog` | 2 | 2 | no mover: sound or music only |  |
| `16a_timer.cog` | 2 | 2 | TouchDoor x2 |  |
| `16b_rahn.cog` | 2 | 2 | no mover: sound or music only |  |
| `17a_SarrissSearch.cog` | 2 | 2 | no mover: actor AI (actors are skipped) |  |
| `18_FFieldSwitch.Cog` | 2 | 1 | SurfaceCelSwitch x2 |  |
| `19_elev_switch2.cog` | 2 | 1 | SwitchMove x4 |  |
| `20_BocSearch.cog` | 2 | 2 | no mover: actor AI (actors are skipped) |  |
| `20_EndLevel.cog` | 2 | 2 | no mover: level end |  |
| `21_Core.cog` | 2 | 2 | Approximate x8 | moves this thing on the 'user1' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `21_endlevel.cog` | 2 | 2 | no mover: level end |  |
| `21_JerecAI.cog` | 2 | 2 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `21_JerecSeek.cog` | 2 | 2 | no mover: actor AI (actors are skipped) |  |
| `C2_DUALFORCEFIELD.COG` | 2 | 1 | SurfaceCelSwitch x2 |  |
| `C2_SIMPLEFORCEFIELD.COG` | 2 | 1 | SurfaceCelSwitch x2 |  |
| `C3_DUALELEV.COG` | 2 | 1 | MultiStepPath x2 |  |
| `C3_SASCOLORED.COG` | 2 | 1 | TouchDoor x4 |  |
| `C3_SASNEUTRAL.COG` | 2 | 1 | SwitchMove x4 |  |
| `C3_SWITCHDOOR.COG` | 2 | 1 | SwitchMove x2 |  |
| `CTF_DOORGOLD.COG` | 2 | 1 | TouchDoor x2 |  |
| `CTF_DOORRED.COG` | 2 | 1 | TouchDoor x2 |  |
| `darkjedi_boc.cog` | 2 | 2 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `darkjedi_gorc.cog` | 2 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `darkjedi_jerec.cog` | 2 | 2 | no mover: sound or music only |  |
| `darkjedi_pic.cog` | 2 | 1 | no mover: scripting, messages or objectives only |  |
| `M5_CONVBELT.COG` | 2 | 1 | no mover: surface or sky effect (T5) |  |
| `00_elev_doors5.cog` | 1 | 1 | SwitchMove x12 |  |
| `00_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `00_GENERATOR2.COG` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `00_hidethings.cog` | 1 | 1 | no mover: shows or hides things |  |
| `00_LIGHTNING.cog` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `00_PlayAmbient2.cog` | 1 | 1 | no mover: sound or music only |  |
| `00_sendmessage.cog` | 1 | 1 | no mover: scripting, messages or objectives only |  |
| `00_twoonebee.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `01_bartender.cog` | 1 | 1 | no mover: sound or music only |  |
| `01_cargodoors.cog` | 1 | 1 | SwitchMove x2 |  |
| `01_cargoelev1.cog` | 1 | 1 | SwitchMove x1 |  |
| `01_cargoelev2.cog` | 1 | 1 | SwitchMove x1 |  |
| `01_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `01_funicularA.cog` | 1 | 1 | SwitchMove x2 |  |
| `01_funicularB.cog` | 1 | 1 | SwitchMove x1 |  |
| `01_goal0.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `01_initialry.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `01_Red_Elev.cog` | 1 | 1 | SwitchMove x1 |  |
| `01_SoundTrack.cog` | 1 | 1 | no mover: music only |  |
| `01_TieBomber.cog` | 1 | 1 | no mover: thing light animation |  |
| `01_TuskenTwitch.cog` | 1 | 1 | no mover: no visible movement |  |
| `02_boom0.cog` | 1 | 1 | SwitchMove x1 |  |
| `02_boom1.cog` | 1 | 1 | SwitchMove x1 |  |
| `02_cratelift.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `02_CratePuzDoor.cog` | 1 | 1 | SwitchMove x1 |  |
| `02_cratepuzzle_a.cog` | 1 | 1 | SwitchMove x1 |  |
| `02_cratepuzzle_b.cog` | 1 | 1 | SwitchMove x1 |  |
| `02_CrowEnd.cog` | 1 | 1 | no mover: shows or hides things |  |
| `02_CrowStart.cog` | 1 | 1 | no mover: sound or music only |  |
| `02_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `02_end_crategen.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `02_greedodate.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `02_pipedoor.cog` | 1 | 1 | TouchDoor x1 |  |
| `02_signanim.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `03_beam01.cog` | 1 | 1 | TwoStopLift x1 |  |
| `03_doorkey.cog` | 1 | 1 | SwitchMove x2 |  |
| `03_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `03_FRONTDOOR.COG` | 1 | 1 | TwoStopLift x2 |  |
| `03_garage_door.cog` | 1 | 1 | no mover: opens or closes adjoins (passage) |  |
| `03_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `03_kyhealth.cog` | 1 | 1 | no mover: sound or music only |  |
| `03_kyleswim.cog` | 1 | 1 | no mover: sound or music only |  |
| `03_lightning.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `03_paddlepool.cog` | 1 | 1 | Approximate x2, SwitchMove x2 | moves this thing with RotatePivot, which is not reproduced; its frames are kept as a path. |
| `03_roomlight.cog` | 1 | 1 | no mover: light or surface state only |  |
| `03_secretwall.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'damaged' message (damage), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `03_thinglight.cog` | 1 | 1 | no mover: light or surface state only |  |
| `03_window.cog` | 1 | 1 | TouchDoor x1 |  |
| `04_britta.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'user5' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `04_britta2.cog` | 1 | 1 | no mover: no visible movement |  |
| `04_brittaEX.cog` | 1 | 1 | SwitchMove x2 |  |
| `04_brittaFZ.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'user7' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `04_EndLevel.cog` | 1 | 1 | no mover: level end |  |
| `04_garage.cog` | 1 | 1 | SwitchMove x1 |  |
| `04_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `04_oldstdoor.cog` | 1 | 1 | SwitchMove x1 |  |
| `04_remotes1.cog` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `04_remotes2.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `04_remotes3.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `04_sabertarget.cog` | 1 | 1 | SurfaceCelSwitch x9 |  |
| `04_slashgrate2.cog` | 1 | 1 | no mover: no visible movement |  |
| `04_soundofcars.cog` | 1 | 1 | no mover: sound or music only |  |
| `04_specialthrust.cog` | 1 | 1 | no mover: sector thrust (wind, push) |  |
| `06a_startlevel.cog` | 1 | 1 | no mover: music only |  |
| `06b_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `06b_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `06_ATSTelev.cog` | 1 | 1 | TwoStopLift x1 |  |
| `06_bigdoordamage2.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `06_blasters.cog` | 1 | 1 | no mover: sound or music only |  |
| `06_bomber.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'sighted' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `06_brokendoor.cog` | 1 | 1 | MultiStepPath x1 |  |
| `06_coffeerun.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_company.cog` | 1 | 1 | no mover: sound or music only |  |
| `06_drama.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_elev2ballroom.cog` | 1 | 1 | SwitchMove x1, TouchDoor x1 |  |
| `06_endpart1.cog` | 1 | 1 | no mover: level end |  |
| `06_frontdrawbridge.cog` | 1 | 1 | SwitchMove x2 |  |
| `06_harassed.cog` | 1 | 1 | no mover: sound or music only |  |
| `06_hidefish.cog` | 1 | 1 | no mover: shows or hides things |  |
| `06_max.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_maxstart.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_paying.cog` | 1 | 1 | no mover: sound or music only |  |
| `06_plazadoor.cog` | 1 | 1 | TouchDoor x1 |  |
| `06_quisling.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `06_ruinsdoor.cog` | 1 | 1 | Approximate x1 | moves this thing with RotatePivot, which is not reproduced; its frames are kept as a path. |
| `06_slashgrate.cog` | 1 | 1 | no mover: opens or closes adjoins (passage) |  |
| `06_squadmaker.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'user0' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `06_thirddoor.cog` | 1 | 1 | SwitchMove x1 |  |
| `06_towerbars.cog` | 1 | 1 | Approximate x2 | moves this thing on the 'user0' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `06_towerdoors.cog` | 1 | 1 | Approximate x1, SwitchMove x2 | moves this thing on the 'user0, user1' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `06_Tuskentrap.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `07_boomchair.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `07_boomtable.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `07_Rahnlines.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `07_yun.cog` | 1 | 1 | ActorTriggered x8 |  |
| `08_Baydoors2.cog` | 1 | 1 | TwoStopLift x5 |  |
| `08_beast_door.cog` | 1 | 1 | SwitchMove x1, TwoStopLift x2 |  |
| `08_beast_elev.cog` | 1 | 1 | SwitchMove x1, TwoStopLift x1 |  |
| `08_elev6.cog` | 1 | 1 | SwitchMove x4, TwoStopLift x1 |  |
| `08_Endlevel.cog` | 1 | 1 | no mover: level end |  |
| `08_firstguys.cog` | 1 | 1 | no mover: no visible movement |  |
| `08_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `08_HiddenLight.cog` | 1 | 1 | MultiStepPath x1 |  |
| `08_HiddenLight2.cog` | 1 | 1 | MultiStepPath x1 |  |
| `08_irPuzzle.cog` | 1 | 1 | ActorTriggered x1, MultiStepPath x1 |  |
| `08_kkjoadr.cog` | 1 | 1 | no mover: sound or music only |  |
| `08_scelev.cog` | 1 | 1 | MultiStepPath x1 |  |
| `08_slashgrate.cog` | 1 | 1 | TwoStopLift x8 |  |
| `08_tiebomber.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `08_tiebomber3.cog` | 1 | 1 | no mover: scripting, messages or objectives only |  |
| `08_trapdoor.cog` | 1 | 1 | TwoStopLift x2 |  |
| `08_vents2.cog` | 1 | 1 | no mover: sector thrust (wind, push) |  |
| `08_vents2a.cog` | 1 | 1 | no mover: scripting, messages or objectives only |  |
| `08_yourebusted.cog` | 1 | 1 | Approximate x1 | moves this thing on the 'user2' message (script event), which has no direct Jedi Academy trigger; its frames are kept as a path. |
| `09_bigdoordamage.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `09_chopgrate.cog` | 1 | 1 | MultiStepPath x1 |  |
| `09_diagramlines.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_donefueling.cog` | 1 | 1 | TwoStopLift x3 |  |
| `09_drainpipes.cog` | 1 | 1 | SurfaceCelSwitch x4 |  |
| `09_endlevel.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_fueltanks.cog` | 1 | 1 | SwitchMove x16 |  |
| `09_gantrydrainsounds.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_getwrench.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `09_matanim.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `09_pipedoor_a.cog` | 1 | 1 | SwitchMove x3 |  |
| `09_pipedoor_b.cog` | 1 | 1 | SwitchMove x2 |  |
| `09_pipedoor_c.cog` | 1 | 1 | SwitchMove x2 |  |
| `09_pipelines.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_secr_elev.cog` | 1 | 1 | MultiStepPath x1 |  |
| `09_startlevel.cog` | 1 | 1 | no mover: music only |  |
| `09_tankdoors.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `09_tanklines.cog` | 1 | 1 | no mover: sound or music only |  |
| `09_tilting_floor.cog` | 1 | 1 | Approximate x2 | moves this thing with RotatePivot, which is not reproduced; its frames are kept as a path. |
| `09_util_elev2.cog` | 1 | 1 | SwitchMove x2, TwoStopLift x1 |  |
| `09_util_elev3.cog` | 1 | 1 | SwitchMove x2, TwoStopLift x1 |  |
| `10_cargodoors.cog` | 1 | 1 | Approximate x2 | moves this thing with rotatepivot, which is not reproduced; its frames are kept as a path. |
| `10_coredoor.cog` | 1 | 1 | TouchDoor x1 |  |
| `10_endlevel.cog` | 1 | 1 | SwitchMove x2 |  |
| `10_fanstop.cog` | 1 | 1 | SurfaceCelSwitch x12 |  |
| `10_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `10_hatch.cog` | 1 | 1 | TouchDoor x1 |  |
| `10_powercore.cog` | 1 | 1 | SurfaceCelSwitch x14 |  |
| `10_surfmove.cog` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `11_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `11_sound.cog` | 1 | 1 | no mover: sound or music only |  |
| `12_CargoGenerator.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `12_doorlocked1side.cog` | 1 | 1 | no mover: sound or music only |  |
| `12_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `12_FFieldswitch.cog` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `12_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `12_officerkey.cog` | 1 | 1 | no mover: teleports things (CTF) |  |
| `12_quickpower.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `12_sectordamage.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `12_thinglight.cog` | 1 | 1 | no mover: light or surface state only |  |
| `12_TrooperElev.cog` | 1 | 1 | TwoStopLift x1 |  |
| `14_endit.cog` | 1 | 1 | SwitchMove x2, TwoStopLift x2 |  |
| `14_forcefield.cog` | 1 | 1 | SurfaceCelSwitch x4 |  |
| `14_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `14_oopsdoor2.cog` | 1 | 1 | SwitchMove x3, TwoStopLift x1 |  |
| `14_puzzledoor.cog` | 1 | 1 | SwitchMove x3 |  |
| `15_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `15_mawdoor.cog` | 1 | 1 | TouchDoor x1 |  |
| `15_Rahnlines.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `15_sectordoor.cog` | 1 | 1 | TouchDoor x1 |  |
| `17a_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `17a_RahnLines.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `17b_Camera.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `17b_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `18_endelev.cog` | 1 | 1 | SwitchMove x1 |  |
| `18_finalthrust1.cog` | 1 | 1 | SwitchMove x8 |  |
| `18_firstdoor.cog` | 1 | 1 | Approximate x1, TwoStopLift x2 | moves this thing once, to frame 1, from a startup, timer or pulse message (no player trigger); its frames are kept as a path. |
| `18_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `18_hienergy.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `18_objectcog.cog` | 1 | 1 | no mover: no visible movement |  |
| `18_ventpuzzle.cog` | 1 | 1 | SwitchMove x6, TwoStopLift x3 |  |
| `18_vpuzparts.cog` | 1 | 1 | no mover: spawns things at run time (JK_RUNTIME_THING) |  |
| `19a_endlevel.cog` | 1 | 1 | no mover: level end |  |
| `19a_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `19b_endlevel.cog` | 1 | 1 | SwitchMove x1 |  |
| `19b_goals.cog` | 1 | 1 | no mover: no visible movement |  |
| `19_airlock.cog` | 1 | 1 | SwitchMove x2, TwoStopLift x2 |  |
| `19_counterweight.cog` | 1 | 1 | TouchDoor x5 |  |
| `19_counterweight2.cog` | 1 | 1 | TouchDoor x3 |  |
| `19_DoorTrigger.cog` | 1 | 1 | SwitchMove x1, TouchDoor x1 |  |
| `19_keydoor.cog` | 1 | 1 | SwitchMove x2 |  |
| `19_payload.cog` | 1 | 1 | SwitchMove x15 |  |
| `19_startup.cog` | 1 | 1 | Approximate x1, MultiStepPath x1 | moves this thing once, to frame 1, from a startup, timer or pulse message (no player trigger); its frames are kept as a path. |
| `19_thrustpuzzle.cog` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `20b_Camera.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `20_RahnLines.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `21B_Camera.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `21_RahnLines.cog` | 1 | 1 | no mover: actor AI (actors are skipped) |  |
| `C1_AMBIENTWINDSOUND.COG` | 1 | 1 | no mover: sound or music only |  |
| `C1_CTFCALLBACK.COG` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `C1_SECRETELEV.COG` | 1 | 1 | SwitchMove x1, TwoStopLift x1 |  |
| `C1_SLIDESKIES.COG` | 1 | 1 | no mover: surface or sky effect (T5) |  |
| `C2_AMBIENTWINDSOUND.COG` | 1 | 1 | no mover: sound or music only |  |
| `C2_BREAKINGGLASSCLIENT.COG` | 1 | 1 | no mover: opens or closes adjoins (passage) |  |
| `C2_KILLINGFLOOR.COG` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `C2_MOVINGPLATFORM.COG` | 1 | 1 | SwitchMove x1, TwoStopLift x1 |  |
| `C3_AMBIENTTECHSOUND.COG` | 1 | 1 | no mover: sound or music only |  |
| `C3_AMBIENTWINDSOUND.COG` | 1 | 1 | no mover: sound or music only |  |
| `C3_CAMERAFF.COG` | 1 | 1 | no mover: scripting, messages or objectives only |  |
| `C3_DETONATORS.COG` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `C3_ELEVSWITCH.COG` | 1 | 1 | SwitchMove x2 |  |
| `C3_FANSPUZZLE.COG` | 1 | 1 | no mover: sector thrust (wind, push) |  |
| `C3_FORCEFIELD.COG` | 1 | 1 | no mover: opens or closes adjoins (passage) |  |
| `C3_LARGELEVEL.COG` | 1 | 1 | no mover: opens or closes adjoins (passage) |  |
| `C3_LARGELEVELSRV.COG` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `C3_MOVINGPLATFORM.COG` | 1 | 1 | SwitchMove x1 |  |
| `C3_TURRETS.COG` | 1 | 1 | SurfaceCelSwitch x1 |  |
| `CTF_KEYSCLIENT.COG` | 1 | 1 | no mover: no visible movement |  |
| `darkjedi_maw.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `darkjedi_sariss.cog` | 1 | 1 | no mover: damage or healing zone (T5 hurt volumes) |  |
| `darkjedi_yun.cog` | 1 | 1 | no mover: sound or music only |  |
| `darkjedi_yun2.cog` | 1 | 1 | no mover: sound or music only |  |
| `M2_FFieldSwitch.cog` | 1 | 1 | SurfaceCelSwitch x2 |  |
| `M2_turrettrap.cog` | 1 | 1 | SwitchMove x4 |  |
| `M4_reactorthrust.cog` | 1 | 1 | SwitchMove x5 |  |
| `M5_ANTIGRAV.COG` | 1 | 1 | no mover: sector thrust (wind, push) |  |
| `M5_FANDAMAGE.COG` | 1 | 1 | SurfaceCelSwitch x2 |  |
| `M5_FLASHINGREDLIGHT.COG` | 1 | 1 | no mover: light or surface state only |  |
| `M5_SURFANIM.COG` | 1 | 1 | no mover: surface or sky effect (T5) |  |

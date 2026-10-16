# Plan — Conversion des zones SWTOR vers des maps JA SWGL

Branche : `feature/swtor-maps`. Orchestration : un agent sonnet neuf par tâche, jamais réutilisé.
Chaque agent écrit son rapport dans `docs/superpowers/plans/swtor/reports/T<n>.md`. L'orchestrateur commit.

## Données
- Install SWTOR : `C:\Program Files (x86)\Electronic Arts\BioWare\Star Wars - The Old Republic\Assets` (.tor)
- Assets déjà extraits : `C:\jka_tor_assets\resources` (`world/areas/<id>/area.dat`, `_everywhere_.dat`, `area_*.dat`, `art/static/area/<planète>/arch/*.gr2`, `.mat`, `.dds`, buckets GOM dans `systemgenerated/`).
- Code d'aide (à **copier**, pas référencer) : `C:\Users\lambe\source\repos\SWTOR-Character-maker\` → `Swtor.Formats` (Myp, Gr2, Dds, Xml/MaterialReader, Stb, Gom).

## Architecture cible
`Swtor.Core` (lecteurs SWTOR) → `SwtorSceneBuilder` produit un `ModuleScene` → `ModuleExporter.BuildPlan` + `Kotor.Compiler` existants → `swtor_<map>.pk3`.

## Tâches
- **T1 — Port des formats** : nouveau projet `src/Swtor.Core` (+ tests) avec le code copié de Swtor.Formats (Gr2, Dds, Mat XML, Myp/hash, Stb). Build + tests verts.
- **T2 — Rétro-ingénierie des zones** : lecteurs `area.dat` (AREA_DAT_BINARY_FORMAT) et room `.dat` (ROOM_DAT_BINARY_FORMAT) : rooms, instances (chemin gr2 + transform), lumières. Commande CLI `swtor-dump-area`. Doc format dans `docs/superpowers/specs/swtor-formats.md`.
- **T3 — Catalogue des zones** : correspondance id numérique ↔ nom lisible (GOM/STB/.not), commande `swtor-list-areas`.
- **T4 — Scène SWTOR** : abstraction « source de jeu » (KotorGame → + Swtor), `SwtorSceneBuilder` (gr2 arch placés, diffuse → TGA, échelle validée), `export-ja --game swtor <area>[:room]`. Première zone cible : un petit intérieur.
- **T5 — Rendu et jouabilité** : éclairage (lumières SWTOR ou vertex lighting), collisions depuis les meshes, découpage par room / limites BSP, terrain et SpeedTree journalisés si non gérés.
- **T6 — Intégration UI** : branche SWTOR dans l'arbre, `SwtorWorkflowSteps` (Preview → Import → Export → PK3).
- **T7 — Vérification en jeu** : export d'une zone, lancement SWGL, captures, rapport des pièces manquantes.
- **T8 — Revue de code** de la branche.

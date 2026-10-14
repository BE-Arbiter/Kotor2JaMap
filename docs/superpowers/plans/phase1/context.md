# Phase 1 — Module KOTOR vers Jedi Academy : contexte partagé

Chaque agent lit **uniquement** ce fichier et **son** `step-NN.md`. Le plan d'ensemble (`docs/superpowers/plans/2026-10-14-phase1-module-vers-ja.md`) n'est pas à lire.

## Chemins

- **KOTOR (lecture seule)** : `C:\Program Files (x86)\Steam\steamapps\common\swkotor\`. Variable d'environnement `KOTOR_PATH` ou option `--kotor <dossier>` pour le changer.
  - `chitin.key` + `data\*.bif` : `models.bif` (MDL, MDX, WOK), `layouts.bif` (LYT), `lightmaps*.bif` (lightmaps TGA + TXI, VIS), `templates.bif`, `2da.bif`, `textures.bif`.
  - `modules\<m>.rim` contient `<area>.are`, `<area>.git` et `module.ifo`. `modules\<m>_s.rim` contient les DLG, UTC, UTD, UTP, etc.
  - `TexturePacks\swpc_tex_tpa.erf` (haute qualité), puis `tpb`, `tpc` et `gui`.
  - `Override\` (vide aujourd'hui, mais prioritaire), `dialog.tlk`.
  - Exemple vérifié pour `tar_m02aa` :
    - le RIM contient `m02aa.are`, `m02aa.git` et `module.ifo` ;
    - `layouts.bif` contient `m02aa.lyt` ;
    - `models.bif` contient `m02aa_01a.mdl/.mdx/.wok` … `m02aa_09g` ;
    - `lightmaps.bif` contient `m02aa_01a_lm0.tga/.txi` et suivants.
- **Jedi Academy SWGL** : `C:\Program Files (x86)\Steam\steamapps\common\Jedi Academy - SWGL_DEV\GameData\`. On n'y écrit que `base\kotor_<module>.pk3`.
- **Dossier de travail** : `C:\jka_maps\kotor_work\<module>\` (réglable). Il reçoit les TGA, les shaders, le BSP intermédiaire et `<module>.journal.txt`.
- **Projet TFU (source de copie, lecture seule)** : `C:\Users\lambe\source\repos\SWGLFU-Map-importer\`.

## Solution

- `Kotor2Ja.sln`
- `src/Kotor.Core/` : formats KOTOR (`Archives`, `Gff`, `Tlk`, `TwoDa`, `Layout`, `Mdl`, `Walkmesh`, `Textures`), `Modules`, `Scene`, `Export`, `Geometry`, `Diagnostics`.
- `src/Kotor.Compiler/` : compilateur BSP copié de `Tfu.Compiler` (GPL-2.0).
- `src/Kotor2Ja.Cli/` : commande `kotor2ja` (System.CommandLine).
- `tests/Kotor.Core.Tests/`, `tests/Kotor.Compiler.Tests/`, `tests/Kotor2Ja.Cli.Tests/` : xUnit.
  - Tests unitaires sur des octets synthétiques.
  - Tests d'intégration sur le jeu installé, en `[SkippableFact]`, ignorés si KOTOR est absent (`TestEnv.KotorDir`).

## Conventions

- Repère KOTOR : Z vers le haut, mètres. Repère JA : Z vers le haut, unités Quake. **La conversion est une simple multiplication par l'échelle** (défaut `36`), faite uniquement à l'export (étape 08). Tout ce qui est en amont reste en mètres KOTOR.
- Les noms de ressources (resref) sont comparés sans tenir compte de la casse et stockés en minuscules.
- Journal : `ImportLog.Warn(code, message)`. Les codes sont en MAJUSCULES et propres à chaque étape (par exemple `MDL_NODE_SKIPPED`, `TEXTURE_MISSING`). **Rien n'est ignoré sans entrée au journal.**
- Les fichiers du projet TFU copiés gardent leur en-tête de licence. Le namespace `Tfu.*` devient `Kotor.*`.

## Protocole

- Un agent ne lance jamais de sous-agent et ne relance jamais un autre agent. Une correction est faite par un nouvel agent qui lit le rapport précédent.
- Rapport : `docs/superpowers/plans/phase1/reports/step-NN-<impl|review|fix-R|rereview-R>.md`, en français. L'agent renvoie seulement `OK` ou `BLOQUE` + une raison courte.
- **Les agents ne commitent pas.** L'orchestrateur commite après une relecture favorable.
- Commande de test globale : `dotnet test Kotor2Ja.sln`. Elle doit être verte à la fin de chaque étape.
- Si un format ne correspond pas à ce que dit l'étape, l'agent s'arrête et le décrit dans son rapport (`BLOQUE`), au lieu de contourner.

## Références de formats (code ouvert, à consulter, ne pas copier tel quel)

- xoreos : `src/aurora/keyfile.cpp`, `biffile.cpp`, `rimfile.cpp`, `erffile.cpp`, `gff3file.cpp`, `talktable_tlk.cpp`, `2dafile.cpp`, `src/graphics/aurora/model_kotor.cpp`, `src/graphics/images/tpc.cpp`, `txi.cpp`.
- PyKotor : `Libraries/PyKotor/src/pykotor/resource/formats/` (`mdl`, `bwm`, `tpc`, `lyt`, `gff`, `erf`, `rim`, `key`, `bif`).
- KotorBlender : `io_scene_kotor/format/mdl/reader.py`, `format/bwm/reader.py`.
- Wiki DeadlyStream et xoreos-docs (`specs/bioware/*.pdf`, `specs/kotor_mdl.html`).

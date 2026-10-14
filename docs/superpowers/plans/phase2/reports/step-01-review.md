# Étape 01 — Revue (conformité puis qualité)

Base 8065b7b, tête 47e8505. Revue du diff uniquement, plus lecture de `Pk3Builder` (écriture `.tmp` puis `File.Move`). Aucun test relancé, rien écrit dans GameData.

## Conformité

- ✅ Projet `Kotor.Pipeline` (réf. Core + Compiler), dans la solution, `ModuleExporter` déplacé (`git mv`, similarité 77 %), namespace `Kotor.Pipeline`.
- ✅ Signatures de `PipelineSteps`, `ImportStepResult`, `ExportStepResult`, `Pk3StepResult` conformes au plan. `PipelineStepException(code, message)` créée avec `Code`.
- ✅ `ModuleExporter.Export` (deux surcharges) garde sa signature et son `ExportResult`, enchaîne les étapes et convertit `PipelineStepException`. Messages et journal inchangés (`Module illisible : …`, message E/S brut).
- ✅ `Import` n'écrit rien (test sur `WorkDir`). Staging, shader, TGA et pk3 aux mêmes emplacements qu'en phase 1.
- ✅ `BspReader` : en-tête, version, lump hors fichier, longueur non multiple, index hors des sommets de la surface lèvent `InvalidDataException` avec message. `BspSurface`/`BspBrushInfo`/`BspFile` conformes.
- ✅ Tests demandés présents : round-trip, mauvais en-tête, lump hors plage, déterminisme, chaîne = `ModuleExporter` (SkippableFact, dossiers temporaires), pk3 verrouillé (code `EXPORT_IO_ERROR`, contenu inchangé).
- ✅ Contraintes globales : tests et vérification CLI sur dossiers temporaires, aucune écriture dans GameData par le code. Les deux chemins (CLI et étapes) passent par le même code, donc mêmes fichiers.
- ⚠️ Écart assumé au plan : le round-trip compare des ensembles (le compilateur réordonne les sommets). Acceptable et documenté.
- ⚠️ « pk3 identiques » : seuls la liste d'entrées et le `.bsp` sont comparés (horodatages du zip). Conforme au texte du plan, qui ne demande rien de plus.
- ⚠️ Focus « BSP relu différent de celui écrit » : voir Important 1.

## Points forts

- Découpage net : l'exception porte le code, le code est journalisé une seule fois (`Fail`), `ModuleExporter` n'a plus de logique d'étape.
- `CreatePk3` verrouillé : `Pk3Builder` écrit un `.tmp` puis renomme, donc le pk3 existant reste intact ; le test vérifie code, journal et octets.
- `BspReader` borne tout en `long`, vérifie `Count` par taille d'enregistrement, valide shader/sommets/index avant d'allouer (pas d'allocation démesurée sur fichier corrompu).
- La relecture est intégrée à l'étape Export avec son propre code (`BSP_READBACK_FAILED`), attrapée proprement.
- Aucun plantage : toutes les exceptions d'E/S de l'étape Export et CreatePk3 sont converties en `PipelineStepException`.

## Problèmes

### Critique
Aucun.

### Important

1. `src/Kotor.Compiler/Bsp/BspReader.cs:229-259` et `src/Kotor.Pipeline/PipelineSteps.cs:571-574` — la relecture ne détecte pas un BSP « différent de celui écrit » dans les cas ciblés par la revue : lump surfaces vide (0 surface), lump sommets/index vide, surface avec `numIndexes == 0` ou `numIndexes % 3 != 0`. Le lecteur renvoie alors un `BspFile` vide sans erreur, et l'étape Export ne compare rien au plan (`plan.Input.Surfaces`) : elle journalise un succès et `CreatePk3` empaquette un `.bsp` vide ou tronqué. L'application (étapes suivantes) affichera un aperçu vide au lieu d'un message d'échec. Correctif : dans `BspReader`, rejeter `numIndexes % 3 != 0` et une surface sans index ou sans sommet (`Surface {s} : aucun index`), et dans `PipelineSteps.Export` après lecture, échouer en `BSP_READBACK_FAILED` si `parsed.Surfaces.Count == 0` ou si le nombre de shaders/surfaces relus est incohérent avec `plan.Input` (au minimum non vide quand l'entrée a des surfaces). Ajouter les tests correspondants (lump surfaces mis à longueur 0, surface avec `numIndexes = 0`).

### Mineur

1. `src/Kotor.Pipeline` — le chemin `BSP_READBACK_FAILED` (et `SHADER_LIMIT`, `OPTIONS_INVALID`, `MODULE_UNREADABLE`) n'a aucun test dans `PipelineStepsTests.cs` ; seul `EXPORT_IO_ERROR` est couvert. Le plan ne l'exigeait pas mais c'est le focus de la revue.
2. `tests/Kotor.Pipeline.Tests/TestEnv.cs:13` — `JaBaseDir` est une copie codée en dur vers le dossier GameData, non utilisée par les tests de ce projet : propriété morte et risque d'écriture accidentelle dans GameData si quelqu'un l'emploie. À supprimer.
3. `tests/Kotor.Pipeline.Tests/PipelineStepsTests.cs:920` — assertion trop faible (`EndsWith("/t") || Contains("kotor")`) ; vérifier le nom de shader exact attendu.
4. `tests/Kotor.Compiler.Tests/BspReaderTests.cs:734-746` — blancs de mise en forme (double ligne vide, pas de ligne vide avant `[Fact]`).
5. `src/Kotor.Compiler/Bsp/BspReader.cs:191` — `ofs < HeaderSize` rejette un lump vide dont l'offset serait 0 ; vrai uniquement si un autre écrivain que `BspWriter` produit le fichier (aujourd'hui OK, tests verts). À assouplir si `len == 0`.
6. `src/Kotor.Pipeline/PipelineSteps.cs:555` — `BSP_ERROR` n'est pas journalisé par l'étape (le rapport dit « déjà journalisé par le compilateur ») ; le contrat « le code est déjà écrit au journal » dépend donc d'un comportement externe non testé. Vérifier ou journaliser via `Fail`.

## Évaluation

**Corrections nécessaires** — un point Important (la relecture accepte un BSP vide ou sans index, contre le focus « la lecture échoue proprement avec un message »). Le reste est solide.

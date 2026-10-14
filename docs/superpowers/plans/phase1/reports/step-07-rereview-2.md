# Étape 07 — Re-revue 2 (fix 2, 30f2006..aeda7e6)

## Verdict par constat

### 1. (Important) Budget de plans non borne supérieure — RÉSOLU

- `src/Kotor.Compiler/Compiler.cs` (boucle `for (int attempt...)`, bloc `try`/`catch (BspLimitException) when (...)`) : la borne est désormais garantie par reconstruction. Le budget estimé reste une première estimation, mais tout dépassement (plans de nœuds, nœuds, feuilles, brushes, côtés) est absorbé par réduction de 20 % puis reconstruction.
- Seule `BspLimitException` est interceptée dans la boucle. Les autres exceptions (IO, FormatException, ArgumentException...) traversent la boucle et sont traitées par le `catch` externe existant, comme avant.
- Aucun BSP partiel possible : `File.WriteAllBytes` est situé après la boucle ; on n'en sort que par `break`, placé après `BspWriter.Write` réussi (`ms.SetLength(0)` avant chaque écriture). Si le garde `when` est faux (80 tentatives épuisées, ou plus aucune collision), l'exception remonte au `catch (BspLimitException)` externe : `error` renseigné, rien n'est écrit, `BSP_ERROR` journalisé.
- Terminaison : bornée par `MaxShrinkAttempts = 80`. De plus `solidKeep`/`walkKeep` décroissent strictement (n*4/5 atteint 0 depuis 1) et la boucle s'arrête quand les deux sont à 0.
- Journal : `COLLISION_CAPPED` et `WALKABLE_CAPPED` émis après la boucle. Les avertissements d'une tentative échouée sont jetés (`iterLog`/`iterWarnings`), seuls ceux de la tentative réussie sont reportés. `ImportLog` n'expose que des avertissements (`ImportLog.cs:8,10`) : rien n'est perdu.
- Test : `tests/Kotor.Compiler.Tests/CompilerTests.cs` `Tree_node_planes_are_enforced_by_rebuild_and_compilation_succeeds` (3000 triangles inclinés, `MaxPlanes = 25000`) vérifie compilation OK, plans <= max, > 1000 brushes, `COLLISION_CAPPED`, pas de `BSP_ERROR`. Le rapport dit qu'il échoue sans la boucle (25412 requis) : plausible et cohérent avec le code. Le test à nombreux brushes à MaxPlanes réduit demandé est présent.

### Limites non liées à la collision (surfaces de dessin, shaders...)

Elles échouent proprement (erreur propagée, aucun fichier, `BSP_ERROR`), sans boucle infinie. Mais la boucle ne distingue pas la cause : `BspLimitException` levée par `RenderSurfaces`/`BspWriter` pour une limite que la réduction de collision ne peut pas résoudre déclenche jusqu'à 80 reconstructions complètes inutiles, puis l'erreur d'origine. Voir observations.

## Nouveau défaut introduit

- Encodage de `src/Kotor.Compiler/Compiler.cs` (diff lignes 1, 57, 69-70, 195) : le fichier a reçu un BOM UTF-8 et les accents des commentaires sont devenus du mojibake (`InspirÃ©`, `dÃ©`, `chaÃ®ne`, `entitÃ©s`...). Cause : relecture en ANSI puis réécriture UTF-8 par PowerShell (`Set-Content`). Impact : commentaires/documentation XML seulement (aucune chaîne de code accentuée n'apparaît dans le diff, les messages sont sans accents), compilation et tests non affectés. Régression cosmétique à corriger : restaurer le texte UTF-8 sans BOM (comme les autres fichiers du dépôt). Le rapport fix-2 mentionne seulement un BOM « possible » et ne signale pas le mojibake.

## Observations hors périmètre

- Pire cas coûteux : ~42 itérations pour vider le solide puis ~40 pour le terrain depuis ~11 800 éléments, soit plus que le plafond de 80 ; dans un cas extrême le terrain pourrait ne pas atteindre un état conforme et l'erreur serait alors renvoyée (la garantie « jamais d'échec » est donc vraie dans la pratique, pas mathématiquement). Une réduction adaptative (ex. dichotomie) ou un plafond relevé règlerait les deux points.
- Une garde simple (ne boucler que si la limite concernée est liée à la collision : plans/nœuds/feuilles/brushes/côtés) éviterait les 80 reconstructions inutiles sur les limites de surfaces.
- Le `CapPolygons` initial journalise aussi `COLLISION_CAPPED` : un second message peut apparaître après la boucle (doublon possible, sans gravité).

## Verdict

Constat 1 : résolu. Aucun constat ouvert. Un défaut mineur nouveau (mojibake + BOM dans les commentaires de `Compiler.cs`) à corriger avant clôture ; sinon APPROUVÉ.

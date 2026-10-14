# Etape 03 - Revue (base 008065e, tete a1095d5)

## Conformite

- ✅ Interfaces `PreviewModel`/`PreviewBuilder`/`IWorkflowSteps`/`WorkflowController` conformes a la specification (les deux parametres optionnels ajoutes sont compatibles avec les signatures fixees).
- ✅ Vues : Previsualiser = scene KOTOR, Importer = plan + reperes, Exporter = BSP relu, Creer PK3 = vue inchangee (`Packed`, modele `null` -> `_preview` conserve).
- ✅ Automate : `Task.Run`, `IsBusy`, `SelectModule` refuse si occupe, `CanPreview`/`CanNext` faux pendant le travail, echec = `Stage` inchange + `LastError` + ligne `ERREUR :` au journal, generation ecartant les resultats tardifs.
- ✅ Texture absente : cle `missing`, damier 8x8 (`ModuleExporter.Checker()`), present dans les trois vues.
- ✅ UV : `FromScene` inverse v (y compris Uv1 de la lightmap) ; `FromImport`/`FromBsp` reprennent les UV Q3 telles quelles ; `TgaReader` rend la ligne 0 en haut (verifie : bit 0x20 gere), donc coherent avec la convention de la vue.
- ✅ Lightmaps eclaircies a l'etape Importer (`Brighten` avec `LightmapBrightness`) ; a l'etape Exporter elles sont relues depuis le staging deja eclairci.
- ⚠️ « Un lot par surface du BSP » et « la vue montre exactement ce qui part dans la carte » : la boite de ciel est ecartee (voir Important 1).
- ⚠️ Exporter = « BSP relu » : reperes et collision proviennent de la vue Importer, pas du BSP (voir Mineur 3).

## Points forts

- Thread-safety du controleur saine : tout l'etat est lu et ecrit sous `_gate` ; le resultat est applique dans un seul bloc verrouille ; `_busy` est remis a faux dans un `finally` meme pour un resultat ecarte, donc une etape ancienne ne peut pas chevaucher une nouvelle (le nouveau `Run` attend `_busy == false`), ce qui protege aussi l'etat interne non verrouille de `KotorWorkflowSteps` (un seul thread d'etape a la fois, avec barriere memoire via le verrou).
- Garde de peremption correcte : le jeton est capture sous verrou au lancement, compare sous verrou a la fin ; les lignes de journal d'un resultat ecarte sont aussi filtrees (`IsCurrent`) ; l'erreur d'une etape ecartee n'ecrase pas `LastError`.
- `SelectModule` atomique (test de `_busy` + changement + reset sous le meme verrou).
- Pas de plantage : toute exception (hors OOM) est capturee ; le `Task` renvoye ne leve jamais.
- Test reel execute (non ignore), egalite des triangles Importer/Exporter verifiee ; gestion d'erreurs de lecture TGA par avertissement journalise.
- Pas de duplication : textures/lightmaps/blend deduits du plan ou de `ShaderPlanner`.

## Problemes

### Critique
Aucun.

### Important

1. **Boite de ciel ecartee dans `FromBsp` : conflit avec « la vue montre exactement ce qui part dans la carte »** - `src/Kotor.Pipeline/Preview/PreviewBuilder.cs:186` (+ test `PreviewBuilderTests.cs:770`). Le BSP relu contient 12 triangles de ciel absents de la vue ; le compte de triangles de la vue Exporter ne correspond donc pas au BSP (et le test `FromBsp_one_batch_per_surface` filtre aussi le ciel, au lieu du « un lot par surface » de la specification). L'egalite Importer = Exporter est obtenue en masquant une difference reelle. Resolution recommandee : garder la surface de ciel comme lot du modele (`Name` explicite, par exemple `sky`, drapeau `IsSky` ou `PreviewBlend` dedie, cle de texture `missing` non utilisee) pour que la vue, `TriangleCount` et le BSP concordent ; l'application decide de la dessiner (ou non) et le test d'egalite compare « hors ciel » explicitement, avec un second controle « BSP total = Importer + 12 ». A defaut minimal : exposer `PreviewModel.SkyTriangles` et l'ecrire au journal (« boite de ciel ajoutee par le compilateur : 12 triangles ») pour que l'omission soit visible.

### Mineur

1. **Reprevisualisation en echec : etat desynchronise** - `KotorWorkflowSteps.cs:394`. `Preview` met `_scene/_imported/_exported/_importModel` a null avant de travailler ; si l'on reclique « Prévisualiser » depuis `Imported`/`Exported` et que l'etape echoue, le controleur garde `Stage`/`Preview` anciens (bouton « Exporter ») mais l'implementation a perdu son etat : « Exporter » leve ensuite « Importer d'abord. » a chaque clic. Fix : construire `scene/entities/model` dans des locaux et ne remettre l'etat a zero qu'apres succes (ou faire du controleur le seul a invalider : `PreviewAsync` en echec depuis un etat avance doit remettre `Stage = None`). Meme remarque, de moindre portee, pour `Import` qui efface `_exported` avant succes.
2. **Ligne `PK3 :` non filtree par la generation** - `WorkflowController.cs:605`. `_log.Enqueue` direct, hors `IsCurrent` : un `CreatePk3` ecarte (apres `Reset`) ecrit quand meme au journal. Fix : passer par `p.Report(...)` ou verifier la generation.
3. **Vue Exporter : reperes et collision repris de la vue Importer** - `KotorWorkflowSteps.cs:454`. Ce n'est pas du « relu du BSP » ; si le compilateur perdait une entite, la vue ne le montrerait pas. Les entites du BSP (`bsp.Entities`, deja parsees pour le spawn) pourraient fournir au moins les reperes ponctuels. A documenter si conserve (le rapport le signale).
4. **Collision incoherente entre vues** - `PreviewBuilder.cs:116-118` (tous les triangles de walkmesh) contre `:156-160` (walkable + `walkmesh_solid`). L'affichage de la collision change d'une etape a l'autre sans que la carte ait change ; a harmoniser ou a documenter.
5. **`FromBsp` sans garde de longueur** - `PreviewBuilder.cs:204` (`s.Normals[i]`, `s.Uv0[i]`, `s.Uv1[i]` sans verification, contrairement a `FromImport`) : un BSP aux tableaux incoherents leve une `IndexOutOfRange` (capturee en « Exception inattendue » par le controleur, donc sans plantage, mais message peu parlant).
6. **Cle de texture commune diffuse/lightmap** (deja signalee par le rapport) - `PreviewBuilder.KeyOf` : collision possible sans erreur ; `TryAdd` garde la premiere sans avertissement.
7. **`ToDictionary` sur `plan.Shaders`/`shaders`** - `PreviewBuilder.cs:133,181` : leve sur noms de shader en double (peu probable ; `TryAdd` serait plus sur).

## Evaluation

**Corrections nécessaires** (1 point Important : la boite de ciel ecartee contredit la contrainte « la vue montre exactement ce qui part dans la carte » ; le reste de l'automate, la garde de peremption et la securite des fils sont solides).

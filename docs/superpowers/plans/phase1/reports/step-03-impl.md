# Étape 03 — rapport d'implémentation (GFF, TLK, catalogue, `list-modules`)

## Réalisé
- `Gff/GffReader`, `GffStruct`, `GffLocString` : lecteur GFF V3.2 (types 0 à 17 selon la correspondance du plan ; orientation lue w,x,y,z puis convertie en `Quaternion(x,y,z,w)`). Garde-fous : version vérifiée, profondeur limitée à 64, indices contrôlés (`InvalidDataException`).
- `Tlk/TlkFile` : TLK V3.0, Windows-1252, `Get` renvoie null hors table (dont 0xFFFFFFFF).
- `Modules/ModuleInfo`, `ModuleCatalog` : `ListModuleNames` (hors `*_s.rim`, minuscules, tri ordinal) et `Load` (IFO puis ARE ; nom = TLK, sinon première chaîne locale, sinon resref).
- `Kotor2Ja.Cli/Commands/ListModulesCommand` + `Program.cs` (option `--kotor`, sinon `KOTOR_PATH`, sinon chemin Steam ; code 2 si dossier introuvable). Un module illisible est affiché `(illisible : ...)` au lieu d'interrompre la liste.
- `System.CommandLine 2.0.13` ajouté à `Kotor2Ja.Cli.csproj` (même version que le projet TFU).

## TDD
- RED : après écriture des tests, `dotnet test tests/Kotor.Core.Tests` échoue à la compilation (CS0234 : espaces de noms `Kotor.Core.Gff`, `Tlk`, `Modules` inexistants).
- Premier GREEN partiel : 2 échecs dont `Real_tar_m02aa_info` (OverflowException dans `ReadField` : `checked((int)v)` calculé pour des types inline comme float/uint). Corrigé en ne calculant l'offset de données que pour les types complexes. Le test réel a donc détecté un vrai bug.
- GREEN final : `dotnet test Kotor2Ja.sln` : Kotor.Core.Tests 37/37, Kotor.Compiler.Tests 39/39, aucun avertissement, aucun test ignoré (KOTOR installé).
- Tests ajoutés : `Gff_reads_synthetic_struct`, `Get_returns_default_for_missing_label`, `Bad_version_throws`, `Tlk_reads_synthetic_entry`, `ListModuleNames_excludes_s_rims`, `Load_reads_synthetic_module`, `Real_tar_m02aa_info`, `Real_catalog_has_23_taris_modules`. Support : `tests/Kotor.Core.Tests/Gff/GffBuilder.cs` (constructeur de GFF synthétiques).

## Fichiers
Créés : `src/Kotor.Core/Gff/{GffReader,GffStruct,GffLocString}.cs`, `src/Kotor.Core/Tlk/TlkFile.cs`, `src/Kotor.Core/Modules/{ModuleCatalog,ModuleInfo}.cs`, `src/Kotor2Ja.Cli/Commands/ListModulesCommand.cs`, `tests/Kotor.Core.Tests/{Gff/GffReaderTests,Gff/GffBuilder,Tlk/TlkFileTests,Modules/ModuleCatalogTests}.cs`.
Modifiés : `src/Kotor2Ja.Cli/Program.cs`, `src/Kotor2Ja.Cli/Kotor2Ja.Cli.csproj`.

## Sortie `list-modules` (lignes tar_*, 23 modules ; 117 lignes au total, code de sortie 0 ; `--kotor C:/nope` donne code 2)
```
tar_m02aa	Taris - appartements sud
tar_m02ab	Taris - quartier nord de la ville haute
tar_m02ac	Taris - Ville haute, sud
tar_m02ad	Taris - appartements nord
tar_m02ae	Taris - cantina ville haute
tar_m02af	Taris - planque
tar_m03aa	Taris - ville basse
tar_m03ab	Taris - appartements ville basse
tar_m03ad	Taris - appartements ville basse
tar_m03ae	Taris - cantina de Javyar
tar_m03af	Taris - plate-forme à fonceurs
tar_m03mg	Taris - piste de fonceurs
tar_m04aa	Taris - cité souterraine
tar_m05aa	Taris - égouts inférieurs
tar_m05ab	Taris - égouts supérieurs
tar_m08aa	Taris - propriété de Davik
tar_m09aa	Taris - base sith
tar_m09ab	Taris - base sith
tar_m10aa	Taris - base des Vulkars
tar_m10ab	Taris - base des Vulkars
tar_m10ac	Taris - base des Vulkars
tar_m11aa	Taris - base des Beks
tar_m11ab	Taris - base des Beks
```
Nom réel de `tar_m02aa` : **Taris - appartements sud** (area `m02aa`).

## Remarques
- Les noms sont en français (dialog.tlk localisé) ; l'encodage Windows-1252 rend correctement les accents.
- Le test `Real_tar_m02aa_info` valide la direction par norme > 0 ; la valeur exacte n'est pas figée.
- L'orientation (type 16) n'est testée que sur données synthétiques (ordre w,x,y,z du plan), pas sur un fichier réel.
- Le constructeur de tests utilise `Encoding.Latin1` (équivalent à 1252 pour les caractères testés).

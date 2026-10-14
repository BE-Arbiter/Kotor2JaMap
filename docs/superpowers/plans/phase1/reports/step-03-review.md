# Étape 03 — revue (GFF, TLK, catalogue, `list-modules`)

Base 0718443, tête b335741. Revue sur le diff uniquement ; les affirmations du rapport (37/37, 39/39) ne sont pas rejouées.

## Conformité

- ✅ Fichiers et interfaces conformes au plan (`GffLocString`, `GffStruct`, `GffReader.Read(byte[], out string)`, `TlkFile.Open/Get`, `ModuleEntry`, `ModuleInfo`, `ModuleCatalog.ListModuleNames/Load`).
- ✅ Correspondance des types GFF 0 à 17 conforme. Les décalages d'en-tête (8..52), l'indexation des champs (1 champ = index direct, sinon table d'indices), la CExoLocString (taille, StrRef, nombre, id, longueur, texte) et la CExoString sont corrects.
- ✅ TLK V3.0 : en-tête sur 20 octets, entrées de 40 octets, offset @28 et taille @32, Windows-1252, null hors table (dont 0xFFFFFFFF).
- ✅ IFO/ARE : `Mod_Area_list[0].Area_Name`, `Mod_Entry_X/Y/Z`, `Mod_Entry_Dir_X/Y`. Nom affiché : TLK, puis première chaîne locale (ordre de clé), puis resref.
- ✅ `ListModuleNames` : `*.rim` hors `*_s`, minuscules, `Distinct`, tri ordinal.
- ✅ Commande `list-modules [--kotor]`, sortie `<module>\t<nom>`, code 2 si le dossier est introuvable, enregistrée dans `Program.cs`.
- ✅ Tests demandés tous présents (synthétique, valeur par défaut, TLK, `_s.rim`, deux `SkippableFact` réels) plus `Bad_version_throws` et `Load_reads_synthetic_module`. Les 23 modules `tar_*` de la sortie rapportée sont cohérents (6+6+1+2+1+2+3+2).
- ✅ KOTOR en lecture seule (uniquement `ReadAllBytes`, énumération, `ResourceResolver`).
- ⚠️ Resref en minuscules : appliqué à `AreaResRef` et aux noms de modules. `GffReader` renvoie les resref (type 11) tels quels (acceptable pour un lecteur générique, le plan ne l'exige pas), et `ModuleInfo.Module` reprend l'argument sans normalisation.
- ⚠️ Orientation (type 16) : le plan demande de « vérifier dans les références » l'ordre w,x,y,z. Le rapport avoue que ce n'est testé que sur données synthétiques, donc l'ordre n'est pas vérifié. Aucun usage à cette étape.
- ⚠️ Fidélité (journal) : un module illisible est affiché `(illisible : …)`, ce n'est pas silencieux. Mais l'absence de `dialog.tlk` ou d'ARE retombe sans trace sur la resref, et les champs d'entrée absents valent 0 sans avertissement. Le journal à codes n'existe pas encore à cette étape, donc pas bloquant.

## Points forts

- Lecteur GFF compact, correct sur les points fins : types inline contre types à données déportées, cache des structs, profondeur limitée, `checked` sur les conversions.
- Constructeur GFF de test indépendant du lecteur, utilisé pour tester les familles de types et le catalogue sans KOTOR.
- Le test réel a révélé un vrai bug (calcul d'offset sur les types inline), corrigé. Bon emploi du TDD.
- Dégradation propre dans `list-modules` : un module défaillant n'interrompt pas la liste.

## Problèmes

### Critique
Aucun.

### Important
Aucun.

### Mineur
1. `src/Kotor.Core/Gff/GffReader.cs:123-235` : le rapport annonce des « indices contrôlés (`InvalidDataException`) », ce qui est partiel. Les offsets de listes, de données de champ, d'étiquettes et de CExoLocString ne sont pas vérifiés par rapport à la taille du buffer. Un GFF tronqué ou corrompu lève `ArgumentOutOfRangeException`, `IndexOutOfRangeException` ou `OverflowException` (`ReadLabel` fait `d[off+len]`), voire une allocation énorme via `new List<GffStruct>(n)`. Correctif : valider les offsets de l'en-tête et `n`/`len` contre `data.Length`, ou encapsuler en `InvalidDataException`.
2. `src/Kotor2Ja.Cli/Commands/ListModulesCommand.cs:435` : le `catch` ne couvre que `InvalidDataException` et `IOException`. Avec le point 1, un module corrompu fait planter toute la commande au lieu d'afficher `(illisible : …)`. Correctif : élargir le filtre ou corriger le lecteur.
3. `src/Kotor2Ja.Cli/Commands/ListModulesCommand.cs:426` et `src/Kotor.Core/Modules/ModuleCatalog.cs:307-318` : l'absence de `dialog.tlk` ou d'ARE donne silencieusement la resref comme nom de zone. Prévoir un avertissement (journal à codes quand il existera).
4. `src/Kotor.Core/Gff/GffReader.cs:173,176-181` (type 16) : ordre w,x,y,z non confirmé par un fichier réel ou une référence. Le vérifier avant la première utilisation (par ex. orientation d'une caméra ou d'un placeable réel).
5. `GffReader.cs:111-117` et `TlkFile.cs:350-360` : enregistrement du `CodePagesEncodingProvider` dupliqué. À factoriser dans un petit helper `Encodings.Win1252`.
6. `ModuleCatalogTests` : le chemin « nom issu du TLK, prioritaire sur la chaîne locale » n'est couvert que par le test réel (ignoré sans KOTOR). Un test synthétique avec un `TlkFile` serait plus robuste.
7. `ModuleInfo.Module` n'est pas mis en minuscules (cohérence avec la contrainte resref).

## Évaluation

**Approuvé** (réserves mineures uniquement).

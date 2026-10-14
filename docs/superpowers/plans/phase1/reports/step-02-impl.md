# Étape 02 — Rapport d'implémentation

## Réalisé
Archives KOTOR et résolution des ressources, dans `src/Kotor.Core/Archives/` (namespace `Kotor.Core.Archives`) :
- `ResourceType.cs` : enum `ResourceType : ushort` (valeurs de l'étape) et `ResourceKey` (record struct, resref normalisé en minuscules à la construction). Un type non nommé reste valide (cast numérique).
- `IResourceArchive.cs` : interface + helpers internes (`ArchiveIo` : resref 16 octets, lecture à offset, contrôle de signature).
- `RimArchive.cs`, `ErfArchive.cs` (accepte `ERF V1.0` et `MOD V1.0`) : table de clés lue à l'ouverture, données lues à la demande via `FileStream`.
- `KeyBifArchive.cs` : lit `chitin.key`, ouvre chaque BIF à la demande, table des variables mise en cache, lecture de la ressource par seek (BIF jamais chargé entièrement). Méthode additionnelle `BifNameOf(key)`.
- `ResourceResolver.cs` : ordre Override -> `<m>.rim` -> `<m>_s.rim` -> (Tpc/Txi) `swpc_tex_tpa.erf` -> chitin.key. Archives absentes ignorées. `FindSource` renvoie `Override\<fichier>`, le chemin du RIM/ERF, ou `chitin.key:data\models.bif`.

## TDD
RED : `dotnet test tests/Kotor.Core.Tests --filter Archives` -> `error CS0234: Le nom de type ou d'espace de noms 'Archives' n'existe pas dans l'espace de noms 'Kotor.Core'` (types absents).

GREEN : `dotnet test Kotor2Ja.sln` ->
- Kotor.Core.Tests : réussite 29, échec 0, ignorés 0 (les tests d'intégration réels s'exécutent, ne sont pas ignorés)
- Kotor.Compiler.Tests : réussite 39, échec 0
- Kotor2Ja.Cli.Tests : aucun test (comme à l'étape 01)
Aucun avertissement de compilation.

Tests ajoutés (`tests/Kotor.Core.Tests/Archives/`) : `Rim_reads_synthetic_entries`, `Keys_are_lowercase`, `Erf_reads_synthetic_entries`, `Mod_signature_is_accepted`, `Resolver_prefers_override`, `Resolver_falls_back_to_rim`, `Resolver_missing_returns_null`, `[SkippableFact] Real_install_finds_tar_m02aa_resources`, `[SkippableFact] Real_key_lists_and_reads_models` ; helper `ArchiveBuilders.cs` (RIM/ERF synthétiques en mémoire, faux dossier KOTOR dans un dossier temporaire).

## Fichiers
Créés : 6 fichiers sous `src/Kotor.Core/Archives/` ; `tests/Kotor.Core.Tests/Archives/{ArchiveBuilders,RimArchiveTests,ErfArchiveTests,ResourceResolverTests,KeyBifArchiveTests}.cs`. Aucun fichier existant modifié.

## Concerns
- Les formats réels correspondent à la description (test d'intégration vert sur `tar_m02aa`, `models.bif` confirmé).
- Doublons de clés dans une archive : la première entrée gagne (`TryAdd`).
- Les archives gardent des `FileStream` ouverts (lecture partagée) ; non thread-safe (seek + read).
- Un fichier Override avec un type inconnu est cherché avec l'extension numérique.
- Rien n'a été commité.

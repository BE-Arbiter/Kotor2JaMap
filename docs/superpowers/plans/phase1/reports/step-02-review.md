# Étape 02 — Revue (f07da95..7baeb8c)

## Conformité
- ✅ Fichiers attendus créés (ResourceType/ResourceKey, IResourceArchive, KeyBifArchive, RimArchive, ErfArchive, ResourceResolver) et tests associés.
- ✅ Formats : offsets KEY (12 / 22 octets, `id>>20`, `id&0xFFFFF`), BIF (entrée de 16 octets, offset/size @+4/+8), RIM (type u32 @16, offset @24, size @28), ERF (clé de 24 octets, type u16 @20, entrée de ressource de 8 octets). Signatures `ERF V1.0` et `MOD V1.0` acceptées.
- ✅ BIF ouverts à la demande, table des variables mise en cache, lecture par seek sans charger le BIF entier.
- ✅ Ordre du résolveur : Override, `<m>.rim`, `<m>_s.rim`, `swpc_tex_tpa.erf` (Tpc/Txi seulement), chitin.key. Archives absentes ignorées.
- ✅ Focus « Override prime » : le fichier libre est testé avant toute archive. Couvert par `Resolver_prefers_override` (Override vs RIM).
- ✅ Focus « casse » : `ResourceKey` met le resref en minuscules à la construction. Couvert par `Keys_are_lowercase`, `Resolver_falls_back_to_rim` (`"M02AA"`) et `Rim_reads_synthetic_entries` (`"B_TWO"`).
- ✅ Types inconnus conservés comme valeurs numériques brutes (cast `ushort` vers l'enum).
- ⚠️ `ResourceKey` est un `record struct` avec propriétés et constructeur explicites, pas un record positionnel. La normalisation l'exige, mais `Deconstruct` disparaît. Sans conséquence.
- ✅ Tests demandés tous présents, plus `Resolver_falls_back_to_rim` et `Real_key_lists_and_reads_models`. Intégration réelle non ignorée (selon le rapport).

## Points forts
- Les `FileStream` sont libérés si l'ouverture échoue (`catch { Dispose; throw; }`), y compris dans `ForModule`.
- `ArchiveIo` mutualise la lecture, la signature et le resref.
- `FindSource` renvoie `chitin.key:data\models.bif`, utile pour le journal.
- Les doublons de clés sont traités de façon déterministe (`TryAdd`, le premier gagne).
- Les constructeurs d'archives synthétiques sont propres et réutilisables.

## Problèmes
### Critique
Aucun.

### Important
Aucun.

### Mineur
1. `ResourceResolver.cs:354-355` : le nom du fichier Override est cherché en minuscules. Sur un système de fichiers sensible à la casse, `Override\M02AA.LYT` ne serait pas trouvé. Sous Windows (cible actuelle) c'est sans effet. Correctif : énumérer `Override` une fois dans un dictionnaire insensible à la casse. Aucun test ne vérifie l'Override avec un nom en majuscules.
2. `ResourceResolver.cs:332-333` : un type inconnu produit une extension numérique (`.9999`), qui ne correspond à aucun fichier réel. Le rapport le signale. Inoffensif, mais l'Override est de fait inopérant pour ces types.
3. `ResourceResolver.cs:354` : le `resRef` n'est pas validé avant `Path.Combine`. Un resref contenant `..\` ou un chemin absolu pourrait sortir de `Override`. Risque faible car les resrefs viennent des fichiers du jeu, mais le garde-fou est simple (rejeter `Path.GetFileName(x) != x`).
4. `RimArchive.cs:468` : `(ushort)` appliqué à un `uint` tronque silencieusement un type invalide au lieu de le rejeter.
5. `KeyBifArchive.cs:260` : `varCount * 16` n'est pas dans un `checked`, alors que les autres archives utilisent `checked`. Sans effet sur des données saines, incohérent sur des fichiers corrompus.
6. Les tests ne couvrent pas l'ordre `_s.rim` après `.rim` ni la restriction du texture pack à Tpc/Txi. Le comportement est correct à la lecture du code, mais aucune régression ne serait détectée.
7. Les archives ne sont pas thread-safe (seek + read sur un `FileStream` partagé). C'est signalé dans le rapport et acceptable en phase 1.

## Évaluation
**Approuvé**

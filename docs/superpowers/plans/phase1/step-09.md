# Étape 09 — Validation en jeu : échelle et rendu (orchestrateur + utilisateur)

**But :** fixer l'échelle par défaut sur preuve en jeu, et vérifier les critères de la spec §8 qui relèvent de la phase 1.

Cette étape n'est pas confiée à un agent. L'orchestrateur prépare les cartes et la liste de contrôle. L'utilisateur joue et répond.

**Fichiers :**
- Modifier (selon la décision) : `ExportOptions.DefaultScale`, `src/Kotor2Ja.Cli/kotor2ja.config.json`, et le test `Config_defaults_when_missing`.
- Écrire : `reports/step-09-validation.md` (réponses de l'utilisateur, captures éventuelles, décision).

- [ ] **Étape 1 : produire trois variantes d'échelle**

```bash
dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name tar_m02aa_s32 --scale 32
```
```bash
dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name tar_m02aa_s36 --scale 36
```
```bash
dotnet run -c Release --project src/Kotor2Ja.Cli -- export-ja tar_m02aa --name tar_m02aa_s40 --scale 40
```
Attendu : trois pk3 `kotor_tar_m02aa_s32/36/40.pk3` dans `GameData\base\`, chacun avec le code de sortie 0.

- [ ] **Étape 2 : liste de contrôle pour l'utilisateur** (`SWGL_SP.x86_64.exe`, rendu Vulkan, `devmap tar_m02aa_s36` puis les deux autres). Pour chaque variante :
  1. La carte se charge sans erreur bloquante (noter les messages de la console).
  2. On apparaît sur le sol, sans tomber à travers.
  3. **Échelle** :
     - une porte KOTOR (fermée, figée en phase 1) fait environ 1,5 fois la hauteur du joueur ;
     - un couloir se parcourt sans paraître étroit ni démesuré ;
     - comparaison avec un PNJ SWGL (`npc spawn` d'un humain).
  4. On ne traverse pas les murs.
  5. Les textures sont à l'endroit, et l'éclairage ressemble à KOTOR (ni brûlé ni trop sombre).
- [ ] **Étape 3 : décision.** L'utilisateur choisit une échelle (32, 36, 40 ou une autre valeur à retester).
  - Si l'éclairage est jugé trop sombre ou trop clair, on réexporte avec `--lightmap-brightness` (1,5 puis 2,0) jusqu'à son accord, et la valeur retenue devient la valeur par défaut.
- [ ] **Étape 4 : appliquer** les valeurs par défaut retenues, puis lancer `dotnet test Kotor2Ja.sln` (tout vert).
- [ ] **Étape 5 : nettoyer.** Supprimer les pk3 de variantes de `GameData\base\` (avec l'accord de l'utilisateur) et réexporter `tar_m02aa` avec les valeurs par défaut.
- [ ] **Étape 6 : commit**, puis relecture finale de la branche par un nouvel agent sonnet, puis fusion locale dans `master`.

Tout défaut constaté en jeu (trou, texture, collision) fait l'objet d'une correction confiée à un nouvel agent, avec un rapport `step-09-fix-R.md`, avant la fusion.

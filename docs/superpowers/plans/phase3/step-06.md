# Étape 06 — Passe anglais

**But :** tout texte visible par l'utilisateur est en anglais (spec §2, « Anglais »).

**Fichiers :** tout `src/**` où un texte arrive à l'utilisateur :
- les libellés ImGui, le titre de la fenêtre, les noms de nœuds et les libellés de boutons (`NextLabel` de `WorkflowController`) ;
- les messages de `ImportLog.Warn` (les codes ne changent pas), le bilan du journal (`Resume par code` devient `Summary by code`) ;
- les messages d'exception remontés à l'utilisateur, `ExportOptions.Validate` ;
- l'aide et les sorties de la CLI, les messages d'erreur de la configuration ;
- `README.md`, à réécrire en anglais : installation, utilisation de l'application et de la CLI, configuration, KOTOR 2.

Les tests qui vérifient des textes sont à adapter.

**Ce qui est fixé :**
- Libellés : `Preview`, `Import`, `Export`, `Create PK3`, `KOTOR`, `KOTOR 2`, `Collisions`, `Sky`, `Double-sided`, `Log`. Titre : `KOTOR → Jedi Academy`.
- Les noms des types d'entités affichés suivent les noms de l'enum en anglais : `Placeable`, `Door`, `Waypoint`, `Trigger`, `Sound`, `Store`, `Camera`, `Light`.
- On ne traduit pas : les commentaires de code, `docs/**`, les codes du journal, les noms de zones issus du TLK.
- Pas de bibliothèque de localisation : les chaînes sont simplement remplacées.

- [ ] **Étape 1 : inventaire.** Chercher les chaînes françaises restantes (`grep` sur les accents, sur les mots courants comme « introuvable », « Échelle », « maillage », « pièce », « Étape », sur `"` suivi d'une majuscule accentuée…) et les lister dans le rapport.
- [ ] **Étape 2 : traduire** et adapter les tests.
- [ ] **Étape 3 : `dotnet build` / `dotnet test` verts.** Refaire la recherche : il ne reste que des commentaires. Joindre au rapport la commande de recherche et son résultat final.
- [ ] **Étape 4 : rapport** `reports/step-06-impl.md`.

# SWGLKotor-Map

Outil en ligne de commande (C#, .NET 10) qui convertit un module de *Star Wars: Knights of the Old Republic* en carte pour *Jedi Academy* (SWGL).

## État actuel (phase 1, étape 01)

- Solution `Kotor2Ja.sln` : bibliothèques `Kotor.Core` et `Kotor.Compiler`, commande `kotor2ja` (pas encore de commande).
- Compilateur BSP repris du projet TFU (voir `THIRD-PARTY-NOTICES.md`).
- Écriture TGA, PNG, shader et pk3 reprise du projet TFU.

## Prérequis

- SDK .NET 10
- Une installation de KOTOR (Steam). Chemin par défaut : `C:\Program Files (x86)\Steam\steamapps\common\swkotor`. Il peut être changé avec la variable d'environnement `KOTOR_PATH`.

## Compiler et tester

```
dotnet build Kotor2Ja.sln
dotnet test Kotor2Ja.sln
```

Les tests d'intégration sur le jeu installé sont ignorés si KOTOR est absent.

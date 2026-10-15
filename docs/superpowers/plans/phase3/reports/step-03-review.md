# Étape 03 — Objets placés → MD3 : relecture

## Verdict 1 : conformité

❌ (un écart bloquant sur les limites par surface ; le reste est conforme, ou justifié).

Écarts :
- `src/Kotor.Core/Export/Md3Writer.cs:15` : limites 4096 sommets / 8192 triangles (constantes de `qfiles.h`). Le chargeur réel de ce moteur refuse beaucoup moins (voir Critical 1). La demande de relecture parle de 4096 triangles : ni l'un ni l'autre n'est la limite effective.
- Écart assumé, accepté : `angles "0 <yaw> 0"` au lieu de `yaw + 90` (voir ci-dessous). La spec disait explicitement « l'agent doit le vérifier », donc ce n'est pas un écart de conformité.
- `PipelineSteps.ConvertObjects` : sans dossier de jeu, retour silencieux d'un résultat vide, sans code au journal (voir Minor 2).

Conformes : classnames/clés (`g_misc.cpp:408-417`, `g_spawn.cpp:265-266`, `cg_main.cpp:2192-2218`, vérifiées), un MD3 par modèle, shader `models/<dossier>/<texture>`, `rgbGen lightingDiffuse`, alpha avec `GE128` + `cull none`, `modelscale` entier, codes `PLACEABLE_SKIPPED` / `PLACEABLE_NO_PWK` / `PLACEABLES`, écriture limitée au staging (tests en dossiers temporaires, sauf `out/` du dépôt prévu par le plan).

### Jugement sur « pas de +90 »
L'argument est correct. Les sommets du MD3 sont ceux du MDL, non tournés, dans le même repère (Z haut). `AnglesToAxis` avec un yaw `a` fait tourner le modèle de `a` anti-horaire autour de Z, exactement comme le `Bearing` du GIT. Le +90 (0 = +Y chez KOTOR, 0 = +X chez Quake) ne vaut que si l'on tourne la géométrie ; la spec supposait un modèle réorienté, ce qui n'est pas le cas. La preuve empirique (95/962 points hors walkmesh pour `yaw`, contre 14 % pour `-yaw` et 25 à 27 % pour ±90) départage nettement les ±90 mais peu `yaw` contre `-yaw` ; la preuve mathématique porte donc l'essentiel. La confirmation visuelle en jeu reste à faire (réserve légitime). Le même yaw est utilisé pour la PWK (`PlaceableConverter.cs:87`), donc modèle et collision restent cohérents.

## Verdict 2 : qualité

### Critical

1. **Limites par surface supérieures à celles du chargeur MD3 du moteur : une carte peut ne plus se charger.**
   `Md3Writer.cs:15,121,132` autorise 4095+ sommets et 8192 triangles par surface. Le chargeur `R_LoadMD3` (`OpenJK-SWGL/code/rd-vulkan/tr_model.cpp:1106-1119`) refuse `numVerts >= SHADER_MAX_VERTEXES` et `numTriangles*3 >= SHADER_MAX_INDEXES`, avec `SHADER_MAX_VERTEXES = 1200` et `SHADER_MAX_INDEXES = 6*1200` (`code/qcommon/qfiles.h:33-34`), soit au plus 1199 sommets et 2399 triangles. Le rendu applique la même borne (`RB_CHECKOVERFLOW`).
   Scénario : un placeable à gros maillage (par exemple une grande structure, 1500 sommets pour une seule texture) donne une surface de 1500 sommets, écrite sans découpe. `cgi_R_RegisterModel` renvoie 0 et `CG_CreateMiscEnts` fait `Com_Error(ERR_DROP, "misc_model_static failed to load model")` (`cg_main.cpp:2243-2247`) : la carte entière échoue au chargement, pas seulement l'objet. Les tests (`Md3WriterTests.cs:81`, 5000 sommets) valident l'ancienne limite et ne détectent rien.
   Correction : `MaxVerticesPerSurface = 1199`, `MaxTrianglesPerSurface = 2399` (le découpage existe déjà), adapter le test, et recompter `MaxSurfaces` (32 reste la borne du moteur, les erreurs sont déjà journalisées par `PLACEABLE_SKIPPED`).

### Important

1. **Surfaces > 32 : modèle sauté avec un message d'`ArgumentException`.** Avec la découpe à 1199 sommets, davantage de modèles dépasseront 32 surfaces ; le code les saute proprement (`PlaceableConverter.cs:~140`, journalisé), mais leur collision PWK disparaît aussi. Accepter ou fusionner les petites surfaces d'une même texture. Constat à traiter après la correction ci-dessus, d'où Important et non Critical.

2. **Collision non ajoutée pour les objets sautés.** `PlaceableConverter.cs:67` : un objet dont le MD3 échoue n'a ni modèle ni collision (cohérent avec la spec), mais un `plc_invis` (sans maillage rendu, avec PWK) perd aussi sa collision invisible. Documenté en réserve 2 ; à confirmer avec l'orchestrateur, car des blocages invisibles servent souvent de garde-fous de gameplay.

### Minor

1. `Md3Writer.cs:~148` (`EncodeNormal`) : l'encodeur utilise 255/2π alors que le décodeur du moteur (`tr_model.cpp:1165-1175`) interprète l'octet sur 256 pas de 2π. Erreur angulaire jusqu'à ~1,4° sur la longitude/latitude, invisible en pratique (quirk connu de q3map2). Utiliser 256/2π serait exact.
2. `PipelineSteps.cs` (`ConvertObjects`) : sans dossier de jeu lisible, retour vide sans aucune ligne au journal. Pas un abandon d'élément, mais une ligne d'information (code, anglais) éviterait de croire que la carte n'a pas d'objets.
3. `ModuleEntitiesReader.ReadObjects` relit et revalide le GIT : les avertissements `GIT_ENTRY_INVALID` (texte français, existant) peuvent sortir deux fois si le même journal sert à la lecture de la vue. De plus `Read` résout désormais le modèle de chaque objet (2DA, GFF) pour la vue Preview : coût inutile à l'ouverture d'un module.
4. `ModelMeshBuilder.cs:~36` : normales transformées par `TransformNormal(transform)` ; correct pour rotation/translation, faux pour une échelle non uniforme (rare). Les nœuds skin/danglymesh sont rendus en pose de base sans journal spécifique.
5. Déterminisme du pk3 : l'ordre est trié (`Pk3Builder.cs:28`), les MD3, shaders et textures sont triés, donc le contenu est stable. Les octets du zip varient en revanche avec l'heure de modification des fichiers du staging (`CreateEntryFromFile`) ; cela préexiste à l'étape. À corriger si « identique » est vérifié par hachage du pk3 (fixer `LastWriteTime`).
6. Enroulement des triangles : conservé tel que dans le MDL, comme pour le BSP (aucun retournement dans le compilateur), donc cohérent avec le reste de la carte ; non vérifié visuellement. Les UV (`t = 1 − v`, `Md3Writer.cs:~90`) sont cohérents avec `ModuleExporter.FlipV` (`ModuleExporter.cs:141,245`) : correct.

### Format MD3 : vérifications faites (sans écart)
En-tête 108 octets, frame 56, en-tête de surface 108, shader 68, ST 8, XYZN 8 par sommet ; `ofsSurfaces`, `ofsEnd`, offsets de surface relatifs au début de surface, tout aligné sur 4 ; `ofsEnd` du fichier = taille totale ; noms 64 octets terminés par zéro (63 utiles, refus au-delà) ; version 15 ; normale : octet bas = acos(z), haut = atan2(y,x), conforme au décodage `tr_model.cpp`. Limite de 256 shaders : sans objet (un shader par surface). Partage : un MD3 par modèle (cache `models`), textures et shaders chargés une fois par nom, TGA dédupliqués sur `RelPath` avec ceux du BSP. `modelscale` : entier `k = ceil(max/511)`, appliqué seulement au MD3, jamais à l'origine ni à la PWK ; le moteur lit bien `modelscale` (`g_misc.cpp:411`). PWK : rotation par `e.Yaw` puis translation puis `scale`, cohérente avec le modèle.

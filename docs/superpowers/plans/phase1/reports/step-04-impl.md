# Étape 04 — Rapport d'implémentation (LYT, TPC/TGA/TXI)

## Ce qui a été construit
- `Layout/LayoutFile.cs`, `LayoutRoom.cs` : parseur LYT (culture invariante, noms en minuscules, sections track/obstacle lues et ignorées). Ordre d'une ligne doorhook : `room name 0 x y z qx qy qz qw` (champ 3 ignoré ; quaternion stocké x,y,z,w). Source : PyKotor `io_lyt.py` (lecture et écriture).
- `Textures/DxtDecoder.cs` : DXT1 (mode 3 couleurs : indice 3 transparent) et DXT5, dimensions non multiples de 4 rognées.
- `Textures/TpcDecoder.cs` : encodages 1 (gris), 2 (RGB / DXT1), 4 (RGBA / DXT5), premier mip, texte TXI après toutes les mips (tailles de mips calculées), cube (`h == 6*w`) : première face, `Raw["cube"]="1"`. L'encodage 0x0C (BGRA swizzlé) lève `InvalidDataException` (hors périmètre de l'étape).
- `Textures/TxiInfo.cs`, `KotorTexture.cs` (+ `TextureKind`), `TextureLookup.cs` (diffuse : TPC puis TGA+TXI ; lightmap : TGA puis TPC ; casse ignorée).
- `Export/TgaReader.cs` : ajout du RLE (type 10) ; les autres types restent rejetés.

## Orientation verticale
- xoreos `tpc.cpp` ne retourne rien à la lecture (aucun flip) : les lignes sont copiées telles que stockées, l'orientation est laissée au moteur GL (v=0 = bas). Donc le fichier est probablement stocké de bas en haut.
- Vérification visuelle sur textures réelles : décodé sans retournement puis avec. Le logo `logo_sw_02` (texte "STAR WARS KNIGHTS OF THE OLD REPUBLIC", `swpc_tex_gui.erf`) et `lbl_abilities` (barre de titre en haut) sont à l'endroit **avec** retournement des lignes. `TpcDecoder.StoredBottomUp = true` retourne donc les lignes ; le contrat « ligne 0 = haut » est respecté.
- Les TGA (lightmaps) sont déjà gérés par `TgaReader` (bit d'origine de l'en-tête). PNG de sortie : `out/test-textures/lta_cli01.png` (512x512, sable/roche, orientation peu parlante : strates, sable en haut) et `m02aa_01a_lm0.png` (64x64, lightmap). Ces deux-là n'ont pas de motif orienté net ; l'orientation est tranchée par le logo. Contrôle final aux étapes 06/09 comme prévu.

## TDD
- RED : après écriture des tests, `dotnet test` échoue à la compilation (espaces de noms `Kotor.Core.Layout`/`Textures` et types introuvables, CS0234/CS0246).
- GREEN : `dotnet test Kotor2Ja.sln` : Kotor.Core.Tests 52 réussis, 0 ignoré (les 2 tests réels s'exécutent) ; Kotor.Compiler.Tests 39 réussis ; 0 avertissement (build `--no-incremental`).
- Tests ajoutés : `Lyt_parses_rooms_and_doorhooks`, `Real_lyt_tar_m02aa`, `Dxt1_decodes_known_block`, `Dxt1_three_color_mode_has_transparent_index3`, `Dxt5_decodes_alpha_block`, `Dxt_non_multiple_of_4_is_cropped`, `Tpc_uncompressed_rgba_roundtrip` (vérifie aussi le retournement), `Tpc_rgb_gray_and_dxt1`, `Tpc_skips_mips_to_find_txi`, `Tpc_cube_keeps_first_face`, `Txi_detects_animation`, `Txi_defaults_and_fields`, `Lookup_prefers_tpc_over_tga_for_diffuse`, `Real_lightmap_and_diffuse_decode`, `Reads_rle_tga`, `Rejects_unsupported_tga_type` (remplace `Rejects_compressed_tga`, devenu faux).

## Fichiers
Créés : `src/Kotor.Core/Layout/{LayoutFile,LayoutRoom}.cs`, `src/Kotor.Core/Textures/{KotorTexture,TpcDecoder,DxtDecoder,TxiInfo,TextureLookup}.cs`, `tests/Kotor.Core.Tests/Layout/LayoutFileTests.cs`, `tests/Kotor.Core.Tests/Textures/{DxtDecoderTests,TpcDecoderTests,TxiInfoTests,TextureLookupTests}.cs`.
Modifiés : `src/Kotor.Core/Export/TgaReader.cs`, `tests/Kotor.Core.Tests/TgaReaderTests.cs`.

## Réserves
- Le LYT réel `m02aa` a bien ≥ 10 pièces dont `m02aa_01a` ; le format doorhook n'est vérifié que sur texte synthétique (le test réel ne contrôle pas les doorhooks). Le champ ignoré (index 2) est supposé toujours `0`.
- TPC swizzlé (0x0C) et gris compressé non gérés (exception).
- `TxiInfo.Parse` : `EnvMapTexture` retombe sur `bumpyshinytexture` si `envmaptexture` est absent (ajout non demandé, pratique pour l'étape matériaux).
- Les erreurs de décodage (fichier corrompu) lèvent une exception, `Find` ne renvoie `null` que si la ressource est absente.
- Bash sur cet environnement échoue sur les heredocs contenant des apostrophes : fichiers écrits via l'outil Write.

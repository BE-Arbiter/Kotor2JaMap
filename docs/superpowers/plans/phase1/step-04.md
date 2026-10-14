# Étape 04 — Disposition LYT et textures TPC/TGA/TXI

**But :** connaître les pièces d'un module et leur position, et décoder n'importe quelle texture ou lightmap KOTOR en RGBA.

**Fichiers :**
- Créer : `src/Kotor.Core/Layout/{LayoutFile,LayoutRoom}.cs`
- Créer : `src/Kotor.Core/Textures/{KotorTexture,TpcDecoder,DxtDecoder,TxiInfo,TextureLookup}.cs`
- Tester : `tests/Kotor.Core.Tests/Layout/LayoutFileTests.cs`, `Textures/{DxtDecoderTests,TpcDecoderTests,TxiInfoTests,TextureLookupTests}.cs`

**Interfaces :**
- Consomme : `ResourceResolver` (02) et `TgaReader` (01). Si `TgaReader` ne gère pas le RLE (type 10), il faut l'ajouter, avec un test.
- Produit :
```csharp
public sealed record LayoutRoom(string Model, Vector3 Position);              // Model en minuscules, mètres
public sealed record LayoutDoorHook(string Name, string Room, Vector3 Position, Quaternion Orientation);
public sealed class LayoutFile
{
    public IReadOnlyList<LayoutRoom> Rooms { get; }
    public IReadOnlyList<LayoutDoorHook> DoorHooks { get; }
    public static LayoutFile Parse(string text);
}
public sealed record KotorTexture(int Width, int Height, byte[] Rgba, bool HasAlpha, TxiInfo Txi); // Rgba : ligne 0 = haut de l'image
public static class DxtDecoder { public static byte[] DecodeDxt1(ReadOnlySpan<byte> d, int w, int h); public static byte[] DecodeDxt5(ReadOnlySpan<byte> d, int w, int h); }
public static class TpcDecoder { public static KotorTexture Decode(byte[] tpc); }   // premier niveau de mip seulement
public sealed record TxiInfo(string Blending /* "default" | "additive" | "punchthrough" */, bool Decal, string? EnvMapTexture,
    bool IsAnimated, IReadOnlyDictionary<string, string> Raw)
{ public static TxiInfo Empty { get; } public static TxiInfo Parse(string? text); }
public enum TextureKind { Diffuse, Lightmap }
public static class TextureLookup
{
    public static KotorTexture? Find(ResourceResolver res, string name, TextureKind kind); // null si introuvable
}
```
**Ce qui est fixé :**
- **LYT** : texte. Lignes `roomcount N` suivies de N lignes `<model> <x> <y> <z>`, puis `trackcount`, `obstaclecount` et `doorhookcount` (`<name> <room> <x> <y> <z> <qw?> <qx> <qy> <qz>…` : vérifier l'ordre dans les références).
  - Les sections `track` et `obstacle` sont lues et ignorées.
  - Les nombres sont lus en culture invariante.
- **TPC** : en-tête de 128 octets : `dataSize` u32 @0 (0 = non compressé), `alphaTest` f32 @4, `width` u16 @8, `height` u16 @10, `encoding` u8 @12, `mipCount` u8 @13. Les données commencent @128.
  - `encoding` 1 : gris 8 bits.
  - `encoding` 2 : RGB si non compressé, DXT1 si `dataSize > 0`.
  - `encoding` 4 : RGBA si non compressé, DXT5 si compressé.
  - Le texte TXI suit toutes les mips, jusqu'à la fin du fichier.
  - Une texture cubique (`height == 6 * width`) est décodée face par face, et on garde la première face. `TxiInfo.Raw` garde `"cube"`.
  - Le sens vertical (haut/bas) est à vérifier dans xoreos `tpc.cpp`. Le contrat est « ligne 0 = haut ». Le contrôle visuel final a lieu aux étapes 06 et 09.
- **Ordre de `TextureLookup`** :
  - Diffuse : `Tpc` d'abord (le résolveur passe déjà par `swpc_tex_tpa.erf`), puis `Tga` et `Txi` à côté.
  - Lightmap : `Tga` (`lightmaps*.bif`) d'abord, puis `Tpc`.
- `TxiInfo.IsAnimated` est vrai si `proceduretype` est présent (`cycle`, `arturo`, `water`…), ou si `numx`/`numy` ou `defaultwidth` sont présents. La texture est quand même exportée immobile (spec §6). C'est l'appelant qui journalise.

- [ ] **Étape 1 : tests qui échouent**
  - `Lyt_parses_rooms_and_doorhooks` : un texte LYT de 2 pièces et 1 doorhook donne `Rooms.Count == 2`, positions exactes et noms en minuscules.
  - `Dxt1_decodes_known_block` : un bloc 4×4 aux couleurs connues (c0 = rouge pur 0xF800, c1 = bleu pur 0x001F, indices tous à 0) donne 16 pixels rouges opaques.
  - `Dxt5_decodes_alpha_block` : un bloc avec alpha0 = 255 et alpha1 = 0, indices tous à 1, donne un alpha de 0 partout.
  - `Tpc_uncompressed_rgba_roundtrip` : un TPC synthétique 2×2 RGBA, suivi d'un TXI `blending additive`, donne les bons pixels et `Txi.Blending == "additive"`.
  - `Txi_detects_animation` : `"proceduretype cycle\nnumx 4"` donne `IsAnimated`.
  - `Lookup_prefers_tpc_over_tga_for_diffuse` : dans un faux dossier KOTOR, la même texture existe en TGA dans `Override\` et en TPC dans un ERF `TexturePacks\swpc_tex_tpa.erf`. En diffuse, c'est le TPC qui est renvoyé ; en lightmap, c'est le TGA. Le nom demandé en majuscules (`"LTA_X"`) est trouvé.
  - `[SkippableFact] Real_lyt_tar_m02aa` : `m02aa.lyt` contient au moins 10 pièces, dont `m02aa_01a`.
  - `[SkippableFact] Real_lightmap_and_diffuse_decode` :
    - `m02aa_01a_lm0` (lightmap) est décodé, avec une largeur et une hauteur puissances de 2 ;
    - la texture diffuse `lta_cli01` (présente dans `swpc_tex_tpa.erf`, vérifié) est décodée.
    - Le test écrit les deux en PNG dans `out/test-textures/` (avec `PngWriter`) pour l'œil du relecteur.
- [ ] **Étape 2 : lancer les tests.** Attendu : échec.
- [ ] **Étape 3 : implémenter.**
- [ ] **Étape 4 : `dotnet test Kotor2Ja.sln`** tout vert. Ouvrir les PNG produits et noter dans le rapport s'ils sont à l'endroit. On peut se baser sur une texture avec du texte ou un motif orienté.
- [ ] **Étape 5 : rapport** `reports/step-04-impl.md`.

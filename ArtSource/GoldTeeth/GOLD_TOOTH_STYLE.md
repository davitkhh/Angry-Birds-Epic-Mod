# Gold tooth style

How gold teeth are painted on pig mouth sprites in this project. Use this whenever the user asks for a "golden tooth" (or gold on any small rounded part of a body sprite). The user has approved this look and rejected others, so match it exactly. Don't improvise a new gold.

- Implementation: [gold_tooth.py](gold_tooth.py) (library `gild()` + CLI). **Run it, don't re-derive it.**
- Visual reference: [style_reference.png](style_reference.png). From left: Moustache pig Scream tooth (large), Moustache Angry lower tooth (medium), Prince Porky Scream tooth (tiny, green line art).

## The look

The game's style is flat cel shading, a dark outline and one soft light source from the top-left. A gold tooth keeps the original tooth's exact silhouette and anti-aliasing and only changes its colour:

| Part | Colour | Where |
|---|---|---|
| Lit gold | `#FFE254` (255,226,84) | top-left |
| Body gold | `#FAB61E` (250,182,30) | middle |
| Shade gold | `#C4700A` (196,112,10) | bottom-right, plus a darker band on the right ~22% (rim) |
| Shine stripe | `#FFFCD2` (255,252,210) | vertical stripe at ~28% across, fading out over the bottom ~45% |
| Glint | white, small plus/star shape | ~30% across, 3 px down; **only on teeth ≥ 9×9 px** |
| Edge fade | warm dark brown (70,24,8) | anti-aliased rim, on the usual dark-red mouth interior |
| Edge fade (other line art) | the outline's own mean colour | when the surrounding outline is not red-brown (e.g. Prince Porky's dark-green line art), so the line art stays intact |

Shading direction: `k = 0.55·u + 0.45·v` over the tooth's bounding box (u = left→right, v = top→bottom). Light→body over k 0–0.62, body→shade over 0.62–1.

Each pixel's blend from edge colour to gold uses how pale that pixel was in the **clean** (unrecoloured) tooth: `t = (lum − lum(48,14,14)) / (235 − lum(48,14,14))`. That keeps the original anti-aliasing.

It has to read as clearly **more saturated and orange** than the moustache pirates' dirty-yellow teeth (`#FFF492`). A paler gold just looks like another dirty tooth.

## What was rejected (don't do this)

- Sampling a colour ramp **column by column** from an existing gold tooth and stretching it across another tooth. The other session did this on 2026-11-11 and the user said it "wasn't accurate to [the] style." It loses the diagonal light, the shine stripe and the glint, and gives smeared orange edges.
- Tinting the 1px dilated ring around the tooth: the dark outline/interior must stay as it was.
- Brown edge fade on green line art (Porky): it draws a brown halo through his outline.

## Workflow

1. **Find the tooth.** Users point at teeth with zoomed GIMP screenshots (≈2–4×, checkerboard background, sprite label at the top-left). Match them by content to the sprite in `Assets/content/4_Characters/2_Pigs/<n>_<Rig>/BodyParts/`. "Top-left", "bottom-left", etc. are in the sprite's own orientation. For "the same tooth in all sprites", pick the analogous tooth in each expression. Skip sprites that don't show that row of teeth and say so (e.g. Moustache `Mouth_Open` has no upper teeth).
2. **Choose a clean source.** For an `XPirate.png` variant, pass the base `X.png` as `--clean`. Same size and alignment, teeth still white. That works even if the variant's tooth was already recoloured yellow or gilded badly. Without `--clean`, the target itself must still have the pale tooth.
3. **Back up** the target PNGs **outside** the scratchpad (it gets wiped between sessions) and keep the rig folder in the backup path. `MoustachePig` and `PrincePorky` share filenames like `Mouth_AngryPirate.png`, so a flat backup dir silently overwrites one with the other.
4. **Gild**, with x y = any pixel inside the tooth's pale body:
   ```
   python3 -B ArtSource/GoldTeeth/gold_tooth.py <target.png> <x> <y> --clean <base.png>
   ```
   For a non-destructive preview, add `--out <scratch.png>`, or call `gild()` from Python and paste into an edit sheet (see below).
5. **Check it** at ~12× nearest-neighbour zoom next to an existing gold tooth. Also confirm only the tooth area changed and **alpha is untouched**. Compare RGB and alpha separately: `ImageChops.difference(a, b).getbbox()` on RGBA images looks only at alpha, so it reports "no change" for pure colour edits.
6. **Ship it** (only if the user wants it in-game; "just show me" means preview files only):
   - Rebuild the swap atlas: `python3 EditSheets/variant_atlas.py <Rig> <Rig>_Atlas_03 <BodyPartsDir> Pirate`, then verify each atlas slot crop equals its PNG. Owned atlases: MoustachePig_Atlas_03 and PrincePorky_Atlas_03. PigBrute_Atlas_03 is shared, so paste in place there.
   - Edit sheet: paste the updated sprites into `EditSheets/<sheet>.png` **and** `.<sheet>_original.png` at the manifest rects (first check that the user hasn't painted on the sheet: `cmp` the two), so the next diff shows only the user's edits.
   - Unity open: `touch Temp/EquipmentPresetRefresh.request`, then check `~/.config/unity3d/Editor.log` for `Start importing` lines for the PNGs and atlas textures.
7. **Paste-ready deliverables** (when the user wants to paint them in themselves): a full-sheet-size transparent layer holding only the gold-tooth pixels at sheet coordinates ("Open as Layers" at 0,0, then merge down), plus per-sprite full PNGs with their paste coordinates.

## Gold teeth currently in the game (pirate variants)

- **MoustachePig** (`9_MoustachePig/BodyParts`):
  - `Mouth_ScreamPirate`: top-right upper (≈53–66, 11–28), bottom-left lower (≈36–48, 53–64), lower tooth under the tongue tip (seed 85,54)
  - `Mouth_AngryPirate`: rightmost upper (≈98–110, 15–25), bottom-left lower (≈22–31, 36–40), lower front tooth (seed 80,38)
  - `Mouth_OpenPirate`: bottom-left (≈23–29, 23–26), first lower tooth under the tongue (seed 61,30)
- **PrincePorky** (`4_PrincePorky/BodyParts`): the tooth right of the top-left one, in all 5 pirate mouths. Seeds: Angry 24,3 · Defeat 9,7 · Laughing 9,12 · Scream 9,7 · Shriek 10,5.

The last tooth in each Moustache mouth and all of Porky's were redone with `gold_tooth.py` on 2026-11-11. The others are the user's GIMP pastes of the same recipe.

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

## Tusks and engravings (`gild_tusk`, `engrave_dollar`)

Use these for long curved horns like the Brute's tusks. They also suit anything with painted-in shading stripes, which `gild` would leave un-gilded because its tooth test skips mid-tone olive pixels.

- **`gild_tusk(img, seed, clean)`**: same palette and light as above, with three differences:
  - The mask is the whole tusk inside its dark outline, olive stripes included.
  - The tusk's own shading is kept: pixel luminance relative to the pale tusk (235) pulls toward `G_DARK`. A tusk drawn in shadow (the Laughing back tusk) comes out as dim gold.
  - The diagonal light, right-rim shade and shine stripe are measured **per row**, so they follow the horn's curve.
- **`engrave_dollar(img, inner)`**: a $ with **two vertical strokes**. It's a recessed groove (122,52,0) with a lit lower-right wall (255,236,150), since light comes from the top-left.
  - It **bends with the tusk** (user request 2026-11-12). The glyph is drawn upright at high resolution, then sampled through `tusk_frame()`:
    - The tusk is sliced **across its PCA long axis** and one **quadratic arc** is fitted through the slice midpoints. Pixel-row midpoints were rejected: on leaning tusks they snake into an S and bend the $ the wrong way. The user caught that.
    - The glyph's vertical axis runs along that arc, top toward the tip (the narrow end).
    - Its horizontal axis is the perpendicular offset from the arc. It's rotated onto the tusk, never mirrored.
    - Its width follows a straight fit of the tusk's half-width (`fill` 0.72 of it), applied at half strength.
  - Don't go back to stamping a rotated, flat glyph.
  - Things that made it read as "8" or "B", now avoided:
    - the full taper (it bloats the lower bowl);
    - S ends that curl back toward the middle (they now run from -55° to 125°, stroke 0.085 of the height);
    - measuring along rows instead of the centre line (it shears the glyph on leaning tusks).
  - Placement: the inscribed-circle centre, moved 6% of the glyph height toward the tip (`lift`), because the lip often covers the tusk's base. Height is about 2.6 × the inscribed radius, min 11 px.
  - Below about 10 px across, it only reads as a $ shape.
- **Every pixel of the tusk must be gilded** (the user caught un-gilded pale pixels in the transparent exports, 2026-11-12).
  - `tusk_mask` lets very pale pixels lean up to 25 toward green, because the lip tints them; for example (239,255,151) is tusk body.
  - Edge pixels where the tusk meets the **green lip** (not the dark outline) are included through `lip_blend`. That projects the pixel onto the line from its outside neighbour's colour to the pale tusk colour. `gild_tusk` then paints `lerp(neighbour, gold, fraction)` instead of the brown fade.
  - A second pass picks up slivers that run under the lip and only touch the rim.
  - Check after any mask change: count pixels within 1 px outside the mask with luminance > 110 and red > 120. It must be 0 for every tusk.
- **Broken ("defeated") tusks get a ¢, not a $** (user request 2026-11-12), and it must **not go over the flat cut face**.
  - `tusk_cap(img, inner, seed)` floods the dark olive cut-face ellipse (luminance < 160) from a seed on it.
  - Exclude it plus a 2 px margin and pass the rest as `region` to `engrave(..., cent_glyph(), region=side, up=cap_centre, axis=side_centre - cap_centre)`. The glyph is sized, placed and clipped inside the side only.
  - The `axis` override matters: a stump's visible side is wider than it is long, so the default long-axis estimate would lay the ¢ sideways across the stump.
  - `cent_glyph()` is an open c with one vertical stroke running past it.
  - Only `Mouth_KO` has a broken tusk (stump seed 113,62; cut-face seed 112,56). Verified: 0 engraved pixels on the face or its margin.
- **Brute tusk seeds** (pirate mouths, `3_PigBrute/BodyParts`): Mouth 14,30 + 132,45 · Damaged 152,55 · KO 113,62 (broken stump) · Laughing 198,48 + 129,61 (back tusk in shadow) · Scream 156,45 · Surprised 15,30 + 130,55.
- Previews and paste-ready files (2026-11-12, **not in the game yet**) are in `out/PigBrute_tusks_plain/` and `out/PigBrute_tusks_dollar/`.

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
- **PigPirate** (`6_PigPirate/BodyParts`, swap atlas `PigPirate_Atlas_04`, added 2026-11-12): the bottom-right tooth, meaning the rightmost tooth on the lower jaw (often on the jaw's right-hand curve rather than in the flat bottom row). Seeds: Mouth 79,23 · Angry 67,39 · Attack 126,55 · Happy 67,57 · Scared 66,66 · WideOpen 87,70. `Mouth_SadPirate` shows only upper teeth, so it has none. The base `X.png` teeth are white, so use them as `--clean`. When rebuilding Atlas_04, **pass the 7 mouth names explicitly**: the folder also has `Headgear_Pirate.png`, `MainHand_AirPirate.png` etc. (gear, not variants), which the default run would pack in.
- The Brute's regular teeth all hang from the upper lip, and his only lower teeth are the tusks. A "bottom" tooth on the Brute means a tusk, so ask before gilding one.

The last tooth in each Moustache mouth and all of Porky's were redone with `gold_tooth.py` on 2026-11-11. The others are the user's GIMP pastes of the same recipe.

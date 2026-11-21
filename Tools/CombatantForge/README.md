# Combatant Forge

Builds a new combatant on any bird or pig rig, then uploads it into the game.

    python3 Tools/CombatantForge/forge.py        # opens http://127.0.0.1:8765/

Needs Python 3 with Pillow. Projects are saved in `projects/<id>.json`.

## What an upload writes

The combatant has its own inventory and no prefab of its own. For id `x`:

| What | Where |
| --- | --- |
| Baked art, one texture per slot | `Assets/content/4_Characters/_Forge/Baked/x/` |
| Swap-only atlas | `Forge_x_Atlas(_low)` in `MonoBehaviour`, `Material`, `Texture2D` |
| Atlas reference | one line in the base rig's prefab and `_low` prefab |
| Combatant and inventory | `pig_forge_x`, `pig_forge_x_01` |
| Items | `pig_forge_x_mainhand`, `_offhand`, `_headgear` |
| Changed or new abilities | `pig_<type>_forge_x_<name>`, with loca |

Art added through the library goes to `Assets/content/4_Characters/_Forge/<Birds|Pigs>/<Category>/`.
Any PNG placed there by hand also shows up in the library.

## How it works in the game

- Texture names are the contract: the class item's asset is `Forge_x`, and the other textures are `Forge_x_Back`,
  `Forge_x_MainHand`, `Forge_x_OffHand` and `Forge_x__<Bone>`.
- `CharacterAssetController.SetForgeFeatures` swaps `Forge_x__<Bone>` onto non-equipment bones such as Snout.
- The pirate variant uses `Forge_x_Pirate`, which triggers the existing `SetPirateVariants`. Ids stay lowercase so no
  other combatant triggers it by accident.
- `SkillBlind` reads a custom strip colour from the skill parameter `ui_color_rgb` (0xRRGGBB).
- An existing ability that isn't edited is referenced as is. An edited one becomes this combatant's own copy.
- Transforms are baked into the texture and pivot (`publish.bake_sprite`), so slots can hold any position, rotation,
  scale, skew or flip. Draw order comes from the rig's bone depths.

## Existing combatants (View existing)

`existing.py` opens any `PigBalancingData` row whose base the Forge can draw, and writes edits back into that row.
They appear in the library's Combatants tab as tiles drawn with their own equipment (`/pigicon/<NameId>`, `gamedata.rig_icon` with slots).

- Variants are pigs whose NameId extends another's (`pig_x_bird3` under `pig_x`). Updating a pig also updates its
  ticked variants (ticks: `projects/existing/_unticked.json`): changed art, abilities and settings are copied,
  Health, Attack and Scale scale by the same factor, and a new name reaches variants that had the same name.
- Rows other pigs share are never edited in place. The receivers get copies named after `<NameId>__e<rev>`
  (inventory `pig_forge_<NameId>__e<rev>_inv<n>`, items `..._mainhand` etc., name `..._name<n>`, abilities
  `pig_<type>_forge_<NameId>__e<rev>_<name>`). The revision goes up when a pig outside the update still uses the
  current copies, for example a variant that was unticked after it got them, so it keeps them.
- Edits are saved in `projects/existing/<NameId>.json` with a fingerprint of the game rows they were made on. If the
  game changed since, the pig opens fresh from the game.
- The game's turn plan (`SkillCombos`) is kept with renamed abilities swapped in; adding an ability replaces it with
  the Forge's plan. Duplicate makes a new Forge combatant from a pig and keeps its plan the same way.

## Animation and projectile per ability

- The preview steps each bone through its clip's SmoothMoves trigger frames (`TRIGGER_EVENT_TYPE`: 1 hide,
  3 depth, 4 texture, 5 pivot), so mouths, lids and so on change as in the game. Each clip lists its user events
  (Movement, Impact, Projectile).
- Attacks also choose their reach (`anim_ranged`: 1 ranged, 0 melee, absent = the main hand item decides). These options
  live on each ability, not on the main hand. The library's Projectiles category lists every prefab in
  `4_Projectiles` (`/projectileicon/<name>`); drag one onto an attack to make it fire it.
- An ability can name its clip and an attack its projectile. They are stored as skill parameters `anim_clip` and
  `anim_projectile`: a 24-bit FNV-1a hash of the name (`publish.clip_hash`, `ForgeSkillAnimation.Hash`). The game
  plays the clip in `CharacterControllerBattleGroundBase.PlaySkillClip`, and a projectile makes the attack ranged
  (`AttackSkillTemplate`), fired on the clip's Projectile event or halfway through it.

## Encoding

- Unity open: each JSON is written in place, then the tool waits for the watcher's `Reimported` log line.
- Unity closed: the tool encodes in batch mode and restores the other languages' loca `.bytes`.

## Eye beams

- A beams layer (`kind: 'beams'`, `beams.py`) sits in a slot's layer stack but is never baked. It follows an art layer
  (eyes are in that art's pixels, from its centre, y up) or has its own placement, and it lists the abilities that turn
  it on (every support / every attack, or ticked ones). Library: Features → Eye_Beams (drop it on a slot or a layer row).
- Art with the game's own beams (the basic Stoneguard helmet) brings a beams layer along, from the "builtin" entries
  of `Assets/Resources/HelmetBeams.json`.
- Upload writes the slot's beams under its texture name into that file (kinds as `Support`/`Attack`, ticked abilities
  as `skill:<NameId>`), plus `Resources/ForgeBeams/*_cover.png` for the slot's layers above the beams.
  The game side is `HelmetBeams.cs`, hooked into `PlaySupport/Attack/RageSkillAnimation`.

## Bells

- A bells layer (`kind: 'bells'`, `bells.py`) is a ring with a big and a small brass bell hanging off the art layer it
  follows (ring centre in that art's pixels) or its own placement; any number per slot (each its own set in the game,
  later sets drawn in front), never baked. On the Slots tab, drag a set by its ring or a bell to move it. Its panel sets the ring
  (whole, the Dark Cupid handle-cut ring, none), "Place" on the canvas, "Game spot" on art with a preset, and what rings
  it out: "any knock (always)", every attack / support / rage, or ticked abilities (those also jolt the bells as their
  clip starts). Library: Features → Bells. Hide/Show on the layer row hides them.
- Each bell's metal (Big bell / Small bell: Brass, Silver (sterling), Bronze (bell metal); layer `materials`) changes
  its sprite and, from the config's `materials` table (E, density, decay against the recording's brass): pitch
  ×sqrt((E/ρ)/(E/ρ)brass), ring length (the strike's decay e^-t/τ reshaped to τ·decay), mass (swing damping, knocks,
  loudness). Silver ≈ ×0.754 pitch, ×0.75 ring, ×1.21 mass; bronze ≈ ×0.959, ×1.35, ×1.03.
  Surface per metal: `roughness` (art: polished cel bands + glint vs bronze's as-cast matte gradients and pitting;
  physics: hanger-loop friction on the ring), `patina` (art: tarnish in the recesses), `restitution` (clapper and
  bell-on-bell bounce, from hardness: bronze 0.45, brass 0.35, sterling 0.28).
- The preview runs ScytheBells.cs's physics (pendulums on the ring, clappers, bell-on-bell knocks) and plays each bell's
  strike from `bell_ring.wav` through WebAudio (`/bellsound`, re-encoded to 16-bit), pitch 1/size, volume from impact
  speed, at the physics step's time. "Sound" in the panel turns it off (remembered per browser).
- Upload writes the slot's bells into `Assets/Resources/ScytheBellsConfig.json` "weapons" (matched by texture; `on`
  tokens `Always`/`Attack`/`Support`/`Rage`/`skill:<NameId>`), plus `Resources/ForgeBells/*_bells_cover.png` for the
  slot's layers above the bells, which hide the bells only where they overlap them (game: stencil shaders in
  `Resources/ScytheBellsShaders`; preview: the set is drawn on its own canvas and the cover drawn `source-atop`); a set
  whose slot is behind the body draws behind the character, as ScytheBells.BehindBody does. "builtin" entries (the belled Dark Cupid item) and "presets" come from
  `ArtSource/ScytheBells/bells.py`, which keeps Forge entries when it regenerates the file.

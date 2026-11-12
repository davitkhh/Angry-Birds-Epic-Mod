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

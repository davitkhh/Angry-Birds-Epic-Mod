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

## Encoding

- Unity open: each JSON is written in place, then the tool waits for the watcher's `Reimported` log line.
- Unity closed: the tool encodes in batch mode and restores the other languages' loca `.bytes`.

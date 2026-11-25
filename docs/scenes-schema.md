# Soundstage Scene file format (v2, 21/11/2026)

A Scene is one saved look: lights with roles, palettes that recolour those roles, and optional blocks for the set, the
atmosphere, the camera, the look and the pose. This replaces the old preset format (v1, `presets-schema.md` before part 4).
Old files still load: see "Old files" below.

## Where the files are

- **Shipped:** `src/Soundstage/scenes.json`, copied beside `Soundstage.dll` on build. It is written by
  `tools/gen_scenes.py` and never edited by hand. Change a parameter in the generator and run
  `python tools/gen_scenes.py`.
- **Yours:** `user-scenes.json` in the plugin config folder (the Soundstage one), same format, shown under **Mine**.
- **Old:** `user-presets.json` in the same folder is read once if there is no `user-scenes.json` yet, converted, and
  written to `user-scenes.json`. The old file is never changed or deleted.

## Positions

All positions are relative to the player when the Scene is applied, in metres: the stage sits at the player's feet,
turned to the player's facing.

- **azimuthDeg**: 0 = straight in front of the player, +90 = player's left, -90 = player's right, 180 = behind.
- **distance**: horizontal distance from the player's centre.
- **height**: height above the player's feet.
- **aimAt**: `"face"` places the light relative to the head instead of a fixed height (part 8). The light keeps its azimuth, distance
  and elevation (height minus aimHeight, or minus 1.55 when there is no aimHeight), measured from the face, and a Spot or Flat aims at
  the face. If the head cannot be read the light is placed as authored.
- **aimHeight**: for Spot and Flat lights, the light is aimed at the point on the player's vertical axis at this
  height. Null means aim level, straight at the axis at the light's own height.

Stagehand units (from `docs/stagehand-notes.md`): colour channels 0 to 1; intensity is not capped at 1, and the editor
default is 4; range in metres; spot `coneDeg` is the full cone angle (editor default 30) and `angularFalloffDeg` its soft
edge (default 5).

## The file

```json
{
  "formatVersion": 2,
  "scenes": [
    {
      "id": "neon-split",
      "name": "Neon split",
      "category": "Neon",
      "description": "One or two plain sentences on the look.",
      "bestIn": "Dark interior or night, close portrait.",
      "rig": "split neon",
      "tags": ["neon", "portrait"],
      "legacyIds": { "neon-split-blue-red": "", "neon-orange-teal-split": "Orange and teal" },
      "defaultPalette": "Blue and red",
      "palettes": {
        "Blue and red":    { "key": [0.1, 0.25, 1.0], "key2": [1.0, 0.05, 0.15], "fill": [0.05, 0.1, 0.6] },
        "Orange and teal": { "key": [1.0, 0.45, 0.05], "key2": [0.0, 0.85, 0.7],  "fill": [0.0, 0.5, 0.45] }
      },
      "references": [2, 11],
      "lights": [ { "name": "Key", "role": "key", "shape": "Spot", "color": [0.1, 0.25, 1.0], "intensity": 8.0,
                    "range": 20.2, "falloff": "Quadratic", "falloffFactor": 1.0, "coneDeg": 45.0,
                    "angularFalloffDeg": 12.0, "azimuthDeg": -65, "distance": 1.6, "height": 1.7, "aimHeight": 1.55,
                    "characterShadows": true, "objectShadows": false, "specular": true } ],
      "backdrop": null,
      "set": null, "atmosphere": null, "camera": null, "look": null, "pose": null, "animation": null
    }
  ]
}
```

- `shape`: `Point`, `Spot` or `Flat` (no `Ambient`: it only works outdoors).
- `falloff`: `Linear`, `Quadratic` or `Cubic`.
- At most **6** lights per Scene, not counting `backdrop.light`. (It was 4, Keylight's own number. Six is to be confirmed
  in game with the six-light test in the Tools tab.)

### Roles and palettes

Each light has a **role**: `key`, `key2`, `fill`, `rim`, `rim2`, `kicker`, `bg` and so on. A role is unique inside a
Scene. `palettes` maps a palette name to role-to-colour, and a palette can also set `backdrop` (the backdrop colour).
Applying a palette recolours the lights with those roles. A role the palette does not list keeps the light's own colour.

- `defaultPalette` names the palette whose colours the lights carry. If empty, the first palette is the default.
- When a palette other than the default is applied, each recoloured light's `range` is recomputed by the brightness
  rule below, because the brightest colour channel sets the range.
- Saving a Scene writes the colours you see into its default palette, so a saved Scene reloads as you left it.

### The brightness rule

The game sets a light's range to where it falls to about 2 percent (measured on nine lights, 18/11/2026):

    max(colour) x intensity / (falloffFactor x range^n) = 0.0196        (n = 1, 2, 3 for Linear, Quadratic, Cubic)

Every range in `scenes.json` comes from this, through `game_range` in the generator, and the self-check recomputes it
from the file for every light and every palette (within 0.05).

### `legacyIds`

Old preset ids that now mean this Scene. The value is the palette that old id selected, or an empty string for the
default. A saved selection such as `soft-window` therefore resolves to Window light. Eleven old presets became nine Scenes:

| Old id | Scene | Palette |
|---|---|---|
| `neon-split-blue-red` | Neon split | Blue and red |
| `neon-orange-teal-split` | Neon split | Orange and teal |
| `neon-magenta-on-blue` | Neon wall, hard | Magenta on blue |
| `neon-pink-on-lavender` | Neon wall, soft | Pink and lavender |
| `neon-orange-on-cyan` | Neon wall, soft | Orange and cyan |
| `neon-green-rim` | Rim only | Green neon |
| `soft-backlit-hair-glow` | Hair glow | Cool white |
| `harsh-half-face` | Split light | Warm white |
| `soft-window` | Window light | Daylight |
| `natural-golden-hour` | Golden hour | Golden |
| `soft-white-high-key` | High key | White |

### `backdrop`

Null means no backdrop. When present it places the dyeable white rectangular partition
`bgcommon/hou/indoor/general/0766/bgparts/fun_b0_m0766.mdl` behind the player, facing them, dyed `color`, scaled by `scale`,
its base at the player's feet. `backdrop.light` is an optional extra light, same format as `lights` with the role `bg`, for
lighting the backdrop itself. Part 9 replaces the partition with a seamless sweep, described by `set`.

### Optional blocks

All optional, all data only in part 4. A build that cannot apply a block logs one line and ignores it, so a Scene file
never breaks because a feature is missing. A property this build has never heard of is ignored the same way, with one log
line per name per file.

| Block | Applied by | Fields |
|---|---|---|
| `set` | part 9 | `kind` (`none`, `partition`, `sweep`), `colorRole` or `color`, `sweepWidth` (4 to 16 m, default 8), `sweepDistance` (0 = use the backdrop's, else 2.5), `sweepCove` (default 1), `props` (kit ids from `kits/*.json`), `kitPlacements` (kit id to `x`, `z`, `yawDeg`), `picture` (`path`, `height`, `x`, `z`, `centreHeight`, `yawDeg`), `notes` |
| `atmosphere` | part 9 | `haze` (`none`, `light`, `heavy`), `dust`, `sun`, `items` (a list of `id`, `enabled`, `amount` naming entries of `atmosphere.json`; an entry overrides the haze, dust and sun flags for that id), `notes` |
| `camera` | part 10 | `framing` (`full-body`, `three-quarter`, `waist-up`, `chest-up`, `portrait`, `close-up`; the older words `close-portrait`, `three-quarter-waist-up` and `side-on` still work), `height` (`eye`, `chest`, `low`), `side`, optional `azimuthDeg` (overrides `side`; positive is the subject's right), `offsetY` (metres), `fovAdd`, `notes` |
| `look` | part 11 | `slot`: a mood name such as `neon-night`. Each user maps slots to their own ReShade presets. `autoFocus`: `true` turns auto focus on while the Scene is applied; left out means off. |
| `pose` | parts 7, 14 | `tags` (suggestions for the Poses shelf), `poseFile`, `outfit` |
| `animation` | part 8 | `effects`: `target` (a role, or `bg`), `kind` (`pulse`, `drift`, `flicker`, `strobe`), `amount`, `rateHz`, `enabled`, `property` (`intensity`, or `hue` for a pulse), `seed` (optional) |

Animation effects that pulse or flicker ship switched off in the first fifteen Scenes. Slow `drift` ships on, at about 8 percent.
The three animated Scenes (Club strobe, Campfire flicker, Neon sign flicker) are the exception: the flicker ships on, every strobe
effect ships off. A strobe never flashes faster than 3 Hz whatever `rateHz` says. Animation changes intensity and colour only; the
range stays where the brightness rule put it. Without a `seed` the noise seed is made from the Scene id, target and kind, so a
Scene flickers the same way on every machine.

## Old files

A v1 file (`formatVersion` 1, a `presets` array) loads as v2. Each preset's lights get roles by order (`key`, `fill`,
`rim`, then `kicker`, `kicker2`, `kicker3`), the backdrop light gets `bg`, and the lights' own colours become one palette
named `Default`. Nothing else changes, so the v1 field names are the v2 field names.

## Catalogue

Fifteen Scenes in `scenes.json`: Neon split, Neon wall (hard), Neon wall (soft), Rim only, Hair glow, Split light, Window
light, Golden hour and High key (refined from the old presets), then Rembrandt, Butterfly, Loop, Clamshell, Silhouette and
Low key (new rigs; starting values, tuned in game). Part 8 adds three animated Scenes.

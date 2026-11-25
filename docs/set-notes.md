# Set tab notes (part 9)

What was measured from the game files, what the Set tab builds from it, and what still needs a run in game. Everything under "Measured" came from `tools/measure_mdl.py`, which reads the installed game read-only and prints numbers (run with `python tools\measure_mdl.py 766 204 --meshes`). Sizes are metres at scale 1, from the vertex positions of LOD 0.

## Measured: the partition (9.1)

`fun_b0_m0766`, the white dyeable partition the old backdrop used. The README said its size and pivot were unknown. They are not any more.

| Measure | Value |
|---|---|
| width (X) | 4.0 metres, from -2.0 to +2.0 |
| height (Y) | 3.0 metres, from 0.0 to 3.0 |
| thickness (Z) | 0.2258 metres, from -0.1129 to +0.1129 |
| pivot | the middle of the bottom edge: x and z centred, y at the foot |
| geometry | one mesh, 42 vertices, one material (the dyeable shader) |

So the old panel at scale 1 stood 4 m wide by 3 m tall with its foot on the ground, and a Scene's `scale` multiplied those. That is why a scale of 1.2 to 1.8 was needed to fill a frame.

Another partition measured for comparison: `0502` is 4.0 wide by 6.5 high by 0.77 thick, pivot at the foot, two meshes (140 vertices).

## Measured: the picture canvas (9.5, 9.6)

`fun_b0_m0204`, the portrait of the Admiral.

| Mesh | Vertices | What it is | Box (x, y, z) |
|---|---|---|---|
| 0 | 4 | the picture quad | x -0.6348 to 0.6348, y -0.9751 to 1.0664, z 0.0794 |
| 1 | 3860 | the frame | x -0.7812 to 0.7812, y -1.4395 to 1.6826, z -0.0072 to 0.1375 |

So the quad is 1.2696 m wide by 2.0415 m high, its centre is 0.045 m above the pivot, and the pivot is the middle of the hanging point, not the foot. The quad sits at z 0.0794, in front of the frame's back, so its front is probably +Z in the model's own frame. The picture prop scales the quad to the image's shape and lifts the pivot so the picture's centre is at the height asked for.

## Measured: the furniture used by the kits (9.3)

`top_bias_z` is how far the top third of the model sits from the middle of its box along Z. For something with a back (a chair, a couch) a negative number means the tall part is at -Z, so its open front is probably at +Z. It is a guess at facing, and the in-game check settles it.

| Key | Name | Width | Height | Depth | top_bias_z |
|---|---|---|---|---|---|
| 0158 | riviera couch | 2.05 | 1.51 | 0.99 | -0.42 |
| 0155 | riviera armchair | 1.21 | 1.28 | 0.99 | -0.40 |
| 0077 | manor high-back chair | 0.90 | 2.11 | 0.74 | -0.28 |
| 0303 | riviera stool | 0.53 | 0.50 | 0.53 | 0 |
| 0146 | riviera floor lamp | 0.34 | 1.44 | 0.49 | n/a |
| 0139 | riviera rug | 3.80 | 0.03 | 5.96 | n/a |
| 0080 | manor bookshelf | 2.55 | 4.02 | 0.84 | +0.19 |
| 0073 | manor fireplace | 1.88 | 1.62 | 0.89 | +0.20 |
| 0308 | shipping crate | 0.69 | 0.69 | 0.69 | n/a |
| 0309 | wine barrel | 0.87 | 1.07 | 0.87 | n/a |
| 0264 | potted umbrella fig | 1.28 | 2.50 | 1.16 | n/a |
| 0265 | potted dragon tree | 1.70 | 3.85 | 1.85 | n/a |
| 0150 | potted spider plant | 1.46 | 1.63 | 1.40 | n/a |
| 0151 | potted azalea | 0.82 | 1.11 | 0.84 | n/a |
| 0149 | potted axilflower | 0.82 | 0.80 | 0.72 | n/a |
| 0272 | the Emperor's throne | 3.57 | 6.04 | 3.34 | n/a |
| 0054 | oasis pillar | 1.24 | 6.51 | 1.24 | n/a |

All of them have their foot at y = 0 apart from the rug (a 3 cm slab at y 0.02 to 0.05). The bookshelf and fireplace lean the other way from the chairs, so the kits turn them 180 degrees. That is a guess too.

## What the sweep is (9.2)

Built from the partition's measured size, all dyed the Scene's backdrop colour, in the stage frame (+Z forward, origin at the subject's feet):

- **Wall**: pieces stood at z = -distance, foot at the top of the cove, repeated across for width and stacked for height.
- **Cove**: the same piece pitched 135 degrees, so its length runs from the wall's foot down and forward to the floor at 45 degrees, scaled to the cove size (default 1 m).
- **Floor**: the same piece pitched 90 degrees (laid flat), from the cove's foot to 2.5 m in front of the subject, in rows of at most 3 m.
- Every piece is thinned to a quarter of its depth (5.6 cm) and overlaps its neighbours by 1 cm so no hairline shows. The floor's top is 1.2 cm above the ground so it does not flicker against it.
- Width 8 m, distance 2.5 m, cove 1 m, wall height 3 m makes 8 pieces. The layout never goes over 40 pieces; if a request needs more, the floor's front edge pulls back.

The maths (positions, rotations, scales, joins between pieces) is checked offline in `SetChecks`. Whether it looks seamless from the camera is checklist step `set/1`.

Fallback if the pieces do not join: set `kind` to `"partition"` and the Scene's old backdrop block places the single panel again, now with a real size to scale from.

## Prop kits (9.3)

Seven kits ship in `kits/*.json`, each at most 12 objects (the stage size is not measured, so the cap is the guard): lounge, library, interview chairs, greenery, crates and barrels, fireside, throne room. Every model path was read back from the game files (17 of 17 found). All start as `untested` and the checklist (`set/3`) records which look right. Keep only the ones you confirm.

Candidates came from `data/ffxiv-canvas/find_canvas.py` over the housing furniture sheet, filtered by name.

## Atmosphere candidates (9.4)

Stagehand's definitions have two object types for this: `VfxObjectDefinition` (an `.avfx` path, a colour tint, position, scale) and `SoundObjectDefinition` (an `.scd` path, a sound index, volume, fade in, speed, positional or not). Candidates came from the `VFX` sheet and the `BGM` sheet, and each path was read back from the game files (all present). None has been looked at in game yet, so every row below is UNTESTED until you run checklist step `set/4`.

| Id | Kind | Path | Result |
|---|---|---|---|
| haze | vfx | vfx/common/eff/lcut_smoke0f.avfx | UNTESTED |
| light-shafts | vfx | vfx/common/eff/m0614_overray_lp_c0v.avfx | UNTESTED |
| sun | vfx | vfx/common/eff/m0330_cast_sun_c0k2.avfx | UNTESTED |
| dust | vfx | vfx/common/eff/foot_dust_a.avfx | UNTESTED (may play once, not loop) |
| leaves | vfx | vfx/common/eff/pop_leaf_sml_c0i2.avfx | UNTESTED |
| glow | vfx | vfx/common/eff/aura_bene0h.avfx | UNTESTED |
| room-night | sound | music/ffxiv/BGM_Field_Housing_Night.scd | UNTESTED |
| room-calm | sound | music/ffxiv/BGM_Town_Rest_01.scd | UNTESTED |
| room-bar | sound | music/ffxiv/BGM_Field_Bar.scd | UNTESTED |

The sounds are music files. No ambience-only `.scd` was found in the sheets read, so "ambient sound" is room music for now. An item that does not show or sound in game is dropped from `atmosphere.json` (its status goes to `dropped`).

## Picture canvas test (9.5) and picture prop (9.6)

The canvas test is the old Tools-tab button (Canvas test), now on the Set tab. The checklist walks it: texture swap shows, orientation correct, normal and specular acceptable. The picture prop reuses the same model and the same modpack route, with the image converted to an uncompressed B8G8R8A8 `.tex` (long side at most 1024 px) by `PictureProp`. If the canvas test fails in game, the picture control in the Set tab stays hidden and Scenes carry no picture.

Result: NOT RUN (your step, `set/6`).

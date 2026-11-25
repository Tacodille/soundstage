# Proof: head bone and GPose camera (Part 2)

Status: **built, not yet run in game.** Every in-game row below is a step in the plugin's test checklist
(`/soundstage test`, area "Part 2: head bone and GPose camera"). Your results land in `dalamud.log` as
`[Soundstage/TEST] gameaccess <step> ...` and the probe's own evidence as `[Soundstage/PROBE]`. Fill the table from those lines,
not from memory.

## 2.1 What the installed structs have

Measured on 21/11/2026 by reflection over `FFXIVClientStructs.dll` 7.57.0.9480 (the copy Dalamud 15.0.4.1 ships at
`%APPDATA%\XIVLauncher\addon\Hooks\dev\`). No signature and no hook is needed: everything below is a plain struct field or
method that exists in that build. The Probe tab's "List the types and fields this proof uses" button repeats this check live and
logs it (`[Soundstage/PROBE] TYPES`), so a patch that renames something shows up as a `NO`.

Camera (read and write):

| Type | Member | Offset | Use |
|---|---|---|---|
| `FFXIV.Client.Game.Control.CameraManager` | `Instance()`, `GetActiveCamera()` | | find the camera |
| `FFXIV.Client.Game.Camera` | `DirH` | 0x140 | horizontal orbit angle (yaw), radians, -pi to pi |
| | `DirV` | 0x144 | vertical orbit angle (pitch), clamped to `DirVMin` (0x158) and `DirVMax` (0x15C) |
| | `Distance` | 0x124 | orbit distance, clamped to `MinDistance` (0x128) and `MaxDistance` (0x12C) |
| | `FoV` | 0x130 | zoom FOV, carries outside GPose (not written by Soundstage) |
| | `CameraBase.SceneCamera.Object.Position` | 0x60 | lens position, read; the write is tested but the game recalculates it |
| | `CameraBase.SceneCamera.LookAtVector` | | look-at, read |
| (raw offset) | GPose FOV slider | **0x13C** | -0.5 to 0.5, resets on leaving GPose. **Not named in the installed `Camera` struct**; the offset comes from the reference plugin's layout and is exactly what the probe has to confirm |
| `FFXIV.Client.Game.GameMain` | `IsInGPose()` | | the GPose gate |

Head (route A):

`Control.Instance()->LocalPlayer` (`BattleChara`) -> `DrawObject` -> `CharacterBase.Skeleton` (`Render.Skeleton`) ->
`PartialSkeletons[i].GetHavokPose(0)` (`hkaPose`) -> `Skeleton->Bones[b].Name` matched to `j_kao` -> `ModelPose[b].Translation`,
scaled by the model scale, rotated by the model rotation and added to the model position. The skeleton is searched across all
partial skeletons because the head bone may sit in one other than the first. `j_kao` is the name Brio's own head filter uses
(`BrioBoneCategories`, "head" category), which is the only confirmation available without the game; the probe's `READ skeleton`
line records what each partial skeleton holds.

Head (route B, Brio): `GetPoseAsJson` returns Brio's `.pose` JSON (`docs/brio-notes.md`). Brio stores bone transforms
relative to the parent bone, so even if `j_kao` has a `Position` it is a local value, not a world position. `BrioHeadRoute.Inspect`
reports whether `j_kao` and a `Position` field are there; checklist step 4 records the answer.

Reference: "Lights, Camera, Action!" by NeNeppie (`github.com/NeNeppie/LightsCameraAction`, AGPL-3.0). Read only, to learn which
camera fields exist and that its author found the position cannot be externally edited because the game resets it to the
client's. Nothing was copied: Soundstage is MIT and the code here is written against the installed FFXIVClientStructs types. That
note is the reason the position write has its own check ("kept" or "put back") and the orbit, pitch and distance writes are the
route the later parts rely on.

## Results

Fill from `dalamud.log` after the checklist run. One row per "Done when" item.

| Item | Checklist step | PASS or FAIL | Value you saw | Notes |
|---|---|---|---|---|
| Head position read (route A) | 2, 3 | not run | | |
| Head route B (Brio JSON) | 4 | not run | | an answer either way counts |
| Camera position read | 5, 6 | not run | | |
| Camera position write | 10 | not run | | "kept" or "put back" |
| Camera yaw/pitch write | 7, 8 | not run | | |
| Camera FOV write | 9 | not run | | |
| Restore and refusal outside GPose | 11, 12 | not run | | |

Date, game patch, Dalamud version and FFXIVClientStructs version the proof ran on (these break on patches):
Dalamud 15.0.4.1 and FFXIVClientStructs 7.57.0.9480 are the build it was written against (21/11/2026). Game patch and run date:
to be filled in by you with the first results.

## What each FAIL shrinks

If a row fails, these are the fallbacks and the parts they affect.

- **Head read fails on both routes**: part 8's face aim uses the player position plus a per-body-height table (the existing
  `aimHeight` approach, which already works), and part 16 loses "light direction from face placement".
- **Camera write fails** (orbit, pitch, FOV all refused or ignored): part 10 builds the thirds overlay and a "suggested framing"
  text card only, with no turntable and no camera block applied; part 16's camera placement becomes advice text; part 12's
  contact sheet varies Scenes, not angles.
- **Only the position write is put back** (the likely answer going by the reference plugin): nothing is lost. Parts 8, 10 and 16
  place the camera by orbit yaw, pitch and distance about the subject, which `GposeCamera.TryWrite` already does.
- **Only the FOV write fails**: part 10 keeps framing by distance and drops FOV presets.
- **A failure that is only the 0x13C offset** (FOV slider reads garbage or nothing moves): the probe's raw read of that offset
  will show it; fix the offset in `ClientStructsReader.AddedFovOffset`, which is one constant.

## Safety built in

- Every game-memory access is wrapped by `GuardedAccess`: an exception becomes an "unavailable" answer with a reason, each
  distinct failure is logged once per session, and camera calls refuse outside GPose (nothing is touched).
- The first write remembers the camera as it was; Restore puts yaw, pitch, distance, the GPose FOV slider and position back.
  Leaving GPose drops the remembered state because the game resets its own camera then.
- Pitch and distance writes are clamped to the game's own limits; the GPose FOV slider is clamped to -0.5 to 0.5.
- The wrappers are pure (`GameAccess/Pure`) and the offline self-check drives every failure path with a fake reader
  (`Checks/GameAccessChecks.cs`).

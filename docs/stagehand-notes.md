# Stagehand 0.5.5 notes for Soundstage

Formerly Keylight: this was written about the prototype under that name, and the name is Soundstage throughout.

Source read: `universalconquistador/Stagehand` at tag `v0.5.5` (commit 48b6f54, 07/10/2026), cloned to the session scratchpad. Nothing was built or run. Paths below are relative to the repo root. Line numbers are from that checkout.

Checked on this machine (18/11/2026):

- `Soundstage\bin\Release\Stagehand.Api.dll` and `Stagehand.Definitions.dll` are byte-identical (SHA-256) to the ones in `installedPlugins\Stagehand\0.5.5.0\` and to the NuGet 0.5.5 copies. No version skew.
- The installed `Stagehand.Api.xml` and `Stagehand.dll` both contain `TryCreateOrUpdateTemporaryStageWithTransform`. The provider has it.
- `pluginConfigs\Soundstage.json` does not exist, so Soundstage's config has never been saved: no slider was moved and the defaults (key 1.0, fill 0.35) were what went to the game.
- `dalamud.log` has no Soundstage lines apart from load (00:31:59) and one `Long UiBuilder(Soundstage) 157ms` hitch at 00:32:11. No exception, no Stagehand error. The plugin logs nothing itself, so the log cannot say what the calls returned.
- The stage file `Documents\Stages\Scene.json` is an empty stage (`Objects: {}`, `FormatVersion: 0`, 170 bytes). The known-good light is in Stagehand's autosave: `%APPDATA%\XIVLauncher\pluginConfigs\Stagehand\autosave\Scene.json` (see "Known-good reference").

## 1. How temporary stages work (IPC provider side)

All in `Stagehand/Services/IpcApiService.cs`.

### Create or update

`TryCreateOrUpdateTemporaryStageWithTransform` (lines 145-159):

1. `StageDefinition.TryParseDefinitionString(definitionString, out definition)` (`Stagehand.Definitions/StageDefinition.cs:160-172`). Only `JsonException` is caught. A parse failure returns `false`. Any other exception propagates through IPC to the caller.
2. Key is `temp://temp/<stageId>` (`Services/LiveStageService.cs:66-71`).
3. Stores `{definition, translation, rotation, scale, debugName}` in `_temporaryStages` (line 151).
4. **Immediately** calls `_liveStageService.CreateOrUpdateLiveStage(...)` (line 152), which builds the real game objects. Returns `true`.

So the XML doc ("to show the created stage, call TrySetTemporaryStageVisible") does not match 0.5.5. The stage is live and visible as soon as Create returns `true`. `TrySetTemporaryStageVisible(id, true)` just calls `CreateOrUpdateLiveStage` again with the stored transform (lines 186-200), which is an update with identical data. The ApiDemo does the same pair of calls every tick (`Stagehand.ApiDemo/Trail.cs:147-148`).

Return value `true` means "definition parsed and handed to the live stage". It does not mean a light exists. `LiveStage.Update` silently drops any object whose creation returns null (`Live/LiveStage.cs:94-101, 106-109`), and `CreateLight` returns null when `SceneLight.Create` returns null (`Live/LiveObjectService.cs:139-143`, with a `// TODO: Log!`). Nothing is logged on either path.

### Set transform, visible, destroy

- `TrySetTemporaryStageTransform` (161-173): `false` if the id is unknown or no live stage exists for the key. After a location change the live stage is gone, so this returns `false` until the stage is shown again.
- `TrySetTemporaryStageVisible(id, true)` (189-199): `false` only if the id is not in `_temporaryStages`. Otherwise recreates/updates and returns `true`.
- `TrySetTemporaryStageVisible(id, false)` (203): destroys the live stage, returns whether one existed. The definition stays stored, so `true` brings it back.
- `TryDestroyTemporaryStage` (175-184): removes the stored definition and the live stage.
- Everything is `ConcurrentDictionary`-backed. There is no thread check, no marshalling to the framework thread, no rate limit, no "temporary stages enabled" setting, no housing-only rule, no GPose check, no territory check, and no `FormatVersion` check on parse (a grep for `FormatVersion` shows it is only compared in `Windows/LibraryWindow.cs:906` for local files). `IntendedTerritoryType` in `Info` is not read by the IPC path.

### Thread

The provider runs on whatever thread calls it. Stagehand's own creates run on the framework thread (`LocalStageService.cs:119, 197, 205`) or from a UI draw (`LocalStageService.cs:97`, the manual Show button). The ApiDemo calls from `Framework.Update`. Calling from `UiBuilder.Draw` is therefore known to work for the manual path. Marshalling Place onto `IFramework.RunOnFrameworkThread` is still the safer match to what the demo and Stagehand do.

### Location

`Services/GameStateService.cs:45-54` runs every framework tick, builds a `StageLocation` (world, territory, ward, division, house, room; `Stagehand.Api/StageLocation.cs:22-97`) and raises `LocationChanged` when it differs from the last one. `IpcApiService.OnLocationChanged` (72-81) then destroys the live stage of every temporary stage (but keeps the stored definition) and re-raises the event to IPC subscribers (docs: "immediately after all temporary stages are hidden"). Nothing brings them back. The consumer must call `TrySetTemporaryStageVisible(id, true)` (or Create again) in its handler. Soundstage does (`Plugin.cs:82-89`).

Notes:

- The constructor discards its first `TryGetLocation` result (`GameStateService.cs:40`, `out var location` is never assigned to `_lastLocation`), so the first framework tick after Stagehand loads always fires one `LocationChanged`. Harmless after login.
- `TryGetLocation` returns false and `default` when not logged in, so logging out and in fires the event too.
- Entering GPose does not change any field of `StageLocation` as far as the source shows (territory, world, housing state). A temporary stage should survive entering and leaving GPose. This is inferred from reading the fields, not measured in game.
- Temporary stages exist only until Stagehand unloads (`IStagehandApi.Temporary.cs:12-13`). `LiveStageService.Dispose` destroys all live stages.

### Other IPC facts

- API revision is 1.2 (`Stagehand.Api/IStagehandApi.cs:28`). `CheckApiAvailability` returns `StagehandTooOld` only if the plugin's revision is below the library's. MareSempiterne logs "rev 1.2 connected", which matches.
- IPC name prefix is `Stagehand` (`[IpcInterface("Stagehand")]`), generated by HQIPC 1.1.2. `StageLocation` and `ApiRevision` cross the boundary by Dalamud's JSON fallback; Vector3 and Quaternion are shared core types.

## 2. From light definition to game light

### Definition fields and defaults (`Stagehand.Definitions/Objects/LightDefinition.cs`)

| Field | Class default | Line |
|---|---|---|
| Shape | Point | 86 |
| Color (Vector3) | (1, 1, 1) | 91 |
| Intensity | 4.0 | 96 |
| ProjectedTextureGamePath | "" | 104 |
| ShadowPlaneNear / Far | 0.25 / 100 | 114, 122 |
| FalloffFunction | Quadratic | 130 |
| FalloffFactor | 1.0 | 143 |
| SpotLightAngleDegrees | 30 | 148 |
| AngularFalloffDegrees | 5 | 153 |
| Range | 15 | 161 |
| CharacterShadowRange | 50 | 166 |
| EnableSpecularHighlights / DynamicShadows / CharacterShadows / ObjectShadows | all true | 66-81 |
| Position, Scale, RotationPitchYawRollDegrees (base class, `ObjectDefinition.cs`) | 0, 1, 0 | 35-45 |

Soundstage's `LightDefinition`s leave the shadow, falloff and texture fields at these defaults, apart from the values it sets.

### Mapping to the game (`Stagehand/Live/LiveLight.cs`, `LiveObjectService.cs`)

- `CreateLight(shape)` (`LiveObjectService.cs:130-148`) calls `SceneLight.Create(shape, "Stagehand.Light", null)` with the shape chosen in `VisitLightDefinition` (405-414): Ambient to `WorldLight`, Point to `PointLight`, Spot to `SpotLight`, Flat to `FlatLight`. Then `TryUpdate` applies the definition.
- Constructor defaults before `TryUpdate` (`LiveLight.cs:95-116`): `RenderLight.Transform` points at the draw object position, `MaxRange = RenderLight.UnlimitedMaxRange` (the FFXIVClientStructs doc says this value is what GPose's own lights use), Intensity 1, Color white.
- `TryUpdate` (145-233) writes straight to the render light:
  - Flags: `SpecularHighlights`, `DynamicShadows`, `CharacterShadows`, `ObjectShadows` from the four bools (162-179).
  - `Color = lightDefinition.Color` and `Intensity = lightDefinition.Intensity` (190-191). Raw floats, no scaling, no clamping in Stagehand. Colour is a Vector3 where 1.0 is a full channel. The editor's `DragFloat` for intensity allows 0 to 100 (`Editor/DefinitionEditors/Objects/LightDefinitionEditor.cs:319`), so intensity is an open-ended float, not 0 to 1, and the new-light default is 4.
  - `Range`, `FalloffType` (Linear/Quadratic/Cubic), `FalloffFactor`, `SpotLightAngleDegrees`, `AngularFalloffDegrees`, `CharacterShadowRange`, `ShadowPlaneNear/Far`, `FlatLightSkewAngleDegrees` (converted to radians) are copied across.
  - `SpotLightAngleDegrees` is the full cone angle: the editor draws the cone with half-angle `0.5 * SpotLightAngleDegrees` and the falloff cone with `0.5 * (cone + falloff)` (`LightDefinitionEditor.cs`, `DrawOverlays`, Spot branch). `AngularFalloffDegrees` is added on top of that.
  - Position, rotation and scale are written to the scene object, then `UpdateTransforms(false)` (216-224). The transform notifications are in `LiveDrawObject.cs:11-13`.
- The doc comment says Ambient "seems to only function in exterior location" (`LightDefinition.cs:16-18`). Not relevant to Soundstage.
- Axis: a Spot or Flat light shines along its own **+Z** (`LightDefinition.cs:26-29`, 31-34).
- A light is not visible as an object. A temporary stage has no editor overlay (the gizmo circles and cones in `DrawOverlays` exist only for an open editor), so a light that is too dim looks identical to nothing happening.

### Transform

- Position is in world metres. The editor stores world coordinates with an identity stage transform (see the autosave file below).
- Rotation is stored as `RotationPitchYawRollDegrees` = (pitch, yaw, roll) in degrees, and converted with `Quaternion.CreateFromYawPitchRoll(yaw=Y, pitch=X, roll=Z)` (`ObjectDefinition.cs:51-54`).
- The parent (stage) transform is applied by `LiveObject.ApplyParentTransform` (`Live/LiveObject.cs:58-72`):
  - `rotation = parentRotation * localRotation`
  - `position = Vector3.Transform(localPosition, parentRotation) * parentUniformScale + parentTranslation`
  - `scale = localScale * parentUniformScale`

  So the stage transform is applied to every object, lights included, and a stage placed at the player's feet with a yaw quaternion works as a rigid frame. `LiveLight.TryUpdate` does this at 216-219. Soundstage's use of it is correct.
- Derived from System.Numerics (right-handed): pitch about X with a positive angle takes +Z towards -Y, so positive pitch tips a +Z spot **downwards**; yaw about Y with a positive angle takes +Z towards +X (`(sin yaw, 0, cos yaw)`). `CreateFromYawPitchRoll` applies roll, then pitch, then yaw, so pitch tilts in the light's own frame and yaw then turns that about world Y. Soundstage's maths (`StagehandBridge.cs:81-83, 92`) matches this reading. Not verified in game.

Because the whole stage is one rigid frame attached to the player and the key light is aimed at the stage origin, a wrong yaw or left/right convention would put the key light on the wrong side or behind the player but still pointed at them. It cannot make the beam miss. Only a wrong pitch sign could, and then by about 7 degrees against a 40 degree cone.

## 3. Editor defaults for new lights (known to work)

`Stagehand/Editor/Windows/EditorWindow.cs` lines 479-520 (Create menu). Every light starts from the class defaults in the table above. The menu only sets:

| Menu item | Shape | Position | Rotation | Other |
|---|---|---|---|---|
| Ambient Light | Ambient | camera position | identity | defaults |
| **Point Light** (line 490) | Point | camera position | identity (0,0,0) | Color white, Intensity 4, Range 15, Quadratic, Factor 1, shadows all on, ShadowPlane 0.25/100, CharShadowRange 50 |
| **Spot Light** (line 501) | Spot | camera position | camera orientation (`GetCameraQuaternion`, line 452: inverse of the view-matrix rotation times a 180 degree yaw, so the light looks where the camera looks) | Cone 30, Angular falloff 5, Range 15, Intensity 4, otherwise as Point |
| Flat Light | Flat | camera position | camera orientation | defaults |

The user then edits colour, intensity (0-100), cone (0-179), falloff (0-90) and range (0-300) in the object editor (`LightDefinitionEditor.cs:312-441`).

### Known-good reference (a hand-made stage)

`%APPDATA%\XIVLauncher\pluginConfigs\Stagehand\autosave\Scene.json` (written 00:24 on 18/11/2026). The copy in `Documents\Stages\Scene.json` was still the empty stage when read. The autosave has `FormatVersion 2` and one object:

- `Type: Light`, `Shape: Point`, `Color (1, 0, 0)`, `Intensity 4`, `Range 15`, `FalloffFunction Quadratic`, `FalloffFactor 1`, all four shadow flags true, `SpotLightAngleDegrees 30`, `AngularFalloffDegrees 5`, `ShadowPlaneNear 0.25`, `ShadowPlaneFar 100`, `CharacterShadowRange 50`, `ProjectedTextureGamePath ""`
- `Position (-100.9422, 20.928253, 27.550707)` (world metres, the camera position when created), `RotationPitchYawRollDegrees (0,0,0)`, `Scale (1,1,1)`, `ModpackId ""`
- JSON shape: `Vector3` is written as `{"X":..,"Y":..,"Z":..}`; enums are strings (`"Point"`, `"Quadratic"`); the `Type` discriminator is the first property of each object.

Soundstage's generated JSON uses the same serialiser options family (`DefinitionStringSerializerOptions`: same converters, not indented), so field names, colour scale (0 to 1 per channel), position units and rotation format all match. `FormatVersion` is 0 in IPC strings, which Stagehand does not check.

## 4. API README and the ApiDemo versus Soundstage

`Stagehand.Api/README.md` is four steps (create client, use it, call it, dispose). The only working consumer in the repo is `Stagehand.ApiDemo/Trail.cs`:

- Builds one persistent `StageDefinition` and re-sends it with `TryCreateOrUpdateTemporaryStage` followed by `TrySetTemporaryStageVisible(id, true)` every placement interval (lines 145-149), after `CheckApiAvailability() == Available`.
- Uses a fresh `Guid` as the stage id (line 46). Does not set `Info`.
- Calls from `IFramework.Update` (line 67 onwards).
- Handles `LocationChanged` by clearing its marks (lines 53-65). It does not call Show there, because it re-sends every interval anyway.
- Disposes with `TryDestroyTemporaryStage` (161).

What Soundstage does differently:

1. It calls from `UiBuilder.Draw` (button handler), not the framework update. That works for Stagehand's own Show button, but differs from the demo.
2. It does not check `CheckApiAvailability()` before calling; it relies on exceptions. The status line only reflects the last `Refresh()`.
3. It sends the stage once and relies on it staying alive. The demo re-sends continuously, so a lost stage heals within one interval.
4. It uses the `WithTransform` overload and moves the stage with `TrySetTemporaryStageTransform` (the demo never does).
5. Intensities are 1.0 and 0.35, against a default of 4 and a known-working hand-made 4.

## 5. Why Soundstage's Place lights does nothing

The code path from the button to the game objects reads clean. I found no condition (setting, zone, housing, GPose, thread, rate limit) under which Stagehand silently refuses a temporary stage. What I cannot see from here is the return value the button produced, because Soundstage shows it only in its window and logs nothing. The ranking below is by likelihood from the source and the files, not from a measured result. Nothing here has been tried in game.

### Cause 1 (most likely): the lights exist but are far too faint to notice

Evidence:

- Editor default intensity is 4.0 and the known-good test was a saturated red (1,0,0) point light at 4 with range 15 (`LightDefinition.cs:96`, autosave file). Soundstage sends the key spot at **1.0** (cream, near white) with range 6 and the fill at **0.35** (pale blue) with range 5. That is 4 times and 11 times below the tested value, and neither colour is saturated.
- `Soundstage.json` was never written, so the sliders were never touched. The shipped defaults are what ran (`Configuration.cs:11-12`).
- The slider tops out at 5 (`MainWindow.cs:42, 45`) while Stagehand allows 100, and daylight or a lit room can wash out a 1.0 light on a face.
- A temporary stage draws no overlay, so a dim light has no visible marker.
- How the engine turns intensity, range and falloff into brightness is not in this source, so I cannot say how dim 1.0 looks. That part is unmeasured.

Change:

- Default key intensity 4.0 and fill 2.0; widen both sliders to 0 to 20 or so.
- Raise ranges to the editor's 15 (spot and point), or at least 10.
- Add a "Smoke test" button that sends the exact known-good light, so the plumbing can be proved separately from the look: one `LightDefinition { Shape = Point, Color = (1,0,0), Intensity = 4, Range = 15, Position = (0, 1.2, 0.8) }` as the only object, placed with the same stage call. If a red glow appears on the character, the IPC path works and only the levels are wrong.

### Cause 2: the real call result is unknown, and one failure path is silent

Evidence:

- `PlaceNow` returns `false` with no status update if `objects.LocalPlayer` is null (`Plugin.cs:74-75`). The window then still shows the previous "Last call" (or "n/a").
- `Place` records the result only into window text (`StagehandBridge.cs:128, 132`). The status was not read, and `dalamud.log` has nothing.
- Stagehand can return `true` yet create no light (`CreateLight` null, see section 1) and logs nothing in that case.
- The 157 ms `Long UiBuilder(Soundstage)` hitch 12 seconds after load is consistent with a first Place press (first IPC call, JSON, light creation), but it could also be the window opening. Not proof.

Change:

- Inject `IPluginLog` and log every call and result with the stage id, the position and yaw, and the definition string. Log "LocalPlayer null" explicitly and show it in the window.
- After Create, call `GetLocation()` and log it. Log the JSON once so it can be diffed against the autosave file.
- Run the whole Place on `framework.RunOnFrameworkThread(...)` and report the result on the next draw.

### Cause 3: the stage is created and then destroyed by a location change

Evidence:

- Stagehand destroys all temporary live stages on every `LocationChanged` (`IpcApiService.cs:72-81`), including the one fired on its first tick after load and on any relog, zone change or move into or out of a house.
- Soundstage's handler only re-shows if `bridge.Placed` is true (`Plugin.cs:84`) and runs `Show()` then `PlaceNow()` (`Plugin.cs:87`), which itself bails out silently if `LocalPlayer` is null (cause 2).
- A hot reload of the dev plugin runs `Dispose`, which destroys the stage (`StagehandBridge.cs:192`). A rebuild after pressing Place would remove the lights.
- I found no evidence that anything fires `LocationChanged` repeatedly while standing still. An oscillating location would show as flicker rather than nothing.

Change:

- Like the demo, heal by re-sending. Keep the last definition and, on `LocationChanged` or on a slow timer (for example once a second, only while Placed), call `TrySetTemporaryStageVisible(id, true)`; if it returns `false`, call Create again. Log each heal.

### Cause 4: spot aim is off (low)

- The key spot is aimed at the stage origin, which is the player's feet position, at an aim height of 1.6 (`StagehandBridge.cs:21, 76`). As explained in section 2, a rigid stage means a yaw or handedness error cannot make the beam miss, and a pitch sign error is about 7 degrees on a 40 degree cone. So this cannot explain "nothing" and would only change where it lands.
- `SpotLightAngleDegrees` is the full cone angle, so 40 is a 20 degree half-angle. Fine.

Change (only if the smoke test shows the point light but not the spot): send a test spot at the editor default (cone 30, falloff 5, range 15, intensity 4) 2 metres in front of the player, yawed to face them, and compare.

### Cause 5: JSON rejected (low)

- A parse failure returns `false` (`IpcApiService.cs:147, 157`). The DLLs are byte-identical to Stagehand's, so field names and converters match. Only a malformed string would fail, and the status line would show `false`.

### Cause 6: light budget or shadows (speculative)

- The key light has dynamic, character and object shadows on, as did the hand-made test. I have no evidence the game caps lights or shadows in a way that would hide these. If everything else checks out, test the key with shadows off.

### Separate problem found: Soundstage's window hides in GPose

Stagehand sets `PluginInterface.UiBuilder.DisableGposeUiHide = true` (`Stagehand/Plugin.cs:167`) so its windows stay up in GPose. Soundstage does not, so by default Dalamud hides Soundstage's window while in GPose, and a GPose lighting tool needs it visible. Set `pluginInterface.UiBuilder.DisableGposeUiHide = true` in the Soundstage `Plugin` constructor. This does not explain a press outside GPose doing nothing.

## 6. Suggested order of work

1. Add logging and the LocalPlayer-null message (cause 2), so the next press produces a fact.
2. Add the smoke-test button with the exact known-good red point light (cause 1).
3. Raise defaults to intensity 4 and range 10 to 15, widen the sliders.
4. Add the self-healing re-show (cause 3).
5. Set `DisableGposeUiHide`.
6. Only then tune the spot aim.

# Integrations: Glamourer and Penumbra (Part 7.5)

What a Scene's `pose` block can reach for, and exactly which IPC calls do it. Brio's side is in `docs/brio-notes.md`.

Nothing here runs unless the Scene has a pose block **and** the user ticked "Apply pose and outfit" for that Scene
(`ScenesTabSettings.ApplyPoseOutfitFor`). A missing plugin, pose, design or mod skips only that part, with one line in the
Scenes tab and one `[Soundstage/SCENES]` log line.

## Where the names came from

The consumer libraries are the plugins' own API packages, taken from NuGet (restored from the local cache; both are the
official packages from the plugin authors' repositories):

| Package | Version | Size | Plugin installed on this PC |
|---|---|---|---|
| `Glamourer.Api` | 2.8.2 | 70,360 bytes | Glamourer 1.7.1.3 |
| `Penumbra.Api` | 5.19.2 | 1,009,217 bytes | Penumbra 1.7.2.1 |

Source: https://www.nuget.org/packages/Glamourer.Api and https://www.nuget.org/packages/Penumbra.Api.

Labels were checked two ways. The strings `Glamourer.ApiVersion.V2`, `Glamourer.GetDesignList.V2`, `Penumbra.ApiVersion.V5`,
`Penumbra.GetCollectionForObject.V5`, `Penumbra.TrySetMod.V5` and `Penumbra.TrySetModSettings.V5` all appear in the
`Glamourer.Api.dll` and `Penumbra.Api.dll` that the installed plugins ship (found by a byte search of
`%APPDATA%\XIVLauncher\installedPlugins`, read only). The typed wrapper classes in the packages build the label and
the call, so Soundstage never writes a label by hand. Not independently seen as a string: `Glamourer.ApplyDesign`'s version
suffix (the wrapper supplies it). Whether a call works against the running plugin is a checklist step, not something
measured offline.

## Glamourer

| Use | Call (class in `Glamourer.Api.IpcSubscribers`) | Notes |
|---|---|---|
| Is it there | `ApiVersion` -> `(Major, Minor)` | Available when `Major >= 1`. Any exception means "not running". |
| Find a design by name | `GetDesignList` -> `Dictionary<Guid, string>` (id to name) | Name match is case-insensitive. |
| Apply it | `ApplyDesign(Guid designId, int objectIndex, uint key, ApplyFlag flags)` -> `GlamourerApiEc` | Object index 0 (the local player), key 0, flags `ApplyFlagEx.DesignDefault` (equipment and customisation, no lock). Success is the only good answer. |

Code: `src/Soundstage/Integrations/GlamourerBridge.cs`.

## Penumbra

| Use | Call (class in `Penumbra.Api.IpcSubscribers`) | Notes |
|---|---|---|
| Is it there | `ApiVersion` -> `(Breaking, Features)` | Available when `Breaking >= 5`. |
| Which collection | `GetCollectionForObject(int objectIndex)` -> `(ObjectValid, IndividualSet, (Id, Name))` | Object index 0. A nil id means nothing to set. |
| Enable or disable a mod | `TrySetMod(Guid collection, string modDirectory, bool enabled, string modName)` -> `PenumbraApiEc` | `Success` and `NothingChanged` both count as fine. |
| Pick options | `TrySetModSettings(Guid collection, string modDirectory, string groupName, IReadOnlyList<string> options, string modName)` -> `PenumbraApiEc` | One name for a single-choice group, several for a multi-choice group. If any option is missing Penumbra changes nothing for that call. |

The parameter order of the subscriber classes differs from the interface in the XML docs (the interface lists
`modName` before the value; the subscribers list the value first and `modName` last). The code follows the subscribers,
which is what the compiler checks; the Scenes checklist has a step that applies a real mod setting so a wrong order shows up.

These settings are written into the collection and stay there. Penumbra's API has no temporary version of them that the
package exposes, so the "Apply pose and outfit" tick is the safeguard, and the tab says what the block will change before
the tick is made.

Code: `src/Soundstage/Integrations/PenumbraBridge.cs`.

## Brio and the pose shelf

A pose block names a pose by `posePack` and `poseName` (the shelf's pack folder and the file name without its extension).
`PoseShelf.FindPose` finds it among the scanned poses and `PoseShelf.LoadForScene` loads it on the GPose actor with the
shelf's ticked parts (body, expression). It needs GPose and Brio. A pose file named by path (`poseFile`) is never used,
because a path means nothing on another machine, and it is removed from share codes.

Code: `src/Soundstage/Integrations/BrioPosePort.cs`, `src/Soundstage/Scenes/PoseBlockApplier.cs` (the pure part that decides
what to do and what to say, checked offline with stand-in ports).

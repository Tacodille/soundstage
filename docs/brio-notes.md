# Brio IPC notes

Formerly Keylight: this was written about the prototype under that name, and the name is Soundstage throughout.

What Soundstage's Poses shelf calls, and where each fact comes from. Read on 20/11/2026 against the installed Brio
0.8.0.12 (`...\XIVLauncher\installedPlugins\Brio\0.8.0.12\`, `IPC.EnableBrioIPC` true in `pluginConfigs\Brio.json`).

Sources, all read-only:

- **Brio** = github.com/Etheirys/Brio at tag `v0.8.0.12`. Paths below are from that tree.
- **Brio.API** = github.com/Etheirys/Brio.API at commit `85489a6d36637237330911ad6e25fdca05be4a24`, the commit the tag
  pins in its `Libs/Brio API` submodule (`.gitmodules`; tree entry `Libs/Brio API`). Its csproj says PackageVersion
  3.0.1, and nuget.org lists Brio.API 3.0.0 and 3.0.1. Soundstage references **Brio.API 3.0.1**.
- The installed folder also holds `Brio.API.dll` and `Brio.API.xml`; the xml lists the same methods (LoadPoseFromFile,
  LoadPoseFromJson, GetPoseAsJson, ResetPose and so on), so the installed build agrees with the source.

Line numbers are the line in the file as read.

## The IPC calls Soundstage uses

All are `FuncSubscriber`s over Dalamud IPC. `IGameObject` is Dalamud's type, so the actor is passed straight through.

| Call | Label | Signature | Source |
|---|---|---|---|
| ApiVersion | `Brio.ApiVersion` | `() -> (int Breaking, int Feature)` | Brio.API `Brio.API/IPC/BrioState.cs` lines 8-20 (label line 11) |
| LoadPoseFromFile | `Brio.LoadPoseFromFile.V3` | `(IGameObject actor, string path) -> bool` | Brio.API `Brio.API/IPC/Posing.cs` lines 48-59 (label line 51, Invoke line 54) |
| LoadPoseFromJson | `Brio.LoadPoseFromJson.V3` | wire type `(IGameObject, bool isLegacyCMToolPose, string json) -> bool` | Brio.API `Brio.API/IPC/Posing.cs` lines 61-72 (label line 64) |
| GetPoseAsJson | `Brio.GetPoseAsJson.V3` | `(IGameObject actor) -> string?` | Brio.API `Brio.API/IPC/Posing.cs` lines 74-85 (label line 77) |

- The subscriber class `LoadPoseFromJson.Invoke` reorders its arguments to `(actor, json, isLegacyCMToolPose = false)`
  (`Posing.cs` lines 67-68), and names the json parameter `filename`. It is the JSON text, not a path
  (`IPosing.cs` line 48 and its doc comment lines 41-47). Soundstage calls the subscriber, so it never touches the wire order.
- The providers are registered in Brio's `Brio/IPC/API/IPCProviders.cs` lines 56-58 (the three pose calls) and line 34
  (ApiVersion via `ApiVersion.Provider`, line 34).
- "V3" in the labels is `Brio.MajorAPIVersion = 3` (`Brio/Brio.cs` line 49; minor 0 on line 50). Soundstage accepts
  `Breaking == 3`.
- Brio only answers while `IPC.EnableBrioIPC` is true (`Brio/IPC/API/BrioAPIService.cs` line 12). With it off, or Brio
  not loaded, the call throws `IpcNotReadyError` (Brio.API `FuncSubscriber.cs`, `Invoke`). Soundstage catches that, logs
  it, and shows "Brio not available".
- **Not used:** `IsValidGPoseSession`. Its label is `$"Brio.{nameof(IsAvailable)}"` (`BrioState.cs` line 41), the same
  string as `IsAvailable`, which looks like a copy-paste slip in Brio.API. Soundstage asks Dalamud's `IClientState.IsGPosing`
  instead. Brio's own handlers also return false outside GPose (`Brio/IPC/API/PosingAPI.cs` lines 53, 69, 37).

## What the load calls do (this decides Body / Expression)

- **There are no importer options in the IPC.** Neither load call takes one (signatures above), and the list of
  providers (`IPCProviders.cs` lines 33-60) does not expose `GetNewPoseImporterOptions` (`Brio/Game/Posing/PosingService.cs`
  line 92, internal to Brio). The strings `DefaultIPCImporterOptions` and `ExpressionsOnly` in the dll are Brio internals.
- `LoadPoseFromFile` calls `posingCapability.ImportPose(filename)` (`PosingAPI.cs` line 59). That overload picks the
  reader by extension, `.cmp` as a CM Tool file, anything else as a Brio pose (`Brio/Capabilities/Posing/PosingCapability.cs`
  lines 129-145, cmp at 133-137), and passes no options, so `ImportPose_Internal` falls to `DefaultImporterOptions`
  (line 212). Those are rotation only with the `weapon` and `ex` bone categories off (`PosingService.cs` lines 45-47).
  It returns true even when the file is invalid: the error is only a Brio toast (`PosingCapability.cs` lines 141-144).
- `LoadPoseFromJson` calls `ImportPose(..., null, asIPCpose: true)` (`PosingAPI.cs` lines 79 and 83), which selects
  `DefaultIPCImporterOptions` (`PosingCapability.cs` lines 206-209): **all transform components, every bone category**
  (`PosingService.cs` line 61). `isLegacyCMToolPose` true parses the JSON as a CM Tool pose (`PosingAPI.cs` line 79).
- `GetPoseAsJson` returns `JsonSerializer.Serialize(posingCapability.ExportPoseAsFileData())` (`PosingAPI.cs` lines 43-45),
  the same Brio pose JSON that `LoadPoseFromJson` reads. That makes snapshot and restore symmetrical.
- An import applies only the bones that are present in the file: `PoseImporter.ApplyBone` does
  `poseFile.Bones.TryGetValue(bone.Name, ...)` before applying (`Brio/Game/Posing/PoseImporter.cs` lines 28-38). An import of
  a pose with no bones, main hand or off hand is rejected as "Invalid pose file" (`PosingCapability.cs` lines 179-184).

### So Body-only and Expression-only are done in Soundstage

Because the IPC cannot say "body only", Soundstage deletes the other part's bones from the JSON before calling
`LoadPoseFromJson`, using Brio's own category rules:

- Body = Brio's `BodyOptions`: the `weapon`, `ears`, `hair`, `face`, `eyes`, `lips`, `jaw`, `head`, `legacy` and `ex`
  categories off, everything else on (`PosingService.cs` lines 65-75). Bones are matched by name prefix, first checked
  against `n_throw` (always excluded), then the category prefixes (`Brio/Game/Posing/BoneFilter.cs` lines 109-149).
  Soundstage also empties `MainHand` and `OffHand` for body-only, because weapons are off.
- Expression = Brio's `ExpressionOptions`: only `head`, `ears`, `hair`, `face`, `eyes`, `lips`, `jaw` on
  (`PosingService.cs` lines 77-85). Soundstage also empties `MainHand`, `OffHand`, `Prop` and `Ornament`.
- The prefix table is generated from `Brio/Resources/Embedded/Data/BoneCategories.json` (Filter categories only) into
  `src\Soundstage\BrioBoneCategories.cs`.
- **Both parts ticked** skips all that and calls `LoadPoseFromFile`, so a plain click behaves exactly as Brio's own
  default pose import does.
- **`.cmp` files** always go through `LoadPoseFromFile` as body (Expression is greyed). Brio's own importer treats CM Tool poses
  as having no expression part.

Known differences from Brio's own library import, so you can judge them in game:

- Body-only and Expression-only run with the IPC options (all transform components, so bone positions and scales as
  well as rotations). Brio's library body import is rotation plus position. If a partial import looks stretched, this is
  the first suspect.
- Anamnesis-style bone names (`Head`, `ArmLeft`) in old files are converted by Brio after the JSON is read
  (`PoseFile.SanitizeBoneNames`, `Brio/Files/PoseFile.cs` lines 123-131). Soundstage's filter sees the raw names, so an
  Anamnesis-named file counts as all "other" (body). Not seen in the 67 files here; those use game names.
- Every load adds a snapshot to Brio's undo history (`PosingCapability.cs` line 249-250), so hover preview fills it.

## Reading the current pose (hover preview)

`GetPoseAsJson` exists (above), so hover preview is real: snapshot once with it, load the hovered pose, and put the snapshot
back with `LoadPoseFromJson(actor, json, false)`. The restore uses the IPC import options (everything), which is what
the snapshot contains, so it puts the whole pose back.

## Embedded preview image

`.pose` files derive from `JsonDocumentBase`, which has `string? Base64Image` (`Brio/Files/JsonDocumentBase.cs` line 23).
Brio's library decodes it with `Convert.FromBase64String` and loads the bytes as an image (`Brio/Services/Library/Sources/FileSource.cs`
lines 251-255), and the metadata editor writes it (`Brio/UI/Modals/MetadataModal.cs` line 208). So it is an ordinary
image file (PNG or JPEG) in base64. Soundstage loads it with Dalamud's `ITextureProvider.CreateFromImageAsync`.
Measured here: 29 of the 67 files have the `Base64Image` key and all 29 hold null, so no real file shows a thumbnail yet.
Export a pose from Brio with a picture attached (Brio's metadata window) to get one.

## Where the actor comes from

The IPC takes the game object Brio is posing. In GPose that is the GPose actor, not object index 0. Soundstage uses, in order:
`ITargetManager.GPoseTarget`, then a GPose-range object (index 201 or more) with the local player's name, then the local
player. Which one it used is in the log line (`actor=Name#index`). If the first load returns false with a real GPose
target, this lookup is the thing to check.

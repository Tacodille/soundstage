# Soundstage

A GPose studio for FFXIV. Pick a Scene and it sets the lights, a backdrop and some props around your character, frames the
camera, switches your ReShade look, and loads a pose, all in one click. Then you tweak it with live sliders and save it as
your own. It's a Dalamud plugin (API 15), version 0.1, by Tacodile.

It used to be called Keylight. If you ran that, your settings and your own presets carry over (see "Coming from Keylight").

**What it needs**

| Thing | Version | For |
|---|---|---|
| [Stagehand](https://github.com/universalconquistador/Stagehand) | 0.5.x, installed and enabled | Lights, backdrops, props. The Lighting, Set and Scenes tabs. |
| [Brio](https://github.com/Etheirys/Brio) | 0.8.x, with IPC switched on in its settings | The Poses shelf and everything that loads a pose. |
| Glamourer and Penumbra | Optional | A Scene can switch an outfit design and mod settings. |
| The ReShade add-on | Optional, a separate download | Look tab (a ReShade preset per Scene, auto focus) and one screenshot route. |
| An AI key | Optional, your own | Photo to Scene and pose tagging. See [SETUP-AI.md](SETUP-AI.md). |
| The face helper | Optional, downloaded from the Face tab | Reading a photo's head angle, face and body pose, locally. |

The **Status** tab tells you which of these are answering right now, and what to do about the ones that aren't.

## Install

1. In game, `/xlsettings` > Experimental > Custom Plugin Repositories, add
   `https://raw.githubusercontent.com/Tacodille/soundstage-releases/main/repo.json` and save.
2. `/xlplugins`, search for Soundstage, install.
3. Install Stagehand and Brio from the same window if you haven't, and switch IPC on in Brio's settings.
4. Go into GPose and type `/soundstage`.

### The ReShade add-on (optional, separate download)

The add-on lets Soundstage switch your ReShade preset and nudge a focus setting from inside the game. It comes as its own
file, `soundstage.addon64`, beside the plugin zip in the releases. The plugin never writes into your game folder, so you copy it in
yourself:

1. Close the game.
2. Copy `soundstage.addon64` into the game folder, the one with `dxgi.dll` (the Status tab's **Open the game folder** button opens it).
3. Start the game. The Status tab's add-on row goes green.

You need ReShade 6.x with add-on support installed already. If you've set `AutoSavePreset=1` in `ReShade.ini`, the Look tab
warns you, because ReShade may save a value the add-on pushed into your preset. The add-on puts values back when a Scene comes off.

### The face helper (optional, downloaded on request)

Face, expression and body matching read a photo with Google's MediaPipe models, on your PC, with nothing uploaded. That needs
a small Python and a few libraries, so the plugin doesn't ship them. The Face tab has a **Download the face helper** button:

- **122 MB to download, 327 MB once unpacked.** If you use the body tools there's one more model, **9 MB**.
- Only from python.org, PyPI and Google. Every file is checked against a pinned hash and thrown away if it doesn't match.
- It lives in the plugin's own config folder. **Remove face helper** on the Face tab deletes it.

## What's in the window

| Tab | What it does |
|---|---|
| **Scenes** | The catalogue (shipped Scenes by category, then **Mine**), palettes, **Apply**, **Remove all**, undo, **Snapshot** of what's on stage, **Save as**, share codes (`ss1:...`), and the pose and outfit a Scene carries. |
| **Lighting** | Key light sliders, face-aimed lights, gels and colour harmony, animated lights (pulse, flicker, drift, a capped strobe), and exposure colours that warn when a light is too hot at the face. |
| **Set** | A seamless backdrop sweep built from the dyeable partition, seven prop kits, game effects and music ("atmosphere"), and your own picture on a portrait canvas. |
| **Poses** | Your `.pose` and `.cmp` files through Brio. Folders, search, favourites, recents, hover preview, tags, thumbnails, **Tag with AI**, and poses that fit the current Scene. |
| **Camera** | Framing presets, a turntable, and composition overlays (thirds, golden ratio, centre cross, safe margin). |
| **Look** | A ReShade preset per Scene and auto focus, through the add-on. |
| **Workflow** | Capture, the photo booth (countdown, hide everything, shoot, restore), a contact sheet of many Scenes, and an onion skin. |
| **AI** | Your key, a key test, and the privacy note. See [SETUP-AI.md](SETUP-AI.md). |
| **Photo** | Pick a photo, get a Scene card (light direction, softness, colours), apply, adjust or save it. |
| **Face** | A photo's head angle and light direction, read locally, to move the camera and aim the key light. |
| **Expression** | Calibrate your character's face, then match a photo's expression. Has a body section that matches a photo's pose. Both are labelled experimental until their own ratings say otherwise. |
| **Status** | The live checks above. |
| **Tools** | Only after `/soundstage dev`: the test checklist, the light survey, a smoke test and the probes. |

Things worth knowing:

- The window stays up in GPose. The light markers (a dot per light) hide with the game UI.
- Stagehand drops temporary stages when you change zone. Soundstage re-sends what you had placed and keeps trying for about 10 seconds.
- A Scene has at most six lights for now.
- Scenes are plain JSON. `scenes.json` ships beside the dll, your own live in `user-scenes.json` in the plugin's config
  folder. The format is in [docs/scenes-schema.md](docs/scenes-schema.md). A bad Scene is skipped and the reason goes to the log.

## Commands

- `/soundstage` opens the window.
- `/soundstage test` opens the in-plugin test checklist.
- `/soundstage markers` toggles the light markers (`on` or `off` sets them). Good on a hotbar macro.
- `/soundstage dev` shows or hides the Tools tab.
- `/soundstage survey 3` logs the game's lights within 25 m, labelled level 3 (a dev tool).

## Poses, in a bit more detail

Brio's calls and source notes are in [docs/brio-notes.md](docs/brio-notes.md).

- Folders default to `Documents\FFXIV Mods` plus Brio's own library folders when they exist. Add or remove folders under
  **Pose folders**, **Rescan** re-reads them. The scan is recursive, runs off the game thread and stops at 5,000 files (it says so).
- **Body / Expression** boxes are both ticked by default. One ticked loads only that part (Soundstage strips the other bones first,
  because Brio's IPC has no body-only option). A `.cmp` has no expression, so it loads as body.
- **Hover preview** loads a pose after about 350 ms resting on a row and puts the old one back when you leave without clicking.
  Every load is an undo step in Brio, so turn it off if that bothers you.
- If a `.pose` carries an embedded picture it shows as a thumbnail. **Make thumbnails** renders the rest through the capture
  routes on a plain grey stage and caches a small picture each.

## Test checklist

One checklist covers every feature, for anyone building or testing the plugin. `/soundstage test` opens it (or Tools > Test checklist).

1. Build (below), add the dll as a dev plugin, enable it, log in.
2. Work down each area, in GPose where it says so. Press Pass or Fail on each step (a note is optional). Every click logs
   `[Soundstage/TEST] <area> <step id> <title>: PASS|FAIL note=...` to `dalamud.log` and is saved in the config under
   `area/step`, so results survive a reload. **Copy summary** puts every area on the clipboard.

The areas, in order:

| Area | Covers |
|---|---|
| Part 1: rename, migration, tabs | Loads as Soundstage, old settings carried over, the tabs behave |
| Core: lights, backdrop, canvas, presets | Stagehand connected, smoke test, each preset, live update, markers, backdrop, picture canvas, save and reload, zone change, remove all |
| Part 2: head bone and GPose camera | Head bone read, camera orbit, pitch, FOV and position writes, restore, outside-GPose refusal |
| Part 3: ReShade add-on | Add-on loads, ping, preset switch, uniform write, screenshot, refusal of a missing preset, unload |
| Part 4: Scenes (format v2 and the catalogue) | The catalogue's Scenes, palettes, the six-light test, old preset ids |
| Part 5: Poses shelf | Scan, load, body and expression split, hover preview and its undo cost, tags |
| Part 6: optional AI layer | Key save, Test key, wrong key, consent, no key leak in the log |
| Part 7: Scenes tab | Apply, undo, snapshot, user Scenes, share codes, pose and outfit |
| Part 8: Lighting | Face aim, animated lights and their frame cost, gels, harmony, exposure |
| Part 9: Set | Sweep joins, kit facing, atmosphere items, the canvas and your own picture |
| Part 10: Camera | Framing presets, the yaw and pitch conventions, FOV, turntable, overlays |
| Part 11: Look | Preset per Scene, mapping, auto focus, restore on removal, add-on copy-in |
| Part 12: Workflow | Capture routes and timings, booth, contact sheet, onion skin |
| Part 13: photo to Scene | Consent, a real photo to a Scene, left and right, ten rated photos |
| Part 14: Pose thumbnails and AI tags | Thumbnails, tagging within the day's budget, Poses that fit |
| Part 15: Status tab and clean install | Each status row going red and green, the clean install from the repository |
| Part 16: Face | Helper download, head angle, camera, light estimate, the error table |
| Part 17: Expression | Calibrate, match, the five-photo rating |
| Part 18: Body pose | Skeleton check, mirror, match, the five-photo rating |

Known unknowns the checklist is there to settle: whether the sweep joins seamlessly, which way each kit's furniture faces,
whether each atmosphere item shows, the canvas picture's facing and whether the texture swap shows, which way the camera's yaw
and pitch run, whether ReShade's auto-save catches the add-on's changes, and whether hover preview floods Brio's undo list.
[docs/troubleshooting.md](docs/troubleshooting.md) lists each with its workaround.

### Adding an area to the checklist

One new file, nothing else: `src\Soundstage\Checklist\Areas\<Name>Steps.cs`, a class that implements `IChecklistArea`
(an `Id` like `"scenes"`, a `Name`, an `Order` of the part number times 10, and a list of `ChecklistStep(id, title,
instructions)`). The window finds it by reflection and the self-check discovers it too. Area files must not use Dalamud
types (they are compiled into the offline self-check, so one that does fails that build). If a step needs buttons or a
value the plugin can read for itself, add a second new file, `Checklist\AreaUi\<Name>StepsUi.cs`, implementing
`IChecklistAreaUi` with the same `AreaId`; every member has a default.

### Adding a tab

One new file under `src\Soundstage\Tabs\` with a class that implements `ITab` (`Name`, `Order`, `Draw(Studio)`). `TabHost`
finds it by reflection. Settings for a feature go in `Configuration.Section<T>("yourKey")`, not in a new field on the shared
class. A new self-check area is one new file in `tests\Soundstage.SelfCheck\Checks\` deriving from `CheckArea`.

## Coming from Keylight

On first start with no `Soundstage.json`, the plugin reads the old `pluginConfigs\Keylight.json` as plain JSON, carries every
setting across, and copies (never moves) `pluginConfigs\Keylight\user-presets.json` to `pluginConfigs\Soundstage\user-presets.json`,
so "Mine" comes with you. The old files are left as a backup. If the old settings file is damaged it logs one line
(`[Soundstage/MIGRATE]`) and starts from defaults.

## Logs

Every Stagehand call, its arguments and its result, and every early return with its reason, goes to `dalamud.log` with the
prefix `[Soundstage/SH]`; the same reason shows in the window's status line. Brio uses `[Soundstage/BRIO]`, checklist results
`[Soundstage/TEST]`, the migration `[Soundstage/MIGRATE]`. Grep for `Soundstage/`.

## Build

```
cd src\Soundstage
dotnet build -c Release
```

Output: `src\Soundstage\bin\Release\Soundstage.dll` with its libraries beside it. Stagehand's two libraries (0.5.5), Brio.API
(3.0.1), Glamourer.Api and Penumbra.Api come from NuGet. Dalamud itself is read from `%APPDATA%\XIVLauncher\addon\Hooks\dev\`.
The Dalamud SDK also writes `bin\Release\Soundstage\latest.zip`, which is what a release uploads.

Load it as a dev plugin: `/xlsettings` > Experimental > Dev Plugin Locations, add the full path to `Soundstage.dll`, then
`/xlplugins` > Dev Tools > Installed Dev Plugins, enable Soundstage.

The ReShade add-on builds with `python tools\build_addon.py` (needs the Visual Studio C++ build tools). It writes
`reshade-addon\out\soundstage.addon64` and tells you where to copy it.

## Offline self-check

```
cd tests\Soundstage.SelfCheck
dotnet run -c Release
```

The checks are split by area in `Checks\`, and the runner finds every `CheckArea` by reflection. They compile the plugin's
Dalamud-free source (Scenes, Set, Poses, Ai, Look, Camera, Lighting, Face, Expression, Capture, Workflow, Status, the checklist
areas) and test it without the game: parsing, light maths, the Scene store, the key store's leak sweep, the migration (on
copies, in a temp folder), the face and body solvers, and the Status rows. Some checks run a Python script and skip with a
note if no Python is found; `SOUNDSTAGE_PYTHON` points them at one.

## Tools

- `tools\scrub_check.py` finds personal paths, names, emails and wording that shouldn't be in a public tree. Exit 0 is clean.
- `tools\gen_troubleshooting.py` rewrites the generated tables in `docs\troubleshooting.md` from the plugin's own messages
  (`--check` just tells you if they're stale).
- `tools\gen_scenes.py` writes `scenes.json` and `gels.json`. The self-check fails if the shipped files are stale.
- `tools\release\release.py` builds, and packages the zip and `repo.json` into `dist\`. It publishes nothing unless you pass `--publish`.

## Licence

MIT, see [LICENSE](LICENSE). The libraries that travel with the plugin, the add-on's header licence and the face helper's
downloads are listed with their own licences in [NOTICE](NOTICE). Two of them (Stagehand's libraries) are AGPL-3.0-or-later.

# Proof: ReShade add-on and screenshot route

Part 3 of the Soundstage plan. This file is the record. Rows marked **NOT RUN** need a run in game; the in-plugin checklist
area "Part 3: ReShade add-on" (`/soundstage test`) walks him through each one and logs the result.

## 1. ReShade state (measured 21/11/2026, read only)

| Item | Value | Where it came from |
|---|---|---|
| Version | 6.8.0.2158 (64-bit) | `ReShade.log` line 1, "Initializing crosire's ReShade version '6.8.0.2158'" |
| Loaded as | `dxgi.dll` in the FFXIV `game` folder, into `ffxiv_dx11.exe` | same line |
| Add-on search | `Searching for add-ons (*.addon, *.addon64) in '<game folder>' ...` | `ReShade.log` line 46 (so this is the add-on-capable build) |
| Add-ons installed | none: `ls *.addon*` in the game folder finds nothing | directory listing |
| Back buffer | `DXGI_FORMAT_R8G8B8A8_UNORM` | swap chain description in `ReShade.log` |
| `PresetPath` | `.\reshade-presets\GShade\<a preset>.ini` (relative to the game folder) | `ReShade.ini` |
| `PresetShortcutKeys` | empty | `ReShade.ini` |
| `PresetTransitionDuration` | 1000 | `ReShade.ini` |
| `KeyScreenshot` | 44 (Print Screen) | `ReShade.ini` |
| Screenshot settings | `SavePath` set, `FileFormat=1` (PNG), `FileNaming=%AppName% %Date% %Time%_%Count%` | `ReShade.ini` `[SCREENSHOT]` |
| Preset folder layout | `reshade-presets\<pack>\...ini`: 29 pack folders, 1604 `.ini` files | directory listing |
| Shaders | `reshade-shaders\Shaders`, `Textures`, `Addons`, `ComputeShaders`, `Optional` | directory listing |

The add-on search line says the build can load add-ons. It is not proof until the probe add-on loads (row 3.3 below).

## 2. SDK used

- ReShade add-on headers from `github.com/crosire/reshade`, tag **v6.8.0** (commit `18deaa52de0c425a78b329e9cb3c497281cd00ec`), the tag that matches
  ReShade 6.8.0.x. `RESHADE_API_VERSION` in that tag is 20. Copied unmodified into `reshade-addon/include/` with the project's
  `LICENSE.md` (BSD 3-clause). The headers themselves say "BSD-3-Clause OR MIT".
- Function names checked against those headers before coding (plan step 3.2):
  `effect_runtime::set_current_preset_path(const char*)`, `get_current_preset_path`, `find_uniform_variable(effect, name)`,
  `get_uniform_variable_type`, `set_uniform_value_float(variable, const float*, count)`, `capture_screenshot(void *pixels)`,
  `get_screenshot_width_and_height`, `save_screenshot(const char *postfix)`, events `init_effect_runtime`, `destroy_effect_runtime`,
  `reshade_present`, `reshade_screenshot`.
- `save_screenshot` takes only a postfix, so the add-on cannot choose the folder with it. `shot <path>` therefore uses
  `capture_screenshot` (raw back buffer pixels, after effects) and writes the PNG itself to the path given.

## 3. Verdict table

| # | Check | Result | Date | Notes |
|---|---|---|---|---|
| 3.1 | ReShade state recorded | **DONE** | 21/11/2026 | Section 1. |
| 3.2 | Add-on builds with MSVC | **PASS** (builds, exports `NAME` and `DESCRIPTION` as ReShade expects) | 21/11/2026 | `tools/build_addon.py` exit 0, 0 warnings, `reshade-addon/out/soundstage.addon64`, 274,944 bytes. MSVC 14.44 (Build Tools 2022). It has not run inside ReShade yet. |
| 3.3 | Add-on loads in game | **NOT RUN** (checklist step 1) | | Expect a load line in `ReShade.log`. |
| 3.4a | Preset switch over the pipe | **NOT RUN** (checklist step 3) | | |
| 3.4b | Uniform write over the pipe | **NOT RUN** (checklist step 4) | | |
| 3.4c | Screenshot capture to a chosen path | **NOT RUN** (checklist step 5) | | |
| 3.4d | Negative control: nonexistent preset | **NOT RUN** (checklist step 6); also covered offline by the self-check for the plugin side | | The add-on checks the file exists before it asks ReShade to switch. |
| 3.5 | Key-press fallback route | **NOT RUN** (checklist step 7) | | you bind a shortcut in ReShade's own UI; the plugin never edits `ReShade.ini`. |

PASS and FAIL are filled in from the checklist's `[Soundstage/TEST] reshade ...` lines in `dalamud.log`.

## 4. Verdict and routes for parts 11 and 12

Run decision (21/11/2026): build every part assuming the proofs pass. So until a row above reads FAIL:

- **Part 11 (Look): build the add-on route.** The plugin sends `preset`, `uniform` and (for auto focus) `uniform` commands over the pipe
  `\\.\pipe\soundstage-reshade`, using `Look/ReShadeProbe.cs` as the client to grow from. The fallback stays only as a runtime degrade:
  if the pipe does not answer, the Look tab shows a plain status line and a "copy preset name" button.
- **Part 12 (capture): build the `shot` route** (add-on writes a PNG to a path the plugin chooses). If the pipe does not answer, capture
  uses the game's own screenshot key.
- **Key-press fallback route: drop as a main path.** Keep it as a documented manual option (you bind a key in ReShade) only if checklist
  step 7 passes, otherwise drop it.

If a checklist row comes back FAIL, edit this section with the date and the route that replaces it.

## 5. The wire protocol

One request per line (UTF-8, `\n`), one reply per line, over the named pipe `\\.\pipe\soundstage-reshade`. The add-on serves one client
at a time; commands run on ReShade's present thread and the pipe waits up to 3 seconds for the frame.

| Request | Reply on success | Reply on failure |
|---|---|---|
| `ping` | `OK soundstage-addon 1` | |
| `status` | `OK preset=<current preset path>` | |
| `preset <path>` | `OK preset=<path>` | `ERR preset file not found: <path>` (current preset unchanged) |
| `uniform <name> <v1> [v2 v3 v4]` | `OK <name>=<values>` | `ERR uniform not found: <name>` or `ERR bad number` |
| `uniforms` | `OK <name>,<name>,...` (up to 200) | |
| `shot <path>` | `OK <path> <w>x<h>` | `ERR ...` (unsupported format, write failed) |

Relative preset paths are taken against the game folder, the same as `PresetPath` in `ReShade.ini`.

### Protocol version 2 (part 11)

`ping` now answers `OK soundstage-addon 2`. The plugin reads that number and refuses, with a plain sentence, any command the add-on is too old
for (an older add-on also says `ERR unknown command: <verb>`, which the plugin turns into the same sentence).

| Request | Reply on success | Notes |
|---|---|---|
| `focus <uniform> <value>` | `OK focus <uniform>=<value>` | One float uniform, not negative. `ERR uniform not found`, `ERR <name> is not a float uniform`, `ERR bad number`. Not logged per call (auto focus sends several a second). |
| `restore` | `OK restored=<n>` | Puts every uniform the add-on changed (`uniform` or `focus`) back to the value it had the first time, then forgets them. |
| `unload` | `OK unloaded restored=<n>` | Restores like `restore`, then drops the connected client so the single pipe instance is free. Works before an effect runtime exists. |

ReShade can auto-save a changed uniform into the user's preset (`AutoSavePreset=1` under `[OVERLAY]`), which is why the plugin sends `restore` before it switches the preset back.

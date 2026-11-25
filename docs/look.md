# Look: ReShade presets per Scene, and auto focus

A Scene carries a look **slot**, a mood name such as `neon-night`, `soft-bloom`, `clean-bright`, `dark-moody` or `warm-grade`. It never carries a
ReShade preset, because shader packs differ. On the Look tab you map each slot to one of your own presets; a Scene from someone else just uses
whatever you mapped, and an unmapped slot does nothing (the tab says so).

## How it reaches ReShade

- **Add-on route (the main one).** `soundstage.addon64` talks to the plugin over a local named pipe and switches the preset, writes uniforms and
  sets focus. Build it with `python tools/build_addon.py`, then copy `reshade-addon/out/soundstage.addon64` next to `dxgi.dll` in the game folder
  and restart the game. Soundstage never copies it for you. Protocol: `docs/proof-reshade.md`, section 5.
- **Manual route (the fallback).** With no add-on the Look tab shows which preset to pick and a copy button. If you like, bind a shortcut key to one
  preset in ReShade's own settings and press it yourself. Soundstage does not send key presses and never edits `ReShade.ini`.

## Your own preset comes back

The first time a Scene changes the look, Soundstage reads your current preset and remembers it (also in its config, so a crash does not lose it).
Removing the Scene, applying a Scene with no look, unloading the plugin, or pressing Put it back on the Look tab restores it. The add-on also
puts back any uniform it changed.

**Auto-save.** If `ReShade.ini` has `AutoSavePreset=1`, ReShade may write a changed uniform into your preset file. Soundstage restores the old
values when the Scene comes off, but the Look tab shows a warning while auto-save is on; turning it off in ReShade's settings is the surest way.

## Auto focus

Name your depth-of-field shader's focus setting (the tab can list the real names from ReShade). Focus value = camera-to-head distance in metres
x Scale + Offset, kept between Lowest and Highest. Off by default for every Scene; a Scene turns it on with `"autoFocus": true` in its look block
or the checkbox on the Look tab. It needs add-on version 2 and the GPose camera and head reads from `GameAccess/`.

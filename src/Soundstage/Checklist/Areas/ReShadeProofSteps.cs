// No Dalamud types here (see IChecklistArea.cs). No buttons: these steps are "do this, see that".
namespace Soundstage.Checklist;

/// <summary>Part 3 of the Soundstage plan: the in-game half of the ReShade add-on proof (docs/proof-reshade.md).</summary>
public sealed class ReShadeProofSteps : IChecklistArea
{
    public string Id => "reshade";
    public string Name => "Part 3: ReShade add-on";
    public int Order => 30;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Add-on loads", "With the game closed, build the add-on (python tools/build_addon.py in the Soundstage folder), then copy reshade-addon/out/soundstage.addon64 into the FFXIV game folder, the one that holds dxgi.dll. Start the game and log in. Open ReShade.log in that folder and search for Soundstage. PASS = a line saying [Soundstage] add-on loaded, and another saying effect runtime ready. Also open ReShade's overlay (Home) > Add-ons tab and see Soundstage listed. FAIL = no such line (note whether ReShade.log mentions the file at all)."),
        new("2", "Plugin reaches the add-on", "Run /soundstage dev, open the ReShade probe tab and press Ping. PASS = OK: soundstage-addon 1 in green. FAIL = the red message about the add-on not answering (note whether step 1 passed)."),
        new("3", "Preset switch", "On the ReShade probe tab, press Current preset and note the name. Type the path of a different preset (like .\\reshade-presets\\<pack>\\<name>.ini, any real one that looks clearly different) and press Switch preset. PASS = the picture changes at once and Current preset now shows the new path. Switch back to your old one afterwards."),
        new("4", "Uniform write", "Press List uniforms and pick a name that looks like brightness, exposure or strength (any one that visibly changes the picture). Type it in Uniform, set Value to something very different from the default, press Set uniform. PASS = the picture changes and the probe says OK. The change is live only: switch to your preset again (step 3) to undo it. FAIL = OK but nothing changes, or a not-found error for a name that is in the list."),
        new("5", "Screenshot capture", "Stand somewhere with the UI visible, press Take shot (it writes to soundstage-probe.png in your Temp folder, or change the path). Open that file. PASS = it is a PNG of the game with the current ReShade look applied, the right size and the right colours (not blue/red swapped). Note whether the game's own UI is in it."),
        new("6", "Nonexistent preset is refused", "Type a preset path that does not exist (like .\\reshade-presets\\nope.ini) and press Switch preset. PASS = a red message saying the preset file was not found, and the picture and Current preset are unchanged."),
        new("7", "Key-press fallback", "In ReShade's overlay > Settings, bind a shortcut key to one preset (that sets PresetShortcutKeys in ReShade.ini; Soundstage never edits that file). Alt-tab to nothing else, click the game window so it has focus, and press the key by hand: PASS A = the preset switches. Then, only if you have a tool that can send a key to the game (or the Part 11 build has the button), try sending it and note whether ReShade ignored it. This step decides whether the key route stays as a documented manual option."),
        new("8", "Add-on unloads cleanly", "Close the game normally. PASS = it closes with no crash or hang within a few seconds and ReShade.log has no error line mentioning Soundstage."),
    };
}

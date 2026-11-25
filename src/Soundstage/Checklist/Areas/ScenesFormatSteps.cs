// No Dalamud types here (see IChecklistArea.cs). The buttons for these steps are in Checklist/AreaUi/ScenesFormatUi.cs.
namespace Soundstage.Checklist;

/// <summary>Part 4 of the Soundstage plan: the Scene format and the 15 refined and new Scenes, checked in game.</summary>
public sealed class ScenesFormatSteps : IChecklistArea
{
    public string Id => "scenesformat";
    public string Name => "Part 4: Scenes (format v2 and the catalogue)";
    public int Order => 40;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Apply each Scene and see if it looks like its description",
            "Press Next Scene, look at the result in game with your character in front of you, read the description, then mark that Scene. PASS means it looks like its description. FAIL needs a note that says which: too dark, too bright, wrong direction, or what else. Do all 15. The nine refined Scenes are Neon split, Neon wall (hard), Neon wall (soft), Rim only, Hair glow, Split light, Window light, Golden hour and High key. The six new rigs are Rembrandt, Butterfly, Loop, Clamshell, Silhouette and Low key. For Rembrandt, look for a small triangle of light on the cheek that is in shadow. Some Scenes also have a backdrop panel: it should appear behind you."),
        new("2", "Palettes",
            "Press each palette button. The lights should change colour and keep roughly the same brightness. PASS when every button recolours the Scene as named (Neon split has four palettes, Neon wall (soft) two, Rim only three). FAIL, with a note, if a palette goes much brighter or darker than the one before."),
        new("3", "Old preset names are gone, the Scenes are there",
            "In the Lighting tab the Scene list shows the new names, not the old ones. PASS when the Scene each old preset became is there (for example Soft window is now Window light) and the Mine group still lists your saved presets (Beta test look)."),
        new("4", "Mine carried over",
            "Open the plugin config folder (the Soundstage folder where user-presets.json is). PASS when user-scenes.json is there too, the old user-presets.json is still there, and Mine in the Lighting tab shows your saved look and applies it."),
        new("5", "Save keeps the palette",
            "Press Apply Neon split, Violet and amber, then Save as Part 4 test, then Reload scenes, then pick Part 4 test from Mine in the Lighting tab and Apply. PASS when it comes back violet and amber, not blue and red."),
        new("6", "Six lights",
            "Press Apply six-light test Scene. PASS when all six lights show (a ring of six different colours around you). FAIL, with a note of how many you can see, if fewer: the cap of six may then need to come down."),
        new("7", "Log lines",
            "Search dalamud.log for scenes:. There is a line saying how many Scenes loaded (18, plus anything in Mine, plus the built-in one). Every block (set, atmosphere, camera, look, pose and animation) is applied now, so there is no line saying a block is not applied. PASS when no line says a Scene was skipped and no line says a block is kept and ignored."),
    };
}

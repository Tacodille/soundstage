// No Dalamud types here (see IChecklistArea.cs). No buttons: every step is on the Scenes tab itself.
namespace Soundstage.Checklist;

/// <summary>Part 7 of the Soundstage plan: the Scenes tab (browse, apply, undo, snapshot, your own Scenes, share codes, pose and outfit), checked in game.</summary>
public sealed class ScenesUserSteps : IChecklistArea
{
    public string Id => "scenesuser";
    public string Name => "Part 7: Scenes tab (apply, undo, snapshot, share codes, pose and outfit)";
    public int Order => 70;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Browse, search and filter",
            "Open the Scenes tab (it is the first one). The list shows the 15 Scenes under their categories, plus the built-in one and anything in Mine. Type rim in the search box: only the rim Scenes stay. Clear it, then pick a Category and a Tag and watch the list narrow. PASS when search, Category and Tag each narrow the list and clearing them brings everything back."),
        new("2", "Apply, palette and Remove all",
            "Click Neon split in the list. Its description, best-in text and a Palette box show. Pick a different palette, press Apply, and look at your character. Press Remove all. PASS when the lights match the Scene and palette you chose and Remove all clears every light and the backdrop."),
        new("3", "Undo",
            "The Undo button shows how many steps it can go back. Press Apply on Neon split, then on Rembrandt, then on Golden hour. Press Undo: Rembrandt comes back. Press Undo again: Neon split. Now Apply a Scene, drag the key intensity in the Lighting tab, let go, wait a second, then press Undo: only the drag is undone. PASS when each Undo goes back exactly one step and Undo (0) is greyed out at the start."),
        new("4", "Snapshot and save as yours",
            "Apply Window light. In the Lighting tab change the key angle, distance and colour. Back on the Scenes tab press Snapshot now. A line says it was taken from the placed stage. Type Part 7 test in the Name box and press Save to Mine. PASS when Part 7 test is listed under Mine, and after Remove all and then Apply on it the lights come back as you tweaked them, not as Window light was."),
        new("5", "Snapshot looks the same as what was placed",
            "Apply Split light, press Snapshot now, save it as Same look, press Remove all, then Apply Same look. With light markers on, the markers sit in the same places as they did for Split light, and the face is lit the same way. PASS when you cannot tell the two apart (within about a hand's width on the markers). FAIL, with a note of which light moved, if one is clearly off."),
        new("6", "Unsaved snapshot asks first",
            "Press Snapshot now and do not save. Press Apply on a different Scene. A box asks about the unsaved snapshot. Press Keep the snapshot: nothing changes. Press Apply again and choose Apply anyway: the Scene applies and the snapshot is gone. PASS when both buttons do what they say."),
        new("7", "Rename and delete your own",
            "Select Part 7 test under Mine. Change the name next to Rename and press Rename: the list shows the new name. Press Delete: a box asks, and Cancel keeps it. Press Delete again and confirm: it leaves Mine. Shipped Scenes show neither button. PASS when all of that holds and user-scenes.json in the plugin config folder still opens as plain JSON."),
        new("8", "Share code to another copy",
            "Apply a Scene, tweak it, Snapshot now and save it as Code test. With it selected press Copy share code. Paste into Notepad: it starts with ss1: and has no key or file path in it. Press Remove all, delete Code test from Mine, paste the code into the box on the Scenes tab and press Read code. A list shows what it holds. Save it as Code test 2 and Apply it. PASS when it comes back looking like what you copied. Bonus, if second has this build: paste the same code there too and note that it applies."),
        new("9", "A bad code is refused, not a crash",
            "Paste each of these in turn and press Read code: ss1:abc, then a normal sentence, then a real code with its last few characters cut off. PASS when each gives one plain line saying why, nothing is saved, and the game and the window carry on normally."),
        new("10", "Pose and outfit block",
            "Open user-scenes.json in the plugin config folder (close nothing, copy it first). On one of your Mine Scenes add a pose block, for example \"pose\": { \"posePack\": \"<a pack from your pose shelf>\", \"poseName\": \"<a pose in it>\", \"glamourerDesign\": \"<a design in Glamourer>\" } (keep the commas right), save, then press Reload Scenes. Select that Scene: a Pose and outfit section lists what it would do. Enter GPose. Press Apply WITHOUT the tick: your pose and outfit must not change, and a line says the block was left alone. Tick Apply pose and outfit and press Apply again. PASS when the pose loads, the design is applied, and the first Apply changed nothing."),
        new("11", "Penumbra mod settings",
            "Add \"penumbraMods\": [ { \"modDirectory\": \"<folder name of one of your Penumbra mods>\", \"modName\": \"<its name>\", \"enabled\": true, \"options\": { \"<a group name>\": [ \"<an option name>\" ] } } ] to the same pose block, Reload Scenes, tick the box and Apply. In Penumbra, open that mod: it is enabled and the option is the one you named (settings are saved in your collection, which the tab says). PASS when the option matches. FAIL, with a note, if the group or option is wrong or untouched: that points at the order of arguments in Soundstage's Penumbra call."),
        new("12", "A missing plugin is said plainly",
            "If you are happy to, switch Glamourer off in Dalamud's installed plugins for a moment, press Apply on the Scene from step 10 with the tick on, then switch it back on. PASS when a line under the buttons says the Glamourer design was skipped because Glamourer is not running, and everything else still applied. Leave this step as Skipped, with a note, if you would rather not turn it off."),
        new("13", "Log lines",
            "Search dalamud.log for [Soundstage/SCENES]. There are lines for each Apply with its undo depth, the tweak you made, each Undo, saves, and the pose block lines from steps 10 to 12. PASS when they are there and none of them contains a key, a password or a file path."),
    };
}

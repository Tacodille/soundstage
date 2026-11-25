// No Dalamud types here (see IChecklistArea.cs). Live read-outs and the Rescan button are in Checklist/AreaUi/PosesStepsUi.cs.
namespace Soundstage.Checklist;

/// <summary>
/// Part 5 of the Soundstage plan: the in-game half of "Poses shelf proved in game". Every step is one row of the plan's
/// poses checklist. Each action also writes a [Soundstage/BRIO] line to dalamud.log, so search for that as well as the
/// [Soundstage/TEST] result lines.
/// </summary>
public sealed class PosesSteps : IChecklistArea
{
    public string Id => "poses";
    public string Name => "Part 5: Poses shelf";
    public int Order => 50;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Brio ready", "Filled in automatically. Brio 0.8.x has to be loaded with IPC switched on (Brio settings, IPC). If this fails, nothing else in this area can pass: fix it and press Recheck Brio. PASS looks like 'Brio ready' with an API version."),
        new("2", "Pose list loads", "Open the Poses tab. The list should show your pose packs, and the summary beside Rescan should read something like 67 pose file(s) in 18 packs on your own folders. Press Rescan once. PASS: the count comes back the same and the plugin did not hitch."),
        new("3", "Click loads on the target", "Enter GPose and pick a target (or leave it on yourself). In the Poses tab tick Body and Expression, then click one pose. PASS: the actor takes the pose and the status line says Loaded with the actor's name. Dalamud log gets a [Soundstage/BRIO] LoadPoseFromFile line ending in True."),
        new("4", "Body only", "Untick Expression, tick Body, click a pose that has a face in it. PASS: the body moves into the pose and the face stays as it was. The log line is a LoadPoseFromJson one with expression=False."),
        new("5", "Expression only", "Tick Expression, untick Body, click a pose with a face. PASS: only the face changes and the body stays put. The log line is a LoadPoseFromJson one with body=False."),
        new("6", "A .cmp row loads as body", "Find a row marked (.cmp) (greyed). With Body ticked, click it. PASS: the body takes the pose. Hover it: the tooltip says .cmp is body only. With Body unticked the status line should say to tick Body instead of loading anything."),
        new("7", "Hover preview, then restore", "Tick Hover preview. Rest the mouse on a row for about half a second: the actor should show that pose. Move to another row, then off the list without clicking. PASS: the pose from before the preview comes back. Repeat, then collapse the Poses header or switch to the Lighting tab while previewing: the old pose should come back then too. Note anything that sticks or flickers."),
        new("8", "Favourites and recently used", "Star two poses. They appear under Favourites at the top. Click two other poses: they appear under Recently used, newest first. Reload the plugin or restart the game; the favourites and recent list should still be there."),
        new("9", "GPose only message", "Leave GPose. The Poses tab should say 'Brio ready ... Enter GPose to load poses', and clicking a pose should load nothing and say 'Not in GPose'. Hovering should not change your character. PASS: nothing moved."),
        new("10", "Log lines", "Search dalamud.log for [Soundstage/BRIO]. PASS: there is a line for every load and every restore you did in steps 3 to 7 (LoadPoseFromFile, LoadPoseFromJson, GetPoseAsJson), none ending in EXCEPTION, plus one 'scan done' line. Put anything odd in the note."),
        new("11", "Brio undo history under hover", "Optional. Hover over about ten different rows in a minute, then open Brio's undo history. If it holds a flood of entries (a new one for every row you passed over), say so in the note and Fail: a hover delay setting is the fix. If it holds a few or none, Pass."),
        new("12", "Tags", "Right-click a pose row, type a tag (for example sitting) and press Enter. The row shows [sitting] after its name. Type sit in the tag filter next to Search: only tagged poses stay. The tags drop-down lists what you have used. Then rename that pose file in Explorer and press Rescan: the tag should still be on it (tags follow the file contents, not its name). PASS: all of that. They live in pose-tags.json in the plugin config folder."),
    };
}

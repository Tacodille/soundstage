// No Dalamud types here (see IChecklistArea.cs). No buttons: these steps are "do this, see that".
namespace Soundstage.Checklist;

/// <summary>Part 1 of the Soundstage plan: the in-game half of its validation table (rename, migration, tabs).</summary>
public sealed class RenameSteps : IChecklistArea
{
    public string Id => "rename";
    public string Name => "Part 1: rename, migration, tabs";
    public int Order => 5;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Loads as Soundstage", "In /xlsettings > Experimental > Dev Plugin Locations, replace the old Keylight path with the full path to your Soundstage.dll (the build output folder), enable it in Dev Tools, then run /soundstage. The window opens, titled Soundstage, with Lighting, Set and Poses tabs."),
        new("2", "Old settings and Mine presets carried over", "On the first start the selected preset is the one you had (Soft window), Follow player and the pose folder are as you left them, and Mine lists your saved presets (Beta test look)."),
        new("3", "Log lines", "Search dalamud.log for [Soundstage/. There are lines, and on the first start one [Soundstage/MIGRATE] line saying your Keylight settings were carried over."),
        new("4", "Tabs behave as before", "Lighting: apply a preset and drag the key sliders. Set: the backdrop fixes and the canvas test. Poses: the shelf lists your poses and a hover preview still works, and the pose goes back when you move to another tab. /soundstage dev shows a Tools tab."),
    };
}

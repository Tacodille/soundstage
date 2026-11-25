// No Dalamud types here (see IChecklistArea.cs). The buttons for these steps are in Checklist/AreaUi/CoreStepsUi.cs.
namespace Soundstage.Checklist;

/// <summary>
/// The ten steps of Keylight's beta test, ported for Soundstage. Step ids stay "1" to "10". Results are saved under
/// "core/1" and so on, so Keylight's old results (keys "1", "3:neon-green-rim") count for nothing here.
/// </summary>
public sealed class CoreSteps : IChecklistArea
{
    public string Id => "core";
    public string Name => "Core: lights, backdrop, canvas, presets";
    public int Order => 10;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Stagehand connected", "Filled in automatically from the availability check. The API revision goes in the note."),
        new("2", "Smoke test", "Press Place smoke test. A red light should appear in front of you."),
        new("3", "Apply each preset", "Press Next preset, look at the result, then Pass or Fail that preset. Repeat until every preset has a result."),
        new("4", "Live update in GPose", "Enter GPose, apply a preset, then drag Key intensity and Key angle in the Lighting tab. The light should change as you drag."),
        new("5", "Markers", "Dots should sit on the lights. Scroll Lock should hide them. /soundstage markers should toggle them."),
        new("6", "Backdrop", "Apply a preset that has a backdrop: a coloured panel should appear behind you. Note whether the colour, size and facing are right (the Backdrop section in the Set tab can fix size, distance, yaw and height)."),
        new("7", "Picture canvas", "Press Place canvas test. The portrait should show the colour grid with TOP at the top, red top-left, blue top-right, the arrow pointing up."),
        new("8", "Save a custom preset", "Press Save test preset, then turn Soundstage off and on in Dev Tools. It should still be in Mine and apply."),
        new("9", "Zone change", "With lights (and a backdrop) showing, teleport or change area. They should come back by themselves."),
        new("10", "Remove all", "Press Remove all. Every light, the backdrop and the canvas should disappear."),
    };
}

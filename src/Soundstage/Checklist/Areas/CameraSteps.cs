// No Dalamud types here (see IChecklistArea.cs). Pure data: do this, see that.
namespace Soundstage.Checklist;

/// <summary>Part 10 of the Soundstage plan: the Camera tab's in-game half (framing presets, turntable, overlay).</summary>
public sealed class CameraSteps : IChecklistArea
{
    public string Id => "camera";
    public string Name => "Part 10: Camera (framing, turntable, thirds)";
    public int Order => 100;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Camera buttons wait for GPose",
            "Outside GPose, open the Camera tab. The framing buttons, sliders and turntable buttons are greyed out and a line says you are not in GPose. Then enter GPose: they wake up. PASS means greyed outside GPose and live inside it, with nothing changing on your camera while greyed."),
        new("2", "Six framing presets, front view",
            "In GPose with your character facing you, press each framing button in turn: Full body, Three-quarter, Waist up, Chest up, Portrait, Close-up. Each should land with the lens in front of your character's face side, looking at them, and each should be closer than the one before. PASS means all six work and each is closer. If the camera lands behind your character or off to a side, do not mark FAIL yet: open 'If the camera lands on the wrong side' on the tab and set Yaw trim to 180 (or 90 or -90 for a side), press a framing button again, and note the trim that worked. Mark PASS with that trim in the note. FAIL if no trim fixes it."),
        new("3", "Camera height and tilt",
            "Press Full body, then Close-up. Full body should look at your character from roughly chest height with the whole body in frame, and Close-up should be level with the face. If the camera tilts the wrong way (looking up at the jaw when it should look down a little, or the reverse), tick 'Flip the tilt' on the tab and press the button again. PASS with a note saying whether you needed the flip. Also note if the camera looks at the wrong height (feet or well above the head) with a one-line description."),
        new("4", "Angle slider and height trim",
            "Pick Chest up, drag 'Angle round the subject' to +90, then -90. The camera should swing to your character's right side, then left, following live as you drag. Drag 'Camera height trim' up and down: the camera rises and falls. PASS means both sliders move the camera as described."),
        new("5", "FOV slider",
            "With the FOV box ticked, drag 'FOV added' from 0 to +0.4 and back. The picture should zoom or widen smoothly and come back at 0. PASS means it changes the view and returns. If nothing moves, or the picture goes strange, untick 'Write the FOV slider too', mark FAIL and say which. The FOV is written to a memory offset (0x13C) that Part 2's probe was meant to confirm, so this step is the confirmation."),
        new("6", "Game limits respected",
            "Press Close-up and then Full body. The camera should never go inside your character or flip upside down. Open dalamud.log and search for [Soundstage/CAM]: each press logs one 'framing' line ending in OK. PASS means no flips and OK on every line."),
        new("7", "Turntable",
            "Pick Three-quarter, set Seconds per lap to 12, press Start (clockwise). The camera circles your character at a steady speed and a full lap takes about 12 seconds. Press Stop: it stops where it is. Start again, then Stop and put the camera back: the camera returns to where it was before. Press Start (other way): it circles the other direction. PASS means all four behaviours. Note the lap time you measured."),
        new("8", "Turntable cancels on leaving GPose",
            "Start the turntable, then leave GPose. The turntable stops (the tab says GPose ended) and nothing moves your normal game camera afterwards. Enter GPose again: the camera is not still spinning. PASS means it stopped and stayed stopped."),
        new("9", "Thirds overlay",
            "Tick Thirds on the tab: a 3 by 3 grid draws over the whole game view. Tick Golden ratio (amber lines at 38 and 62 per cent), Centre cross, and Safe margin (a box inset from the screen edge; move the percentage slider and the box moves). PASS means each switch shows its lines and unticking it removes them."),
        new("10", "Overlay hides with the game UI",
            "With Thirds and Safe margin on, press Scroll Lock (or the GPose hide-UI toggle). The overlay disappears along with the game UI and the light markers, and a screenshot taken now has no lines. Press it again: the lines return. PASS means it hides and returns with the game UI."),
        new("11", "Scene with a camera block",
            "Tick 'Apply camera when a Scene is applied'. In GPose apply a Scene that has a camera block (Neon split is chest-up, Window light is waist-up, Rim only is side-on and Silhouette is full body from the right). The camera moves to that Scene's framing on apply. Untick the box and apply another Scene: the camera does not move. PASS means it moves when ticked and stays put when unticked. Outside GPose, applying a Scene still works and the log says the camera was skipped."),
        new("12", "Save into Scene",
            "Apply any Scene, pick Waist up, set the angle to 35, press 'Save into Scene'. The note beside the button says it was saved. Apply the same Scene again from the list (not saved to file): this step only checks the button does not error. PASS means the note appears and the log has no exception."),
    };
}

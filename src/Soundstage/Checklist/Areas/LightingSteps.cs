// No Dalamud types here (see IChecklistArea.cs). The Lighting tab itself carries the controls these steps use.
namespace Soundstage.Checklist;

/// <summary>
/// Part 8 of the Soundstage plan: the in-game half of "Lighting: face aim, animation, gels, exposure". The offline checks cover
/// the maths; these steps cover what only the game can show. Frame costs are written to dalamud.log under [Soundstage/ANIM].
/// </summary>
public sealed class LightingSteps : IChecklistArea
{
    public string Id => "lighting";
    public string Name => "Part 8: Lighting";
    public int Order => 80;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Every light has controls", "Open the Lighting tab, pick Clamshell and press Apply. The tab should list both lights (Key and Low fill), each under its own header with Intensity, Colour, Gel, Angle, Distance, Height and a Follow the face box. PASS: moving any slider on either light changes that light in the game as you drag, with no flicker of the whole stage."),
        new("2", "Face aim follows the head (Rembrandt)", "Pick Rembrandt, Apply, and stand still, not in GPose. The line under 'Lights in Rembrandt' should say 'following the head bone'. PASS: the little cheek triangle of light shows on the cheek away from the key. Now crouch or sit with an emote: after a moment the key should drop with your head and the triangle should stay on the cheek. Note the head height the line shows."),
        new("3", "Face aim: tick and untick", "On Rembrandt, untick Follow the face on the Key. Crouch: the key should stay at its fixed height. Tick it again: it should follow you. PASS if both behave. FAIL if ticking does nothing."),
        new("4", "Face aim fallback is said out loud", "If you can make the head read fail (for example right after a zone change before the model loads), the line should say it is using the Scene's own aim heights, and the lights should sit where the Scene put them. If the head read never fails, write 'could not trigger' in the note and Pass."),
        new("5", "Animated Scenes play", "Apply Campfire flicker: the orange light should flicker and drift, the moon should stay steady. Apply Neon sign flicker: the pink key and the wall behind should dip together. PASS: both visibly animate and the game stays smooth. Remove all afterwards."),
        new("6", "Strobe is off, then on", "Apply Club strobe. PASS: nothing flashes yet, the strobe line in the Animation section is unticked, and the photosensitivity note is showing. Tick 'Strobe on kicker'. PASS: the white light flashes about twice a second. Drag 'per second' to the top: it must stop at 3. Untick it: the flashing stops at once."),
        new("7", "Play animation box", "On Campfire flicker, untick Play animation: the flame freezes (and the stage keeps its lights). Tick it again: it resumes. PASS if both."),
        new("8", "Animation cost numbers", "On Campfire flicker, press 'Measure frame cost (10 s)' and leave the game alone for ten seconds. The tab then shows the frame interval with animation OFF and ON and the whole-stage re-send cost. PASS: you have the numbers; write them in the note (average and worst for OFF and ON, and the re-send average). They are also in dalamud.log under [Soundstage/ANIM]. If the ON average frame interval is more than about 2 ms above OFF, or the re-send averages over 4 ms, say so: that is a Fail, and the Most re-sends per second slider is the first fix."),
        new("9", "Throttle slows itself down", "While Neon sign flicker plays, drag 'Most re-sends per second' to 15. The line under it says what rate it is really running at. PASS: if re-sends are cheap it stays at 15, and if they are not, the line shows a lower rate and the log has a 'throttle now' line. Put which one happened in the note."),
        new("10", "Gels and the colour box", "On any Scene, open a light's Gel list and pick Amber. PASS: the light turns amber in the game and the Colour box agrees. The range number beside Follow the face changes if the brightest channel changed."),
        new("11", "Colour harmony", "On Neon split, set Harmony to Triadic, leave the base colour, press Make palette. PASS: the three lights take three hues 120 degrees apart, the message says it made the palette, and the brightness looks about the same as before. Try Complementary and Analogous too."),
        new("12", "Exposure badges", "On any Scene each light header ends with a coloured badge: green 'ok', amber 'likely too hot', red 'will clip', with a value and the distance to your face. Raise a light's Intensity to 20 and watch it go amber, then red. PASS: the badge moves with the slider and with Distance."),
        new("13", "Calibrate the exposure threshold", "Apply High key, Neon wall (hard) and Loop, one at a time. For each, raise the key's intensity until the face just starts to look blown out in the game, and write the value the badge shows at that moment. Then set 'Amber from' a little below the lowest of the three and 'Red from' at about where it clips. PASS: you have settled on two numbers; write them in the note (Amber from, Red from). They are saved with the plugin config."),
        new("14", "Markers and saving", "Tick Show light markers: the light markers show for every light. Tweak two sliders and a harmony palette, press Save current as with a name, then pick it under Mine and Apply. PASS: your tweaks and the harmony colours come back, and a face-aimed light is still face-aimed."),
    };
}

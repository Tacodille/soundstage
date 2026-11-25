// No Dalamud types here (see IChecklistArea.cs).
namespace Soundstage.Checklist;

/// <summary>
/// Part 9 of the Soundstage plan: the in-game half of the Set tab. Every row of the plan's Part 9 validation that needs the
/// game is a step here, and so is the canvas test that was never run in the Keylight beta. Everything the Set tab builds
/// writes a [Soundstage/SET] line to dalamud.log, so search for that as well as the [Soundstage/TEST] result lines.
/// </summary>
public sealed class SetSteps : IChecklistArea
{
    public string Id => "set";
    public string Name => "Part 9: Set (sweep, kits, atmosphere, picture)";
    public int Order => 90;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Sweep looks seamless",
            "Stagehand has to be loaded. Apply Neon wall (soft), or any Scene with a sweep, then open the Set tab and check the Backdrop type is Sweep. Stand with the camera in front of you, then pan across the whole backdrop and look at where the wall meets the cove and the cove meets the floor. PASS: one smooth colour from wall to floor with no gap, no dark line and no flicker where the pieces join, and the floor does not shimmer against the ground. FAIL (with a note saying which join): a gap, a line, a flicker, or a piece that faces the wrong way. If it fails, set the type to Panel and the old single panel comes back."),
        new("2", "Sweep colour, width, distance and cove",
            "In the Set tab drag Width to the far end, then back to about 8. Drag Distance behind you and Cove size. Pick a new Sweep colour with the colour square, then press Use palette colour. PASS: the sweep follows each control when you let go of the slider, the colour matches the swatch, Use palette colour brings back the Scene's own backdrop colour, and the pieces still join at the widest setting. Also switch the palette on the Lighting tab (Neon wall has two palettes) and PASS if the sweep recolours with it while Use palette colour is on."),
        new("3", "Prop kits",
            "In the Set tab tick each kit in turn: Lounge corner, Library, Interview chairs, Greenery, Crates and barrels, Fireside, Throne room. Look at each from the camera. PASS for a kit means it appears, stands on the ground (nothing floating or sunk), is the right size and faces the camera or each other. If a kit stands there but faces the wrong way, press its Flip 180 button and note which kit needed it. FAIL a kit that does not show, is the wrong size or looks bad. In the note, list the kits that look right and the kits to drop. Try Across and Back/forward on one kit: it should slide as a whole. Untick it and the kit goes."),
        new("4", "Atmosphere items",
            "In the Set tab, Atmosphere, tick each item one at a time and look or listen: Haze, Light shafts, Sun glow, Dust, Falling leaves, Soft glow, then the three Room music items. PASS for an item is that you can see or hear it, it sits where it should (behind or around you) and the Size or Volume slider changes it. FAIL an item that does not show, plays once and stops, looks wrong or is too loud. List every item with its result in the note, because failed items are dropped from the catalogue. Music: a track that plays over the game's own music is fine; note if it stops when you untick it. Dust may only puff once."),
        new("5", "A Scene carries its set",
            "Tick one kit and one atmosphere item, set a sweep colour, then Save current as Part 9 test on the Lighting tab, Reload scenes, apply Part 9 test from Mine. PASS: the sweep, the kit and the atmosphere item all come back as they were. Then untick Show backdrop on the Set tab: the sweep goes and the lights stay."),
        new("6", "Canvas test (the one the Keylight beta never ran)",
            "On the Set tab open Canvas test and press Canvas test (portrait). A portrait should appear about 1.5 m front left of you at head height showing a coloured grid with TOP and BOTTOM, an up arrow, L on the left edge and R on the right, red top left and blue top right. PASS needs all four: the grid texture shows instead of the Admiral (the texture swap works), TOP is at the top and L is on the left as you look at it (orientation correct, not mirrored or turned), the normal and specular look acceptable (it is not black or glossy mess), and the frame still looks fine. If it faces away or is sideways, use the Canvas yaw offset buttons and note which value fixed it. FAIL, with a note, if the texture does not change or is mirrored. If this fails, the picture prop is dropped and the picture control should be ignored."),
        new("7", "Picture prop (only if step 6 passed)",
            "In the Set tab, Picture, type the full path of a PNG or JPG (a photo works), press Hang it. PASS: your picture shows on the canvas the right way up and not mirrored, in its own shape (a wide photo is wide), the status line says the pixel size (at most 1024 on the long side), and the Picture height, Across, Back/forward, Centre height and Extra turn sliders move it. Take it down removes it. Save a Scene with the picture, reload it, apply it: the picture comes back while the file is still there. Skip this step if step 6 failed."),
        new("8", "Zone change keeps the set",
            "With a sweep, a kit and an atmosphere item up, change zone (or enter and leave GPose, or relog and press Apply again). PASS: Soundstage re-sends the whole set and it comes back. Stagehand drops temporary stages on a zone change and Soundstage puts them back; note if only part of the set returns."),
        new("9", "Log lines",
            "Search dalamud.log for [Soundstage/SET]. PASS: there is a line saying how many kits and atmosphere items loaded (7 kits and 9 items for the shipped files), and a line each time you applied or changed a Scene that says what it built (for example sweep 8 piece(s); kit lounge 4 object(s); atmosphere 2 item(s)), with no exception lines. Put anything odd in the note."),
    };
}

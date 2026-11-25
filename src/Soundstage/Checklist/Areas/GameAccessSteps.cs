// No Dalamud types here (see IChecklistArea.cs). The steps use the Probe tab (type /soundstage dev, then open the Probe tab).
namespace Soundstage.Checklist;

/// <summary>Part 2 of the Soundstage plan: the in-game half of the head-bone and GPose camera proof.</summary>
public sealed class GameAccessSteps : IChecklistArea
{
    public string Id => "gameaccess";
    public string Name => "Part 2: head bone and GPose camera";
    public int Order => 20;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Probe tab and installed types", "Run /soundstage dev, open the Probe tab and press \"List the types and fields this proof uses\". PASS is every entry reading yes, with the FFXIVClientStructs version shown at the start. Put the version and the game patch in the note."),
        new("2", "Head position read (route A)", "Stand still, logged in (GPose not needed). Press \"Read head (route A: skeleton)\". PASS is a head position about 1.4 to 1.9 m above the player position for a Miqo'te or Midlander, and a [Soundstage/PROBE] READ head line in dalamud.log. Put the height in the note. FAIL if it says unavailable or the height is wild."),
        new("3", "Head read follows the pose", "Tick Live read-outs, then crouch with an emote (or sit). The head height drops by a clear amount and comes back when you stand. PASS if it tracks you."),
        new("4", "Route B (Brio pose JSON)", "Press \"Check route B (Brio pose JSON)\". Write what the line says in the note: whether j_kao is present and whether it has a Position. PASS means the check ran and the answer is recorded, whichever way it came out (a Brio pose holds parent-relative values, so \"local only\" is an expected answer)."),
        new("5", "Camera read", "Enter GPose. Press \"Read camera\". PASS is a position, yaw, pitch, distance and FOV, and a READ camera line in dalamud.log."),
        new("6", "Camera read changes", "Still in GPose, move the camera by hand (drag it round and zoom), then press \"Read camera\" again. PASS if the position, yaw or distance changed to match what you did."),
        new("7", "Camera orbit write (yaw)", "Press \"Write orbit\" with 30 degrees. PASS if the frame swings round the subject by about 30 degrees and you can still move the camera by hand afterwards. Check the WRITE orbit line shows ok=True."),
        new("8", "Camera pitch write", "Press \"Write pitch\" (15 degrees). PASS if the camera tilts and you can still move it by hand."),
        new("9", "Camera FOV write", "Press \"Write FOV nudge\". PASS if the picture zooms a little (the GPose FOV slider in the game's own menu should move too). Say which way it went in the note."),
        new("10", "Camera position write", "Press \"Write position (lift)\" and wait about half a second. The status line then says whether the game kept the camera position or put it back, and a POSITIONCHECK line goes to dalamud.log. PASS means you saw an answer either way. Put \"kept\" or \"put back\" in the note, because Parts 10 and 16 depend on it."),
        new("11", "Restore", "Press \"Restore\". PASS if the camera goes back to where it was before the first write, and the RESTORE line shows ok=True. Then write again, leave GPose without restoring and re-enter: the camera is the game's own, not your write."),
        new("12", "Refuses outside GPose", "Leave GPose. Press \"Write orbit\". PASS if it says you are not in GPose, nothing moves, and the camera is untouched."),
        new("13", "Log evidence", "Search dalamud.log for [Soundstage/PROBE]. PASS if there is a READ line, a READ line with different values after you moved the camera, a WRITE line and a RESTORE line."),
    };
}

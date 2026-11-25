// No Dalamud types here (see IChecklistArea.cs). Pure data: do this, see that.
namespace Soundstage.Checklist;

/// <summary>Part 16 of the Soundstage plan: the Face tab's in-game half (helper download, head angle to camera, light from shading, error table).</summary>
public sealed class FaceSteps : IChecklistArea
{
    public string Id => "face";
    public string Name => "Part 16: Face (head angle and light from a photo)";
    public int Order => 160;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Download the face helper",
            "Open the Face tab. It says the face model is not on this computer yet and shows a size of about 122 MB. Press 'Download the face helper'. A bar counts up through each file (Python, the packages, then Google's face model) and ends on 'Face helper installed'. PASS means the download finishes, the tab says 'Face helper installed' with a size of about 330 MB, and the Download button has gone. FAIL if a file says it failed its hash check (note which file). Needs the internet for this one step only."),
        new("2", "Analyse a picture",
            "Paste the full path of any portrait photo (png or jpg) into the box and press 'Analyse picture'. The first one takes a few seconds while the model starts; the tab then shows yaw, pitch and roll in degrees, a framing confidence, a line of words for the head angle and the strongest expression scores. Press it again on the same picture: it should come back almost at once. PASS means numbers appear and the second run is quick. Note the first run time."),
        new("3", "Sensible failures",
            "Press 'Analyse picture' with a path that does not exist, then with a picture that has no face in it (a landscape or a blank screenshot). Each time the tab shows one plain sentence ('That picture file was not found' or 'No face found in the picture') and nothing else breaks. PASS means plain sentences and the game carries on normally."),
        new("4", "Head turn reads the right way round",
            "Analyse a portrait where the person's face turns clearly toward the right-hand side of the picture. Yaw should be positive and the words say 'toward the picture's right'. Analyse one turned to the left: yaw negative. PASS means the sign follows the way the face points on screen. FAIL if it is the wrong way round."),
        new("5", "Match moves the camera (in GPose)",
            "Enter GPose with your character and apply any Scene. On the Face tab pick a portrait where the face is turned about 30 degrees toward the picture's right and press 'Match head angle and light (local, nothing uploaded)'. The camera moves to the matching framing and sits round on your character's right side by about 30 degrees, and the tab prints a line saying so. PASS means the camera moves and the side is right. If the side is wrong, note which, and use the Yaw trim on the Camera tab to see whether that fixes it."),
        new("6", "Match aims the key light",
            "With a Scene on stage, press Match on a portrait lit strongly from one side. The tab says which way it thinks the light comes from and sets your key light's angle and height to match (the note ends 'Key light set to ...'). The stage updates. PASS means the key light moves to the side the photo is lit from. Note the picture and which way it moved."),
        new("7", "Outside GPose",
            "Leave GPose. Press Match again. The camera is not moved (the 'Move camera to match' button is greyed with the GPose reason) but the tab still prints what the camera should do, and the light still aims. PASS means no camera change outside GPose and a plain note instead."),
        new("8", "Light error table (five angles)",
            "Pick one Scene. For each key azimuth in turn (-60, -30, 0, +30, +60 degrees, set on the Lighting tab or by editing the Scene), put your character in front of the camera, take a screenshot in GPose (the game's own screenshot, or the Workflow tab's capture if it is there), then run 'Analyse picture' and 'Match' on that screenshot. For each, write the angle you set and the light angle the Face tab reports into this step's note, like '-60: -41, -30: -22, 0: 3, 30: 24, 60: 38'. PASS means all five are recorded, whatever the error. Larger errors are fine, they are the table. The plugin does not claim more than this table."),
        new("9", "Helper stops by itself",
            "After a few analyses, leave the game alone for about two minutes, then open Task Manager and look under Details for python.exe. None of them should belong to the plugin's face-helper folder. The Face tab's 'Stop helper now' button stops it at once. PASS means no leftover helper after the idle time."),
        new("10", "Remove it",
            "Press 'Remove face helper'. The tab goes back to the 'not on this computer yet' message and the folder 'face-helper' inside the plugin's config folder is gone. Download it again (step 1) if you want to keep using the tab. PASS means it removes cleanly."),
    };
}

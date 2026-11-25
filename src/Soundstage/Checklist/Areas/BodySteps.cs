// No Dalamud types here (see IChecklistArea.cs). Pure data: do this, see that.
namespace Soundstage.Checklist;

/// <summary>Part 18 of the Soundstage plan: copying a photo's body pose onto your character through Brio.</summary>
public sealed class BodySteps : IChecklistArea
{
    public string Id => "body";
    public string Name => "Part 18: Body pose (copy a photo's pose)";
    public int Order => 180;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Greyed out with a reason",
            "Open the Expression tab and scroll to 'Body pose'. Before you have the face helper (Face tab) it says the body model needs the face helper first. With the helper but not the body model, 'Read body photo' and 'Match pose' are greyed and a line says the body model is not downloaded yet. Outside GPose, 'Match pose' and 'Check the body map' are greyed and a line says 'Not in GPose'. PASS means every greyed button has a plain reason beside it and nothing throws."),
        new("2", "Download the body model",
            "With the face helper installed, press 'Download the body model' (about 9 MB). A bar fills, then the line reads 'Body model on this computer (9.0 MB)'. In the plugin's config folder, inside 'face-helper', the 'model' folder now holds 'pose_landmarker_full.task' and there is a 'pose.json' next to 'model'. PASS means the file is there at about 9 MB and the buttons are no longer greyed for that reason. Press 'Remove body model' and download again to check that both work."),
        new("3", "One helper, not two",
            "Open the Face tab and read a portrait, then open the Expression tab and read a body photo. Open Task Manager and look for python.exe processes whose path ends in face-helper\\python. PASS means exactly one (it stops by itself after two minutes idle). FAIL if two are running at once: that means the Face and Expression tabs are not sharing one helper."),
        new("4", "Check the body map",
            "Enter GPose with your character standing in Brio's default pose and press 'Check the body map'. A paragraph appears. PASS means it says every mapped bone is in the pose, that all (or all but one) of the checked segments point the way the map expects, and ends 'The map fits'. The same numbers go to dalamud.log as lines starting '[Soundstage/BODY] rest': copy them into this step's note. FAIL if it lists missing bones (write their names down: the map in body-map.json needs those names) or says the map does NOT fit (note which segments it names). Then pose your character oddly in Brio and press it again: it should now complain."),
        new("5", "Read a full-length photo",
            "Paste the full path of a photo of one person standing in full view (head to feet, not cropped, arms visible) and press 'Read body photo'. PASS means the line reads 'Photo read: N of 16 joints clear enough to copy' with N at 12 or more. Try a photo that is cropped at the knees: N drops and the legs are left alone later. Try a photo with no person in it: it says no body was found."),
        new("6", "Match one photo",
            "With the photo read and your character in GPose, press 'Match pose'. The camera moves to the full-body framing, the Soundstage window blinks away for each picture, and a line counts through up to four passes, then says 'Outline score N before, M after'. PASS means your character's arms, legs, spine and neck visibly move toward the photo's pose, M is higher than N, and the average limb direction error line is lower after than before. Write the two scores and the two errors in this step's note. If the character is the mirror image of the photo, tick Mirror under 'Body tuning', Undo, and run it again."),
        new("7", "Limits hold",
            "Match a photo with a hard pose (an arm reaching straight up, a deep crouch, a high kick). The character should reach as far as it safely can and stop: no elbow or knee bent backwards, no arm through the chest, no leg twisted round. The result line says how many segments were held at a joint limit. PASS means the result looks like a body even if the match is only partial. FAIL if a joint breaks: note which one, and the pose; the limit for it is in body-map.json."),
        new("8", "Face, hair and ears untouched",
            "Before step 6, give your character a clear expression and note the hair and ears. After the match, look at the face again. PASS means the expression, hair and ears are exactly as they were (the match only loads body bones). The head still looks where it looked: the neck turns the head with the spine, but the face is not changed."),
        new("9", "Undo and cancel",
            "After a match press 'Undo (put the old pose back)': your old pose returns. Start another match and press 'Cancel body match' during the first passes: the old pose returns at once and the status says so. Ctrl+Z in Brio also steps back, because each result is one pose load. PASS means undo and cancel both restore the starting pose."),
        new("10", "The picture is not left behind",
            "After any match, open the plugin's config folder and look for 'body-shots'. PASS means the folder is gone or empty (each screenshot is deleted after it is read) and no body photo or screenshot is kept anywhere by the plugin."),
        new("11", "Five photos, five ratings",
            "Match five different full-length photos. After each, rate how recognisable the pose is from 1 (not at all) to 5 (spot on) with the buttons under the result. Copy each score and rating into the body table in docs/expression-trials.md (photo description, outline score, rating). PASS means five rated rows are recorded. If the median of the five is under 3, the body section marks itself EXPERIMENTAL and shows only a tick box to switch it back on: check that this happens, and that the face section above it is not hidden by it."),
        new("12", "Another body (Midlander alt)",
            "If you have your Midlander alt (c0101), switch to it, enter GPose, press 'Check the body map' and match one photo. PASS means the map fits the alt without changes and the match works. The map uses bone names, not a race, so any difference is a finding for body-map.json. Skip this step if you do not want to test the alt."),
    };
}

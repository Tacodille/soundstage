// No Dalamud types here (see IChecklistArea.cs). Pure data: do this, see that.
namespace Soundstage.Checklist;

/// <summary>Part 17 of the Soundstage plan: calibrating a face and matching a photo's expression through Brio.</summary>
public sealed class ExpressionSteps : IChecklistArea
{
    public string Id => "expression";
    public string Name => "Part 17: Expression (copy a photo's face)";
    public int Order => 170;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Greyed out with a reason",
            "Before you download the face model (Face tab), open the Expression tab. The Calibrate and Match buttons are greyed and a line says the face model is not on this computer yet and to use the Face tab. Then enter GPose without Brio switched on for IPC (or with Brio off): the buttons stay greyed and the line names Brio. PASS means every greyed button has a plain reason beside it and nothing throws."),
        new("2", "The face key",
            "Download the face model on the Face tab if you have not. Enter GPose with your character and open the Expression tab. It shows a line like 'Face race8-clan15-g0-face1: not calibrated yet' (race, clan, gender and face id of your character). PASS means the line appears and the numbers are the ones for your character. Note the key: the table is stored under it."),
        new("3", "Calibrate my face",
            "Give your character a neutral face in Brio (no smile, brows level, eyes open), enter GPose, and press 'Calibrate my face'. The camera moves to a close-up, a bar counts up through about 80 pictures (keep the Expression tab showing; the Soundstage window blinks away for each picture), and it ends with 'Done. N bone moves measured'. Your original expression comes back. PASS means it finishes, the face looks the way it did before you started, and the status line says Done. Note how long it took and how many bone moves it measured. FAIL if it stops with 'no readable face' (then check the close-up framing) or if the face is left distorted."),
        new("4", "The table is on disk",
            "Open the plugin's config folder (the folder that holds Soundstage's settings) and look for 'expression-tables'. It holds one .json file named after the face key from step 2. PASS means the file is there and is more than a few kilobytes."),
        new("5", "Cancel and carry on",
            "Press Calibrate again, wait for the bar to pass about a third, then press 'Cancel calibration'. Your expression comes back at once and the status says where it stopped. Press Calibrate again: it says 'carry on', starts from where it stopped (the bar starts part way) and finishes with fewer pictures. PASS means cancel restores the face and the second run resumes."),
        new("6", "Match one photo",
            "On the Expression tab paste the full path of a portrait with a clear expression (a smile, raised brows or an open mouth) and press 'Read photo'. It shows the photo's head yaw, pitch and roll. Press 'Match expression'. A status line counts through up to six passes, then says 'Score N before, M after'. Your character's face now shows something like the photo's expression. PASS means the face visibly moves towards the photo and M is higher than N. Write the two scores in this step's note."),
        new("7", "Before and after, and undo",
            "After a match the tab shows a small table of the blendshapes that differ most (Photo, Before, After) and a head-angle line. Press 'Undo (put the old expression back)': your old expression returns. Press Ctrl+Z in Brio too, if you like: the matched face is one pose load in its history. PASS means the table shows and Undo works."),
        new("8", "The face never distorts",
            "Match a photo with an extreme expression (a very wide open mouth or a hard squint). The face should reach as far as it safely can and stop: no stretched lips, no eyelids inside the eye, no cheeks through the nose. PASS means the result looks like a face, even if the match is only partial. FAIL if a feature breaks, and note which bone group (brows, lids, mouth, jaw) did it, then lower the 'Safe range' under Tuning and calibrate again."),
        new("9", "Five photos, five ratings",
            "Match five different photos. After each, rate how recognisable the expression is from 1 (not at all) to 5 (spot on) with the buttons under the result. Then copy each score and rating into docs/expression-trials.md (photo description, score, rating). PASS means five rated rows are recorded. If the median of your five ratings is under 3, the tab marks itself EXPERIMENTAL and shows only a tick box to switch it back on: check that this happens."),
        new("10", "Another face (Midlander alt)",
            "If you have your Midlander alt, switch to it, enter GPose, and open the tab. The face key line shows the other race and clan and says 'not calibrated yet' (the table for the Miqo'te is untouched). Calibrate it. PASS means the second table is a second file in 'expression-tables' and matching works on the alt. Skip this step if you do not want to calibrate the alt."),
    };
}

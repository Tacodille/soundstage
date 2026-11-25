// No Dalamud types here (see IChecklistArea.cs). No buttons: these steps are "do this, see that".
namespace Soundstage.Checklist;

/// <summary>Part 13 of the Soundstage plan: photo to Scene, in game and with your own photos.</summary>
public sealed class PhotoSteps : IChecklistArea
{
    public string Id => "photo";
    public string Name => "Part 13: photo to Scene";
    public int Order => 130;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "The Photo tab with no key",
            "Do this first if the AI tab has no saved key. Open the Photo tab, paste the path of any picture (or press Browse), press Load photo. PASS: you see a small preview and a line saying what would be sent (a JPEG at most 1024 pixels on the long side, no camera or location data), and below it a short note about getting a key with a button to the AI tab. No Analyse button and nothing was sent."),
        new("2", "Privacy note comes first",
            "Save a key on the AI tab but do not agree to the privacy note. Back on the Photo tab with a loaded photo. PASS: the privacy note for your provider is shown with a Yes button, and there is no Analyse button until you press Yes. (If you already agreed on the AI tab, press Withdraw there first.)"),
        new("3", "Analyse one photo",
            "Agree to the note, then press the Analyse button. Its label names the provider and model. PASS: within about 20 seconds a card appears under 'Here is what I read': key direction and softness in words, colour swatches, mood words, the closest Scene, and head angle and expression as suggestions. A Cancel button was there while it waited. Nothing on stage has changed yet."),
        new("4", "Apply, then see it in game",
            "Press Apply this Scene with a character in GPose. PASS: the lights appear and the key light is on the side the card said, with the colours of the swatches. Compare it with your photo. This is your first rating (1 to 5) for docs/photo-trials.md."),
        new("5", "Apply and adjust opens Lighting",
            "Press Apply and adjust. PASS: the Lighting tab comes to the front and shows the Scene applied, with the key light's sliders ready to move."),
        new("6", "Save as my Scene",
            "Back on Photo, type a name and press Save as my Scene. PASS: a green line says it is saved to Mine, and the Lighting tab's Scene list shows it under Mine. Restart the game plugin and it is still there."),
        new("7", "The original photo is untouched",
            "Look at the photo you used in File Explorer. PASS: same size and the Date modified you expect, and no new files appear next to it."),
        new("8", "A turned phone photo comes in upright",
            "Load a portrait photo taken on a phone held upright. PASS: the preview is the right way up, not on its side."),
        new("9", "Bad inputs say something plain",
            "Load a text file, then a path that does not exist, then press Cancel in the middle of an Analyse. PASS: each gives one plain line (not a picture, not found, cancelled), nothing changes on stage and the tab keeps working."),
        new("10", "Ten photos, rated",
            "Run ten photos you are allowed to use through the tab, apply each result and rate it 1 to 5. Write the ratings and one line on what went wrong into docs/photo-trials.md (the photos stay out of the repository). PASS: ten rows filled in. No accuracy is claimed beyond those ten."),
    };
}

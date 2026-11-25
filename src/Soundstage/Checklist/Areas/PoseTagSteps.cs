// No Dalamud types in this file or in Checklist/Areas/*.cs: the offline self-check compiles them as shared files.
namespace Soundstage.Checklist;

/// <summary>Part 14 of the Soundstage plan: pose thumbnails, AI tags and "Poses that fit", in game.</summary>
public sealed class PoseTagSteps : IChecklistArea
{
    public string Id => "posetags";
    public string Name => "Part 14: Pose thumbnails and AI tags";
    public int Order => 140;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Make thumbnails for one pack",
            "Enter GPose on your own character and frame them (full body, in the middle of the picture). Open the Poses tab, scroll to 'Thumbnails and AI tags' and pick one small pack in the Pack box. Press Make thumbnails. The Soundstage window and the game UI vanish, your character runs through the poses on a plain grey set, and a progress count shows in the status line. Press Rescan on the shelf afterwards. PASS means every pose in the pack has a thumbnail ('N with a thumbnail' on the panel matches the pack size) and the status line says 'Made N thumbnail(s)'. Note how long it took per pose and whether any came out empty or cut off."),
        new("2", "Your pose, Scene and window come back",
            "Before step 1, note your current pose and Scene. After the thumbnails finish: your original pose is back on the character, your original Scene is on stage (not the grey one), the Soundstage window and the game UI are back, and the light markers are as they were. PASS means all of that. FAIL if anything stays changed: say which."),
        new("3", "Cancel and leaving GPose",
            "Start Make thumbnails for a pack of six or more, then press Cancel after two or three. PASS means it stops within a moment, everything comes back as in step 2, and the thumbnails made so far are still there. Then start it again and leave GPose part way: it stops, and nothing stays hidden."),
        new("4", "Thumbnails in the shelf tooltip",
            "On the Poses tab, rest the mouse on a pose from the pack for a moment. A small picture of that pose appears in the tooltip, cropped to the character. PASS means the picture matches the pose. A pose file that carries its own preview picture still shows that one. The log (search dalamud.log for [Soundstage/BRIO] thumbnail cache) says how many poses had a thumbnail after the last scan."),
        new("5", "Consent before anything is sent",
            "Open the AI tab and make sure a key is saved and tested. Press Tag with AI. PASS means the privacy note appears first, it mentions the 256 pixel pictures of your own character, and nothing is sent until you press Yes. Say no (leave the panel) and check the AI tab's 'sent today' count has not moved. If you said yes to the older note for photos, you are asked again here; that is expected."),
        new("6", "Tag ten poses with AI",
            "Pick a pack, press Tag with AI and let it run. Tags appear after each pose in the shelf as they arrive, not all at the end. Stop after about ten (press Cancel). PASS means ten poses have tags that are plain words from the list (standing, sitting, kneeling, and so on, plus up to three of the model's own words). Then rate each of the ten: 1 for right, 0.5 for partly right, 0 for wrong, and write the ten ratings and the provider and model in the note. The ratings go in the commit message that lands this part."),
        new("7", "Nothing outside the list",
            "Look at the tags the model added in step 6. PASS means every tag is either in the list in Poses\\vocabulary.json (beside the plugin) or one of at most three free words per pose. Search dalamud.log for 'dropped': a line there means the model offered something off the list and it was thrown away, which is correct."),
        new("8", "The tag filter finds them",
            "Type 'sit' in the shelf's tag filter. PASS means only poses tagged sitting (or similar) are listed. Pick a tag from the 'tags' drop-down: the list narrows to it. Right-click a pose and remove one AI tag by hand, then add one: both stick after Rescan."),
        new("9", "The daily allowance",
            "Watch the line under the consent: 'Today: N request(s) sent'. If you use an OpenRouter ':free' model it shows 'of 50'. PASS means the number goes up by one per pose tagged. If you can, run a pack big enough to reach the limit: the queue stops with 'Stopped for today', says how many poses are waiting, and the button changes to 'Continue tagging' the next day. Skip with a note if you do not have a pack that large."),
        new("10", "Poses that fit a Scene",
            "Open the Scenes tab and pick a Scene whose pose block lists tags (a Scene you saved, or an imported one). Under it, 'Poses that fit' lists poses sharing those tags, best first, each with the tags it shares. Press Load beside one in GPose. PASS means that pose loads on your character. If the Scene has no pose tags the section is absent, which is correct."),
    };
}

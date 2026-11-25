// No Dalamud types in this file or in Checklist/Areas/*.cs: the offline self-check compiles them as shared files.
namespace Soundstage.Checklist;

/// <summary>Part 12 of the Soundstage plan: capture, photo booth, contact sheet and onion skin, in game.</summary>
public sealed class WorkflowSteps : IChecklistArea
{
    public string Id => "workflow";
    public string Name => "Part 12: Workflow (capture, booth, contact sheet, onion skin)";
    public int Order => 120;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Capture via the ReShade add-on",
            "This needs the add-on loaded (Part 3). Put Add-on first in the route list on the Workflow tab (it is first by default), tick 'Hide the game UI', and press Capture now. The game UI and the Soundstage window vanish for a moment and come back, and a PNG appears in the capture folder (use Open folder). Open it. PASS means the picture is the game view with no game UI and no Soundstage window. In the note, write whether the Dalamud overlay (plugin windows, markers) is in the file or not, and how long the log says it took: search dalamud.log for [Soundstage/WORK] capture via addon. Those two facts fill the table in docs/capture-notes.md. FAIL if no file appears via the add-on and the log says why."),
        new("2", "Capture via the screenshot key",
            "On the Workflow tab, remove Add-on from the route list (press Off beside it) so Screenshot key is first. Press Capture now. PASS means a PNG lands in the capture folder, the log says 'capture via key', and the picture is clean. Note whether the Dalamud overlay is in the file and the time in the log. If the folder was not found, type the game's screenshot folder into the 'Screenshot folder' box on the tab. Put Add-on back with its Add button afterwards."),
        new("3", "Capture via the window",
            "Leave only Window in the route list and press Capture now. PASS means a PNG lands in the capture folder with the log line 'capture via window'. This route copies what is on screen, so note whether the overlay is in it (it should be, if you left something showing). Restore the route order to Add-on, Screenshot key, Window afterwards."),
        new("4", "Route fallback",
            "Put Add-on first in the route list and make it fail: either the add-on is not installed, or the game has not been restarted since it was copied in. Press Capture now. The log says the add-on route failed, the next route ran, and a PNG still appeared. PASS means a file appears and the status line names the route that worked. Skip with a note if the add-on is installed and you would rather not remove it."),
        new("5", "Capture hides and restores everything",
            "Turn on Thirds (Camera tab), keep light markers on, and show the onion skin (step 9). Press Capture now. During the capture the thirds, the markers, the onion skin, the Soundstage window and the game UI are all out of the shot, and every one is back afterwards, including the game UI (Scroll Lock toggled twice). PASS means the saved picture has none of them and everything returned. FAIL if anything stays hidden: say which, and press Scroll Lock yourself to recover."),
        new("6", "Photo booth",
            "Enter GPose. On the Workflow tab set the countdown to 5 and press Start the booth. The tab counts down, then everything hides, the shot is taken, and everything comes back. PASS means a PNG named Booth ... appears and the countdown was about five seconds. Then start it again and press Cancel in the middle of the countdown: nothing hides and no file appears. Start it a third time and leave GPose during the countdown: it cancels, nothing stays hidden."),
        new("7", "Contact sheet of six Scenes",
            "Enter GPose. Pick six Scenes in the contact sheet list (tick six), set 3 columns, press Make the sheet. The Scenes apply one after another, each settles, and the tab counts them off. At the end your original Scene and camera are back. PASS means a Sheet ... PNG with six labelled pictures in a 3 by 2 grid, each showing a different look, and your stage and camera as they were. If a picture looks like the previous Scene still, raise 'Settle time per Scene' and note the value that worked. FAIL if it stops: the status line names the Scene it stopped at."),
        new("8", "Contact sheet stops cleanly",
            "Start a sheet of six and press Cancel the sheet part way through. PASS means the UI comes back, your original Scene returns, and no half sheet file is left in the capture folder (a .part.png file would be a FAIL). Also check the undo list: the Scenes the sheet applied are in it as ordinary steps, which is expected."),
        new("9", "Onion skin from a saved screenshot",
            "Take a capture of a pose you like (step 1). Change the pose, then paste that PNG's path into the Onion skin box and tick Show. The old shot appears over the game at the opacity you set. Move Opacity, Scale and the two offsets, tick Flip, tick Edges only (you should see only outlines on transparency). PASS means each control changes the overlay the way its label says and the overlay sits behind the Soundstage window. Press Reset position, then untick Show: it goes away."),
        new("10", "Onion skin is not copied",
            "Close and reopen the game or reload the plugin. The Onion skin box still holds the path, and the picture comes back when Show is ticked. Check the capture folder and the plugin's config folder: the reference image has not been copied into either. PASS means the path is remembered and no copy exists."),
        new("11", "Capture folder",
            "Press Open folder on the Workflow tab: Explorer opens at the capture folder, which is under Documents\\Soundstage\\Captures unless you changed it. Type another folder, capture again, and the file lands there. PASS means both."),
    };
}

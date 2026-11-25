// No Dalamud types here (see IChecklistArea.cs). No buttons: these steps are "do this, see that".
namespace Soundstage.Checklist;

/// <summary>Part 15: the Status tab and the clean install from a custom repository.</summary>
public sealed class ReleaseSteps : IChecklistArea
{
    public string Id => "release";
    public string Name => "Part 15: Status tab and clean install";
    public int Order => 150;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "Status tab, all there", "With Stagehand and Brio installed and enabled (Brio's IPC on), open the Status tab. PASS = Stagehand and Brio are green with a version or revision, and the summary line at the top says everything Soundstage needs is answering (amber for the add-on or the key does not stop that)."),
        new("2", "Stagehand off turns the row red", "In /xlplugins switch Stagehand off. Press Recheck on the Status tab. PASS = the Stagehand row goes red with a fix line, the Copy /xlplugins button copies the command, and the Lighting tab greys out with a red line saying why. Switch Stagehand back on, press Recheck, and both go back to normal."),
        new("3", "Brio off turns the row red", "Switch Brio off (or turn its IPC off in its settings) and press Recheck. PASS = the Brio row goes red and mentions IPC. Turn it back on and press Recheck."),
        new("4", "Add-on row", "Without soundstage.addon64 in the game folder: PASS = the add-on row is amber and offers Open the game folder. Copy the add-on in (the separate download, next to dxgi.dll), restart the game and open the Status tab: PASS = it goes green with a version number. Soundstage never writes to the game folder itself."),
        new("5", "AI key row", "With no key saved the row is amber and Open the AI tab takes you there. Save a key, then on the Status tab: PASS = amber 'saved but has not been tested', with a Test key button. Press it: PASS = green. Try a key with a letter changed: PASS = red with the plain message from the AI tab."),
        new("6", "Rows are live, not remembered", "Leave the Status tab open and switch Brio off and on in /xlplugins. PASS = within about 20 seconds the row follows without pressing anything."),
        new("7", "Clean install from the custom repository", "Remove the dev plugin entry. In /xlsettings > Experimental add the repository link as a custom plugin repository (the repo.json of the release), save, then install Soundstage from /xlplugins. PASS = it installs and /soundstage opens the window with every tab. The zip carries the plugin, its data files and its libraries, and nothing else."),
        new("8", "Updating keeps your settings", "With the installed copy, save a Scene of your own, then install a newer build over it. PASS = the Scene is still under Mine and the key is still saved."),
    };
}

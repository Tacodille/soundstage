namespace Soundstage.Tabs;

/// <summary>
/// One tab of the main window. <see cref="TabHost"/> finds every non-abstract implementation with a parameterless
/// constructor by reflection, so a part adds a tab by adding one file under Tabs/ and nothing else.
/// </summary>
public interface ITab
{
    /// <summary>The label on the tab, and its ImGui id.</summary>
    string Name { get; }

    /// <summary>Left to right. Lighting 10, Set 20, Poses 30, Tools 90; leave gaps so a part can slot in between.</summary>
    int Order { get; }

    /// <summary>False hides the tab entirely (the Tools tab until /soundstage dev).</summary>
    bool IsVisible(Studio studio) => true;

    void Draw(Studio studio);

    /// <summary>Called each frame the window is drawn but this tab is not the selected one.</summary>
    void OnHidden(Studio studio) { }
}

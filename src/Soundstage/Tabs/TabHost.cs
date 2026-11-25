using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;

namespace Soundstage.Tabs;

/// <summary>Finds every <see cref="ITab"/> in this assembly and draws them as one tab bar.</summary>
public sealed class TabHost
{
    private readonly IReadOnlyList<ITab> tabs;
    private readonly IPluginLog log;
    private readonly HashSet<string> failed = new();

    public TabHost(IPluginLog log)
    {
        this.log = log;
        tabs = Discover(typeof(TabHost).Assembly, s => log.Information("{P} {Line}", "[Soundstage/TABS]", s));
        log.Information("{P} {N} tab(s): {Names}", "[Soundstage/TABS]", tabs.Count, string.Join(", ", tabs.Select(t => t.Name)));
    }

    public IReadOnlyList<ITab> Tabs => tabs;

    private static string? requestedTab;

    /// <summary>Part 13: asks the tab bar to switch to a tab by name on the next frame (Photo's "Adjust" opens Lighting).</summary>
    public static void RequestTab(string name) => requestedTab = name;

    /// <summary>Every tab type in the assembly, constructed, in tab order. A type that cannot be built is logged and skipped.</summary>
    public static IReadOnlyList<ITab> Discover(System.Reflection.Assembly assembly, Action<string> log)
    {
        var found = new List<ITab>();
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(ITab).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null) { log($"{type.Name} has no parameterless constructor, skipped."); continue; }
            try { found.Add((ITab)Activator.CreateInstance(type)!); }
            catch (Exception ex) { log($"{type.Name} could not be created, skipped: {ex.Message}"); }
        }
        return found.OrderBy(t => t.Order).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    public void Draw(Studio studio)
    {
        if (!ImGui.BeginTabBar("##SoundstageTabs")) return;
        foreach (var tab in tabs)
        {
            if (!tab.IsVisible(studio)) { SafeHidden(tab, studio); continue; }
            var select = requestedTab != null && string.Equals(requestedTab, tab.Name, StringComparison.Ordinal);
            if (select) requestedTab = null;
            if (ImGui.BeginTabItem(tab.Name, select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            {
                try { tab.Draw(studio); }
                catch (Exception ex) { LogOnce(tab, ex); ImGui.TextDisabled("This tab hit an error; it is in the log."); }
                ImGui.EndTabItem();
            }
            else SafeHidden(tab, studio);
        }
        ImGui.EndTabBar();
    }

    private void SafeHidden(ITab tab, Studio studio)
    {
        try { tab.OnHidden(studio); }
        catch (Exception ex) { LogOnce(tab, ex); }
    }

    private void LogOnce(ITab tab, Exception ex)
    {
        if (failed.Add(tab.Name)) log.Information("{P} Tab '{Tab}' threw: {Ex}", "[Soundstage/TABS]", tab.Name, ex);
    }
}

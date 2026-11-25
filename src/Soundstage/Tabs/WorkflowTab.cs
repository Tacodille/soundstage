using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Soundstage.Workflow;

namespace Soundstage.Tabs;

/// <summary>Capture, photo booth, contact sheet and the onion-skin overlay.</summary>
public sealed class WorkflowTab : ITab
{
    public string Name => "Workflow";
    public int Order => 60;

    private string folderText = "";
    private bool folderInit;
    private string shotText = "";
    private bool shotInit;
    private string onionPath = "";
    private bool onionInit;
    private string sceneFilter = "";
    private string note = "";

    public void Draw(Studio studio)
    {
        var host = WorkflowHost.For(studio);
        var s = host.Settings;
        var changed = false;

        DrawCapture(host, s, ref changed);
        ImGui.Separator();
        DrawBooth(host, s, ref changed);
        ImGui.Separator();
        DrawSheet(studio, host, s, ref changed);
        ImGui.Separator();
        DrawOnion(host, s, ref changed);

        if (changed) host.Persist();
    }

    private void DrawCapture(WorkflowHost host, WorkflowSettings s, ref bool changed)
    {
        ImGui.TextUnformatted("Capture");
        if (!folderInit) { folderText = s.OutputFolder; folderInit = true; }
        ImGui.SetNextItemWidth(-130f);
        if (ImGui.InputTextWithHint("##folder", "Documents\\Soundstage\\Captures", ref folderText, 260)) { s.OutputFolder = folderText.Trim(); changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("Open folder"))
        {
            try
            {
                Directory.CreateDirectory(host.OutputFolder);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + host.OutputFolder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex) { note = "Could not open the folder: " + ex.Message; }
        }
        ImGui.TextDisabled("Saving to " + host.OutputFolder);

        var gui = s.HideGameUi;
        if (ImGui.Checkbox("Hide the game UI for a capture (sends Scroll Lock, then again to bring it back)", ref gui)) { s.HideGameUi = gui; changed = true; }
        var timeout = s.TimeoutSeconds;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Seconds to wait per route", ref timeout, 2, 30)) { s.TimeoutSeconds = timeout; changed = true; }
        ImGui.TextDisabled("Routes, in the order tried: " + string.Join(" > ", s.RouteOrder.Select(RouteName)));
        DrawRouteOrder(s, ref changed);
        if (s.RouteOrder.Contains("key"))
        {
            if (!shotInit) { shotText = s.ScreenshotFolder; shotInit = true; }
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint("##shotFolder", "Screenshot folder (blank: ReShade's, then the game's)", ref shotText, 260)) { s.ScreenshotFolder = shotText.Trim(); changed = true; }
        }

        ImGui.BeginDisabled(host.Booth.Busy || host.Sheet.Busy);
        if (ImGui.Button("Capture now")) host.Booth.Start(0, host.NextPath(), needGpose: false);
        ImGui.EndDisabled();
        if (host.Booth.Last is { } last && last.Ok) { ImGui.SameLine(); ImGui.TextDisabled($"Last: {Path.GetFileName(last.Path)} via {last.Route}, {last.Milliseconds} ms"); }
        if (host.Booth.Status.Length > 0) ImGui.TextWrapped(host.Booth.Status);
        if (note.Length > 0) ImGui.TextDisabled(note);
    }

    private static string RouteName(string id) => id switch { "addon" => "ReShade add-on", "key" => "screenshot key", "window" => "window", _ => id };

    private static void DrawRouteOrder(WorkflowSettings s, ref bool changed)
    {
        var order = s.RouteOrder;
        for (var i = 0; i < order.Count; i++)
        {
            ImGui.PushID("route" + i);
            ImGui.TextUnformatted($"{i + 1}. {RouteName(order[i])}");
            ImGui.SameLine();
            if (i > 0 && ImGui.SmallButton("Up")) { (order[i - 1], order[i]) = (order[i], order[i - 1]); changed = true; }
            if (i > 0) ImGui.SameLine();
            if (ImGui.SmallButton("Off")) { order.RemoveAt(i); changed = true; ImGui.PopID(); break; }
            ImGui.PopID();
        }
        foreach (var id in new[] { "addon", "key", "window" })
        {
            if (order.Contains(id)) continue;
            ImGui.SameLine();
            if (ImGui.SmallButton("Add " + RouteName(id))) { order.Add(id); changed = true; }
        }
    }

    private void DrawBooth(WorkflowHost host, WorkflowSettings s, ref bool changed)
    {
        ImGui.TextUnformatted("Photo booth");
        var secs = Math.Clamp(s.BoothSeconds, PhotoBooth.MinSeconds, PhotoBooth.MaxSeconds);
        ImGui.SetNextItemWidth(200f);
        if (ImGui.SliderInt("Countdown (seconds)", ref secs, PhotoBooth.MinSeconds, PhotoBooth.MaxSeconds)) { s.BoothSeconds = secs; changed = true; }
        var inGpose = host.InGpose();
        if (!host.Booth.Busy)
        {
            ImGui.BeginDisabled(!inGpose || host.Sheet.Busy);
            if (ImGui.Button("Start the booth")) host.Booth.Start(secs, host.NextPath("Booth"), needGpose: true);
            ImGui.EndDisabled();
            if (!inGpose) { ImGui.SameLine(); ImGui.TextDisabled("Wakes up in GPose."); }
        }
        else
        {
            if (ImGui.Button("Cancel")) host.Booth.Cancel();
            ImGui.SameLine();
            ImGui.TextUnformatted(host.Booth.State == BoothState.Counting ? $"{host.Booth.SecondsLeft}..." : host.Booth.Status);
        }
        ImGui.TextDisabled("Hides these windows, the light markers, the composition overlay, the onion skin and (if ticked above) the game UI, then puts them all back.");
    }

    private void DrawSheet(Studio studio, WorkflowHost host, WorkflowSettings s, ref bool changed)
    {
        ImGui.TextUnformatted($"Contact sheet ({s.SheetSceneIds.Count} of {WorkflowSettings.MaxSheetScenes} Scenes picked)");
        ImGui.SetNextItemWidth(200f);
        ImGui.InputTextWithHint("##sceneFilter", "Filter the Scene list", ref sceneFilter, 60);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear picks")) { s.SheetSceneIds.Clear(); changed = true; }

        if (ImGui.BeginChild("##sheetScenes", new System.Numerics.Vector2(0, 130), true))
        {
            foreach (var scene in studio.Scenes)
            {
                if (sceneFilter.Length > 0 && scene.Name.IndexOf(sceneFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var picked = s.SheetSceneIds.Contains(scene.Id);
                if (!ImGui.Checkbox(scene.Name + "##pick" + scene.Id, ref picked)) continue;
                if (picked && s.SheetSceneIds.Count < WorkflowSettings.MaxSheetScenes) s.SheetSceneIds.Add(scene.Id);
                else if (!picked) s.SheetSceneIds.Remove(scene.Id);
                changed = true;
            }
        }
        ImGui.EndChild();

        var cols = s.SheetColumns;
        ImGui.SetNextItemWidth(120f);
        if (ImGui.SliderInt("Columns", ref cols, 1, 6)) { s.SheetColumns = cols; changed = true; }
        var settle = s.SettleMs;
        ImGui.SetNextItemWidth(160f);
        if (ImGui.SliderInt("Settle time per Scene (ms)", ref settle, 100, 3000)) { s.SettleMs = settle; changed = true; }

        var inGpose = host.InGpose();
        if (!host.Sheet.Busy)
        {
            ImGui.BeginDisabled(!inGpose || s.SheetSceneIds.Count == 0 || host.Booth.Busy);
            if (ImGui.Button("Make the sheet"))
                host.Sheet.Start(host.ItemsFor(s.SheetSceneIds), host.NextPath("Sheet"), s.SheetColumns, s.SheetCellWidth);
            ImGui.EndDisabled();
            if (!inGpose) { ImGui.SameLine(); ImGui.TextDisabled("Wakes up in GPose."); }
        }
        else
        {
            if (ImGui.Button("Cancel the sheet")) host.Sheet.Cancel();
            ImGui.SameLine();
            ImGui.TextUnformatted($"{host.Sheet.Done} of {host.Sheet.Total} done");
        }
        if (host.Sheet.Status.Length > 0) ImGui.TextWrapped(host.Sheet.Status);
        ImGui.TextDisabled("Your current Scene and camera go back afterwards. The Scenes applied stay in the undo list.");
    }

    private void DrawOnion(WorkflowHost host, WorkflowSettings s, ref bool changed)
    {
        var o = s.Onion;
        ImGui.TextUnformatted("Onion skin");
        if (!onionInit) { onionPath = o.ImagePath; onionInit = true; }
        ImGui.SetNextItemWidth(-90f);
        if (ImGui.InputTextWithHint("##onionPath", "Path to a reference image (png, jpg, bmp)", ref onionPath, 400))
        {
            o.ImagePath = onionPath.Trim().Trim('"');
            host.Onion.Reload();
            changed = true;
        }
        ImGui.SameLine();
        var on = o.On;
        if (ImGui.Checkbox("Show", ref on)) { o.On = on; changed = true; }

        var opacity = o.Opacity;
        if (ImGui.SliderFloat("Opacity", ref opacity, 0f, 1f)) { o.Opacity = opacity; changed = true; }
        var scale = o.Scale;
        if (ImGui.SliderFloat("Scale", ref scale, 0.1f, 4f)) { o.Scale = scale; changed = true; }
        var ox = o.OffsetX;
        if (ImGui.SliderFloat("Offset left or right", ref ox, -1f, 1f)) { o.OffsetX = ox; changed = true; }
        var oy = o.OffsetY;
        if (ImGui.SliderFloat("Offset up or down", ref oy, -1f, 1f)) { o.OffsetY = oy; changed = true; }
        var flip = o.FlipHorizontal;
        if (ImGui.Checkbox("Flip left to right", ref flip)) { o.FlipHorizontal = flip; changed = true; }
        ImGui.SameLine();
        var edge = o.EdgeOnly;
        if (ImGui.Checkbox("Edges only", ref edge)) { o.EdgeOnly = edge; changed = true; }
        if (ImGui.SmallButton("Reset position")) { o.Scale = 1f; o.OffsetX = o.OffsetY = 0f; changed = true; }
        if (host.Onion.Status.Length > 0 && o.On) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.6f, 0.4f, 1f), host.Onion.Status);
        ImGui.TextDisabled("The image is read from where it is. It is not copied or uploaded.");
    }
}

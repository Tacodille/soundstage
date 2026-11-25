using System.Text;
using Dalamud.Bindings.ImGui;

namespace Soundstage.Checklist;

/// <summary>The buttons for the core steps, ported from the Keylight beta window.</summary>
public sealed class CoreStepsUi : IChecklistAreaUi
{
    private static readonly System.Numerics.Vector4 Green = new(0.5f, 1f, 0.6f, 1f);
    private static readonly System.Numerics.Vector4 Red = new(1f, 0.5f, 0.4f, 1f);

    private string lastPresetId = "";

    public string AreaId => "core";

    public ChecklistResult? AutoResult(ChecklistContext ctx, ChecklistStep step)
    {
        if (step.Id != "1") return null;
        var bridge = ctx.Studio.Bridge;
        return bridge.Available
            ? new ChecklistResult("PASS", $"api revision {bridge.Revision}")
            : new ChecklistResult("FAIL", $"not available ({bridge.Availability})");
    }

    public bool OwnsResults(ChecklistStep step) => step.Id == "3";

    public ChecklistResult? OverallResult(ChecklistContext ctx, ChecklistStep step)
    {
        // Step 3 passes when every loaded preset has a PASS and fails when any has a FAIL.
        var presets = ctx.Studio.Presets;
        if (presets.Any(p => ctx.Get(AreaId, "3", p.Id)?.Status == "FAIL")) return new ChecklistResult("FAIL", "");
        if (presets.Count > 0 && presets.All(p => ctx.Get(AreaId, "3", p.Id) != null)) return new ChecklistResult("PASS", "");
        return null;
    }

    public void AppendSummary(ChecklistContext ctx, ChecklistStep step, StringBuilder sb)
    {
        if (step.Id != "3") return;
        foreach (var p in ctx.Studio.Presets)
        {
            var r = ctx.Get(AreaId, "3", p.Id);
            if (r == null) { sb.AppendLine($"     {p.Id}: not done"); continue; }
            sb.AppendLine($"     {p.Id}: {r.Status}{(r.Note.Length > 0 ? " - " + r.Note : "")}");
        }
    }

    public void DrawControls(ChecklistContext ctx, ChecklistStep step)
    {
        var studio = ctx.Studio;
        var plugin = studio.Plugin;
        var config = studio.Config;
        var bridge = studio.Bridge;
        switch (step.Id)
        {
            case "1":
                ImGui.TextUnformatted(bridge.Available ? $"Connected, API revision {bridge.Revision}." : $"Not connected ({bridge.Availability}).");
                if (ImGui.Button("Recheck Stagehand")) bridge.Refresh();
                break;

            case "2":
                if (ImGui.Button("Place smoke test")) plugin.PlaceSmoke();
                ImGui.SameLine();
                if (ImGui.Button("Remove smoke test")) plugin.RemoveSmoke();
                break;

            case "3":
                DrawPresetStep(ctx, step);
                break;

            case "5":
                if (ImGui.Button("Toggle markers"))
                {
                    config.ShowLightMarkers = !config.ShowLightMarkers;
                    plugin.Save();
                }
                ImGui.SameLine();
                ImGui.TextDisabled(config.ShowLightMarkers ? "markers on" : "markers off");
                break;

            case "6":
                if (ImGui.Button("Apply a backdrop preset"))
                {
                    var p = studio.Presets.FirstOrDefault(x => x.Backdrop != null);
                    if (p == null) plugin.SetMessage("No loaded preset has a backdrop.");
                    else plugin.Apply(p);
                }
                ImGui.SameLine();
                ImGui.TextDisabled(studio.Current?.Backdrop != null ? $"applied: {studio.Current.Name}" : "applied preset has no backdrop");
                break;

            case "7":
                if (ImGui.Button("Place canvas test")) plugin.PlaceCanvas();
                ImGui.SameLine();
                if (ImGui.Button("Remove canvas")) plugin.RemoveCanvas();
                break;

            case "8":
                if (ImGui.Button("Save test preset (\"Beta test look\")")) plugin.SaveCurrentAs("Beta test look");
                ImGui.SameLine();
                ImGui.TextDisabled(studio.Presets.Any(p => p.Category == "Mine") ? "Mine has presets" : "Mine is empty");
                break;

            case "10":
                if (ImGui.Button("Remove all")) plugin.RemoveAll();
                break;
        }
    }

    private void DrawPresetStep(ChecklistContext ctx, ChecklistStep step)
    {
        var studio = ctx.Studio;
        var presets = studio.Presets;
        var done = presets.Count(p => ctx.Get(AreaId, "3", p.Id) != null);
        var failed = presets.Count(p => ctx.Get(AreaId, "3", p.Id)?.Status == "FAIL");
        ImGui.TextUnformatted($"{done} of {presets.Count} presets done, {failed} failed.");

        if (ImGui.Button("Next preset"))
        {
            var p = studio.Plugin.ApplyNext();
            if (p != null) lastPresetId = p.Id;
        }
        var shown = presets.FirstOrDefault(p => p.Id == lastPresetId);
        if (shown == null)
        {
            ImGui.TextDisabled("No preset applied from here yet.");
            return;
        }
        ImGui.TextUnformatted($"Applied: {shown.Name} [{shown.Category}] ({shown.Id})");
        if (!string.IsNullOrWhiteSpace(shown.Description)) ImGui.TextWrapped(shown.Description);
        if (ctx.Get(AreaId, "3", shown.Id) is { } res)
            ImGui.TextColored(res.Status == "PASS" ? Green : Red, $"This preset: {res.Status}{(res.Note.Length > 0 ? " - " + res.Note : "")}");
        ctx.DrawResultButtons(AreaId, step, shown.Id);
    }
}

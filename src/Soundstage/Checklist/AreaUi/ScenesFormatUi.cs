using System.Text;
using Dalamud.Bindings.ImGui;

namespace Soundstage.Checklist;

/// <summary>Buttons for the Scenes steps: step through the shipped Scenes and mark each, apply palettes, apply the six-light test.</summary>
public sealed class ScenesFormatUi : IChecklistAreaUi
{
    private static readonly System.Numerics.Vector4 Green = new(0.5f, 1f, 0.6f, 1f);
    private static readonly System.Numerics.Vector4 Red = new(1f, 0.5f, 0.4f, 1f);
    private static readonly string[] PaletteScenes = { "neon-split", "neon-wall-soft", "rim-only" };

    private string lastSceneId = "";
    private int next;

    public string AreaId => "scenesformat";

    /// <summary>The shipped Scenes only: not Mine and not the built-in one.</summary>
    private static List<Scene> Shipped(Studio studio) => studio.Presets.Where(p => p.Category != "Mine" && p.Category != "Built-in").ToList();

    public bool OwnsResults(ChecklistStep step) => step.Id == "1";

    public ChecklistResult? OverallResult(ChecklistContext ctx, ChecklistStep step)
    {
        var scenes = Shipped(ctx.Studio);
        if (scenes.Any(p => ctx.Get(AreaId, "1", p.Id)?.Status == "FAIL")) return new ChecklistResult("FAIL", "");
        if (scenes.Count > 0 && scenes.All(p => ctx.Get(AreaId, "1", p.Id) != null)) return new ChecklistResult("PASS", "");
        return null;
    }

    public void AppendSummary(ChecklistContext ctx, ChecklistStep step, StringBuilder sb)
    {
        if (step.Id != "1") return;
        foreach (var p in Shipped(ctx.Studio))
        {
            var r = ctx.Get(AreaId, "1", p.Id);
            if (r == null) { sb.AppendLine($"     {p.Id}: not done"); continue; }
            sb.AppendLine($"     {p.Id}: {r.Status}{(r.Note.Length > 0 ? " - " + r.Note : "")}");
        }
    }

    public void DrawControls(ChecklistContext ctx, ChecklistStep step)
    {
        var studio = ctx.Studio;
        var plugin = studio.Plugin;
        switch (step.Id)
        {
            case "1":
                DrawSceneStep(ctx, step);
                break;

            case "2":
                foreach (var id in PaletteScenes)
                {
                    var scene = studio.Presets.FirstOrDefault(p => p.Id == id);
                    if (scene == null) continue;
                    ImGui.TextUnformatted(scene.Name + ":");
                    foreach (var palette in scene.Palettes.Keys)
                    {
                        ImGui.SameLine();
                        if (ImGui.Button(palette + "##" + id))
                        {
                            plugin.SelectScene(scene.Id, palette);
                            plugin.Apply(scene, palette);
                        }
                    }
                }
                break;

            case "5":
                if (ImGui.Button("Apply Neon split, Violet and amber"))
                {
                    var scene = studio.Presets.FirstOrDefault(p => p.Id == "neon-split");
                    if (scene != null) { plugin.SelectScene(scene.Id, "Violet and amber"); plugin.Apply(scene, "Violet and amber"); }
                }
                ImGui.SameLine();
                if (ImGui.Button("Save as Part 4 test")) plugin.SaveCurrentAs("Part 4 test");
                ImGui.SameLine();
                if (ImGui.Button("Reload scenes")) plugin.ReloadPresets();
                break;

            case "6":
                if (ImGui.Button("Apply six-light test Scene")) plugin.ApplySixLightTest();
                ImGui.SameLine();
                if (ImGui.Button("Remove all")) plugin.RemoveAll();
                break;
        }
    }

    private void DrawSceneStep(ChecklistContext ctx, ChecklistStep step)
    {
        var studio = ctx.Studio;
        var scenes = Shipped(studio);
        var done = scenes.Count(p => ctx.Get(AreaId, "1", p.Id) != null);
        var failed = scenes.Count(p => ctx.Get(AreaId, "1", p.Id)?.Status == "FAIL");
        ImGui.TextUnformatted($"{done} of {scenes.Count} Scenes done, {failed} failed.");
        if (scenes.Count == 0) { ImGui.TextDisabled("No shipped Scenes loaded. Is scenes.json beside Soundstage.dll?"); return; }

        if (ImGui.Button("Next Scene"))
        {
            var p = scenes[next % scenes.Count];
            next = (next + 1) % scenes.Count;
            studio.Plugin.SelectScene(p.Id, null);
            studio.Plugin.Apply(p);
            lastSceneId = p.Id;
        }
        var shown = scenes.FirstOrDefault(p => p.Id == lastSceneId);
        if (shown == null) { ImGui.TextDisabled("No Scene applied from here yet."); return; }
        ImGui.TextUnformatted($"Applied: {shown.Name} [{shown.Category}] ({shown.Id}), {shown.Lights.Count} light(s)");
        if (!string.IsNullOrWhiteSpace(shown.Description)) ImGui.TextWrapped(shown.Description);
        if (!string.IsNullOrWhiteSpace(shown.BestIn)) ImGui.TextWrapped("Best in: " + shown.BestIn);
        if (ctx.Get(AreaId, "1", shown.Id) is { } res)
            ImGui.TextColored(res.Status == "PASS" ? Green : Red, $"This Scene: {res.Status}{(res.Note.Length > 0 ? " - " + res.Note : "")}");
        ctx.DrawResultButtons(AreaId, step, shown.Id);
    }
}

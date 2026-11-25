using Dalamud.Bindings.ImGui;

namespace Soundstage.Checklist;

/// <summary>Live read-outs for the poses steps: Brio's state, the scan summary and whether you are in GPose.</summary>
public sealed class PosesStepsUi : IChecklistAreaUi
{
    public string AreaId => "poses";

    public ChecklistResult? AutoResult(ChecklistContext ctx, ChecklistStep step)
    {
        if (step.Id != "1") return null;
        var brio = ctx.Studio.Brio;
        return brio.Available
            ? new ChecklistResult("PASS", $"Brio API {brio.Version}")
            : new ChecklistResult("FAIL", $"not available ({brio.LastError ?? "unknown"})");
    }

    public void DrawControls(ChecklistContext ctx, ChecklistStep step)
    {
        var studio = ctx.Studio;
        var poses = studio.Poses;
        switch (step.Id)
        {
            case "1":
                ImGui.TextUnformatted(studio.Brio.Available ? $"Connected, API {studio.Brio.Version}." : $"Not connected ({studio.Brio.LastError ?? "unknown"}).");
                if (ImGui.Button("Recheck Brio")) studio.Brio.Refresh();
                break;

            case "2":
                ImGui.TextUnformatted(poses.ScanSummary);
                ImGui.TextUnformatted($"{poses.EntryCount} file(s) in {poses.PackCount} pack(s).");
                if (ImGui.Button("Rescan")) poses.Rescan();
                break;

            case "9":
                ImGui.TextUnformatted(poses.InGpose ? "You are in GPose right now." : "You are not in GPose right now.");
                break;

            case "3":
            case "4":
            case "5":
            case "6":
            case "7":
            case "12":
                if (!string.IsNullOrEmpty(poses.Status)) ImGui.TextWrapped("Shelf says: " + poses.Status);
                break;
        }
    }
}

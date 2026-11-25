using System.Text;

namespace Soundstage.Checklist;

/// <summary>
/// Optional buttons and live checks for one area, in a file of its own under Checklist/AreaUi/ (the area file itself
/// stays free of Dalamud types). The window pairs it with its area by <see cref="AreaId"/>. Every member but AreaId has a
/// default, so an area whose steps are just "do this, see that" needs no UI file at all.
/// </summary>
public interface IChecklistAreaUi
{
    string AreaId { get; }

    /// <summary>Buttons and read-outs for one step, drawn between its instructions and its Pass / Fail row.</summary>
    void DrawControls(ChecklistContext ctx, ChecklistStep step) { }

    /// <summary>A non-null result means the plugin decided it: the step shows it and has no Pass / Fail buttons.</summary>
    ChecklistResult? AutoResult(ChecklistContext ctx, ChecklistStep step) => null;

    /// <summary>True when the step records several results itself (through <see cref="ChecklistContext.DrawResultButtons"/>); the window then asks <see cref="OverallResult"/>.</summary>
    bool OwnsResults(ChecklistStep step) => false;

    ChecklistResult? OverallResult(ChecklistContext ctx, ChecklistStep step) => null;

    /// <summary>Extra lines under a step in the copied summary.</summary>
    void AppendSummary(ChecklistContext ctx, ChecklistStep step, StringBuilder sb) { }
}

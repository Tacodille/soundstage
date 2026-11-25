using Dalamud.Bindings.ImGui;

namespace Soundstage.Checklist;

/// <summary>What an area's UI hooks get: the studio, and the saved results with the one place that logs them.</summary>
public sealed class ChecklistContext
{
    private readonly Dictionary<string, string> notes = new();

    public ChecklistContext(Studio studio) => Studio = studio;

    public Studio Studio { get; }

    private Dictionary<string, TestResult> Results => Studio.Config.TestResults;

    public TestResult? Get(string areaId, string stepId, string? sub = null) =>
        Results.TryGetValue(ChecklistRules.Key(areaId, stepId, sub), out var r) ? r : null;

    /// <summary>Saves the result in the config and writes the "[Soundstage/TEST] area step title: STATUS note=..." line.</summary>
    public void Record(string areaId, ChecklistStep step, string? sub, string status, string note)
    {
        Results[ChecklistRules.Key(areaId, step.Id, sub)] = new TestResult { Status = status, Note = note, When = DateTime.Now.ToString("dd/MM/yyyy HH:mm") };
        Studio.Save();
        var title = sub == null ? step.Title : $"{step.Title} [{sub}]";
        Studio.Log.Information("{Line:l}", ChecklistRules.LogLine(areaId, step.Id + (sub == null ? "" : ":" + sub), title, status, note));
    }

    /// <summary>The note box plus Pass and Fail. <paramref name="sub"/> is for a step that records several items.</summary>
    public void DrawResultButtons(string areaId, ChecklistStep step, string? sub = null)
    {
        var key = ChecklistRules.Key(areaId, step.Id, sub);
        notes.TryGetValue(key, out var note);
        note ??= "";
        if (ImGui.InputText("Note##note", ref note, 200)) notes[key] = note;
        if (ImGui.Button("Pass")) Record(areaId, step, sub, "PASS", note);
        ImGui.SameLine();
        if (ImGui.Button("Fail")) Record(areaId, step, sub, "FAIL", note);
    }

    public void ClearAll()
    {
        Results.Clear();
        notes.Clear();
        Studio.Save();
        Studio.Log.Information("{Line:l}", $"{ChecklistRules.LogPrefix} all results cleared");
    }
}

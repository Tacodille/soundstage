using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace Soundstage.Checklist;

/// <summary>
/// The one test checklist for every feature. It draws every <see cref="IChecklistArea"/> in order, each step with its
/// instructions, optional controls from the matching <see cref="IChecklistAreaUi"/>, and Pass / Fail with a note. Every
/// click is logged ("[Soundstage/TEST] area step title: PASS|FAIL note=...") and saved in the config.
/// </summary>
public sealed class ChecklistWindow : Window
{
    private static readonly Vector4 Green = new(0.5f, 1f, 0.6f, 1f);
    private static readonly Vector4 Red = new(1f, 0.5f, 0.4f, 1f);
    private static readonly Vector4 Grey = new(0.7f, 0.7f, 0.7f, 1f);

    private readonly Studio studio;
    private readonly ChecklistContext ctx;
    private readonly IReadOnlyList<IChecklistArea> areas;
    private readonly Dictionary<string, IChecklistAreaUi> uis = new();

    public ChecklistWindow(Studio studio) : base("Soundstage test checklist###SoundstageTest")
    {
        this.studio = studio;
        ctx = new ChecklistContext(studio);
        Size = new Vector2(580, 660);
        SizeCondition = ImGuiCond.FirstUseEver;

        void Log(string line) => studio.Log.Information("{P} {Line:l}", ChecklistRules.LogPrefix, line);
        areas = ChecklistRules.Discover(typeof(ChecklistWindow).Assembly, Log);
        foreach (var type in typeof(ChecklistWindow).Assembly.GetTypes())
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IChecklistAreaUi).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null) continue;
            try
            {
                var ui = (IChecklistAreaUi)Activator.CreateInstance(type)!;
                if (!uis.TryAdd(ui.AreaId, ui)) Log($"{type.Name} repeats the UI for area '{ui.AreaId}', skipped.");
            }
            catch (Exception ex) { Log($"{type.Name} could not be created, skipped: {ex.Message}"); }
        }
        Log($"{areas.Count} area(s): {string.Join(", ", areas.Select(a => a.Id))}");
    }

    public IReadOnlyList<IChecklistArea> Areas => areas;

    /// <summary>Part 12: the photo booth and contact sheet hide the plugin's windows while they capture.</summary>
    public override bool DrawConditions() => !Soundstage.Workflow.WorkflowState.UiHidden;

    public override void Draw()
    {
        AutoFill();

        if (ImGui.Button("Copy summary")) ImGui.SetClipboardText(Summary());
        ImGui.SameLine();
        if (ImGui.Button("Open main window")) studio.OpenMainWindow();
        ImGui.SameLine();
        if (ImGui.Button("Clear all results")) ctx.ClearAll();
        ImGui.TextDisabled(OverallLine());
        ImGui.Separator();

        foreach (var area in areas)
        {
            var (pass, fail, todo) = Count(area);
            ImGui.PushID(area.Id);
            if (ImGui.CollapsingHeader($"{area.Name}   {pass} passed, {fail} failed, {todo} to do###head", ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var step in area.Steps)
                {
                    ImGui.PushID(step.Id);
                    DrawStep(area, step);
                    ImGui.PopID();
                    ImGui.Separator();
                }
            }
            ImGui.PopID();
        }
    }

    private IChecklistAreaUi? Ui(IChecklistArea area) => uis.GetValueOrDefault(area.Id);

    private void DrawStep(IChecklistArea area, ChecklistStep step)
    {
        var ui = Ui(area);
        var auto = ui?.AutoResult(ctx, step);
        var status = StepStatus(area, step).Status;
        ImGui.TextUnformatted($"{step.Id}. {step.Title}");
        ImGui.SameLine();
        ImGui.TextColored(status == "PASS" ? Green : status == "FAIL" ? Red : Grey, status.Length == 0 ? "not done" : status);
        ImGui.TextWrapped(step.Instructions);

        try { ui?.DrawControls(ctx, step); }
        catch (Exception ex) { ImGui.TextColored(Red, "This step's controls hit an error; it is in the log."); studio.Log.Information("{P} {A} {S} controls threw: {Ex}", ChecklistRules.LogPrefix, area.Id, step.Id, ex); }

        if (auto == null && ui?.OwnsResults(step) != true) ctx.DrawResultButtons(area.Id, step);
    }

    // ---- Results ----

    /// <summary>Steps whose status the plugin knows by itself are written when it changes; only a change is logged.</summary>
    private void AutoFill()
    {
        foreach (var area in areas)
        {
            var ui = Ui(area);
            if (ui == null) continue;
            foreach (var step in area.Steps)
            {
                ChecklistResult? auto;
                try { auto = ui.AutoResult(ctx, step); }
                catch { continue; }
                if (auto == null) continue;
                var cur = ctx.Get(area.Id, step.Id);
                if (cur != null && cur.Status == auto.Status && cur.Note == auto.Note) continue;
                ctx.Record(area.Id, step, null, auto.Status, auto.Note);
            }
        }
    }

    private (string Status, string Note) StepStatus(IChecklistArea area, ChecklistStep step)
    {
        var ui = Ui(area);
        try
        {
            if (ui?.AutoResult(ctx, step) is { } auto) return (auto.Status, auto.Note);
            if (ui?.OwnsResults(step) == true) return ui.OverallResult(ctx, step) is { } o ? (o.Status, o.Note) : ("", "");
        }
        catch { return ("", ""); }
        return ctx.Get(area.Id, step.Id) is { } r ? (r.Status, r.Note) : ("", "");
    }

    private (int Pass, int Fail, int Todo) Count(IChecklistArea area)
    {
        var pass = area.Steps.Count(s => StepStatus(area, s).Status == "PASS");
        var fail = area.Steps.Count(s => StepStatus(area, s).Status == "FAIL");
        return (pass, fail, area.Steps.Count - pass - fail);
    }

    private string OverallLine()
    {
        int pass = 0, fail = 0, todo = 0;
        foreach (var a in areas) { var (p, f, t) = Count(a); pass += p; fail += f; todo += t; }
        return $"{pass} passed, {fail} failed, {todo} to do.";
    }

    private string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Soundstage test checklist, {DateTime.Now:dd/MM/yyyy HH:mm}. {OverallLine()}");
        var b = studio.Bridge;
        sb.AppendLine($"Stagehand: {(b.Available ? "available, API revision " + b.Revision : "not available (" + b.Availability + ")")}");
        foreach (var area in areas)
        {
            var (p, f, t) = Count(area);
            sb.AppendLine();
            sb.AppendLine($"== {area.Name} ({area.Id}): {p} passed, {f} failed, {t} to do ==");
            foreach (var s in area.Steps)
            {
                var (status, note) = StepStatus(area, s);
                sb.AppendLine($"{s.Id}. {s.Title}: {(status.Length == 0 ? "not done" : status)}{(note.Length > 0 ? " - " + note : "")}");
                try { Ui(area)?.AppendSummary(ctx, s, sb); } catch { }
            }
        }
        return sb.ToString();
    }
}

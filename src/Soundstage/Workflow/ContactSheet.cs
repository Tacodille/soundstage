// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using Soundstage.Capture;

namespace Soundstage.Workflow;

/// <summary>One Scene on the sheet. <see cref="Apply"/> puts it on stage and returns false if that failed.</summary>
public sealed record SheetItem(string Id, string Label, Func<bool> Apply);

public enum SheetState { Idle, Hiding, Settling, Capturing, Composing }

/// <summary>
/// Applies each picked Scene in turn, waits for the stage to settle, captures, then composes a labelled grid. Tick-driven like the booth,
/// because applying a Scene has to happen on the game's frame. It stops at the first Scene that will not apply or capture and says which.
/// The caller supplies <c>saveState</c> and <c>restoreState</c>: put the original Scene (and camera) back on every way out.
/// </summary>
public sealed class ContactSheet
{
    private const long HideGiveUpMs = 1500;
    private const int MinHiddenFrames = 2;

    private readonly UiHider hider;
    private readonly Func<string, CancellationToken, Task<CaptureResult>> capture;
    private readonly Func<bool> inGpose;
    private readonly Func<long> nowMs;
    private readonly Func<int> settleMs;
    private readonly Func<int> hideSettleMs;
    private readonly Action saveState;
    private readonly Action restoreState;
    private readonly Action<string> log;

    private IReadOnlyList<SheetItem> items = Array.Empty<SheetItem>();
    private readonly List<(string Path, string Label)> shots = new();
    private int index;
    private int columns, cellWidth;
    private string destination = "";
    private string tempDir = "";
    private long phaseStartMs;
    private int frames;
    private Task<CaptureResult>? running;
    private CancellationTokenSource? cancel;

    public ContactSheet(UiHider hider, Func<string, CancellationToken, Task<CaptureResult>> capture, Func<bool> inGpose, Func<long> nowMs,
        Func<int> settleMs, Func<int> hideSettleMs, Action saveState, Action restoreState, Action<string> log)
    {
        this.hider = hider;
        this.capture = capture;
        this.inGpose = inGpose;
        this.nowMs = nowMs;
        this.settleMs = settleMs;
        this.hideSettleMs = hideSettleMs;
        this.saveState = saveState;
        this.restoreState = restoreState;
        this.log = log;
    }

    public SheetState State { get; private set; } = SheetState.Idle;
    public bool Busy => State != SheetState.Idle;
    public string Status { get; private set; } = "";
    public string? ResultPath { get; private set; }
    public int Done => shots.Count;
    public int Total => items.Count;

    public bool Start(IReadOnlyList<SheetItem> picked, string dest, int columns, int cellWidth)
    {
        if (Busy) { Status = "Already running."; return false; }
        if (picked.Count == 0) { Status = "Pick at least one Scene."; return false; }
        if (picked.Count > WorkflowSettings.MaxSheetScenes) { Status = $"A sheet holds at most {WorkflowSettings.MaxSheetScenes} Scenes."; return false; }
        if (!inGpose()) { Status = "The contact sheet only runs in GPose."; return false; }
        items = picked;
        destination = dest;
        this.columns = columns;
        this.cellWidth = cellWidth;
        shots.Clear();
        ResultPath = null;
        index = 0;
        tempDir = Path.Combine(Path.GetTempPath(), "Soundstage-sheet-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            saveState();
        }
        catch (Exception ex) { Status = "Could not start: " + ex.Message; Cleanup(); return false; }
        hider.Hide();
        phaseStartMs = nowMs();
        frames = 0;
        State = SheetState.Hiding;
        Status = "Hiding the UI...";
        log($"[Soundstage/WORK] sheet start, {items.Count} Scene(s) -> {dest}");
        return true;
    }

    public void Cancel(string why = "Cancelled.")
    {
        if (Busy) Finish(why);
    }

    public void Tick()
    {
        if (!Busy) return;
        try
        {
            if (!inGpose()) { Finish("Left GPose, so the sheet cancelled."); return; }
            switch (State)
            {
                case SheetState.Hiding:
                    frames++;
                    var waited = nowMs() - phaseStartMs;
                    var ready = waited >= hideSettleMs() && frames >= MinHiddenFrames && hider.AllSettled();
                    if (!ready && waited < HideGiveUpMs + hideSettleMs()) return;
                    ApplyCurrent();
                    return;

                case SheetState.Settling:
                    frames++;
                    if (nowMs() - phaseStartMs < settleMs() || frames < MinHiddenFrames) return;
                    cancel = new CancellationTokenSource();
                    running = capture(Path.Combine(tempDir, $"{index + 1:00}.png"), cancel.Token);
                    State = SheetState.Capturing;
                    Status = $"Capturing {items[index].Label} ({index + 1} of {items.Count})...";
                    return;

                case SheetState.Capturing:
                    if (running == null || !running.IsCompleted) return;
                    if (running.IsFaulted) { Finish($"The capture for '{items[index].Label}' failed: " + (running.Exception?.GetBaseException().Message ?? "unknown")); return; }
                    if (running.IsCanceled) { Finish($"The capture for '{items[index].Label}' was cancelled."); return; }
                    var r = running.Result;
                    running = null;
                    if (!r.Ok) { Finish($"Stopped at '{items[index].Label}': {r.Message}"); return; }
                    shots.Add((r.Path, items[index].Label));
                    index++;
                    if (index >= items.Count) { State = SheetState.Composing; Status = "Making the sheet..."; return; }
                    ApplyCurrent();
                    return;

                case SheetState.Composing:
                    SheetComposer.ComposeToFile(shots, columns, cellWidth, destination);
                    ResultPath = destination;
                    Finish($"Saved the sheet ({shots.Count} Scene(s)) to {destination}.");
                    return;
            }
        }
        catch (Exception ex)
        {
            Finish("The sheet failed: " + ex.Message);
        }
    }

    private void ApplyCurrent()
    {
        var item = items[index];
        bool ok;
        try { ok = item.Apply(); }
        catch (Exception ex) { Finish($"Stopped at '{item.Label}': it threw while applying ({ex.Message})."); return; }
        if (!ok) { Finish($"Stopped at '{item.Label}': the Scene would not apply."); return; }
        phaseStartMs = nowMs();
        frames = 0;
        State = SheetState.Settling;
        Status = $"Settling {item.Label} ({index + 1} of {items.Count})...";
    }

    private void Finish(string status)
    {
        try { cancel?.Cancel(); } catch (Exception) { }
        var notes = new List<string>();
        try { hider.Restore(); } catch (Exception ex) { notes.Add("UI: " + ex.Message); }
        try { restoreState(); } catch (Exception ex) { notes.Add("Scene: " + ex.Message); }
        cancel = null;
        running = null;
        State = SheetState.Idle;
        Cleanup();
        var extra = new List<string>(notes);
        if (hider.Problems.Count > 0) extra.Add("could not put back " + string.Join(", ", hider.Problems));
        Status = extra.Count > 0 ? status + " Restore problems: " + string.Join("; ", extra) + "." : status;
        log("[Soundstage/WORK] sheet finished: " + Status);
    }

    private void Cleanup()
    {
        try { if (tempDir.Length > 0 && Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (Exception) { }
        tempDir = "";
    }
}

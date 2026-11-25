// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using Soundstage.Capture;

namespace Soundstage.Workflow;

public enum BoothState { Idle, Counting, Hiding, Capturing }

/// <summary>
/// The photo booth, driven one tick per frame: count down, hide, wait for the hidden frame, capture, restore. Restore runs on every way
/// out (success, failure, an exception, Cancel, leaving GPose). "Capture now" is the same thing with no countdown.
/// </summary>
public sealed class PhotoBooth
{
    public const int MinSeconds = 3;
    public const int MaxSeconds = 30;
    private const long HideGiveUpMs = 1500;
    private const int MinHiddenFrames = 2;

    private readonly UiHider hider;
    private readonly Func<string, CancellationToken, Task<CaptureResult>> capture;
    private readonly Func<bool> inGpose;
    private readonly Func<long> nowMs;
    private readonly Func<int> settleMs;
    private readonly Action<string> log;

    private long endMs;
    private long hideStartMs;
    private int hiddenFrames;
    private bool requireGpose;
    private string destination = "";
    private Task<CaptureResult>? running;
    private CancellationTokenSource? cancel;

    public PhotoBooth(UiHider hider, Func<string, CancellationToken, Task<CaptureResult>> capture, Func<bool> inGpose, Func<long> nowMs, Func<int> settleMs, Action<string> log)
    {
        this.hider = hider;
        this.capture = capture;
        this.inGpose = inGpose;
        this.nowMs = nowMs;
        this.settleMs = settleMs;
        this.log = log;
    }

    public BoothState State { get; private set; } = BoothState.Idle;
    public bool Busy => State != BoothState.Idle;
    public string Status { get; private set; } = "";
    public CaptureResult? Last { get; private set; }

    public int SecondsLeft => State == BoothState.Counting ? (int)Math.Ceiling(Math.Max(0, endMs - nowMs()) / 1000.0) : 0;

    /// <summary>Starts the countdown (clamped to 3 to 30 s). <paramref name="seconds"/> of 0 captures at once. A booth run needs GPose; "capture now" does not.</summary>
    public bool Start(int seconds, string dest, bool needGpose)
    {
        if (Busy) { Status = "Already running."; return false; }
        if (needGpose && !inGpose()) { Status = "The photo booth only runs in GPose."; return false; }
        requireGpose = needGpose;
        destination = dest;
        var secs = seconds <= 0 ? 0 : Math.Clamp(seconds, MinSeconds, MaxSeconds);
        endMs = nowMs() + secs * 1000L;
        State = BoothState.Counting;
        Status = secs == 0 ? "Capturing..." : $"Capturing in {secs}...";
        log($"[Soundstage/WORK] booth start in {secs} s -> {dest}");
        return true;
    }

    public void Cancel(string why = "Cancelled.")
    {
        if (!Busy) return;
        Finish(null, why);
    }

    /// <summary>Call once a frame from the draw event.</summary>
    public void Tick()
    {
        if (!Busy) return;
        try
        {
            if (requireGpose && !inGpose()) { Finish(null, "Left GPose, so the booth cancelled."); return; }
            switch (State)
            {
                case BoothState.Counting:
                    if (nowMs() < endMs) { Status = $"Capturing in {SecondsLeft}..."; return; }
                    hider.Hide();
                    hideStartMs = nowMs();
                    hiddenFrames = 0;
                    State = BoothState.Hiding;
                    Status = "Hiding the UI...";
                    return;

                case BoothState.Hiding:
                    hiddenFrames++;
                    var waited = nowMs() - hideStartMs;
                    var ready = waited >= settleMs() && hiddenFrames >= MinHiddenFrames && hider.AllSettled();
                    if (!ready && waited < HideGiveUpMs + settleMs()) return;
                    if (!ready) log("[Soundstage/WORK] the UI did not report hidden in time; capturing anyway.");
                    cancel = new CancellationTokenSource();
                    running = capture(destination, cancel.Token);
                    State = BoothState.Capturing;
                    Status = "Capturing...";
                    return;

                case BoothState.Capturing:
                    if (running == null || !running.IsCompleted) return;
                    if (running.IsFaulted) { Finish(null, "Capture failed: " + (running.Exception?.GetBaseException().Message ?? "unknown")); return; }
                    if (running.IsCanceled) { Finish(null, "Capture cancelled."); return; }
                    var result = running.Result;
                    Finish(result, result.Ok ? "Saved " + result.Path + $" ({result.Route}, {result.Milliseconds} ms)." : result.Message);
                    return;
            }
        }
        catch (Exception ex)
        {
            Finish(null, "Capture failed: " + ex.Message);
        }
    }

    private void Finish(CaptureResult? result, string status)
    {
        try { cancel?.Cancel(); } catch (Exception) { }
        try { hider.Restore(); }
        catch (Exception ex) { log("[Soundstage/WORK] restore threw: " + ex.Message); }   // Restore itself does not throw; this is belt and braces
        finally
        {
            cancel = null;
            running = null;
            State = BoothState.Idle;
            Last = result ?? new CaptureResult(false, "", "", 0, status);
            Status = hider.Problems.Count > 0 ? status + " Could not put back: " + string.Join(", ", hider.Problems) + "." : status;
            log("[Soundstage/WORK] booth finished: " + Status);
        }
    }
}

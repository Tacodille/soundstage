// No Dalamud types in this file.
using Soundstage.Face;

namespace Soundstage.Expression;

public enum CalibState { Idle, Running, Done, Cancelled, Failed }

/// <summary>
/// Part 17.2. "Calibrate my face": reads the face as it stands, then nudges each picked bone up and down one step at a time, takes a picture
/// each time and stores how the blendshapes changed. About 80 captures, ticked once a frame so the game keeps running, with progress and a
/// cancel. Progress is saved as it goes, so a run that was stopped carries on from the first move it had not measured. The original expression
/// goes back on every way out.
/// </summary>
public sealed class Calibrator
{
    public const int SaveEvery = 6;
    public const int FailureLimit = 4;

    private readonly IExpressionRig rig;
    private readonly ExpressionTableStore store;
    private readonly ExpressionSettings settings;
    private readonly Shooter shooter;
    private readonly Action<string> log;
    private readonly Func<DateTime> now;

    private string original = "";
    private string originalExpression = "";
    private ExpressionTable table = new();
    private readonly List<(ChannelRecord Channel, bool Plus)> queue = new();
    private int next;
    private bool baselineDone;
    private bool baselineStarted;
    private int sinceSave;
    private int failuresInARow;
    private int skipped;
    private (ChannelRecord Channel, bool Plus)? current;

    public Calibrator(IExpressionRig rig, IFaceAnalyser analyser, ExpressionTableStore store, ExpressionSettings settings, Action<string> log, Func<DateTime>? now = null)
    {
        this.rig = rig;
        this.store = store;
        this.settings = settings;
        this.log = log;
        this.now = now ?? (() => DateTime.UtcNow);
        shooter = new Shooter(rig, analyser, () => settings.SettleFrames);
    }

    public CalibState State { get; private set; } = CalibState.Idle;
    public string Status { get; private set; } = "";
    public int Done { get; private set; }
    public int Total { get; private set; }
    public ExpressionTable Table => table;
    public bool Running => State == CalibState.Running;
    public float Progress => Total == 0 ? 0f : Math.Clamp((float)Done / Total, 0f, 1f);

    public bool Start()
    {
        if (Running) return false;
        if (!rig.Ready(out var why)) return Stop(CalibState.Failed, why, restore: false);
        var key = rig.Key(out why);
        if (key == null) return Stop(CalibState.Failed, "Could not read the character's face: " + why, restore: false);
        var snap = rig.Snapshot();
        if (snap == null) return Stop(CalibState.Failed, "Brio could not read the current pose.", restore: false);
        var expr = ExpressionApply.Build(snap, Array.Empty<ExpressionApply.Offset>(), out var err);
        if (expr == null) return Stop(CalibState.Failed, err, restore: false);

        var wanted = ExpressionChannels.Build(snap, settings);
        if (wanted.Count == 0) return Stop(CalibState.Failed, "The pose has no face bones to measure. Is this character's face loaded in Brio?", restore: false);

        original = snap;
        originalExpression = expr;
        var existing = store.Load(key, out var problem);
        if (problem.Length > 0) log($"[Soundstage/EXPR] the stored table could not be read ({problem}); starting a new one.");
        table = existing ?? ExpressionTable.For(key);

        // Keep the measurements that still match a wanted channel (same bone, axis and step), so a stopped run carries on.
        var kept = new List<ChannelRecord>();
        foreach (var w in wanted)
        {
            var old = table.Channels.FirstOrDefault(c => c.Id == w.Id && MathF.Abs(c.Step - w.Step) < 1e-6f && MathF.Abs(c.Max - w.Max) < 1e-6f);
            kept.Add(old != null && old.Measured ? old : w);
        }
        table.Channels = kept;

        queue.Clear();
        foreach (var c in table.Channels.Where(c => !c.Measured))
        {
            queue.Add((c, true));
            queue.Add((c, false));
        }
        Total = 1 + queue.Count + table.Channels.Count(c => c.Measured) * 2;
        Done = Total - queue.Count - 1;
        next = 0;
        failuresInARow = 0;
        skipped = 0;
        sinceSave = 0;
        baselineDone = false;
        baselineStarted = false;
        current = null;
        shooter.Reset();
        State = CalibState.Running;
        Status = queue.Count == 0 ? "This face is already calibrated; re-reading the baseline." : $"Calibrating: {Done} of {Total}.";
        log($"[Soundstage/EXPR] calibrate start key={key.Id} channels={table.Channels.Count} to-measure={queue.Count / 2} total-captures={Total}");
        return true;
    }

    public void Cancel()
    {
        if (!Running) return;
        Stop(CalibState.Cancelled, $"Stopped at {Done} of {Total}. Your expression is back. Press Calibrate again to carry on.", restore: true);
    }

    /// <summary>Call once a frame.</summary>
    public void Tick()
    {
        if (!Running) return;
        try
        {
            if (!rig.Ready(out var why)) { Stop(CalibState.Failed, "Stopped: " + why, restore: true); return; }
            shooter.Tick();
            if (!baselineStarted) { shooter.Start(originalExpression); baselineStarted = true; return; }
            if (!shooter.Finished) return;

            if (shooter.State == ShotState.Failed) { if (!Failed()) return; }
            else Accept(shooter.Result!);

            shooter.Reset();
            Advance();
        }
        catch (Exception ex)
        {
            Stop(CalibState.Failed, "Calibration hit an error: " + ex.Message, restore: true);
        }
    }

    private bool Failed()
    {
        failuresInARow++;
        var reason = shooter.Error;
        log($"[Soundstage/EXPR] calibrate capture failed ({failuresInARow} in a row): {reason}");
        if (!baselineDone || failuresInARow >= FailureLimit)
        {
            Stop(CalibState.Failed, (baselineDone ? $"{failuresInARow} pictures in a row had no readable face. " : "The first picture had no readable face. ") +
                reason + " Frame the face close up (the Camera tab's close-up) and try again.", restore: true);
            return false;
        }
        skipped++;
        current = null;
        return true;
    }

    private void Accept(FaceResult r)
    {
        failuresInARow = 0;
        if (!baselineDone)
        {
            table.Baseline = r.Blendshapes.Where(kv => kv.Key != Weights.Neutral).ToDictionary(kv => kv.Key, kv => kv.Value);
            baselineDone = true;
            Done++;
            Save();
            return;
        }
        if (current is not { } cur) return;
        var delta = new Dictionary<string, float>();
        foreach (var kv in r.Blendshapes)
        {
            if (kv.Key == Weights.Neutral) continue;
            var d = kv.Value - (table.Baseline.TryGetValue(kv.Key, out var b) ? b : 0f);
            if (MathF.Abs(d) > 1e-4f) delta[kv.Key] = d;
        }
        if (cur.Plus) cur.Channel.Plus = delta; else cur.Channel.Minus = delta;
        Done++;
        if (++sinceSave >= SaveEvery) { Save(); sinceSave = 0; }
        Status = $"Calibrating: {Done} of {Total}.";
    }

    private void Advance()
    {
        while (next < queue.Count)
        {
            var (ch, plus) = queue[next++];
            // A half-measured channel from a failed shot is left unmeasured, so a resume does the pair again.
            var amount = (plus ? 1f : -1f) * ch.Step;
            var json = ExpressionApply.Build(original, new[] { new ExpressionApply.Offset(ch.Bone, ch.Kind, ch.Axis, amount) }, out var err);
            if (json == null)
            {
                log($"[Soundstage/EXPR] skipped {ch.Id}: {err}");
                skipped++;
                Done++;
                continue;
            }
            current = (ch, plus);
            shooter.Start(json);
            return;
        }
        Finish();
    }

    private void Finish()
    {
        // Any channel with only one direction measured counts as unmeasured, so a later run does both again.
        foreach (var c in table.Channels.Where(c => (c.Plus == null) != (c.Minus == null)))
        {
            c.Plus = null;
            c.Minus = null;
        }
        var ok = table.Complete;
        Stop(CalibState.Done, ok
            ? $"Done. {table.Channels.Count} bone moves measured for {table.Key.Id}."
            : $"Done, but {table.Channels.Count(c => !c.Measured)} of {table.Channels.Count} moves could not be measured ({skipped} pictures skipped). Calibrate again to fill the gaps.", restore: true);
    }

    private bool Stop(CalibState state, string status, bool restore)
    {
        shooter.Reset();
        if (restore && originalExpression.Length > 0 && !rig.Load(originalExpression, out var why))
            status += " The original expression could not be put back: " + why;
        if (restore) Save();
        State = state;
        Status = status;
        log($"[Soundstage/EXPR] calibrate {state}: {status}");
        return false;
    }

    private void Save()
    {
        table.SavedUtc = now().ToString("o");
        if (!store.Save(table, out var problem)) log($"[Soundstage/EXPR] could not save the table: {problem}");
    }
}

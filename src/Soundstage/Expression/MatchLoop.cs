// No Dalamud types in this file.
using Soundstage.Face;

namespace Soundstage.Expression;

public enum MatchState { Idle, Running, Done, Failed, Cancelled }

/// <summary>One pass of the loop: what the game's face scored after these offsets.</summary>
public sealed record MatchIteration(int Index, float Error, float Score, float[] Steps, float[] Observed);

/// <summary>
/// Part 17.5. Reads the face as it stands, then up to <see cref="ExpressionSettings.MaxIterations"/> times: solve for bone offsets that bring the
/// blendshapes to the photo's, load them through Brio, take a picture, read the face, and measure the error. After the first read it stops as
/// soon as a pass improves on the best by less than <see cref="ExpressionSettings.Epsilon"/>. The best pass is kept, not the last. The score is
/// 100 x (1 - weighted mean absolute error / 0.5): the model's own number for how close the blendshapes are, and not a judgement of likeness.
/// The head angle is compared and reported, but bones cannot turn the head, so it takes no part in the error (the Face tab aims the camera).
/// </summary>
public sealed class MatchLoop
{
    private readonly IExpressionRig rig;
    private readonly ExpressionSettings settings;
    private readonly Shooter shooter;
    private readonly Action<string> log;

    private ResponseModel model = null!;
    private float[] weights = Array.Empty<float>();
    private float[] target = Array.Empty<float>();
    private FaceResult photo = FaceResult.Unavailable("none");
    private string original = "";
    private string originalExpression = "";
    private float[] currentSteps = Array.Empty<float>();
    private float[] currentObserved = Array.Empty<float>();
    private readonly List<MatchIteration> iterations = new();
    private int best = -1;
    private bool awaiting;

    public MatchLoop(IExpressionRig rig, IFaceAnalyser analyser, ExpressionSettings settings, Action<string> log)
    {
        this.rig = rig;
        this.settings = settings;
        this.log = log;
        shooter = new Shooter(rig, analyser, () => settings.SettleFrames);
    }

    public MatchState State { get; private set; } = MatchState.Idle;
    public string Status { get; private set; } = "";
    public bool Running => State == MatchState.Running;
    public IReadOnlyList<MatchIteration> Iterations => iterations;
    public int BestIndex => best;
    public float? ScoreBefore => iterations.Count > 0 ? iterations[0].Score : null;
    public float? ScoreAfter => best >= 0 ? iterations[best].Score : null;

    /// <summary>The blendshape names the loop works on, in order, so a caller can line up <see cref="MatchIteration.Observed"/> and <see cref="Target"/>.</summary>
    public string[] Names => model?.Names ?? Array.Empty<string>();
    public float[] Target => target;

    /// <summary>What the loop did to the head angle question, in a sentence ("the picture's head is 12 degrees further round").</summary>
    public string AngleNote { get; private set; } = "";

    /// <summary>The bone moves of the best pass, for the log and the tab.</summary>
    public IReadOnlyList<ExpressionApply.Offset> BestOffsets =>
        best < 0 || model == null ? Array.Empty<ExpressionApply.Offset>() : ExpressionChannels.ToOffsets(model.Channels, iterations[best].Steps);

    public bool Start(FaceResult photoFace, ExpressionTable table)
    {
        if (Running) return false;
        if (!photoFace.Available) return Stop(MatchState.Failed, photoFace.Reason, restore: false);
        if (!table.Complete && table.MeasuredCount == 0) return Stop(MatchState.Failed, "This face has not been calibrated yet. Press 'Calibrate my face' first.", restore: false);
        if (!rig.Ready(out var why)) return Stop(MatchState.Failed, why, restore: false);
        var snap = rig.Snapshot();
        if (snap == null) return Stop(MatchState.Failed, "Brio could not read the current pose.", restore: false);
        var expr = ExpressionApply.Build(snap, Array.Empty<ExpressionApply.Offset>(), out var err);
        if (expr == null) return Stop(MatchState.Failed, err, restore: false);

        model = ResponseModel.From(table);
        if (model.Channels.Count == 0) return Stop(MatchState.Failed, "The table has no measured bone moves.", restore: false);
        weights = Weights.For(model.Names);
        photo = photoFace;
        target = model.Names.Select(n => photoFace.Blend(n)).ToArray();
        original = snap;
        originalExpression = expr;
        iterations.Clear();
        best = -1;
        AngleNote = "";
        currentSteps = new float[model.Channels.Count];
        currentObserved = Array.Empty<float>();
        shooter.Reset();
        shooter.Start(null);
        awaiting = true;
        State = MatchState.Running;
        Status = "Reading the face as it is now...";
        log($"[Soundstage/EXPR] match start channels={model.Channels.Count} names={model.Names.Length}");
        return true;
    }

    public void Cancel()
    {
        if (!Running) return;
        Stop(MatchState.Cancelled, "Stopped. Your expression is back as it was.", restore: true);
    }

    /// <summary>Puts the expression from before the match back (Brio keeps it in its own undo history too).</summary>
    public bool Undo(out string why)
    {
        why = "";
        if (originalExpression.Length == 0) { why = "Nothing to undo."; return false; }
        return rig.Load(originalExpression, out why);
    }

    public void Tick()
    {
        if (!Running) return;
        try
        {
            if (!rig.Ready(out var why)) { Stop(MatchState.Failed, "Stopped: " + why, restore: true); return; }
            shooter.Tick();
            if (!awaiting || !shooter.Finished) return;

            if (shooter.State == ShotState.Failed)
            {
                var reason = shooter.Error;
                if (iterations.Count == 0) { Stop(MatchState.Failed, "Could not read the face in the game. " + reason + " Frame the face close up and try again.", restore: true); return; }
                Finish("A picture could not be read (" + reason + "), so it stopped with the best so far.");
                return;
            }

            Record(shooter.Result!);
            shooter.Reset();
            awaiting = false;
            Step();
        }
        catch (Exception ex) { Stop(MatchState.Failed, "The match hit an error: " + ex.Message, restore: true); }
    }

    private void Record(FaceResult r)
    {
        var observed = model.Names.Select(n => r.Blend(n)).ToArray();
        var err = Weights.WeightedError(observed, target, weights);
        var it = new MatchIteration(iterations.Count, err, Weights.Score(err), (float[])currentSteps.Clone(), observed);
        iterations.Add(it);
        currentObserved = observed;
        if (iterations.Count == 1) AngleNote = Angle(r);
        var improved = best < 0 ? float.PositiveInfinity : iterations[best].Error - err;
        if (best < 0 || err < iterations[best].Error) best = it.Index;
        lastImprovement = improved;
        log($"[Soundstage/EXPR] match pass {it.Index}: error {err:0.0000} score {it.Score:0.0} improvement {(float.IsInfinity(improved) ? "n/a" : improved.ToString("0.0000"))}");
        Status = $"Pass {it.Index} of up to {settings.MaxIterations}: score {it.Score:0}.";
    }

    private float lastImprovement;

    private void Step()
    {
        var done = iterations.Count - 1;   // passes made beyond the first read
        if (done >= settings.MaxIterations) { Finish($"Stopped at the {settings.MaxIterations}-pass limit."); return; }
        if (done >= 1 && lastImprovement < settings.Epsilon) { Finish("Stopped: the last pass did not improve it any more."); return; }

        // Closed loop: the table is a model, the picture is the truth. The gap between them is carried into the next solve.
        var predicted = model.Predict(currentSteps);
        var wanted = new float[model.Names.Length];
        for (var i = 0; i < wanted.Length; i++)
        {
            var bias = currentObserved[i] - predicted[i];
            wanted[i] = target[i] - bias - model.Baseline[i];
        }
        var steps = Solver.Clamp(model, Solver.Solve(model, wanted, weights));
        var json = ExpressionApply.Build(original, ExpressionChannels.ToOffsets(model.Channels, steps), out var err);
        if (json == null) { Finish("Could not build the pose: " + err); return; }
        currentSteps = steps;
        awaiting = true;
        shooter.Start(json);
        Status = $"Pass {done + 1} of up to {settings.MaxIterations}...";
    }

    private void Finish(string why)
    {
        // Keep the best pass, not the last. Pass 0 is "nothing moved", which puts the original expression back.
        var bestSteps = iterations[best].Steps;
        var json = best == 0
            ? originalExpression
            : ExpressionApply.Build(original, ExpressionChannels.ToOffsets(model.Channels, bestSteps), out _);
        string tail = "";
        var needLoad = best != iterations.Count - 1;   // the best pass is the one on screen already
        if (needLoad && json != null && !rig.Load(json, out var loadWhy)) tail = " The best result could not be loaded: " + loadWhy;
        var b = iterations[best];
        var first = iterations[0];
        var status = best == 0
            ? $"No pass beat the starting face (score {first.Score:0}), so nothing was changed. {why}"
            : $"Score {first.Score:0} before, {b.Score:0} after (pass {b.Index}). {why}";
        Stop(MatchState.Done, status + tail, restore: false);
    }

    private bool Stop(MatchState state, string status, bool restore)
    {
        shooter.Reset();
        awaiting = false;
        if (restore && originalExpression.Length > 0 && !rig.Load(originalExpression, out var why))
            status += " The original expression could not be put back: " + why;
        State = state;
        Status = status;
        log($"[Soundstage/EXPR] match {state}: {status}");
        return false;
    }

    private string Angle(FaceResult game)
    {
        float dy = photo.YawDeg - game.YawDeg, dp = photo.PitchDeg - game.PitchDeg, dr = photo.RollDeg - game.RollDeg;
        if (MathF.Abs(dy) < 5f && MathF.Abs(dp) < 5f && MathF.Abs(dr) < 5f) return "The head angle already agrees with the photo.";
        return $"The photo's head is {dy:+0;-0;0} degrees round, {dp:+0;-0;0} up and {dr:+0;-0;0} tilted compared with the game. Bones cannot turn the head: use the Face tab to aim the camera.";
    }
}

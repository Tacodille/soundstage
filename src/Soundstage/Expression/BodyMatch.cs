// No Dalamud types in this file.
using System.Numerics;
using Soundstage.Face;

namespace Soundstage.Expression;

/// <summary>Settings of the body section of the Expression tab, kept in <see cref="Configuration.Sections"/> under "expression-body".</summary>
public sealed class BodySettings
{
    public const string Key = "expression-body";

    public string LastImage { get; set; } = "";

    /// <summary>A Camera tab framing id. The whole body has to be in the picture for the pose model to read it.</summary>
    public string Framing { get; set; } = "full-body";

    /// <summary>Swap the photo's left and right.</summary>
    public bool Mirror { get; set; }

    /// <summary>Passes after the first read. Every pass is a picture and a body pose read.</summary>
    public int MaxIterations { get; set; } = 4;

    /// <summary>A pass has to beat the best outline score by this many points (out of 100) or the loop stops.</summary>
    public float Epsilon { get; set; } = 1f;

    /// <summary>How much of the gap between what was asked and what the game showed is added to the next ask. 1 carries all of it.</summary>
    public float Gain { get; set; } = 1f;

    public int SettleFrames { get; set; } = 6;

    /// <summary>Landmarks below this visibility are ignored.</summary>
    public float MinVisibility { get; set; } = 0.3f;

    /// <summary>Apply even when the skeleton check says the map does not fit the character's current pose.</summary>
    public bool SkipSkeletonCheck { get; set; }

    public List<ExpressionTrial> Trials { get; set; } = new();
    public bool ShowExperimental { get; set; }
}

/// <summary>The same rating rule as the face matcher (part 17.8), applied to the body's own five photos.</summary>
public static class BodyTrials
{
    public static float? Median(BodySettings s) => ExpressionTrials.Median(s.Trials.Select(t => t.Rating));

    public static bool IsExperimental(BodySettings s)
        => s.Trials.Count(t => t.Rating is >= 1 and <= 5) >= ExpressionTrials.Needed && Median(s) is { } m && m < ExpressionTrials.Recognisable;

    public static string Describe(BodySettings s)
    {
        var n = s.Trials.Count(t => t.Rating is >= 1 and <= 5);
        if (n == 0) return $"No rated body matches yet. Rate {ExpressionTrials.Needed} to find out whether this is good enough to rely on.";
        var m = Median(s)!.Value;
        if (n < ExpressionTrials.Needed) return $"{n} of {ExpressionTrials.Needed} body matches rated so far (median {m:0.#} of 5).";
        return IsExperimental(s)
            ? $"Experimental: median rating {m:0.#} of 5 over {n} matches, under 'recognisable' (3)."
            : $"Median rating {m:0.#} of 5 over {n} matches.";
    }
}

/// <summary>One pass of the body loop: the outline score the game's picture earned, and how far its segments pointed from the photo's.</summary>
public sealed record BodyIteration(int Index, float Score, float? AngleErrorDeg, IReadOnlyDictionary<string, Vector3> Commands, IReadOnlyDictionary<string, Vector3> Observed, string? Json, int Clamped);

/// <summary>One round of "load a pose, let the game catch up, take a picture, read the body", ticked once a frame like <see cref="Shooter"/>.</summary>
public sealed class BodyShooter
{
    private readonly IExpressionRig rig;
    private readonly Func<string, BodyPoseResult> analyse;
    private readonly Func<int> settleFrames;
    private int frames;
    private string path = "";
    private Task<BodyPoseResult>? analysis;

    public BodyShooter(IExpressionRig rig, Func<string, BodyPoseResult> analyse, Func<int> settleFrames)
    {
        this.rig = rig;
        this.analyse = analyse;
        this.settleFrames = settleFrames;
    }

    public ShotState State { get; private set; } = ShotState.Idle;
    public BodyPoseResult? Result { get; private set; }
    public string Error { get; private set; } = "";
    public bool Busy => State is ShotState.Settling or ShotState.Capturing or ShotState.Analysing;
    public bool Finished => State is ShotState.Done or ShotState.Failed;

    public void Start(string? poseJson)
    {
        Result = null;
        Error = "";
        if (poseJson != null && !rig.Load(poseJson, out var why)) { Fail("Brio would not load the pose: " + why); return; }
        frames = Math.Max(0, settleFrames());
        State = ShotState.Settling;
    }

    public void Reset() { Cancel(); State = ShotState.Idle; Result = null; Error = ""; }

    public void Cancel()
    {
        if (path.Length > 0) { rig.Discard(path); path = ""; }
        analysis = null;
        if (Busy) State = ShotState.Idle;
    }

    public void Tick()
    {
        try
        {
            switch (State)
            {
                case ShotState.Settling:
                    if (frames-- > 0) return;
                    if (!rig.BeginCapture(out var why)) { Fail("Could not start a capture: " + why); return; }
                    State = ShotState.Capturing;
                    return;

                case ShotState.Capturing:
                    var poll = rig.Poll(out var p, out var pwhy);
                    if (poll == CapturePoll.Pending) return;
                    if (poll == CapturePoll.Failed) { Fail("Capture failed: " + pwhy); return; }
                    path = p;
                    var file = p;
                    analysis = Task.Run(() => analyse(file));
                    State = ShotState.Analysing;
                    return;

                case ShotState.Analysing:
                    if (analysis is not { IsCompleted: true }) return;
                    BodyPoseResult r;
                    try { r = analysis.Result; } catch (Exception ex) { r = BodyPoseResult.Unavailable("The body analysis failed: " + ex.Message); }
                    analysis = null;
                    rig.Discard(path);
                    path = "";
                    if (!r.Available) { Fail(r.Reason); return; }
                    Result = r;
                    State = ShotState.Done;
                    return;
            }
        }
        catch (Exception ex) { Fail("Unexpected: " + ex.Message); }
    }

    private void Fail(string why)
    {
        if (path.Length > 0) { rig.Discard(path); path = ""; }
        Error = why;
        State = ShotState.Failed;
    }
}

/// <summary>
/// Part 18.5. Copies a photo's body pose onto the character through Brio, with the outline of the game's picture as the score.
///
/// Pass 0 reads the character as it stands. Pass 1 asks for the photo's segment directions. Each later pass adds the gap between what was asked
/// for and what the game's picture showed (read by the same pose model) to the next ask, so a bone that does not end up where the maths says is
/// pushed on. The score is the outline overlap (see <see cref="SilhouetteScore"/>), the best pass is kept and not the last, and the loop stops at
/// the cap or when a pass from the second on beats the best by less than <see cref="BodySettings.Epsilon"/> points.
/// Only body bones are loaded: the face, hair and ears stay as they are.
/// </summary>
public sealed class BodyMatch
{
    private readonly IExpressionRig rig;
    private readonly BodySettings settings;
    private readonly BodyMap map;
    private readonly BodyShooter shooter;
    private readonly Action<string> log;

    private Dictionary<string, Vector3> target = new();
    private SilhouetteGrid photoSilhouette = new();
    private BodyRetarget.Options options = new();
    private string original = "";
    private string originalBody = "";
    private readonly List<BodyIteration> iterations = new();
    private Dictionary<string, Vector3> currentCommands = new();
    private string? currentJson;
    private int currentClamped;
    private int best = -1;
    private bool awaiting;
    private float lastImprovement;

    public BodyMatch(IExpressionRig rig, Func<string, BodyPoseResult> analyse, BodySettings settings, BodyMap map, Action<string> log)
    {
        this.rig = rig;
        this.settings = settings;
        this.map = map;
        this.log = log;
        shooter = new BodyShooter(rig, analyse, () => settings.SettleFrames);
    }

    public MatchState State { get; private set; } = MatchState.Idle;
    public string Status { get; private set; } = "";
    public bool Running => State == MatchState.Running;
    public IReadOnlyList<BodyIteration> Iterations => iterations;
    public int BestIndex => best;
    public float? ScoreBefore => iterations.Count > 0 ? iterations[0].Score : null;
    public float? ScoreAfter => best >= 0 ? iterations[best].Score : null;
    public IReadOnlyDictionary<string, Vector3> Target => target;

    /// <summary>Notes from the skeleton check and the solver (bones the pose lacks, segments past their limit), for the tab.</summary>
    public IReadOnlyList<string> Notes => notes;
    private readonly List<string> notes = new();

    public bool Start(BodyPoseResult photo, bool mirror)
    {
        if (Running) return false;
        notes.Clear();
        if (!photo.Available) return Stop(MatchState.Failed, photo.Reason, restore: false);
        if (photo.Silhouette == null) return Stop(MatchState.Failed, "The helper gave no outline for the photo, so there is nothing to score against. Use a photo with one clear person.", restore: false);
        if (!rig.Ready(out var why)) return Stop(MatchState.Failed, why, restore: false);
        var snap = rig.Snapshot();
        if (snap == null) return Stop(MatchState.Failed, "Brio could not read the current pose.", restore: false);
        var body = BodyRetarget.BodyOnly(snap, out var err);
        if (body == null) return Stop(MatchState.Failed, err, restore: false);

        var check = BodyRetarget.CheckSkeleton(snap, map);
        if (check.Missing.Count > 0) notes.Add("The pose has no " + string.Join(", ", check.Missing.Take(6)) + (check.Missing.Count > 6 ? " and more" : "") + ".");
        notes.AddRange(check.Notes);
        if (check.DoesNotFit && !settings.SkipSkeletonCheck)
            return Stop(MatchState.Failed, "The body map does not fit this character as it stands now (" + check.Wrong + " of " + check.Checked + " segments point the wrong way at rest). Reset the pose in Brio and try again, or tick 'Apply anyway' under Tuning. Nothing was changed.", restore: false);

        options = new BodyRetarget.Options { Mirror = mirror, MinVisibility = settings.MinVisibility };
        target = BodyRetarget.Directions(photo, map, options);
        if (target.Count == 0) return Stop(MatchState.Failed, "None of the photo's joints are visible enough to copy.", restore: false);
        photoSilhouette = mirror ? photo.Silhouette.Flipped() : photo.Silhouette;
        original = snap;
        originalBody = body;
        iterations.Clear();
        best = -1;
        currentCommands = new Dictionary<string, Vector3>(BodyRetarget.SegmentDirections(snap, map, out _));
        currentJson = null;
        currentClamped = 0;
        shooter.Reset();
        shooter.Start(null);
        awaiting = true;
        State = MatchState.Running;
        Status = "Reading the body as it is now...";
        log($"[Soundstage/BODY] match start joints={target.Count} mirror={mirror}");
        return true;
    }

    public void Cancel()
    {
        if (!Running) return;
        Stop(MatchState.Cancelled, "Stopped. Your pose is back as it was.", restore: true);
    }

    /// <summary>Puts the body from before the match back (Brio keeps it in its own undo history too).</summary>
    public bool Undo(out string why)
    {
        why = "";
        if (originalBody.Length == 0) { why = "Nothing to undo."; return false; }
        return rig.Load(originalBody, out why);
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
                if (iterations.Count == 0) { Stop(MatchState.Failed, "Could not read the body in the game. " + reason + " Use the full-body framing with the whole character in view and try again.", restore: true); return; }
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

    private void Record(BodyPoseResult r)
    {
        var observed = BodyRetarget.Directions(r, map, options);
        float score;
        if (r.Silhouette == null)
        {
            score = 0f;
            const string none = "The pose model found no outline in a game picture, so its outline score is 0 and the joint angles decide between equal scores.";
            if (!notes.Contains(none)) notes.Add(none);
        }
        else score = SilhouetteScore.Score(photoSilhouette, r.Silhouette);
        var angle = BodyRetarget.MeanAngleErrorDeg(target, observed);
        var it = new BodyIteration(iterations.Count, score, angle, currentCommands, observed, currentJson, currentClamped);
        iterations.Add(it);
        var improved = best < 0 ? float.PositiveInfinity : score - iterations[best].Score;
        if (best < 0 || score > iterations[best].Score || (score == iterations[best].Score && angle < iterations[best].AngleErrorDeg)) best = it.Index;
        lastImprovement = improved;
        log($"[Soundstage/BODY] pass {it.Index}: outline score {score:0.0} angle error {(angle.HasValue ? angle.Value.ToString("0.0") : "n/a")} deg clamped {currentClamped} improvement {(float.IsInfinity(improved) ? "n/a" : improved.ToString("0.0"))}");
        Status = $"Pass {it.Index} of up to {settings.MaxIterations}: outline score {score:0}.";
    }

    private void Step()
    {
        var done = iterations.Count - 1;   // passes made beyond the first read
        if (done >= settings.MaxIterations) { Finish($"Stopped at the {settings.MaxIterations}-pass limit."); return; }
        if (done >= 2 && lastImprovement < settings.Epsilon) { Finish("Stopped: the last pass did not improve it any more."); return; }

        // Closed loop: ask for the photo's direction, plus the gap between what the previous ask turned into and what was asked.
        var prev = iterations[^1];
        var commands = new Dictionary<string, Vector3>();
        foreach (var (id, t) in target)
        {
            var ask = t;
            if (done >= 1 && prev.Observed.TryGetValue(id, out var obs) && prev.Commands.TryGetValue(id, out var asked))
            {
                var bias = obs - asked;
                var c = t - settings.Gain * bias;
                if (c.LengthSquared() > 1e-8f) ask = Vector3.Normalize(c);
            }
            commands[id] = ask;
        }

        var solved = BodyRetarget.Apply(original, commands, map);
        if (solved.Json == null) { Finish("Could not build the pose: " + solved.Error); return; }
        var json = BodyRetarget.BodyOnly(solved.Json, out var err);
        if (json == null) { Finish("Could not build the pose: " + err); return; }
        foreach (var w in solved.Warnings) if (!notes.Contains(w)) notes.Add(w);
        currentCommands = commands;
        currentJson = json;
        currentClamped = solved.Clamped;
        awaiting = true;
        shooter.Start(json);
        Status = $"Pass {done + 1} of up to {settings.MaxIterations}...";
    }

    private void Finish(string why)
    {
        // Keep the best pass, not the last. Pass 0 is "nothing moved", which puts the starting body back.
        var json = best == 0 ? originalBody : iterations[best].Json;
        var tail = "";
        var needLoad = best != iterations.Count - 1;   // the best pass is the one on screen already
        if (needLoad && json != null && !rig.Load(json, out var loadWhy)) tail = " The best result could not be loaded: " + loadWhy;
        var b = iterations[best];
        var first = iterations[0];
        var status = best == 0
            ? $"No pass beat the starting pose (outline score {first.Score:0}), so nothing was changed. {why}"
            : $"Outline score {first.Score:0} before, {b.Score:0} after (pass {b.Index}). {why}";
        Stop(MatchState.Done, status + tail, restore: false);
    }

    private bool Stop(MatchState state, string status, bool restore)
    {
        shooter.Reset();
        awaiting = false;
        if (restore && originalBody.Length > 0 && !rig.Load(originalBody, out var why))
            status += " The original pose could not be put back: " + why;
        State = state;
        Status = status;
        log($"[Soundstage/BODY] match {state}: {status}");
        return false;
    }
}

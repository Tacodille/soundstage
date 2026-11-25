// No Dalamud types in this file.
using Soundstage.Face;

namespace Soundstage.Expression;

public enum CapturePoll { Pending, Done, Failed }

/// <summary>
/// What the calibration and the matching loop need from the game. The live version (Expression/Live/ExpressionRig.cs) sits on Brio and the
/// capture service; a test supplies a fake. Every call is made from the draw thread, and none throws.
/// </summary>
public interface IExpressionRig
{
    /// <summary>True when everything the run needs is there (in GPose, Brio answering, the actor found). <paramref name="why"/> says what is missing.</summary>
    bool Ready(out string why);

    /// <summary>The actor's current pose as Brio JSON, or null.</summary>
    string? Snapshot();

    /// <summary>Loads expression-only pose JSON through Brio.</summary>
    bool Load(string poseJson, out string why);

    /// <summary>Race, clan, gender and face id of the actor.</summary>
    FaceKey? Key(out string why);

    /// <summary>Starts taking a picture of the screen.</summary>
    bool BeginCapture(out string why);

    CapturePoll Poll(out string path, out string why);

    /// <summary>Removes a picture the run is finished with.</summary>
    void Discard(string path);
}

public enum ShotState { Idle, Settling, Capturing, Analysing, Done, Failed }

/// <summary>
/// One round of "load a pose, let the game catch up, take a picture, read the face", ticked once a frame so nothing blocks the draw thread.
/// The face analysis runs on a worker task. Calibration and matching both run their steps through this.
/// </summary>
public sealed class Shooter
{
    private readonly IExpressionRig rig;
    private readonly IFaceAnalyser analyser;
    private readonly Func<int> settleFrames;
    private int frames;
    private string path = "";
    private Task<FaceResult>? analysis;

    public Shooter(IExpressionRig rig, IFaceAnalyser analyser, Func<int> settleFrames)
    {
        this.rig = rig;
        this.analyser = analyser;
        this.settleFrames = settleFrames;
    }

    public ShotState State { get; private set; } = ShotState.Idle;
    public FaceResult? Result { get; private set; }
    public string Error { get; private set; } = "";
    public bool Busy => State is ShotState.Settling or ShotState.Capturing or ShotState.Analysing;
    public bool Finished => State is ShotState.Done or ShotState.Failed;

    /// <summary>Loads <paramref name="poseJson"/> (or nothing when null, to read the face as it stands) and begins.</summary>
    public void Start(string? poseJson)
    {
        Result = null;
        Error = "";
        if (poseJson != null && !rig.Load(poseJson, out var why)) { Fail("Brio would not load the pose: " + why); return; }
        frames = Math.Max(0, settleFrames());
        State = ShotState.Settling;
    }

    public void Reset() { Cancel(); State = ShotState.Idle; Result = null; Error = ""; }

    /// <summary>Drops a run in progress. A picture already taken is discarded.</summary>
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
                    analysis = Task.Run(() => analyser.Analyse(file));
                    State = ShotState.Analysing;
                    return;

                case ShotState.Analysing:
                    if (analysis is not { IsCompleted: true }) return;
                    FaceResult r;
                    try { r = analysis.Result; } catch (Exception ex) { r = FaceResult.Unavailable("The face analysis failed: " + ex.Message); }
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

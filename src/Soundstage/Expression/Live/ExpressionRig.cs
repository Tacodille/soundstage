using Dalamud.Game.ClientState.Objects.Types;
using Soundstage.Capture;
using Soundstage.Workflow;

namespace Soundstage.Expression.Live;

/// <summary>
/// The game side of Part 17: the actor Brio poses, its customise data, and the picture. Pictures go through the photo booth of part 12, which
/// hides the Soundstage windows (and the rest of the UI it is set to hide) for the shot and puts them back, so the face is not covered.
/// Not compiled by the offline self-check (it needs Dalamud); the pure code it feeds is.
/// </summary>
public sealed class ExpressionRig : IExpressionRig
{
    private const int CustomizeRace = 0, CustomizeGender = 1, CustomizeTribe = 4, CustomizeFace = 5;

    private readonly Studio studio;
    private readonly string shotFolder;
    private bool shotStarted;
    private int shotNumber;
    private string shotPath = "";

    public ExpressionRig(Studio studio, string shotFolder)
    {
        this.studio = studio;
        this.shotFolder = shotFolder;
    }

    public bool Ready(out string why)
    {
        why = "";
        if (!studio.Brio.Available) { why = "Brio is not available (" + (studio.Brio.LastError ?? "not loaded") + ")."; return false; }
        if (!studio.Poses.InGpose) { why = "Not in GPose."; return false; }
        if (Actor() == null) { why = "No character found to pose."; return false; }
        return true;
    }

    /// <summary>The same pick as the Poses shelf: the GPose copy of the local player (object index 201 and up), else the player.</summary>
    private IGameObject? Actor()
    {
        var objects = studio.Objects;
        var lp = objects?.LocalPlayer;
        if (objects == null || lp == null) return null;
        var name = lp.Name.TextValue;
        foreach (var o in objects)
            if (o.ObjectIndex >= 201 && o.Name.TextValue == name) return o;
        return lp;
    }

    public string? Snapshot()
    {
        var a = Actor();
        return a == null ? null : studio.Brio.GetPoseJson(a);
    }

    public bool Load(string poseJson, out string why)
    {
        why = "";
        var a = Actor();
        if (a == null) { why = "No character found to pose."; return false; }
        var ok = studio.Brio.LoadFromJson(a, poseJson, "expression only");
        if (!ok) why = studio.Brio.LastError ?? "Brio refused the pose.";
        return ok;
    }

    public FaceKey? Key(out string why)
    {
        why = "";
        try
        {
            if (Actor() is not ICharacter c) { why = "The actor has no customise data."; return null; }
            var cu = c.Customize;
            if (cu.Length <= CustomizeFace) { why = "The customise data is too short."; return null; }
            return new FaceKey(cu[CustomizeRace], cu[CustomizeTribe], cu[CustomizeGender], cu[CustomizeFace]);
        }
        catch (Exception ex) { why = ex.Message; return null; }
    }

    public bool BeginCapture(out string why)
    {
        why = "";
        try
        {
            Directory.CreateDirectory(shotFolder);
            shotPath = Path.Combine(shotFolder, $"shot-{++shotNumber}.png");
            var booth = WorkflowHost.For(studio).Booth;
            if (!booth.Start(0, shotPath, needGpose: false)) { why = booth.Status; return false; }
            shotStarted = true;
            return true;
        }
        catch (Exception ex) { why = ex.Message; return false; }
    }

    public CapturePoll Poll(out string path, out string why)
    {
        path = "";
        why = "";
        var booth = WorkflowHost.For(studio).Booth;
        if (!shotStarted) { why = "No capture was started."; return CapturePoll.Failed; }
        if (booth.Busy) return CapturePoll.Pending;
        shotStarted = false;
        var last = booth.Last;
        if (last is { Ok: true } && last.Path == shotPath) { path = last.Path; return CapturePoll.Done; }
        why = last?.Message ?? "the capture did not finish";
        return CapturePoll.Failed;
    }

    public void Discard(string path)
    {
        try { if (path.Length > 0 && File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes the folder of pictures when a run is over.</summary>
    public void Clean()
    {
        try { if (Directory.Exists(shotFolder)) Directory.Delete(shotFolder, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

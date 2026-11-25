using System.Numerics;

namespace Soundstage.GameAccess;

/// <summary>
/// Wraps an <see cref="IGameReader"/> so nothing in game memory can throw into the plugin. Camera calls refuse outside
/// GPose, an exception becomes an "unavailable" answer, and each distinct failure is logged once per session (a probe
/// that fails every frame would otherwise fill the log).
/// </summary>
public sealed class GuardedAccess
{
    public const string LogPrefix = "[Soundstage/PROBE]";
    public const string NotInGpose = "Not in GPose. Enter GPose first; nothing was changed.";

    private readonly IGameReader reader;
    private readonly Action<string> log;
    private readonly HashSet<string> logged = new();
    private CameraState? baseline;

    public GuardedAccess(IGameReader reader, Action<string> log)
    {
        this.reader = reader;
        this.log = log;
    }

    /// <summary>True while a write has been made and not yet restored.</summary>
    public bool HasBaseline => baseline != null;

    public bool InGpose()
    {
        try { return reader.InGpose(); }
        catch (Exception ex) { LogOnce("ingpose", $"InGpose threw {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    public Access<CameraState> ReadCamera()
    {
        if (!InGpose()) return Access<CameraState>.Fail(NotInGpose);
        return Run("camera read", reader.ReadCamera);
    }

    /// <summary>Head and player reads work in and out of GPose; they never write.</summary>
    public Access<Vector3> ReadHead() => Run("head read", reader.ReadHeadPosition);

    public Access<Vector3> ReadPlayer() => Run("player read", reader.ReadPlayerPosition);

    /// <summary>Refuses outside GPose. Remembers the first state it sees so <see cref="Restore"/> can put it back.</summary>
    public Access<bool> WriteCamera(CameraWrite write)
    {
        if (write.IsEmpty) return Access<bool>.Fail("Nothing to write.");
        if (!InGpose()) return Access<bool>.Fail(NotInGpose);
        if (baseline == null)
        {
            var before = Run("camera read before write", reader.ReadCamera);
            if (!before.Ok) return Access<bool>.Fail("Could not read the camera first, so nothing was written: " + before.Reason);
            baseline = before.Value;
        }
        return Run("camera write", () => reader.WriteCamera(write));
    }

    /// <summary>Puts back every field a write could have changed. Outside GPose the game has already reset its own state, so the baseline is just dropped.</summary>
    public Access<bool> Restore()
    {
        if (baseline is not { } b) return Access<bool>.Fail("Nothing to restore.");
        if (!InGpose()) { baseline = null; return Access<bool>.Success(true); }
        var result = Run("camera restore", () => reader.WriteCamera(new CameraWrite(b.Yaw, b.Pitch, b.Distance, b.AddedFov, b.Position)));
        if (result.Ok) baseline = null;
        return result;
    }

    /// <summary>Call once a frame: leaving GPose drops the baseline, since the game resets its own camera state then.</summary>
    public void OnFrame()
    {
        if (baseline != null && !InGpose()) baseline = null;
    }

    private Access<T> Run<T>(string what, Func<Access<T>> call)
    {
        try
        {
            var r = call();
            if (!r.Ok) LogOnce(what, $"{what}: unavailable ({r.Reason})");
            return r;
        }
        catch (Exception ex)
        {
            var reason = $"{ex.GetType().Name}: {ex.Message}";
            LogOnce(what, $"{what}: threw {reason}");
            return Access<T>.Fail(reason);
        }
    }

    private void LogOnce(string key, string line)
    {
        if (logged.Add(key + "|" + line)) log($"{LogPrefix} {line}");
    }
}

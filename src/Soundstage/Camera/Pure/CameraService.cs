// No Dalamud types in this file: the offline self-check compiles Camera/Pure/*.cs (it drives this through a fake reader).
using Soundstage.GameAccess;

namespace Soundstage.CameraTools;

/// <summary>
/// Part 2's guarded camera access with the limits Part 10 adds: distance 0.3 to 12 m, pitch inside the game's range, the GPose FOV
/// slider -0.5 to 0.5. Never writes outside GPose and remembers the camera as it was, so <see cref="Restore"/> puts it back. Never throws.
/// The camera is placed by yaw, pitch and distance, because the game resets a written position (docs/proof-game-access.md).
/// </summary>
public sealed class CameraService
{
    public const string LogPrefix = "[Soundstage/CAM]";
    public const float MinDistance = 0.3f;
    public const float MaxDistance = 12f;

    private readonly GuardedAccess access;
    private readonly Func<float?> subjectFacing;
    private readonly Action<string> log;

    public CameraService(GuardedAccess access, Func<float?> subjectFacing, Action<string> log, CameraSettings? settings = null)
    {
        this.access = access;
        this.subjectFacing = subjectFacing;
        this.log = log;
        Settings = settings ?? new CameraSettings();
    }

    public CameraSettings Settings { get; set; }

    /// <summary>The last failure as one plain line for the tab (empty when the last call worked).</summary>
    public string LastMessage { get; private set; } = "";

    public bool InGpose => access.InGpose();

    /// <summary>True in GPose, where the writes work. Otherwise the reason, ready to show.</summary>
    public bool CanWrite(out string reason)
    {
        if (access.InGpose()) { reason = ""; return true; }
        reason = GuardedAccess.NotInGpose;
        return false;
    }

    public static float ClampDistance(float metres) => float.IsNaN(metres) ? MinDistance : Math.Clamp(metres, MinDistance, MaxDistance);

    public static float ClampPitch(float radians) => float.IsNaN(radians) ? 0f : Math.Clamp(radians, -Framing.MaxPitchRadians, Framing.MaxPitchRadians);

    /// <summary>Writes a pose after clamping it. One log line per value that had to be clamped.</summary>
    public bool Place(CameraPose pose, out string reason)
    {
        var d = ClampDistance(pose.Distance);
        var p = ClampPitch(pose.Pitch);
        var f = OrbitMath.ClampAddedFov(float.IsNaN(pose.AddedFov) ? 0f : pose.AddedFov);
        if (d != pose.Distance) log($"{LogPrefix} distance {pose.Distance:0.##} m clamped to {d:0.##} m.");
        if (p != pose.Pitch) log($"{LogPrefix} pitch {pose.Pitch:0.###} rad clamped to {p:0.###}.");
        var r = access.WriteCamera(new CameraWrite(Yaw: Framing.WrapPi(pose.Yaw), Pitch: p, Distance: d, AddedFov: Settings.WriteFov ? f : null));
        reason = r.Reason;
        LastMessage = r.Ok ? "" : r.Reason;
        return r.Ok;
    }

    /// <summary>Writes the yaw alone (the turntable's per-frame step).</summary>
    public bool PlaceYaw(float yaw, out string reason)
    {
        var r = access.WriteCamera(new CameraWrite(Yaw: Framing.WrapPi(yaw)));
        reason = r.Reason;
        LastMessage = r.Ok ? "" : r.Reason;
        return r.Ok;
    }

    public bool TryReadYaw(out float yaw, out string reason)
    {
        var r = access.ReadCamera();
        yaw = r.Ok ? r.Value.Yaw : 0f;
        reason = r.Reason;
        return r.Ok;
    }

    /// <summary>Solves a preset for the subject's real facing and head height, then places it.</summary>
    public bool ApplyFraming(string preset, float azimuthDeg, float offsetY, float fovExtra, out string reason)
    {
        var p = Framing.Find(preset);
        if (p == null) { reason = $"No framing called '{preset}'."; LastMessage = reason; return false; }
        if (!CanWrite(out reason)) { LastMessage = reason; return false; }

        var facing = subjectFacing();
        if (facing == null) log($"{LogPrefix} no local player to face, so the front is taken as yaw 0.");
        var pose = Framing.Solve(p, azimuthDeg, offsetY, facing ?? 0f, HeadAbovePivot(), fovExtra, Settings.YawTrimDeg, Settings.PitchSign);
        var ok = Place(pose, out reason);
        log($"{LogPrefix} framing {p.Id} az={azimuthDeg:0.#} yaw={pose.Yaw:0.###} pitch={pose.Pitch:0.###} dist={pose.Distance:0.##} fov={pose.AddedFov:0.###} -> {(ok ? "OK" : "FAIL " + reason)}");
        return ok;
    }

    /// <summary>A Scene's camera block, applied only when "Apply camera" is ticked.</summary>
    public bool ApplyScene(SceneCamera cam, out string reason)
    {
        if (!Settings.ApplyCamera) { reason = "Apply camera is off."; return false; }
        if (string.IsNullOrWhiteSpace(cam.Framing)) { reason = "The Scene's camera block names no framing."; return false; }
        var trim = Framing.HeightTrim(cam.Height);
        return ApplyFraming(cam.Framing, Framing.AzimuthFor(cam), (cam.OffsetY ?? 0f) + trim, cam.FovAdd ?? 0f, out reason);
    }

    public bool Restore(out string reason)
    {
        var r = access.Restore();
        reason = r.Reason;
        LastMessage = r.Ok ? "" : r.Reason;
        log($"{LogPrefix} restore -> {(r.Ok ? "OK" : r.Reason)}");
        return r.Ok;
    }

    public void OnFrame() => access.OnFrame();

    public void LogLine(string line) => log($"{LogPrefix} {line}");

    /// <summary>How far the head sits above the orbit pivot (the camera's look-at). 0 when either is unknown or the two disagree by over 3 m.</summary>
    private float HeadAbovePivot()
    {
        var cam = access.ReadCamera();
        var head = access.ReadHead();
        if (!cam.Ok || !head.Ok) return 0f;
        var delta = head.Value.Y - cam.Value.LookAt.Y;
        return MathF.Abs(delta) > 3f ? 0f : delta;
    }
}

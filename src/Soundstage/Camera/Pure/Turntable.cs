// No Dalamud types in this file: the offline self-check compiles Camera/Pure/*.cs.
namespace Soundstage.CameraTools;

/// <summary>Pure angle maths for the turntable.</summary>
public static class TurntableMath
{
    /// <summary>The yaw after <paramref name="elapsed"/> seconds of a lap that takes <paramref name="lapSeconds"/>, wrapped to -pi..pi.</summary>
    public static float YawAt(float startYaw, float elapsed, float lapSeconds, int direction = 1)
    {
        var lap = MathF.Max(lapSeconds, 0.5f);
        return Framing.WrapPi(startYaw + (direction < 0 ? -1f : 1f) * MathF.Tau * (elapsed / lap));
    }
}

/// <summary>
/// Orbits the camera about the subject by stepping the yaw once a frame. It stops on <see cref="Stop"/>, when GPose ends or when a write
/// fails. It writes the yaw only, so the framing the user chose (distance, pitch, FOV) stays as it is.
/// </summary>
public sealed class Turntable
{
    private readonly CameraService camera;
    private float startYaw;
    private float elapsed;

    public Turntable(CameraService camera) => this.camera = camera;

    public bool Running { get; private set; }
    public string Status { get; private set; } = "";
    public float LapSeconds { get; private set; } = 12f;

    public bool Start(float lapSeconds, int direction, out string reason)
    {
        if (!camera.CanWrite(out reason)) { Status = reason; return false; }
        if (!camera.TryReadYaw(out startYaw, out reason)) { Status = reason; return false; }
        LapSeconds = MathF.Max(lapSeconds, 0.5f);
        Direction = direction < 0 ? -1 : 1;
        elapsed = 0;
        Running = true;
        Status = "Turning.";
        camera.LogLine($"turntable start lap={LapSeconds:0.#}s dir={Direction}");
        return true;
    }

    public int Direction { get; private set; } = 1;

    /// <summary>Call once a frame with the frame time in seconds. Returns false when it has stopped (and says why in <see cref="Status"/>).</summary>
    public bool Tick(float dt)
    {
        if (!Running) return false;
        if (!camera.CanWrite(out var why)) { End("Stopped: GPose ended."); return false; }
        elapsed += Math.Clamp(dt, 0f, 0.25f);
        if (!camera.PlaceYaw(TurntableMath.YawAt(startYaw, elapsed, LapSeconds, Direction), out why)) { End("Stopped: " + why); return false; }
        return true;
    }

    /// <summary>Stops where it is.</summary>
    public void Stop()
    {
        if (Running) End("Stopped.");
    }

    /// <summary>Stops and puts the camera back as it was before anything was written.</summary>
    public void StopAndRestore()
    {
        End("Stopped.");
        camera.Restore(out _);
    }

    private void End(string status)
    {
        if (Running) camera.LogLine($"turntable end: {status}");
        Running = false;
        Status = status;
    }
}

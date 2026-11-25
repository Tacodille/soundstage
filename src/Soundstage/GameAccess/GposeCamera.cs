namespace Soundstage.GameAccess;

/// <summary>
/// The GPose camera: read position, orbit angles, distance and FOV; write the orbit angles, distance and the GPose FOV slider.
/// Camera writes refuse outside GPose and remember the first state they saw, so <see cref="Restore"/> puts everything back.
/// Never throws. Must run on the framework thread.
/// </summary>
public static class GposeCamera
{
    public static bool TryRead(out CameraState state, out string reason)
    {
        var r = GameAccessHost.Guard.ReadCamera();
        state = r.Ok ? r.Value : default;
        reason = r.Reason;
        return r.Ok;
    }

    public static bool TryWrite(CameraWrite write, out string reason)
    {
        var r = GameAccessHost.Guard.WriteCamera(write);
        reason = r.Reason;
        return r.Ok;
    }

    /// <summary>Orbits the camera about its subject by changing the yaw only (the game keeps the camera on its orbit sphere).</summary>
    public static bool TryOrbit(float degrees, out string reason)
    {
        if (!TryRead(out var now, out reason)) return false;
        return TryWrite(new CameraWrite(Yaw: OrbitMath.OrbitedYaw(now.Yaw, degrees)), out reason);
    }

    public static bool Restore(out string reason)
    {
        var r = GameAccessHost.Guard.Restore();
        reason = r.Reason;
        return r.Ok;
    }
}

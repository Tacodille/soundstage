using Soundstage.Look;

namespace Soundstage.GameAccess;

/// <summary>Reads the GPose camera and the player's head for auto focus. Framework thread only; returns null outside GPose or when either read fails.</summary>
public static class LookFocusSource
{
    public static AutoFocusInput? Read()
    {
        if (!GposeCamera.TryRead(out var camera, out _)) return null;
        if (!HeadBone.TryGet(out var head, out _)) return null;
        return new AutoFocusInput(camera.Position, head);
    }
}

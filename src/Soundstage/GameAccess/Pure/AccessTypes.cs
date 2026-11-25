// No Dalamud or FFXIVClientStructs types in GameAccess/Pure/*.cs: the offline self-check compiles this folder, so a file here
// that reaches for a game type fails that build. The unsafe readers that touch game memory live one folder up.
using System.Numerics;

namespace Soundstage.GameAccess;

/// <summary>One answer from game memory: a value, or "unavailable" with a plain reason. Never an exception.</summary>
public readonly record struct Access<T>(bool Ok, T? Value, string Reason)
{
    public static Access<T> Success(T value) => new(true, value, "");
    public static Access<T> Fail(string reason) => new(false, default, reason);
}

/// <summary>What the GPose camera looks like this frame. Angles are radians, the way the game stores them.</summary>
/// <param name="Position">The scene camera's world position (where the lens is).</param>
/// <param name="LookAt">The scene camera's look-at vector.</param>
/// <param name="Yaw">Horizontal orbit angle (the game's DirH).</param>
/// <param name="Pitch">Vertical orbit angle (the game's DirV).</param>
/// <param name="Distance">Orbit distance from the subject.</param>
/// <param name="Fov">The camera's own field of view (offset 0x130), which carries outside GPose.</param>
/// <param name="AddedFov">The GPose FOV slider (-0.5 to 0.5), which the game resets on leaving GPose.</param>
public readonly record struct CameraState(Vector3 Position, Vector3 LookAt, float Yaw, float Pitch, float Distance, float Fov, float AddedFov);

/// <summary>Fields to change. A null field is left alone.</summary>
public readonly record struct CameraWrite(float? Yaw = null, float? Pitch = null, float? Distance = null, float? AddedFov = null, Vector3? Position = null)
{
    public bool IsEmpty => Yaw == null && Pitch == null && Distance == null && AddedFov == null && Position == null;
}

/// <summary>Reading and writing the game, behind an interface so the self-check can drive every failure path with a fake.</summary>
public interface IGameReader
{
    bool InGpose();
    Access<CameraState> ReadCamera();
    Access<bool> WriteCamera(CameraWrite write);
    Access<Vector3> ReadHeadPosition();
    Access<Vector3> ReadPlayerPosition();
}

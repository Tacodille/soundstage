// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
using System.Numerics;

namespace Soundstage;

/// <summary>
/// Face aim. A light flagged <c>aimAt: "face"</c> keeps its Scene azimuth, distance and elevation, but measured from the
/// face instead of from a fixed height on the player's axis. Everything here is in stage-local space (the stage sits at the
/// player's feet, turned to the player's facing), the same space <see cref="SceneConverter.LightPosition"/> uses.
/// </summary>
public static class AimSolver
{
    /// <summary>The head height the catalogue was authored for. A light with no aimHeight is read against this.</summary>
    public const float DefaultHeadHeight = 1.55f;

    /// <summary>A flagged light is re-solved when the face has moved further than this (metres).</summary>
    public const float ResolveThresholdMetres = 0.02f;

    public const string Face = "face";

    public static bool IsFaceAimed(SceneLight l) => string.Equals(l.AimAt, Face, StringComparison.OrdinalIgnoreCase);

    public static bool AnyFaceAimed(Scene s) =>
        s.Lights.Any(IsFaceAimed) || (s.Backdrop?.Light != null && IsFaceAimed(s.Backdrop.Light));

    /// <summary>Where the face is taken to be when the head bone cannot be read: on the player's axis, at the light's own aimHeight table value.</summary>
    public static Vector3 FallbackFace(SceneLight l) => new(0f, l.AimHeight ?? DefaultHeadHeight, 0f);

    /// <summary>
    /// The light's stage-local position with the face at <paramref name="face"/>. The horizontal offset is (sin az x d, cos az x d)
    /// as ever; the vertical offset is the Scene's own elevation (height minus the aim height it was authored against).
    /// With the face at the authored place this is exactly <see cref="SceneConverter.LightPosition"/>.
    /// </summary>
    public static Vector3 Position(SceneLight l, Vector3 face)
    {
        var a = l.AzimuthDeg * MathF.PI / 180f;
        var reference = l.AimHeight ?? DefaultHeadHeight;
        return face + new Vector3(MathF.Sin(a) * l.Distance, l.Height - reference, MathF.Cos(a) * l.Distance);
    }

    /// <summary>The world position of the head, as a stage-local point. <paramref name="yawRadians"/> is the player's rotation.</summary>
    public static Vector3 ToStageLocal(Vector3 headWorld, Vector3 playerWorld, float yawRadians)
    {
        var rot = Quaternion.CreateFromYawPitchRoll(yawRadians, 0f, 0f);   // the same rotation StagehandBridge.StageRotation makes
        return Vector3.Transform(headWorld - playerWorld, Quaternion.Inverse(rot));
    }

    /// <summary>
    /// False for a head read that cannot be a face (a skeleton that is not loaded yet reads as the origin or the feet), so
    /// the caller uses <see cref="FallbackFace"/> and says so.
    /// </summary>
    public static bool Plausible(Vector3 faceLocal, out string reason)
    {
        if (float.IsNaN(faceLocal.X) || float.IsNaN(faceLocal.Y) || float.IsNaN(faceLocal.Z)) { reason = "the head read gave NaN"; return false; }
        if (faceLocal.Y < 0.5f || faceLocal.Y > 2.6f) { reason = $"the head read is {faceLocal.Y:F2} m above the feet"; return false; }
        if (MathF.Abs(faceLocal.X) > 1.0f || MathF.Abs(faceLocal.Z) > 1.0f) { reason = "the head read is more than 1 m from the player"; return false; }
        reason = "";
        return true;
    }

    public static bool MovedEnough(Vector3 previous, Vector3 current) => Vector3.Distance(previous, current) > ResolveThresholdMetres;
}

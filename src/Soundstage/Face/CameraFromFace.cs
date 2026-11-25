// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>What the camera should do to reproduce the photo's view of the head.</summary>
public sealed record CameraAdvice(bool Ok, string Reason, float AzimuthDeg, float OffsetY, string Framing, string Text);

/// <summary>
/// Head yaw and pitch from a photo to a camera azimuth and height trim, a pure function of a <see cref="FaceResult"/>.
/// The Camera tab's azimuth is degrees round the subject, positive on the subject's right (CameraSettings.AzimuthDeg).
/// A face turned toward the picture's right (yaw &gt; 0) is a face seen from its own right side, so azimuth = yaw. A chin lifted
/// above the lens (pitch &gt; 0) is a face seen from below, so the camera drops: offset = -distance x sin(pitch).
/// Roll has no camera control (the game camera does not roll), so it is only reported.
/// </summary>
public static class CameraFromFace
{
    public const float MaxAzimuth = 85f;
    public const float MaxOffset = 0.6f;
    public const float LowConfidence = 0.25f;

    public static CameraAdvice Solve(FaceResult face, FaceSettings settings)
    {
        if (!face.Available) return new CameraAdvice(false, face.Reason, 0f, 0f, settings.Framing, face.Reason);

        var az = Math.Clamp(face.YawDeg, -MaxAzimuth, MaxAzimuth);
        var pitch = (face.PitchDeg + settings.PitchBiasDeg) * MathF.PI / 180f;
        var off = Math.Clamp(-settings.PitchDistance * MathF.Sin(pitch), -MaxOffset, MaxOffset);
        var clamped = MathF.Abs(face.YawDeg) > MaxAzimuth;

        var side = MathF.Abs(az) < 2f ? "straight on" : az > 0 ? $"{az:0} degrees round to the subject's right" : $"{-az:0} degrees round to the subject's left";
        var height = MathF.Abs(off) < 0.02f ? "level" : off > 0 ? $"{off:0.00} m higher" : $"{-off:0.00} m lower";
        var text = $"The photo sees the face {side}, camera {height}.";
        if (MathF.Abs(face.RollDeg) >= 3f) text += $" The head tips {MathF.Abs(face.RollDeg):0} degrees {(face.RollDeg > 0 ? "right" : "left")} in the photo; the game camera cannot roll, so tilt the pose instead.";
        if (clamped) text += $" The photo's head angle ({face.YawDeg:0} degrees) is past what the camera is allowed, so it stops at {MaxAzimuth:0}.";
        if (face.Confidence < LowConfidence) text += " The face is small in the picture, so the angle is rough.";
        return new CameraAdvice(true, "", az, off, settings.Framing, text);
    }
}

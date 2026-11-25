// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>
/// The most recent local face result, and the plain words for its head angle. The photo-to-scene part can swap these in for the cloud
/// model's head-angle words (the local numbers are measured, the cloud's are a guess); nothing depends on that being wired.
/// </summary>
public static class HeadAngleWords
{
    /// <summary>Set by the Face tab after each analysis; null until one has run. A later part reads it, it never writes it.</summary>
    public static volatile FaceResult? Latest;

    public static string Describe(FaceResult f)
    {
        if (!f.Available) return "head angle unknown";
        string Side(float yaw) => MathF.Abs(yaw) < 5f ? "facing the camera" : yaw > 0 ? $"turned about {MathF.Round(MathF.Abs(yaw), 0)} degrees toward the picture's right" : $"turned about {MathF.Round(MathF.Abs(yaw), 0)} degrees toward the picture's left";
        string Tilt(float p) => MathF.Abs(p) < 5f ? "level" : p > 0 ? $"chin raised about {MathF.Round(p, 0)} degrees" : $"chin lowered about {MathF.Round(-p, 0)} degrees";
        return $"{Side(f.YawDeg)}, {Tilt(f.PitchDeg)}";
    }
}

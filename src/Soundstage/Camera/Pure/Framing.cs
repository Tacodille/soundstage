// No Dalamud types in this file: the offline self-check compiles Camera/Pure/*.cs.
namespace Soundstage.CameraTools;

/// <summary>What the camera service writes: orbit yaw and pitch (radians, the game's way), orbit distance in metres, and the GPose FOV slider.</summary>
public readonly record struct CameraPose(float Yaw, float Pitch, float Distance, float AddedFov);

/// <summary>One named framing. CameraDrop is how far below the head the lens sits. The game aims the camera at its own pivot, so a framing is distance, height and FOV, not an aim point.</summary>
public sealed record FramingPreset(string Id, string Label, float Distance, float CameraDrop, float AddedFov);

/// <summary>Pure maths from a preset, a facing and an azimuth to a camera pose. No game access.</summary>
public static class Framing
{
    public const float MaxPitchRadians = 1.48f;

    public static readonly IReadOnlyList<FramingPreset> Presets = new FramingPreset[]
    {
        new("full-body", "Full body", 6.0f, 0.60f, 0f),
        new("three-quarter", "Three-quarter", 4.0f, 0.40f, 0f),
        new("waist-up", "Waist up", 2.8f, 0.25f, 0f),
        new("chest-up", "Chest up", 2.0f, 0.10f, 0f),
        new("portrait", "Portrait", 1.4f, 0.00f, -0.10f),
        new("close-up", "Close-up", 0.8f, 0.00f, -0.15f),
    };

    /// <summary>The preset a Scene file's framing word means. Unknown words give null.</summary>
    public static FramingPreset? Find(string? name)
    {
        var id = Canonical(name);
        return Presets.FirstOrDefault(p => p.Id == id);
    }

    /// <summary>Maps the words the shipped Scenes use ("close-portrait", "three-quarter-waist-up", "side-on") onto the six preset ids.</summary>
    public static string Canonical(string? name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        return n switch
        {
            "close-portrait" or "head-and-shoulders" => "portrait",
            "closeup" or "close" => "close-up",
            "three-quarter-waist-up" or "waist" or "waistup" => "waist-up",
            "side-on" or "threequarter" or "3/4" => "three-quarter",
            "fullbody" or "full" => "full-body",
            "chestup" or "chest" => "chest-up",
            _ => n,
        };
    }

    /// <summary>A Scene camera block's azimuth: its own number, else the side word (front 0, right +90, left -90).</summary>
    public static float AzimuthFor(SceneCamera cam) =>
        cam.AzimuthDeg is { } a ? (float)a : (cam.Side ?? "").Trim().ToLowerInvariant() switch
        {
            "right" => 90f,
            "left" => -90f,
            _ => 0f,
        };

    /// <summary>Extra camera height from the Scene's height word: eye 0, chest lower, low lower again.</summary>
    public static float HeightTrim(string? height) => (height ?? "").Trim().ToLowerInvariant() switch
    {
        "chest" => -0.25f,
        "low" => -0.60f,
        _ => 0f,
    };

    /// <summary>
    /// The pose that puts the lens on the subject's front (azimuth 0), turned round to the subject's right by the azimuth.
    /// The game's yaw is taken as the compass angle of the camera round its pivot (compass 0 = +Z, turning towards +X), so a subject
    /// facing <paramref name="subjectFacing"/> radians has its front at yaw = facing. That convention is unconfirmed: YawTrimDeg and
    /// PitchSign in the settings fix it without a rebuild. <paramref name="headAbovePivot"/> is how far the head sits above the
    /// game's orbit pivot (0 when unknown). Distance is not clamped here; the camera service does that.
    /// </summary>
    public static CameraPose Solve(FramingPreset preset, float azimuthDeg, float offsetY, float subjectFacing, float headAbovePivot,
        float addedFovExtra = 0f, float yawTrimDeg = 0f, int pitchSign = 1)
    {
        var yaw = WrapPi(subjectFacing + (azimuthDeg + yawTrimDeg) * MathF.PI / 180f);
        var lensAbovePivot = headAbovePivot - preset.CameraDrop + offsetY;
        var d = MathF.Max(preset.Distance, 0.01f);
        var pitch = (pitchSign < 0 ? -1f : 1f) * MathF.Asin(Math.Clamp(lensAbovePivot / d, -1f, 1f));
        pitch = Math.Clamp(pitch, -MaxPitchRadians, MaxPitchRadians);
        return new CameraPose(yaw, pitch, preset.Distance, preset.AddedFov + addedFovExtra);
    }

    public static float WrapPi(float a)
    {
        const float TwoPi = MathF.PI * 2f;
        a %= TwoPi;
        if (a > MathF.PI) a -= TwoPi;
        else if (a < -MathF.PI) a += TwoPi;
        return a;
    }
}

// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>A camera framing the Scene suggests. Data only in part 4; the Camera tab (part 10) applies it through CameraTools.CameraService.</summary>
public sealed class SceneCamera
{
    /// <summary>"close-portrait", "chest-up", "three-quarter", "three-quarter-waist-up", "side-on" or "full-body".</summary>
    public string Framing { get; set; } = "";

    /// <summary>"eye", "chest" or "low".</summary>
    public string Height { get; set; } = "eye";

    /// <summary>Where the camera sits round the subject: "front", "left", "right".</summary>
    public string Side { get; set; } = "front";

    /// <summary>Degrees round the subject, positive towards the subject's right. When null the Side word decides (front 0, right 90, left -90).</summary>
    public double? AzimuthDeg { get; set; }

    /// <summary>Extra camera height in metres on top of the Height word. Null is none.</summary>
    public float? OffsetY { get; set; }

    /// <summary>Extra GPose FOV slider value on top of the framing's own. Null is none.</summary>
    public float? FovAdd { get; set; }

    public string Notes { get; set; } = "";
}

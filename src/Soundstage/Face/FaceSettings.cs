// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>Settings of the Face tab, kept in <see cref="Configuration.Sections"/> under "face".</summary>
public sealed class FaceSettings
{
    public const string Key = "face";

    /// <summary>The last picture the tab analysed, so it is still there next time.</summary>
    public string LastImage { get; set; } = "";

    /// <summary>The camera framing the head angle is applied through (a Camera tab framing id).</summary>
    public string Framing { get; set; } = "portrait";

    /// <summary>Added to the measured pitch before it becomes a camera height. The model's own zero may not be a head looking straight at the lens; adjust after a render-and-compare.</summary>
    public float PitchBiasDeg { get; set; }

    /// <summary>Metres between the camera and the face, used to turn a pitch into a height trim.</summary>
    public float PitchDistance { get; set; } = 1.2f;

    /// <summary>The angle of the cheek surface away from the nose, either side, in degrees. Sets how strongly a left-right brightness difference reads as a light angle.</summary>
    public float CheekAngleDeg { get; set; } = 40f;

    /// <summary>The same for forehead and chin, up and down.</summary>
    public float ForeheadChinAngleDeg { get; set; } = 30f;

    /// <summary>Seconds the helper stays running after the last picture, then it is stopped.</summary>
    public int IdleSeconds { get; set; } = 120;
}

// No Dalamud types in this file: the offline self-check compiles Camera/Pure/*.cs.
namespace Soundstage.CameraTools;

/// <summary>Settings of the Camera tab, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class CameraSettings
{
    public const string Key = "camera";

    /// <summary>Apply a Scene's camera block when the Scene is applied.</summary>
    public bool ApplyCamera { get; set; } = true;

    /// <summary>Write the GPose FOV slider (raw offset 0x13C, unconfirmed). Off keeps every preset to distance, height and angle.</summary>
    public bool WriteFov { get; set; } = true;

    public string Preset { get; set; } = "three-quarter";
    public float AzimuthDeg { get; set; }
    public float OffsetY { get; set; }
    public float AddedFov { get; set; }

    /// <summary>Turntable lap time in seconds.</summary>
    public float TurntableSeconds { get; set; } = 12f;

    // Overlay switches. Off by default so nothing is drawn until you ask for it.
    public bool Thirds { get; set; }
    public bool Golden { get; set; }
    public bool CentreCross { get; set; }
    public bool SafeMargin { get; set; }
    public float SafeMarginPercent { get; set; } = 5f;

    /// <summary>Added to every written yaw, in degrees. Fixes the game's yaw convention if the first in-game check shows the camera on the wrong side.</summary>
    public float YawTrimDeg { get; set; }

    /// <summary>+1 or -1. Flips the pitch sign if "camera above" turns out to be the other way round in the game's DirV.</summary>
    public int PitchSign { get; set; } = 1;
}

// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
namespace Soundstage;

/// <summary>Settings of the Lighting tab, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class LightingSettings
{
    public const string Key = "lighting";

    /// <summary>
    /// Exposure thresholds, in the same units as <see cref="Exposure.Estimate"/>. The rule (max colour x intensity /
    /// (falloffFactor x d^n)) is measured for where a light's range ends (0.0196); what value looks blown out on a face
    /// is NOT measured. These two numbers are a guess set on 21/11/2026 and you calibrate them in game (checklist step 9).
    /// </summary>
    public const float DefaultAmber = 2.0f;
    public const float DefaultRed = 4.0f;
    public const string ThresholdNote = "Starting guess, set 21/11/2026, not yet calibrated in game.";

    public float AmberThreshold { get; set; } = DefaultAmber;
    public float RedThreshold { get; set; } = DefaultRed;

    /// <summary>Animation plays only while this is on (the tab's Play box). Handy to compare frame cost on and off in one Scene.</summary>
    public bool PlayAnimation { get; set; } = true;

    /// <summary>Upper limit for whole-stage re-sends per second while a Scene animates. The throttle may go lower, never higher.</summary>
    public float MaxAnimationHz { get; set; } = 15f;

    /// <summary>A re-send averaging more than this many milliseconds makes the throttle slow the animation down.</summary>
    public float FrameBudgetMs { get; set; } = 4f;

    /// <summary>A strobe effect never flashes faster than this, whatever its own rate says.</summary>
    public float StrobeMaxHz { get; set; } = 3f;
}

// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>Animation on a Scene's lights. Data only in part 4; the Lighting tab (part 8) plays it.</summary>
public sealed class SceneAnimation
{
    public List<SceneAnimationEffect> Effects { get; set; } = new();
}

public sealed class SceneAnimationEffect
{
    /// <summary>The role of the light it moves (a role from <see cref="Scene.Lights"/>), or "backdrop" for the wash.</summary>
    public string Target { get; set; } = "";

    /// <summary>"pulse", "drift", "flicker" or "strobe" (part 8). Unknown kinds do nothing.</summary>
    public string Kind { get; set; } = "pulse";

    /// <summary>How far intensity swings, as a fraction (0.08 is eight percent).</summary>
    public float Amount { get; set; } = 0.1f;

    /// <summary>Cycles per second. Kept low by default.</summary>
    public float RateHz { get; set; } = 0.25f;

    /// <summary>What a pulse moves: "intensity" (default) or "hue" (rotates the colour by Amount x 360 degrees at the peak).</summary>
    public string Property { get; set; } = "intensity";

    /// <summary>Noise seed for flicker and drift. Null means a stable seed made from the Scene id, target and kind, so a Scene always flickers the same way.</summary>
    public int? Seed { get; set; }

    /// <summary>Off by default: the Scene looks the same until the user switches it on.</summary>
    public bool Enabled { get; set; }
}

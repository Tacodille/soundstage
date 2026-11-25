// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>
/// The set a Scene stands in: the backdrop kind, its colour, and the prop kits and picture around the subject. Part 9
/// applies it (Set/SetApply.cs). Every number is in metres in the stage's frame: +Z is the player's forward, +X their
/// left, the origin is at the player's feet.
/// </summary>
public sealed class SceneSet
{
    /// <summary>"none", "partition" (the old dyed panel from <see cref="Scene.Backdrop"/>) or "sweep" (the seamless sweep).</summary>
    public string Kind { get; set; } = "none";

    /// <summary>Palette role whose colour dyes the backdrop ("bg" or "backdrop"). Empty means <see cref="Color"/> is used.</summary>
    public string ColorRole { get; set; } = "";

    /// <summary>A fixed backdrop colour, used when <see cref="ColorRole"/> is empty or the Scene's palette has no such role.</summary>
    public float[]? Color { get; set; }

    /// <summary>Total width of the sweep in metres. Built from whole pieces, so the real width is rounded up to a piece.</summary>
    public float SweepWidth { get; set; } = 8f;

    /// <summary>Distance from the subject back to the sweep's wall. 0 means "use the Scene's backdrop distance, or 2.5 m".</summary>
    public float SweepDistance { get; set; }

    /// <summary>Radius-ish size of the 45 degree cove between wall and floor, in metres.</summary>
    public float SweepCove { get; set; } = 1f;

    /// <summary>Prop kit ids (kits/*.json). Each one is placed at its own default anchor unless <see cref="KitPlacements"/> overrides it.</summary>
    public List<string> Props { get; set; } = new();

    /// <summary>Per-kit overrides keyed by kit id: where the kit's anchor sits and how it is turned.</summary>
    public Dictionary<string, KitPlacement> KitPlacements { get; set; } = new();

    /// <summary>A picture hung on the canvas model. Null or an empty path means none.</summary>
    public ScenePicture? Picture { get; set; }

    public string Notes { get; set; } = "";
}

/// <summary>Where one kit stands. Offsets are added to the kit file's own anchor.</summary>
public sealed class KitPlacement
{
    public float X { get; set; }
    public float Z { get; set; }

    /// <summary>Turns the whole kit about its anchor, degrees. 180 flips a kit whose models face the wrong way.</summary>
    public float YawDeg { get; set; }
}

/// <summary>A user picture shown on the canvas prop. The file stays where it is; the Scene keeps its path.</summary>
public sealed class ScenePicture
{
    public string Path { get; set; } = "";

    /// <summary>Height of the picture in metres (the width follows the image's shape).</summary>
    public float Height { get; set; } = 1.2f;

    /// <summary>Metres to the left (+) or right (-) of the subject, and metres in front (+Z) or behind.</summary>
    public float X { get; set; } = 1.5f;
    public float Z { get; set; } = 1.0f;

    /// <summary>Centre of the picture above the floor.</summary>
    public float CentreHeight { get; set; } = 1.5f;

    /// <summary>0 = faces the subject. Added on top of "faces the subject", degrees.</summary>
    public float YawDeg { get; set; }
}

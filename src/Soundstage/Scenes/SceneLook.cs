// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>
/// A look slot, not a ReShade preset name: "neon-night" names a mood and each user maps it to their own preset in the
/// Look tab (part 11), so a shared Scene works on a machine with different shaders. A Scene never carries a preset path.
/// </summary>
public sealed class SceneLook
{
    /// <summary>The mood, for example neon-night, soft-bloom, clean-bright, dark-moody or warm-grade. Empty means "leave the look alone".</summary>
    public string Slot { get; set; } = "";

    /// <summary>
    /// True turns auto focus on while this Scene is applied (the Look tab names the depth-of-field uniform). Null or false:
    /// off, which is the default for every Scene. Null is not written to a file, so a Scene without it stays as it was.
    /// </summary>
    public bool? AutoFocus { get; set; }
}

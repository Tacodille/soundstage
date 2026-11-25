// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>
/// What a Scene suggests for the character: tags the Poses shelf can match (parts 7 and 14), a named Brio pose, a Glamourer
/// design and Penumbra mod settings. Part 7 applies the last three, and only when the user ticked "Apply pose and outfit"
/// for that Scene. A missing plugin or a missing pose or design skips just that part with one line.
/// </summary>
public sealed class ScenePose
{
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// A Brio pose file on this machine (part 4's field). Never travels in a share code, because a path is personal:
    /// <see cref="PosePack"/> and <see cref="PoseName"/> are what travel.
    /// </summary>
    public string? PoseFile { get; set; }

    /// <summary>The pack (the folder under a pose root) of the Brio pose to load. Matched with <see cref="PoseName"/> against the Poses shelf.</summary>
    public string? PosePack { get; set; }

    /// <summary>The pose's name (its file name without the extension).</summary>
    public string? PoseName { get; set; }

    /// <summary>Outfit hint (part 4's field): free text for the person reading the Scene; nothing applies it.</summary>
    public string? Outfit { get; set; }

    /// <summary>The name of a Glamourer design to apply to the player.</summary>
    public string? GlamourerDesign { get; set; }

    /// <summary>Penumbra mods to enable, with the option chosen in each group, in the player's collection.</summary>
    public List<PenumbraModChoice> PenumbraMods { get; set; } = new();

    /// <summary>True when the block asks for anything the integrations would act on.</summary>
    public bool HasPoseOrOutfit =>
        !string.IsNullOrWhiteSpace(PoseFile) || !string.IsNullOrWhiteSpace(PoseName) || !string.IsNullOrWhiteSpace(GlamourerDesign) || PenumbraMods.Count > 0;
}

/// <summary>One Penumbra mod and the settings a Scene wants it to have.</summary>
public sealed class PenumbraModChoice
{
    /// <summary>The mod's directory name inside Penumbra's mod root (what the IPC identifies a mod by).</summary>
    public string ModDirectory { get; set; } = "";

    /// <summary>The mod's display name, used as a fallback and in messages.</summary>
    public string ModName { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Option group name to the option names chosen in it. One name for a single-choice group, several for a multi-choice group.</summary>
    public Dictionary<string, List<string>> Options { get; set; } = new();
}

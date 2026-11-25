// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Look;

/// <summary>The Look tab's settings, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class LookSettings
{
    public const string Key = "look";

    /// <summary>Slot name to a preset path relative to the game folder (".\reshade-presets\Pack\Name.ini"). A missing slot is unmapped.</summary>
    public Dictionary<string, string> Mapping { get; set; } = new();

    /// <summary>
    /// The user's own preset from before a Scene changed it, kept here too so a crash or a plugin reload does not lose it.
    /// Empty when no Scene look is applied.
    /// </summary>
    public string PreviousPreset { get; set; } = "";

    /// <summary>The depth-of-field focus uniform the user chose (shader packs name it differently). Empty means auto focus has nothing to write.</summary>
    public string FocusUniform { get; set; } = "";

    /// <summary>Uniform value = distance in metres x Scale + Offset, then kept between Min and Max. The default passes metres straight through.</summary>
    public float FocusScale { get; set; } = 1f;
    public float FocusOffset { get; set; } = 0f;
    public float FocusMin { get; set; } = 0f;
    public float FocusMax { get; set; } = 1000f;

    /// <summary>Overrides the preset folder read from ReShade.ini. Empty uses ReShade.ini.</summary>
    public string PresetFolderOverride { get; set; } = "";

    /// <summary>What part 3's proof found: "pass" (the default, per the run decision), or "fail" once the checklist says so. A fail pins the manual route.</summary>
    public string Verdict { get; set; } = "pass";
}

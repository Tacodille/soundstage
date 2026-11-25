// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>Settings of the Scenes tab, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class ScenesTabSettings
{
    public const string Key = "scenes-tab";

    /// <summary>
    /// Ids of the Scenes whose pose and outfit block the user has agreed to apply. A Scene is never applied with its pose block
    /// unless its id is here. Part 7.5: applying a Scene never changes outfit or mods on its own.
    /// </summary>
    public List<string> ApplyPoseOutfitFor { get; set; } = new();

    public bool WantsPoseOutfit(string sceneId) => ApplyPoseOutfitFor.Contains(sceneId, StringComparer.OrdinalIgnoreCase);
}

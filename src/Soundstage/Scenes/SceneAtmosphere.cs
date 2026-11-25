// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>
/// Haze, dust, light shafts and sound around the subject. Part 4 shipped the three flags; part 9 adds
/// <see cref="Items"/>, a list of items from the atmosphere catalogue (atmosphere.json). The old flags still work: haze,
/// dust and sun map onto catalogue items "haze", "dust" and "sun" when the list does not already name them.
/// </summary>
public sealed class SceneAtmosphere
{
    /// <summary>"none", "light" or "heavy".</summary>
    public string Haze { get; set; } = "none";

    public bool Dust { get; set; }

    /// <summary>True for a visible sun shaft or glow (Golden hour).</summary>
    public bool Sun { get; set; }

    /// <summary>Catalogue items this Scene switches on or off.</summary>
    public List<AtmosphereChoice> Items { get; set; } = new();

    public string Notes { get; set; } = "";
}

public sealed class AtmosphereChoice
{
    /// <summary>The catalogue id, for example "haze".</summary>
    public string Id { get; set; } = "";

    public bool Enabled { get; set; } = true;

    /// <summary>Multiplies the catalogue's own scale (for a visual effect) or volume (for a sound).</summary>
    public float Amount { get; set; } = 1f;
}

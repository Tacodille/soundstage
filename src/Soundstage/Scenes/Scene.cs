// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage;

// ---- File model (docs\scenes-schema.md, v2) --------------------------------------------------------------------

public sealed class SceneFile
{
    public int FormatVersion { get; set; } = 2;
    public List<Scene> Scenes { get; set; } = new();
}

/// <summary>
/// One Scene: lights with roles, palettes that recolour those roles, and optional blocks (set, atmosphere, camera, look,
/// pose, animation) that later parts apply. Field names of the old v1 preset survive, so an old file maps without renames.
/// </summary>
public sealed class Scene
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public string BestIn { get; set; } = "";

    /// <summary>The rig's photographer name, for example "split light" or "rembrandt". Free text.</summary>
    public string Rig { get; set; } = "";

    public List<string> Tags { get; set; } = new();

    /// <summary>Old preset ids that now mean this Scene. The value is the palette the old id selected, or empty for the default.</summary>
    public Dictionary<string, string> LegacyIds { get; set; } = new();

    /// <summary>The palette whose colours the lights carry. Empty means the first palette.</summary>
    public string DefaultPalette { get; set; } = "";

    /// <summary>Palette name to (role to colour). Roles are light roles plus "backdrop" for the backdrop colour.</summary>
    public Dictionary<string, Dictionary<string, float[]>> Palettes { get; set; } = new();

    /// <summary>Reference numbers from the research notes. Kept as raw JSON so any shape round-trips.</summary>
    public JsonElement? References { get; set; }

    public List<SceneLight> Lights { get; set; } = new();

    /// <summary>The partition panel behind the subject. Part 9 replaces it with <see cref="Set"/>.</summary>
    public SceneBackdrop? Backdrop { get; set; }

    public SceneSet? Set { get; set; }
    public SceneAtmosphere? Atmosphere { get; set; }
    public SceneCamera? Camera { get; set; }
    public SceneLook? Look { get; set; }
    public ScenePose? Pose { get; set; }
    public SceneAnimation? Animation { get; set; }

    public Scene Clone() => JsonSerializer.Deserialize<Scene>(JsonSerializer.Serialize(this, SceneStore.WriteOptions), SceneStore.ReadOptions)!;

    /// <summary>The palette name that is the default, falling back to the first one.</summary>
    [JsonIgnore]
    public string EffectiveDefaultPalette =>
        Palettes.ContainsKey(DefaultPalette) ? DefaultPalette : Palettes.Keys.FirstOrDefault() ?? "";

    /// <summary>The names of the optional blocks this Scene actually carries.</summary>
    public IEnumerable<string> BlocksPresent()
    {
        if (Set != null) yield return "set";
        if (Atmosphere != null) yield return "atmosphere";
        if (Camera != null) yield return "camera";
        if (Look != null) yield return "look";
        if (Pose != null) yield return "pose";
        if (Animation != null) yield return "animation";
    }

    /// <summary>
    /// Writes the lights' current colours into the default palette, so a Scene saved after a palette change or a colour
    /// tweak reloads with the colours the user sees. Other palettes stay as they were.
    /// </summary>
    public void BakeDefaultPalette()
    {
        var name = EffectiveDefaultPalette;
        if (name.Length == 0) name = "Default";
        var map = new Dictionary<string, float[]>();
        foreach (var l in Lights) map[l.Role] = (float[])l.Color.Clone();
        if (Backdrop != null) map["backdrop"] = (float[])Backdrop.Color.Clone();
        Palettes[name] = map;
        DefaultPalette = name;
    }
}

public sealed class SceneLight
{
    public string Name { get; set; } = "";

    /// <summary>"key", "key2", "fill", "rim", "kicker", "bg" and so on. Unique inside a Scene; palettes are keyed by it.</summary>
    public string Role { get; set; } = "";

    public string Shape { get; set; } = "Point";
    public float[] Color { get; set; } = { 1f, 1f, 1f };
    public float Intensity { get; set; } = 4f;
    public float Range { get; set; } = 15f;
    public string Falloff { get; set; } = "Quadratic";
    public float FalloffFactor { get; set; } = 1f;
    public float ConeDeg { get; set; } = 30f;
    public float AngularFalloffDeg { get; set; } = 5f;
    public float AzimuthDeg { get; set; }
    public float Distance { get; set; } = 1.5f;
    public float Height { get; set; } = 1.6f;
    public float? AimHeight { get; set; }

    /// <summary>"face": the Lighting tab (part 8) places this light relative to the head instead of a fixed height. Null or empty: as authored.</summary>
    public string? AimAt { get; set; }

    public bool CharacterShadows { get; set; } = true;
    public bool ObjectShadows { get; set; } = true;
    public bool Specular { get; set; } = true;
}

public sealed class SceneBackdrop
{
    public float[] Color { get; set; } = { 1f, 1f, 1f };
    public float DistanceBehind { get; set; } = 2f;
    public float[] Scale { get; set; } = { 1f, 1f, 1f };
    public SceneLight? Light { get; set; }
}

/// <summary>Live adjustments for the backdrop, saved in the plugin config. Because the model's real size and pivot are unknown.</summary>
public sealed class BackdropAdjust
{
    public float ScaleX { get; set; } = 1f;          // multiplies the scene's scale X
    public float ScaleY { get; set; } = 1f;          // multiplies the scene's scale Y
    public float DistanceOffset { get; set; }        // added to the scene's distanceBehind (metres)
    public float YawOffsetDeg { get; set; }          // 0, 90, 180 or 270 on top of "faces the player"
    public float HeightOffset { get; set; }          // metres, added to the base-at-feet height of 0

    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"scaleX x{ScaleX:F2}, scaleY x{ScaleY:F2}, distance {DistanceOffset:+0.00;-0.00;+0.00}, yaw +{YawOffsetDeg:F0}, height {HeightOffset:+0.00;-0.00;+0.00}");
}

/// <summary>Settings of this feature, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class ScenesSettings
{
    public const string Key = "scenes";

    /// <summary>The palette applied for the selected Scene. Empty means the Scene's default.</summary>
    public string SelectedPalette { get; set; } = "";
}

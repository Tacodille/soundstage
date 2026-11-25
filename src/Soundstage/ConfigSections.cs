// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Text.Json;

namespace Soundstage;

/// <summary>
/// Per-feature settings kept as JSON text in <see cref="Configuration.Sections"/>, so a feature stores its own settings
/// without adding a field to the shared Configuration class (parts 4 to 17 each use their own key).
/// </summary>
public static class ConfigSections
{
    /// <summary>The section's value, or a fresh default when it is missing or does not parse.</summary>
    public static T Get<T>(Dictionary<string, string> sections, string key) where T : new()
    {
        if (!sections.TryGetValue(key, out var json)) return new T();
        try { return JsonSerializer.Deserialize<T>(json) ?? new T(); }
        catch (JsonException) { return new T(); }
    }

    public static void Set<T>(Dictionary<string, string> sections, string key, T value) =>
        sections[key] = JsonSerializer.Serialize(value);
}

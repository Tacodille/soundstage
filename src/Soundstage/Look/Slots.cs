// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Look;

/// <summary>Look slots: the mood names Scenes carry, and each user's mapping of them to their own ReShade presets.</summary>
public static class Slots
{
    /// <summary>The slots the shipped Scenes use. A Scene from elsewhere may use another name; it simply shows as unmapped.</summary>
    public static readonly string[] Known = { "neon-night", "soft-bloom", "clean-bright", "dark-moody", "warm-grade" };

    /// <summary>The known slots, then any other slot a loaded Scene uses, each once.</summary>
    public static List<string> All(IEnumerable<Scene> scenes)
    {
        var all = new List<string>(Known);
        foreach (var s in scenes)
        {
            var slot = s.Look?.Slot?.Trim() ?? "";
            if (slot.Length > 0 && !all.Contains(slot, StringComparer.OrdinalIgnoreCase)) all.Add(slot);
        }
        return all;
    }

    /// <summary>The preset path the user mapped to this slot, or null when it is unmapped or empty.</summary>
    public static string? Resolve(string? slot, LookSettings settings)
    {
        if (string.IsNullOrWhiteSpace(slot)) return null;
        foreach (var (key, value) in settings.Mapping)
            if (string.Equals(key, slot.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

    public static void Map(LookSettings settings, string slot, string? presetPath)
    {
        foreach (var key in settings.Mapping.Keys.Where(k => string.Equals(k, slot, StringComparison.OrdinalIgnoreCase)).ToList()) settings.Mapping.Remove(key);
        if (!string.IsNullOrWhiteSpace(presetPath)) settings.Mapping[slot] = presetPath;
    }

    /// <summary>A full path as ".\folder\name.ini" relative to the game folder; a path outside the game folder stays absolute.</summary>
    public static string ToRelative(string gameFolder, string fullPath)
    {
        var game = Path.GetFullPath(gameFolder).TrimEnd('\\') + "\\";
        var full = Path.GetFullPath(fullPath);
        return full.StartsWith(game, StringComparison.OrdinalIgnoreCase) ? @".\" + full[game.Length..] : full;
    }

    /// <summary>The path against the game folder, which is how the add-on reads a relative path too.</summary>
    public static string ToFull(string gameFolder, string presetPath) =>
        Path.GetFullPath(Path.IsPathRooted(presetPath) ? presetPath : Path.Combine(gameFolder, presetPath));

    public static bool PresetExists(string gameFolder, string presetPath)
    {
        try { return File.Exists(ToFull(gameFolder, presetPath)); }
        catch (Exception) { return false; }
    }

    /// <summary>Every .ini under the folder, as paths relative to the game folder, sorted. Empty when the folder is missing.</summary>
    public static List<string> ListPresets(string gameFolder, string presetFolder)
    {
        try
        {
            var root = ToFull(gameFolder, presetFolder);
            if (!Directory.Exists(root)) return new();
            return Directory.EnumerateFiles(root, "*.ini", SearchOption.AllDirectories)
                .Select(f => ToRelative(gameFolder, f)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception) { return new(); }
    }
}

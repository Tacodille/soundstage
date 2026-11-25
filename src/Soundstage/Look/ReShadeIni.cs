// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Look;

/// <summary>
/// Reads the few ReShade.ini keys the Look tab needs. Read only: Soundstage never writes ReShade.ini. A missing file or
/// key is "unknown", never a guess.
/// </summary>
public sealed class ReShadeIni
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public static ReShadeIni Parse(string text)
    {
        var ini = new ReShadeIni();
        var section = "";
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            if (line[0] == '[' && line[^1] == ']') { section = line[1..^1]; continue; }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            ini.values[section + "/" + line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return ini;
    }

    public static ReShadeIni? TryLoad(string gameFolder)
    {
        try
        {
            var path = Path.Combine(gameFolder, "ReShade.ini");
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception) { return null; }
    }

    public string? Get(string section, string key) => values.TryGetValue(section + "/" + key, out var v) ? v : null;

    /// <summary>The preset ReShade has loaded at startup, as written in the file (usually relative to the game folder).</summary>
    public string? PresetPath => Get("GENERAL", "PresetPath") is { Length: > 0 } p ? p : null;

    /// <summary>True when ReShade saves the preset by itself after a change: a uniform tweak from the add-on can then land in the user's preset file.</summary>
    public bool AutoSavePreset => Get("OVERLAY", "AutoSavePreset") == "1";

    /// <summary>
    /// The folder the user keeps presets in, read from <see cref="PresetPath"/>: the top folder under the game folder
    /// (".\reshade-presets\GShade\x.ini" gives ".\reshade-presets"), or the file's own folder when it sits straight in the game folder
    /// or outside it. Null when the key is missing.
    /// </summary>
    public string? PresetRoot()
    {
        var path = PresetPath;
        if (path == null) return null;
        var parts = path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 0 && parts[0] == ".") parts.RemoveAt(0);
        if (!Path.IsPathRooted(path) && !parts.Contains(".."))
            return parts.Count >= 2 ? @".\" + parts[0] : ".";
        var dir = Path.GetDirectoryName(path.Replace('/', '\\'));
        return string.IsNullOrEmpty(dir) ? null : dir;
    }
}

// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
using System.Globalization;
using System.Text.Json;

namespace Soundstage;

/// <summary>One colour filter. Generic names, no maker's numbers. <see cref="Hex"/> is "#rrggbb".</summary>
public sealed class Gel
{
    public string Name { get; set; } = "";
    public string Hex { get; set; } = "#ffffff";
    public string Note { get; set; } = "";

    /// <summary>The colour as 0 to 1 channels, the way a light carries it. White if the hex is malformed.</summary>
    public float[] Colour => Gels.ParseHex(Hex) ?? new[] { 1f, 1f, 1f };
}

/// <summary>
/// The gel list, loaded from gels.json (written by tools/gen_scenes.py, so the list grows without code changes).
/// A missing or broken file gives an empty list and one log line; the picker then says so.
/// </summary>
public static class Gels
{
    public static float[]? ParseHex(string hex)
    {
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return null;
        if (!int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return null;
        return new[] { ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f };
    }

    public static List<Gel> Load(string path, Action<string> log)
    {
        if (!File.Exists(path)) { log($"gels: {path} does not exist; the gel picker will be empty."); return new(); }
        try { return Parse(File.ReadAllText(path), log); }
        catch (Exception ex) { log($"gels: could not read {path}: {ex.Message}"); return new(); }
    }

    public static List<Gel> Parse(string json, Action<string> log)
    {
        var result = new List<Gel>();
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (!doc.RootElement.TryGetProperty("gels", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            log("gels: no \"gels\" array; the gel picker will be empty.");
            return result;
        }
        foreach (var el in arr.EnumerateArray())
        {
            var gel = JsonSerializer.Deserialize<Gel>(el.GetRawText(), SceneStore.ReadOptions);
            if (gel == null || string.IsNullOrWhiteSpace(gel.Name) || ParseHex(gel.Hex) == null)
            {
                log($"gels: skipped one entry with a missing name or a hex that is not #rrggbb ({el.GetRawText()}).");
                continue;
            }
            result.Add(gel);
        }
        return result;
    }
}

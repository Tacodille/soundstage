using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soundstage.Ai;

/// <summary>Pulls one JSON object out of a model's text reply, tolerating code fences and chatter around it.</summary>
public static class ReplyParser
{
    public static JsonObject? ExtractObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (TryParse(trimmed, out var direct)) return direct;

        // Fenced block: ```json ... ```
        var fence = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var start = trimmed.IndexOf('\n', fence);
            var end = start < 0 ? -1 : trimmed.IndexOf("```", start, StringComparison.Ordinal);
            if (start >= 0 && end > start && TryParse(trimmed[(start + 1)..end].Trim(), out var fenced)) return fenced;
        }

        // First balanced {...}, skipping braces inside strings.
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (trimmed[i] != '{') continue;
            var depth = 0;
            var inString = false;
            for (var j = i; j < trimmed.Length; j++)
            {
                var c = trimmed[j];
                if (inString) { if (c == '\\') j++; else if (c == '"') inString = false; continue; }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    if (TryParse(trimmed[i..(j + 1)], out var found)) return found;
                    break;
                }
            }
        }
        return null;
    }

    private static bool TryParse(string text, out JsonObject? obj)
    {
        obj = null;
        try { obj = JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { }
        return obj != null;
    }
}

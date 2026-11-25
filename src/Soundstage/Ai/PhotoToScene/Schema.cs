// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Text.Json.Nodes;

namespace Soundstage.Ai.PhotoToScene;

/// <summary>The fixed word lists the photo prompt offers and the parser accepts. One place, so the prompt and parser agree.</summary>
public static class PhotoWords
{
    /// <summary>The eight sides the key can come from, as seen in the photo (the viewer's left is the subject's right).</summary>
    public static readonly string[] Directions = { "front", "front-left", "left", "back-left", "back", "back-right", "right", "front-right" };

    public static readonly string[] Elevations = { "above", "level", "below" };
    public static readonly string[] Softness = { "hard", "medium", "soft" };

    /// <summary>Up to three of these describe the photo's mood. They overlap the Scene tags on purpose.</summary>
    public static readonly string[] Moods =
    {
        "moody", "soft", "bright", "dark", "warm", "cool", "neon", "natural", "dramatic", "classic", "night", "backlit", "hard", "outdoor", "indoor", "party",
    };

    public static readonly string[] YawBuckets = { "facing camera", "turned slightly left", "turned slightly right", "turned left", "turned right", "profile left", "profile right", "looking back over shoulder" };
    public static readonly string[] PitchBuckets = { "level", "chin up", "chin down" };
}

/// <summary>What the model read from the photo, after the parser has cleaned it. Every field has a safe default.</summary>
public sealed class PhotoAnalysis
{
    /// <summary>A catalogue rig, or empty when the model named one that is not in the catalogue.</summary>
    public string Rig { get; set; } = "";

    /// <summary>One of <see cref="PhotoWords.Directions"/>.</summary>
    public string KeyDirection { get; set; } = "front";

    /// <summary>One of <see cref="PhotoWords.Elevations"/>.</summary>
    public string KeyElevation { get; set; } = "level";

    /// <summary>One of <see cref="PhotoWords.Softness"/>.</summary>
    public string KeySoftness { get; set; } = "medium";

    /// <summary>0..1 RGB, or null when the photo gave none (the Scene keeps its own colour).</summary>
    public float[]? KeyColour { get; set; }
    public float[]? FillColour { get; set; }
    public float[]? RimColour { get; set; }
    public float[]? BackdropColour { get; set; }

    /// <summary>Null when the model did not say. The mapper then leaves the Scene's rim as it is.</summary>
    public bool? HasRim { get; set; }

    public List<string> Mood { get; set; } = new();

    /// <summary>Words only. Shown as suggestions; parts 10, 16 and 17 apply them where they exist.</summary>
    public string HeadYaw { get; set; } = "";
    public string HeadPitch { get; set; } = "";
    public string Expression { get; set; } = "";

    /// <summary>A catalogue Scene id, or empty when the model named one that does not exist.</summary>
    public string ClosestScene { get; set; } = "";

    /// <summary>Things the parser could not use, in plain words, for the card and the log.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>The JSON Schema sent with the picture. Words are plain strings here and checked by <see cref="PhotoParser"/>, so one odd word gives a safe default and not a failed request.</summary>
public static class PhotoSchema
{
    public static JsonObject Build() => JsonNode.Parse("""
        {
          "type": "object",
          "properties": {
            "rig": {"type": "string"},
            "keyDirection": {"type": "string"},
            "keyElevation": {"type": "string"},
            "keySoftness": {"type": "string"},
            "colours": {
              "type": "object",
              "properties": {
                "key": {"type": ["string", "null"]},
                "fill": {"type": ["string", "null"]},
                "rim": {"type": ["string", "null"]}
              }
            },
            "backdropColour": {"type": ["string", "null"]},
            "hasRim": {"type": "boolean"},
            "mood": {"type": "array", "items": {"type": "string"}},
            "headAngle": {"type": "object", "properties": {"yaw": {"type": "string"}, "pitch": {"type": "string"}}},
            "expression": {"type": "string"},
            "closestScene": {"type": "string"}
          },
          "required": ["keyDirection", "keySoftness", "colours", "closestScene"]
        }
        """)!.AsObject();
}

/// <summary>Reads the model's JSON into a <see cref="PhotoAnalysis"/>. Never throws: bad fields become safe defaults with a note.</summary>
public static class PhotoParser
{
    /// <summary>Null analysis (with an error) only when the reply is not a JSON object at all.</summary>
    public static (PhotoAnalysis? Analysis, string? Error) Parse(JsonNode? reply, IReadOnlyList<Scene> catalogue)
    {
        if (reply is not JsonObject root) return (null, "The reply was not a JSON object.");
        var a = new PhotoAnalysis();
        try
        {
            ReadAll(root, catalogue, a);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            a.Notes.Add("Part of the reply could not be read, so defaults were used.");
        }
        return (a, null);
    }

    private static void ReadAll(JsonObject root, IReadOnlyList<Scene> catalogue, PhotoAnalysis a)
    {
        var rig = Text(root["rig"], 60);
        if (rig.Length > 0)
        {
            var match = catalogue.Select(s => s.Rig).FirstOrDefault(r => string.Equals(r, rig, StringComparison.OrdinalIgnoreCase));
            if (match != null) a.Rig = match; else a.Notes.Add($"The model named a rig ('{rig}') that is not in the catalogue.");
        }

        var dirText = Text(root["keyDirection"], 60);
        var (dir, elev) = ReadDirection(dirText);
        if (dir == null)
        {
            a.Notes.Add(dirText.Length == 0 ? "No key direction was given; assumed the front." : $"Could not read the key direction '{dirText}'; assumed the front.");
            dir = "front";
        }
        a.KeyDirection = dir;
        var (_, elev2) = ReadDirection(Text(root["keyElevation"], 30));
        a.KeyElevation = elev2 ?? elev ?? "level";

        var soft = Text(root["keySoftness"], 30).ToLowerInvariant();
        if (PhotoWords.Softness.Contains(soft)) a.KeySoftness = soft;
        else
        {
            a.KeySoftness = "medium";
            a.Notes.Add(soft.Length == 0 ? "No softness was given; assumed medium." : $"Could not read the softness '{soft}'; assumed medium.");
        }

        var colours = root["colours"] as JsonObject;
        a.KeyColour = Colour(colours?["key"], "key", a);
        a.FillColour = Colour(colours?["fill"], "fill", a);
        a.RimColour = Colour(colours?["rim"], "rim", a);
        a.BackdropColour = Colour(root["backdropColour"] ?? colours?["backdrop"], "backdrop", a);

        if (root["hasRim"] is JsonValue hv && hv.TryGetValue<bool>(out var rimFlag)) a.HasRim = rimFlag;

        if (root["mood"] is JsonArray moods)
            foreach (var m in moods.Take(12))
            {
                var word = Text(m, 20).ToLowerInvariant();
                if (PhotoWords.Moods.Contains(word) && !a.Mood.Contains(word) && a.Mood.Count < 3) a.Mood.Add(word);
            }

        if (root["headAngle"] is JsonObject angle)
        {
            a.HeadYaw = Text(angle["yaw"], 40);
            a.HeadPitch = Text(angle["pitch"], 40);
        }
        else a.HeadYaw = Text(root["headAngle"], 60);
        a.Expression = Text(root["expression"], 60);

        var close = Text(root["closestScene"], 80);
        if (close.Length > 0)
        {
            var found = SceneStore.Resolve(catalogue, close, out _);
            if (found != null) a.ClosestScene = found.Id; else a.Notes.Add($"The model named a Scene ('{close}') that is not in the catalogue.");
        }
    }

    /// <summary>A short, single-line, printable string. A number, object or blob becomes empty or is clipped. Text is only ever used as a lookup key or shown, never opened as a path.</summary>
    internal static string Text(JsonNode? node, int max)
    {
        if (node is not JsonValue v || !v.TryGetValue<string>(out var s) || s == null) return "";
        var clean = new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > max ? clean[..max] : clean;
    }

    /// <summary>Hex such as #FFAA00, FFAA00 or #FA0 into 0..1 RGB. Null for anything else.</summary>
    public static float[]? ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var h = text.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h.Select(c => new string(c, 2)));
        if (h.Length != 6 || !h.All(Uri.IsHexDigit)) return null;
        return new[]
        {
            Convert.ToInt32(h[..2], 16) / 255f,
            Convert.ToInt32(h[2..4], 16) / 255f,
            Convert.ToInt32(h[4..6], 16) / 255f,
        };
    }

    private static float[]? Colour(JsonNode? node, string what, PhotoAnalysis a)
    {
        var text = Text(node, 20);
        if (text.Length == 0) return null;
        var c = ParseHex(text);
        if (c == null) a.Notes.Add($"The {what} colour '{text}' is not a hex colour, so the Scene's own was kept.");
        return c;
    }

    /// <summary>Splits words like "front-left above" into one of the eight sides and an elevation, either of which may be missing.</summary>
    public static (string? Direction, string? Elevation) ReadDirection(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var tokens = text.ToLowerInvariant().Split(new[] { ' ', '-', '_', ',', '/', '.' }, StringSplitOptions.RemoveEmptyEntries);
        bool front = false, back = false, left = false, right = false;
        string? elev = null;
        foreach (var t in tokens)
        {
            switch (t)
            {
                case "front" or "frontal" or "headon" or "straight" or "camera": front = true; break;
                case "back" or "behind" or "rear" or "backlit": back = true; break;
                case "left": left = true; break;
                case "right": right = true; break;
                case "frontleft": front = left = true; break;
                case "frontright": front = right = true; break;
                case "backleft": back = left = true; break;
                case "backright": back = right = true; break;
                case "above" or "high" or "overhead" or "top" or "up" or "upper": elev = "above"; break;
                case "below" or "low" or "under" or "down" or "lower": elev = "below"; break;
                case "level" or "eye": elev = "level"; break;
            }
        }
        if (left && right) { left = right = false; }
        string? dir = (front, back, left, right) switch
        {
            (true, _, true, _) => "front-left",
            (true, _, _, true) => "front-right",
            (_, true, true, _) => "back-left",
            (_, true, _, true) => "back-right",
            (_, true, _, _) => "back",
            (_, _, true, _) => "left",
            (_, _, _, true) => "right",
            (true, _, _, _) => "front",
            _ => null,
        };
        return (dir, elev);
    }
}

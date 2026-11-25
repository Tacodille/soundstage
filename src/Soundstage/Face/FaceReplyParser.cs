// No Dalamud types in this file.
using System.Text.Json;

namespace Soundstage.Face;

/// <summary>Reads one JSON line from the helper (tools/face/helper.py) into a <see cref="FaceResult"/>. Never throws.</summary>
public static class FaceReplyParser
{
    public static FaceResult Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return FaceResult.Unavailable("The face helper sent something unexpected.");
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var why = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                return FaceResult.Unavailable(string.IsNullOrWhiteSpace(why) ? "The face helper found no face." : why!);
            }

            var blends = new Dictionary<string, float>();
            if (root.TryGetProperty("blendshapes", out var b) && b.ValueKind == JsonValueKind.Object)
                foreach (var p in b.EnumerateObject())
                    if (p.Value.ValueKind == JsonValueKind.Number) blends[p.Name] = (float)p.Value.GetDouble();

            var lm = new List<float>(478 * 3);
            if (root.TryGetProperty("landmarks", out var l) && l.ValueKind == JsonValueKind.Array)
                foreach (var pt in l.EnumerateArray())
                {
                    if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 3) continue;
                    foreach (var c in pt.EnumerateArray().Take(3)) lm.Add((float)c.GetDouble());
                }

            float Num(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : float.NaN;
            var yaw = Num("yawDeg"); var pitch = Num("pitchDeg"); var roll = Num("rollDeg");
            if (float.IsNaN(yaw) || float.IsNaN(pitch) || float.IsNaN(roll)) return FaceResult.Unavailable("The face helper gave no head angle.");

            return new FaceResult
            {
                Available = true,
                YawDeg = yaw, PitchDeg = pitch, RollDeg = roll,
                Confidence = float.IsNaN(Num("confidence")) ? 0f : Math.Clamp(Num("confidence"), 0f, 1f),
                Blendshapes = blends,
                Landmarks = lm.ToArray(),
            };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return FaceResult.Unavailable("The face helper sent something that could not be read.");
        }
    }
}

using System.Text.Json;

namespace Soundstage.GameAccess;

/// <summary>
/// Route B for the head: does Brio's pose JSON (from GetPoseAsJson) carry a usable head bone? This only reads the JSON text.
/// A Brio pose stores bone transforms relative to the bone's parent, not world positions, so even a "yes" here gives a
/// local transform that needs the whole parent chain to become a world position. The probe reports what it finds.
/// </summary>
public static class BrioHeadRoute
{
    public const string HeadBone = "j_kao";

    public sealed record Finding(bool JsonValid, int BoneCount, bool HasHead, string HeadEntry, bool HasPosition);

    public static Finding Inspect(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Finding(false, 0, false, "", false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return new Finding(true, 0, false, "", false);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (!p.NameEquals("Bones") || p.Value.ValueKind != JsonValueKind.Object) continue;
                var count = 0;
                JsonElement? head = null;
                foreach (var bone in p.Value.EnumerateObject())
                {
                    count++;
                    if (bone.Name.Equals(HeadBone, StringComparison.OrdinalIgnoreCase)) head = bone.Value;
                }
                if (head is not { } h) return new Finding(true, count, false, "", false);
                var text = h.GetRawText();
                var hasPos = h.ValueKind == JsonValueKind.Object && h.EnumerateObject().Any(x => x.Name.Equals("Position", StringComparison.OrdinalIgnoreCase));
                return new Finding(true, count, true, text.Length > 160 ? text[..160] + "..." : text, hasPos);
            }
            return new Finding(true, 0, false, "", false);
        }
        catch (JsonException)
        {
            return new Finding(false, 0, false, "", false);
        }
    }
}

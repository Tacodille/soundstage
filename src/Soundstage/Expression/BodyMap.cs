// No Dalamud types in this file.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Expression;

/// <summary>One bone and the bone it hangs from, so world directions can be walked down from the root. Data, because the file for another skeleton may differ.</summary>
public sealed class BodyBoneLink
{
    [JsonPropertyName("bone")] public string Bone { get; set; } = "";
    [JsonPropertyName("parent")] public string Parent { get; set; } = "";
}

/// <summary>
/// One segment to copy: this bone turns so the line from the child bone's own rest direction points where the photo's limb points. The photo's
/// direction is the line from the average of the <see cref="From"/> landmarks to the average of the <see cref="To"/> landmarks.
/// </summary>
public sealed class BodyJoint
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("bone")] public string Bone { get; set; } = "";

    /// <summary>Bones that start the segment's far end, first one found with a real offset wins. Only its direction is used, so a twist bone with no length is skipped.</summary>
    [JsonPropertyName("child")] public List<string> Child { get; set; } = new();
    [JsonPropertyName("from")] public List<string> From { get; set; } = new();
    [JsonPropertyName("to")] public List<string> To { get; set; } = new();

    /// <summary>The most the segment may swing away from its own rest direction, in degrees.</summary>
    [JsonPropertyName("swingLimitDeg")] public float SwingLimitDeg { get; set; } = 90f;

    /// <summary>The most the bone may twist about its own length, in degrees either way. Twist is kept from the current pose, not read from the photo.</summary>
    [JsonPropertyName("twistLimitDeg")] public float TwistLimitDeg { get; set; } = 45f;

    /// <summary>Optional: the way this segment should point in the model's own frame when the character stands at rest. Used only by the skeleton check.</summary>
    [JsonPropertyName("expect")] public float[]? Expect { get; set; }
    [JsonPropertyName("expectTolDeg")] public float ExpectTolDeg { get; set; } = 60f;
}

public sealed class BodyFrame
{
    [JsonPropertyName("x")] public float[] X { get; set; } = { 1, 0, 0 };
    [JsonPropertyName("y")] public float[] Y { get; set; } = { 0, -1, 0 };
    [JsonPropertyName("z")] public float[] Z { get; set; } = { 0, 0, -1 };

    /// <summary>A MediaPipe world vector in the model's frame.</summary>
    public Vector3 ToModel(Vector3 mp) => new(Dot(X, mp), Dot(Y, mp), Dot(Z, mp));

    private static float Dot(float[] row, Vector3 v) => row.Length < 3 ? 0f : row[0] * v.X + row[1] * v.Y + row[2] * v.Z;
}

/// <summary>The landmark-to-bone table of part 18.3, loaded from body-map.json. The code never names a bone: everything bone-specific is in the file.</summary>
public sealed class BodyMap
{
    public const string ResourceName = "Soundstage.body-map.json";

    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("frame")] public BodyFrame Frame { get; set; } = new();
    [JsonPropertyName("minVisibility")] public float MinVisibility { get; set; } = 0.3f;
    [JsonPropertyName("bones")] public List<BodyBoneLink> Bones { get; set; } = new();
    [JsonPropertyName("joints")] public List<BodyJoint> Joints { get; set; } = new();

    public string? ParentOf(string bone) => Bones.FirstOrDefault(b => b.Bone == bone)?.Parent;

    /// <summary>How many links up to the root. Joints are solved shallowest first so each one sees its parents' new rotations.</summary>
    public int DepthOf(string bone)
    {
        var d = 0;
        var cur = bone;
        var guard = 0;
        while (guard++ < 64)
        {
            var p = ParentOf(cur);
            if (string.IsNullOrEmpty(p)) break;
            d++;
            cur = p;
        }
        return d;
    }

    public static BodyMap? TryParse(string json, out string problem)
    {
        problem = "";
        try
        {
            var m = JsonSerializer.Deserialize<BodyMap>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (m == null || m.Joints.Count == 0) { problem = "The body map has no joints."; return null; }
            var ids = new HashSet<string>();
            foreach (var j in m.Joints)
            {
                if (j.Id.Length == 0 || !ids.Add(j.Id)) { problem = $"Joint id '{j.Id}' is empty or repeated."; return null; }
                if (j.Bone.Length == 0 || j.Child.Count == 0) { problem = $"{j.Id}: needs a bone and at least one child bone."; return null; }
                if (j.From.Count == 0 || j.To.Count == 0) { problem = $"{j.Id}: needs from and to landmarks."; return null; }
                foreach (var n in j.From.Concat(j.To))
                    if (BodyPoseNames.IndexOf(n) < 0) { problem = $"{j.Id}: '{n}' is not a pose landmark name."; return null; }
                if (j.SwingLimitDeg is <= 0 or > 180) { problem = $"{j.Id}: swing limit must be above 0 and at most 180."; return null; }
                if (j.TwistLimitDeg is < 0 or > 180) { problem = $"{j.Id}: twist limit must be 0 to 180."; return null; }
                if (j.Expect is { Length: not 3 }) { problem = $"{j.Id}: expect needs three numbers."; return null; }
                if (m.ParentOf(j.Bone) == null) { problem = $"{j.Id}: bone '{j.Bone}' is not in the bone list."; return null; }
            }
            foreach (var r in new[] { m.Frame.X, m.Frame.Y, m.Frame.Z })
                if (r.Length != 3) { problem = "Each frame row needs three numbers."; return null; }
            return m;
        }
        catch (JsonException e) { problem = "The body map could not be read: " + e.Message; return null; }
    }

    public static BodyMap? LoadEmbedded(out string problem)
    {
        problem = "";
        try
        {
            using var s = typeof(BodyMap).Assembly.GetManifestResourceStream(ResourceName);
            if (s == null) { problem = "The body map is missing from this build."; return null; }
            using var r = new StreamReader(s);
            return TryParse(r.ReadToEnd(), out problem);
        }
        catch (Exception e) { problem = e.Message; return null; }
    }
}

/// <summary>The pose landmark names, shared with the face helper's reply parser (one list, in Face/BodyPoseResult.cs).</summary>
public static class BodyPoseNames
{
    public static int IndexOf(string name) => Soundstage.Face.BodyPoseResult.IndexOf(name);
}

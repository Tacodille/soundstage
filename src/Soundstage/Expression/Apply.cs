// No Dalamud types in this file.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;

namespace Soundstage.Expression;

/// <summary>
/// Part 17.4. Turns offsets into a Brio pose JSON with the expression bones only, ready for the Brio wrapper's LoadFromJson. The JSON is the
/// actor's own current pose (from GetPoseAsJson), so every bone the offsets do not touch keeps its value and the load is a plain pose load,
/// which Brio puts in its undo history like any other.
/// </summary>
public static class ExpressionApply
{
    /// <summary>One move on one bone. <see cref="Amount"/> is in the channel's unit: pose-file units for "pos", degrees for "rot".</summary>
    public sealed record Offset(string Bone, string Kind, string Axis, float Amount);

    /// <summary>
    /// The expression-only JSON for <paramref name="poseJson"/> with <paramref name="offsets"/> added, or null with <paramref name="error"/>.
    /// With no offsets it is the plain expression-only copy of the pose, which is also how the original face is put back.
    /// </summary>
    public static string? Build(string poseJson, IEnumerable<Offset> offsets, out string error)
    {
        error = "";
        JsonNode? root;
        try { root = JsonNode.Parse(poseJson, documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (Exception ex) { error = "The pose is not valid JSON: " + ex.Message; return null; }
        if (root is not JsonObject obj || obj["Bones"] is not JsonObject bones) { error = "The pose has no Bones section."; return null; }

        foreach (var o in offsets)
        {
            if (o.Amount == 0f) continue;
            if (!bones.TryGetPropertyValue(o.Bone, out var node) || node is not JsonObject bone) { error = $"The pose has no bone '{o.Bone}'."; return null; }
            if (!Move(bone, o, out error)) return null;
        }

        return PoseFilter.Reduce(obj.ToJsonString(), body: false, expression: true, out error, out _, out _);
    }

    private static bool Move(JsonObject bone, Offset o, out string error)
    {
        error = "";
        var axis = o.Axis.ToLowerInvariant() switch { "x" => 0, "y" => 1, "z" => 2, _ => -1 };
        if (axis < 0) { error = $"Axis '{o.Axis}' is not x, y or z."; return false; }

        if (o.Kind == "pos")
        {
            var v = ReadVector(bone, "Position", 3, out var asText) ?? new float[] { 0, 0, 0 };
            v[axis] += o.Amount;
            WriteVector(bone, "Position", v, asText);
            return true;
        }
        if (o.Kind == "rot")
        {
            var q = ReadVector(bone, "Rotation", 4, out var asText) ?? new float[] { 0, 0, 0, 1 };
            var cur = new Quaternion(q[0], q[1], q[2], q[3]);
            var ax = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
            var next = Quaternion.Normalize(cur * Quaternion.CreateFromAxisAngle(ax, o.Amount * MathF.PI / 180f));
            WriteVector(bone, "Rotation", new[] { next.X, next.Y, next.Z, next.W }, asText);
            return true;
        }
        error = $"Kind '{o.Kind}' is not pos or rot.";
        return false;
    }

    /// <summary>
    /// Reads a Brio vector. Brio writes them as text ("x, y, z" or "x, y, z, w"); an array of numbers or an object with X, Y, Z, W is accepted as well,
    /// so a change in how the file is written does not break the loader. Null when the field is missing.
    /// </summary>
    public static float[]? ReadVector(JsonObject bone, string field, int length, out bool asText)
    {
        asText = true;
        if (!bone.TryGetPropertyValue(field, out var node) || node == null) return null;
        try
        {
            if (node is JsonValue val && val.TryGetValue<string>(out var s))
            {
                var parts = s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != length) return null;
                return parts.Select(p => float.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            }
            asText = false;
            if (node is JsonArray arr && arr.Count == length) return arr.Select(n => n!.GetValue<float>()).ToArray();
            if (node is JsonObject o)
            {
                var keys = length == 3 ? new[] { "X", "Y", "Z" } : new[] { "X", "Y", "Z", "W" };
                var v = new float[length];
                for (var i = 0; i < length; i++)
                {
                    var key = o.Select(kv => kv.Key).FirstOrDefault(k => string.Equals(k, keys[i], StringComparison.OrdinalIgnoreCase));
                    if (key == null) return null;
                    v[i] = o[key]!.GetValue<float>();
                }
                return v;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or OverflowException) { }
        return null;
    }

    public static void WriteVector(JsonObject bone, string field, float[] v, bool asText)
    {
        if (asText || !bone.ContainsKey(field))
        {
            bone[field] = string.Join(", ", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));
            return;
        }
        var existing = bone[field];
        if (existing is JsonObject o)
        {
            var keys = o.Select(kv => kv.Key).ToList();
            var names = v.Length == 3 ? new[] { "X", "Y", "Z" } : new[] { "X", "Y", "Z", "W" };
            for (var i = 0; i < v.Length; i++)
            {
                var key = keys.FirstOrDefault(k => string.Equals(k, names[i], StringComparison.OrdinalIgnoreCase)) ?? names[i];
                o[key] = v[i];
            }
            return;
        }
        bone[field] = new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
    }
}

/// <summary>Picks which bones and axes a calibration measures. Data-driven: it reads the bone names out of the actor's own pose, so any race works.</summary>
public static class ExpressionChannels
{
    // Round-robin over these groups so brows, lids, mouth, jaw and cheeks are each represented before any one group is filled.
    private static readonly string[][] Groups =
    {
        new[] { "j_f_mayu_", "j_f_mmayu_", "j_f_miken_" },
        new[] { "j_f_mabdn_", "j_f_mabup_", "j_f_eyepuru_" },
        new[] { "j_f_ulip_", "j_f_dlip_", "j_f_lip_", "j_f_umlip_", "j_f_dmlip_" },
        new[] { "j_f_ago", "j_f_dago" },
        new[] { "j_f_hoho_", "j_f_dhoho_", "j_f_shoho_" },
        new[] { "j_f_hana", "j_f_hanaup", "j_f_hanadn" },
    };

    /// <summary>The jaw turns; every other face bone slides.</summary>
    public static string KindFor(string bone) => bone.StartsWith("j_f_ago", StringComparison.Ordinal) || bone.StartsWith("j_f_dago", StringComparison.Ordinal) ? "rot" : "pos";

    public static IReadOnlyList<string> PickBones(string poseJson, int maxBones)
    {
        var present = new List<string>();
        try
        {
            var root = JsonNode.Parse(poseJson, documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true });
            if (root is JsonObject o && o["Bones"] is JsonObject bones)
                present = bones.Select(kv => kv.Key).Where(PoseFilter.IsExpressionBone).ToList();
        }
        catch (System.Text.Json.JsonException) { }

        var lists = Groups.Select(prefixes => present
            .Where(b => prefixes.Any(p => b.StartsWith(p, StringComparison.Ordinal)))
            .OrderBy(b => b, StringComparer.Ordinal).ToList()).ToList();

        var picked = new List<string>();
        for (var round = 0; picked.Count < maxBones; round++)
        {
            var any = false;
            foreach (var l in lists)
            {
                if (round >= l.Count) continue;
                any = true;
                if (picked.Count < maxBones) picked.Add(l[round]);
            }
            if (!any) break;
        }
        return picked;
    }

    /// <summary>Three axes for each picked bone, with the step and safe range from the settings.</summary>
    public static List<ChannelRecord> Build(string poseJson, ExpressionSettings s)
    {
        var list = new List<ChannelRecord>();
        foreach (var bone in PickBones(poseJson, s.MaxBones))
        {
            var kind = KindFor(bone);
            var step = kind == "rot" ? s.RotationStepDeg : s.PositionStep;
            foreach (var axis in new[] { "x", "y", "z" })
                list.Add(new ChannelRecord { Bone = bone, Kind = kind, Axis = axis, Step = step, Max = step * s.SafeSteps });
        }
        return list;
    }

    /// <summary>The offsets for a set of channel values in steps (as the solver returns them).</summary>
    public static List<ExpressionApply.Offset> ToOffsets(IReadOnlyList<ResponseModel.Channel> channels, float[] steps)
    {
        var list = new List<ExpressionApply.Offset>();
        for (var j = 0; j < channels.Count && j < steps.Length; j++)
        {
            if (MathF.Abs(steps[j]) < 1e-4f) continue;
            var c = channels[j].Record;
            list.Add(new ExpressionApply.Offset(c.Bone, c.Kind, c.Axis, steps[j] * c.Step));
        }
        return list;
    }
}

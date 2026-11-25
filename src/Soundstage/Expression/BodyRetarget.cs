// No Dalamud types in this file.
using System.Numerics;
using System.Text.Json.Nodes;
using Soundstage.Face;

namespace Soundstage.Expression;

/// <summary>What the solver did with one joint.</summary>
public sealed record BodyJointResult(string Id, string Bone, bool Solved, string Skipped, Vector3 Target, Vector3 Achieved,
    float SwingDeg, bool SwingClamped, float TwistDeg, bool TwistClamped);

public sealed class BodyResult
{
    /// <summary>The pose JSON with the body bones set (everything else as it was), or null when nothing could be solved.</summary>
    public string? Json { get; init; }
    public IReadOnlyList<BodyJointResult> Joints { get; init; } = Array.Empty<BodyJointResult>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string Error { get; init; } = "";

    public int Solved => Joints.Count(j => j.Solved);
    public int Clamped => Joints.Count(j => j.Solved && (j.SwingClamped || j.TwistClamped));
}

/// <summary>
/// Part 18.4. Turns the photo's limb directions into bone rotations on the actor's own skeleton. Pure maths on a Brio pose JSON; the map says which
/// bone follows which landmarks.
///
/// How a joint is solved: the child bone's own offset in the bone's frame says which way the bone points at rest (r). The photo says which way
/// the segment must point in the model frame (d). That is turned into the parent's frame, limited to the joint's swing range, and the bone's new
/// rotation is the shortest swing taking r to it, with the bone's current twist about r kept (and limited). Joints are solved from the root
/// outwards so a hand sees the arm's new rotation. Nothing here knows an axis convention: it only reads what the actor's pose says.
/// </summary>
public static class BodyRetarget
{
    public sealed class Options
    {
        /// <summary>Swap the photo's left and right (the person is a mirror image, or the model's left is on the other side).</summary>
        public bool Mirror { get; set; }
        public float? MinVisibility { get; set; }
    }

    // ---- directions from landmarks ----

    private static string Swap(string name, bool mirror)
        => !mirror ? name : name.StartsWith("left_", StringComparison.Ordinal) ? "right_" + name[5..] : name.StartsWith("right_", StringComparison.Ordinal) ? "left_" + name[6..] : name;

    private static bool TryPoint(BodyPoseResult p, IEnumerable<string> names, bool mirror, float minVis, out Vector3 point)
    {
        point = Vector3.Zero;
        var n = 0;
        foreach (var raw in names)
        {
            var i = BodyPoseResult.IndexOf(Swap(raw, mirror));
            if (i < 0 || i >= p.World.Length) return false;
            var row = p.World[i];
            if (row.Length < 4 || row[3] < minVis) return false;
            point += new Vector3(row[0], row[1], row[2]);
            n++;
        }
        if (n == 0) return false;
        point /= n;
        return true;
    }

    /// <summary>The direction of every joint's segment in the model frame (unit vectors). A joint whose landmarks are not visible enough is left out.</summary>
    public static Dictionary<string, Vector3> Directions(BodyPoseResult pose, BodyMap map, Options? options = null)
    {
        var o = options ?? new Options();
        var minVis = o.MinVisibility ?? map.MinVisibility;
        var d = new Dictionary<string, Vector3>();
        if (!pose.Available) return d;
        foreach (var j in map.Joints)
        {
            if (!TryPoint(pose, j.From, o.Mirror, minVis, out var a) || !TryPoint(pose, j.To, o.Mirror, minVis, out var b)) continue;
            var v = map.Frame.ToModel(b - a);
            if (v.LengthSquared() < 1e-8f) continue;
            d[j.Id] = Vector3.Normalize(v);
        }
        return d;
    }

    // ---- skeleton reading ----

    private sealed class Rig
    {
        public readonly JsonObject Root;
        public readonly JsonObject Bones;
        public readonly Dictionary<string, Quaternion> Rot = new();
        public readonly Dictionary<string, Vector3> Pos = new();
        public readonly Dictionary<string, bool> RotAsText = new();

        public Rig(JsonObject root, JsonObject bones) { Root = root; Bones = bones; }
    }

    private static Rig? Read(string poseJson, out string error)
    {
        error = "";
        JsonNode? node;
        try { node = JsonNode.Parse(poseJson, documentOptions: new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (Exception ex) { error = "The pose is not valid JSON: " + ex.Message; return null; }
        if (node is not JsonObject root || root["Bones"] is not JsonObject bones) { error = "The pose has no Bones section."; return null; }
        var rig = new Rig(root, bones);
        foreach (var kv in bones)
        {
            if (kv.Value is not JsonObject b) continue;
            var q = ExpressionApply.ReadVector(b, "Rotation", 4, out var asText);
            rig.Rot[kv.Key] = q == null ? Quaternion.Identity : Quaternion.Normalize(new Quaternion(q[0], q[1], q[2], q[3]));
            rig.RotAsText[kv.Key] = asText;
            var t = ExpressionApply.ReadVector(b, "Position", 3, out _);
            rig.Pos[kv.Key] = t == null ? Vector3.Zero : new Vector3(t[0], t[1], t[2]);
        }
        return rig;
    }

    private static Quaternion WorldOf(Rig rig, BodyMap map, string? bone)
    {
        var q = Quaternion.Identity;
        var chain = new List<string>();
        var cur = bone;
        var guard = 0;
        while (!string.IsNullOrEmpty(cur) && guard++ < 64)
        {
            chain.Add(cur);
            cur = map.ParentOf(cur);
        }
        for (var i = chain.Count - 1; i >= 0; i--)
            if (rig.Rot.TryGetValue(chain[i], out var local)) q = Quaternion.Normalize(q * local);   // parent first, then child's own turn
        return q;
    }

    private static string? PickChild(Rig rig, BodyJoint j, out Vector3 rest)
    {
        rest = Vector3.UnitX;
        foreach (var c in j.Child)
        {
            if (!rig.Pos.TryGetValue(c, out var t)) continue;
            if (t.LengthSquared() < 1e-10f) continue;
            rest = Vector3.Normalize(t);
            return c;
        }
        return null;
    }

    // ---- quaternion helpers ----

    /// <summary>The shortest rotation taking unit vector a to unit vector b.</summary>
    public static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        var dot = Math.Clamp(Vector3.Dot(a, b), -1f, 1f);
        if (dot > 0.999999f) return Quaternion.Identity;
        if (dot < -0.999999f)
        {
            var axis = Vector3.Cross(a, Math.Abs(a.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1f + dot));
    }

    /// <summary>The twist of <paramref name="q"/> about the unit axis, as a signed angle in degrees in (-180, 180].</summary>
    public static float TwistAngleDeg(Quaternion q, Vector3 axis)
    {
        var v = new Vector3(q.X, q.Y, q.Z);
        var proj = Vector3.Dot(v, axis);
        var deg = 2f * MathF.Atan2(proj, q.W) * 180f / MathF.PI;
        while (deg > 180f) deg -= 360f;
        while (deg <= -180f) deg += 360f;
        return deg;
    }

    private static float AngleDeg(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    // ---- the solve ----

    /// <summary>
    /// Sets the body bones of <paramref name="poseJson"/> so each mapped segment points along its entry in <paramref name="directions"/> (model frame),
    /// limited by the map. A joint with no direction, no bone or no usable child is skipped and says why.
    /// </summary>
    public static BodyResult Apply(string poseJson, IReadOnlyDictionary<string, Vector3> directions, BodyMap map)
    {
        var rig = Read(poseJson, out var error);
        if (rig == null) return new BodyResult { Error = error };

        var results = new List<BodyJointResult>();
        var warnings = new List<string>();
        foreach (var j in map.Joints.OrderBy(j => map.DepthOf(j.Bone)).ThenBy(j => j.Id, StringComparer.Ordinal))
        {
            BodyJointResult Skip(string why) => new(j.Id, j.Bone, false, why, Vector3.Zero, Vector3.Zero, 0, false, 0, false);

            if (!directions.TryGetValue(j.Id, out var target)) { results.Add(Skip("the photo's landmarks for it are not visible enough")); continue; }
            if (!rig.Rot.ContainsKey(j.Bone)) { var w = Skip($"the pose has no bone {j.Bone}"); results.Add(w); warnings.Add(w.Skipped); continue; }
            var child = PickChild(rig, j, out var rest);
            if (child == null) { var w = Skip($"none of {string.Join(", ", j.Child)} has an offset in this pose"); results.Add(w); warnings.Add($"{j.Id}: {w.Skipped}"); continue; }

            var parentWorld = WorldOf(rig, map, map.ParentOf(j.Bone));
            var want = Vector3.Transform(target, Quaternion.Inverse(parentWorld));   // the target direction in the parent's frame
            want = Vector3.Normalize(want);

            // Swing limit: how far the segment may point from its own rest direction.
            var swing = AngleDeg(rest, want);
            var swingClamped = false;
            if (swing > j.SwingLimitDeg)
            {
                swingClamped = true;
                var axis = Vector3.Cross(rest, want);
                axis = axis.LengthSquared() < 1e-10f ? Vector3.Normalize(Vector3.Cross(rest, Math.Abs(rest.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY)) : Vector3.Normalize(axis);
                want = Vector3.Normalize(Vector3.Transform(rest, Quaternion.CreateFromAxisAngle(axis, j.SwingLimitDeg * MathF.PI / 180f)));
                swing = j.SwingLimitDeg;
            }
            var s = FromTo(rest, want);

            // Twist: keep what the pose has about the bone's own length, within its limit.
            var cur = rig.Rot[j.Bone];
            var twistDeg = TwistAngleDeg(cur, rest);
            var twistClamped = false;
            if (MathF.Abs(twistDeg) > j.TwistLimitDeg) { twistDeg = MathF.Sign(twistDeg) * j.TwistLimitDeg; twistClamped = true; }
            var twist = Quaternion.CreateFromAxisAngle(rest, twistDeg * MathF.PI / 180f);

            var q = Quaternion.Normalize(s * twist);
            rig.Rot[j.Bone] = q;
            var achieved = Vector3.Normalize(Vector3.Transform(Vector3.Transform(rest, q), parentWorld));
            results.Add(new BodyJointResult(j.Id, j.Bone, true, "", target, achieved, swing, swingClamped, twistDeg, twistClamped));
        }

        var any = results.Where(r => r.Solved).ToList();
        if (any.Count == 0) return new BodyResult { Joints = results, Warnings = warnings, Error = "No joint could be solved from this photo and this pose." };

        foreach (var r in any)
        {
            var q = rig.Rot[r.Bone];
            var b = (JsonObject)rig.Bones[r.Bone]!;
            ExpressionApply.WriteVector(b, "Rotation", new[] { q.X, q.Y, q.Z, q.W }, rig.RotAsText.GetValueOrDefault(r.Bone, true));
        }
        return new BodyResult { Json = rig.Root.ToJsonString(), Joints = results, Warnings = warnings };
    }

    /// <summary>
    /// The model-frame direction each mapped segment points in the given pose. The reading the skeleton check and the tests use: forward kinematics
    /// from the pose's own rotations, no landmarks.
    /// </summary>
    public static Dictionary<string, Vector3> SegmentDirections(string poseJson, BodyMap map, out string error)
    {
        var d = new Dictionary<string, Vector3>();
        var rig = Read(poseJson, out error);
        if (rig == null) return d;
        foreach (var j in map.Joints)
        {
            if (!rig.Rot.ContainsKey(j.Bone)) continue;
            if (PickChild(rig, j, out var rest) == null) continue;
            var world = WorldOf(rig, map, j.Bone);
            d[j.Id] = Vector3.Normalize(Vector3.Transform(rest, world));
        }
        return d;
    }

    public sealed record SkeletonCheck(int Checked, int Wrong, IReadOnlyList<string> Missing, IReadOnlyList<string> Notes)
    {
        /// <summary>True when most of the segments that have an expectation point the wrong way, or most of the bones are missing: the map does not fit this character or its current pose.</summary>
        public bool DoesNotFit => Checked == 0 || Wrong * 2 > Checked;
    }

    /// <summary>
    /// Compares the character's current pose with what the map expects of a standing body (thighs and shins down, spine and neck up, upper arms down),
    /// and lists every mapped bone the pose does not have. Run before applying, so a map written for a different skeleton or frame is caught at once.
    /// </summary>
    public static SkeletonCheck CheckSkeleton(string poseJson, BodyMap map)
    {
        var dirs = SegmentDirections(poseJson, map, out var err);
        var missing = new List<string>();
        var notes = new List<string>();
        if (err.Length > 0) return new SkeletonCheck(0, 0, missing, new[] { err });
        var rig = Read(poseJson, out _);
        foreach (var b in map.Bones.Select(b => b.Bone))
            if (rig != null && !rig.Rot.ContainsKey(b) && map.Joints.Any(j => j.Bone == b)) missing.Add(b);
        var checkedCount = 0; var wrong = 0;
        foreach (var j in map.Joints)
        {
            if (j.Expect == null || !dirs.TryGetValue(j.Id, out var d)) continue;
            checkedCount++;
            var expect = new Vector3(j.Expect[0], j.Expect[1], j.Expect[2]);
            var ang = AngleDeg(d, expect);
            if (ang > j.ExpectTolDeg) { wrong++; notes.Add($"{j.Id} points {ang:0} degrees from where the map expects."); }
        }
        return new SkeletonCheck(checkedCount, wrong, missing, notes);
    }

    /// <summary>The body-only copy of a pose JSON, the part Brio loads for a body retarget (the face and expression bones are dropped, so they stay as they are).</summary>
    public static string? BodyOnly(string poseJson, out string error)
        => PoseFilter.Reduce(poseJson, body: true, expression: false, out error, out _, out _);

    /// <summary>The average angle in degrees between each segment's target and observed direction, for the joints both have. Null when they share none.</summary>
    public static float? MeanAngleErrorDeg(IReadOnlyDictionary<string, Vector3> target, IReadOnlyDictionary<string, Vector3> observed)
    {
        var errs = target.Where(kv => observed.ContainsKey(kv.Key)).Select(kv => AngleDeg(kv.Value, observed[kv.Key])).ToList();
        return errs.Count == 0 ? null : errs.Average();
    }
}

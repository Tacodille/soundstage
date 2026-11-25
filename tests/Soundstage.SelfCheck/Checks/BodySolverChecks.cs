// Part 18.4: the solver on a synthetic stick figure. The skeleton is scrambled (random local rotations and offsets, so no axis convention is
// built in), the independent forward kinematics here is not the solver's, and every claim has a control that fails when the solver is skipped.
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundstage.Expression;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed partial class BodyChecks
{
    // ---- a synthetic skeleton ----

    private static string Vec(float[] v) => string.Join(", ", v.Select(x => x.ToString("R", CultureInfo.InvariantCulture)));

    /// <summary>A pose JSON with every bone of the map, random local rotations and offsets, plus two face bones that must never move.</summary>
    private static string ScrambledPose(BodyMap map, int seed, bool asArrays = false)
    {
        var rnd = new Random(seed);
        var bones = new JsonObject();
        JsonNode Wrap(float[] v) => asArrays ? new JsonArray(v.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) : JsonValue.Create(Vec(v))!;
        foreach (var b in map.Bones.Select(b => b.Bone).Concat(new[] { "j_f_mayu_l", "j_f_ago" }))
        {
            var axis = Vector3.Normalize(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f));
            var q = Quaternion.CreateFromAxisAngle(axis, (float)rnd.NextDouble() * MathF.PI * 2f - MathF.PI);
            var pdir = Vector3.Normalize(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f));
            var p = pdir * (0.08f + (float)rnd.NextDouble() * 0.3f);
            bones[b] = new JsonObject { ["Position"] = Wrap(new[] { p.X, p.Y, p.Z }), ["Rotation"] = Wrap(new[] { q.X, q.Y, q.Z, q.W }), ["Scale"] = "1, 1, 1" };
        }
        return new JsonObject { ["Bones"] = bones, ["MainHand"] = new JsonObject(), ["OffHand"] = new JsonObject() }.ToJsonString();
    }

    // Independent forward kinematics: parse with System.Text.Json, walk the parent links by hand.
    private sealed class Fk
    {
        public readonly Dictionary<string, Quaternion> Rot = new();
        public readonly Dictionary<string, Vector3> Pos = new();
        private readonly BodyMap map;

        public Fk(string json, BodyMap map)
        {
            this.map = map;
            using var doc = JsonDocument.Parse(json);
            foreach (var b in doc.RootElement.GetProperty("Bones").EnumerateObject())
            {
                float[] Read(string field)
                {
                    var e = b.Value.GetProperty(field);
                    return e.ValueKind == JsonValueKind.String
                        ? e.GetString()!.Split(',', StringSplitOptions.TrimEntries).Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray()
                        : e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                }
                var q = Read("Rotation"); var p = Read("Position");
                Rot[b.Name] = new Quaternion(q[0], q[1], q[2], q[3]);
                Pos[b.Name] = new Vector3(p[0], p[1], p[2]);
            }
        }

        public Quaternion World(string? bone)
        {
            if (string.IsNullOrEmpty(bone)) return Quaternion.Identity;
            var parent = map.ParentOf(bone);
            return World(parent) * Rot[bone];
        }

        public Vector3 Rest(BodyJoint j) => Vector3.Normalize(Pos[j.Child.First(c => Pos.ContainsKey(c) && Pos[c].Length() > 1e-6f)]);

        /// <summary>The segment's direction in the model frame.</summary>
        public Vector3 Segment(BodyJoint j) => Vector3.Transform(Rest(j), World(j.Bone));

        /// <summary>The segment's direction in its parent's frame.</summary>
        public Vector3 InParent(BodyJoint j) => Vector3.Transform(Rest(j), Rot[j.Bone]);
    }

    private static float Angle(Vector3 a, Vector3 b) => MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI;

    private static float Twist(Quaternion q, Vector3 axis)
    {
        var d = 2f * MathF.Atan2(Vector3.Dot(new Vector3(q.X, q.Y, q.Z), axis), q.W) * 180f / MathF.PI;
        while (d > 180f) d -= 360f;
        while (d <= -180f) d += 360f;
        return d;
    }

    private static Dictionary<string, Vector3> RandomTargets(BodyMap map, int seed)
    {
        var rnd = new Random(seed);
        var d = new Dictionary<string, Vector3>();
        foreach (var j in map.Joints)
            d[j.Id] = Vector3.Normalize(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f));
        return d;
    }

    // ---- the checks ----

    private void Solver()
    {
        var map = LoadMap();
        var worstExact = 0f;
        var unclamped = 0; var clamped = 0; var total = 0;
        var worstLimit = 0f; var worstTwist = 0f;
        var worstControl = new List<float>();
        var untouchedOk = true; var positionsOk = true; var facesOk = true; var jsonOk = true;
        var failedSolve = 0;

        for (var seed = 1; seed <= 60; seed++)
        {
            var pose = ScrambledPose(map, seed, asArrays: seed % 2 == 0);
            var targets = RandomTargets(map, 1000 + seed);
            var res = BodyRetarget.Apply(pose, targets, map);
            if (res.Json == null) { failedSolve++; continue; }
            var before = new Fk(pose, map);
            var after = new Fk(res.Json, map);

            foreach (var j in map.Joints)
            {
                total++;
                var r = res.Joints.First(x => x.Id == j.Id);
                if (!r.Solved) { jsonOk = false; continue; }
                var rest = before.Rest(j);
                if (!r.SwingClamped)
                {
                    unclamped++;
                    worstExact = MathF.Max(worstExact, Angle(after.Segment(j), targets[j.Id]));
                }
                else
                {
                    clamped++;
                    worstLimit = MathF.Max(worstLimit, MathF.Abs(Angle(after.InParent(j), rest) - j.SwingLimitDeg));
                }
                var wantTwist = Math.Clamp(Twist(before.Rot[j.Bone], rest), -j.TwistLimitDeg, j.TwistLimitDeg);
                worstTwist = MathF.Max(worstTwist, MathF.Abs(Twist(after.Rot[j.Bone], rest) - wantTwist));
                worstControl.Add(Angle(before.Segment(j), targets[j.Id]));
            }

            foreach (var kv in before.Rot.Where(k => !map.Joints.Any(j => j.Bone == k.Key)))
                if (after.Rot[kv.Key] != kv.Value) untouchedOk = false;
            foreach (var kv in before.Pos)
                if (after.Pos[kv.Key] != kv.Value) positionsOk = false;
            var after2 = JsonNode.Parse(res.Json)!["Bones"]!;
            if (after2["j_f_mayu_l"]!["Rotation"]!.ToJsonString() != JsonNode.Parse(pose)!["Bones"]!["j_f_mayu_l"]!["Rotation"]!.ToJsonString()) facesOk = false;
            // The vector format the file used is the one written back (text stays text, arrays stay arrays).
            var wasText = JsonNode.Parse(pose)!["Bones"]!["j_sebo_a"]!["Rotation"] is JsonValue;
            var isText = after2["j_sebo_a"]!["Rotation"] is JsonValue;
            if (wasText != isText) jsonOk = false;
        }

        Check("solver: every one of 60 scrambled skeletons solved", failedSolve == 0, $"{failedSolve} failed");
        Check("solver: enough segments in range to mean something", unclamped >= 300 && clamped >= 100, $"{unclamped} in range, {clamped} clamped of {total}");
        Check("solver: an in-range segment points exactly where asked (independent forward kinematics)", unclamped > 0 && worstExact < 0.05f, $"worst {worstExact:0.0000} deg over {unclamped}");
        Check("solver: an out-of-range segment stops exactly at its swing limit", clamped > 0 && worstLimit < 0.05f, $"worst {worstLimit:0.0000} deg over {clamped}");
        Check("solver: twist is the pose's own twist, held to the limit", worstTwist < 0.05f, $"worst {worstTwist:0.0000} deg");
        Check("solver (control): without the solver the same segments are far from their targets", worstControl.Count > 0 && worstControl.Average() > 30f, $"mean {(worstControl.Count > 0 ? worstControl.Average() : 0):0} deg");
        Check("solver: every bone that is not a joint keeps its rotation", untouchedOk);
        Check("solver: no bone's position changes", positionsOk);
        Check("solver: face bones are never touched", facesOk);
        Check("solver: text vectors stay text and array vectors stay arrays", jsonOk);

        // A clamp that is not a clamp: a joint asked for exactly its rest direction is neither clamped nor moved.
        {
            var pose = ScrambledPose(map, 77);
            var fk = new Fk(pose, map);
            // Ask each joint for the direction it already has: nothing should change.
            var same = map.Joints.ToDictionary(j => j.Id, j => fk.Segment(j));
            var res = BodyRetarget.Apply(pose, same, map);
            var after = new Fk(res.Json!, map);
            // A scrambled bone can already sit past its own swing limit, and then it is clamped; every other segment must stay put.
            var kept = map.Joints.Where(j => !res.Joints.First(x => x.Id == j.Id).SwingClamped).ToList();
            var worst = kept.Max(j => Angle(after.Segment(j), same[j.Id]));
            Check("solver: asking for what is already there leaves each in-range segment where it is", kept.Count >= 3 && worst < 0.05f, $"worst {worst:0.0000} deg over {kept.Count}");
            var standing = StandingPose(map, false);
            var sfk = new Fk(standing, map);
            var stay = BodyRetarget.Apply(standing, map.Joints.ToDictionary(j => j.Id, j => sfk.Segment(j)), map);
            var safter = new Fk(stay.Json!, map);
            Check("solver: a standing figure asked for its own directions is not moved at all", stay.Clamped == 0 && map.Joints.All(j => Angle(safter.Segment(j), sfk.Segment(j)) < 0.01f));
            Check("solver (control): the same figure is far from a different request", map.Joints.Average(j => Angle(fk.Segment(j), RandomTargets(map, 5)[j.Id])) > 30f);
        }

        // Boundary of the swing limit: just inside is untouched by the clamp, just outside is clamped.
        {
            var j = map.Joints.First(x => x.Id == "upperArmL");
            var pose = ScrambledPose(map, 3);
            var fk = new Fk(pose, map);
            // Find the parent-frame direction at angle a from rest, then take it to the world through the parent's world rotation as the solver will have it.
            // The parent of an arm is the clavicle, which is also solved, so ask for no clavicle (skipped) to keep its rotation fixed.
            var parentWorld = fk.World(map.ParentOf(j.Bone));
            var rest = fk.Rest(j);
            var perp = Vector3.Normalize(Vector3.Cross(rest, Math.Abs(rest.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY));
            Vector3 At(float deg) => Vector3.Transform(Vector3.Transform(rest, Quaternion.CreateFromAxisAngle(perp, deg * MathF.PI / 180f)), parentWorld);
            var inside = BodyRetarget.Apply(pose, new Dictionary<string, Vector3> { [j.Id] = At(j.SwingLimitDeg - 1f) }, map);
            var outside = BodyRetarget.Apply(pose, new Dictionary<string, Vector3> { [j.Id] = At(j.SwingLimitDeg + 1f) }, map);
            Check("limit: one degree inside the swing limit is not clamped", inside.Joints.First(x => x.Id == j.Id) is { Solved: true, SwingClamped: false });
            Check("limit: one degree outside the swing limit is clamped", outside.Joints.First(x => x.Id == j.Id) is { Solved: true, SwingClamped: true });
            Check("limit: the clamped result sits on the limit", MathF.Abs(outside.Joints.First(x => x.Id == j.Id).SwingDeg - j.SwingLimitDeg) < 0.01f);
        }

        // Skips: a missing bone and an invisible landmark each skip their own joint only.
        {
            var pose = ScrambledPose(map, 9);
            var node = JsonNode.Parse(pose)!;
            node["Bones"]!.AsObject().Remove("j_te_r");
            var targets = RandomTargets(map, 2);
            var res = BodyRetarget.Apply(node.ToJsonString(), targets, map);
            Check("skip: a pose with no right-hand bone solves everything but the hand and the forearm that points at it, and says so", res.Json != null && res.Joints.Count(x => x.Solved) == map.Joints.Count - 2 && res.Joints.First(x => x.Id == "handR") is { Solved: false } && res.Warnings.Any(w => w.Contains("j_te_r")), string.Join("; ", res.Warnings));
            var fewer = new Dictionary<string, Vector3>(targets);
            fewer.Remove("thighL");
            var res2 = BodyRetarget.Apply(pose, fewer, map);
            Check("skip: a joint with no direction is left alone", res2.Joints.First(x => x.Id == "thighL") is { Solved: false } && new Fk(res2.Json!, map).Rot["j_asi_a_l"] == new Fk(pose, map).Rot["j_asi_a_l"]);
            Check("skip (control): a joint that has a direction is moved", new Fk(res2.Json!, map).Rot["j_asi_a_r"] != new Fk(pose, map).Rot["j_asi_a_r"]);
            Check("skip: nothing to solve is an error, not a pose", BodyRetarget.Apply(pose, new Dictionary<string, Vector3>(), map).Json == null);
            Check("skip: a pose that is not JSON is an error, not an exception", BodyRetarget.Apply("{nope", targets, map).Error.Length > 0);
        }

        Directions();
        SkeletonCheckTests();
        Silhouettes();
        Recorded();
    }

    // ---- landmarks to directions ----

    private static BodyPoseResult Stick(Action<Dictionary<string, Vector3>> edit, float vis = 1f)
    {
        // A person facing the camera, MediaPipe frame (x picture right, y down, z away): arms hanging, legs straight.
        var p = new Dictionary<string, Vector3>
        {
            ["left_hip"] = new(0.1f, 0, 0), ["right_hip"] = new(-0.1f, 0, 0),
            ["left_shoulder"] = new(0.2f, -0.45f, 0), ["right_shoulder"] = new(-0.2f, -0.45f, 0),
            ["left_ear"] = new(0.08f, -0.7f, 0), ["right_ear"] = new(-0.08f, -0.7f, 0), ["nose"] = new(0, -0.68f, -0.1f),
            ["left_elbow"] = new(0.2f, -0.17f, 0), ["right_elbow"] = new(-0.2f, -0.17f, 0),
            ["left_wrist"] = new(0.2f, 0.07f, 0), ["right_wrist"] = new(-0.2f, 0.07f, 0),
            ["left_index"] = new(0.2f, 0.15f, 0), ["right_index"] = new(-0.2f, 0.15f, 0), ["left_pinky"] = new(0.2f, 0.15f, 0), ["right_pinky"] = new(-0.2f, 0.15f, 0),
            ["left_knee"] = new(0.1f, 0.4f, 0), ["right_knee"] = new(-0.1f, 0.4f, 0),
            ["left_ankle"] = new(0.1f, 0.8f, 0), ["right_ankle"] = new(-0.1f, 0.8f, 0),
            ["left_heel"] = new(0.1f, 0.85f, 0.03f), ["right_heel"] = new(-0.1f, 0.85f, 0.03f),
            ["left_foot_index"] = new(0.1f, 0.87f, -0.1f), ["right_foot_index"] = new(-0.1f, 0.87f, -0.1f),
        };
        edit(p);
        var world = new float[BodyPoseResult.Count][];
        for (var i = 0; i < world.Length; i++)
        {
            p.TryGetValue(BodyPoseResult.Names[i], out var v);
            world[i] = new[] { v.X, v.Y, v.Z, p.ContainsKey(BodyPoseResult.Names[i]) ? vis : 0f };
        }
        return new BodyPoseResult { Available = true, World = world, Image = world };
    }

    private void Directions()
    {
        var map = LoadMap();
        var hang = BodyRetarget.Directions(Stick(_ => { }), map);
        Check("directions: all 16 joints read from a full stick figure", hang.Count == 16, $"{hang.Count}");
        Check("directions: a hanging arm points down in the model's frame", NearV(hang["upperArmL"], new Vector3(0, -1, 0), 0.05f), hang["upperArmL"].ToString());
        Check("directions: the spine points up", NearV(hang["spine"], new Vector3(0, 1, 0), 0.05f));
        Check("directions: a leg points down", NearV(hang["thighR"], new Vector3(0, -1, 0), 0.05f));
        Check("directions: the left clavicle points to the left (+x)", hang["clavicleL"].X > 0.9f && hang["clavicleR"].X < -0.9f);

        var t = BodyRetarget.Directions(Stick(p => { p["left_elbow"] = p["left_shoulder"] + new Vector3(0.28f, 0, 0); p["left_wrist"] = p["left_elbow"] + new Vector3(0.25f, 0, 0); }), map);
        Check("directions: an arm out to the picture's right points along +x", NearV(t["upperArmL"], Vector3.UnitX, 0.01f) && NearV(t["forearmL"], Vector3.UnitX, 0.01f));
        var up = BodyRetarget.Directions(Stick(p => p["left_elbow"] = p["left_shoulder"] + new Vector3(0, -0.28f, 0)), map);
        Check("directions: an arm raised (picture up, y negative) points along +y", NearV(up["upperArmL"], Vector3.UnitY, 0.01f));
        var fwd = BodyRetarget.Directions(Stick(p => p["left_elbow"] = p["left_shoulder"] + new Vector3(0, 0, -0.28f)), map);
        Check("directions: an arm reaching at the camera points along the character's front (+z)", NearV(fwd["upperArmL"], Vector3.UnitZ, 0.01f));

        var m = BodyRetarget.Directions(Stick(p => p["left_elbow"] = p["left_shoulder"] + new Vector3(0.28f, 0, 0)), map, new BodyRetarget.Options { Mirror = true });
        Check("mirror: the right arm now takes the left arm's landmarks", NearV(m["upperArmR"], Vector3.UnitX, 0.01f) && NearV(m["upperArmL"], new Vector3(0, -1, 0), 0.05f));

        var hidden = BodyRetarget.Directions(Stick(p => { }, vis: 0.1f), map);
        Check("directions: landmarks below the visibility floor give no direction", hidden.Count == 0);
        var partial = BodyRetarget.Directions(Stick(p => p.Remove("left_wrist")), map);
        Check("directions: one missing landmark drops only the joints that use it", !partial.ContainsKey("forearmL") && partial.ContainsKey("forearmR") && partial.ContainsKey("upperArmL"));
        Check("directions: an unavailable result gives none", BodyRetarget.Directions(BodyPoseResult.Unavailable("no"), map).Count == 0);
        var avg = BodyRetarget.MeanAngleErrorDeg(hang, hang);
        Check("angle error: a figure against itself is 0", avg is { } a0 && a0 < 0.01f);
        Check("angle error (control): against a raised arm it is not 0", BodyRetarget.MeanAngleErrorDeg(hang, up) is { } a1 && a1 > 3f);
    }

    // ---- is the map the right shape for the skeleton ----

    private static string StandingPose(BodyMap map, bool upsideDown)
    {
        // Identity rotations, so every bone's offset is in the model frame: legs and arms hang down, the spine goes up.
        var off = new Dictionary<string, Vector3>
        {
            ["n_root"] = default, ["n_hara"] = new(0, 1.0f, 0), ["j_kosi"] = new(0, 0.05f, 0), ["j_sebo_a"] = new(0, 0.1f, 0), ["j_sebo_b"] = new(0, 0.12f, 0), ["j_sebo_c"] = new(0, 0.12f, 0),
            ["j_kubi"] = new(0, 0.15f, 0), ["j_kao"] = new(0, 0.1f, 0),
            ["j_sako_l"] = new(0.05f, 0.1f, 0), ["j_ude_a_l"] = new(0.1f, 0, 0), ["j_ude_b_l"] = new(0, -0.28f, 0), ["j_te_l"] = new(0, -0.25f, 0), ["j_naka_a_l"] = new(0, -0.1f, 0),
            ["j_sako_r"] = new(-0.05f, 0.1f, 0), ["j_ude_a_r"] = new(-0.1f, 0, 0), ["j_ude_b_r"] = new(0, -0.28f, 0), ["j_te_r"] = new(0, -0.25f, 0), ["j_naka_a_r"] = new(0, -0.1f, 0),
            ["j_asi_a_l"] = new(0.08f, -0.05f, 0), ["j_asi_b_l"] = new(0, -0.4f, 0), ["j_asi_c_l"] = new(0, -0.4f, 0), ["j_asi_d_l"] = new(0, -0.05f, 0.12f),
            ["j_asi_a_r"] = new(-0.08f, -0.05f, 0), ["j_asi_b_r"] = new(0, -0.4f, 0), ["j_asi_c_r"] = new(0, -0.4f, 0), ["j_asi_d_r"] = new(0, -0.05f, 0.12f),
        };
        var bones = new JsonObject();
        foreach (var b in map.Bones.Select(x => x.Bone))
        {
            var v = off[b];
            var q = upsideDown && b == "n_root" ? Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI) : Quaternion.Identity;
            bones[b] = new JsonObject { ["Position"] = Vec(new[] { v.X, v.Y, v.Z }), ["Rotation"] = Vec(new[] { q.X, q.Y, q.Z, q.W }), ["Scale"] = "1, 1, 1" };
        }
        return new JsonObject { ["Bones"] = bones }.ToJsonString();
    }

    private void SkeletonCheckTests()
    {
        var map = LoadMap();
        var ok = BodyRetarget.CheckSkeleton(StandingPose(map, false), map);
        Check("skeleton check: a standing figure fits the map", !ok.DoesNotFit && ok.Checked >= 8 && ok.Wrong == 0 && ok.Missing.Count == 0, $"{ok.Wrong} of {ok.Checked} wrong, {ok.Missing.Count} missing");
        var flipped = BodyRetarget.CheckSkeleton(StandingPose(map, true), map);
        Check("skeleton check (control): the same figure upside down does not fit", flipped.DoesNotFit && flipped.Wrong == flipped.Checked, $"{flipped.Wrong} of {flipped.Checked} wrong");
        var scrambled = BodyRetarget.CheckSkeleton(ScrambledPose(map, 4), map);
        Check("skeleton check (control): a scrambled figure does not fit", scrambled.DoesNotFit, $"{scrambled.Wrong} of {scrambled.Checked} wrong");
        var node = JsonNode.Parse(StandingPose(map, false))!;
        node["Bones"]!.AsObject().Remove("j_asi_a_l");
        var miss = BodyRetarget.CheckSkeleton(node.ToJsonString(), map);
        Check("skeleton check: a missing mapped bone is listed", miss.Missing.Contains("j_asi_a_l"));
        Check("skeleton check: not JSON is not a crash", BodyRetarget.CheckSkeleton("{nope", map).DoesNotFit);

        // Solving a standing figure toward the photo's own standing stick figure leaves it standing (end to end through the map's frame).
        var res = BodyRetarget.Apply(StandingPose(map, false), BodyRetarget.Directions(Stick(_ => { }), map), map);
        var after = BodyRetarget.CheckSkeleton(res.Json!, map);
        Check("end to end: a standing figure matched to a standing photo still fits the map", res.Json != null && !after.DoesNotFit && after.Wrong == 0);
        var raise = BodyRetarget.Directions(Stick(p => { p["left_elbow"] = p["left_shoulder"] + new Vector3(0.28f, 0, 0); p["left_wrist"] = p["left_elbow"] + new Vector3(0.25f, 0, 0); }), map);
        var arm = BodyRetarget.Apply(StandingPose(map, false), raise, map);
        var seg = BodyRetarget.SegmentDirections(arm.Json!, map, out _);
        Check("end to end: a T-pose photo puts the character's left arm out along +x", NearV(seg["upperArmL"], Vector3.UnitX, 0.01f) && NearV(seg["forearmL"], Vector3.UnitX, 0.01f), seg["upperArmL"].ToString());
        Check("end to end (control): the other arm is still hanging", NearV(seg["upperArmR"], new Vector3(0, -1, 0), 0.05f));
    }

    // ---- outlines ----

    private static SilhouetteGrid Blob(int size, Func<int, int, bool> fill, float aspect)
    {
        var c = new bool[size * size];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++) c[y * size + x] = fill(x, y);
        return new SilhouetteGrid { Size = size, Cells = c, Aspect = aspect, Cover = 0.2f };
    }

    private void Silhouettes()
    {
        var a = Blob(64, (x, y) => x < 40, 0.5f);
        var b = Blob(64, (x, y) => x < 40, 0.5f);
        var half = Blob(64, (x, y) => x < 20, 0.5f);
        var wide = Blob(64, (x, y) => x < 40, 1.5f);
        Check("outline: the same outline scores 100", MathF.Abs(SilhouetteScore.Score(a, b) - 100f) < 0.001f);
        Check("outline: half the area is a third of the overlap in union terms", MathF.Abs(SilhouetteScore.Iou(a, half) - 0.5f) < 0.001f);
        Check("outline (control): a different shape scores lower", SilhouetteScore.Score(a, half) < SilhouetteScore.Score(a, b) - 40f);
        Check("outline: the same shape in a box of very different proportions scores lower", SilhouetteScore.Score(a, wide) < 40f, $"{SilhouetteScore.Score(a, wide):0.0}");
        Check("outline: a mirrored lopsided shape is not the shape", SilhouetteScore.Iou(half, half.Flipped()) < 0.01f);
        Check("outline: flipping twice is the shape again", SilhouetteScore.Iou(a, a.Flipped().Flipped()) > 0.999f);
        Check("outline: grids of different sizes do not compare", SilhouetteScore.Iou(a, Blob(32, (x, y) => true, 1f)) == 0f);

        var bits = new byte[512];
        bits[0] = 0b1010_0000;
        var g = SilhouetteGrid.FromBase64(Convert.ToBase64String(bits), 64, 0.4f, 0.2f)!;
        Check("outline: packed bits are read most significant bit first (numpy packbits)", g.Cells[0] && !g.Cells[1] && g.Cells[2] && !g.Cells[3] && g.Filled == 2);
        Check("outline: too few bits is refused", SilhouetteGrid.FromBase64(Convert.ToBase64String(new byte[10]), 64, 1f, 1f) == null);
        Check("outline: bad base64 is refused", SilhouetteGrid.FromBase64("!!!", 64, 1f, 1f) == null);
    }

    // ---- recorded replies from the real helper ----

    private void Recorded()
    {
        var map = LoadMap();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRoot, "TestData", "body-recorded.json")));
        var one = BodyPoseResult.Parse(doc.RootElement.GetProperty("p1").GetRawText());
        var four = BodyPoseResult.Parse(doc.RootElement.GetProperty("p4").GetRawText());
        Check("recorded: the helper's real reply parses (33 landmarks each way, outline present)", one.Available && one.World.Length == 33 && one.Image.Length == 33 && one.Silhouette is { Size: 64 }, one.Reason);
        Check("recorded: the outline has a plausible person in it", one.Silhouette!.Filled is > 1000 and < 4000 && one.Silhouette.Aspect is > 0.3f and < 0.6f, $"{one.Silhouette.Filled} cells, aspect {one.Silhouette.Aspect}");
        var d = BodyRetarget.Directions(one, map);
        Check("recorded: a real photo gives all 16 joints", d.Count == 16, $"{d.Count}");
        Check("recorded: the real spine points up and the real thighs point down in the model's frame", d["spine"].Y > 0.9f && d["thighL"].Y < -0.9f && d["thighR"].Y < -0.9f, $"spine {d["spine"]}");
        Check("recorded: the real left clavicle points to +x (the person's left is the picture's right)", d["clavicleL"].X > 0.8f && d["clavicleR"].X < -0.8f);
        var self = SilhouetteScore.Score(one.Silhouette, one.Silhouette);
        var other = SilhouetteScore.Score(one.Silhouette, four.Silhouette!);
        var mirror = SilhouetteScore.Score(one.Silhouette, one.Silhouette.Flipped());
        Check("recorded: a real outline against itself is 100", MathF.Abs(self - 100f) < 0.001f);
        Check("recorded (control): a different person and pose scores well below", other < 70f, $"{other:0.0}");
        Check("recorded (control): its own mirror image scores below itself", mirror < 90f, $"{mirror:0.0}");
        Check("recorded: the helper's own failure reply is a typed error", !BodyPoseResult.Parse("{\"id\":2,\"ok\":false,\"reason\":\"No body found in the picture.\"}").Available);
        Check("recorded: a reply with the wrong landmark count is refused", !BodyPoseResult.Parse("{\"ok\":true,\"image\":[[0,0,0,1]],\"world\":[[0,0,0,1]]}").Available);
        Check("recorded: not JSON is refused, not thrown", !BodyPoseResult.Parse("{nope").Available);

        var real = BodyRetarget.Apply(StandingPose(map, false), d, map);
        Check("recorded: the real photo solves onto a standing figure", real.Json != null && real.Solved == 16, $"{real.Solved} solved, {real.Clamped} clamped");
    }
}

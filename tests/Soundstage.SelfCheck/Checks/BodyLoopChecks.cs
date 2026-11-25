// Part 18.5: the matching loop against a fake game. The fake game turns a loaded pose into the segment directions an independent forward
// kinematics gives, adds a fixed error to every joint (a game that does not do exactly what is asked), and "photographs" them as pose landmarks
// and an outline drawn from those landmarks. The photo is drawn the same way from the pose the player wants.
using System.Numerics;
using System.Text.Json.Nodes;
using Soundstage.Expression;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed partial class BodyChecks
{
    // ---- landmarks and an outline from segment directions (model frame) ----

    private static Vector3 ToMp(Vector3 model) => new(model.X, -model.Y, -model.Z);   // the inverse of the map's frame (a half turn about x)

    private static (BodyPoseResult Pose, Dictionary<string, Vector3> Dirs) Figure(Func<string, Vector3> dir)
    {
        var p = new Dictionary<string, Vector3>();
        p["left_hip"] = new(0.1f, 0, 0); p["right_hip"] = new(-0.1f, 0, 0);
        var ms = ToMp(dir("spine")) * 0.45f;
        var c = ToMp(dir("clavicleL")) * 0.2f;
        p["left_shoulder"] = ms + c; p["right_shoulder"] = ms - c;
        var me = ms + ToMp(dir("neck")) * 0.25f;
        p["left_ear"] = me + new Vector3(0.08f, 0, 0); p["right_ear"] = me - new Vector3(0.08f, 0, 0);
        p["nose"] = me + new Vector3(0, 0.02f, -0.08f);
        foreach (var (side, sh) in new[] { ("L", "left"), ("R", "right") })
        {
            var s = p[sh + "_shoulder"];
            p[sh + "_elbow"] = s + ToMp(dir("upperArm" + side)) * 0.28f;
            p[sh + "_wrist"] = p[sh + "_elbow"] + ToMp(dir("forearm" + side)) * 0.25f;
            var hand = p[sh + "_wrist"] + ToMp(dir("hand" + side)) * 0.1f;
            p[sh + "_index"] = hand; p[sh + "_pinky"] = hand;
            p[sh + "_knee"] = p[sh + "_hip"] + ToMp(dir("thigh" + side)) * 0.4f;
            p[sh + "_ankle"] = p[sh + "_knee"] + ToMp(dir("shin" + side)) * 0.4f;
            p[sh + "_heel"] = p[sh + "_ankle"] + new Vector3(0, 0.05f, 0.03f);
            p[sh + "_foot_index"] = p[sh + "_heel"] + ToMp(dir("foot" + side)) * 0.15f;
        }
        var world = new float[BodyPoseResult.Count][];
        for (var i = 0; i < world.Length; i++)
        {
            var has = p.TryGetValue(BodyPoseResult.Names[i], out var v);
            world[i] = new[] { v.X, v.Y, v.Z, has ? 1f : 0f };
        }
        return (new BodyPoseResult { Available = true, World = world, Image = world, Silhouette = Outline(p) }, new Dictionary<string, Vector3>());
    }

    private static SilhouetteGrid Outline(Dictionary<string, Vector3> p)
    {
        // Thick lines between landmarks on a 160 x 160 canvas, then the bounding box of what is covered, sampled to 64 x 64.
        const int N = 160;
        var segs = new (string A, string B, float R)[]
        {
            ("left_shoulder", "right_shoulder", 0.07f), ("left_hip", "right_hip", 0.07f), ("left_shoulder", "left_hip", 0.07f), ("right_shoulder", "right_hip", 0.07f),
            ("left_shoulder", "left_elbow", 0.04f), ("left_elbow", "left_wrist", 0.035f), ("right_shoulder", "right_elbow", 0.04f), ("right_elbow", "right_wrist", 0.035f),
            ("left_hip", "left_knee", 0.06f), ("left_knee", "left_ankle", 0.05f), ("right_hip", "right_knee", 0.06f), ("right_knee", "right_ankle", 0.05f),
            ("left_ear", "right_ear", 0.09f),
        };
        float minX = -1, maxX = 1, minY = -1, maxY = 1;   // the canvas covers a metre either way
        bool Hit(float x, float y)
        {
            foreach (var (a, b, r) in segs)
            {
                var pa = new Vector2(p[a].X, p[a].Y); var pb = new Vector2(p[b].X, p[b].Y);
                var ab = pb - pa; var t = ab.LengthSquared() < 1e-9f ? 0f : Math.Clamp(Vector2.Dot(new Vector2(x, y) - pa, ab) / ab.LengthSquared(), 0f, 1f);
                if (Vector2.Distance(new Vector2(x, y), pa + ab * t) <= r) return true;
            }
            return false;
        }
        var m = new bool[N, N];
        int x0 = N, x1 = -1, y0 = N, y1 = -1;
        for (var yy = 0; yy < N; yy++)
            for (var xx = 0; xx < N; xx++)
            {
                var x = minX + (xx + 0.5f) / N * (maxX - minX); var y = minY + (yy + 0.5f) / N * (maxY - minY);
                if (!Hit(x, y)) continue;
                m[yy, xx] = true;
                x0 = Math.Min(x0, xx); x1 = Math.Max(x1, xx); y0 = Math.Min(y0, yy); y1 = Math.Max(y1, yy);
            }
        var w = x1 - x0 + 1; var h = y1 - y0 + 1;
        var cells = new bool[64 * 64];
        for (var gy = 0; gy < 64; gy++)
            for (var gx = 0; gx < 64; gx++)
                cells[gy * 64 + gx] = m[y0 + Math.Min(h - 1, gy * h / 64), x0 + Math.Min(w - 1, gx * w / 64)];
        return new SilhouetteGrid { Size = 64, Cells = cells, Aspect = w / (float)h, Cover = 0.2f };
    }

    // ---- the fake game ----

    private sealed class FakeGame : IExpressionRig
    {
        private readonly BodyMap map;
        public string Base;
        public string Current;
        public float BiasDeg;
        public int Shots;
        public bool Ready_ = true;
        public int FailShotAt = -1;
        public bool NoOutline;
        public readonly List<string> Loads = new();
        private bool pending;
        private int shotNo;

        public FakeGame(BodyMap map, string basePose, float biasDeg)
        {
            this.map = map;
            Base = basePose;
            Current = basePose;
            BiasDeg = biasDeg;
        }

        public bool Ready(out string why) { why = Ready_ ? "" : "Not in GPose."; return Ready_; }

        public string? Snapshot()
        {
            var n = JsonNode.Parse(Current)!;
            n["Bones"]!["j_f_mayu_l"] = new JsonObject { ["Position"] = "0, 0, 0", ["Rotation"] = "0, 0, 0, 1", ["Scale"] = "1, 1, 1" };
            return n.ToJsonString();
        }

        public bool Load(string poseJson, out string why)
        {
            why = "";
            Loads.Add(poseJson);
            // A body-only load: the bones in it replace the game's, the rest stay.
            var cur = JsonNode.Parse(Current)!["Bones"]!.AsObject();
            foreach (var kv in JsonNode.Parse(poseJson)!["Bones"]!.AsObject()) cur[kv.Key] = JsonNode.Parse(kv.Value!.ToJsonString());
            Current = new JsonObject { ["Bones"] = JsonNode.Parse(cur.ToJsonString()) }.ToJsonString();
            return true;
        }

        public FaceKey? Key(out string why) { why = ""; return null; }

        public bool BeginCapture(out string why)
        {
            why = "";
            if (shotNo++ == FailShotAt) { why = "the booth is busy"; return false; }
            pending = true;
            Shots++;
            return true;
        }

        public CapturePoll Poll(out string path, out string why) { path = pending ? "fake-shot" : ""; why = ""; pending = false; return CapturePoll.Done; }
        public void Discard(string path) { }

        /// <summary>What the pose model would read from a screenshot of the game as it stands.</summary>
        public BodyPoseResult Photograph(string _)
        {
            var fk = new Fk(Current, map);
            var rnd = new Random(7);
            var errors = map.Joints.ToDictionary(j => j.Id, j =>
            {
                var axis = Vector3.Normalize(new Vector3((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f));
                return Quaternion.CreateFromAxisAngle(axis, BiasDeg * MathF.PI / 180f);
            });
            var (pose, _) = Figure(id => Vector3.Transform(fk.Segment(map.Joints.First(j => j.Id == id)), errors[id]));
            return NoOutline ? new BodyPoseResult { Available = true, World = pose.World, Image = pose.Image } : pose;
        }
    }

    private static BodyPoseResult PhotoOf(BodyMap map, string pose)
    {
        var fk = new Fk(pose, map);
        return Figure(id => fk.Segment(map.Joints.First(j => j.Id == id))).Pose;
    }

    /// <summary>The standing figure with the player's wanted pose solved onto it: left arm out and right arm up, left knee lifted.</summary>
    private static string WantedPose(BodyMap map)
    {
        var standing = StandingPose(map, false);
        var dirs = new Dictionary<string, Vector3>();
        var fk = new Fk(standing, map);
        foreach (var j in map.Joints) dirs[j.Id] = fk.Segment(j);
        dirs["upperArmL"] = Vector3.UnitX; dirs["forearmL"] = Vector3.UnitX; dirs["handL"] = Vector3.UnitX;
        dirs["upperArmR"] = Vector3.Normalize(new Vector3(-0.4f, 1f, 0f)); dirs["forearmR"] = Vector3.Normalize(new Vector3(-0.3f, 1f, 0f)); dirs["handR"] = Vector3.Normalize(new Vector3(-0.3f, 1f, 0f));
        dirs["thighL"] = Vector3.Normalize(new Vector3(0.2f, -0.6f, 0.8f)); dirs["shinL"] = Vector3.Normalize(new Vector3(0.1f, -0.9f, -0.3f));
        return BodyRetarget.Apply(standing, dirs, map).Json!;
    }

    private static void Pump(BodyMatch m, int ms = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (m.Running && DateTime.UtcNow < until) { m.Tick(); Thread.Sleep(1); }
    }

    private void Loop()
    {
        var map = LoadMap();
        var standing = StandingPose(map, false);
        var photo = PhotoOf(map, WantedPose(map));
        var log = new List<string>();

        // 1. A game that does exactly what it is asked.
        {
            var game = new FakeGame(map, standing, 0f);
            var m = new BodyMatch(game, game.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            var started = m.Start(photo, mirror: false);
            Pump(m);
            Check("loop (exact game): it starts and ends Done", started && m.State == MatchState.Done, m.Status);
            Check("loop (exact game): the first read is the starting figure and scores low", m.ScoreBefore is < 80f, $"{m.ScoreBefore:0.0}");
            Check("loop (exact game): the result scores near 100", m.ScoreAfter is > 90f, $"{m.ScoreAfter:0.0}");
            Check("loop (exact game): the angle error is near 0", m.Iterations[m.BestIndex].AngleErrorDeg is < 1f, $"{m.Iterations[m.BestIndex].AngleErrorDeg:0.00} deg");
            Check("loop (exact game): the best pass is kept, not the last", m.Iterations.All(i => i.Score <= m.Iterations[m.BestIndex].Score + 1e-4f));
            Check("loop (exact game): a picture per pass and no more", game.Shots == m.Iterations.Count, $"{game.Shots} shots, {m.Iterations.Count} passes");
            Check("loop: only body bones were ever loaded (no face bone in any load)", game.Loads.Count > 0 && game.Loads.All(j => !JsonNode.Parse(j)!["Bones"]!.AsObject().Any(k => k.Key.StartsWith("j_f_"))));
            var fk = new Fk(game.Current, map);
            var after = map.Joints.Average(j => Angle(fk.Segment(j), BodyRetarget.Directions(photo, map)[j.Id]));
            Check("loop (exact game): the character ends up where the photo is, by independent forward kinematics", after < 1f, $"mean {after:0.00} deg");
            Check("loop (control): the character started far from the photo", map.Joints.Average(j => Angle(new Fk(standing, map).Segment(j), BodyRetarget.Directions(photo, map)[j.Id])) > 8f);
        }

        // 2. A game that is 18 degrees off on every joint: the feedback has to earn its keep.
        {
            var withFeedback = RunBiased(map, standing, photo, 18f, gain: 1f);
            var without = RunBiased(map, standing, photo, 18f, gain: 0f);
            Check("loop (biased game): pass 1 alone is left with a real error", without.Iterations.Count >= 2 && without.Iterations[1].AngleErrorDeg is > 8f, $"{without.Iterations[1].AngleErrorDeg:0.0} deg");
            Check("loop (biased game): carrying the gap forward cuts the angle error", withFeedback.Iterations[withFeedback.BestIndex].AngleErrorDeg < without.Iterations[without.BestIndex].AngleErrorDeg - 4f,
                $"with {withFeedback.Iterations[withFeedback.BestIndex].AngleErrorDeg:0.0} deg, without {without.Iterations[without.BestIndex].AngleErrorDeg:0.0} deg");
            Check("loop (biased game): and does not lower the outline score", withFeedback.ScoreAfter >= without.ScoreAfter - 0.5f, $"with {withFeedback.ScoreAfter:0.0}, without {without.ScoreAfter:0.0}");
            Check("loop (biased game): the result beats the start", withFeedback.ScoreAfter > withFeedback.ScoreBefore + 5f, $"{withFeedback.ScoreBefore:0.0} to {withFeedback.ScoreAfter:0.0}");
        }

        // 3. Termination.
        {
            var flat = new FakeGame(map, standing, 0f);
            var m = new BodyMatch(flat, _ => FlatReply(flat, map), new BodySettings { SettleFrames = 0, Epsilon = 1f, MaxIterations = 6 }, map, log.Add);
            m.Start(photo, false); Pump(m);
            Check("termination: a game that never improves stops after the second solve pass, well before the cap", m.State == MatchState.Done && flat.Shots == 3, $"{flat.Shots} shots");
            Check("termination: and keeps the starting pose, saying so", m.BestIndex == 0 && m.Status.Contains("nothing was changed"), m.Status);

            var capped = new FakeGame(map, standing, 0f);
            var m2 = new BodyMatch(capped, _ => FlatReply(capped, map), new BodySettings { SettleFrames = 0, Epsilon = -1f, MaxIterations = 3 }, map, log.Add);
            m2.Start(photo, false); Pump(m2);
            Check("termination: the cap itself stops it (never-stopping epsilon, 3 passes)", m2.State == MatchState.Done && capped.Shots == 4, $"{capped.Shots} shots");
        }

        // 4. Failures leave the character as it was, and say what happened.
        {
            var g = new FakeGame(map, standing, 0f) { FailShotAt = 0 };
            var m = new BodyMatch(g, g.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m.Start(photo, false); Pump(m);
            Check("failure: a first picture that cannot be taken ends Failed with advice", m.State == MatchState.Failed && m.Status.Contains("full-body"), m.Status);
            Check("failure: nothing changed (the body put back is the body it started with)", g.Loads.Count <= 1 && new Fk(g.Current, map).Rot["j_ude_a_l"] == new Fk(standing, map).Rot["j_ude_a_l"]);

            var g2 = new FakeGame(map, standing, 0f) { FailShotAt = 2 };
            var m2 = new BodyMatch(g2, g2.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m2.Start(photo, false); Pump(m2);
            Check("failure: a picture lost part way keeps the best so far and ends Done", m2.State == MatchState.Done && m2.Iterations.Count == 2 && m2.Status.Contains("best so far"), m2.Status);

            var g3 = new FakeGame(map, standing, 0f) { NoOutline = true };
            var m3 = new BodyMatch(g3, g3.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m3.Start(photo, false); Pump(m3);
            Check("failure: a game picture with no outline scores 0, says so, and the joint angles break the tie", m3.State == MatchState.Done && m3.ScoreAfter == 0f && m3.ScoreBefore == 0f && m3.Notes.Any(n => n.Contains("no outline")) && m3.Iterations[m3.BestIndex].AngleErrorDeg < m3.Iterations[0].AngleErrorDeg, m3.Status);

            var noSil = new BodyPoseResult { Available = true, World = photo.World, Image = photo.Image };
            var g4 = new FakeGame(map, standing, 0f);
            var m4 = new BodyMatch(g4, g4.Photograph, new BodySettings(), map, log.Add);
            Check("failure: a photo with no outline is refused before anything moves", !m4.Start(noSil, false) && m4.State == MatchState.Failed && g4.Loads.Count == 0 && g4.Shots == 0, m4.Status);
            Check("failure: an unavailable photo is refused with its own reason", !m4.Start(BodyPoseResult.Unavailable("No body found in the picture."), false) && m4.Status.Contains("No body found"));
            g4.Ready_ = false;
            Check("failure: not in GPose is refused with the rig's reason", !m4.Start(photo, false) && m4.Status.Contains("GPose"));
        }

        // 5. Cancel and undo.
        {
            var g = new FakeGame(map, standing, 0f);
            var m = new BodyMatch(g, g.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m.Start(photo, false);
            for (var i = 0; i < 400 && m.Iterations.Count < 2; i++) { m.Tick(); Thread.Sleep(1); }
            var moved = new Fk(g.Current, map).Rot["j_ude_a_l"] != new Fk(standing, map).Rot["j_ude_a_l"];
            m.Cancel();
            Check("cancel: the run was under way and had moved the arm", moved);
            Check("cancel: Cancelled, and the body is put back", m.State == MatchState.Cancelled && new Fk(g.Current, map).Rot["j_ude_a_l"] == new Fk(standing, map).Rot["j_ude_a_l"], m.Status);

            var g2 = new FakeGame(map, standing, 0f);
            var m2 = new BodyMatch(g2, g2.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m2.Start(photo, false); Pump(m2);
            Check("undo: after a finished match the arm has moved", new Fk(g2.Current, map).Rot["j_ude_a_l"] != new Fk(standing, map).Rot["j_ude_a_l"]);
            Check("undo: Undo puts the starting body back", m2.Undo(out _) && new Fk(g2.Current, map).Rot["j_ude_a_l"] == new Fk(standing, map).Rot["j_ude_a_l"]);
            Check("undo (control): nothing to undo before any run", !new BodyMatch(g2, g2.Photograph, new BodySettings(), map, log.Add).Undo(out var why) && why.Length > 0);
        }

        // 6. The skeleton check stops a map that does not fit.
        {
            var upside = StandingPose(map, true);
            var g = new FakeGame(map, upside, 0f);
            var m = new BodyMatch(g, g.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            Check("skeleton: a character the map does not fit is refused before anything moves", !m.Start(photo, false) && m.Status.Contains("does not fit") && g.Loads.Count == 0 && g.Shots == 0, m.Status);
            var m2 = new BodyMatch(g, g.Photograph, new BodySettings { SettleFrames = 0, SkipSkeletonCheck = true }, map, log.Add);
            Check("skeleton (control): with the check switched off it goes ahead", m2.Start(photo, false));
            m2.Cancel();
        }

        // 7. Mirror.
        {
            var g = new FakeGame(map, standing, 0f);
            var m = new BodyMatch(g, g.Photograph, new BodySettings { SettleFrames = 0 }, map, log.Add);
            m.Start(photo, mirror: true);
            var straight = BodyRetarget.Directions(photo, map);
            Check("mirror: the left arm follows the photo's right arm", NearV(m.Target["upperArmL"], straight["upperArmL"], 0.5f) == false && Vector3.Dot(m.Target["upperArmL"], BodyRetarget.Directions(photo, map, new BodyRetarget.Options { Mirror = true })["upperArmL"]) > 0.999f);
            m.Cancel();
        }

        // 8. Settings and the rating rule.
        {
            var s = new BodySettings();
            Check("trials: no ratings is not experimental", !BodyTrials.IsExperimental(s) && BodyTrials.Describe(s).Contains("No rated"));
            foreach (var r in new[] { 2, 2, 3, 2, 1 }) s.Trials.Add(new ExpressionTrial { Rating = r });
            Check("trials: a median of 2 over five photos is experimental", BodyTrials.IsExperimental(s) && BodyTrials.Median(s) == 2f && BodyTrials.Describe(s).StartsWith("Experimental"));
            s.Trials.Clear();
            foreach (var r in new[] { 4, 3, 5, 3, 1 }) s.Trials.Add(new ExpressionTrial { Rating = r });
            Check("trials (control): a median of 3 over five photos is not experimental", !BodyTrials.IsExperimental(s) && BodyTrials.Median(s) == 3f);
            s.Trials.RemoveRange(0, 2);
            Check("trials: fewer than five ratings never label it experimental", !BodyTrials.IsExperimental(s));
        }
    }

    private static BodyPoseResult FlatReply(FakeGame g, BodyMap map)
    {
        g.Photograph("");   // keeps the shot counted the same way
        var fk = new Fk(StandingPose(map, false), map);
        return Figure(id => fk.Segment(map.Joints.First(j => j.Id == id))).Pose;
    }

    private BodyMatch RunBiased(BodyMap map, string standing, BodyPoseResult photo, float bias, float gain)
    {
        var game = new FakeGame(map, standing, bias);
        var m = new BodyMatch(game, game.Photograph, new BodySettings { SettleFrames = 0, Gain = gain, MaxIterations = 5, Epsilon = 0.1f }, map, _ => { });
        m.Start(photo, false);
        Pump(m);
        return m;
    }
}

// Part 17: the expression table, solver, apply, calibrator and matching loop. No game and no face model: a fake face is a straight-line
// world (each bone offset moves some blendshapes by a fixed amount), so the calibration has a known answer to recover and the loop a
// known place to land.
using System.Text.Json.Nodes;
using Soundstage.Expression;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed class ExpressionChecks : CheckArea
{
    public override string Name => "Expression (Part 17)";
    public override int Order => 170;

    public override void Run()
    {
        SolverChecks();
        ErrorAndScore();
        TableRoundTrip();
        ApplyChecks();
        ChannelChecks();
        CalibrationChecks();
        MatchChecks();
        TerminationChecks();
        TrialChecks();
    }

    // ---- the fake face ----

    private static readonly string[] Names = { "browDownLeft", "browDownRight", "browInnerUp", "eyeBlinkLeft", "jawOpen", "mouthSmileLeft", "cheekPuff" };

    // bone, kind, axis, blendshape, change per unit (per pose unit for pos, per degree for rot)
    private static readonly (string Bone, string Kind, string Axis, string Blend, float PerUnit)[] Truth =
    {
        ("j_f_mayu_l", "pos", "y", "browDownLeft", 25f),
        ("j_f_mayu_r", "pos", "y", "browDownRight", 25f),
        ("j_f_mmayu_l", "pos", "y", "browInnerUp", 25f),
        ("j_f_mmayu_l", "pos", "x", "browDownLeft", -10f),
        ("j_f_mabdn_01_l", "pos", "y", "eyeBlinkLeft", 25f),
        ("j_f_ago", "rot", "x", "jawOpen", 0.02f),
        ("j_f_dmlip_01_l", "pos", "x", "mouthSmileLeft", 25f),
        ("j_f_dmlip_01_l", "pos", "z", "cheekPuff", 25f),
    };

    private static readonly string[] FaceBones = { "j_f_mayu_l", "j_f_mayu_r", "j_f_mmayu_l", "j_f_mabdn_01_l", "j_f_ago", "j_f_dmlip_01_l" };

    private static string NeutralPose()
    {
        var bones = new JsonObject();
        foreach (var b in FaceBones.Append("j_kosi"))
            bones[b] = new JsonObject { ["Position"] = "0, 0, 0", ["Rotation"] = "0, 0, 0, 1", ["Scale"] = "1, 1, 1" };
        return new JsonObject { ["TypeName"] = "Brio Pose", ["Bones"] = bones, ["MainHand"] = new JsonObject(), ["OffHand"] = new JsonObject() }.ToJsonString();
    }

    private sealed class World
    {
        public string Json = NeutralPose();
        public int Loads;
        public int Captures;
        public int Discards;
        public float BaseValue = 0.3f;

        public float Offset(string bone, string kind, string axis)
        {
            var node = JsonNode.Parse(Json)!["Bones"]![bone] as JsonObject;
            if (node == null) return 0f;
            var ax = axis == "x" ? 0 : axis == "y" ? 1 : 2;
            if (kind == "pos") return ExpressionApply.ReadVector(node, "Position", 3, out _)?[ax] ?? 0f;
            var q = ExpressionApply.ReadVector(node, "Rotation", 4, out _) ?? new float[] { 0, 0, 0, 1 };
            // rotation about x only in these checks: the angle is 2 x atan2(qx, qw)
            return ax == 0 ? 2f * MathF.Atan2(q[0], q[3]) * 180f / MathF.PI : 0f;
        }

        public Dictionary<string, float> Blends()
        {
            var d = Names.ToDictionary(n => n, _ => BaseValue);
            foreach (var t in Truth) d[t.Blend] += Offset(t.Bone, t.Kind, t.Axis) * t.PerUnit;
            foreach (var k in d.Keys.ToList()) d[k] = Math.Clamp(d[k], 0f, 1f);
            d["_neutral"] = 0.1f;
            return d;
        }
    }

    private sealed class FakeRig : IExpressionRig
    {
        public readonly World W;
        public bool IsReady = true;
        public bool FailCapturesAfter = false;
        public int CapturesAllowed = int.MaxValue;
        private bool pending;
        private int polls;

        public FakeRig(World w) => W = w;

        public bool Ready(out string why) { why = IsReady ? "" : "Not in GPose."; return IsReady; }
        public string? Snapshot() => W.Json;
        public bool Load(string poseJson, out string why) { why = ""; W.Json = poseJson; W.Loads++; return true; }
        public FaceKey? Key(out string why) { why = ""; return new FaceKey(8, 15, 0, 1); }

        public bool BeginCapture(out string why)
        {
            why = "";
            if (W.Captures >= CapturesAllowed) { why = "out of captures"; return false; }
            pending = true;
            polls = 0;
            return true;
        }

        public CapturePoll Poll(out string path, out string why)
        {
            path = "";
            why = "";
            if (!pending) { why = "none started"; return CapturePoll.Failed; }
            if (polls++ < 1) return CapturePoll.Pending;   // one frame of waiting, like a real capture
            pending = false;
            W.Captures++;
            path = "fake-" + W.Captures + ".png";
            return CapturePoll.Done;
        }

        public void Discard(string path) => W.Discards++;
    }

    private sealed class FakeAnalyser : IFaceAnalyser
    {
        private readonly Func<FaceResult> make;
        public FakeAnalyser(Func<FaceResult> make) => this.make = make;
        public int Calls;
        public bool CanTry => true;
        public string Status => "fake";
        public FaceResult Analyse(string imagePath) { Calls++; return make(); }
    }

    private static FaceResult Face(Dictionary<string, float> blends, float yaw = 0) => new() { Available = true, Blendshapes = blends, YawDeg = yaw, Confidence = 0.5f };

    private static FakeAnalyser Reads(World w) => new(() => Face(w.Blends()));

    private static ExpressionSettings Settings() => new() { SettleFrames = 1, MaxBones = 13 };

    private static bool Pump(Action tick, Func<bool> running, int limit = 20000)
    {
        for (var i = 0; i < limit; i++)
        {
            tick();
            if (!running()) return true;
            Thread.Sleep(1);   // the face analysis runs on a worker, so give it real time
        }
        return false;
    }

    private static string TempFolder()
    {
        var d = Path.Combine(Path.GetTempPath(), "ss-expr-check-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(d);
        return d;
    }

    private static ResponseModel.Channel Ch(float[] plus, float[] minus, float maxSteps, string bone = "b") =>
        new(new ChannelRecord { Bone = bone, Kind = "pos", Axis = "x", Step = 1, Max = maxSteps }, plus, minus, maxSteps);

    // ---- 17.3 solver ----

    private void SolverChecks()
    {
        // three blendshapes, three channels: channel j moves blendshape j by +0.1 per step one way and -0.05 the other.
        var chans = new[]
        {
            Ch(new[] { 0.1f, 0, 0 }, new[] { -0.05f, 0, 0 }, 3f),
            Ch(new[] { 0, 0.1f, 0 }, new[] { 0, -0.05f, 0 }, 3f),
            Ch(new[] { 0, 0, 0.1f }, new[] { 0, 0, -0.05f }, 3f),
        };
        var model = new ResponseModel(new[] { "a", "b", "c" }, new float[] { 0.3f, 0.3f, 0.3f }, chans);
        var w = new float[] { 1, 1, 1 };

        var s = Solver.Solve(model, new[] { 0.2f, 0f, 0f }, w);
        Check("solver: a known answer comes back (+0.2 on a = 2 steps)", Near(s[0], 2f, 0.02f) && Near(s[1], 0f, 0.02f) && Near(s[2], 0f, 0.02f), string.Join(", ", s));

        s = Solver.Solve(model, new[] { -0.1f, 0.1f, 0f }, w);
        Check("solver: the other direction uses the minus column (-0.1 on a = 2 steps down)", Near(s[0], -2f, 0.02f) && Near(s[1], 1f, 0.02f), string.Join(", ", s));

        s = Solver.Solve(model, new[] { 0.9f, -0.9f, 0f }, w);
        Check("solver: clamps to the safe range (wanted 9 steps, allowed 3)", Near(s[0], 3f, 1e-3f) && Near(s[1], -3f, 1e-3f), string.Join(", ", s));
        Check("solver: Clamp keeps every channel in range", Solver.Clamp(model, new[] { 9f, -9f, 1f }).SequenceEqual(new[] { 3f, -3f, 1f }));

        // non-negative least squares: two channels that both push 'a', one also pushes 'b' the wrong way. The better one must win.
        var two = new ResponseModel(new[] { "a", "b" }, new float[] { 0, 0 }, new[]
        {
            Ch(new[] { 0.1f, 0f }, new[] { 0f, 0f }, 5f, "clean"),
            Ch(new[] { 0.1f, 0.1f }, new[] { 0f, 0f }, 5f, "messy"),
        });
        s = Solver.Solve(two, new[] { 0.2f, 0f }, new float[] { 1, 1 });
        Check("solver: prefers the channel with no side effect", s[0] > 1.9f && MathF.Abs(s[1]) < 0.05f, string.Join(", ", s));

        // weights: a conflict where one blendshape must lose. Two blendshapes both pushed by one channel, wanted +0.1 and -0.1.
        var conflict = new ResponseModel(new[] { "heavy", "light" }, new float[] { 0, 0 }, new[] { Ch(new[] { 0.1f, 0.1f }, new[] { -0.1f, -0.1f }, 5f) });
        var heavy = Solver.Solve(conflict, new[] { 0.1f, -0.1f }, new float[] { 3, 1 });
        var light = Solver.Solve(conflict, new[] { 0.1f, -0.1f }, new float[] { 1, 3 });
        Check("solver: the heavier blendshape wins a conflict", heavy[0] > 0.2f && light[0] < -0.2f, $"{heavy[0]:0.00} vs {light[0]:0.00}");

        Check("solver: no channels gives an empty answer, not an error", Solver.Solve(new ResponseModel(new[] { "a" }, new float[] { 0 }, Array.Empty<ResponseModel.Channel>()), new[] { 1f }, new[] { 1f }).Length == 0);

        var predicted = model.Predict(new[] { 2f, -2f, 0f });
        Check("solver: Predict uses plus one way and minus the other", Near(predicted[0], 0.5f) && Near(predicted[1], 0.2f) && Near(predicted[2], 0.3f), string.Join(", ", predicted));

        var visible = Weights.For(new[] { "browDownLeft", "cheekPuff" });
        Check("solver: the 12 visible blendshapes weigh 3 and the rest 1", Weights.Visible.Length == 12 && visible[0] == 3f && visible[1] == 1f);
    }

    private void ErrorAndScore()
    {
        var w = new float[] { 3, 1 };
        Check("score: identical faces score 100", Near(Weights.Score(Weights.WeightedError(new[] { 0.2f, 0.4f }, new[] { 0.2f, 0.4f }, w)), 100f));
        Check("score: an error of 0.5 scores 0 and worse is clamped at 0", Near(Weights.Score(0.5f), 0f) && Near(Weights.Score(0.9f), 0f));
        Check("score: an error of 0.25 scores 50", Near(Weights.Score(0.25f), 50f));
        var e = Weights.WeightedError(new[] { 0f, 0f }, new[] { 0.4f, 0.8f }, w);   // (3 x 0.4 + 1 x 0.8) / 4 = 0.5
        Check("score: the error is weighted", Near(e, 0.5f), e.ToString());
    }

    // ---- 17.1 table ----

    private void TableRoundTrip()
    {
        var key = new FaceKey(8, 15, 0, 3);
        var t = ExpressionTable.For(key);
        t.Baseline["jawOpen"] = 0.1f;
        t.Channels.Add(new ChannelRecord { Bone = "j_f_ago", Kind = "rot", Axis = "x", Step = 6, Max = 12, Plus = new() { ["jawOpen"] = 0.12f }, Minus = new() { ["jawOpen"] = -0.1f } });
        t.Channels.Add(new ChannelRecord { Bone = "j_f_mayu_l", Kind = "pos", Axis = "y", Step = 0.004f, Max = 0.008f });
        Check("table: the face key names race, clan, gender and face", key.Id == "race8-clan15-g0-face3", key.Id);

        var back = ExpressionTable.TryParse(t.ToJson(), out var problem);
        Check("table: round trip keeps the key, baseline and channels", back != null && back.Key == key && back.Channels.Count == 2 &&
            Near(back.Baseline["jawOpen"], 0.1f) && back.Channels[0].Plus!["jawOpen"] == 0.12f && back.Channels[0].Minus!["jawOpen"] == -0.1f, problem);
        Check("table: a channel with no measurement stays unmeasured and the table is not complete", back != null && !back.Channels[1].Measured && !back.Complete && back.MeasuredCount == 1);
        Check("table: a different version is refused", ExpressionTable.TryParse(t.ToJson().Replace("\"Version\": 1", "\"Version\": 9"), out var p2) == null && p2.Contains("version"), p2);
        Check("table: garbage is refused with a reason", ExpressionTable.TryParse("{ not json", out var p3) == null && p3.Length > 0);

        var dir = TempFolder();
        try
        {
            var store = new ExpressionTableStore(Path.Combine(dir, "expression-tables"));
            Check("table store: nothing stored gives null, not an error", store.Load(key, out var p4) == null && p4.Length == 0);
            Check("table store: saves to expression-tables/<faceKey>.json", store.Save(t, out var p5) && File.Exists(Path.Combine(dir, "expression-tables", "race8-clan15-g0-face3.json")), p5);
            var loaded = store.Load(key, out _);
            Check("table store: loads what it saved", loaded != null && loaded.Channels.Count == 2 && !File.Exists(store.PathFor(key) + ".tmp"));
            Check("table store: a different face has its own file", store.Load(new FaceKey(7, 13, 0, 3), out _) == null);
            File.WriteAllText(store.PathFor(key), "{ broken");
            Check("table store: a damaged file is reported, not thrown", store.Load(key, out var p6) == null && p6.Length > 0, p6);
            Check("table store: delete removes it", store.Delete(key) && !File.Exists(store.PathFor(key)));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- 17.4 apply ----

    private void ApplyChecks()
    {
        var pose = NeutralPose();
        var json = ExpressionApply.Build(pose, new[] { new ExpressionApply.Offset("j_f_mayu_l", "pos", "y", 0.004f) }, out var err);
        var bones = json == null ? null : JsonNode.Parse(json)!["Bones"]!.AsObject();
        Check("apply: builds a pose with the face bones only", json != null && bones != null && !bones.ContainsKey("j_kosi") && bones.ContainsKey("j_f_mayu_l"), err);
        var pos = bones == null ? null : ExpressionApply.ReadVector(bones["j_f_mayu_l"]!.AsObject(), "Position", 3, out var text);
        Check("apply: the offset lands on the right axis and the others stay", pos != null && Near(pos[1], 0.004f, 1e-6f) && pos[0] == 0f && pos[2] == 0f);
        Check("apply: a bone with no offset keeps its transform", bones != null && bones["j_f_mayu_r"]!["Position"]!.GetValue<string>() == "0, 0, 0");

        var rot = ExpressionApply.Build(pose, new[] { new ExpressionApply.Offset("j_f_ago", "rot", "x", 10f) }, out err);
        var q = rot == null ? null : ExpressionApply.ReadVector(JsonNode.Parse(rot)!["Bones"]!["j_f_ago"]!.AsObject(), "Rotation", 4, out _);
        Check("apply: a rotation offset turns the bone about its own axis", q != null && Near(2f * MathF.Atan2(q[0], q[3]) * 180f / MathF.PI, 10f, 0.01f), err);

        Check("apply: no offsets is the plain expression-only copy (how the original comes back)",
            ExpressionApply.Build(pose, Array.Empty<ExpressionApply.Offset>(), out _) is { } plain && !plain.Contains("j_kosi") && plain.Contains("j_f_ago"));
        Check("apply: an unknown bone is refused with its name", ExpressionApply.Build(pose, new[] { new ExpressionApply.Offset("j_f_nope", "pos", "x", 1f) }, out err) == null && err.Contains("j_f_nope"), err);
        Check("apply: a bad axis is refused", ExpressionApply.Build(pose, new[] { new ExpressionApply.Offset("j_f_ago", "pos", "w", 1f) }, out err) == null);
        Check("apply: text that is not JSON is refused", ExpressionApply.Build("nope", Array.Empty<ExpressionApply.Offset>(), out err) == null && err.Length > 0);

        // a different way of writing the vector (array, then object) is read and written back in the same form
        var arr = new JsonObject { ["Bones"] = new JsonObject { ["j_f_mayu_l"] = new JsonObject { ["Position"] = new JsonArray(0.0, 0.0, 0.0) } } }.ToJsonString();
        var a2 = ExpressionApply.Build(arr, new[] { new ExpressionApply.Offset("j_f_mayu_l", "pos", "x", 0.5f) }, out err);
        Check("apply: an array vector is read and written as an array", a2 != null && JsonNode.Parse(a2)!["Bones"]!["j_f_mayu_l"]!["Position"] is JsonArray, err);
        var obj = new JsonObject { ["Bones"] = new JsonObject { ["j_f_mayu_l"] = new JsonObject { ["Position"] = new JsonObject { ["X"] = 0.0, ["Y"] = 0.0, ["Z"] = 0.0 } } } }.ToJsonString();
        var o2 = ExpressionApply.Build(obj, new[] { new ExpressionApply.Offset("j_f_mayu_l", "pos", "y", 0.5f) }, out err);
        Check("apply: an object vector keeps its X, Y, Z keys", o2 != null && Near(JsonNode.Parse(o2)!["Bones"]!["j_f_mayu_l"]!["Position"]!["Y"]!.GetValue<float>(), 0.5f), err);
        var empty = new JsonObject { ["Bones"] = new JsonObject { ["j_f_mayu_l"] = new JsonObject() } }.ToJsonString();
        Check("apply: a bone with an empty entry gets a Position written", ExpressionApply.Build(empty, new[] { new ExpressionApply.Offset("j_f_mayu_l", "pos", "x", 0.25f) }, out err) is { } e1 && e1.Contains("0.25"), err);
    }

    private void ChannelChecks()
    {
        var picked = ExpressionChannels.PickBones(NeutralPose(), 13);
        Check("channels: picks the face bones in the pose and nothing else", picked.Count == 6 && !picked.Contains("j_kosi") && picked.Contains("j_f_ago"), string.Join(",", picked));
        Check("channels: round robin gives every region a bone before any gets a second", ExpressionChannels.PickBones(NeutralPose(), 4).Select(b => b[..7]).Distinct().Count() >= 3);
        var built = ExpressionChannels.Build(NeutralPose(), new ExpressionSettings { MaxBones = 13 });
        Check("channels: three axes per bone, the jaw turns and the rest slide", built.Count == 18 && built.Where(c => c.Bone == "j_f_ago").All(c => c.Kind == "rot") && built.Where(c => c.Bone != "j_f_ago").All(c => c.Kind == "pos"));
        Check("channels: the safe range is the step times the safe steps", built.All(c => Near(c.Max, c.Step * 2f, 1e-6f)));
        var cap = ExpressionChannels.Build(NeutralPose(), new ExpressionSettings { MaxBones = 2 });
        Check("channels: the bone cap holds", cap.Select(c => c.Bone).Distinct().Count() == 2 && cap.Count == 6);
        Check("channels: a different race's bone names are picked up from its own pose", ExpressionChannels.PickBones(new JsonObject { ["Bones"] = new JsonObject { ["j_f_mayu_r"] = new JsonObject(), ["j_f_hoho_l"] = new JsonObject() } }.ToJsonString(), 13).Count == 2);
    }

    // ---- 17.2 calibration ----

    private (Calibrator Cal, World World, FakeRig Rig, ExpressionTableStore Store, string Dir) Calib(FakeAnalyser? analyser = null, World? world = null)
    {
        var w = world ?? new World();
        var rig = new FakeRig(w);
        var dir = TempFolder();
        var store = new ExpressionTableStore(Path.Combine(dir, "expression-tables"));
        return (new Calibrator(rig, analyser ?? Reads(w), store, Settings(), _ => { }, () => new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc)), w, rig, store, dir);
    }

    private void CalibrationChecks()
    {
        var (cal, w, rig, store, dir) = Calib();
        try
        {
            var original = ExpressionApply.Build(NeutralPose(), Array.Empty<ExpressionApply.Offset>(), out _)!;
            Check("calibrate: starts", cal.Start() && cal.Running && cal.Total == 37, $"total {cal.Total}");
            var finished = Pump(cal.Tick, () => cal.Running);
            Check("calibrate: finishes on its own", finished && cal.State == CalibState.Done, cal.Status);
            Check("calibrate: 80 or so captures at the default (here 6 bones: 1 + 36)", w.Captures == 37, w.Captures.ToString());
            Check("calibrate: every move is measured and the table is complete", cal.Table.Complete && cal.Table.Channels.Count == 18);
            Check("calibrate: every picture file is thrown away", w.Discards == w.Captures);
            Check("calibrate: the original expression is put back at the end", w.Json == original);

            var ch = cal.Table.Channels.First(c => c.Id == "j_f_mayu_l|pos|y");
            Check("calibrate: recovers the response (browDownLeft +0.1 at +step, -0.1 at -step)", Near(ch.Plus!["browDownLeft"], 0.1f, 0.005f) && Near(ch.Minus!["browDownLeft"], -0.1f, 0.005f),
                $"{ch.Plus!.GetValueOrDefault("browDownLeft")} / {ch.Minus!.GetValueOrDefault("browDownLeft")}");
            var jaw = cal.Table.Channels.First(c => c.Id == "j_f_ago|rot|x");
            Check("calibrate: recovers the jaw (6 degrees = jawOpen +0.12)", Near(jaw.Plus!["jawOpen"], 0.12f, 0.005f));
            var none = cal.Table.Channels.First(c => c.Id == "j_f_mayu_r|pos|z");
            Check("calibrate: a move that changes nothing stores an empty response", none.Plus!.Count == 0 && none.Minus!.Count == 0);
            Check("calibrate: the baseline is the starting face", Near(cal.Table.Baseline["jawOpen"], 0.3f) && !cal.Table.Baseline.ContainsKey("_neutral"));

            var saved = store.Load(new FaceKey(8, 15, 0, 1), out _);
            Check("calibrate: the table is stored under the face key", saved != null && saved.Complete && saved.SavedUtc.StartsWith("2026-11-21"));
        }
        finally { Directory.Delete(dir, recursive: true); }

        // cancel half way, then resume
        (cal, w, rig, store, dir) = Calib();
        try
        {
            cal.Start();
            for (var i = 0; i < 4000 && w.Captures < 12; i++) { cal.Tick(); Thread.Sleep(1); }
            Check("calibrate: has progress to show", cal.Done > 0 && cal.Progress > 0f && cal.Progress < 1f, $"{cal.Done}/{cal.Total}");
            cal.Cancel();
            var original = ExpressionApply.Build(NeutralPose(), Array.Empty<ExpressionApply.Offset>(), out _)!;
            Check("calibrate: cancel stops and puts the original expression back", cal.State == CalibState.Cancelled && w.Json == original);
            var partial = store.Load(new FaceKey(8, 15, 0, 1), out _);
            Check("calibrate: cancel keeps the progress made", partial != null && partial.MeasuredCount > 0 && !partial.Complete, partial?.MeasuredCount.ToString());
            var before = w.Captures;
            var resumed = new Calibrator(rig, Reads(w), store, Settings(), _ => { });
            resumed.Start();
            Check("calibrate: a resume knows how much was done", resumed.Done > 0 && resumed.Total == 37);
            Pump(resumed.Tick, () => resumed.Running);
            Check("calibrate: resume finishes the table with fewer pictures than a fresh run", resumed.State == CalibState.Done && resumed.Table.Complete && w.Captures - before < 37, (w.Captures - before).ToString());
        }
        finally { Directory.Delete(dir, recursive: true); }

        // problems
        (cal, w, rig, store, dir) = Calib();
        try
        {
            rig.IsReady = false;
            Check("calibrate: refuses outside GPose with the reason", !cal.Start() && cal.State == CalibState.Failed && cal.Status.Contains("GPose"), cal.Status);
        }
        finally { Directory.Delete(dir, recursive: true); }

        var noFace = new FakeAnalyser(() => FaceResult.Unavailable("No face found in the picture."));
        (cal, w, rig, store, dir) = Calib(noFace);
        try
        {
            cal.Start();
            var done = Pump(cal.Tick, () => cal.Running);
            Check("calibrate: no readable face ends with the reason and does not hang", done && cal.State == CalibState.Failed && cal.Status.Contains("No face found"), cal.Status);
            Check("calibrate: the original expression is back after a failure", w.Json == ExpressionApply.Build(NeutralPose(), Array.Empty<ExpressionApply.Offset>(), out _));
        }
        finally { Directory.Delete(dir, recursive: true); }

        // a read that goes wrong half way: stops after four bad pictures in a row
        var calls = 0;
        var flaky = new FakeAnalyser(() => ++calls <= 5 ? Face(new World().Blends()) : FaceResult.Unavailable("lost the face"));
        (cal, w, rig, store, dir) = Calib(flaky);
        try
        {
            cal.Start();
            var done = Pump(cal.Tick, () => cal.Running);
            Check("calibrate: four unreadable pictures in a row stop it", done && cal.State == CalibState.Failed && cal.Status.Contains("in a row"), cal.Status);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- 17.5 matching ----

    private (ResponseModel Model, ExpressionTable Table) Calibrated()
    {
        var (cal, w, _, _, dir) = Calib();
        try
        {
            cal.Start();
            Pump(cal.Tick, () => cal.Running);
            return (ResponseModel.From(cal.Table), cal.Table);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    private static FaceResult PhotoWith(params (string Bone, string Kind, string Axis, float Value)[] offsets)
    {
        var w = new World();
        var json = ExpressionApply.Build(w.Json, offsets.Select(o => new ExpressionApply.Offset(o.Bone, o.Kind, o.Axis, o.Value)), out _)!;
        w.Json = json;
        return Face(w.Blends(), yaw: 12f);
    }

    private void MatchChecks()
    {
        var (_, table) = Calibrated();
        var settings = Settings();

        // a target made by moving bones inside the safe range: the loop should land close to it
        var photo = PhotoWith(("j_f_mayu_l", "pos", "y", 0.006f), ("j_f_mmayu_l", "pos", "y", -0.004f), ("j_f_dmlip_01_l", "pos", "x", 0.005f), ("j_f_ago", "rot", "x", 8f));
        var w = new World();
        var rig = new FakeRig(w);
        var loop = new MatchLoop(rig, Reads(w), settings, _ => { });
        Check("match: starts", loop.Start(photo, table) && loop.Running);
        var done = Pump(loop.Tick, () => loop.Running);
        Check("match: finishes", done && loop.State == MatchState.Done, loop.Status);
        Check("match: improves on the starting face", loop.ScoreAfter > loop.ScoreBefore, $"{loop.ScoreBefore:0.0} -> {loop.ScoreAfter:0.0}");
        Check("match: lands close on a face that is reachable (score 97 or better)", loop.ScoreAfter >= 97f, loop.ScoreAfter?.ToString("0.0"));
        Check("match: stays inside the pass cap", loop.Iterations.Count - 1 <= settings.MaxIterations, loop.Iterations.Count.ToString());
        Check("match: the game ends on the best pass", Near(Weights.Score(Weights.WeightedError(Names.Order().Select(n => w.Blends()[n]).ToArray(), Names.Order().Select(n => photo.Blend(n)).ToArray(), Weights.For(Names.Order().ToArray()))), loop.ScoreAfter!.Value, 1.5f));
        Check("match: the head angle difference is reported", loop.AngleNote.Contains("12") || loop.AngleNote.Contains("round"), loop.AngleNote);
        Check("match: the original is available to undo", loop.Undo(out _) && w.Json == ExpressionApply.Build(NeutralPose(), Array.Empty<ExpressionApply.Offset>(), out _));

        // a target past the safe range: the offsets must stop at the limit
        var far = PhotoWith(("j_f_mayu_l", "pos", "y", 0.009f));   // 2.25 steps, safe range is 2
        w = new World();
        rig = new FakeRig(w);
        loop = new MatchLoop(rig, Reads(w), settings, _ => { });
        loop.Start(far, table);
        Pump(loop.Tick, () => loop.Running);
        var applied = new World { Json = w.Json }.Offset("j_f_mayu_l", "pos", "y");
        Check("clamp: no bone goes past its safe range", applied <= 0.008f + 1e-6f && applied > 0.0075f, applied.ToString("0.0000"));
        var bestOffsets = loop.BestOffsets;
        Check("clamp: every offset of the best pass is within the table's range", bestOffsets.All(o => Math.Abs(o.Amount) <= table.Channels.First(c => c.Bone == o.Bone && c.Axis == o.Axis).Max + 1e-6f));

        // a match with no table, or no photo
        loop = new MatchLoop(new FakeRig(new World()), Reads(new World()), settings, _ => { });
        Check("match: refuses without a calibrated table", !loop.Start(photo, ExpressionTable.For(new FaceKey(8, 15, 0, 1))) && loop.State == MatchState.Failed && loop.Status.Contains("calibrated"), loop.Status);
        Check("match: refuses a photo with no face", !loop.Start(FaceResult.Unavailable("No face"), table) && loop.Status == "No face");

        // cancel puts the face back
        w = new World();
        rig = new FakeRig(w);
        loop = new MatchLoop(rig, Reads(w), settings, _ => { });
        loop.Start(photo, table);
        for (var i = 0; i < 12; i++) { loop.Tick(); Thread.Sleep(1); }
        loop.Cancel();
        Check("match: cancel puts the old expression back", loop.State == MatchState.Cancelled && w.Json == ExpressionApply.Build(NeutralPose(), Array.Empty<ExpressionApply.Offset>(), out _));

        // a capture that dies after the first read still ends with the best so far
        w = new World();
        rig = new FakeRig(w) { CapturesAllowed = 1 };
        loop = new MatchLoop(rig, Reads(w), settings, _ => { });
        loop.Start(photo, table);
        done = Pump(loop.Tick, () => loop.Running);
        Check("match: a lost picture ends it with the best so far, not a hang", done && loop.State == MatchState.Done && loop.Iterations.Count == 1, loop.Status);
    }

    // ---- termination ----

    private void TerminationChecks()
    {
        var (_, table) = Calibrated();
        var settings = Settings();
        var photo = PhotoWith(("j_f_mayu_l", "pos", "y", 0.006f));

        // CONTROL: an analyser that returns random values. The loop must end, within the cap, and never hang.
        var rng = new Random(17);
        var randomAnalyser = new FakeAnalyser(() => Face(Names.Append("_neutral").ToDictionary(n => n, _ => (float)rng.NextDouble())));
        var w = new World();
        var loop = new MatchLoop(new FakeRig(w), randomAnalyser, settings, _ => { });
        loop.Start(photo, table);
        var done = Pump(loop.Tick, () => loop.Running, limit: 5000);
        Check("termination control: a random analyser ends, it does not hang", done && loop.State == MatchState.Done, loop.Status);
        Check("termination control: a random analyser stops within the cap", loop.Iterations.Count - 1 <= settings.MaxIterations, loop.Iterations.Count.ToString());
        Check("termination control: the best pass is kept, not the last", loop.Iterations[loop.BestIndex].Error <= loop.Iterations.Min(i => i.Error) + 1e-6f);

        // an analyser that keeps improving by more than epsilon every time never converges: it must stop exactly at the cap.
        var calls = 0;
        var target = Names.ToDictionary(n => n, n => photo.Blend(n));
        var improving = new FakeAnalyser(() =>
        {
            var k = calls++;
            var off = 0.4f * MathF.Pow(0.6f, k);
            var d = target.ToDictionary(kv => kv.Key, kv => Math.Clamp(kv.Value + off, 0f, 1f));
            d["_neutral"] = 0.1f;
            return Face(d);
        });
        // the target values sit at least 0.4 below 1 for the names that matter, so the clamp does not eat the error
        var shifted = Face(photo.Blendshapes.ToDictionary(kv => kv.Key, kv => Math.Min(kv.Value, 0.5f)));
        target = Names.ToDictionary(n => n, n => shifted.Blend(n));
        loop = new MatchLoop(new FakeRig(new World()), improving, settings, _ => { });
        loop.Start(shifted, table);
        done = Pump(loop.Tick, () => loop.Running, limit: 5000);
        Check("termination: an analyser that never converges stops at the iteration cap", done && loop.State == MatchState.Done && loop.Iterations.Count - 1 == settings.MaxIterations, $"{loop.Iterations.Count - 1} passes; {loop.Status}");
        Check("termination: the best pass of a steady improver is the last", loop.BestIndex == loop.Iterations.Count - 1);

        // a flat analyser (nothing ever changes) stops after one pass, on epsilon
        var flat = new FakeAnalyser(() => Face(new World().Blends()));
        loop = new MatchLoop(new FakeRig(new World()), flat, settings, _ => { });
        loop.Start(photo, table);
        done = Pump(loop.Tick, () => loop.Running, limit: 5000);
        Check("termination: no improvement stops early on epsilon", done && loop.State == MatchState.Done && loop.Iterations.Count == 2, loop.Iterations.Count.ToString());
        Check("termination: a pass that did not help leaves the starting face alone", loop.BestIndex == 0 && loop.Status.Contains("nothing was changed"), loop.Status);

        // the Shooter on its own: a capture that never finishes is the rig's problem, but a failed one must surface
        var rig = new FakeRig(new World()) { CapturesAllowed = 0 };
        var shooter = new Shooter(rig, Reads(rig.W), () => 0);
        shooter.Start(null);
        for (var i = 0; i < 10 && !shooter.Finished; i++) { shooter.Tick(); Thread.Sleep(1); }
        Check("shooter: a capture that cannot start fails with the reason", shooter.State == ShotState.Failed && shooter.Error.Contains("out of captures"), shooter.Error);
    }

    // ---- 17.8 rating rule ----

    private void TrialChecks()
    {
        Check("trials: median of an odd count is the middle", ExpressionTrials.Median(new[] { 5, 1, 3 }) == 3f);
        Check("trials: median of an even count is the mean of the middle two", ExpressionTrials.Median(new[] { 1, 2, 4, 5 }) == 3f);
        Check("trials: no ratings has no median", ExpressionTrials.Median(Array.Empty<int>()) == null);

        ExpressionSettings With(params int[] r) => new() { Trials = r.Select(x => new ExpressionTrial { Rating = x }).ToList() };
        Check("trials: five ratings with a median of 2 make it experimental", ExpressionTrials.IsExperimental(With(1, 2, 2, 3, 5)));
        Check("trials: a median of exactly 3 ('recognisable') is not experimental", !ExpressionTrials.IsExperimental(With(1, 3, 3, 4, 5)));
        Check("trials: four low ratings are not enough to judge", !ExpressionTrials.IsExperimental(With(1, 1, 1, 1)));
        Check("trials: out-of-range ratings are ignored", !ExpressionTrials.IsExperimental(With(1, 1, 1, 1, 9)) && ExpressionTrials.Median(new[] { 0, 9 }) == null);
        Check("trials: the tab text says how many are rated", ExpressionTrials.Describe(With(2, 3)).Contains("2 of 5") && ExpressionTrials.Describe(With(1, 2, 2, 3, 5)).StartsWith("Experimental"));
        var s = new ExpressionSettings();
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        var back = System.Text.Json.JsonSerializer.Deserialize<ExpressionSettings>(json)!;
        Check("settings: round trip through the config section", back.MaxIterations == 6 && back.Framing == "close-up" && Near(back.Epsilon, 0.002f));
    }
}

// Part 10: framing maths, clamps, the camera service through a fake reader, the turntable's angles and the overlay lines. No game needed.
using System.Numerics;
using System.Text.Json;
using Soundstage.CameraTools;
using Soundstage.GameAccess;

namespace Soundstage.SelfCheck;

public sealed class CameraChecks : CheckArea
{
    public override string Name => "Camera (Part 10)";
    public override int Order => 100;

    private sealed class FakeReader : IGameReader
    {
        public bool Gpose = true;
        public bool FailWrite;
        public CameraState Camera = new(new Vector3(0, 1.5f, 5), new Vector3(0, 1.6f, 0), 0.5f, 0.1f, 4f, 0.78f, 0f);
        public Vector3? Head = new Vector3(0, 1.7f, 0);
        public readonly List<CameraWrite> Writes = new();

        public bool InGpose() => Gpose;
        public Access<CameraState> ReadCamera() => Access<CameraState>.Success(Camera);

        public Access<bool> WriteCamera(CameraWrite w)
        {
            if (FailWrite) return Access<bool>.Fail("write refused");
            Writes.Add(w);
            Camera = Camera with
            {
                Yaw = w.Yaw ?? Camera.Yaw, Pitch = w.Pitch ?? Camera.Pitch, Distance = w.Distance ?? Camera.Distance,
                AddedFov = w.AddedFov ?? Camera.AddedFov, Position = w.Position ?? Camera.Position,
            };
            return Access<bool>.Success(true);
        }

        public Access<Vector3> ReadHeadPosition() => Head is { } h ? Access<Vector3>.Success(h) : Access<Vector3>.Fail("no head");
        public Access<Vector3> ReadPlayerPosition() => Access<Vector3>.Success(Vector3.Zero);
    }

    public override void Run()
    {
        Maths();
        Service();
        TurntableChecks();
        Overlay();
        SceneBlock();
    }

    private void Maths()
    {
        Check("six framing presets exist", Framing.Presets.Count == 6 && new[] { "full-body", "three-quarter", "waist-up", "chest-up", "portrait", "close-up" }.All(i => Framing.Find(i) != null));
        Check("distances get smaller from full body to close-up", Framing.Presets.Zip(Framing.Presets.Skip(1), (a, b) => a.Distance > b.Distance).All(x => x));
        Check("every preset distance is inside the clamp", Framing.Presets.All(p => p.Distance >= CameraService.MinDistance && p.Distance <= CameraService.MaxDistance));
        Check("shipped Scene words map onto presets", Framing.Find("close-portrait")?.Id == "portrait" && Framing.Find("three-quarter-waist-up")?.Id == "waist-up"
            && Framing.Find("side-on")?.Id == "three-quarter" && Framing.Find("full-body")?.Id == "full-body" && Framing.Find("nonsense") == null);

        var p = Framing.Find("chest-up")!;
        var front = Framing.Solve(p, 0, 0, 0f, 0f);
        Check("azimuth 0 puts the lens on the subject's front (yaw = facing)", Near(front.Yaw, 0f));
        var faced = Framing.Solve(p, 0, 0, 1.0f, 0f);
        Check("yaw follows the subject's facing", Near(faced.Yaw, 1.0f));
        var right = Framing.Solve(p, 90, 0, 0f, 0f);
        Check("azimuth +90 is a quarter turn to the subject's right", Near(right.Yaw, MathF.PI / 2f));
        var wrap = Framing.Solve(p, 90, 0, 3.0f, 0f);
        Check("yaw wraps into -pi..pi", wrap.Yaw >= -MathF.PI && wrap.Yaw <= MathF.PI && Near(wrap.Yaw, 3.0f + MathF.PI / 2f - MathF.Tau));
        var trimmed = Framing.Solve(p, 0, 0, 0f, 0f, yawTrimDeg: 180f);
        Check("yaw trim shifts the yaw", Near(MathF.Abs(trimmed.Yaw), MathF.PI));

        var level = Framing.Solve(Framing.Find("portrait")!, 0, 0, 0f, 0f);
        Check("portrait with the head at the pivot is level", Near(level.Pitch, 0f));
        var low = Framing.Solve(Framing.Find("full-body")!, 0, 0, 0f, 0f);
        Check("a lens below the head gives a negative pitch (with sign +1)", low.Pitch < 0f && Near(low.Pitch, MathF.Asin(-0.6f / 6f)));
        var flipped = Framing.Solve(Framing.Find("full-body")!, 0, 0, 0f, 0f, pitchSign: -1);
        Check("pitch sign -1 flips it", Near(flipped.Pitch, -low.Pitch));
        var extreme = Framing.Solve(Framing.Find("close-up")!, 0, 50f, 0f, 0f);
        Check("pitch never passes the limit", MathF.Abs(extreme.Pitch) <= Framing.MaxPitchRadians + 1e-6f);

        Check("side words give azimuths", Framing.AzimuthFor(new SceneCamera { Side = "right" }) == 90f && Framing.AzimuthFor(new SceneCamera { Side = "left" }) == -90f
            && Framing.AzimuthFor(new SceneCamera { Side = "front" }) == 0f && Framing.AzimuthFor(new SceneCamera { Side = "right", AzimuthDeg = 35 }) == 35f);
        Check("height words lower the lens", Framing.HeightTrim("eye") == 0f && Framing.HeightTrim("chest") < 0f && Framing.HeightTrim("low") < Framing.HeightTrim("chest"));
    }

    private void Service()
    {
        var logs = new List<string>();
        var fake = new FakeReader();
        var svc = new CameraService(new GuardedAccess(fake, logs.Add), () => 0f, logs.Add);

        Check("clamp control: 40 m becomes 12 m", CameraService.ClampDistance(40f) == 12f && CameraService.ClampDistance(0.01f) == 0.3f && CameraService.ClampDistance(float.NaN) == 0.3f);
        var ok = svc.Place(new CameraPose(0.2f, 0.1f, 40f, 0f), out var why);
        var clampLines = logs.Count(l => l.Contains("[Soundstage/CAM]") && l.Contains("clamped"));
        Check("a 40 m request writes 12 m and logs one clamp line", ok && fake.Writes.Count == 1 && fake.Writes[0].Distance == 12f && clampLines == 1, why + " lines=" + clampLines);
        svc.Place(new CameraPose(0f, 5f, 2f, 3f), out _);
        Check("pitch and FOV are clamped on the way out", fake.Writes[^1].Pitch <= Framing.MaxPitchRadians + 1e-6f && fake.Writes[^1].AddedFov == 0.5f);

        var ok2 = svc.ApplyFraming("portrait", 0, 0, 0, out why);
        Check("ApplyFraming writes yaw, pitch, distance and FOV", ok2 && fake.Writes[^1].Distance == 1.4f && fake.Writes[^1].Yaw != null && fake.Writes[^1].Pitch != null && fake.Writes[^1].AddedFov != null, why);
        Check("ApplyFraming logs one [Soundstage/CAM] framing line", logs.Count(l => l.Contains("[Soundstage/CAM] framing portrait")) == 1);
        Check("head height feeds the pitch (head 0.1 above the pivot lifts the lens)", fake.Writes[^1].Pitch > 0f);

        svc.Settings.WriteFov = false;
        svc.ApplyFraming("close-up", 0, 0, 0, out _);
        Check("WriteFov off leaves the FOV slider alone", fake.Writes[^1].AddedFov == null);
        svc.Settings.WriteFov = true;

        Check("an unknown framing is refused", !svc.ApplyFraming("nope", 0, 0, 0, out var r1) && r1.Contains("nope"));

        var before = fake.Writes.Count;
        fake.Gpose = false;
        Check("never writes outside GPose", !svc.ApplyFraming("portrait", 0, 0, 0, out var r2) && fake.Writes.Count == before && r2.Contains("GPose"), r2);
        Check("Place also refuses outside GPose", !svc.Place(new CameraPose(0, 0, 2, 0), out _) && fake.Writes.Count == before);
        fake.Gpose = true;

        fake.Head = null;
        Check("framing still works when the head cannot be read", svc.ApplyFraming("chest-up", 0, 0, 0, out _));
        fake.Head = new Vector3(0, 9f, 0);
        svc.ApplyFraming("chest-up", 0, 0, 0, out _);
        Check("a head 7 m off the pivot is ignored as bad data", Near(fake.Writes[^1].Pitch ?? 9f, MathF.Asin(-0.1f / 2f)));

        var fresh = new FakeReader();
        var svc2 = new CameraService(new GuardedAccess(fresh, _ => { }), () => 1f, _ => { });
        var original = fresh.Camera;
        svc2.ApplyFraming("close-up", 90, 0, 0, out _);
        Check("Restore puts yaw, pitch, distance and FOV back", svc2.Restore(out _) && Near(fresh.Camera.Yaw, original.Yaw) && Near(fresh.Camera.Pitch, original.Pitch)
            && Near(fresh.Camera.Distance, original.Distance) && Near(fresh.Camera.AddedFov, original.AddedFov));

        var failing = new FakeReader { FailWrite = true };
        var svc3 = new CameraService(new GuardedAccess(failing, _ => { }), () => 0f, _ => { });
        Check("a refused write comes back as a plain reason, not an exception", !svc3.ApplyFraming("portrait", 0, 0, 0, out var r3) && r3.Length > 0 && svc3.LastMessage == r3);
    }

    private void TurntableChecks()
    {
        Check("turntable: start of the lap is the start yaw", Near(TurntableMath.YawAt(0.4f, 0f, 12f), 0.4f));
        Check("turntable: a quarter lap is 90 degrees", Near(TurntableMath.YawAt(0f, 3f, 12f), MathF.PI / 2f));
        Check("turntable: a half lap is 180 degrees", Near(MathF.Abs(TurntableMath.YawAt(0f, 6f, 12f)), MathF.PI));
        Check("turntable: a full lap comes back to the start", Near(TurntableMath.YawAt(0.4f, 12f, 12f), 0.4f, 1e-3f));
        Check("turntable: the other direction goes the other way", Near(TurntableMath.YawAt(0f, 3f, 12f, -1), -MathF.PI / 2f));
        Check("turntable: yaw always sits inside -pi..pi", Enumerable.Range(0, 400).All(i => MathF.Abs(TurntableMath.YawAt(2f, i * 0.1f, 12f)) <= MathF.PI + 1e-5f));

        var fake = new FakeReader();
        var logs = new List<string>();
        var svc = new CameraService(new GuardedAccess(fake, logs.Add), () => 0f, logs.Add);
        var t = new Turntable(svc);
        Check("turntable starts in GPose", t.Start(12f, 1, out _) && t.Running);
        for (var i = 0; i < 360; i++) t.Tick(1f / 60f);   // 6 seconds of a 12 second lap
        Check("after 6 s of frames the camera is half a lap round", Near(MathF.Abs(fake.Camera.Yaw - 0.5f), MathF.PI, 0.02f) || Near(MathF.Abs(Framing.WrapPi(fake.Camera.Yaw - 0.5f)), MathF.PI, 0.02f));
        Check("the turntable writes yaw only", fake.Writes.All(w => w.Pitch == null && w.Distance == null && w.AddedFov == null));
        t.Stop();
        var writes = fake.Writes.Count;
        t.Tick(1f / 60f);
        Check("Stop stops: no more writes", !t.Running && fake.Writes.Count == writes);

        t.Start(12f, 1, out _);
        for (var i = 0; i < 30; i++) t.Tick(1f / 60f);
        t.StopAndRestore();
        Check("StopAndRestore puts the camera back", !t.Running && Near(fake.Camera.Yaw, 0.5f));

        t.Start(12f, 1, out _);
        fake.Gpose = false;
        var more = fake.Writes.Count;
        var stillRunning = t.Tick(1f / 60f);
        Check("leaving GPose cancels the turntable", !stillRunning && !t.Running && fake.Writes.Count == more && t.Status.Contains("GPose"));
        fake.Gpose = true;

        fake.Gpose = false;
        Check("turntable refuses to start outside GPose", !t.Start(12f, 1, out var why) && !t.Running && why.Length > 0);
    }

    private void Overlay()
    {
        var s = new CameraSettings();
        Check("overlay: nothing on, no lines", ThirdsGeometry.Build(1920, 1080, s).Count == 0);
        s.Thirds = true;
        var th = ThirdsGeometry.Build(1920, 1080, s);
        Check("thirds: four lines at one third and two thirds", th.Count == 4 && th.Any(l => Near(l.X1, 640f, 0.01f) && l.Y1 == 0 && Near(l.Y2, 1080f)) && th.Any(l => Near(l.Y1, 720f, 0.01f)));
        s.Golden = true;
        var both = ThirdsGeometry.Build(1920, 1080, s);
        Check("golden ratio adds four lines at 0.382 and 0.618", both.Count == 8 && both.Any(l => l.Kind == OverlayKind.Golden && Near(l.X1, 1920f * 0.381966f, 0.5f)) && both.Any(l => l.Kind == OverlayKind.Golden && Near(l.Y1, 1080f * 0.618034f, 0.5f)));
        s.CentreCross = true;
        s.SafeMargin = true;
        s.SafeMarginPercent = 10f;
        var all = ThirdsGeometry.Build(1920, 1080, s);
        Check("centre cross adds two lines through the middle", all.Count(l => l.Kind == OverlayKind.Cross) == 2 && all.Any(l => l.Kind == OverlayKind.Cross && Near(l.X1, 960f)));
        var margin = all.Where(l => l.Kind == OverlayKind.Margin).ToList();
        Check("safe margin is a closed box inset by the percentage", margin.Count == 4 && margin.Min(l => Math.Min(l.X1, l.X2)) is var minX && Near(minX, 192f, 0.01f) && Near(margin.Max(l => Math.Max(l.Y1, l.Y2)), 972f, 0.01f));
        s.SafeMarginPercent = 90f;
        Check("safe margin percentage is capped at 25", ThirdsGeometry.Build(1000, 1000, s).Where(l => l.Kind == OverlayKind.Margin).Min(l => Math.Min(l.X1, l.X2)) <= 250.01f);
        Check("zero-size screen draws nothing", ThirdsGeometry.Build(0, 0, s).Count == 0);
    }

    private void SceneBlock()
    {
        Check("the camera block is marked applied", SceneStore.AppliedBlocks.Contains("camera"));
        var cam = new SceneCamera { Framing = "chest-up", Height = "chest", Side = "right", AzimuthDeg = 30, OffsetY = 0.1f, FovAdd = 0.05f };
        var json = JsonSerializer.Serialize(cam, SceneStore.WriteOptions);
        var back = JsonSerializer.Deserialize<SceneCamera>(json, SceneStore.ReadOptions)!;
        Check("new camera fields round-trip", back.AzimuthDeg == 30 && back.OffsetY == 0.1f && back.FovAdd == 0.05f && back.Framing == "chest-up");
        var plain = JsonSerializer.Serialize(new SceneCamera { Framing = "portrait", Side = "front" }, SceneStore.WriteOptions);
        Check("an old camera block writes no new fields", !plain.Contains("azimuth", StringComparison.OrdinalIgnoreCase) && !plain.Contains("fovAdd", StringComparison.OrdinalIgnoreCase));

        var fake = new FakeReader();
        var svc = new CameraService(new GuardedAccess(fake, _ => { }), () => 0f, _ => { });
        Check("ApplyScene writes the camera when Apply camera is on", svc.ApplyScene(back, out _) && fake.Writes.Count == 1);
        svc.Settings.ApplyCamera = false;
        Check("ApplyScene does nothing when Apply camera is off", !svc.ApplyScene(back, out _) && fake.Writes.Count == 1);
        svc.Settings.ApplyCamera = true;
        Check("ApplyScene refuses a block with no framing", !svc.ApplyScene(new SceneCamera(), out _));

        var shipped = Path.Combine(root, "scenes.json");
        if (File.Exists(shipped))
        {
            var scenes = SceneStore.LoadFile(shipped, _ => { });
            var withCam = scenes.Where(s => s.Camera != null).ToList();
            Check("every shipped Scene's framing word maps to a preset", withCam.Count > 0 && withCam.All(s => Framing.Find(s.Camera!.Framing) != null), string.Join(",", withCam.Where(s => Framing.Find(s.Camera!.Framing) == null).Select(s => s.Camera!.Framing).Distinct()));
        }
    }
}

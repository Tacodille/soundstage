// Part 2: the game-memory wrappers, driven by a fake reader. No game needed.
using System.Numerics;
using Soundstage.GameAccess;

namespace Soundstage.SelfCheck;

public sealed class GameAccessChecks : CheckArea
{
    public override string Name => "Game access (Part 2)";
    public override int Order => 20;

    private sealed class FakeReader : IGameReader
    {
        public bool Gpose = true;
        public bool ThrowOnCamera, ThrowOnHead, ThrowOnGpose, FailHead;
        public CameraState Camera = new(new Vector3(1, 2, 3), new Vector3(0, 1, 0), 0.5f, -0.3f, 6f, 0.78f, 0f);
        public readonly List<CameraWrite> Writes = new();
        public int ReadCount;

        public bool InGpose() => ThrowOnGpose ? throw new InvalidOperationException("gpose boom") : Gpose;

        public Access<CameraState> ReadCamera()
        {
            ReadCount++;
            if (ThrowOnCamera) throw new AccessViolationException("camera boom");
            return Access<CameraState>.Success(Camera);
        }

        public Access<bool> WriteCamera(CameraWrite w)
        {
            if (ThrowOnCamera) throw new AccessViolationException("camera boom");
            Writes.Add(w);
            Camera = Camera with
            {
                Yaw = w.Yaw ?? Camera.Yaw, Pitch = w.Pitch ?? Camera.Pitch, Distance = w.Distance ?? Camera.Distance,
                AddedFov = w.AddedFov ?? Camera.AddedFov, Position = w.Position ?? Camera.Position,
            };
            return Access<bool>.Success(true);
        }

        public Access<Vector3> ReadHeadPosition()
        {
            if (ThrowOnHead) throw new NullReferenceException("head boom");
            return FailHead ? Access<Vector3>.Fail("no j_kao") : Access<Vector3>.Success(new Vector3(1, 1.7f, 1));
        }

        public Access<Vector3> ReadPlayerPosition() => Access<Vector3>.Success(new Vector3(1, 0, 1));
    }

    public override void Run()
    {
        // ---- refuses outside GPose, touches nothing ----
        {
            var fake = new FakeReader { Gpose = false };
            var logs = new List<string>();
            var g = new GuardedAccess(fake, logs.Add);
            var read = g.ReadCamera();
            var write = g.WriteCamera(new CameraWrite(Yaw: 1f));
            Check("outside GPose: camera read refuses with the plain message", !read.Ok && read.Reason == GuardedAccess.NotInGpose);
            Check("outside GPose: camera write refuses and the reader is never asked", !write.Ok && fake.Writes.Count == 0 && fake.ReadCount == 0 && !g.HasBaseline);
        }

        // ---- exceptions become "unavailable", logged once ----
        {
            var fake = new FakeReader { ThrowOnCamera = true, ThrowOnHead = true };
            var logs = new List<string>();
            var g = new GuardedAccess(fake, logs.Add);
            Access<CameraState> r1 = default, r2 = default;
            var threw = false;
            try { r1 = g.ReadCamera(); r2 = g.ReadCamera(); g.ReadHead(); g.ReadHead(); } catch { threw = true; }
            Check("a throwing reader never throws out of the wrapper", !threw && !r1.Ok && !r2.Ok && r1.Reason.Contains("camera boom"));
            Check("the same failure is logged once per session, with the probe prefix", logs.Count(l => l.Contains("camera read")) == 1 && logs.Count(l => l.Contains("head read")) == 1 && logs.All(l => l.StartsWith(GuardedAccess.LogPrefix)),
                string.Join(" | ", logs));
            var w = g.WriteCamera(new CameraWrite(Yaw: 1f));
            Check("a write whose pre-read fails writes nothing and says so", !w.Ok && w.Reason.Contains("nothing was written") && fake.Writes.Count == 0 && !g.HasBaseline);
        }
        {
            var fake = new FakeReader { ThrowOnGpose = true };
            var g = new GuardedAccess(fake, _ => { });
            Check("a throwing GPose check counts as not in GPose", !g.InGpose() && !g.ReadCamera().Ok);
        }
        {
            var fake = new FakeReader { FailHead = true };
            var g = new GuardedAccess(fake, _ => { });
            var h = g.ReadHead();
            Check("a head read that fails returns the reason, not a position", !h.Ok && h.Reason == "no j_kao");
        }

        // ---- write, restore, leave GPose ----
        {
            var fake = new FakeReader();
            var original = fake.Camera;
            var g = new GuardedAccess(fake, _ => { });
            Check("an empty write is refused", !g.WriteCamera(default).Ok && fake.Writes.Count == 0);
            var w1 = g.WriteCamera(new CameraWrite(Yaw: OrbitMath.OrbitedYaw(original.Yaw, 30f)));
            var w2 = g.WriteCamera(new CameraWrite(AddedFov: 0.2f));
            Check("a write goes through and the first state is remembered once", w1.Ok && w2.Ok && g.HasBaseline && fake.Writes.Count == 2 && !Near(fake.Camera.Yaw, original.Yaw));
            var restored = g.Restore();
            Check("restore puts every written field back and drops the baseline", restored.Ok && !g.HasBaseline && Near(fake.Camera.Yaw, original.Yaw) && Near(fake.Camera.AddedFov, original.AddedFov)
                && Near(fake.Camera.Distance, original.Distance) && NearV(fake.Camera.Position, original.Position));
            Check("restore with nothing to restore says so", !g.Restore().Ok);
        }
        {
            var fake = new FakeReader();
            var g = new GuardedAccess(fake, _ => { });
            g.WriteCamera(new CameraWrite(Distance: 3f));
            fake.Gpose = false;
            g.OnFrame();
            Check("leaving GPose drops the baseline and a later restore does not write", !g.HasBaseline && !g.Restore().Ok && fake.Writes.Count == 1);
            fake.Gpose = true;
            g.WriteCamera(new CameraWrite(Distance: 3f));
            fake.Gpose = false;
            var r = g.Restore();
            Check("a restore after the game already left GPose succeeds without writing", r.Ok && fake.Writes.Count == 2 && !g.HasBaseline);
        }

        // ---- angle maths ----
        Check("orbit +30 degrees from 170 wraps to -160", Near(OrbitMath.OrbitedYaw(170f * Rad, 30f), -160f * Rad, 1e-4f));
        Check("orbit -30 degrees from -170 wraps to 160", Near(OrbitMath.OrbitedYaw(-170f * Rad, -30f), 160f * Rad, 1e-4f));
        Check("orbit +30 degrees from 0 is 30 degrees", Near(OrbitMath.OrbitedYaw(0f, 30f), 30f * Rad, 1e-5f));
        Check("GPose FOV clamps to -0.5 and 0.5", OrbitMath.ClampAddedFov(0.9f) == 0.5f && OrbitMath.ClampAddedFov(-2f) == -0.5f && OrbitMath.ClampAddedFov(0.1f) == 0.1f);

        // ---- Brio route B (synthetic JSON, shaped like a Brio pose file) ----
        {
            const string withPos = "{\"Version\":\"1\",\"Bones\":{\"n_root\":{\"Position\":\"0, 0, 0\",\"Rotation\":\"0, 0, 0, 1\"},\"j_kao\":{\"Position\":\"0, 0.1, 0\",\"Rotation\":\"0, 0, 0, 1\"}}}";
            const string noPos = "{\"Bones\":{\"j_kao\":{\"Rotation\":\"0, 0, 0, 1\"}}}";
            const string noHead = "{\"Bones\":{\"n_root\":{}}}";
            var a = BrioHeadRoute.Inspect(withPos);
            var b = BrioHeadRoute.Inspect(noPos);
            var c = BrioHeadRoute.Inspect(noHead);
            Check("route B finds j_kao and its Position field", a.JsonValid && a.BoneCount == 2 && a.HasHead && a.HasPosition && a.HeadEntry.Contains("0.1"));
            Check("route B tells a head with no Position from one with", b.HasHead && !b.HasPosition);
            Check("route B reports a pose with no head bone", c.JsonValid && !c.HasHead && c.BoneCount == 1);
            Check("route B survives null, empty and broken JSON", !BrioHeadRoute.Inspect(null).JsonValid && !BrioHeadRoute.Inspect("").JsonValid && !BrioHeadRoute.Inspect("{\"Bones\":{").JsonValid);
        }
    }
}

using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Soundstage.GameAccess;

namespace Soundstage.Tabs;

/// <summary>
/// Dev-only (after /soundstage dev). Buttons for the Part 2 proof: read the head, read the camera, write an orbit, a position
/// nudge and a FOV nudge, restore. Every call is guarded and the camera calls refuse outside GPose. Each action writes one
/// "[Soundstage/PROBE]" line, which is the evidence the proof document is filled from.
/// </summary>
public sealed class ProbeTab : ITab
{
    public string Name => "Probe";
    public int Order => 95;
    public bool IsVisible(Studio studio) => studio.Config.DevMode;

    private const string P = GuardedAccess.LogPrefix;
    private const int PositionHoldFrames = 30;

    private string status = "Nothing pressed yet.";
    private string headLine = "", routeBLine = "", cameraLine = "", typesLine = "";
    private bool live;
    private float orbitDegrees = 30f;
    private float pitchDegrees = 15f;
    private float fovNudge = 0.2f;
    private float liftMetres = 0.5f;

    // A position write is checked PositionHoldFrames frames later: did the game keep it, or put the camera back?
    private Vector3? pendingTarget;
    private int pendingFrames;

    public void Draw(Studio studio)
    {
        GameAccessHost.Use(studio.Log);
        var guard = GameAccessHost.Guard;
        guard.OnFrame();
        var inGpose = guard.InGpose();

        ImGui.TextWrapped("Part 2 proof. Head reads work anywhere. Camera reads and writes only work in GPose and refuse outside it. Restore puts every field back.");
        ImGui.TextUnformatted(inGpose ? "In GPose: yes" : "In GPose: no");
        ImGui.Checkbox("Live read-outs", ref live);
        ImGui.Separator();

        // ---- Head ----
        ImGui.TextUnformatted("Head");
        var readHead = ImGui.Button("Read head (route A: skeleton)");
        if (readHead || (live && headLine.Length > 0)) ReadHead(studio, guard, pressed: readHead);
        ImGui.SameLine();
        if (ImGui.Button("Check route B (Brio pose JSON)")) CheckRouteB(studio);
        if (headLine.Length > 0) ImGui.TextWrapped(headLine);
        if (routeBLine.Length > 0) ImGui.TextWrapped(routeBLine);
        ImGui.Separator();

        // ---- Camera ----
        ImGui.TextUnformatted("GPose camera");
        var readCam = ImGui.Button("Read camera");
        if (readCam || (live && inGpose && cameraLine.Length > 0)) ReadCamera(guard, pressed: readCam);
        if (cameraLine.Length > 0) ImGui.TextWrapped(cameraLine);

        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("Orbit (degrees)##orbit", ref orbitDegrees, -90f, 90f);
        if (ImGui.Button("Write orbit")) Orbit(guard);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("Pitch (degrees)##pitch", ref pitchDegrees, -30f, 30f);
        if (ImGui.Button("Write pitch")) Pitch(guard);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("FOV nudge##fov", ref fovNudge, -0.5f, 0.5f);
        if (ImGui.Button("Write FOV nudge")) FovNudge(guard);
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("Lift (m)##lift", ref liftMetres, -1f, 1f);
        if (ImGui.Button("Write position (lift)")) PositionNudge(guard);
        ImGui.SameLine();
        if (ImGui.Button("Restore")) Restore(guard);
        TickPendingPosition(guard);

        ImGui.TextWrapped(status);
        ImGui.Separator();

        // ---- Installed types ----
        if (ImGui.Button("List the types and fields this proof uses")) typesLine = ListTypes(studio);
        if (typesLine.Length > 0) ImGui.TextWrapped(typesLine);
    }

    // ---- Head ----

    private void ReadHead(Studio studio, GuardedAccess guard, bool pressed)
    {
        var head = guard.ReadHead();
        var player = guard.ReadPlayer();
        if (head.Ok && player.Ok)
        {
            var above = head.Value.Y - player.Value.Y;
            headLine = $"Head {Fmt(head.Value)}; player {Fmt(player.Value)}; head is {above:F2} m above the player position.";
        }
        else
        {
            headLine = "Head unavailable: " + head.Reason;
        }
        if (pressed) studio.Log.Information("{P} READ head ok={Ok} {Line}", P, head.Ok, headLine);
        if (pressed && head.Ok) studio.Log.Information("{P} READ skeleton {S}", P, GameAccessHost.Reader.DescribeSkeleton());
    }

    private void CheckRouteB(Studio studio)
    {
        var player = studio.Objects?.LocalPlayer;
        if (player == null) { routeBLine = "Route B: no local player."; studio.Log.Information("{P} ROUTEB {L}", P, routeBLine); return; }
        if (!studio.Brio.Available) { studio.Brio.Refresh(); }
        if (!studio.Brio.Available) { routeBLine = "Route B: Brio is not available (" + studio.Brio.LastError + ")."; studio.Log.Information("{P} ROUTEB {L}", P, routeBLine); return; }
        var f = BrioHeadRoute.Inspect(studio.Brio.GetPoseJson(player));
        routeBLine = !f.JsonValid
            ? "Route B: Brio returned no readable pose JSON."
            : $"Route B: pose JSON has {f.BoneCount} bones; {BrioHeadRoute.HeadBone} {(f.HasHead ? "present" : "absent")}; Position field {(f.HasPosition ? "present" : "absent")}. Entry: {f.HeadEntry} (a Brio pose stores parent-relative transforms, so this is a local value, not a world position).";
        studio.Log.Information("{P} ROUTEB {L}", P, routeBLine);
    }

    // ---- Camera ----

    private void ReadCamera(GuardedAccess guard, bool pressed)
    {
        var r = guard.ReadCamera();
        cameraLine = r.Ok
            ? $"Position {Fmt(r.Value.Position)}; look-at {Fmt(r.Value.LookAt)}; yaw {Deg(r.Value.Yaw):F1} deg; pitch {Deg(r.Value.Pitch):F1} deg; distance {r.Value.Distance:F2}; FOV {r.Value.Fov:F3}; GPose FOV slider {r.Value.AddedFov:F3}"
            : "Camera unavailable: " + r.Reason;
        if (pressed) GameAccessHost.Log($"{P} READ camera ok={r.Ok} {cameraLine}");
    }

    private void Orbit(GuardedAccess guard)
    {
        var before = guard.ReadCamera();
        if (!before.Ok) { Say("Write orbit refused: " + before.Reason, "WRITE orbit refused"); return; }
        var target = OrbitMath.OrbitedYaw(before.Value.Yaw, orbitDegrees);
        var w = guard.WriteCamera(new CameraWrite(Yaw: target));
        Say(w.Ok ? $"Wrote an orbit of {orbitDegrees:F0} degrees (yaw {Deg(before.Value.Yaw):F1} to {Deg(target):F1}). The frame should swing; you can still move the camera by hand." : "Write orbit refused: " + w.Reason,
            $"WRITE orbit degrees={orbitDegrees:F0} ok={w.Ok} yawBefore={Deg(before.Value.Yaw):F1} yawTarget={Deg(target):F1} reason={w.Reason}");
    }

    private void Pitch(GuardedAccess guard)
    {
        var before = guard.ReadCamera();
        if (!before.Ok) { Say("Write pitch refused: " + before.Reason, "WRITE pitch refused"); return; }
        var target = before.Value.Pitch + pitchDegrees * MathF.PI / 180f;
        var w = guard.WriteCamera(new CameraWrite(Pitch: target));
        Say(w.Ok ? $"Wrote pitch {Deg(before.Value.Pitch):F1} to {Deg(target):F1} degrees (the game clamps it to its own limits). The camera should tilt." : "Write pitch refused: " + w.Reason,
            $"WRITE pitch degrees={pitchDegrees:F0} ok={w.Ok} before={Deg(before.Value.Pitch):F1} target={Deg(target):F1} reason={w.Reason}");
    }

    private void FovNudge(GuardedAccess guard)
    {
        var before = guard.ReadCamera();
        if (!before.Ok) { Say("Write FOV refused: " + before.Reason, "WRITE fov refused"); return; }
        var target = OrbitMath.ClampAddedFov(before.Value.AddedFov + fovNudge);
        var w = guard.WriteCamera(new CameraWrite(AddedFov: target));
        Say(w.Ok ? $"Wrote GPose FOV {before.Value.AddedFov:F3} to {target:F3}. The picture should zoom a little." : "Write FOV refused: " + w.Reason,
            $"WRITE fov ok={w.Ok} before={before.Value.AddedFov:F3} target={target:F3} reason={w.Reason}");
    }

    private void PositionNudge(GuardedAccess guard)
    {
        var before = guard.ReadCamera();
        if (!before.Ok) { Say("Write position refused: " + before.Reason, "WRITE position refused"); return; }
        var target = before.Value.Position + new Vector3(0, liftMetres, 0);
        var w = guard.WriteCamera(new CameraWrite(Position: target));
        if (w.Ok) { pendingTarget = target; pendingFrames = 0; }
        Say(w.Ok ? $"Wrote camera position {Fmt(before.Value.Position)} to {Fmt(target)}. Checking in {PositionHoldFrames} frames whether the game kept it." : "Write position refused: " + w.Reason,
            $"WRITE position ok={w.Ok} before={Fmt(before.Value.Position)} target={Fmt(target)} reason={w.Reason}");
    }

    private void TickPendingPosition(GuardedAccess guard)
    {
        if (pendingTarget is not { } target) return;
        if (++pendingFrames < PositionHoldFrames) return;
        pendingTarget = null;
        var now = guard.ReadCamera();
        if (!now.Ok) { Say("Position check: " + now.Reason, "POSITIONCHECK unavailable"); return; }
        var held = Vector3.Distance(now.Value.Position, target) < 0.05f;
        Say(held ? "The game kept the written position." : $"The game put the camera back (now {Fmt(now.Value.Position)}). Orbit and distance are the writes that stick.",
            $"POSITIONCHECK held={held} target={Fmt(target)} now={Fmt(now.Value.Position)}");
    }

    private void Restore(GuardedAccess guard)
    {
        pendingTarget = null;
        var r = guard.Restore();
        Say(r.Ok ? "Restored the camera to what it was before the first write." : "Restore: " + r.Reason, $"RESTORE ok={r.Ok} reason={r.Reason}");
    }

    // ---- Installed types ----

    private static string ListTypes(Studio studio)
    {
        var asm = typeof(FFXIVClientStructs.FFXIV.Client.Game.Camera).Assembly;
        var lines = new List<string> { $"FFXIVClientStructs {asm.GetName().Version}" };
        void Check(Type t, params string[] members)
        {
            var found = members.Select(m => $"{m}={(t.GetField(m, BindingFlags.Public | BindingFlags.Instance) != null || t.GetProperty(m) != null || t.GetMethod(m) != null ? "yes" : "NO")}");
            lines.Add($"{t.Name}: {string.Join(", ", found)}");
        }
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Game.Camera), "DirH", "DirV", "Distance", "FoV", "MinDistance", "MaxDistance", "DirVMin", "DirVMax");
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager), "Instance", "GetActiveCamera", "Camera");
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Game.GameMain), "IsInGPose");
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase), "Skeleton");
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Graphics.Render.PartialSkeleton), "GetHavokPose");
        Check(typeof(FFXIVClientStructs.FFXIV.Client.Game.Control.Control), "LocalPlayer");
        lines.Add($"Raw offset used for the GPose FOV slider: 0x{ClientStructsReader.AddedFovOffset:X} (not named in the installed Camera struct)");
        var text = string.Join(" | ", lines);
        studio.Log.Information("{P} TYPES {T}", P, text);
        return text;
    }

    // ---- Helpers ----

    private void Say(string message, string logLine)
    {
        status = message;
        GameAccessHost.Log($"{P} {logLine}");
    }

    private static string Fmt(Vector3 v) => $"({v.X:F2}, {v.Y:F2}, {v.Z:F2})";
    private static float Deg(float rad) => rad * 180f / MathF.PI;
}

using Dalamud.Bindings.ImGui;
using Soundstage.CameraTools;

namespace Soundstage.Tabs;

/// <summary>Framing buttons, azimuth and FOV sliders, the composition overlays, the turntable and "Save into Scene".</summary>
public sealed class CameraTab : ITab
{
    public string Name => "Camera";
    public int Order => 40;

    private string saveNote = "";

    public void Draw(Studio studio)
    {
        var host = studio.Camera;
        if (host == null) { ImGui.TextDisabled("The camera service did not start. See the log."); return; }
        var s = host.Settings;
        var svc = host.Service;
        var changed = false;

        var canWrite = svc.CanWrite(out var reason);
        DrawFraming(studio, host, s, canWrite, reason, ref changed);
        ImGui.Separator();
        DrawTurntable(host, s, canWrite, reason, ref changed);
        ImGui.Separator();
        DrawOverlays(s, ref changed);
        ImGui.Separator();
        DrawAdvanced(s, ref changed);

        if (changed) host.Persist();
    }

    private void DrawFraming(Studio studio, CameraHost host, CameraSettings s, bool canWrite, string reason, ref bool changed)
    {
        ImGui.TextUnformatted("Framing");
        if (!canWrite) ImGui.TextDisabled(reason + " The buttons below wake up in GPose.");

        ImGui.BeginDisabled(!canWrite);
        foreach (var p in Framing.Presets)
        {
            var selected = s.Preset == p.Id;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
            if (ImGui.Button(p.Label + "##f" + p.Id))
            {
                s.Preset = p.Id;
                host.Service.ApplyFraming(p.Id, s.AzimuthDeg, s.OffsetY, s.AddedFov, out _);
                changed = true;
            }
            if (selected) ImGui.PopStyleColor();
            ImGui.SameLine();
        }
        ImGui.NewLine();

        var az = s.AzimuthDeg;
        var live = false;
        if (ImGui.SliderFloat("Angle round the subject (deg, + is their right)", ref az, -180f, 180f)) { s.AzimuthDeg = az; changed = true; live = true; }
        var oy = s.OffsetY;
        if (ImGui.SliderFloat("Camera height trim (m)", ref oy, -1f, 1f)) { s.OffsetY = oy; changed = true; live = true; }
        ImGui.BeginDisabled(!s.WriteFov);
        var fv = s.AddedFov;
        if (ImGui.SliderFloat("FOV added", ref fv, -0.5f, 0.5f)) { s.AddedFov = fv; changed = true; live = true; }
        ImGui.EndDisabled();
        if (live) host.Service.ApplyFraming(s.Preset, s.AzimuthDeg, s.OffsetY, s.AddedFov, out _);

        if (ImGui.Button("Put the camera back")) host.Service.Restore(out _);
        ImGui.EndDisabled();

        var apply = s.ApplyCamera;
        if (ImGui.Checkbox("Apply camera when a Scene is applied", ref apply)) { s.ApplyCamera = apply; changed = true; }
        var fovOn = s.WriteFov;
        if (ImGui.Checkbox("Write the FOV slider too", ref fovOn)) { s.WriteFov = fovOn; changed = true; }

        ImGui.BeginDisabled(studio.Current == null);
        if (ImGui.Button("Save into Scene"))
            saveNote = SaveIntoScene(studio, s);
        ImGui.EndDisabled();
        if (saveNote.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled(saveNote); }
        if (host.Service.LastMessage.Length > 0 && canWrite) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.6f, 0.4f, 1f), host.Service.LastMessage);
    }

    /// <summary>Copies the tab's framing into the working Scene's camera block. Saving the Scene file is the Scenes tab's job.</summary>
    private static string SaveIntoScene(Studio studio, CameraSettings s)
    {
        var scene = studio.Current;
        if (scene == null) return "Apply a Scene first.";
        scene.Camera = new SceneCamera
        {
            Framing = s.Preset,
            Height = "eye",
            Side = s.AzimuthDeg > 45 ? "right" : s.AzimuthDeg < -45 ? "left" : "front",
            AzimuthDeg = Math.Abs(s.AzimuthDeg) < 0.01f ? null : s.AzimuthDeg,
            OffsetY = Math.Abs(s.OffsetY) < 0.001f ? null : s.OffsetY,
            FovAdd = Math.Abs(s.AddedFov) < 0.001f ? null : s.AddedFov,
            Notes = scene.Camera?.Notes ?? "",
        };
        return "Saved into the working Scene. Save the Scene to keep it.";
    }

    private void DrawTurntable(CameraHost host, CameraSettings s, bool canWrite, string reason, ref bool changed)
    {
        ImGui.TextUnformatted("Turntable");
        var secs = s.TurntableSeconds;
        if (ImGui.SliderFloat("Seconds per lap", ref secs, 3f, 60f)) { s.TurntableSeconds = secs; changed = true; }
        var t = host.Turntable;
        ImGui.BeginDisabled(!canWrite);
        if (!t.Running)
        {
            if (ImGui.Button("Start (clockwise)")) t.Start(s.TurntableSeconds, 1, out _);
            ImGui.SameLine();
            if (ImGui.Button("Start (other way)")) t.Start(s.TurntableSeconds, -1, out _);
        }
        else
        {
            if (ImGui.Button("Stop")) t.Stop();
            ImGui.SameLine();
            if (ImGui.Button("Stop and put the camera back")) t.StopAndRestore();
        }
        ImGui.EndDisabled();
        if (!canWrite) ImGui.TextDisabled(reason);
        else if (t.Status.Length > 0) ImGui.TextDisabled(t.Status);
    }

    private static void DrawOverlays(CameraSettings s, ref bool changed)
    {
        ImGui.TextUnformatted("Composition overlay (hides with the game UI, so Scroll Lock gives a clean shot)");
        changed |= Toggle("Thirds", s.Thirds, v => s.Thirds = v);
        ImGui.SameLine();
        changed |= Toggle("Golden ratio", s.Golden, v => s.Golden = v);
        ImGui.SameLine();
        changed |= Toggle("Centre cross", s.CentreCross, v => s.CentreCross = v);
        changed |= Toggle("Safe margin", s.SafeMargin, v => s.SafeMargin = v);
        if (s.SafeMargin)
        {
            ImGui.SameLine();
            var pct = s.SafeMarginPercent;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.SliderFloat("% in from the edge", ref pct, 1f, 25f)) { s.SafeMarginPercent = pct; changed = true; }
        }
    }

    private static void DrawAdvanced(CameraSettings s, ref bool changed)
    {
        if (!ImGui.CollapsingHeader("If the camera lands on the wrong side or tilts the wrong way")) return;
        ImGui.TextWrapped("The game's yaw and pitch directions are worked out from its layout, not measured yet. These two fix them without a rebuild.");
        var trim = s.YawTrimDeg;
        if (ImGui.SliderFloat("Yaw trim (deg)", ref trim, -180f, 180f)) { s.YawTrimDeg = trim; changed = true; }
        var flip = s.PitchSign < 0;
        if (ImGui.Checkbox("Flip the tilt", ref flip)) { s.PitchSign = flip ? -1 : 1; changed = true; }
    }

    private static bool Toggle(string label, bool value, Action<bool> set)
    {
        if (!ImGui.Checkbox(label, ref value)) return false;
        set(value);
        return true;
    }
}

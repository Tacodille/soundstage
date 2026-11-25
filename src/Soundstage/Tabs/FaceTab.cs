using Dalamud.Bindings.ImGui;
using Soundstage.Face;

namespace Soundstage.Tabs;

/// <summary>
/// Part 16: reads head angle and light direction from a picture on this computer, then aims the camera and the key light to match.
/// Nothing is uploaded: the face model runs in a helper process downloaded once from its official sources.
/// </summary>
public sealed class FaceTab : ITab
{
    public string Name => "Face";
    public int Order => 74;

    private FaceServices? services;
    private FaceSettings? settings;
    private string path = "";
    private bool pathLoaded;

    private Task<FaceResult>? job;
    private bool jobIsMatch;
    private FaceResult? result;
    private CameraAdvice? advice;
    private LightEstimate? light;
    private string note = "";

    public void Draw(Studio studio)
    {
        EnsureServices(studio);
        var svc = services!;
        var s = settings!;

        ImGui.TextWrapped("Reads where a photo's head points and where its light comes from, then sets your camera and key light to match. The picture is read on this computer only and nothing is uploaded.");
        ImGui.Separator();
        DrawHelper(svc);
        ImGui.Separator();

        if (!pathLoaded) { path = s.LastImage; pathLoaded = true; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##facepath", "Full path to a picture (png, jpg)", ref path, 512);

        var busy = job is { IsCompleted: false };
        ConsumeJob(studio);
        var ready = svc.Installed && !busy && path.Length > 0;

        ImGui.BeginDisabled(!ready);
        if (ImGui.Button("Analyse picture")) StartJob(svc, match: false);
        ImGui.SameLine();
        if (ImGui.Button("Match head angle and light (local, nothing uploaded)")) StartJob(svc, match: true);
        ImGui.EndDisabled();
        if (busy) ImGui.TextDisabled("Working on it. The first picture takes a few seconds while the model starts.");
        if (!svc.Installed) ImGui.TextDisabled("Download the face helper above first.");

        if (note.Length > 0) ImGui.TextWrapped(note);
        if (result is { Available: true } r) DrawResult(r);
        if (advice != null && result is { Available: true })
        {
            ImGui.Separator();
            ImGui.TextWrapped(advice.Text);
            if (light != null) ImGui.TextWrapped(light.Text);
            ImGui.Separator();
            DrawApply(studio, advice, light);
        }

        if (ImGui.CollapsingHeader("Tuning"))
        {
            var changed = false;
            var f = s.PitchBiasDeg;
            if (ImGui.SliderFloat("Pitch bias (deg)", ref f, -20f, 20f)) { s.PitchBiasDeg = f; changed = true; }
            f = s.CheekAngleDeg;
            if (ImGui.SliderFloat("Cheek angle (deg)", ref f, 15f, 70f)) { s.CheekAngleDeg = f; changed = true; }
            f = s.ForeheadChinAngleDeg;
            if (ImGui.SliderFloat("Forehead and chin angle (deg)", ref f, 15f, 60f)) { s.ForeheadChinAngleDeg = f; changed = true; }
            var fr = s.Framing;
            if (ImGui.InputText("Camera framing id", ref fr, 32)) { s.Framing = fr; changed = true; }
            if (changed) Persist(studio);
            ImGui.TextDisabled("Cheek and forehead angles scale how strongly a brighter side reads as a sideways or overhead light. Tune them against the error table in docs/face-model-decision.md.");
        }
    }

    private void EnsureServices(Studio studio)
    {
        if (services != null) return;
        settings = studio.Config.Section<FaceSettings>(FaceSettings.Key);
        services = studio.Face;
    }

    private void Persist(Studio studio)
    {
        studio.Config.SetSection(FaceSettings.Key, settings!);
        studio.Save();
        services!.Analyser.IdleSeconds = settings!.IdleSeconds;
    }

    private void DrawHelper(FaceServices svc)
    {
        if (svc.Manifest == null)
        {
            ImGui.TextWrapped("The face helper cannot be set up: " + svc.ManifestProblem);
            return;
        }
        if (svc.Installing)
        {
            var p = svc.Progress;
            var frac = p.TotalBytes > 0 ? (float)p.DoneBytes / p.TotalBytes : 0f;
            ImGui.ProgressBar(frac, new System.Numerics.Vector2(-1, 0), $"{p.Step}  ({p.DoneBytes / 1048576.0:0} of {p.TotalBytes / 1048576.0:0} MB)");
            if (ImGui.Button("Cancel download")) svc.CancelInstall();
            return;
        }
        if (svc.Installed)
        {
            ImGui.TextUnformatted($"Face helper installed ({svc.Layout.InstalledBytes() / 1048576.0:0} MB on disk). {svc.Analyser.Status}");
            if (ImGui.SmallButton("Stop helper now")) svc.Analyser.Dispose();
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove face helper")) svc.Remove();
        }
        else
        {
            ImGui.TextWrapped($"The face model is not on this computer yet. It is a one-off download of about {svc.Manifest.TotalDownloadBytes / 1048576.0:0} MB " +
                              "(an embeddable Python, the MediaPipe packages and Google's face model, each from its official site and checked against a pinned hash). " +
                              $"It unpacks to about 350 MB inside the plugin's config folder and can be removed from here.");
            if (ImGui.Button("Download the face helper")) svc.StartInstall();
        }
        if (svc.LastInstallMessage.Length > 0) ImGui.TextWrapped(svc.LastInstallMessage);
    }

    private static void DrawResult(FaceResult r)
    {
        ImGui.TextUnformatted($"Head: yaw {r.YawDeg:0.0}, pitch {r.PitchDeg:0.0}, roll {r.RollDeg:0.0} degrees.  Framing confidence {r.Confidence:0.00}.");
        ImGui.TextDisabled(HeadAngleWords.Describe(r));
        var top = r.Blendshapes.Where(kv => kv.Key != "_neutral").OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value:0.00}");
        ImGui.TextWrapped("Strongest expression scores: " + string.Join(", ", top));
    }

    private void DrawApply(Studio studio, CameraAdvice a, LightEstimate? l)
    {
        var cam = studio.Camera;
        var canCam = cam != null && a.Ok && cam.Service.CanWrite(out _);
        ImGui.BeginDisabled(!canCam);
        if (ImGui.Button("Move camera to match")) ApplyCamera(studio, a);
        ImGui.EndDisabled();
        if (!canCam && cam != null && !cam.Service.CanWrite(out var why)) { ImGui.SameLine(); ImGui.TextDisabled(why); }
        ImGui.SameLine();
        ImGui.BeginDisabled(l is not { Ok: true });
        if (ImGui.Button("Aim key light to match") && l != null) ApplyLight(studio, l);
        ImGui.EndDisabled();
    }

    private void StartJob(FaceServices svc, bool match)
    {
        settings!.LastImage = path;
        var p = path;
        jobIsMatch = match;
        note = "";
        job = Task.Run(() => svc.Analyser.Analyse(p));
    }

    private void ConsumeJob(Studio studio)
    {
        if (job is not { IsCompleted: true }) return;
        var t = job;
        job = null;
        FaceResult r;
        try { r = t.Result; } catch (Exception e) { r = FaceResult.Unavailable("The face analysis failed: " + e.Message); }
        result = r;
        HeadAngleWords.Latest = r;
        Persist(studio);
        advice = null; light = null;
        if (!r.Available) { note = r.Reason; return; }

        advice = CameraFromFace.Solve(r, settings!);
        var img = FaceImage.TryLoad(path, out var why);
        light = img == null ? new LightEstimate(false, why, 0, 0, 0, 0, 0, 0, why) : LightFromShading.Estimate(r, img, settings!);
        if (!light.Ok) note = "Light: " + light.Reason;
        if (jobIsMatch)
        {
            ApplyCamera(studio, advice);
            if (light.Ok) ApplyLight(studio, light);
        }
    }

    private void ApplyCamera(Studio studio, CameraAdvice a)
    {
        var cam = studio.Camera;
        if (cam == null || !a.Ok) return;
        if (!cam.Service.ApplyFraming(a.Framing, a.AzimuthDeg, a.OffsetY, cam.Settings.AddedFov, out var why))
            note = "Camera not moved: " + why + " (" + a.Text + ")";
    }

    private void ApplyLight(Studio studio, LightEstimate l)
    {
        var cur = studio.Plugin.Current;
        if (cur == null || cur.Lights.Count == 0) { note = (note + " Apply a Scene first, then aim its key light.").Trim(); return; }
        var key = cur.Lights.FirstOrDefault(x => x.Role == "key") ?? cur.Lights[0];
        var reference = key.AimHeight ?? AimSolver.DefaultHeadHeight;
        key.AzimuthDeg = Math.Clamp(l.AzimuthDeg, -90f, 90f);
        key.Height = Math.Clamp(reference + key.Distance * MathF.Tan(l.ElevationDeg * MathF.PI / 180f), 0.3f, 3.5f);
        if (studio.Bridge.Placed) studio.Plugin.PlaceNow();
        note = (note + $" Key light ('{key.Role}') set to {key.AzimuthDeg:0} degrees round, {key.Height:0.00} m high.").Trim();
    }
}

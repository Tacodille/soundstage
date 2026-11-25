using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundstage.Expression;
using Soundstage.Expression.Live;
using Soundstage.Face;

namespace Soundstage.Tabs;

/// <summary>
/// Part 18: the body half of the Expression tab. Reads a full-length photo with the body pose model (a 9 MB download that rides on the face
/// helper), turns the photo's limb directions into bone rotations on your character through Brio, and scores each try by how well the outline of the
/// game's picture overlaps the photo's. Everything runs on this computer. Drawn by <see cref="ExpressionTab"/> below the face controls.
/// </summary>
public sealed class BodySection
{
    private BodySettings? settings;
    private BodyMap? map;
    private string mapProblem = "";
    private ExpressionRig? rig;
    private BodyMatch? match;
    private bool started;

    private string path = "";
    private bool pathLoaded;
    private Task<BodyPoseResult>? photoJob;
    private BodyPoseResult? photo;
    private string photoNote = "";
    private bool rated;
    private string note = "";
    private string checkReport = "";

    public void Draw(Studio studio, FaceServices face, bool faceBusy)
    {
        if (!Ensure(studio, face)) return;
        var s = settings!;
        var m = match!;

        ImGui.TextUnformatted("Body pose");
        ImGui.TextWrapped("Copies a full-length photo's pose onto your character: arms, legs, spine and neck turn to point the way the photo's do. " +
                          "It does not turn the whole body round, move the character about or touch the face. Nothing is uploaded.");
        ImGui.TextDisabled(BodyTrials.Describe(s));

        if (BodyTrials.IsExperimental(s) && !s.ShowExperimental)
        {
            ImGui.TextWrapped("Experimental. Your own ratings put this below 'recognisable', so it is switched off. Tick the box to use it anyway.");
            var on = s.ShowExperimental;
            if (ImGui.Checkbox("Show the experimental body matcher", ref on)) { s.ShowExperimental = on; Persist(studio); }
            return;
        }
        if (BodyTrials.IsExperimental(s)) ImGui.TextColored(new Vector4(1f, 0.75f, 0.3f, 1f), "EXPERIMENTAL: the matches have not been good enough in your own ratings.");

        if (map == null) { ImGui.TextWrapped("The body map could not be loaded: " + mapProblem); return; }

        DrawModel(studio, face);
        var ready = face.PoseInstalled;

        if (!pathLoaded) { path = s.LastImage; pathLoaded = true; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##bodypath", "Full path to a full-length photo (png, jpg)", ref path, 512);

        m.Tick();
        if (started && !m.Running) { started = false; rig!.Clean(); }
        ConsumePhoto();

        var reading = photoJob is { IsCompleted: false };
        ImGui.BeginDisabled(!ready || reading || m.Running || path.Length == 0);
        if (ImGui.Button("Read body photo")) { s.LastImage = path; Persist(studio); photoNote = ""; var p = path; photoJob = Task.Run(() => face.Analyser.AnalysePose(p)); }
        ImGui.EndDisabled();
        if (reading) { ImGui.SameLine(); ImGui.TextDisabled("Reading the photo..."); }
        if (photoNote.Length > 0) ImGui.TextWrapped(photoNote);

        var canMatch = ready && photo is { Available: true, Silhouette: not null } && !m.Running && !faceBusy && rig!.Ready(out _);
        ImGui.BeginDisabled(!canMatch);
        if (ImGui.Button("Match pose")) { rated = false; note = ""; FrameBody(studio); started = m.Start(photo!, s.Mirror); }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(m.Running || !rig!.Ready(out _) || map == null);
        if (ImGui.Button("Check the body map")) RunCheck(studio);
        ImGui.EndDisabled();
        if (!ready) ImGui.TextDisabled("Greyed out: the body model is not downloaded yet.");
        else if (photo is not { Available: true, Silhouette: not null }) ImGui.TextDisabled("Read a photo first.");
        else if (faceBusy) ImGui.TextDisabled("The face section is running. Wait for it to finish.");
        else if (!rig!.Ready(out var why)) ImGui.TextDisabled(why);
        if (note.Length > 0) ImGui.TextWrapped(note);
        if (checkReport.Length > 0) ImGui.TextWrapped(checkReport);

        if (m.Running)
        {
            ImGui.TextWrapped(m.Status);
            if (ImGui.Button("Cancel body match")) m.Cancel();
        }
        else
        {
            if (m.Status.Length > 0) ImGui.TextWrapped(m.Status);
            if (m.ScoreBefore is { } before && m.ScoreAfter is { } after && m.State == MatchState.Done) DrawResult(studio, m, before, after);
        }

        DrawTuning(studio, s);
    }

    private bool Ensure(Studio studio, FaceServices face)
    {
        if (settings != null) return true;
        settings = studio.Config.Section<BodySettings>(BodySettings.Key);
        map = BodyMap.LoadEmbedded(out mapProblem);
        var dir = studio.Plugin.ConfigDirectory;
        rig = new ExpressionRig(studio, Path.Combine(dir, "body-shots"));
        if (map != null)
            match = new BodyMatch(rig, p => face.Analyser.AnalysePose(p), settings, map, line => studio.Log.Information("{Line:l}", line));
        else
            match = new BodyMatch(rig, _ => BodyPoseResult.Unavailable("no map"), settings, new BodyMap(), _ => { });
        return true;
    }

    private void Persist(Studio studio)
    {
        studio.Config.SetSection(BodySettings.Key, settings!);
        studio.Save();
    }

    private void DrawModel(Studio studio, FaceServices face)
    {
        if (!face.Installed)
        {
            ImGui.TextWrapped("Needs the face helper first (Face tab). The body model runs inside it.");
            return;
        }
        var pm = face.PoseManifest;
        if (face.PoseInstalling)
        {
            var pr = face.PoseProgress;
            var frac = pr.TotalBytes > 0 ? Math.Clamp(pr.DoneBytes / (float)pr.TotalBytes, 0f, 1f) : 0f;
            ImGui.ProgressBar(frac, new Vector2(-1, 0), pr.Step);
            if (ImGui.Button("Cancel download")) face.CancelPoseInstall();
            return;
        }
        if (face.PoseInstalled)
        {
            ImGui.TextDisabled("Body model on this computer" + (pm != null ? $" ({pm.Size / 1048576.0:0.0} MB)." : "."));
            ImGui.SameLine();
            if (ImGui.SmallButton("Remove body model")) face.RemovePose();
        }
        else
        {
            ImGui.TextWrapped($"The body model is a one-off download of about {(pm == null ? 9 : pm.Size / 1048576.0):0} MB from Google's model store, checked against a pinned hash before it is used.");
            ImGui.BeginDisabled(pm == null);
            if (ImGui.Button("Download the body model")) face.StartPoseInstall();
            ImGui.EndDisabled();
        }
        if (face.LastPoseMessage.Length > 0) ImGui.TextWrapped(face.LastPoseMessage);
    }

    private void FrameBody(Studio studio)
    {
        var c = studio.Camera;
        if (c == null) { note = "No camera service, so the camera was not moved. Frame the whole character yourself."; return; }
        note = c.Service.ApplyFraming(settings!.Framing, 0f, 0f, c.Settings.AddedFov, out var why) ? "Camera set to the full-body framing." : "Camera not moved: " + why;
    }

    private void RunCheck(Studio studio)
    {
        var snap = rig!.Snapshot();
        if (snap == null) { checkReport = "Brio could not read the current pose."; return; }
        var c = BodyRetarget.CheckSkeleton(snap, map!);
        var parts = new List<string>();
        parts.Add(c.Missing.Count == 0 ? "Every mapped bone is in the pose." : "Missing bones: " + string.Join(", ", c.Missing) + ".");
        parts.Add($"{c.Checked - c.Wrong} of {c.Checked} checked segments point the way the map expects at rest.");
        parts.AddRange(c.Notes);
        parts.Add(c.DoesNotFit ? "The map does NOT fit this character as it stands. Reset its pose in Brio, then check again." : "The map fits.");
        checkReport = string.Join(" ", parts);

        // The same numbers go to the log so a mismatch can be read off without the screen.
        var dirs = BodyRetarget.SegmentDirections(snap, map!, out _);
        foreach (var (id, d) in dirs.OrderBy(k => k.Key, StringComparer.Ordinal))
            studio.Log.Information("[Soundstage/BODY] rest {Id} points ({X:0.00}, {Y:0.00}, {Z:0.00})", id, d.X, d.Y, d.Z);
        studio.Log.Information("[Soundstage/BODY] check: {Report}", checkReport);
    }

    private void DrawResult(Studio studio, BodyMatch m, float before, float after)
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"Outline score before {before:0}, after {after:0} (out of 100).");
        ImGui.TextDisabled("This is how well the outline of the game's picture overlaps the photo's, after both are cut to their own box. It is not a judgement of likeness: you rate that below.");
        var best = m.Iterations[m.BestIndex];
        var first = m.Iterations[0];
        if (best.AngleErrorDeg is { } a && first.AngleErrorDeg is { } a0)
            ImGui.TextUnformatted($"Average limb direction error {a0:0} degrees before, {a:0} after. {best.Clamped} of {m.Target.Count} segments were held at a joint limit.");
        foreach (var n in m.Notes.Take(6)) ImGui.TextWrapped(n);

        if (ImGui.CollapsingHeader("Per joint (degrees off the photo)"))
        {
            if (ImGui.BeginTable("##bodyjoints", 3, ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Joint"); ImGui.TableSetupColumn("Before"); ImGui.TableSetupColumn("After");
                ImGui.TableHeadersRow();
                foreach (var (id, t) in m.Target.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(id);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(first.Observed.TryGetValue(id, out var o0) ? Deg(t, o0) : "n/a");
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(best.Observed.TryGetValue(id, out var o1) ? Deg(t, o1) : "n/a");
                }
                ImGui.EndTable();
            }
        }

        if (ImGui.Button("Undo (put the old pose back)"))
            note = m.Undo(out var why) ? "Old pose back." : "Could not undo: " + why;

        ImGui.TextUnformatted("How recognisable is the pose, 1 (not at all) to 5 (spot on)?");
        ImGui.BeginDisabled(rated);
        for (var r = 1; r <= 5; r++)
        {
            if (r > 1) ImGui.SameLine();
            if (ImGui.Button(r.ToString() + "##bodyrate" + r)) Rate(studio, r, after);
        }
        ImGui.EndDisabled();
        if (rated) ImGui.TextDisabled("Rated. Copy the score and rating into docs/expression-trials.md (body table).");
    }

    private static string Deg(Vector3 a, Vector3 b) => (MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180f / MathF.PI).ToString("0");

    private void Rate(Studio studio, int rating, float score)
    {
        settings!.Trials.Add(new ExpressionTrial { Note = Path.GetFileName(path), Score = score, Rating = rating, Date = DateTime.Now.ToString("yyyy-MM-dd") });
        rated = true;
        Persist(studio);
    }

    private void DrawTuning(Studio studio, BodySettings s)
    {
        if (!ImGui.CollapsingHeader("Body tuning")) return;
        var changed = false;
        var mirror = s.Mirror;
        if (ImGui.Checkbox("Mirror (swap the photo's left and right)", ref mirror)) { s.Mirror = mirror; changed = true; }
        var i = s.MaxIterations;
        if (ImGui.SliderInt("Most body passes", ref i, 1, 8)) { s.MaxIterations = i; changed = true; }
        var f = s.Gain;
        if (ImGui.SliderFloat("Feedback (0 = ask once)", ref f, 0f, 1.5f)) { s.Gain = f; changed = true; }
        f = s.Epsilon;
        if (ImGui.SliderFloat("Stop when a pass gains less than (points)", ref f, 0f, 5f)) { s.Epsilon = f; changed = true; }
        i = s.SettleFrames;
        if (ImGui.SliderInt("Frames to wait before each picture", ref i, 0, 30)) { s.SettleFrames = i; changed = true; }
        f = s.MinVisibility;
        if (ImGui.SliderFloat("Ignore landmarks less visible than", ref f, 0f, 0.9f)) { s.MinVisibility = f; changed = true; }
        var skip = s.SkipSkeletonCheck;
        if (ImGui.Checkbox("Apply anyway when the body map does not fit", ref skip)) { s.SkipSkeletonCheck = skip; changed = true; }
        var fr = s.Framing;
        if (ImGui.InputText("Camera framing id", ref fr, 32)) { s.Framing = fr; changed = true; }
        if (changed) Persist(studio);
        ImGui.TextDisabled("Joint limits and which bone follows which landmark are in body-map.json inside the plugin, not here.");
    }

    private void ConsumePhoto()
    {
        if (photoJob is not { IsCompleted: true }) return;
        var t = photoJob;
        photoJob = null;
        try { photo = t.Result; } catch (Exception ex) { photo = BodyPoseResult.Unavailable("The body analysis failed: " + ex.Message); }
        if (!photo.Available) { photoNote = photo.Reason; return; }
        var seen = map == null ? 0 : BodyRetarget.Directions(photo, map, new BodyRetarget.Options { MinVisibility = settings!.MinVisibility }).Count;
        photoNote = photo.Silhouette == null
            ? "Photo read, but the helper gave no outline, so it cannot be scored."
            : $"Photo read: {seen} of {map?.Joints.Count ?? 0} joints clear enough to copy.";
    }
}

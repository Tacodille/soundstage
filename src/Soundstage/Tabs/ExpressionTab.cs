using Dalamud.Bindings.ImGui;
using Soundstage.Expression;
using Soundstage.Expression.Live;
using Soundstage.Face;

namespace Soundstage.Tabs;

/// <summary>
/// Part 17: copy a photo's expression onto your character's face. "Calibrate my face" measures how this face responds to its own expression
/// bones (once per race, clan, gender and face), then "Match expression" nudges those bones through Brio until the face reads like the photo.
/// Everything runs on this computer. The face model is the helper from the Face tab, shared with it through Studio.Face.
/// </summary>
public sealed class ExpressionTab : ITab
{
    public string Name => "Expression";
    public int Order => 76;

    private ExpressionSettings? settings;
    private FaceServices? face;
    private ExpressionRig? rig;
    private ExpressionTableStore? store;
    private Calibrator? calibrator;
    private MatchLoop? match;
    private FaceKey? key;
    private ExpressionTable? table;
    private string keyProblem = "";
    private bool tableLoaded;

    private string path = "";
    private bool pathLoaded;
    private Task<FaceResult>? photoJob;
    private FaceResult? photo;
    private string photoNote = "";
    private bool rated;
    private string note = "";

    public void Draw(Studio studio)
    {
        Ensure(studio);
        var s = settings!;
        var analyser = face!.Analyser;

        ImGui.TextWrapped("Copies a photo's expression onto your character's face. First calibrate your face (it measures how your own face bones move it), then match a photo. " +
                          "Calibrating takes about 80 pictures, so leave this tab showing while it runs. Nothing is uploaded.");
        ImGui.TextDisabled(ExpressionTrials.Describe(s));

        if (ExpressionTrials.IsExperimental(s) && !s.ShowExperimental)
        {
            ImGui.Separator();
            ImGui.TextWrapped("Experimental. Your own ratings put this below 'recognisable', so it is switched off. Tick the box to use it anyway.");
            var on = s.ShowExperimental;
            if (ImGui.Checkbox("Show the experimental expression matcher", ref on)) { s.ShowExperimental = on; Persist(studio); }
            ImGui.Separator();
            DrawBodySection(studio);   // the body section has its own rating, so a low face rating does not hide it
            return;
        }
        if (ExpressionTrials.IsExperimental(s)) ImGui.TextColored(new System.Numerics.Vector4(1f, 0.75f, 0.3f, 1f), "EXPERIMENTAL: the matches have not been good enough in your own ratings.");

        ImGui.Separator();
        if (!analyser.CanTry)
        {
            ImGui.TextWrapped("Greyed out: the face model is not on this computer yet. " + analyser.Status + " Download it from the Face tab, then come back.");
        }

        var cal = calibrator!;
        var m = match!;
        var busy = cal.Running || m.Running;
        cal.Tick();
        m.Tick();
        if (busy && !cal.Running && !m.Running) { tableLoaded = false; rig!.Clean(); }
        ConsumePhoto(studio);

        DrawCalibrate(studio, analyser, cal, m);
        ImGui.Separator();
        DrawMatch(studio, analyser, cal, m);
        ImGui.Separator();
        DrawBodySection(studio);

        if (ImGui.CollapsingHeader("Tuning"))
        {
            var changed = false;
            var f = s.PositionStep;
            if (ImGui.SliderFloat("Slide step (pose units)", ref f, 0.001f, 0.02f, "%.4f")) { s.PositionStep = f; changed = true; }
            f = s.RotationStepDeg;
            if (ImGui.SliderFloat("Jaw turn step (deg)", ref f, 1f, 15f)) { s.RotationStepDeg = f; changed = true; }
            f = s.SafeSteps;
            if (ImGui.SliderFloat("Safe range (steps either way)", ref f, 1f, 4f)) { s.SafeSteps = f; changed = true; }
            var i = s.SettleFrames;
            if (ImGui.SliderInt("Frames to wait before each picture", ref i, 0, 30)) { s.SettleFrames = i; changed = true; }
            i = s.MaxIterations;
            if (ImGui.SliderInt("Most match passes", ref i, 1, 10)) { s.MaxIterations = i; changed = true; }
            if (changed) Persist(studio);
            ImGui.TextDisabled("A new step or range means the stored table no longer matches: calibrate again to rebuild it.");
        }
    }

    // ---- Part 18: the body section. Its state lives in BodySection; this slot only hands it the shared helper and whether the face run is busy. ----
    private readonly BodySection body = new();

    private void DrawBodySection(Studio studio) => body.Draw(studio, face!, faceBusy: calibrator is { Running: true } || match is { Running: true });

    private void Ensure(Studio studio)
    {
        if (settings != null) return;
        settings = studio.Config.Section<ExpressionSettings>(ExpressionSettings.Key);
        var dir = studio.Plugin.ConfigDirectory;
        face = studio.Face;
        rig = new ExpressionRig(studio, Path.Combine(dir, "expression-shots"));
        store = new ExpressionTableStore(Path.Combine(dir, "expression-tables"));
        Action<string> log = line => studio.Log.Information("{Line:l}", line);
        calibrator = new Calibrator(rig, face.Analyser, store, settings, log);
        match = new MatchLoop(rig, face.Analyser, settings, log);
    }

    private void Persist(Studio studio)
    {
        studio.Config.SetSection(ExpressionSettings.Key, settings!);
        studio.Save();
    }

    private void RefreshKey(bool force)
    {
        if (tableLoaded && !force) return;
        if (!rig!.Ready(out var why)) { key = null; keyProblem = why; table = null; return; }
        key = rig.Key(out why);
        keyProblem = key == null ? why : "";
        table = key == null ? null : store!.Load(key, out _);
        tableLoaded = key != null;
    }

    private void DrawCalibrate(Studio studio, IFaceAnalyser analyser, Calibrator cal, MatchLoop m)
    {
        RefreshKey(force: false);
        if (key != null)
        {
            var status = table == null ? "not calibrated yet" : table.Complete ? $"calibrated ({table.Channels.Count} bone moves)" : $"part calibrated ({table.MeasuredCount} of {table.Channels.Count} bone moves)";
            ImGui.TextUnformatted($"Face {key.Id}: {status}.");
        }
        else ImGui.TextDisabled(keyProblem.Length > 0 ? keyProblem : "Enter GPose to see this face.");

        if (cal.Running)
        {
            ImGui.ProgressBar(cal.Progress, new System.Numerics.Vector2(-1, 0), $"{cal.Done} of {cal.Total}");
            ImGui.TextWrapped(cal.Status);
            if (ImGui.Button("Cancel calibration")) cal.Cancel();
            return;
        }

        var ready = analyser.CanTry && !m.Running && rig!.Ready(out _);
        ImGui.BeginDisabled(!ready);
        if (ImGui.Button(table is { MeasuredCount: > 0 } ? "Calibrate my face (carry on)" : "Calibrate my face")) StartCalibration(studio, cal);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!(ready && cam(studio) != null));
        if (ImGui.Button("Frame the face close up")) FrameFace(studio);
        ImGui.EndDisabled();
        if (!ready) ImGui.TextDisabled(analyser.CanTry ? (rig!.Ready(out var why) ? "" : why) : "Needs the face model (Face tab).");
        ImGui.TextDisabled("Start from a neutral face. A face that is already smiling measures the smile as normal.");
        if (cal.Status.Length > 0) ImGui.TextWrapped(cal.Status);
    }

    private static CameraTools.CameraHost? cam(Studio studio) => studio.Camera;

    private void FrameFace(Studio studio)
    {
        var c = cam(studio);
        if (c == null) return;
        note = c.Service.ApplyFraming(settings!.Framing, 0f, 0f, c.Settings.AddedFov, out var why) ? "Camera set to the close-up." : "Camera not moved: " + why;
    }

    private void StartCalibration(Studio studio, Calibrator cal)
    {
        FrameFace(studio);
        cal.Start();
        tableLoaded = false;
    }

    private void DrawMatch(Studio studio, IFaceAnalyser analyser, Calibrator cal, MatchLoop m)
    {
        if (!pathLoaded) { path = settings!.LastImage; pathLoaded = true; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##exprpath", "Full path to the photo (png, jpg)", ref path, 512);

        var reading = photoJob is { IsCompleted: false };
        ImGui.BeginDisabled(!analyser.CanTry || reading || m.Running || cal.Running || path.Length == 0);
        if (ImGui.Button("Read photo")) { settings!.LastImage = path; Persist(studio); photoNote = ""; var p = path; photoJob = Task.Run(() => analyser.Analyse(p)); }
        ImGui.EndDisabled();
        if (reading) { ImGui.SameLine(); ImGui.TextDisabled("Reading the photo..."); }
        if (photoNote.Length > 0) ImGui.TextWrapped(photoNote);

        var canMatch = analyser.CanTry && photo is { Available: true } && table is { MeasuredCount: > 0 } && !m.Running && !cal.Running && rig!.Ready(out _);
        ImGui.BeginDisabled(!canMatch);
        if (ImGui.Button("Match expression")) { rated = false; FrameFace(studio); m.Start(photo!, table!); }
        ImGui.EndDisabled();
        if (!analyser.CanTry) ImGui.TextDisabled("Greyed out: the face model is not downloaded (Face tab).");
        else if (table is not { MeasuredCount: > 0 }) ImGui.TextDisabled("Calibrate your face first.");
        else if (photo is not { Available: true }) ImGui.TextDisabled("Read a photo first.");
        if (note.Length > 0) ImGui.TextWrapped(note);

        if (m.Running)
        {
            ImGui.TextWrapped(m.Status);
            if (ImGui.Button("Cancel match")) m.Cancel();
            return;
        }
        if (m.Status.Length > 0) ImGui.TextWrapped(m.Status);
        if (m.ScoreBefore is { } before && m.ScoreAfter is { } after && m.State == MatchState.Done) DrawResult(studio, m, before, after);
    }

    private void DrawResult(Studio studio, MatchLoop m, float before, float after)
    {
        ImGui.Separator();
        ImGui.TextUnformatted($"Score before {before:0}, after {after:0} (out of 100).");
        ImGui.TextDisabled("This is the face model's own number for how close the blendshapes are. It is not a judgement of likeness: you rate that below.");
        if (m.AngleNote.Length > 0) ImGui.TextWrapped(m.AngleNote);

        var names = m.Names;
        if (names.Length > 0 && m.BestIndex >= 0)
        {
            var obs0 = m.Iterations[0].Observed;
            var obs1 = m.Iterations[m.BestIndex].Observed;
            var order = Enumerable.Range(0, names.Length).OrderByDescending(i => MathF.Abs(m.Target[i] - obs0[i])).Take(8).ToList();
            if (ImGui.BeginTable("##exprcmp", 4, ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn("Blendshape");
                ImGui.TableSetupColumn("Photo");
                ImGui.TableSetupColumn("Before");
                ImGui.TableSetupColumn("After");
                ImGui.TableHeadersRow();
                foreach (var i in order)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(names[i]);
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(m.Target[i].ToString("0.00"));
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(obs0[i].ToString("0.00"));
                    ImGui.TableNextColumn(); ImGui.TextUnformatted(obs1[i].ToString("0.00"));
                }
                ImGui.EndTable();
            }
        }

        if (ImGui.Button("Undo (put the old expression back)"))
            note = m.Undo(out var why) ? "Old expression back." : "Could not undo: " + why;

        ImGui.TextUnformatted("How recognisable is it, 1 (not at all) to 5 (spot on)?");
        ImGui.BeginDisabled(rated);
        for (var r = 1; r <= 5; r++)
        {
            if (r > 1) ImGui.SameLine();
            if (ImGui.Button(r.ToString() + "##rate" + r)) Rate(studio, r, after);
        }
        ImGui.EndDisabled();
        if (rated) ImGui.TextDisabled("Rated. Copy the score and rating into docs/expression-trials.md.");
    }

    private void Rate(Studio studio, int rating, float score)
    {
        settings!.Trials.Add(new ExpressionTrial { Note = Path.GetFileName(path), Score = score, Rating = rating, Date = DateTime.Now.ToString("yyyy-MM-dd") });
        rated = true;
        Persist(studio);
    }

    private void ConsumePhoto(Studio studio)
    {
        if (photoJob is not { IsCompleted: true }) return;
        var t = photoJob;
        photoJob = null;
        try { photo = t.Result; } catch (Exception e) { photo = FaceResult.Unavailable("The face analysis failed: " + e.Message); }
        photoNote = photo.Available ? $"Photo read: head yaw {photo.YawDeg:0}, pitch {photo.PitchDeg:0}, roll {photo.RollDeg:0}." : photo.Reason;
    }
}

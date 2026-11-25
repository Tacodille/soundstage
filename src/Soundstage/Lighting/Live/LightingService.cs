using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Dalamud.Plugin.Services;
using Soundstage.GameAccess;

namespace Soundstage;

/// <summary>
/// The live side of the Lighting tab: reads the head for face-aimed lights, plays a Scene's animation block, throttles the
/// re-sends and measures what they cost. The maths it calls is in Lighting/*.cs and is checked offline; this file is the part
/// that touches the game and the framework thread, and is checked in game (checklist area "lighting").
///
/// Today's per-tick work only moves transforms, so animation needs a whole-stage re-send (the same call Stagehand's own demo
/// makes every tick). It runs from the framework tick, at most <see cref="LightingSettings.MaxAnimationHz"/> times a second
/// (never above 15) and only while a Scene has an enabled animation effect or a face-aimed light whose face has moved.
/// </summary>
public sealed class LightingService
{
    public const string LogPrefix = "[Soundstage/ANIM]";
    private const double SummaryEverySeconds = 5.0;
    private const int ThrottleWindow = 20;
    private const double MeasureSecondsPerPhase = 5.0;

    private readonly Plugin plugin;
    private readonly IObjectTable objects;
    private readonly IPluginLog log;

    private LightingSettings? settings;
    private AnimationThrottle throttle = new(Animator.HardMaxHz);

    // ---- face ----
    private Vector3? lastSolvedFace;

    /// <summary>One line for the tab: where the face aim is getting the head from.</summary>
    public string HeadStatus { get; private set; } = "No face-aimed light in this Scene.";

    /// <summary>The face (stage-local) the last re-send used, or null when lights were placed as authored.</summary>
    public Vector3? LastFace { get; private set; }

    // ---- animation ----
    private long lastTickStamp;
    private long lastSendStamp;
    private long animStartStamp;
    private string animScene = "";
    private bool wasAnimating;
    private long lastSummaryStamp;
    private readonly CostMeter frameOn = new();
    private readonly CostMeter frameOff = new();
    private readonly CostMeter resend = new();
    private readonly CostMeter window = new();

    public enum MeasurePhase { None, Off, On }

    private MeasurePhase measure = MeasurePhase.None;
    private long measureStart;
    private bool measureRestorePlay;

    public MeasurePhase Measuring => measure;

    /// <summary>The result of the last measurement run, for the tab. Empty until one has run.</summary>
    public string LastMeasure { get; private set; } = "";

    public List<Gel> Gels { get; }

    public LightingService(Plugin plugin, IObjectTable objects, IPluginLog log, string gelsPath)
    {
        this.plugin = plugin;
        this.objects = objects;
        this.log = log;
        GameAccessHost.Use(log);
        Animator.RegisterBlock();
        Gels = Soundstage.Gels.Load(gelsPath, line => log.Information("{P} {L}", LogPrefix, line));
    }

    public LightingSettings Settings => settings ??= plugin.Config.Section<LightingSettings>(LightingSettings.Key);

    public void SaveSettings()
    {
        plugin.Config.SetSection(LightingSettings.Key, Settings);
        plugin.Save();
    }

    public float CurrentHz => throttle.Hz;

    // ------------------------------------------------------------------------------------------------------------
    // Face aim
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Reads the head and returns it in stage-local space, without recording anything. Null means "place lights as authored".</summary>
    private Vector3? PeekFace(Scene scene, out string status)
    {
        if (!AimSolver.AnyFaceAimed(scene)) { status = "No face-aimed light in this Scene."; return null; }
        var local = objects.LocalPlayer;
        if (local == null) { status = "Face aim: no player yet, lights placed as authored."; return null; }
        if (!HeadBone.TryGet(out var head, out var reason))
        {
            status = $"Face aim: head bone not readable ({reason}). Using the Scene's own aim heights instead.";
            return null;
        }
        var face = AimSolver.ToStageLocal(head, local.Position, local.Rotation);
        if (!AimSolver.Plausible(face, out var why))
        {
            status = $"Face aim: {why}. Using the Scene's own aim heights instead.";
            return null;
        }
        status = string.Create(CultureInfo.InvariantCulture, $"Face aim: following the head bone ({face.Y:F2} m up, {face.X:F2} m left, {face.Z:F2} m forward of your feet).");
        return face;
    }

    /// <summary>Called by <see cref="Plugin.PlaceNow"/>: the face to solve with now, remembered so the tick can tell when it has moved.</summary>
    public Vector3? ResolveFace(Scene scene)
    {
        var face = PeekFace(scene, out var status);
        if (status != HeadStatus)
        {
            HeadStatus = status;
            log.Information("{P} {S}", LogPrefix, status);
        }
        lastSolvedFace = face;
        LastFace = face;
        return face;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Animation
    // ------------------------------------------------------------------------------------------------------------

    private bool Animating(Scene scene) =>
        Settings.PlayAnimation && measure != MeasurePhase.Off && Animator.HasActive(scene);

    /// <summary>The Scene as it should look right now: the animation applied, or the Scene itself when nothing plays.</summary>
    public Scene Frame(Scene scene)
    {
        if (!Animating(scene)) return scene;
        if (scene.Id != animScene || animStartStamp == 0) { animScene = scene.Id; animStartStamp = Stopwatch.GetTimestamp(); }
        var t = (Stopwatch.GetTimestamp() - animStartStamp) / (double)Stopwatch.Frequency;
        return Animator.Apply(scene, t, Settings.StrobeMaxHz);
    }

    /// <summary>Starts a 5 second animation-off then 5 second animation-on run on the applied Scene and logs both frame times.</summary>
    public string StartMeasure()
    {
        var cur = plugin.Current;
        if (cur == null || !plugin.Bridge.Placed) return "Apply a Scene first.";
        if (!Animator.HasActive(cur)) return "This Scene has no animation switched on. Switch an effect on first (a strobe stays off unless you do).";
        measureRestorePlay = Settings.PlayAnimation;
        frameOn.Reset(); frameOff.Reset(); resend.Reset(); window.Reset();
        measure = MeasurePhase.Off;
        measureStart = Stopwatch.GetTimestamp();
        throttle = new AnimationThrottle(Settings.MaxAnimationHz);
        plugin.PlaceNow(quiet: true);   // show the still state straight away
        log.Information("{P} measure started on '{S}': {N} s with animation off, then {N} s on.", LogPrefix, cur.Id, MeasureSecondsPerPhase, MeasureSecondsPerPhase);
        return "Measuring: 5 s with the animation off, then 5 s on.";
    }

    /// <summary>Called every framework frame while no rebuild is pending. Cheap when nothing animates and nothing is face-aimed.</summary>
    public void Tick()
    {
        var now = Stopwatch.GetTimestamp();
        var frameMs = lastTickStamp == 0 ? 0.0 : (now - lastTickStamp) * 1000.0 / Stopwatch.Frequency;
        lastTickStamp = now;

        var cur = plugin.Current;
        if (cur == null || !plugin.Bridge.Placed)
        {
            if (wasAnimating) EndAnimation();
            if (measure != MeasurePhase.None) FinishMeasure("the stage was removed");
            animStartStamp = 0;
            return;
        }

        var animating = Animating(cur);
        var faceAimed = AimSolver.AnyFaceAimed(cur);
        if (!animating && !faceAimed && measure == MeasurePhase.None)
        {
            if (wasAnimating) EndAnimation();
            return;
        }

        // Frame interval, on and off, only inside the same Scene: "off" counts frames of an animated Scene that is paused.
        if (frameMs > 0)
        {
            if (animating) frameOn.Add(frameMs);
            else if (Animator.HasActive(cur)) frameOff.Add(frameMs);
        }

        if (measure != MeasurePhase.None) AdvanceMeasure(now, animating);
        animating = Animating(cur);

        if (animating && !wasAnimating)
        {
            wasAnimating = true;
            lastSummaryStamp = now;
            if (measure == MeasurePhase.None) { resend.Reset(); window.Reset(); frameOn.Reset(); throttle = new AnimationThrottle(Settings.MaxAnimationHz); }
            log.Information("{P} animation on for '{S}' (cap {Cap:F0} Hz, strobe cap {St:F0} Hz).", LogPrefix, cur.Id, throttle.Cap, Settings.StrobeMaxHz);
        }
        else if (!animating && wasAnimating) EndAnimation();

        if (Math.Abs(throttle.Cap - Math.Clamp(Settings.MaxAnimationHz, AnimationThrottle.MinHz, Animator.HardMaxHz)) > 1e-3f && measure == MeasurePhase.None)
            throttle = new AnimationThrottle(Settings.MaxAnimationHz);

        var sinceSendMs = (now - lastSendStamp) * 1000.0 / Stopwatch.Frequency;
        var interval = 1000.0 / (animating ? throttle.Hz : Animator.HardMaxHz);
        if (sinceSendMs < interval) return;

        var moved = false;
        if (faceAimed)
        {
            var face = PeekFace(cur, out _);
            moved = face != lastSolvedFace && (face == null || lastSolvedFace == null || AimSolver.MovedEnough(lastSolvedFace.Value, face.Value));
        }
        if (!animating && !moved) return;

        var sw = Stopwatch.StartNew();
        plugin.PlaceNow(quiet: true);
        sw.Stop();
        lastSendStamp = Stopwatch.GetTimestamp();
        var ms = sw.Elapsed.TotalMilliseconds;
        resend.Add(ms);

        if (animating)
        {
            window.Add(ms);
            if (window.Count >= ThrottleWindow)
            {
                if (throttle.Evaluate(window.Average, Settings.FrameBudgetMs))
                    log.Information("{P} throttle now {Hz:F1} Hz (last {N} re-sends averaged {Avg:F2} ms, budget {B:F1} ms).", LogPrefix, throttle.Hz, window.Count, window.Average, Settings.FrameBudgetMs);
                window.Reset();
            }
            if ((now - lastSummaryStamp) / (double)Stopwatch.Frequency >= SummaryEverySeconds)
            {
                lastSummaryStamp = now;
                LogSummary(cur.Id, "running");
            }
        }
    }

    private void EndAnimation()
    {
        wasAnimating = false;
        LogSummary(plugin.Current?.Id ?? "(none)", "stopped");
    }

    private void LogSummary(string sceneId, string state)
    {
        log.Information("{P} {State} scene='{S}' at {Hz:F1} Hz. Frame interval, animation ON: {On}. Frame interval, animation OFF (same Scene, paused): {Off}. Whole-stage re-send: {Re}.",
            LogPrefix, state, sceneId, throttle.Hz, frameOn, frameOff, resend);
    }

    // ---- measurement run ----

    private void AdvanceMeasure(long now, bool animating)
    {
        var seconds = (now - measureStart) / (double)Stopwatch.Frequency;
        if (measure == MeasurePhase.Off && seconds >= MeasureSecondsPerPhase)
        {
            measure = MeasurePhase.On;
            measureStart = now;
            resend.Reset(); window.Reset();
            log.Information("{P} measure: animation off done ({Off}); switching it on.", LogPrefix, frameOff);
        }
        else if (measure == MeasurePhase.On && seconds >= MeasureSecondsPerPhase)
            FinishMeasure("done");
    }

    private void FinishMeasure(string how)
    {
        var scene = plugin.Current?.Id ?? "(none)";
        LastMeasure = $"Frame interval animation OFF: {frameOff}. ON: {frameOn}. Whole-stage re-send: {resend}. Rate {throttle.Hz:F1} Hz.";
        log.Information("{P} measure {How} scene='{S}': {R}", LogPrefix, how, scene, LastMeasure);
        measure = MeasurePhase.None;
        Settings.PlayAnimation = measureRestorePlay;
    }
}

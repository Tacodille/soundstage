// No Dalamud types in this file.
using System.Reflection;
using System.Runtime.Loader;

namespace Soundstage.Face;

/// <summary>
/// The Face tab's services in one place: the helper folder, the pinned file list, the analyser and the background download. Built by the tab
/// on first draw, so no shared file (Plugin.cs, Studio.cs) needs a line for it. It stops the helper when the plugin unloads, through the
/// plugin's own load context, because the tab has no dispose call of its own.
/// </summary>
public sealed class FaceServices : IDisposable
{
    public const string ManifestResource = "Soundstage.face-pack.json";

    private readonly Action<string> log;
    private CancellationTokenSource? cts;
    private Task<FaceInstallResult>? installTask;

    public FaceServices(string configDirectory, FaceSettings settings, Action<string> log, IFileFetcher? fetcher = null, FacePackManifest? manifest = null)
    {
        this.log = log;
        Settings = settings;
        Layout = new FacePackLayout(configDirectory);
        Fetcher = fetcher ?? new HttpFileFetcher();
        var problem = "";
        Manifest = manifest ?? LoadManifest(out problem);
        ManifestProblem = problem;
        Analyser = new HelperFaceAnalyser(() => Manifest != null && Layout.IsInstalled(Manifest.HelperVersion),
            () => RealHelperProcess.Start(Layout, out var err) ?? LogNull(err), log, settings.IdleSeconds, () => PoseInstalled);
        PoseManifest = LoadPoseManifest();
        try { AssemblyLoadContext.GetLoadContext(typeof(FaceServices).Assembly)!.Unloading += _ => Dispose(); }
        catch (Exception) { /* a host without load contexts: Dispose is still called by whoever made this */ }
    }

    public FaceSettings Settings { get; }
    public FacePackLayout Layout { get; }
    public IFileFetcher Fetcher { get; }
    public FacePackManifest? Manifest { get; }
    public string ManifestProblem { get; private set; }
    public HelperFaceAnalyser Analyser { get; }
    public FaceInstallProgress Progress { get; } = new();

    /// <summary>Part 18: the body pose model, a second small download (9 MB) that needs the face helper first.</summary>
    public PosePackManifest? PoseManifest { get; }
    public FaceInstallProgress PoseProgress { get; } = new();
    public string LastPoseMessage { get; private set; } = "";
    private Task<FaceInstallResult>? poseTask;
    private CancellationTokenSource? poseCts;

    public bool PoseInstalled => Installed && PosePackInstaller.Installed(PoseManifest, Layout);
    public bool PoseInstalling => poseTask is { IsCompleted: false };

    private static PosePackManifest? LoadPoseManifest()
    {
        try
        {
            using var s = typeof(FaceServices).Assembly.GetManifestResourceStream(PosePackManifest.ManifestResource);
            if (s == null) return null;
            using var r = new StreamReader(s);
            return PosePackManifest.TryParse(r.ReadToEnd(), out _);
        }
        catch (Exception) { return null; }
    }

    /// <summary>Starts the body model download on a background task. False (with the reason in <see cref="LastPoseMessage"/>) if it cannot start.</summary>
    public bool StartPoseInstall()
    {
        if (PoseInstalling) return false;
        if (PoseManifest == null) { LastPoseMessage = "The body model list is missing from this build."; return false; }
        if (!Installed) { LastPoseMessage = "Download the face helper first (Face tab). The body model runs inside it."; return false; }
        var files = EmbeddedHelperFiles();
        if (files.Count < 2) { LastPoseMessage = "The helper's own files are missing from this build."; return false; }
        Analyser.Dispose();   // so the next start carries the new model and the new scripts
        poseCts = new CancellationTokenSource();
        LastPoseMessage = "";
        var manifest = PoseManifest;
        poseTask = Task.Run(() =>
        {
            var r = PosePackInstaller.Install(manifest, Layout, Fetcher, files, PoseProgress, log, poseCts.Token);
            LastPoseMessage = r.Ok ? "Body model installed." : r.Reason;
            return r;
        });
        return true;
    }

    public void CancelPoseInstall() => poseCts?.Cancel();

    public bool RemovePose()
    {
        Analyser.Dispose();
        var ok = PosePackInstaller.Remove(Layout, out var why);
        LastPoseMessage = ok ? "Body model removed." : why;
        return ok;
    }
    public string LastInstallMessage { get; private set; } = "";

    public bool Installed => Manifest != null && Layout.IsInstalled(Manifest.HelperVersion);
    public bool Installing => installTask is { IsCompleted: false };

    private IHelperProcess? LogNull(string err)
    {
        log("[Soundstage/FACE] " + err);
        return null;
    }

    private FacePackManifest? LoadManifest(out string problem)
    {
        problem = "";
        try
        {
            using var s = typeof(FaceServices).Assembly.GetManifestResourceStream(ManifestResource);
            if (s == null) { problem = "The face helper list is missing from this build."; return null; }
            using var r = new StreamReader(s);
            var m = FacePackManifest.TryParse(r.ReadToEnd(), out problem);
           
            return m;
        }
        catch (Exception e) { problem = e.Message; return null; }
    }

    /// <summary>The helper's own two source files, read from the dll, to write beside the downloaded Python.</summary>
    public static Dictionary<string, string> EmbeddedHelperFiles()
    {
        var d = new Dictionary<string, string>();
        var asm = typeof(FaceServices).Assembly;
        foreach (var name in new[] { "helper.py", "face_core.py" })
        {
            using var s = asm.GetManifestResourceStream("Soundstage.face." + name);
            if (s == null) continue;
            using var r = new StreamReader(s);
            d[name] = r.ReadToEnd();
        }
        return d;
    }

    /// <summary>Starts the download on a background task. Returns false (with the reason in <see cref="LastInstallMessage"/>) if it cannot start.</summary>
    public bool StartInstall()
    {
        if (Installing) return false;
        if (Manifest == null) { LastInstallMessage = ManifestProblem; return false; }
        var files = EmbeddedHelperFiles();
        if (files.Count < 2) { LastInstallMessage = "The helper's own files are missing from this build."; return false; }
        Analyser.Dispose();
        cts = new CancellationTokenSource();
        LastInstallMessage = "";
        installTask = Task.Run(() =>
        {
            var r = FacePackInstaller.Install(Manifest, Layout, Fetcher, files, Progress, log, cts.Token);
            LastInstallMessage = r.Ok ? "Face helper installed." : r.Reason;
            return r;
        });
        return true;
    }

    public void CancelInstall() => cts?.Cancel();

    public bool Remove()
    {
        Analyser.Dispose();
        var ok = FacePackInstaller.Remove(Layout, out var why);
        LastInstallMessage = ok ? "Face helper removed." : why;
        return ok;
    }

    public void Dispose()
    {
        try { cts?.Cancel(); } catch (Exception) { }
        try { poseCts?.Cancel(); } catch (Exception) { }
        Analyser.Dispose();
    }
}

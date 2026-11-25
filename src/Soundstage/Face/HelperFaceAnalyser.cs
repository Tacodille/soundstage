// No Dalamud types in this file.
using System.Diagnostics;
using System.Text.Json;

namespace Soundstage.Face;

/// <summary>A running helper, as the analyser sees it: lines in, lines out. The self-check swaps in a fake.</summary>
public interface IHelperProcess : IDisposable
{
    bool HasExited { get; }
    void WriteLine(string line);

    /// <summary>The next line, or null when the helper closed its output or <paramref name="timeoutMs"/> passed.</summary>
    string? ReadLine(int timeoutMs);

    void Kill();
}

/// <summary>Starts the real helper: the downloaded python.exe running helper.py. No window, no sockets, stderr drained and dropped.</summary>
public sealed class RealHelperProcess : IHelperProcess
{
    private readonly Process proc;

    private RealHelperProcess(Process p) => proc = p;

    public static IHelperProcess? Start(FacePackLayout layout, out string error)
    {
        error = "";
        try
        {
            var psi = new ProcessStartInfo(layout.PythonExe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = layout.HelperDir,
            };
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(layout.HelperScript);
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(layout.ModelFile);
            if (File.Exists(layout.PoseModelFile))
            {
                psi.ArgumentList.Add("--pose-model");
                psi.ArgumentList.Add(layout.PoseModelFile);
            }
            var p = Process.Start(psi);
            if (p == null) { error = "Windows would not start the face helper."; return null; }
            p.ErrorDataReceived += (_, _) => { };   // drained so a chatty model can never fill the pipe and stall
            p.BeginErrorReadLine();
            return new RealHelperProcess(p);
        }
        catch (Exception e)
        {
            error = "Windows would not start the face helper: " + e.Message;
            return null;
        }
    }

    public bool HasExited { get { try { return proc.HasExited; } catch (Exception) { return true; } } }

    public void WriteLine(string line)
    {
        proc.StandardInput.WriteLine(line);
        proc.StandardInput.Flush();
    }

    public string? ReadLine(int timeoutMs)
    {
        var t = proc.StandardOutput.ReadLineAsync();
        return t.Wait(timeoutMs) ? t.Result : null;
    }

    public void Kill()
    {
        try { if (!proc.HasExited) proc.Kill(true); } catch (Exception) { }
    }

    public void Dispose()
    {
        Kill();
        proc.Dispose();
    }
}

/// <summary>
/// <see cref="IFaceAnalyser"/> over the downloaded helper process (Option B in docs/face-model-decision.md). The process is started on
/// first use and left running for <see cref="IdleSeconds"/>, so a second picture costs milliseconds, not a start. It is killed when idle,
/// when the plugin unloads (the helper also exits by itself when its input closes). Every failure is a result, never an exception.
/// </summary>
public sealed class HelperFaceAnalyser : IFaceAnalyser, IDisposable
{
    public const int StartTimeoutMs = 90_000;
    public const int AnalyseTimeoutMs = 30_000;

    private readonly Func<IHelperProcess?> launch;
    private readonly Func<bool> installed;
    private readonly Action<string> log;
    private readonly object gate = new();
    private IHelperProcess? proc;
    private int nextId = 1;
    private Timer? idleTimer;
    private string status = "";

    private readonly Func<bool>? poseInstalled;

    public HelperFaceAnalyser(Func<bool> installed, Func<IHelperProcess?> launch, Action<string> log, int idleSeconds = 120, Func<bool>? poseInstalled = null)
    {
        this.poseInstalled = poseInstalled;
        this.installed = installed;
        this.launch = launch;
        this.log = log;
        IdleSeconds = idleSeconds;
    }

    public int IdleSeconds { get; set; }

    public bool CanTry => installed();

    public string Status => !installed() ? "The face helper is not downloaded yet." : status.Length > 0 ? status : "Ready (starts when you analyse a picture).";

    public bool Running { get { lock (gate) return proc is { HasExited: false }; } }

    public FaceResult Analyse(string imagePath)
    {
        var line = Ask(imagePath, null, out var why);
        return line == null ? FaceResult.Unavailable(why) : FaceReplyParser.Parse(line);
    }

    /// <summary>Part 18: whether the body pose model has been downloaded (the helper then starts with it).</summary>
    public bool PoseCanTry => installed() && (poseInstalled?.Invoke() ?? false);

    /// <summary>Part 18: the body landmarks and outline of the first person in the picture. Same rules as <see cref="Analyse"/>: blocks, never throws.</summary>
    public BodyPoseResult AnalysePose(string imagePath)
    {
        if (!PoseCanTry) return BodyPoseResult.Unavailable("The body pose model is not downloaded yet. Use the download button in the Expression tab's body section.");
        var line = Ask(imagePath, "pose", out var why);
        return line == null ? BodyPoseResult.Unavailable(why) : BodyPoseResult.Parse(line);
    }

    /// <summary>Sends one request and returns the helper's reply line, or null with <paramref name="failure"/> as a sentence for the tab.</summary>
    private string? Ask(string imagePath, string? mode, out string failure)
    {
        failure = "";
        try
        {
            if (!installed()) { failure = "The face helper is not downloaded yet. Use the download button on the Face tab."; return null; }
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) { failure = "That picture file was not found."; return null; }

            lock (gate)
            {
                idleTimer?.Dispose();
                idleTimer = null;
                var started = EnsureStarted(out var why);
                if (!started) { failure = why; return null; }

                var id = nextId++;
                var fields = new Dictionary<string, object> { ["id"] = id, ["image"] = Path.GetFullPath(imagePath) };
                if (mode != null) fields["mode"] = mode;
                var req = JsonSerializer.Serialize(fields);
                var sw = Stopwatch.StartNew();
                try { proc!.WriteLine(req); }
                catch (Exception) { Drop("The face helper stopped answering."); failure = "The face helper had stopped. Try again."; return null; }

                string? line;
                // Skip any stray line that is not this request's answer (a late reply from a request that timed out cannot happen: a timeout kills the process).
                while ((line = proc.ReadLine(AnalyseTimeoutMs)) != null)
                {
                    var reply = ReadId(line);
                    if (reply == id || reply == null) break;
                }
                if (line == null)
                {
                    Drop(proc.HasExited ? "The face helper closed unexpectedly." : "The face helper took too long and was stopped.");
                    failure = status;
                    return null;
                }
                log($"[Soundstage/FACE] {(mode ?? "face")} analysed in {sw.ElapsedMilliseconds} ms.");
                status = "Ready.";
                return line;
            }
        }
        catch (Exception e)
        {
            log($"[Soundstage/FACE] analyse failed: {e}");
            failure = "The face analysis failed: " + e.Message;
            return null;
        }
        finally
        {
            lock (gate)
            {
                if (proc != null && idleTimer == null && IdleSeconds > 0)
                    idleTimer = new Timer(_ => Stop("idle"), null, IdleSeconds * 1000, Timeout.Infinite);
            }
        }
    }

    private bool EnsureStarted(out string why)
    {
        why = "";
        if (proc is { HasExited: false }) return true;
        proc?.Dispose();
        proc = null;
        var sw = Stopwatch.StartNew();
        var p = launch();
        if (p == null) { status = "Could not start the face helper."; why = status; return false; }
        var ready = p.ReadLine(StartTimeoutMs);
        if (ready == null)
        {
            p.Kill(); p.Dispose();
            status = "The face helper did not start in time.";
            why = status;
            return false;
        }
        try
        {
            using var doc = JsonDocument.Parse(ready);
            if (!doc.RootElement.TryGetProperty("ready", out var r) || r.ValueKind != JsonValueKind.True)
            {
                var reason = doc.RootElement.TryGetProperty("reason", out var rs) ? rs.GetString() : "it said it was not ready";
                p.Kill(); p.Dispose();
                status = "The face helper failed to start: " + reason;
                why = status;
                return false;
            }
        }
        catch (JsonException)
        {
            p.Kill(); p.Dispose();
            status = "The face helper sent something unexpected while starting.";
            why = status;
            return false;
        }
        proc = p;
        log($"[Soundstage/FACE] helper ready after {sw.ElapsedMilliseconds} ms.");
        return true;
    }

    private static int? ReadId(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.TryGetProperty("id", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : null;
        }
        catch (JsonException) { return null; }
    }

    private void Drop(string reason)
    {
        status = reason;
        log($"[Soundstage/FACE] {reason}");
        try { proc?.Kill(); proc?.Dispose(); } catch (Exception) { }
        proc = null;
    }

    private void Stop(string why)
    {
        lock (gate)
        {
            if (proc == null) return;
            try
            {
                if (!proc.HasExited) { try { proc.WriteLine("{\"quit\":true}"); } catch (Exception) { } }
                proc.Kill();
                proc.Dispose();
            }
            catch (Exception) { }
            proc = null;
            idleTimer?.Dispose();
            idleTimer = null;
            log($"[Soundstage/FACE] helper stopped ({why}).");
        }
    }

    /// <summary>Stops the helper now (the Face tab's button, plugin unload).</summary>
    public void Dispose() => Stop("dispose");
}

// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Diagnostics;

namespace Soundstage.Capture;

/// <summary>
/// One call, several routes behind it. Routes are tried in order; each gets a timeout; the first that produces a readable image wins.
/// The output is written to a temporary name beside the target and renamed, so a half-written file never has the final name.
/// </summary>
public sealed class CaptureService
{
    public const string LogPrefix = "[Soundstage/WORK]";

    private readonly Func<IReadOnlyList<ICaptureRoute>> routes;
    private readonly Func<TimeSpan> timeout;
    private readonly Action<string> log;

    public CaptureService(Func<IReadOnlyList<ICaptureRoute>> routes, Func<TimeSpan> timeout, Action<string> log)
    {
        this.routes = routes;
        this.timeout = timeout;
        this.log = log;
    }

    /// <summary>A timestamped name that is legal on Windows (no colon).</summary>
    public static string DefaultName(DateTime now, string prefix = "Soundstage") => $"{prefix} {now:yyyy-MM-dd HH-mm-ss}.png";

    public async Task<CaptureResult> CaptureAsync(string destination, CancellationToken cancel = default)
    {
        var started = Stopwatch.StartNew();
        var attempts = new List<string>();
        var list = routes();
        if (list.Count == 0) return Fail("No capture route is switched on.", started);
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".";
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex) { return Fail("Could not make the capture folder: " + ex.Message, started); }

        // ".part.png" because the add-on route insists on a .png name.
        var temp = Path.ChangeExtension(Path.GetFullPath(destination), null) + ".part.png";
        foreach (var route in list)
        {
            cancel.ThrowIfCancellationRequested();
            var routeTimer = Stopwatch.StartNew();
            DeleteQuietly(temp);
            RouteResult result;
            var limit = timeout();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            linked.CancelAfter(limit);
            try
            {
                var run = route.CaptureAsync(temp, linked.Token);
                // A route that ignores its token must not hold the sheet up, so the wait itself has the deadline.
                var finished = await Task.WhenAny(run, Task.Delay(limit, cancel)).ConfigureAwait(false);
                result = finished == run ? await run.ConfigureAwait(false) : RouteResult.Fail("timed out");
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { DeleteQuietly(temp); throw; }
            catch (OperationCanceledException) { result = RouteResult.Fail("timed out"); }
            catch (Exception ex) { result = RouteResult.Fail(ex.Message); }

            if (result.Ok && !PngCheck.IsReadableImage(temp, out var why)) result = RouteResult.Fail("the file it wrote was not a readable image (" + why + ")");
            if (result.Ok)
            {
                try
                {
                    File.Move(temp, destination, overwrite: true);
                    log($"{LogPrefix} capture via {route.Id} ok in {routeTimer.ElapsedMilliseconds} ms -> {destination}");
                    return new CaptureResult(true, destination, route.Id, started.ElapsedMilliseconds, "");
                }
                catch (Exception ex) { result = RouteResult.Fail("could not rename the finished file: " + ex.Message); }
            }
            DeleteQuietly(temp);
            attempts.Add($"{route.Label}: {result.Message}");
            log($"{LogPrefix} capture via {route.Id} failed after {routeTimer.ElapsedMilliseconds} ms: {result.Message}");
        }
        return Fail("Every route failed. " + string.Join(" | ", attempts), started);
    }

    private CaptureResult Fail(string message, Stopwatch timer)
    {
        log($"{LogPrefix} capture failed: {message}");
        return new CaptureResult(false, "", "", timer.ElapsedMilliseconds, message);
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Reads just enough of a file to tell a real image from an empty or truncated one.</summary>
public static class PngCheck
{
    public static bool IsReadableImage(string path, out string why)
    {
        why = "";
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { why = "no file"; return false; }
            if (info.Length < 64) { why = "only " + info.Length + " bytes"; return false; }
            using var s = File.OpenRead(path);
            using var img = System.Drawing.Image.FromStream(s, useEmbeddedColorManagement: false, validateImageData: true);
            if (img.Width < 2 || img.Height < 2) { why = "image is " + img.Width + "x" + img.Height; return false; }
            return true;
        }
        catch (Exception ex) { why = ex.Message; return false; }
    }
}

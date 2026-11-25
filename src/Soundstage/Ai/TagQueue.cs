// No Dalamud types in the Ai folder: the offline self-check compiles these files as shared sources.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Soundstage.Poses;

namespace Soundstage.Ai;

/// <summary>
/// The daily allowances the queue stops at. The figures are the ones written, with their date, in docs/ai-providers.md (a self-check reads
/// that file and fails if these drift from it). A figure the user typed on the AI tab wins. Where the docs give no figure (Gemini's page named
/// models that did not match the plugin's defaults) the answer is "unknown", and the queue relies on the provider's own rate-limit reply.
/// </summary>
public static class TagLimits
{
    /// <summary>OpenRouter, models whose id ends ":free", fewer than 10 credits ever bought. Read 21/11/2026 at openrouter.ai/docs/api-reference/limits.</summary>
    public const int OpenRouterFreeDaily = 50;

    /// <summary>The same page: requests a minute for ":free" models.</summary>
    public const int OpenRouterFreePerMinute = 20;

    public static int? DailyLimit(string providerId, string model, int userLimit)
    {
        if (userLimit > 0) return userLimit;
        if (providerId == OpenAiCompatProvider.OpenRouterId && model.EndsWith(":free", StringComparison.Ordinal)) return OpenRouterFreeDaily;
        return null;
    }

    /// <summary>Milliseconds to leave between two requests so a per-minute limit is not hit (0 when there is none known).</summary>
    public static int MinGapMs(string providerId, string model) =>
        providerId == OpenAiCompatProvider.OpenRouterId && model.EndsWith(":free", StringComparison.Ordinal) ? 60_000 / OpenRouterFreePerMinute : 0;
}

/// <summary>What the queue needs from the AI layer, so the checks can stand in for it with a fake clock.</summary>
public interface IPoseTagBackend
{
    string ProviderId { get; }
    string ProviderName { get; }
    /// <summary>Requests sent today to this provider (all Soundstage features share it).</summary>
    int UsedToday();
    /// <summary>The day's allowance, or null when no figure is known.</summary>
    int? DailyLimit();
    int MinGapMs();
    /// <summary>Today's date as the counter reckons it (a fake clock in the checks).</summary>
    string Today();
    Task<AiResult<JsonObject>> Analyse(byte[] png, JsonObject schema, string instruction, CancellationToken ct);
}

/// <summary>The real thing: <see cref="AiService"/> does the consent check, the key check and the counting.</summary>
public sealed class AiServiceBackend : IPoseTagBackend
{
    private readonly AiService ai;
    private readonly Func<DateTime> now;

    public AiServiceBackend(AiService ai, Func<DateTime>? now = null)
    {
        this.ai = ai;
        this.now = now ?? (() => DateTime.Now);
    }

    public string ProviderId => ai.ProviderId;
    public string ProviderName => AiService.NameOf(ai.ProviderId);
    public int UsedToday() => ai.UsedToday(ai.ProviderId);
    public int? DailyLimit() => TagLimits.DailyLimit(ai.ProviderId, ai.Settings.For(ai.ProviderId).Model, ai.Settings.For(ai.ProviderId).DailyLimit);
    public int MinGapMs() => TagLimits.MinGapMs(ai.ProviderId, ai.Settings.For(ai.ProviderId).Model);
    public string Today() => now().ToString("yyyy-MM-dd");
    public Task<AiResult<JsonObject>> Analyse(byte[] png, JsonObject schema, string instruction, CancellationToken ct) =>
        ai.AnalyseImage(png, "image/png", schema, instruction, ct);
}

public sealed record QueueItem(string Hash, string Name, string ThumbPath);

public enum QueueState { Idle, Running, PausedBudget, PausedError, Finished, Cancelled }

/// <summary>
/// "Tag with AI": sends each pose's thumbnail, one at a time, and writes the tags to the store as each reply arrives. It stops before the day's
/// allowance is used (so the 51st request on a 50-a-day provider is never sent), says when it can go on, and goes on when the day has changed
/// and the user presses the button again. Which poses are already done is kept in <c>pose-tag-queue.json</c>, so a second run skips them.
/// </summary>
public sealed class TagQueue
{
    public const string StateFile = "pose-tag-queue.json";
    private const int MaxSkippedInARow = 5;

    private readonly IPoseTagBackend backend;
    private readonly PoseTags tags;
    private readonly PoseVocabulary vocab;
    private readonly string statePath;
    private readonly Action<string> log;
    private readonly Func<int, CancellationToken, Task> delay;
    private readonly HashSet<string> done = new(StringComparer.Ordinal);
    private readonly List<QueueItem> pending = new();
    private string pausedOn = "";

    public TagQueue(IPoseTagBackend backend, PoseTags tags, PoseVocabulary vocab, string statePath, Action<string> log, Func<int, CancellationToken, Task>? delay = null)
    {
        this.backend = backend;
        this.tags = tags;
        this.vocab = vocab;
        this.statePath = statePath;
        this.log = log;
        this.delay = delay ?? ((ms, ct) => Task.Delay(ms, ct));
        LoadState();
    }

    public QueueState State { get; private set; } = QueueState.Idle;
    public string Status { get; private set; } = "";
    public int Total { get; private set; }
    public int Processed { get; private set; }
    public int Tagged { get; private set; }
    public int Skipped { get; private set; }
    public int Remaining => pending.Count;
    public bool Busy => State == QueueState.Running;
    public int DoneCount => done.Count;
    public bool IsDone(string hash) => done.Contains(hash);

    /// <summary>True when the queue stopped for today's allowance and the day has changed since.</summary>
    public bool CanResume => State == QueueState.PausedBudget && pausedOn != backend.Today();

    /// <summary>Adds poses that are not already tagged by this queue or waiting in it. Returns how many were added.</summary>
    public int Enqueue(IEnumerable<QueueItem> items, bool again = false)
    {
        var added = 0;
        foreach (var it in items)
        {
            if (it.Hash.Length == 0) continue;
            if (!again && done.Contains(it.Hash)) continue;
            if (pending.Any(p => p.Hash == it.Hash)) continue;
            pending.Add(it);
            added++;
        }
        Total = Processed + pending.Count;
        return added;
    }

    public void Clear()
    {
        if (Busy) return;
        pending.Clear();
        Total = Processed = Tagged = Skipped = 0;
        State = QueueState.Idle;
        Status = "";
    }

    /// <summary>Goes through the waiting poses until they run out, the allowance runs out, or something needs the user.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        if (Busy) return;
        if (pending.Count == 0) { State = QueueState.Finished; Status = "Nothing to tag."; return; }
        State = QueueState.Running;
        Status = "Starting...";
        var skippedInARow = 0;
        var firstRequest = true;
        try
        {
            while (pending.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var limit = backend.DailyLimit();
                var used = backend.UsedToday();
                if (limit is { } l && used >= l)
                {
                    Pause(QueueState.PausedBudget, $"Stopped for today: {used} of {l} requests used on {backend.ProviderName}. {pending.Count} pose(s) are waiting; press Tag with AI again tomorrow.");
                    return;
                }

                if (!firstRequest && backend.MinGapMs() > 0) await delay(backend.MinGapMs(), ct).ConfigureAwait(false);
                firstRequest = false;

                var item = pending[0];
                var png = ReadThumb(item);
                if (png == null)
                {
                    Skip(item, "its thumbnail is missing or unreadable");
                    if (++skippedInARow >= MaxSkippedInARow) { Pause(QueueState.PausedError, "Stopped: several poses in a row had no readable thumbnail. Make thumbnails again first."); return; }
                    continue;
                }

                Status = $"Tagging {item.Name} ({Processed + 1} of {Total})...";
                var result = await backend.Analyse(png, PoseTagger.Schema(), PoseTagger.Instruction(vocab), ct).ConfigureAwait(false);
                if (!result.Ok)
                {
                    var f = result.Failure!;
                    var plain = AiMessages.For(f, backend.ProviderName);
                    switch (f.Error)
                    {
                        case AiError.RateLimit:
                            Pause(QueueState.PausedBudget, "Stopped: " + plain + $" {pending.Count} pose(s) are waiting.");
                            return;
                        case AiError.NoKey or AiError.ConsentNeeded or AiError.BadKey or AiError.RegionUnavailable or AiError.ModelNotFound or AiError.NotConfigured
                            or AiError.NoNetwork or AiError.Timeout or AiError.ServerError:
                            Pause(QueueState.PausedError, "Stopped: " + plain);
                            return;
                        default:
                            Skip(item, f.Error.ToString());
                            if (++skippedInARow >= MaxSkippedInARow) { Pause(QueueState.PausedError, "Stopped after several replies in a row could not be read. " + plain); return; }
                            continue;
                    }
                }

                skippedInARow = 0;
                var read = PoseTagger.Read(result.Value!, vocab, log);
                var all = read.Tags.Concat(read.Free).ToList();
                foreach (var t in all) tags.Add(item.Hash, t);
                if (all.Count > 0)
                {
                    tags.Save();
                    Tagged++;
                }
                done.Add(item.Hash);
                pending.RemoveAt(0);
                Processed++;
                SaveState();
                log($"{PoseTagger.LogPrefix} tagged '{item.Name}' with {all.Count} tag(s).");
            }
            State = QueueState.Finished;
            Status = $"Done: {Tagged} pose(s) tagged" + (Skipped > 0 ? $", {Skipped} skipped" : "") + ".";
        }
        catch (OperationCanceledException)
        {
            State = QueueState.Cancelled;
            Status = $"Cancelled with {pending.Count} pose(s) left. Tags so far are kept.";
        }
        catch (Exception ex)
        {
            State = QueueState.PausedError;
            Status = "Stopped: " + ex.GetType().Name + " while tagging. Check dalamud.log.";
            log($"{PoseTagger.LogPrefix} tag queue threw {ex.GetType().Name}");
        }
    }

    private void Pause(QueueState state, string status)
    {
        State = state;
        pausedOn = backend.Today();
        Status = status;
        log($"{PoseTagger.LogPrefix} tag queue paused: {status}");
    }

    private void Skip(QueueItem item, string why)
    {
        pending.Remove(item);
        Skipped++;
        Processed++;
        log($"{PoseTagger.LogPrefix} skipped '{item.Name}': {why}.");
    }

    private static byte[]? ReadThumb(QueueItem item)
    {
        try { return File.Exists(item.ThumbPath) ? File.ReadAllBytes(item.ThumbPath) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---- which poses are done ----

    private sealed class StateShape
    {
        [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = 1;
        [JsonPropertyName("done")] public List<string> Done { get; set; } = new();
    }

    private void LoadState()
    {
        if (statePath.Length == 0 || !File.Exists(statePath)) return;
        try
        {
            var shape = JsonSerializer.Deserialize<StateShape>(File.ReadAllText(statePath, Encoding.UTF8));
            foreach (var h in shape?.Done ?? new()) done.Add(h);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log($"{PoseTagger.LogPrefix} {StateFile} could not be read ({ex.Message}); starting with no poses marked done.");
        }
    }

    private void SaveState()
    {
        if (statePath.Length == 0) return;
        try
        {
            var dir = Path.GetDirectoryName(statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new StateShape { Done = done.OrderBy(h => h, StringComparer.Ordinal).ToList() }), new UTF8Encoding(false));
            File.Move(tmp, statePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"{PoseTagger.LogPrefix} could not save {StateFile}: {ex.Message}");
        }
    }
}

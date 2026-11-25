using System.Numerics;
using System.Runtime.CompilerServices;
using Dalamud.Bindings.ImGui;
using Soundstage.Ai;
using Soundstage.Tabs;
using Soundstage.Workflow;

namespace Soundstage.Poses;

/// <summary>
/// Everything Part 14 owns that touches the game or ImGui: the thumbnail job's hooks into the shelf and the stage, the tag queue's run on a
/// worker thread, and the "Thumbnails and AI tags" panel under the shelf. Found from a Studio with <see cref="For"/> (no new Studio property).
/// The thumbnail job ticks from the shelf's framework tick, so it keeps going with the Soundstage windows hidden.
/// </summary>
public sealed class PoseTagHost
{
    private static readonly ConditionalWeakTable<Studio, PoseTagHost> Hosts = new();

    public static PoseTagHost For(Studio studio) => Hosts.GetValue(studio, s => new PoseTagHost(s));

    private const string AllPacks = "(all packs)";

    private readonly Studio studio;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly ThumbnailJob job;
    private Scene? savedScene;
    private string savedPalette = "";
    private TagQueue? queue;
    private PoseVocabulary? vocabulary;
    private CancellationTokenSource? queueCts;
    private string pack = AllPacks;
    private string message = "";

    private PoseTagHost(Studio studio)
    {
        this.studio = studio;
        var work = WorkflowHost.For(studio);
        job = new ThumbnailJob(work.Hider, Cache(), work.Capture.CaptureAsync, work.InGpose, () => clock.ElapsedMilliseconds,
            () => work.Settings.SettleMs, () => work.Settings.HideSettleMs, Begin, Apply, End, Log);
        studio.Poses.ExtraTick = job.Tick;
    }

    private ThumbnailCache Cache() => studio.Poses.Cache ?? new ThumbnailCache(Path.Combine(studio.Plugin.ConfigDirectory, ThumbnailCache.FolderName));

    private void Log(string line) => studio.Log.Information("{Line:l}", line);

    // ---- thumbnail job hooks ----

    private string? Begin()
    {
        var work = WorkflowHost.For(studio);
        if (work.Booth.Busy || work.Sheet.Busy) return "The photo booth or the contact sheet is running. Wait for it to finish.";
        var why = studio.Poses.BeginThumbRun();
        if (why != null) return why;
        try
        {
            savedScene = studio.Current?.Clone();
            savedPalette = studio.Plugin.SelectedPalette;
            var stage = ThumbnailStage.Build(studio.Presets);
            if (!studio.Plugin.Apply(stage))
            {
                var fail = "Could not put the plain grey stage up: " + studio.Plugin.Message;
                studio.Poses.EndThumbRun();
                return fail;
            }
        }
        catch (Exception ex)
        {
            studio.Poses.EndThumbRun();
            return "Could not put the plain grey stage up: " + ex.Message;
        }
        return null;
    }

    private bool Apply(ThumbItem item)
    {
        var entry = studio.Poses.Entries.FirstOrDefault(e => string.Equals(e.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        return entry != null && studio.Poses.LoadForThumb(entry);
    }

    private void End()
    {
        var problems = new List<string>();
        var poseProblem = studio.Poses.EndThumbRun();
        if (poseProblem != null) problems.Add(poseProblem);
        try
        {
            if (savedScene != null) studio.Plugin.RestoreWorking(savedScene, savedPalette);
        }
        catch (Exception ex) { problems.Add("Could not put your Scene back: " + ex.Message); }
        savedScene = null;
        studio.Poses.ThumbnailsChanged();
        if (problems.Count > 0) throw new InvalidOperationException(string.Join(" ", problems));
    }

    // ---- tag queue ----

    private PoseVocabulary Vocabulary() =>
        vocabulary ??= PoseVocabulary.Load(Path.Combine(studio.Plugin.AssemblyDirectory, PoseVocabulary.ShippedFile),
            Path.Combine(studio.Plugin.ConfigDirectory, PoseVocabulary.UserFile));

    private TagQueue? Queue()
    {
        if (queue != null) return queue;
        var tags = studio.Poses.Tags;
        if (tags == null) return null;
        var vocab = Vocabulary();
        if (vocab.Note.Length > 0) Log($"{PoseTagger.LogPrefix} pose vocabulary: {vocab.Note}");
        queue = new TagQueue(new AiServiceBackend(studio.Ai), tags, vocab, Path.Combine(studio.Plugin.ConfigDirectory, TagQueue.StateFile), Log);
        return queue;
    }

    private IEnumerable<PoseEntry> Chosen() =>
        studio.Poses.Entries.Where(e => pack == AllPacks || string.Equals(e.Pack, pack, StringComparison.OrdinalIgnoreCase));

    private void StartTagging(TagQueue q)
    {
        var tags = studio.Poses.Tags!;
        var cache = Cache();
        var items = new List<QueueItem>();
        foreach (var e in Chosen())
        {
            var hash = tags.HashFor(e.Path);
            if (hash != null && cache.Has(hash)) items.Add(new QueueItem(hash, e.Name, cache.PathFor(hash)));
        }
        var added = q.Enqueue(items);
        if (added == 0) { message = items.Count == 0 ? "None of those poses has a thumbnail yet. Press Make thumbnails first." : "Every pose with a thumbnail in that list has already been tagged."; return; }
        message = "";
        queueCts?.Dispose();
        queueCts = new CancellationTokenSource();
        var token = queueCts.Token;
        _ = Task.Run(async () =>
        {
            try { await q.RunAsync(token).ConfigureAwait(false); }
            catch (Exception ex) { studio.Ai.Log.Info("pose tag queue threw " + ex.GetType().Name); }
        });
    }

    // ---- the panel ----

    public void DrawPanel(Studio s)
    {
        ImGui.Separator();
        if (!ImGui.CollapsingHeader("Thumbnails and AI tags##posetags")) return;

        var shelf = s.Poses;
        var packs = shelf.Entries.Select(e => e.Pack).Distinct().OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        if (pack != AllPacks && !packs.Contains(pack)) pack = AllPacks;
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("Pack##posetagpack", pack))
        {
            if (ImGui.Selectable(AllPacks, pack == AllPacks)) pack = AllPacks;
            foreach (var p in packs) if (ImGui.Selectable(p + "##packpick", p == pack)) pack = p;
            ImGui.EndCombo();
        }
        var chosen = Chosen().ToList();
        var cache = Cache();
        var withPicture = shelf.Tags == null ? 0 : chosen.Count(e => shelf.Tags.HashFor(e.Path) is { } h && cache.Has(h));
        ImGui.TextDisabled($"{chosen.Count} pose(s) in the list, {withPicture} with a thumbnail.");

        DrawThumbnails(shelf, chosen);
        ImGui.Spacing();
        DrawTagging(s);
        if (message.Length > 0) ImGui.TextWrapped(message);
    }

    private void DrawThumbnails(PoseShelf shelf, List<PoseEntry> chosen)
    {
        ImGui.TextUnformatted("1. Thumbnails");
        ImGui.TextWrapped("Renders each pose on your own character in a plain grey studio (GPose only), then puts your pose, Scene and window back. Frame your character in GPose first; the camera is not moved.");
        if (job.Busy)
        {
            ImGui.ProgressBar(job.Total == 0 ? 0f : (float)job.Position / job.Total, new Vector2(220, 0), $"{job.Position} of {job.Total}");
            ImGui.SameLine();
            if (ImGui.Button("Cancel##thumbs")) job.Cancel();
        }
        else if (ImGui.Button("Make thumbnails##thumbs"))
        {
            var tags = shelf.Tags;
            var items = new List<ThumbItem>();
            if (tags != null)
                foreach (var e in chosen)
                    if (tags.HashFor(e.Path) is { } h) items.Add(new ThumbItem(h, e.Name, e.Path));
            job.Start(items);
        }
        if (job.Status.Length > 0) ImGui.TextWrapped(job.Status);
    }

    private void DrawTagging(Studio s)
    {
        ImGui.TextUnformatted("2. Tag with AI");
        var ai = s.Ai;
        var q = Queue();
        if (q == null) { ImGui.TextDisabled("The tag store is not loaded yet."); return; }
        ImGui.TextWrapped("Sends each thumbnail (256 pixels, your character in a pose) to your AI provider, one at a time, and adds the tags it picks from a fixed list. It stops before the day's free allowance runs out and carries on when you press the button on a later day.");

        if (!ai.Keys.Has(ai.ProviderId))
        {
            ImGui.TextWrapped("You need a key first: open the AI tab, press Get a key, paste it in and press Test key.");
            if (ImGui.Button("Take me to the AI tab##posetags")) TabHost.RequestTab("AI");
            return;
        }
        if (!ai.HasConsent(ai.ProviderId))
        {
            ImGui.TextWrapped("Before the first thumbnail goes anywhere, read this:");
            ImGui.TextWrapped(ai.ConsentText(ai.ProviderId));
            if (ImGui.Button("Yes, that is fine for pictures of my character##posetagsconsent")) ai.GiveConsent(ai.ProviderId);
            return;
        }

        var backend = new AiServiceBackend(ai);
        var limit = backend.DailyLimit();
        ImGui.TextDisabled($"Today: {backend.UsedToday()} request(s) sent to {backend.ProviderName}" + (limit is { } l ? $" of {l}." : ". No daily figure is known for this provider; the queue stops when it says you are out."));

        if (q.Busy)
        {
            ImGui.ProgressBar(q.Total == 0 ? 0f : (float)q.Processed / q.Total, new Vector2(220, 0), $"{q.Processed} of {q.Total}");
            ImGui.SameLine();
            if (ImGui.Button("Cancel##tagq")) queueCts?.Cancel();
        }
        else
        {
            var label = q.CanResume ? "Continue tagging##tagq" : q.Remaining > 0 ? "Tag the rest##tagq" : "Tag with AI##tagq";
            if (ImGui.Button(label)) StartTagging(q);
            if (q.Remaining > 0 && q.State != QueueState.Running)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"{q.Remaining} waiting");
            }
        }
        if (q.Status.Length > 0) ImGui.TextWrapped(q.Status);
        if (q.DoneCount > 0) ImGui.TextDisabled($"{q.DoneCount} pose(s) done so far. Tags are on the shelf already; right-click a pose to change them.");
    }
}

/// <summary>"Poses that fit" for the selected Scene in the Scenes tab: poses whose tags overlap the Scene's pose tags, one click to load.</summary>
public static class PoseFitSection
{
    private static string note = "";

    public static void Draw(Studio studio, Scene? scene)
    {
        if (scene?.Pose is not { Tags.Count: > 0 } pose) return;
        var shelf = studio.Poses;
        var store = shelf.Tags;
        ImGui.Spacing();
        ImGui.TextUnformatted("Poses that fit");
        ImGui.SameLine();
        ImGui.TextDisabled("(" + string.Join(", ", pose.Tags) + ")");
        if (store == null) { ImGui.TextDisabled("The Poses tab has not loaded its tags yet. Open it once."); return; }

        var candidates = shelf.Entries
            .Select(e => new PoseCandidate(e.Path, e.Name, e.Pack, store.TagsFor(store.HashFor(e.Path))))
            .Where(c => c.Tags.Count > 0);
        var matches = Matching.Rank(pose.Tags, candidates);
        if (matches.Count == 0)
        {
            ImGui.TextDisabled(store.TaggedCount == 0 ? "No poses are tagged yet. Make thumbnails and Tag with AI on the Poses tab, or tag a few by hand." : "No tagged pose shares a tag with this Scene.");
            return;
        }
        foreach (var m in matches)
        {
            ImGui.PushID("fit|" + m.Pose.Key);
            if (ImGui.SmallButton("Load")) Load(shelf, m.Pose.Key);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{m.Pose.Name}  ({m.Pose.Pack})");
            ImGui.SameLine();
            ImGui.TextDisabled("shares " + string.Join(", ", m.Shared));
            ImGui.PopID();
        }
        if (note.Length > 0) ImGui.TextDisabled(note);
    }

    private static void Load(PoseShelf shelf, string path)
    {
        var entry = shelf.Entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
        if (entry == null) { note = "That pose is no longer in the shelf."; return; }
        note = shelf.LoadForScene(entry, out var msg) ? $"Loaded '{entry.Name}'." : msg;
    }
}

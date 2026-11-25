// Part 14: pose thumbnails and AI tags, offline. The provider is a fake HTTP handler, the clock is a variable, the "captures" are pictures
// made in memory. No real network, no real key, no game.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Soundstage;
using Soundstage.Ai;
using Soundstage.Capture;
using Soundstage.Poses;
using Soundstage.Workflow;

namespace Soundstage.SelfCheck;

public sealed class PoseTagChecks : CheckArea
{
    public override string Name => "Pose thumbnails and tags (offline)";
    public override int Order => 140;

    private const string Sentinel = "zz-posetag-sentinel-9d1f43-KEYMATERIAL";

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public Func<int, string> Reply { get; set; } = _ => "{}";
        public int Status { get; set; } = 200;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(Reply(Bodies.Count), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeProtector : IKeyProtector
    {
        public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain).Reverse().ToArray());
        public string? Unprotect(string stored) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(stored).Reverse().ToArray()); } catch (FormatException) { return null; } }
    }

    private sealed class Clock { public DateTime Now = new(2026, 11, 21, 10, 0, 0); }

    /// <summary>A fake backend with a settable allowance and a clock, for the queue rules that do not need HTTP.</summary>
    private sealed class FakeBackend : IPoseTagBackend
    {
        public readonly Clock Clock;
        public int Used;
        public string UsedDate;
        public int? Limit = 50;
        public Func<int, AiResult<JsonObject>> Reply = n => AiResult<JsonObject>.Success(Ok("standing"));
        public int Sent;
        public FakeBackend(Clock clock) { Clock = clock; UsedDate = Today(); }
        public string ProviderId => "openrouter";
        public string ProviderName => "OpenRouter";
        public int UsedToday() { if (UsedDate != Today()) { UsedDate = Today(); Used = 0; } return Used; }
        public int? DailyLimit() => Limit;
        public int MinGapMs() => 0;
        public string Today() => Clock.Now.ToString("yyyy-MM-dd");
        public Task<AiResult<JsonObject>> Analyse(byte[] png, JsonObject schema, string instruction, CancellationToken ct)
        {
            UsedToday();
            Used++;
            Sent++;
            return Task.FromResult(Reply(Sent));
        }
    }

    private static JsonObject Ok(params string[] tags) =>
        new() { ["tags"] = new JsonArray(tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()), ["words"] = new JsonArray() };

    private static string OpenRouterReply(string contentJson) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = contentJson } }) }.ToJsonString();

    private static Raster Plate(int w, int h, int bx, int by, int bw, int bh, byte grey = 128)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 4;
                var inBox = x >= bx && x < bx + bw && y >= by && y < by + bh;
                px[o] = inBox ? (byte)30 : grey; px[o + 1] = inBox ? (byte)200 : grey; px[o + 2] = inBox ? (byte)90 : grey; px[o + 3] = 255;
            }
        return new Raster(w, h, px);
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "soundstage-posetag-" + Guid.NewGuid().ToString("N")[..10]);

    private static string Hash(int i) => i.ToString("x").PadLeft(64, '0');

    // ---- the checks -----------------------------------------------------------------------------------------------

    public override void Run()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        try
        {
            VocabularyChecks(dir);
            ReadChecks();
            ImageChecks(dir);
            JobChecks(dir);
            LimitChecks();
            QueueChecks(dir);
            RealServiceChecks(dir);
            MatchingChecks();
            ConsentChecks();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }

    // ---- 14.3 vocabulary and reading -----------------------------------------------------------------------------------

    private void VocabularyChecks(string dir)
    {
        var shipped = Path.Combine(root, "Poses", "vocabulary.json");
        var v = PoseVocabulary.Load(shipped, Path.Combine(dir, "none.json"));
        Check("vocabulary: the shipped file loads with every word the plan names",
            new[] { "standing", "sitting", "kneeling", "lying", "leaning", "walking", "arms crossed", "hands on hips", "looking back", "looking up", "combat", "cute", "elegant", "dynamic" }.All(v.Contains)
            && v.Note.Length == 0, v.Note);
        Check("vocabulary: every word is already in stored form and none repeats", v.Words.All(w => PoseTags.Normalise(w) == w) && v.Words.Distinct().Count() == v.Words.Count);

        var user = Path.Combine(dir, "pose-vocabulary.json");
        File.WriteAllText(user, """{"words":["Hand On Chin!","new word","standing"]}""");
        var merged = PoseVocabulary.Load(shipped, user);
        Check("vocabulary: a user file adds words (normalised, no repeats) and keeps the shipped ones", merged.Contains("hand on chin") && merged.Contains("new word") && merged.Contains("sitting") && merged.Words.Count == v.Words.Count + 1);

        var bad = Path.Combine(dir, "bad.json");
        File.WriteAllText(bad, "{ not json");
        var fallback = PoseVocabulary.Load(bad, Path.Combine(dir, "none.json"));
        Check("vocabulary: a damaged shipped file falls back to the built-in list and says so", fallback.Contains("standing") && fallback.Note.Contains("built-in"), fallback.Note);
        var missing = PoseVocabulary.Load(Path.Combine(dir, "gone.json"), Path.Combine(dir, "none.json"));
        Check("vocabulary: a missing shipped file also falls back", missing.Contains("kneeling") && missing.Note.Contains("missing"), missing.Note);
    }

    private void ReadChecks()
    {
        var v = PoseVocabulary.BuiltIn();
        var log = new List<string>();
        var reply = new JsonObject
        {
            ["tags"] = new JsonArray("Standing", "arms crossed", "banana pose", "standing", 42),
            ["words"] = new JsonArray("Moody!", "standing", "wind swept", "extra one", "extra two", "extra three"),
        };
        var r = PoseTagger.Read(reply, v, log.Add);
        Check("read: in-list tags kept (case-insensitive, no repeats)", r.Tags.SequenceEqual(new[] { "standing", "arms crossed" }), string.Join(",", r.Tags));
        Check("CONTROL: a tag outside the vocabulary is dropped, with exactly one log line", r.Dropped.SequenceEqual(new[] { "banana pose" }) && log.Count == 1 && log[0].Contains("banana pose"), string.Join(" | ", log));
        Check("read: free words are normalised, not vocabulary words, and capped at three", r.Free.SequenceEqual(new[] { "moody", "wind swept", "extra one" }), string.Join(",", r.Free));

        var quiet = new List<string>();
        PoseTagger.Read(Ok("standing", "cute"), v, quiet.Add);
        Check("read: a clean reply writes no log line", quiet.Count == 0);
        var none = PoseTagger.Read(new JsonObject(), v, quiet.Add);
        Check("read: a reply with no tags field gives nothing and does not throw", none.Tags.Count == 0 && none.Free.Count == 0);

        var schema = PoseTagger.Schema();
        Check("schema: tags is required and the vocabulary is not an enum (one odd word must not fail the request)",
            schema["required"]!.AsArray().Select(n => n!.GetValue<string>()).SequenceEqual(new[] { "tags" }) && schema["properties"]!["tags"]!["items"]!["enum"] == null);
        var ins = PoseTagger.Instruction(v);
        Check("prompt: lists every vocabulary word and asks about the pose only", v.Words.All(ins.Contains) && ins.Contains("pose only"));
    }

    // ---- 14.1 images and cache ------------------------------------------------------------------------------------------

    private void ImageChecks(string dir)
    {
        var plate = Plate(640, 360, 250, 40, 120, 280);
        var box = ThumbnailImage.ActorBounds(plate);
        Check("image: the actor box is found on a uniform background", box is { } b && b.X == 250 && b.Y == 40 && b.W == 120 && b.H == 280, box?.ToString() ?? "null");
        Check("image: an empty background has no actor box", ThumbnailImage.ActorBounds(Plate(200, 200, 0, 0, 0, 0)) == null);
        var speck = Plate(200, 200, 100, 100, 1, 1);
        Check("image: one stray pixel does not make an actor", ThumbnailImage.ActorBounds(speck) == null);

        var (x, y, side) = ThumbnailImage.SquareWindow(plate, box, out var note);
        Check("image: the crop is a square inside the picture that contains the whole actor", side >= 280 && x >= 0 && y >= 0 && x + side <= 640 && y + side <= 360 && x <= 250 && x + side >= 370 && note.Contains("actor"), $"{x},{y},{side}");
        var (cx, cy, cs) = ThumbnailImage.SquareWindow(Plate(640, 360, 0, 0, 0, 0), null, out var cnote);
        Check("image: no actor gives a centre crop", cs == 360 && cx == 140 && cy == 0 && cnote.Contains("centre"));

        var small = ThumbnailImage.Resample(plate, x, y, side, ThumbnailImage.Size);
        Check("image: the result is 256 by 256", small.Width == 256 && small.Height == 256 && small.Bgra.Length == 256 * 256 * 4);
        var centre = (128 * 256 + 128) * 4;
        Check("image: the actor's colour survives the shrink at the middle", small.Bgra[centre + 1] > 150 && small.Bgra[centre] < 80);

        var capture = Path.Combine(dir, "cap.png");
        File.WriteAllBytes(capture, ThumbnailImage.EncodePng(plate));
        var png = ThumbnailImage.Make(capture, out _);
        var decoded = png == null ? null : ThumbnailImage.Load(WritePng(dir, png));
        Check("image: Make goes from a capture file to a 256 pixel PNG that decodes", decoded is { Width: 256, Height: 256 });
        File.WriteAllText(Path.Combine(dir, "notpng.png"), "nope");
        Check("image: an unreadable capture gives null with a reason", ThumbnailImage.Make(Path.Combine(dir, "notpng.png"), out var why) == null && why.Length > 0);

        var cache = new ThumbnailCache(Path.Combine(dir, "thumbs"));
        var h = Hash(0xabc);
        Check("cache: a miss before saving", !cache.Has(h) && cache.Read(h) == null && cache.CountHits(new[] { h, null, "" }) == 0);
        cache.Save(h, png!);
        Check("cache: a hit after saving, bytes unchanged, no temp file left",
            cache.Has(h) && cache.Read(h)!.SequenceEqual(png!) && cache.CountHits(new[] { h, Hash(1) }) == 1 && !Directory.GetFiles(cache.Folder, "*.tmp").Any());

        // The key is the content hash the tag store uses, so a renamed or moved pose file finds its picture again.
        var a = Path.Combine(dir, "a.pose");
        var b2 = Path.Combine(dir, "renamed.pose");
        File.WriteAllText(a, """{"Bones":{}}""");
        File.Copy(a, b2);
        Check("cache: the same pose bytes under another name share one thumbnail", new PoseTags().HashFor(a) == new PoseTags().HashFor(b2));

        // The plain stage.
        var catalogue = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), _ => { });
        var stage = ThumbnailStage.Build(catalogue);
        Check("stage: a grey sweep, no coloured edge light, and no look, camera, pose or atmosphere",
            stage.Id == ThumbnailStage.SceneId && stage.Set is { Kind: "sweep" } && stage.Set.Color![0] == 0.5f && stage.Lights.Count >= 1
            && stage.Lights.All(l => l.Role is not ("kicker" or "rim" or "bg")) && stage.Look == null && stage.Camera == null && stage.Pose == null && stage.Atmosphere == null && stage.Backdrop == null);
        Check("stage: building it leaves the shipped Scene alone", catalogue.First(s => s.Id == "high-key").Lights.Any(l => l.Role == "kicker") && catalogue.First(s => s.Id == "high-key").Id == "high-key" && !ReferenceEquals(catalogue.First(s => s.Id == "high-key").Lights, stage.Lights));
    }

    private static string WritePng(string dir, byte[] bytes)
    {
        var p = Path.Combine(dir, "out-" + Guid.NewGuid().ToString("N")[..6] + ".png");
        File.WriteAllBytes(p, bytes);
        return p;
    }

    // ---- 14.1 the job -------------------------------------------------------------------------------------------------

    private sealed class JobRig
    {
        public readonly ThumbnailCache Cache;
        public readonly ThumbnailJob Job;
        public long Now;
        public bool InGpose = true;
        public string? BeginFails;
        public readonly List<string> Loaded = new();
        public readonly HashSet<string> LoadFails = new();
        public int Ends, Captures, Begins;
        public int FailCaptureAt = -1;
        public readonly List<string> Log = new();
        public readonly List<string> CapturePaths = new();

        public JobRig(string dir)
        {
            Cache = new ThumbnailCache(Path.Combine(dir, "thumbs-" + Guid.NewGuid().ToString("N")[..6]));
            var tempScratch = Path.Combine(dir, "caps");
            Job = new ThumbnailJob(new UiHider(() => Array.Empty<HideTarget>(), Log.Add), Cache, Capture, () => InGpose, () => Now, () => 0, () => 0,
                () => { Begins++; return BeginFails; },
                item => { Loaded.Add(item.Name); return !LoadFails.Contains(item.Name); },
                () => Ends++, Log.Add);
            _ = tempScratch;
        }

        private Task<CaptureResult> Capture(string path, CancellationToken ct)
        {
            Captures++;
            CapturePaths.Add(path);
            if (Captures == FailCaptureAt) return Task.FromResult(new CaptureResult(false, "", "", 1, "no route worked"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, ThumbnailImage.EncodePng(Plate(320, 240, 100, 30, 60, 180)));
            return Task.FromResult(new CaptureResult(true, path, "fake", 1, ""));
        }

        public void RunTicks(int max = 400, Func<bool>? until = null)
        {
            for (var i = 0; i < max && Job.Busy; i++)
            {
                Now += 100;
                Job.Tick();
                if (until != null && until()) return;
            }
        }
    }

    private void JobChecks(string dir)
    {
        var items = Enumerable.Range(1, 4).Select(i => new ThumbItem(Hash(i), "Pose " + i, "p" + i)).ToList();

        var r = new JobRig(dir);
        Check("job: starts in GPose with items", r.Job.Start(items) && r.Job.Busy && r.Begins == 1);
        r.RunTicks();
        Check("job: every pose rendered, cached and restored once", !r.Job.Busy && r.Job.Made == 4 && items.All(i => r.Cache.Has(i.Hash)) && r.Ends == 1 && r.Loaded.Count == 4, r.Job.Status);
        Check("job: each cached picture is 256 by 256", items.All(i => ThumbnailImage.Load(r.Cache.PathFor(i.Hash)) is { Width: 256, Height: 256 }));
        Check("job: capture temp files are gone", r.CapturePaths.All(p => !File.Exists(p)));

        r.Loaded.Clear();
        Check("job: a second run skips poses that already have a thumbnail (nothing to do, no begin)", !r.Job.Start(items) && r.Begins == 1 && r.Job.Status.Contains("already"));
        var more = items.Concat(new[] { new ThumbItem(Hash(9), "Pose 9", "p9") }).ToList();
        r.Job.Start(more);
        r.RunTicks();
        Check("job: a second run renders only the new pose", r.Loaded.SequenceEqual(new[] { "Pose 9" }) && r.Job.Made == 1);

        var outside = new JobRig(dir) { InGpose = false };
        Check("job: refuses outside GPose and touches nothing", !outside.Job.Start(items) && outside.Begins == 0 && outside.Job.Status.Contains("GPose"));

        var cancelled = new JobRig(dir);
        cancelled.Job.Start(items);
        cancelled.RunTicks(until: () => cancelled.Cache.Has(Hash(2)));
        cancelled.Job.Cancel();
        Check("job: cancel keeps what is done, stops, and restores the pose and stage once",
            !cancelled.Job.Busy && cancelled.Cache.Has(Hash(1)) && cancelled.Cache.Has(Hash(2)) && !cancelled.Cache.Has(Hash(4)) && cancelled.Ends == 1, $"ends {cancelled.Ends}");

        var left = new JobRig(dir);
        left.Job.Start(items);
        left.RunTicks(until: () => left.Cache.Has(Hash(1)));
        left.InGpose = false;
        left.RunTicks(5);
        Check("job: leaving GPose stops it and still restores", !left.Job.Busy && left.Ends == 1 && left.Job.Status.Contains("Left GPose"));

        var skipping = new JobRig(dir);
        skipping.LoadFails.Add("Pose 2");
        skipping.Job.Start(items);
        skipping.RunTicks();
        Check("job: a pose that will not load is skipped and counted, the rest carry on",
            skipping.Job.Made == 3 && skipping.Job.Skipped == 1 && !skipping.Cache.Has(Hash(2)) && skipping.Cache.Has(Hash(3)) && skipping.Ends == 1, skipping.Job.Status);

        var allBad = new JobRig(dir);
        foreach (var i in items) allBad.LoadFails.Add(i.Name);
        allBad.Job.Start(items);
        allBad.RunTicks();
        Check("job: when no pose loads it ends cleanly and restores", !allBad.Job.Busy && allBad.Job.Made == 0 && allBad.Job.Skipped == 4 && allBad.Ends == 1);

        var capFail = new JobRig(dir) { FailCaptureAt = 2 };
        capFail.Job.Start(items);
        capFail.RunTicks();
        Check("job: a failed capture stops the job with the reason, and restores", capFail.Job.Made == 1 && capFail.Job.Status.Contains("no route worked") && capFail.Ends == 1, capFail.Job.Status);

        var noBegin = new JobRig(dir) { BeginFails = "Brio is not available." };
        Check("job: if the stage cannot be set up it says why and runs nothing", !noBegin.Job.Start(items) && noBegin.Job.Status == "Brio is not available." && noBegin.Loaded.Count == 0 && noBegin.Ends == 1);
    }

    // ---- 14.4 limits and queue ------------------------------------------------------------------------------------------

    private void LimitChecks()
    {
        Check("limits: a ':free' OpenRouter model gets the documented 50 a day, a typed figure wins, others have none",
            TagLimits.DailyLimit(OpenAiCompatProvider.OpenRouterId, "google/x:free", 0) == 50
            && TagLimits.DailyLimit(OpenAiCompatProvider.OpenRouterId, "google/x:free", 1000) == 1000
            && TagLimits.DailyLimit(OpenAiCompatProvider.OpenRouterId, "google/x", 0) == null
            && TagLimits.DailyLimit(GeminiProvider.ProviderId, "m", 0) == null
            && TagLimits.MinGapMs(OpenAiCompatProvider.OpenRouterId, "x:free") == 3000);

        var docs = Path.GetFullPath(Path.Combine(root, "..", "..", "docs", "ai-providers.md"));
        var text = File.Exists(docs) ? File.ReadAllText(docs) : "";
        Check("limits: the figures in code are the dated ones in docs/ai-providers.md",
            text.Contains($"{TagLimits.OpenRouterFreePerMinute} requests a minute") && text.Contains($"{TagLimits.OpenRouterFreeDaily} a day") && text.Contains("21/11/2026"), docs);
    }

    private static IEnumerable<QueueItem> QItems(string dir, int from, int count)
    {
        var folder = Path.Combine(dir, "qthumbs");
        Directory.CreateDirectory(folder);
        var png = ThumbnailImage.EncodePng(Plate(8, 8, 2, 2, 4, 4));
        for (var i = from; i < from + count; i++)
        {
            var path = Path.Combine(folder, Hash(i) + ".png");
            File.WriteAllBytes(path, png);
            yield return new QueueItem(Hash(i), "Pose " + i, path);
        }
    }

    private void QueueChecks(string dir)
    {
        var vocab = PoseVocabulary.BuiltIn();
        var clock = new Clock();
        var backend = new FakeBackend(clock) { Limit = 50 };
        var tags = new PoseTags(Path.Combine(dir, "q-tags.json"));
        var log = new List<string>();
        var statePath = Path.Combine(dir, "q-state.json");
        var q = new TagQueue(backend, tags, vocab, statePath, log.Add);
        q.Enqueue(QItems(dir, 1, 60).ToList());
        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: sixty poses on a 50 a day allowance, the 51st request is refused", backend.Sent == 50 && q.State == QueueState.PausedBudget && q.Remaining == 10, $"sent {backend.Sent}, {q.State}");
        Check("queue: results were written as they arrived (50 poses tagged, on disk)", q.Tagged == 50 && PoseTags.Load(tags.StorePath).TaggedCount == 50 && PoseTags.Load(tags.StorePath).TagsFor(Hash(1)).SequenceEqual(new[] { "standing" }));
        Check("queue: the pause says how many are waiting and that tomorrow is the way on", q.Status.Contains("10 pose") && q.Status.Contains("tomorrow"), q.Status);
        Check("queue: not resumable the same day", !q.CanResume);

        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: pressing again the same day sends nothing", backend.Sent == 50 && q.State == QueueState.PausedBudget);

        clock.Now = clock.Now.AddDays(1);
        Check("queue: the next day it can resume", q.CanResume);
        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: resumed with a fake clock, the other ten go through and it finishes", backend.Sent == 60 && q.State == QueueState.Finished && q.Remaining == 0 && q.Tagged == 60 && q.DoneCount == 60, q.Status);

        var again = new TagQueue(backend, tags, vocab, statePath, log.Add);
        Check("queue: a new queue (after a restart) knows which poses are done and adds none of them", again.Enqueue(QItems(dir, 1, 60).ToList()) == 0 && again.DoneCount == 60);

        // Error handling.
        var b2 = new FakeBackend(clock) { Limit = null };
        b2.Reply = n => n == 3 ? AiResult<JsonObject>.Fail(AiError.RateLimit) : AiResult<JsonObject>.Success(Ok("sitting", "made up tag"));
        var tags2 = new PoseTags(Path.Combine(dir, "q2-tags.json"));
        var log2 = new List<string>();
        var q2 = new TagQueue(b2, tags2, vocab, Path.Combine(dir, "q2-state.json"), log2.Add);
        q2.Enqueue(QItems(dir, 100, 5).ToList());
        q2.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: a rate-limit reply pauses it with the pose still waiting (no figure known)", q2.State == QueueState.PausedBudget && q2.Remaining == 3 && q2.Processed == 2 && !q2.IsDone(Hash(102)));
        Check("CONTROL: a tag outside the vocabulary never reaches the store, and costs one log line per reply",
            tags2.AllTags().Select(t => t.Tag).SequenceEqual(new[] { "sitting" }) && log2.Count(l => l.Contains("dropped 1 outside")) == 2);

        var b3 = new FakeBackend(clock) { Limit = null, Reply = _ => AiResult<JsonObject>.Fail(AiError.ConsentNeeded) };
        var q3 = new TagQueue(b3, new PoseTags(), vocab, "", _ => { });
        q3.Enqueue(QItems(dir, 200, 3).ToList());
        q3.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: a missing consent stops it at the first pose, nothing marked done", b3.Sent == 1 && q3.State == QueueState.PausedError && q3.Remaining == 3 && q3.DoneCount == 0);

        var b4 = new FakeBackend(clock) { Limit = null, Reply = _ => AiResult<JsonObject>.Fail(AiError.NotJson) };
        var q4 = new TagQueue(b4, new PoseTags(), vocab, "", _ => { });
        q4.Enqueue(QItems(dir, 300, 9).ToList());
        q4.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: unreadable replies skip the pose, and five in a row stop the queue", b4.Sent == 5 && q4.Skipped == 5 && q4.State == QueueState.PausedError && q4.Remaining == 4);

        var q5 = new TagQueue(new FakeBackend(clock) { Limit = null }, new PoseTags(), vocab, "", _ => { });
        q5.Enqueue(new[] { new QueueItem(Hash(1), "Gone", Path.Combine(dir, "does-not-exist.png")) });
        q5.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("queue: a pose whose thumbnail has vanished is skipped, not sent", q5.Skipped == 1 && q5.State == QueueState.Finished);

        var cts = new CancellationTokenSource();
        var b6 = new FakeBackend(clock) { Limit = null };
        var q6 = new TagQueue(b6, new PoseTags(), vocab, "", _ => { });
        b6.Reply = n => { if (n == 2) cts.Cancel(); return AiResult<JsonObject>.Success(Ok("cute")); };
        q6.Enqueue(QItems(dir, 400, 5).ToList());
        q6.RunAsync(cts.Token).GetAwaiter().GetResult();
        Check("queue: cancel keeps the tags written so far and leaves the rest waiting", q6.State == QueueState.Cancelled && q6.Tagged == 2 && q6.Remaining == 3);

        // Tag only a selected pack: the queue only ever sees what it is given.
        var q7 = new TagQueue(new FakeBackend(clock), new PoseTags(), vocab, "", _ => { });
        Check("queue: enqueue adds only the poses given, and each once", q7.Enqueue(QItems(dir, 500, 3).ToList()) == 3 && q7.Enqueue(QItems(dir, 501, 3).ToList()) == 1 && q7.Remaining == 4);
    }

    // ---- 14.4 / 14.5 with the real AI layer, a fake server and a fake clock ------------------------------------------------

    private void RealServiceChecks(string dir)
    {
        var clock = new Clock();
        var handler = new FakeHandler { Reply = _ => OpenRouterReply("""{"tags":["kneeling","looking back","nonsense"],"words":["windswept"]}""") };
        var log = new List<string>();
        var svc = new AiService(new AiSettings(), () => { }, log.Add, new FakeProtector(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, () => clock.Now);
        svc.ProviderId = OpenAiCompatProvider.OpenRouterId;
        svc.Settings.For(OpenAiCompatProvider.OpenRouterId).Model = "vendor/vision-model:free";
        svc.Keys.Set(OpenAiCompatProvider.OpenRouterId, Sentinel);
        var tags = new PoseTags(Path.Combine(dir, "real-tags.json"));
        var q = new TagQueue(new AiServiceBackend(svc, () => clock.Now), tags, PoseVocabulary.BuiltIn(), Path.Combine(dir, "real-state.json"), log.Add, (ms, ct) => Task.CompletedTask);

        q.Enqueue(QItems(dir, 1000, 2).ToList());
        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("consent: with a key but no consent, nothing is sent and the queue says it needs one", handler.Bodies.Count == 0 && q.State == QueueState.PausedError && q.Status.Length > 0 && q.Remaining == 2, q.Status);

        svc.GiveConsent(OpenAiCompatProvider.OpenRouterId);
        svc.Settings.Consents.First().TextVersion = 1;
        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("consent: a yes given under the version-1 wording does not cover the thumbnails", handler.Bodies.Count == 0 && !svc.HasConsent(OpenAiCompatProvider.OpenRouterId));

        svc.GiveConsent(OpenAiCompatProvider.OpenRouterId);
        q.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("consent: after the new yes both poses go, tags are stored from the vocabulary and the free word",
            handler.Bodies.Count == 2 && q.State == QueueState.Finished && tags.TagsFor(Hash(1000)).SequenceEqual(new[] { "kneeling", "looking back", "windswept" }), q.Status);
        Check("request: the thumbnail goes as a picture with the vocabulary in the prompt, and the key is in no log line",
            handler.Bodies[0].Contains("image_url") && handler.Bodies[0].Contains("kneeling") && !string.Join("\n", log).Contains(Sentinel));

        // The 51st request of the day, through the real service and its own counter.
        var handler2 = new FakeHandler { Reply = _ => OpenRouterReply("""{"tags":["standing"]}""") };
        var svc2 = new AiService(new AiSettings(), () => { }, _ => { }, new FakeProtector(), new HttpClient(handler2) { Timeout = Timeout.InfiniteTimeSpan }, () => clock.Now);
        svc2.ProviderId = OpenAiCompatProvider.OpenRouterId;
        svc2.Settings.For(OpenAiCompatProvider.OpenRouterId).Model = "vendor/m:free";
        svc2.Keys.Set(OpenAiCompatProvider.OpenRouterId, Sentinel);
        svc2.GiveConsent(OpenAiCompatProvider.OpenRouterId);
        var q2 = new TagQueue(new AiServiceBackend(svc2, () => clock.Now), new PoseTags(), PoseVocabulary.BuiltIn(), "", _ => { }, (ms, ct) => Task.CompletedTask);
        q2.Enqueue(QItems(dir, 2000, 60).ToList());
        q2.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("budget: the 51st request of the day is refused and the queue is paused", handler2.Bodies.Count == 50 && q2.State == QueueState.PausedBudget && svc2.UsedToday(OpenAiCompatProvider.OpenRouterId) == 50, $"sent {handler2.Bodies.Count}");
        clock.Now = clock.Now.AddDays(1);
        Check("budget: the day after, the real counter is back to zero and the queue can resume", svc2.UsedToday(OpenAiCompatProvider.OpenRouterId) == 0 && q2.CanResume);
        q2.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check("budget: and it finishes the remaining ten", handler2.Bodies.Count == 60 && q2.State == QueueState.Finished);
    }

    // ---- 14.6 matching ----------------------------------------------------------------------------------------------------

    private void MatchingChecks()
    {
        PoseCandidate P(string name, params string[] t) => new("k-" + name, name, "pack", t);
        var poses = new[]
        {
            P("a sit", "sitting", "cute"),
            P("b sit elegant", "sitting", "elegant", "looking back"),
            P("c stand", "standing", "cute"),
            P("d untagged"),
            P("e kneel", "kneeling"),
        };
        var ranked = Matching.Rank(new[] { "sitting", "elegant" }, poses);
        Check("matching: ranked by shared tags, then tidiness; no overlap and untagged are left out", ranked.Select(m => m.Pose.Name).SequenceEqual(new[] { "b sit elegant", "a sit" }), string.Join(",", ranked.Select(m => m.Pose.Name)));
        Check("matching: reports which tags were shared", ranked[0].Shared.SequenceEqual(new[] { "sitting", "elegant" }) && ranked[0].Overlap == 2);
        Check("matching: a prefix of three letters or more agrees (sit with sitting), shorter ones do not",
            Matching.Rank(new[] { "sit" }, poses).Count == 2 && Matching.Rank(new[] { "si" }, poses).Count == 0 && Matching.Agree("standing", "standing") && !Matching.Agree("sit", "stand"));
        Check("matching: the Scene's tags are normalised first", Matching.Rank(new[] { "  Sitting! " }, poses).Count == 2);
        Check("matching: no Scene tags, no matches; a limit is honoured", Matching.Rank(Array.Empty<string>(), poses).Count == 0 && Matching.Rank(new[] { "cute", "sitting", "standing", "kneeling" }, poses, 2).Count == 2);
        Check("matching: ties break by name, so the order is stable", Matching.Rank(new[] { "cute" }, poses).Select(m => m.Pose.Name).SequenceEqual(new[] { "a sit", "c stand" }));
    }

    // ---- 14.5 consent ------------------------------------------------------------------------------------------------------

    private void ConsentChecks()
    {
        Check("consent: the wording version is past the photo-only version", Consent.TextVersion >= 2);
        foreach (var id in new[] { GeminiProvider.ProviderId, OpenAiCompatProvider.OpenRouterId, OpenAiCompatProvider.CustomId })
        {
            var text = Consent.Text(id, AiService.NameOf(id), "m", "https://x.test/v1");
            Check($"consent: the {id} wording names the thumbnails and the Tag with AI button and still says it leaves the computer",
                text.Contains("Tag with AI") && text.Contains("256 pixels") && text.Contains("your own character") && text.Contains("leaves your computer"));
        }
    }
}

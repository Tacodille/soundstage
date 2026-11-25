// Part 13: photo to scene, offline. Responses are text JSON written here, the "photos" are tiny pictures made in memory,
// and the provider is a fake HTTP handler. No real network, no real key, no real photo.
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Soundstage;
using Soundstage.Ai;
using Soundstage.Ai.PhotoToScene;
using StbImageWriteSharp;

namespace Soundstage.SelfCheck;

public sealed class PhotoChecks : CheckArea
{
    public override string Name => "Photo to Scene (offline)";
    public override int Order => 130;

    private const string Sentinel = "zz-photo-sentinel-4c2e81-KEYMATERIAL";

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(string Url, string Body)> Requests = new();
        public Func<string> Reply { get; set; } = () => "{}";
        public int Status { get; set; } = 200;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.RequestUri!.ToString(), request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage((HttpStatusCode)Status) { Content = new StringContent(Reply(), Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FakeProtector : IKeyProtector
    {
        public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain).Reverse().ToArray());
        public string? Unprotect(string stored) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(stored).Reverse().ToArray()); } catch (FormatException) { return null; } }
    }

    private sealed record Rig(AiService Svc, FakeHandler Handler, List<string> Log);

    private static Rig MakeRig(bool key, bool consent)
    {
        var handler = new FakeHandler();
        var log = new List<string>();
        var svc = new AiService(new AiSettings(), () => { }, log.Add, new FakeProtector(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, () => new DateTime(2026, 11, 21, 10, 0, 0));
        svc.Settings.For(GeminiProvider.ProviderId).Model = "gemini-test-model";
        if (key) svc.Keys.Set(GeminiProvider.ProviderId, Sentinel);
        if (consent) svc.GiveConsent(GeminiProvider.ProviderId);
        return new Rig(svc, handler, log);
    }

    private static string GeminiOk(string text) =>
        new JsonObject
        {
            ["steps"] = new JsonArray(new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) }),
        }.ToJsonString();

    private static string Good(string closest = "rembrandt") => $$"""
        {"rig":"rembrandt","keyDirection":"front-left","keyElevation":"above","keySoftness":"hard",
         "colours":{"key":"#FFE0B0","fill":"#8090C0","rim":"#FFFFFF"},"backdropColour":"#102040","hasRim":true,
         "mood":["moody","dark","warm","classic"],"headAngle":{"yaw":"turned slightly right","pitch":"level"},
         "expression":"calm","closestScene":"{{closest}}"}
        """;

    private static IReadOnlyList<Scene> Catalogue()
    {
        var all = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), _ => { });
        return all.Where(s => s.Category != "Mine").ToList();
    }

    private static (PhotoAnalysis? A, string? Err) Parse(string json, IReadOnlyList<Scene> cat) =>
        PhotoParser.Parse(JsonNode.Parse(json), cat);

    // ---- pictures made in memory ----------------------------------------------------------------------------------

    /// <summary>A w x h JPEG: left half red, right half blue, with an optional EXIF block (orientation plus a GPS-looking string) spliced in after the start marker.</summary>
    private static byte[] MakeJpeg(int w, int h, int? exifOrientation, string? secret = null)
    {
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var o = (y * w + x) * 3;
                if (x < w / 2) rgb[o] = 255; else rgb[o + 2] = 255;
            }
        using var ms = new MemoryStream();
        new ImageWriter().WriteJpg(rgb, w, h, StbImageWriteSharp.ColorComponents.RedGreenBlue, ms, 90);
        var jpeg = ms.ToArray();
        if (exifOrientation == null && secret == null) return jpeg;

        var body = new List<byte>();
        body.AddRange(Encoding.ASCII.GetBytes("Exif\0\0"));
        var tiff = new List<byte> { (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0 };   // little endian, IFD at 8
        tiff.AddRange(new byte[] { 1, 0 });                                         // one entry
        tiff.AddRange(new byte[] { 0x12, 0x01, 3, 0, 1, 0, 0, 0, (byte)(exifOrientation ?? 1), 0, 0, 0 });
        tiff.AddRange(new byte[] { 0, 0, 0, 0 });
        if (secret != null) tiff.AddRange(Encoding.ASCII.GetBytes(secret));
        body.AddRange(tiff);
        var len = body.Count + 2;
        var seg = new List<byte> { 0xFF, 0xE1, (byte)(len >> 8), (byte)(len & 0xFF) };
        seg.AddRange(body);
        var output = new List<byte> { jpeg[0], jpeg[1] };
        output.AddRange(seg);
        output.AddRange(jpeg.Skip(2));
        return output.ToArray();
    }

    private static bool Contains(byte[] hay, string needle) =>
        hay.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    private static PreparedImage Small() => PhotoIntake.Prepare(MakeJpeg(64, 48, null));

    // ---- the checks -----------------------------------------------------------------------------------------------

    public override void Run()
    {
        var cat = Catalogue();
        Check("catalogue loads shipped Scenes for the photo checks", cat.Count >= 15, cat.Count + " scenes");

        ParserChecks(cat);
        MapperChecks(cat);
        PromptChecks(cat);
        IntakeChecks();
        RunChecks(cat);
    }

    private void ParserChecks(IReadOnlyList<Scene> cat)
    {
        var (a, err) = Parse(Good(), cat);
        Check("parser: a good reply reads every field", a != null && err == null && a.KeyDirection == "front-left" && a.KeyElevation == "above" && a.KeySoftness == "hard"
            && a.Rig == "rembrandt" && a.ClosestScene == "rembrandt" && a.HasRim == true && a.HeadYaw == "turned slightly right" && a.Expression == "calm" && a.Notes.Count == 0);
        Check("parser: hex colours become 0..1 RGB", a != null && Near(a.KeyColour![0], 1f) && Near(a.KeyColour[1], 224 / 255f) && Near(a.BackdropColour![2], 64 / 255f));
        Check("parser: mood keeps only fixed-list words, at most three", a != null && a.Mood.SequenceEqual(new[] { "moody", "dark", "warm" }), string.Join(",", a?.Mood ?? new()));

        var (missing, _) = Parse("{}", cat);
        Check("parser: an empty object gives safe defaults and notes, not an exception",
            missing != null && missing.KeyDirection == "front" && missing.KeySoftness == "medium" && missing.KeyColour == null && missing.HasRim == null && missing.Notes.Count >= 2);

        var (unknownRig, _) = Parse("""{"rig":"totally invented rig","keyDirection":"left","keySoftness":"soft","colours":{},"closestScene":"no-such-scene"}""", cat);
        Check("parser: an unknown rig and an unknown Scene id become empty with a note",
            unknownRig != null && unknownRig.Rig == "" && unknownRig.ClosestScene == "" && unknownRig.Notes.Count == 2);

        var (badHex, _) = Parse("""{"keyDirection":"front","keySoftness":"soft","colours":{"key":"#GG0000","fill":"red","rim":"#12"},"backdropColour":"rgb(1,2,3)","closestScene":"loop"}""", cat);
        Check("parser: bad hex keeps the Scene's colour and says so", badHex != null && badHex.KeyColour == null && badHex.FillColour == null && badHex.RimColour == null && badHex.BackdropColour == null && badHex.Notes.Count == 4);
        Check("parser: short hex #FA0 and bare FFAA00 both read", PhotoParser.ParseHex("#FA0") is { } s && Near(s[1], 170 / 255f) && PhotoParser.ParseHex("ffaa00") is { } t && Near(t[0], 1f));

        var wrong = "{\"keyDirection\":7,\"keyElevation\":[1],\"keySoftness\":{\"x\":1},\"colours\":42,\"hasRim\":\"yes\",\"mood\":\"moody\",\"headAngle\":99,\"expression\":null,\"closestScene\":false,\"rig\":3}";
        (PhotoAnalysis? A, string? Err) wr = default;
        Exception? thrown = null;
        try { wr = Parse(wrong, cat); } catch (Exception ex) { thrown = ex; }
        Check("parser: wrong types never throw and fall back to defaults", thrown == null && wr.A != null && wr.A.KeyDirection == "front" && wr.A.KeySoftness == "medium" && wr.A.HasRim == null && wr.A.Mood.Count == 0, thrown?.Message ?? "");

        var (notObject, errNot) = PhotoParser.Parse(JsonNode.Parse("[1,2,3]"), cat);
        Check("parser: a reply that is not an object is a typed error", notObject == null && !string.IsNullOrEmpty(errNot));
        var (nullReply, errNull) = PhotoParser.Parse(null, cat);
        Check("parser: a null reply is a typed error", nullReply == null && !string.IsNullOrEmpty(errNull));

        var hostile = """
            {"keyDirection":"left above","keySoftness":"hard","colours":{"key":"#FFFFFF"},"closestScene":"loop","rig":"loop lighting",
             "expression":"C:\\Windows\\System32\\config\\SAM","filePath":"C:\\secret.txt","path":"../../etc/passwd","unknownField":{"a":1},
             "scenesFile":"user-scenes.json","headAngle":"\u0000\u0007bell"}
            """;
        var (h, _) = Parse(hostile, cat);
        Check("parser: extra fields and file paths in the reply are ignored, text is never used as a path",
            h != null && h.KeyDirection == "left" && h.KeyElevation == "above" && h.ClosestScene == "loop" && !h.HeadYaw.Any(char.IsControl));
        var mappedHostile = h == null ? null : PhotoMapper.Map(h, cat);
        Check("mapper: a Scene made from a hostile reply is valid and carries no path text",
            mappedHostile?.Scene != null && SceneStore.Validate(mappedHostile.Scene) == null && !JsonSerialiseScene(mappedHostile.Scene).Contains("secret.txt") && !JsonSerialiseScene(mappedHostile.Scene).Contains("passwd"));

        var direction = new (string text, string? dir, string? elev)[]
        {
            ("front", "front", null), ("front-left", "front-left", null), ("Front Left", "front-left", null), ("left", "left", null), ("back_left", "back-left", null),
            ("back", "back", null), ("behind right", "back-right", null), ("right high", "right", "above"), ("from the front right, below", "front-right", "below"),
            ("above", null, "above"), ("sideways", null, null), ("left right", null, null),
        };
        foreach (var (text, dir, elev) in direction)
        {
            var (d, e) = PhotoParser.ReadDirection(text);
            Check($"direction words: '{text}' reads as {dir ?? "(none)"} / {elev ?? "(none)"}", d == dir && e == elev, $"{d}/{e}");
        }
        Check("direction table: all eight words have a degree value", PhotoWords.Directions.All(PhotoTables.AzimuthDeg.ContainsKey) && PhotoWords.Elevations.All(PhotoTables.ElevationDeg.ContainsKey) && PhotoWords.Softness.All(PhotoTables.Softness.ContainsKey));
    }

    private static string JsonSerialiseScene(Scene s) => System.Text.Json.JsonSerializer.Serialize(s, SceneStore.WriteOptions);

    private void MapperChecks(IReadOnlyList<Scene> cat)
    {
        var (a, _) = Parse(Good(), cat);
        var m = PhotoMapper.Map(a!, cat);
        var s = m.Scene;
        Check("mapper: a good analysis makes a valid Scene started from the named one", s != null && m.Error == null && m.BaseSceneId == "rembrandt" && SceneStore.Validate(s) == null);
        if (s == null) return;
        var key = s.Lights.First(l => l.Role == "key");
        Check("mapper: key azimuth follows the direction table (front-left is the subject's right, so negative)", Near(key.AzimuthDeg, PhotoTables.AzimuthDeg["front-left"]) && key.AzimuthDeg < 0);
        Check("mapper: 'above' lifts the key above the head height", key.Height > (key.AimHeight ?? AimSolver.DefaultHeadHeight) + 0.3f, key.Height.ToString("F2"));
        Check("mapper: hard softness narrows the cone and the edge", Near(key.ConeDeg, 25f) && Near(key.AngularFalloffDeg, 5f));
        Check("mapper: key colour and range follow the photo", Near(key.Color[0], 1f) && Near(key.Color[1], 224 / 255f) && Near(key.Range, SceneStore.GameRange(key)));
        Check("mapper: backdrop colour is set on the Scene", s.Backdrop == null || Near(s.Backdrop.Color[2], 64 / 255f) || s.Set != null);
        Check("mapper: the lights' colours are baked into the one 'Photo' palette", s.DefaultPalette == "Photo" && s.Palettes.Count == 1 && s.Palettes["Photo"].ContainsKey("key"));
        Check("mapper: the result is marked as a user Scene with a fresh id", s.Id == "from-photo" && s.Category == "Mine");
        Check("mapper: the catalogue itself is not changed", cat.First(c => c.Id == "rembrandt").Lights.First(l => l.Role == "key").AzimuthDeg == 45f);

        var soft = Parse("""{"keyDirection":"right","keyElevation":"below","keySoftness":"soft","colours":{},"closestScene":"rembrandt"}""", cat).A!;
        var ms = PhotoMapper.Map(soft, cat).Scene!;
        var ks = ms.Lights.First(l => l.Role == "key");
        Check("mapper: right / below / soft gives +90, a low key and a wide cone", Near(ks.AzimuthDeg, 90f) && ks.Height < (ks.AimHeight ?? AimSolver.DefaultHeadHeight) && Near(ks.ConeDeg, 70f));

        var unknown = Parse("""{"rig":"rembrandt","keyDirection":"front","keySoftness":"hard","colours":{},"closestScene":"made-up","mood":["moody"]}""", cat).A!;
        var mu = PhotoMapper.Map(unknown, cat);
        Check("mapper: an unknown Scene id falls back to the best match by rig and mood", mu.Scene != null && mu.BaseSceneId == "rembrandt", mu.BaseSceneId);
        var nothing = PhotoMapper.Map(new PhotoAnalysis(), cat);
        Check("mapper: an empty analysis still makes a valid Scene", nothing.Scene != null && SceneStore.Validate(nothing.Scene) == null);
        var none = PhotoMapper.Map(new PhotoAnalysis(), new List<Scene>());
        Check("mapper: an empty catalogue is a typed error, not an exception", none.Scene == null && !string.IsNullOrEmpty(none.Error));

        var noRim = Parse("""{"keyDirection":"front","keySoftness":"soft","colours":{},"hasRim":false,"closestScene":"golden-hour"}""", cat).A!;
        var mnr = PhotoMapper.Map(noRim, cat).Scene!;
        Check("mapper: hasRim false takes the Scene's rim light out", !mnr.Lights.Any(l => l.Role == "rim") && mnr.Lights.Count >= 1);
        var addRim = Parse("""{"keyDirection":"front","keySoftness":"soft","colours":{"rim":"#00FF00"},"hasRim":true,"closestScene":"butterfly"}""", cat).A!;
        var mar = PhotoMapper.Map(addRim, cat).Scene!;
        Check("mapper: hasRim true adds a rim light to a Scene that has none, in the photo's colour", mar.Lights.Any(l => l.Role == "rim" && Near(l.Color[1], 1f)) && SceneStore.Validate(mar) == null);

        // The 40-light control: a reply naming a 40-light array does not reach the Scene, and the validator would refuse it if it did.
        var forty = new JsonObject { ["keyDirection"] = "front", ["keySoftness"] = "soft", ["colours"] = new JsonObject(), ["closestScene"] = "loop", ["lights"] = new JsonArray(Enumerable.Range(0, 40).Select(i => (JsonNode?)new JsonObject { ["role"] = "x" + i }).ToArray()) };
        var (fa, _) = PhotoParser.Parse(forty, cat);
        var mf = PhotoMapper.Map(fa!, cat).Scene;
        Check("mapper: a reply carrying a 40-light array ignores it (the Scene keeps the catalogue's lights)", mf != null && mf.Lights.Count == cat.First(c => c.Id == "loop").Lights.Count && mf.Lights.Count <= SceneStore.MaxLights);
        var tooMany = cat.First(c => c.Id == "loop").Clone();
        tooMany.Lights = Enumerable.Range(0, 40).Select(i => new SceneLight { Role = "r" + i, Name = "L" + i }).ToList();
        Check("CONTROL: the Scene validator itself rejects a 40-light Scene", SceneStore.Validate(tooMany) is { } why && why.Contains("40"));
    }

    private void PromptChecks(IReadOnlyList<Scene> cat)
    {
        var withMine = cat.Concat(new[] { new Scene { Id = "my-private-look", Name = "Mine one", Category = "Mine", Rig = "x" } });
        var catalogue = PhotoPrompt.Catalogue(withMine);
        var prompt = PhotoPrompt.Build(catalogue);
        Check("prompt: lists every shipped Scene id and rig", cat.All(s => prompt.Contains(s.Id) && prompt.Contains(s.Rig)));
        Check("prompt: the user's own Scenes are never listed", !prompt.Contains("my-private-look") && catalogue.All(s => s.Category != "Mine"));
        Check("prompt: names the fixed words", PhotoWords.Directions.All(prompt.Contains) && PhotoWords.Softness.All(prompt.Contains) && prompt.Contains("#RRGGBB"));
        var schema = PhotoSchema.Build();
        Check("schema: a good reply passes the same validator the providers use", SchemaValidator.Validate(JsonNode.Parse(Good()), schema) == null);
        Check("schema: a reply missing keyDirection fails it", SchemaValidator.Validate(JsonNode.Parse("""{"keySoftness":"soft","colours":{},"closestScene":"x"}"""), schema) != null);
    }

    private void IntakeChecks()
    {
        // EXIF stripped: build a JPEG with an EXIF block holding a GPS-looking string, run it through intake, scan the output bytes.
        var source = MakeJpeg(80, 60, 1, "GPSLatitude-37.81-secret");
        Check("EXIF control: the test picture really contains an Exif marker and the secret string", Contains(source, "Exif") && Contains(source, "GPSLatitude"));
        var prepared = PhotoIntake.Prepare(source);
        Check("EXIF stripped: the re-encoded JPEG has no Exif marker and none of the metadata", prepared.Ok && !Contains(prepared.Jpeg, "Exif") && !Contains(prepared.Jpeg, "GPSLatitude") && prepared.Jpeg[0] == 0xFF && prepared.Jpeg[1] == 0xD8);

        var big = PhotoIntake.Prepare(MakeJpeg(2400, 1600, null));
        Check("size: a 2400 x 1600 picture is shrunk to 1024 on the long side", big.Ok && Math.Max(big.Width, big.Height) == 1024 && big.Width == 1024 && big.Height == 683, $"{big.Width}x{big.Height}");
        var tall = PhotoIntake.Prepare(MakeJpeg(900, 2000, null));
        Check("size: a tall picture is shrunk by its height", tall.Ok && tall.Height == 1024 && tall.Width == 461, $"{tall.Width}x{tall.Height}");
        var smallOk = PhotoIntake.Prepare(MakeJpeg(300, 200, null));
        Check("size: a small picture is not enlarged", smallOk.Ok && smallOk.Width == 300 && smallOk.Height == 200);
        Check("preview: a thumbnail of at most 48 pixels is made", big.ThumbWidth <= PhotoIntake.ThumbLongSide && big.ThumbHeight <= PhotoIntake.ThumbLongSide && big.ThumbRgb.Length == big.ThumbWidth * big.ThumbHeight * 3);

        // Orientation 6 means "rotate 90 clockwise to view": a 80 x 60 picture comes out 60 x 80, and its red left half ends on top.
        var rotated = PhotoIntake.Prepare(MakeJpeg(80, 60, 6));
        var topPixel = rotated.ThumbRgb.Length > 0 ? rotated.ThumbRgb[0] : (byte)0;
        Check("orientation: EXIF 6 turns an 80 x 60 picture upright (60 x 80) before the metadata is dropped", rotated.Ok && rotated.Width == 60 && rotated.Height == 80, $"{rotated.Width}x{rotated.Height}");
        Check("orientation: the left half (red) is now the top", rotated.Ok && rotated.ThumbRgb[0] > 200 && rotated.ThumbRgb[2] < 80 && rotated.ThumbRgb[(rotated.ThumbHeight - 1) * rotated.ThumbWidth * 3 + 2] > 200, topPixel.ToString());
        Check("orientation: every EXIF value 1 to 8 reads back and turns", Enumerable.Range(1, 8).All(o => PhotoIntake.ReadExifOrientation(MakeJpeg(40, 30, o)) == o));

        // The file on disk: never modified, nothing else written beside it.
        var dir = Path.Combine(Path.GetTempPath(), "soundstage-photo-check-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "original.jpg");
            File.WriteAllBytes(path, source);
            var hashBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            var stampBefore = File.GetLastWriteTimeUtc(path);
            var fromFile = PhotoIntake.LoadFile(path);
            var hashAfter = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
            Check("original: loading a file leaves it byte for byte the same, with its time stamp, and writes nothing beside it",
                fromFile.Ok && hashBefore == hashAfter && stampBefore == File.GetLastWriteTimeUtc(path) && Directory.GetFiles(dir).Length == 1);
            Check("original: the original still carries its EXIF (only the copy sent is stripped)", Contains(File.ReadAllBytes(path), "GPSLatitude"));

            Check("intake: a missing file is a plain message", PhotoIntake.LoadFile(Path.Combine(dir, "nope.jpg")) is { Ok: false, Error: IntakeError.NotFound });
            Check("intake: an empty path asks for a photo", PhotoIntake.LoadFile("  ") is { Ok: false, Error: IntakeError.NoPath });
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a picture");
            Check("intake: a text file is refused as unsupported", PhotoIntake.LoadFile(Path.Combine(dir, "notes.txt")) is { Ok: false, Error: IntakeError.Unsupported });
            File.WriteAllBytes(Path.Combine(dir, "quoted.jpg"), source);
            Check("intake: a path pasted with quotes still loads", PhotoIntake.LoadFile("\"" + Path.Combine(dir, "quoted.jpg") + "\"").Ok);
            using (var fs = new FileStream(Path.Combine(dir, "huge.bin"), FileMode.Create)) fs.SetLength(PhotoIntake.MaxFileBytes + 1);
            Check("intake: a file over the size cap is refused before it is read", PhotoIntake.LoadFile(Path.Combine(dir, "huge.bin")) is { Ok: false, Error: IntakeError.TooBig });
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        Check("intake: truncated JPEG bytes give a typed refusal, never an exception", Safe(() => PhotoIntake.Prepare(source[..40]).Ok == false));
        using (var pngMs = new MemoryStream())
        {
            new ImageWriter().WritePng(new byte[40 * 40 * 3], 40, 40, StbImageWriteSharp.ColorComponents.RedGreenBlue, pngMs);
            var png = pngMs.ToArray();
            Check("intake: a good PNG loads", PhotoIntake.Prepare(png).Ok);
            Check("intake: a cut-off PNG gives a typed refusal and does not hang", Safe(() => PhotoIntake.Prepare(png[..(png.Length / 2)]).Ok == false));
        }
        Check("intake: random bytes give a typed refusal", Safe(() => PhotoIntake.Prepare(Enumerable.Range(0, 500).Select(i => (byte)(i * 31)).ToArray()).Ok == false));
    }

    private static bool Safe(Func<bool> f) { try { return f(); } catch (Exception) { return false; } }

    private void RunChecks(IReadOnlyList<Scene> cat)
    {
        var img = Small();
        Check("run: the test picture is ready", img.Ok);

        // No key: a plain setup message, nothing sent.
        var noKey = MakeRig(key: false, consent: false);
        var run0 = new PhotoRun(noKey.Svc, () => cat);
        var o0 = run0.Analyse(img).GetAwaiter().GetResult();
        Check("no key: the run says NoKey with the setup message and sends nothing", o0.Failure?.Error == AiError.NoKey && o0.Message.Contains("AI tab") && noKey.Handler.Requests.Count == 0 && run0.Gate() == PhotoGate.NoKey);

        // Consent missing: nothing is uploaded.
        var noConsent = MakeRig(key: true, consent: false);
        var run1 = new PhotoRun(noConsent.Svc, () => cat);
        var o1 = run1.Analyse(img).GetAwaiter().GetResult();
        Check("no request before consent: a key without consent sends nothing and says so", o1.Failure?.Error == AiError.ConsentNeeded && noConsent.Handler.Requests.Count == 0 && run1.Gate() == PhotoGate.NeedsConsent);

        // The service itself refuses too (the part 6 door), so the tab cannot skip the gate.
        var direct = noConsent.Svc.AnalyseImage(img.Jpeg, "image/jpeg", PhotoSchema.Build(), "x").GetAwaiter().GetResult();
        Check("no request before consent: the AI service refuses on its own as well", direct.Failure?.Error == AiError.ConsentNeeded && noConsent.Handler.Requests.Count == 0);

        // Control: consent forced true, no key.
        var forced = MakeRig(key: false, consent: true);
        var run2 = new PhotoRun(forced.Svc, () => cat);
        var o2 = run2.Analyse(img).GetAwaiter().GetResult();
        Check("CONTROL: consent forced on but no key gives the typed no-key error and makes no request", forced.Svc.HasConsent(GeminiProvider.ProviderId) && o2.Failure?.Error == AiError.NoKey && forced.Handler.Requests.Count == 0);

        // Whole path with a recorded answer.
        var ok = MakeRig(key: true, consent: true);
        ok.Handler.Reply = () => GeminiOk(Good());
        var run3 = new PhotoRun(ok.Svc, () => cat.Concat(new[] { new Scene { Id = "my-private-look", Name = "Mine", Category = "Mine", Rig = "x" } }));
        var o3 = run3.Analyse(img).GetAwaiter().GetResult();
        Check("run: a recorded good reply becomes a card and a valid Scene", o3.Ok && o3.Scene!.Id == "from-photo" && SceneStore.Validate(o3.Scene) == null && o3.BaseSceneId == "rembrandt");
        Check("run: exactly one request was sent", ok.Handler.Requests.Count == 1);
        var sent = ok.Handler.Requests.FirstOrDefault();
        var b64 = JsonNode.Parse(sent.Body)?["input"]?.AsArray().Select(n => n?["data"]?.GetValue<string>()).FirstOrDefault(x => x != null);
        var sentBytes = b64 == null ? Array.Empty<byte>() : Convert.FromBase64String(b64);
        Check("sent image: is the stripped JPEG (no Exif), at most 1024 on the long side", sentBytes.Length > 0 && sentBytes.SequenceEqual(img.Jpeg) && !Contains(sentBytes, "Exif") && sentBytes[0] == 0xFF);
        Check("sent text: the user's own Scenes are not in the request", !sent.Body.Contains("my-private-look") && sent.Body.Contains("rembrandt"));
        Check("key: the key is not in the request body or any log line", !sent.Body.Contains(Sentinel) && ok.Log.All(l => !l.Contains(Sentinel)));
        Check("button: names the provider and the model", run3.ButtonLabel() == "Analyse with Google Gemini (gemini-test-model)", run3.ButtonLabel());

        // Provider answers that are valid JSON but odd: safe defaults, still a Scene.
        var odd = MakeRig(key: true, consent: true);
        odd.Handler.Reply = () => GeminiOk("""{"keyDirection":"sideways","keySoftness":"crunchy","colours":{"key":"nope"},"closestScene":"???"}""");
        var o4 = new PhotoRun(odd.Svc, () => cat).Analyse(img).GetAwaiter().GetResult();
        Check("run: an odd but well-formed reply still gives a valid Scene with notes", o4.Ok && o4.Notes.Count >= 3 && SceneStore.Validate(o4.Scene!) == null);

        // Typed errors, each with its plain message and nothing thrown.
        var cases = new (int Status, string Body, AiError Want)[]
        {
            (401, """{"error":{"message":"API key not valid"}}""", AiError.BadKey),
            (429, """{"error":{"message":"quota"}}""", AiError.RateLimit),
            (503, """{"error":{"message":"busy"}}""", AiError.ServerError),
            (200, GeminiOk("this is not json at all"), AiError.NotJson),
            (200, GeminiOk("""{"hello":"world"}"""), AiError.SchemaMismatch),
        };
        foreach (var (status, body, want) in cases)
        {
            var r = MakeRig(key: true, consent: true);
            r.Handler.Status = status;
            r.Handler.Reply = () => body;
            var o = new PhotoRun(r.Svc, () => cat).Analyse(img).GetAwaiter().GetResult();
            Check($"errors: {status} gives {want} with its own plain message and no Scene", o.Failure?.Error == want && o.Scene == null && o.Message.Length > 20 && !o.Message.Contains(Sentinel), o.Failure?.Error.ToString() ?? "(none)");
        }

        // Cancel.
        var slow = MakeRig(key: true, consent: true);
        slow.Handler.Reply = () => GeminiOk(Good());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var oc = new PhotoRun(slow.Svc, () => cat).Analyse(img, cancelled.Token).GetAwaiter().GetResult();
        Check("cancel: an already cancelled request ends as Cancelled with no Scene and no exception", oc.Cancelled && oc.Scene == null && oc.Failure == null || oc.Failure != null);

        // Optional face hint hook (part 16): shown as words, never needed.
        var hinted = MakeRig(key: true, consent: true);
        hinted.Handler.Reply = () => GeminiOk(Good());
        var run5 = new PhotoRun(hinted.Svc, () => cat) { FaceHints = new FixedHints() };
        var o5 = run5.Analyse(img).GetAwaiter().GetResult();
        Check("face hook: an optional hint rides along and the run works without it", o5.Ok && o5.FaceHint?.HeadYaw == "profile left" && o3.FaceHint == null);

        // A picture that failed intake never reaches the provider.
        var bad = MakeRig(key: true, consent: true);
        var ob = new PhotoRun(bad.Svc, () => cat).Analyse(PhotoIntake.Prepare(new byte[] { 1, 2, 3 })).GetAwaiter().GetResult();
        Check("run: a picture that failed intake sends nothing", !ob.Ok && bad.Handler.Requests.Count == 0 && ob.Message.Length > 0);
    }

    private sealed class FixedHints : IPhotoFaceHints
    {
        public PhotoFaceHint? Read(byte[] jpeg) => new("profile left", "level", "calm");
    }
}

// Part 6: the optional AI layer, offline. A fake HTTP handler returns canned responses; no real network is touched.
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundstage;
using Soundstage.Ai;

namespace Soundstage.SelfCheck;

public sealed class AiChecks : CheckArea
{
    public override string Name => "AI layer (offline, fake HTTP)";
    public override int Order => 60;

    // Deliberately matches none of the key-shaped patterns in AiLog, so only the saved-key scrub can hide it.
    private const string Sentinel = "zz-sentinel-9f3a7c1e-KEYMATERIAL";
    // Matches the AIza shape, so the shape scrub is tested on its own.
    private const string ShapedKey = "AIzaSyFAKE0123456789abcdefghijklmnopq";

    private static readonly JsonObject Schema = JsonNode.Parse("""
        {"type":"object","properties":{"mood":{"type":"string"},"count":{"type":"integer","minimum":0}},"required":["mood"]}
        """)!.AsObject();
    private static readonly byte[] Png = { 1, 2, 3, 4, 5, 6, 7, 8 };

    // ---- fakes ----------------------------------------------------------------------------------------------------

    private sealed record Seen(HttpMethod Method, string Url, Dictionary<string, string> Headers, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<Seen> Requests = new();
        public Func<int, Seen, HttpResponseMessage> Respond { get; set; } = (_, _) => Reply(200, "{}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            var seen = new Seen(request.Method, request.RequestUri!.ToString(), headers, body);
            Requests.Add(seen);
            return Respond(Requests.Count - 1, seen);
        }
    }

    private static HttpResponseMessage Reply(int status, string body) =>
        new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeProtector : IKeyProtector
    {
        public string Protect(string plain) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plain).Select(b => (byte)(b ^ 0x5A)).Reverse().ToArray());
        public string? Unprotect(string stored)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(stored).Reverse().Select(b => (byte)(b ^ 0x5A)).ToArray()); }
            catch (FormatException) { return null; }
        }
    }

    private static string GeminiOk(string text) =>
        new JsonObject
        {
            ["id"] = "i1",
            ["status"] = "completed",
            ["steps"] = new JsonArray(new JsonObject { ["type"] = "model_output", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }) }),
        }.ToJsonString();

    private static string ChatOk(string text) =>
        new JsonObject { ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text } }) }.ToJsonString();

    private sealed record Rig(AiService Svc, FakeHandler Handler, List<string> Log);

    private static Rig Make(string? key, bool redact = true, IKeyProtector? protector = null, Func<DateTime>? now = null)
    {
        var handler = new FakeHandler();
        var log = new List<string>();
        var svc = new AiService(new AiSettings(), () => { }, log.Add, protector ?? new FakeProtector(), new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, now ?? (() => new DateTime(2026, 11, 21, 10, 0, 0)), redact);
        svc.Settings.For(OpenAiCompatProvider.CustomId).BaseUrl = "https://example.test/v1";
        svc.Settings.For(OpenAiCompatProvider.CustomId).Model = "vision-model";
        svc.Settings.For(OpenAiCompatProvider.OpenRouterId).Model = "vendor/model:free";
        svc.Settings.For(GeminiProvider.ProviderId).Model = "gemini-2.5-flash";
        if (key != null)
            foreach (var id in AiService.ProviderIds) { svc.Keys.Set(id, key); svc.GiveConsent(id); }
        return new Rig(svc, handler, log);
    }

    private static AiResult<JsonObject> Analyse(Rig r, string providerId)
    {
        r.Svc.ProviderId = providerId;
        return r.Svc.AnalyseImage(Png, "image/png", Schema, "Describe the mood.").GetAwaiter().GetResult();
    }

    private static AiTestResult Test(Rig r, string providerId)
    {
        r.Svc.ProviderId = providerId;
        return r.Svc.TestKey().GetAwaiter().GetResult();
    }

    // ---- the checks -----------------------------------------------------------------------------------------------

    public override void Run()
    {
        Messages();
        SchemaAndParser();
        GeminiShapes();
        GeminiErrors();
        ModelPick();
        OpenAiShapes();
        ConsentAndGate();
        KeyStoreChecks();
        UsageCounter();
        LeakSweep();
    }

    private void Messages()
    {
        var all = Enum.GetValues<AiError>();
        var missing = all.Where(e => string.IsNullOrWhiteSpace(AiMessages.For(e, "Google Gemini")) || AiMessages.For(e, "Google Gemini") == "Something went wrong.").ToList();
        Check($"every AiError class ({all.Length}) has its own plain message", missing.Count == 0, string.Join(",", missing));
        var distinct = all.Select(e => AiMessages.For(e, "X")).Distinct().Count();
        Check("the messages are all different", distinct == all.Length, $"{distinct} distinct");
        var jargon = all.Where(e => System.Text.RegularExpressions.Regex.IsMatch(AiMessages.For(e, "X"), @"\b(HTTP|\d{3}|JSON|schema|exception|token|API)\b|\d{3}")).ToList();
        Check("no message carries a status number or jargon word", jargon.Count == 0, string.Join(",", jargon));
        Check("every message ends with something to do (a full stop, one line)", all.All(e => AiMessages.For(e, "X").EndsWith('.') && !AiMessages.For(e, "X").Contains('\n')));
    }

    private void SchemaAndParser()
    {
        Check("schema: good object passes", SchemaValidator.Validate(JsonNode.Parse("""{"mood":"calm","count":2}"""), Schema) == null);
        Check("schema: missing required field fails", SchemaValidator.Validate(JsonNode.Parse("""{"count":2}"""), Schema) is { } a && a.Contains("mood"));
        Check("schema: wrong type fails", SchemaValidator.Validate(JsonNode.Parse("""{"mood":3}"""), Schema) != null);
        Check("schema: a 2.5 is not an integer", SchemaValidator.Validate(JsonNode.Parse("""{"mood":"x","count":2.5}"""), Schema) != null);
        Check("schema: below minimum fails", SchemaValidator.Validate(JsonNode.Parse("""{"mood":"x","count":-1}"""), Schema) != null);
        var en = JsonNode.Parse("""{"type":"string","enum":["a","b"]}""");
        Check("schema: enum accepts a, rejects c", SchemaValidator.Validate(JsonValue.Create("a"), en) == null && SchemaValidator.Validate(JsonValue.Create("c"), en) != null);
        var arr = JsonNode.Parse("""{"type":"array","items":{"type":"string"},"minItems":1}""");
        Check("schema: array items checked, empty array fails minItems",
            SchemaValidator.Validate(JsonNode.Parse("""["a","b"]"""), arr) == null && SchemaValidator.Validate(JsonNode.Parse("""[1]"""), arr) != null && SchemaValidator.Validate(JsonNode.Parse("[]"), arr) != null);

        Check("parser: plain object", ReplyParser.ExtractObject("""{"mood":"calm"}""")?["mood"]?.GetValue<string>() == "calm");
        Check("parser: fenced block", ReplyParser.ExtractObject("Here you go:\n```json\n{\"mood\":\"calm\"}\n```\nEnjoy")?["mood"]?.GetValue<string>() == "calm");
        Check("parser: chatter around a braced object, braces inside a string", ReplyParser.ExtractObject("Sure! {\"mood\":\"a } b\"} done")?["mood"]?.GetValue<string>() == "a } b");
        Check("parser: no object gives null", ReplyParser.ExtractObject("I could not do that") == null && ReplyParser.ExtractObject("") == null && ReplyParser.ExtractObject("[1,2]") == null);
    }

    private void GeminiShapes()
    {
        var r = Make(Sentinel);
        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("{\"mood\":\"calm\",\"count\":2}"));
        var result = Analyse(r, GeminiProvider.ProviderId);
        Check("gemini: a good reply parses to the object", result.Ok && result.Value!["mood"]?.GetValue<string>() == "calm", result.Failure?.ToString() ?? "");

        var seen = r.Handler.Requests.FirstOrDefault();
        Check("gemini: one POST to the interactions endpoint", r.Handler.Requests.Count == 1 && seen?.Method == HttpMethod.Post && seen.Url == "https://generativelanguage.googleapis.com/v1beta/interactions", seen?.Url ?? "");
        Check("gemini: key travels in the x-goog-api-key header", seen?.Headers.GetValueOrDefault("x-goog-api-key") == Sentinel);
        Check("gemini: the key is nowhere in the URL", seen != null && !seen.Url.Contains(Sentinel) && !seen.Url.Contains("key="));
        var body = JsonNode.Parse(seen?.Body ?? "{}")!;
        Check("gemini: body has text + inline image (mime and base64) and a JSON response_format with our schema",
            body["input"]?[0]?["type"]?.GetValue<string>() == "text"
            && body["input"]?[1]?["mime_type"]?.GetValue<string>() == "image/png"
            && body["input"]?[1]?["data"]?.GetValue<string>() == Convert.ToBase64String(Png)
            && body["response_format"]?["mime_type"]?.GetValue<string>() == "application/json"
            && body["response_format"]?["schema"]?["required"]?[0]?.GetValue<string>() == "mood");
        Check("gemini: the key is not in the request body", !(seen?.Body.Contains(Sentinel) ?? true));

        r.Handler.Respond = (_, _) => Reply(200, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{\\\"mood\\\":\\\"warm\\\"}\"}]}}]}");
        Check("gemini: the older candidates shape is read too", Analyse(r, GeminiProvider.ProviderId).Value?["mood"]?.GetValue<string>() == "warm");

        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("I think it is calm."));
        Check("gemini: a reply that is not JSON is NotJson", Analyse(r, GeminiProvider.ProviderId).Failure?.Error == AiError.NotJson);
        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("{\"count\":2}"));
        Check("gemini: JSON that fails the schema is SchemaMismatch", Analyse(r, GeminiProvider.ProviderId).Failure?.Error == AiError.SchemaMismatch);
        r.Handler.Respond = (_, _) => Reply(200, "not json at all");
        Check("gemini: a 200 with a junk body is NotJson, not a crash", Analyse(r, GeminiProvider.ProviderId).Failure?.Error == AiError.NotJson);

        // Blank model: the call lists models first, then uses the one it picked.
        var r2 = Make(Sentinel);
        r2.Svc.Settings.For(GeminiProvider.ProviderId).Model = "";
        r2.Handler.Respond = (i, s) => s.Url.Contains("/models")
            ? Reply(200, """{"models":[{"name":"models/gemini-2.0-flash"},{"name":"models/gemini-2.5-flash"},{"name":"models/text-embedding-004"}]}""")
            : Reply(200, GeminiOk("{\"mood\":\"calm\"}"));
        var ok = Analyse(r2, GeminiProvider.ProviderId);
        var model = JsonNode.Parse(r2.Handler.Requests.Last().Body)?["model"]?.GetValue<string>();
        Check("gemini: a blank model is chosen from the key's own model list", ok.Ok && model == "gemini-2.5-flash", model ?? "");
    }

    private void GeminiErrors()
    {
        const string Region = """{"error":{"code":400,"message":"User location is not supported for the API use.","status":"FAILED_PRECONDITION"}}""";
        const string BadKeyBody = """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}""";
        var table = new (string Name, Func<int, Seen, HttpResponseMessage> Respond, AiError Expect)[]
        {
            ("401", (_, _) => Reply(401, "{}"), AiError.BadKey),
            ("400 key not valid", (_, _) => Reply(400, BadKeyBody), AiError.BadKey),
            ("403 permission denied", (_, _) => Reply(403, """{"error":{"message":"Your project has been denied access.","status":"PERMISSION_DENIED"}}"""), AiError.BadKey),
            ("400 location not supported", (_, _) => Reply(400, Region), AiError.RegionUnavailable),
            ("403 location blocked", (_, _) => Reply(403, """{"error":{"message":"The service is not available in your region."}}"""), AiError.RegionUnavailable),
            ("404", (_, _) => Reply(404, """{"error":{"message":"models/foo is not found for API version v1beta."}}"""), AiError.ModelNotFound),
            ("400 model not found", (_, _) => Reply(400, """{"error":{"message":"model gemini-x does not exist"}}"""), AiError.ModelNotFound),
            ("429", (_, _) => Reply(429, """{"error":{"code":"rate_limit_exceeded","message":"Too many requests"}}"""), AiError.RateLimit),
            ("500", (_, _) => Reply(500, "{}"), AiError.ServerError),
            ("503", (_, _) => Reply(503, "{}"), AiError.ServerError),
            ("504", (_, _) => Reply(504, "{}"), AiError.Timeout),
            ("400 other", (_, _) => Reply(400, """{"error":{"message":"Unsupported mime type"}}"""), AiError.BadRequest),
            ("no connection", (_, _) => throw new HttpRequestException("No such host is known"), AiError.NoNetwork),
            ("timeout", (_, _) => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"), AiError.Timeout),
            ("surprise", (_, _) => throw new InvalidOperationException("boom"), AiError.Unknown),
        };
        foreach (var (name, respond, expect) in table)
        {
            var r = Make(Sentinel);
            r.Handler.Respond = respond;
            var got = Analyse(r, GeminiProvider.ProviderId).Failure?.Error;
            Check($"gemini image call, {name} -> {expect}", got == expect, got?.ToString() ?? "no failure");
        }

        // Test key: good, wrong key, region, offline, rate limit - and the message the button shows.
        var good = Make(Sentinel);
        good.Handler.Respond = (_, _) => Reply(200, """{"models":[{"name":"models/gemini-2.5-flash"},{"name":"models/gemini-2.5-flash-lite"}]}""");
        var t = Test(good, GeminiProvider.ProviderId);
        Check("gemini Test key: good key is Ok, says how many models and which it will use", t.Ok && t.Message.Contains("The key works") && t.Message.Contains("2 models") && t.ChosenModel == "gemini-2.5-flash", t.Message);
        Check("gemini Test key: uses a list call (GET /models) with the key in the header, never the URL",
            good.Handler.Requests.Count == 1 && good.Handler.Requests[0].Method == HttpMethod.Get && good.Handler.Requests[0].Url.Contains("/models") && !good.Handler.Requests[0].Url.Contains("key=") && good.Handler.Requests[0].Headers["x-goog-api-key"] == Sentinel);

        foreach (var (label, respond, expect) in new (string, Func<int, Seen, HttpResponseMessage>, AiError)[]
        {
            ("wrong key", (_, _) => Reply(400, BadKeyBody), AiError.BadKey),
            ("region", (_, _) => Reply(400, Region), AiError.RegionUnavailable),
            ("offline", (_, _) => throw new HttpRequestException("offline"), AiError.NoNetwork),
            ("rate limit", (_, _) => Reply(429, "{}"), AiError.RateLimit),
        })
        {
            var r = Make(Sentinel);
            r.Handler.Respond = respond;
            var res = Test(r, GeminiProvider.ProviderId);
            Check($"gemini Test key: {label} gives its own plain message", !res.Ok && res.Failure?.Error == expect && res.Message == AiMessages.For(expect, "Google Gemini"), res.Message);
        }

        var nokey = Make(null);
        Check("Test key with no key saved says so and sends nothing", Test(nokey, GeminiProvider.ProviderId) is { Ok: false } nk && nk.Failure?.Error == AiError.NoKey && nokey.Handler.Requests.Count == 0);
    }

    private void ModelPick()
    {
        var names = new[] { "models/gemini-2.0-flash", "models/gemini-2.5-flash-lite", "models/gemini-2.5-flash", "models/gemini-3-flash-preview", "models/gemini-2.5-flash-image", "models/gemini-2.5-pro", "models/text-embedding-004", "models/gemma-3-27b-it" };
        Check("model pick: stable flash with the highest version, not lite, image, preview or embedding", GeminiProvider.PickDefaultModel(names) == "gemini-2.5-flash", GeminiProvider.PickDefaultModel(names) ?? "null");
        Check("model pick: a preview is used when it is all there is", GeminiProvider.PickDefaultModel(new[] { "models/gemini-3-flash-preview" }) == "gemini-3-flash-preview");
        Check("model pick: nothing usable gives null (the fallback name is then used)", GeminiProvider.PickDefaultModel(new[] { "models/text-embedding-004" }) == null && GeminiProvider.PickDefaultModel(Array.Empty<string>()) == null);
    }

    private void OpenAiShapes()
    {
        var r = Make(Sentinel);
        r.Handler.Respond = (_, _) => Reply(200, ChatOk("{\"mood\":\"calm\"}"));
        var result = Analyse(r, OpenAiCompatProvider.OpenRouterId);
        var seen = r.Handler.Requests.FirstOrDefault();
        Check("openrouter: a good reply parses", result.Ok && result.Value!["mood"]?.GetValue<string>() == "calm", result.Failure?.ToString() ?? "");
        Check("openrouter: POST /chat/completions with the key as a Bearer header, none in the URL", seen != null && seen.Url == "https://openrouter.ai/api/v1/chat/completions" && seen.Headers["Authorization"] == "Bearer " + Sentinel && !seen.Url.Contains(Sentinel), seen?.Url ?? "");
        var body = JsonNode.Parse(seen?.Body ?? "{}")!;
        var imageUrl = body["messages"]?[0]?["content"]?[1]?["image_url"]?["url"]?.GetValue<string>();
        Check("openrouter: image goes as a data URL in the standard message form", imageUrl == "data:image/png;base64," + Convert.ToBase64String(Png) && body["messages"]?[0]?["content"]?[1]?["type"]?.GetValue<string>() == "image_url", imageUrl ?? "");
        Check("openrouter: structured output asked for with json_schema + our schema", body["response_format"]?["type"]?.GetValue<string>() == "json_schema" && body["response_format"]?["json_schema"]?["schema"]?["required"]?[0]?.GetValue<string>() == "mood");

        // Endpoint without structured output: 400 mentioning response_format, then the plain JSON-only repeat succeeds.
        var plain = Make(Sentinel);
        plain.Handler.Respond = (i, s) => s.Body.Contains("response_format")
            ? Reply(400, """{"error":{"message":"response_format is not supported by this model"}}""")
            : Reply(200, ChatOk("Sure!\n```json\n{\"mood\":\"calm\"}\n```"));
        var fallback = Analyse(plain, OpenAiCompatProvider.CustomId);
        Check("custom: falls back to a JSON-only instruction when response_format is refused", fallback.Ok && plain.Handler.Requests.Count == 2 && !plain.Handler.Requests[1].Body.Contains("response_format") && plain.Handler.Requests[1].Body.Contains("JSON object only"), $"{plain.Handler.Requests.Count} requests");
        Analyse(plain, OpenAiCompatProvider.CustomId);
        Check("custom: and remembers it, so the next call needs one request", plain.Handler.Requests.Count == 3);

        plain.Handler.Respond = (_, _) => Reply(200, "{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"mood\\\":\\\"calm\\\"}\"}]}}]}");
        Check("custom: content given as a list of text parts is read", Analyse(plain, OpenAiCompatProvider.CustomId).Ok);

        var unsafeUrl = Make(Sentinel);
        unsafeUrl.Svc.Settings.For(OpenAiCompatProvider.CustomId).BaseUrl = "http://example.test/v1";
        var refused = Analyse(unsafeUrl, OpenAiCompatProvider.CustomId);
        Check("custom: a plain-http address on another host is refused before anything is sent", refused.Failure?.Error == AiError.NotConfigured && unsafeUrl.Handler.Requests.Count == 0);
        unsafeUrl.Svc.Settings.For(OpenAiCompatProvider.CustomId).BaseUrl = "http://localhost:1234/v1";
        unsafeUrl.Svc.GiveConsent(OpenAiCompatProvider.CustomId);
        unsafeUrl.Handler.Respond = (_, _) => Reply(200, ChatOk("{\"mood\":\"calm\"}"));
        Check("custom: http to this machine (a local server) is allowed", Analyse(unsafeUrl, OpenAiCompatProvider.CustomId).Ok && unsafeUrl.Handler.Requests.Count == 1);

        var noModel = Make(Sentinel);
        noModel.Svc.Settings.For(OpenAiCompatProvider.CustomId).Model = "";
        Check("custom: no model name is NotConfigured, nothing sent", Analyse(noModel, OpenAiCompatProvider.CustomId).Failure?.Error == AiError.NotConfigured && noModel.Handler.Requests.Count == 0);

        foreach (var (name, status, expect) in new[] { ("401", 401, AiError.BadKey), ("429", 429, AiError.RateLimit), ("402 no credits", 402, AiError.RateLimit), ("404", 404, AiError.ModelNotFound), ("502", 502, AiError.ServerError) })
        {
            var e = Make(Sentinel);
            e.Handler.Respond = (_, _) => Reply(status, """{"error":{"code":0,"message":"x"}}""");
            Check($"openrouter image call, {name} -> {expect}", Analyse(e, OpenAiCompatProvider.OpenRouterId).Failure?.Error == expect);
        }

        // Test key on OpenRouter: /key for the free daily count, /models for a free vision model.
        var tk = Make(Sentinel);
        tk.Svc.Settings.For(OpenAiCompatProvider.OpenRouterId).Model = "";
        tk.Handler.Respond = (_, s) => s.Url.EndsWith("/key")
            ? Reply(200, """{"data":{"label":"x","free_model_daily_requests":{"used":3,"limit":50,"remaining":47}}}""")
            : Reply(200, """{"data":[{"id":"paid/model","architecture":{"input_modalities":["text","image"]}},{"id":"vendor/text-only:free","architecture":{"input_modalities":["text"]}},{"id":"vendor/vision:free","architecture":{"input_modalities":["text","image"]}}]}""");
        var tr = Test(tk, OpenAiCompatProvider.OpenRouterId);
        Check("openrouter Test key: Ok, 47 left today, suggests a :free model that takes images", tr.Ok && tr.RemainingToday == 47 && tr.ChosenModel == "vendor/vision:free" && tr.Message.Contains("47"), tr.Message);
        Check("openrouter Test key: both calls carry the Bearer header", tk.Handler.Requests.All(q => q.Headers["Authorization"] == "Bearer " + Sentinel));

        var bad = Make(Sentinel);
        bad.Handler.Respond = (_, _) => Reply(401, """{"error":{"code":401,"message":"No auth credentials found"}}""");
        var br = Test(bad, OpenAiCompatProvider.OpenRouterId);
        Check("openrouter Test key: wrong key gives the bad-key message", !br.Ok && br.Message == AiMessages.For(AiError.BadKey, "OpenRouter"), br.Message);

        var noList = Make(Sentinel);
        noList.Handler.Respond = (_, _) => Reply(404, "{}");
        var nl = Test(noList, OpenAiCompatProvider.CustomId);
        Check("custom Test key: an endpoint with no model list is reported as connected but unchecked, not as a missing model", nl.Ok && nl.Message.Contains("no model list"), nl.Message);
    }

    private void ConsentAndGate()
    {
        var r = Make(Sentinel);
        foreach (var id in AiService.ProviderIds) r.Svc.WithdrawConsent(id);
        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("{\"mood\":\"calm\"}"));

        var blocked = Analyse(r, GeminiProvider.ProviderId);
        Check("consent: with no consent recorded nothing is sent", blocked.Failure?.Error == AiError.ConsentNeeded && r.Handler.Requests.Count == 0);
        r.Handler.Respond = (_, _) => Reply(200, """{"models":[{"name":"models/gemini-2.5-flash"}]}""");
        Check("consent: declining leaves Test key usable (it sends no picture)", Test(r, GeminiProvider.ProviderId).Ok);
        r.Handler.Requests.Clear();

        r.Svc.GiveConsent(GeminiProvider.ProviderId);
        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("{\"mood\":\"calm\"}"));
        Check("consent: after a yes the picture goes", Analyse(r, GeminiProvider.ProviderId).Ok && r.Handler.Requests.Count == 1);

        r.Svc.ProviderId = OpenAiCompatProvider.OpenRouterId;
        Check("consent: a yes for Gemini does not cover OpenRouter (changing provider asks again)", !r.Svc.HasConsent(OpenAiCompatProvider.OpenRouterId) && Analyse(r, OpenAiCompatProvider.OpenRouterId).Failure?.Error == AiError.ConsentNeeded);

        r.Svc.GiveConsent(OpenAiCompatProvider.CustomId);
        r.Svc.Settings.For(OpenAiCompatProvider.CustomId).BaseUrl = "https://other.test/v1";
        Check("consent: a custom endpoint on a new host asks again", !r.Svc.HasConsent(OpenAiCompatProvider.CustomId));

        r.Svc.Settings.Consents.First(c => c.ProviderId == GeminiProvider.ProviderId).TextVersion = Consent.TextVersion - 1;
        Check("consent: a yes to an older wording version no longer counts", !r.Svc.HasConsent(GeminiProvider.ProviderId));

        var text = Consent.Text(GeminiProvider.ProviderId, "Google Gemini", "gemini-2.5-flash", "");
        Check("consent text names the provider and model, says it leaves the machine and mentions free-tier data use",
            text.Contains("Google Gemini") && text.Contains("gemini-2.5-flash") && text.Contains("leaves your computer") && text.Contains("free tier"));

        var nokey = Make(null);
        Check("no key: AnalyseImage says NoKey and sends nothing", nokey.Svc.AnalyseImage(Png, "image/png", Schema, "x").GetAwaiter().GetResult().Failure?.Error == AiError.NoKey && nokey.Handler.Requests.Count == 0);
    }

    private void KeyStoreChecks()
    {
        var settings = new AiSettings();
        var saves = 0;
        var store = new KeyStore(settings, new FakeProtector(), () => saves++);
        store.Set("gemini", "  " + Sentinel + "  ");
        Check("key store: a key is trimmed, stored and read back", store.Get("gemini") == Sentinel && saves == 1);
        var json = JsonSerializer.Serialize(settings);
        Check("key store: the saved settings JSON has no plain-text key", !json.Contains(Sentinel) && json.Contains("ProtectedKey"));

        var reloaded = new KeyStore(JsonSerializer.Deserialize<AiSettings>(json)!, new FakeProtector(), () => { });
        Check("key store: a fresh store reading that JSON gets the key back", reloaded.Get("gemini") == Sentinel);
        var broken = JsonSerializer.Deserialize<AiSettings>(json)!;
        broken.For("gemini").ProtectedKey = "!!!not base64!!!";
        Check("key store: an unreadable stored key reads as missing, not as a crash", new KeyStore(broken, new FakeProtector(), () => { }).Get("gemini") == null);
        store.Clear("gemini");
        Check("key store: forget removes it", store.Get("gemini") == null && settings.For("gemini").ProtectedKey == "");

        if (OperatingSystem.IsWindows())
        {
            var dpapi = new DpapiKeyProtector();
            var cipher = dpapi.Protect(Sentinel);
            Check("DPAPI: round trip returns the key, ciphertext is not the key", dpapi.Unprotect(cipher) == Sentinel && !cipher.Contains(Sentinel) && !Encoding.UTF8.GetString(Convert.FromBase64String(cipher)).Contains(Sentinel));
            Check("DPAPI: garbage reads as missing", dpapi.Unprotect("AAAA") == null && dpapi.Unprotect("not base64!") == null);
            var real = new AiSettings();
            new KeyStore(real, dpapi, () => { }).Set("gemini", Sentinel);
            Check("DPAPI: settings JSON written with the real protector holds no plain-text key", !JsonSerializer.Serialize(real).Contains(Sentinel));
        }
    }

    private void UsageCounter()
    {
        var today = new DateTime(2026, 11, 21, 10, 0, 0);
        var r = Make(Sentinel, now: () => today);
        r.Handler.Respond = (_, _) => Reply(200, GeminiOk("{\"mood\":\"calm\"}"));
        Analyse(r, GeminiProvider.ProviderId);
        Analyse(r, GeminiProvider.ProviderId);
        Check("usage: two requests today count two", r.Svc.UsedToday(GeminiProvider.ProviderId) == 2);
        today = today.AddDays(1);
        Check("usage: the next day starts at zero", r.Svc.UsedToday(GeminiProvider.ProviderId) == 0);
        var other = Make(Sentinel);
        other.Handler.Respond = (_, _) => Reply(200, ChatOk("{\"mood\":\"calm\"}"));
        Analyse(other, OpenAiCompatProvider.OpenRouterId);
        Check("usage: counted per provider", other.Svc.UsedToday(OpenAiCompatProvider.OpenRouterId) == 1 && other.Svc.UsedToday(GeminiProvider.ProviderId) == 0);
    }

    // ---- the key never leaks ---------------------------------------------------------------------------------------

    private static readonly (string Name, Func<string, Func<int, Seen, HttpResponseMessage>> Make)[] Hostile =
    {
        ("400 echoing the key", k => (_, _) => Reply(400, $"{{\"error\":{{\"message\":\"API key not valid: {k}\"}}}}")),
        ("401 echoing a header", k => (_, _) => Reply(401, $"Authorization: Bearer {k}\nx-goog-api-key: {k}")),
        ("403 key in query string text", k => (_, _) => Reply(403, $"denied for https://x.test/v1?key={k}&alt=json")),
        ("404 echoing the key", k => (_, _) => Reply(404, $"{{\"error\":{{\"message\":\"model for {k} not found\"}}}}")),
        ("429 echoing the key", k => (_, _) => Reply(429, $"{{\"error\":{{\"message\":\"quota for {k}\"}}}}")),
        ("500 plain text with the key", k => (_, _) => Reply(500, $"upstream failure for {k}")),
        ("200 not JSON, key in the text", k => (_, _) => Reply(200, GeminiOk($"I cannot do that, {k}"))),
        ("200 JSON failing schema, key in the text", k => (_, _) => Reply(200, GeminiOk($"{{\"count\":1,\"note\":\"{k}\"}}"))),
        ("chat 200 not JSON, key in the text", k => (_, _) => Reply(200, ChatOk($"nope {k}"))),
        ("connection error naming the key", k => (_, _) => throw new HttpRequestException($"Name resolution failed for host with key {k}")),
        ("timeout message naming the key", k => (_, _) => throw new TaskCanceledException($"timed out sending {k}")),
        ("surprise exception naming the key", k => (_, _) => throw new InvalidOperationException($"bad state {k}")),
        ("model list 200 with the key as a name", k => (_, _) => Reply(200, $"{{\"models\":[{{\"name\":\"models/{k}\"}}],\"data\":[{{\"id\":\"{k}\"}}]}}")),
        ("model list 400 about region", k => (_, _) => Reply(400, $"User location is not supported for the API use. ({k})")),
    };

    /// <summary>Runs every provider through every hostile reply, for the image call and Test key, and returns each output line that holds the key.</summary>
    private static List<string> Sweep(string key, bool redact)
    {
        var leaks = new List<string>();
        void Scan(string source, string? text)
        {
            if (text != null && text.Contains(key, StringComparison.Ordinal)) leaks.Add($"{source}: {AiLog.Clip(text, 160)}");
        }
        foreach (var (name, makeRespond) in Hostile)
            foreach (var provider in AiService.ProviderIds)
            {
                var r = Make(key, redact);
                r.Handler.Respond = makeRespond(key);
                var image = Analyse(r, provider);
                Scan($"[{provider}/{name}] failure detail", image.Failure?.Detail);
                if (image.Failure != null) Scan($"[{provider}/{name}] message", AiMessages.For(image.Failure, AiService.NameOf(provider)));
                if (image.Value != null) Scan($"[{provider}/{name}] parsed value kept the key", image.Value.ToJsonString());
                var test = Test(r, provider);
                Scan($"[{provider}/{name}] Test key message", test.Message);
                Scan($"[{provider}/{name}] Test key detail", test.Failure?.Detail);
                foreach (var line in r.Log) Scan($"[{provider}/{name}] log line", line);
                // Everything the config would write.
                Scan($"[{provider}/{name}] settings JSON", JsonSerializer.Serialize(r.Svc.Settings));
            }
        return leaks;
    }

    private void LeakSweep()
    {
        var scenarios = Hostile.Length * AiService.ProviderIds.Length;

        var clean = Sweep(Sentinel, redact: true);
        Check($"LEAK: a sentinel key fed through {scenarios} provider/response scenarios (image call + Test key) appears in no log line, failure detail, message or saved setting",
            clean.Count == 0, clean.FirstOrDefault() ?? "");

        var shaped = Sweep(ShapedKey, redact: true);
        Check("LEAK: a key shaped like a real Google key (AIza...) is hidden too", shaped.Count == 0, shaped.FirstOrDefault() ?? "");

        // Control (clause 2): the same sweep with redaction off must find leaks, or the check above could never fail.
        var control = Sweep(Sentinel, redact: false);
        Check("LEAK control: with redaction switched off the same sweep DOES find the key", control.Count > 0, control.Count > 0 ? $"{control.Count} leaking outputs, first: {control[0]}" : "no leak found, so the check above proves nothing");
        Check("LEAK control: the leak is in a log line (the place redaction exists for)", control.Any(l => l.Contains("log line")), "");

        // The scrub on its own.
        var rig = Make(Sentinel);
        var redactor = rig.Svc.Log;
        Check("redact: the saved key (raw and URL-escaped) is replaced", !redactor.Redact($"a {Sentinel} b {Uri.EscapeDataString(Sentinel + " &")}").Contains(Sentinel) && redactor.Redact($"x {Sentinel}").Contains(AiLog.Mask));
        Check("redact: Authorization and x-goog-api-key header values and ?key= are hidden even for an unsaved key",
            !redactor.Redact("Authorization: Bearer abc123SECRET").Contains("abc123SECRET")
            && !redactor.Redact("x-goog-api-key: abc123SECRET").Contains("abc123SECRET")
            && !redactor.Redact("GET https://h.test/m?alt=json&key=abc123SECRET").Contains("abc123SECRET")
            && !redactor.Redact("token sk-or-v1-0123456789abcdef0123456789").Contains("0123456789abcdef"));
        Check("redact: ordinary text is left alone", redactor.Redact("Gemini image request, model gemini-2.5-flash, image 1234 bytes") == "Gemini image request, model gemini-2.5-flash, image 1234 bytes");

        // Scene, share-code, pose-tag and preset types hold no key by construction.
        var holders = typeof(AiSettings).Assembly.GetTypes();
        var keyTypes = new HashSet<Type> { typeof(AiSettings), typeof(ProviderSettings), typeof(KeyStore), typeof(AiService) };
        var risky = holders
            .Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name, @"^(Scene|ShareCode|PoseTag|Preset)"))
            .SelectMany(t => t.GetProperties().Select(p => (Type: t, Name: p.Name, p.PropertyType)).Concat(t.GetFields().Select(f => (Type: t, Name: f.Name, PropertyType: f.FieldType))))
            .Where(m => keyTypes.Contains(m.PropertyType) || System.Text.RegularExpressions.Regex.IsMatch(m.Name, @"(?i)api.?key|secret|bearer|authorization|credential"))
            .Where(m => !(m.Type == typeof(ShareCode) && m.Name == "SecretValues"))   // a Func hook that is handed the stored key at call time; it holds none (proved by the share-code row below)
            .Select(m => $"{m.Type.Name}.{m.Name}")
            .ToList();
        var checkedTypes = holders.Count(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name, @"^(Scene|ShareCode|PoseTag|Preset)"));
        Check($"no Scene, share-code, pose-tag or Preset type ({checkedTypes} found today) has a member that can hold a key", risky.Count == 0 && checkedTypes > 0, string.Join(",", risky));

        // Share codes refuse the stored key by value (part 7 hook, wired by AiService.GuardShareCodes).
        {
            var shareRig = Make(Sentinel);
            var priorHook = ShareCode.SecretValues;
            try
            {
                var carrier = SceneStore.BuiltInWarmKeyCoolFill();
                carrier.Name = "Mine " + Sentinel;
                ShareCode.SecretValues = () => Array.Empty<string>();
                var okBefore = ShareCode.TryEncode(carrier, out var plainCode, out _);
                Check("share code control: with no guard wired, a Scene carrying the key value is NOT refused (so the refusal below is the guard's)", okBefore);
                AiService.GuardShareCodes(() => shareRig.Svc);
                var refused = !ShareCode.TryEncode(carrier, out _, out var why);
                Check("share code: encoding a Scene that contains the stored key value is refused", refused && why.Contains("your stored key"), why);
                var decoded = ShareCode.Decode(plainCode);
                Check("share code: decoding a pasted code that contains the stored key value is refused", decoded.Scene == null && (decoded.Error ?? "").Contains("your stored key"), decoded.Error ?? "decoded");
                Check("share code: a clean Scene still encodes with the guard wired", ShareCode.TryEncode(SceneStore.BuiltInWarmKeyCoolFill(), out _, out _));
            }
            finally { ShareCode.SecretValues = priorHook; }
        }

        var preset = JsonSerializer.Serialize(SceneStore.BuiltInWarmKeyCoolFill(), SceneStore.WriteOptions);
        Check("a serialised Scene holds no key (sentinel absent)", !preset.Contains(Sentinel) && preset.Length > 20);
    }
}

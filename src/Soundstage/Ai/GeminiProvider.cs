using System.Text.Json.Nodes;

namespace Soundstage.Ai;

/// <summary>
/// Google Gemini through the Gemini API, with a key from Google AI Studio. Shapes follow the current official docs (URLs
/// and the date they were read are in docs/ai-providers.md): the Interactions endpoint for the image call, the models
/// list for Test key. The key goes in the x-goog-api-key header, never the URL. The model list comes from the API and the
/// default is picked from what the key can see (<see cref="PickDefaultModel"/>), with an editable fallback.
/// </summary>
public sealed class GeminiProvider : IAiProvider
{
    public const string ProviderId = "gemini";
    public const string DefaultBaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>The Api-Revision header value the Interactions docs showed on 21/11/2026. Change here if Google moves it.</summary>
    public const string ApiRevision = "2026-05-20";

    /// <summary>Used when the key's model list yields nothing usable and the user has typed no model.</summary>
    public const string FallbackModel = "gemini-flash-latest";

    private readonly HttpClient http;
    private readonly Func<string?> key;
    private readonly Func<string> model;
    private readonly AiLog log;
    private readonly string baseUrl;

    public GeminiProvider(HttpClient http, Func<string?> key, Func<string> model, AiLog log, string baseUrl = DefaultBaseUrl)
    {
        this.http = http;
        this.key = key;
        this.model = model;
        this.log = log;
        this.baseUrl = baseUrl.TrimEnd('/');
    }

    public string Id => ProviderId;
    public string DisplayName => "Google Gemini";

    private Dictionary<string, string> Headers(string apiKey) => new()
    {
        ["x-goog-api-key"] = apiKey,
        ["Api-Revision"] = ApiRevision,
    };

    public async Task<AiResult<JsonObject>> AnalyseImage(byte[] imageBytes, string mime, JsonObject schema, string instruction, CancellationToken ct = default)
    {
        var apiKey = key();
        if (string.IsNullOrWhiteSpace(apiKey)) return AiResult<JsonObject>.Fail(AiError.NoKey);
        if (!AiHttp.IsSafeBaseUrl(baseUrl)) return AiResult<JsonObject>.Fail(AiError.NotConfigured, "base address is not https");

        var chosen = model();
        if (string.IsNullOrWhiteSpace(chosen))
        {
            var listed = await ListModels(apiKey, ct).ConfigureAwait(false);
            chosen = listed.Failure != null ? FallbackModel : PickDefaultModel(listed.Names) ?? FallbackModel;
        }

        var body = new JsonObject
        {
            ["model"] = chosen,
            ["input"] = new JsonArray
            {
                new JsonObject { ["type"] = "text", ["text"] = instruction },
                new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(imageBytes), ["mime_type"] = mime },
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = schema.DeepClone(),
            },
        };

        log.Info($"Gemini image request, model {chosen}, image {imageBytes.Length} bytes");
        var reply = await AiHttp.Send(http, HttpMethod.Post, baseUrl + "/interactions", Headers(apiKey), body.ToJsonString(), log, ct).ConfigureAwait(false);
        if (reply.Failure != null) return AiResult<JsonObject>.Fail(reply.Failure);
        return Interpret(reply.Body, schema, log);
    }

    /// <summary>Turns a success body into a validated object. Accepts the Interactions shape, and the older candidates shape as a fallback.</summary>
    public static AiResult<JsonObject> Interpret(string body, JsonObject schema, AiLog log)
    {
        string? text = null;
        try
        {
            if (JsonNode.Parse(body) is JsonObject root)
            {
                if (root["steps"] is JsonArray steps)
                    foreach (var step in steps.OfType<JsonObject>())
                        if (step["type"]?.GetValue<string>() == "model_output" && step["content"] is JsonArray parts)
                            foreach (var part in parts.OfType<JsonObject>())
                                if (part["type"]?.GetValue<string>() == "text" && part["text"] is JsonValue t && t.TryGetValue<string>(out var s)) text = (text ?? "") + s;
                if (text == null && root["candidates"] is JsonArray cands && cands.Count > 0 && cands[0]?["content"]?["parts"] is JsonArray cparts)
                    foreach (var part in cparts.OfType<JsonObject>())
                        if (part["text"] is JsonValue t2 && t2.TryGetValue<string>(out var s2)) text = (text ?? "") + s2;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }

        return Finish(text, schema, log);
    }

    internal static AiResult<JsonObject> Finish(string? text, JsonObject schema, AiLog log)
    {
        var obj = ReplyParser.ExtractObject(text);
        if (obj == null)
        {
            log.Info("reply was not JSON: " + AiLog.Clip(text ?? "(no text)", 120));
            return AiResult<JsonObject>.Fail(AiError.NotJson, "no JSON object in the reply");
        }
        var problem = SchemaValidator.Validate(obj, schema);
        if (problem != null)
        {
            log.Info("reply failed the schema: " + problem);
            return AiResult<JsonObject>.Fail(AiError.SchemaMismatch, problem);
        }
        return AiResult<JsonObject>.Success(obj);
    }

    public async Task<AiTestResult> TestKey(CancellationToken ct = default)
    {
        var apiKey = key();
        if (string.IsNullOrWhiteSpace(apiKey)) return Failed(new AiFailure(AiError.NoKey));
        if (!AiHttp.IsSafeBaseUrl(baseUrl)) return Failed(new AiFailure(AiError.NotConfigured, "base address is not https"));

        var listed = await ListModels(apiKey, ct).ConfigureAwait(false);
        if (listed.Failure != null) return Failed(listed.Failure);

        var picked = PickDefaultModel(listed.Names) ?? FallbackModel;
        var current = model();
        var using_ = string.IsNullOrWhiteSpace(current) ? picked : current;
        var note = string.IsNullOrWhiteSpace(current) ? $"Soundstage will use {picked}." : $"Soundstage will use {current}.";
        return new AiTestResult(true, $"The key works. Google lists {listed.Names.Count} models for it. {note}", null, using_, listed.Names);
    }

    private AiTestResult Failed(AiFailure f) => new(false, AiMessages.For(f, DisplayName), f);

    private async Task<(IReadOnlyList<string> Names, AiFailure? Failure)> ListModels(string apiKey, CancellationToken ct)
    {
        var names = new List<string>();
        string? token = null;
        for (var page = 0; page < 5; page++)
        {
            var url = baseUrl + "/models?pageSize=100" + (token == null ? "" : "&pageToken=" + Uri.EscapeDataString(token));
            var reply = await AiHttp.Send(http, HttpMethod.Get, url, Headers(apiKey), null, log, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (reply.Failure != null) return (names, reply.Failure);
            try
            {
                var root = JsonNode.Parse(reply.Body) as JsonObject;
                if (root?["models"] is JsonArray models)
                    foreach (var m in models.OfType<JsonObject>())
                        if (m["name"] is JsonValue n && n.TryGetValue<string>(out var name)) names.Add(name);
                token = root?["nextPageToken"]?.GetValue<string>();
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
            {
                return (names, new AiFailure(AiError.NotJson, "model list was not JSON"));
            }
            if (string.IsNullOrEmpty(token)) break;
        }
        return (names, null);
    }

    private static readonly string[] Skip =
    {
        "embedding", "imagen", "veo", "tts", "aqa", "image", "live", "audio", "robotics", "computer-use", "learnlm", "gemma", "lite", "thinking",
    };

    /// <summary>
    /// The best everyday vision model among the key's models: a Gemini "flash" (not lite, image, audio or live), a stable
    /// name over a preview or experimental one, then the highest version number. Null when nothing fits.
    /// </summary>
    public static string? PickDefaultModel(IEnumerable<string> names)
    {
        var candidates = names
            .Select(n => n.StartsWith("models/", StringComparison.Ordinal) ? n[7..] : n)
            .Where(n => n.StartsWith("gemini", StringComparison.OrdinalIgnoreCase) && n.Contains("flash", StringComparison.OrdinalIgnoreCase))
            .Where(n => !Skip.Any(s => n.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (candidates.Count == 0) return null;
        return candidates
            .OrderBy(n => n.Contains("preview", StringComparison.OrdinalIgnoreCase) || n.Contains("exp", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(Version)
            .ThenBy(n => n.Length)
            .ThenBy(n => n, StringComparer.Ordinal)
            .First();
    }

    private static double Version(string name)
    {
        var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d+(?:\.\d+)?)");
        return m.Success && double.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}

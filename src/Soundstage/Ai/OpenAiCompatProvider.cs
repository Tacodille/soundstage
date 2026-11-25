using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Soundstage.Ai;

/// <summary>
/// Any OpenAI-compatible chat-completions endpoint. OpenRouter is the preset (<see cref="OpenRouterBaseUrl"/>); "custom" is
/// whatever base address the user types (a local server, another host). The image goes as a data URL in the standard
/// message form. Structured output is requested with response_format json_schema; if the endpoint rejects that, the call
/// is repeated once asking for JSON in plain words, and the tolerant parser plus our own schema check do the rest.
/// A key is only ever sent over https, or over http to this machine.
/// </summary>
public sealed class OpenAiCompatProvider : IAiProvider
{
    public const string OpenRouterId = "openrouter";
    public const string CustomId = "custom";
    public const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    private readonly HttpClient http;
    private readonly Func<string?> key;
    private readonly Func<string> baseUrl;
    private readonly Func<string> model;
    private readonly AiLog log;
    private bool schemaRejected;

    public OpenAiCompatProvider(string id, string displayName, HttpClient http, Func<string?> key, Func<string> baseUrl, Func<string> model, AiLog log)
    {
        Id = id;
        DisplayName = displayName;
        this.http = http;
        this.key = key;
        this.baseUrl = baseUrl;
        this.model = model;
        this.log = log;
    }

    public string Id { get; }
    public string DisplayName { get; }

    private string Base => baseUrl().Trim().TrimEnd('/');
    private bool IsOpenRouter => Id == OpenRouterId || Base.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Headers(string apiKey) => new() { ["Authorization"] = "Bearer " + apiKey };

    public async Task<AiResult<JsonObject>> AnalyseImage(byte[] imageBytes, string mime, JsonObject schema, string instruction, CancellationToken ct = default)
    {
        var apiKey = key();
        if (string.IsNullOrWhiteSpace(apiKey)) return AiResult<JsonObject>.Fail(AiError.NoKey);
        if (!AiHttp.IsSafeBaseUrl(Base)) return AiResult<JsonObject>.Fail(AiError.NotConfigured, "base address must be https (or http to this machine)");
        var chosen = model().Trim();
        if (chosen.Length == 0) return AiResult<JsonObject>.Fail(AiError.NotConfigured, "no model name");

        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(imageBytes)}";
        var useSchema = !schemaRejected;
        log.Info($"{DisplayName} image request, model {chosen}, image {imageBytes.Length} bytes, structured output {(useSchema ? "on" : "off")}");

        var reply = await Post(apiKey, chosen, dataUrl, schema, instruction, useSchema, ct).ConfigureAwait(false);
        if (useSchema && reply.Failure is { Error: AiError.BadRequest or AiError.ModelNotFound } f && f.HttpStatus == 400 && LooksLikeSchemaRejection(reply.Body))
        {
            log.Info("endpoint rejected response_format; repeating with a plain JSON-only instruction");
            schemaRejected = true;
            reply = await Post(apiKey, chosen, dataUrl, schema, instruction, false, ct).ConfigureAwait(false);
        }
        if (reply.Failure != null) return AiResult<JsonObject>.Fail(reply.Failure);
        return Interpret(reply.Body, schema, log);
    }

    private static bool LooksLikeSchemaRejection(string body) =>
        Regex.IsMatch(body, @"(?i)(response_format|json_schema|structured|json_object)");

    private Task<AiHttpReply> Post(string apiKey, string chosen, string dataUrl, JsonObject schema, string instruction, bool useSchema, CancellationToken ct)
    {
        var text = useSchema ? instruction
            : instruction + "\n\nReply with a single JSON object only, no other text, matching this JSON Schema:\n" + schema.ToJsonString();
        var body = new JsonObject
        {
            ["model"] = chosen,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = text },
                        new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = dataUrl } },
                    },
                },
            },
        };
        if (useSchema)
            body["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject { ["name"] = "soundstage_reply", ["strict"] = true, ["schema"] = schema.DeepClone() },
            };
        return AiHttp.Send(http, HttpMethod.Post, Base + "/chat/completions", Headers(apiKey), body.ToJsonString(), log, ct);
    }

    /// <summary>Success body to a validated object: reads choices[0].message.content (a string, or a list of text parts).</summary>
    public static AiResult<JsonObject> Interpret(string body, JsonObject schema, AiLog log)
    {
        string? text = null;
        try
        {
            if (JsonNode.Parse(body) is JsonObject root && root["choices"] is JsonArray choices && choices.Count > 0)
            {
                var content = choices[0]?["message"]?["content"];
                if (content is JsonValue v && v.TryGetValue<string>(out var s)) text = s;
                else if (content is JsonArray parts)
                    text = string.Concat(parts.OfType<JsonObject>().Select(p => p["text"] is JsonValue tv && tv.TryGetValue<string>(out var ts) ? ts : ""));
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        return GeminiProvider.Finish(text, schema, log);
    }

    public async Task<AiTestResult> TestKey(CancellationToken ct = default)
    {
        var apiKey = key();
        if (string.IsNullOrWhiteSpace(apiKey)) return Failed(new AiFailure(AiError.NoKey));
        if (!AiHttp.IsSafeBaseUrl(Base)) return Failed(new AiFailure(AiError.NotConfigured, "base address must be https (or http to this machine)"));

        int? remaining = null;
        if (IsOpenRouter)
        {
            // /key is how OpenRouter checks a key and reports the free daily count (docs/ai-providers.md).
            var keyReply = await AiHttp.Send(http, HttpMethod.Get, Base + "/key", Headers(apiKey), null, log, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (keyReply.Failure != null) return Failed(keyReply.Failure);
            remaining = ParseRemaining(keyReply.Body);
        }

        var reply = await AiHttp.Send(http, HttpMethod.Get, Base + "/models", Headers(apiKey), null, log, ct, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        if (reply.Failure is { HttpStatus: 404 or 405 })
        {
            return new AiTestResult(true, $"Connected to {DisplayName}, but it has no model list, so the key itself was not checked. Type the model name yourself.", null, null, null, remaining);
        }
        if (reply.Failure != null) return Failed(reply.Failure);

        var (all, vision) = ParseModels(reply.Body);
        var current = model().Trim();
        string? suggestion = current.Length > 0 ? current : (IsOpenRouter ? vision.FirstOrDefault(m => m.EndsWith(":free", StringComparison.Ordinal)) : vision.FirstOrDefault());
        var extra = remaining is int r ? $" You have {r} free requests left today." : "";
        var note = suggestion != null
            ? (current.Length > 0 ? $"Soundstage will use {current}." : $"Suggested model: {suggestion}. Copy it into the Model box.")
            : "Pick a model that accepts images and type it into the Model box.";
        return new AiTestResult(true, $"The key works.{extra} {note}".Replace("  ", " "), null, suggestion, vision.Count > 0 ? vision : all, remaining);
    }

    private AiTestResult Failed(AiFailure f) => new(false, AiMessages.For(f, DisplayName), f);

    internal static int? ParseRemaining(string body)
    {
        try
        {
            var root = JsonNode.Parse(body) as JsonObject;
            var block = root?["free_model_daily_requests"] ?? root?["data"]?["free_model_daily_requests"];
            var r = block?["remaining"];
            if (r is JsonValue v && v.TryGetValue<int>(out var n)) return n;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        return null;
    }

    /// <summary>All model ids in a /models reply, and the ones that list image as an input.</summary>
    internal static (List<string> All, List<string> Vision) ParseModels(string body)
    {
        var all = new List<string>();
        var vision = new List<string>();
        try
        {
            var root = JsonNode.Parse(body) as JsonObject;
            if (root?["data"] is JsonArray data)
                foreach (var m in data.OfType<JsonObject>())
                {
                    if (m["id"] is not JsonValue idv || !idv.TryGetValue<string>(out var id)) continue;
                    all.Add(id);
                    if (m["architecture"]?["input_modalities"] is JsonArray mods && mods.Any(x => x?.GetValue<string>() == "image")) vision.Add(id);
                }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { }
        return (all, vision);
    }
}

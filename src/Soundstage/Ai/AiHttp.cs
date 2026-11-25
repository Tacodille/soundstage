using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Soundstage.Ai;

/// <summary>What came back from one HTTP call, or why none did.</summary>
public sealed record AiHttpReply(int Status, string Body, AiFailure? Failure)
{
    public bool Ok => Failure == null && Status is >= 200 and < 300;
}

/// <summary>
/// The shared HTTP plumbing: one request, every exception turned into a typed failure with a scrubbed detail, and one
/// table that maps a status and body onto an <see cref="AiError"/>. Keys go in headers only, never in a URL.
/// </summary>
public static class AiHttp
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static async Task<AiHttpReply> Send(HttpClient http, HttpMethod method, string url, IDictionary<string, string> headers,
        string? jsonBody, AiLog log, CancellationToken ct, TimeSpan? timeout = null)
    {
        using var request = new HttpRequestMessage(method, url);
        foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
        if (jsonBody != null) request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout ?? DefaultTimeout);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, limit.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            log.Info($"{method} {Origin(url)} -> {status}");
            if (status is >= 200 and < 300) return new AiHttpReply(status, body, null);
            var failure = MapStatus(status, body, log);
            log.Info($"  failed as {failure.Error}: {failure.Detail}");
            return new AiHttpReply(status, body, failure);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            log.Info($"{method} {Origin(url)} -> timed out");
            return new AiHttpReply(0, "", new AiFailure(AiError.Timeout, "timed out"));
        }
        catch (OperationCanceledException)
        {
            return new AiHttpReply(0, "", new AiFailure(AiError.Unknown, "cancelled"));
        }
        catch (HttpRequestException ex)
        {
            var detail = log.Describe(ex);
            log.Info($"{method} {Origin(url)} -> no connection: {detail}");
            return new AiHttpReply(0, "", new AiFailure(AiError.NoNetwork, detail));
        }
        catch (Exception ex)
        {
            var detail = log.Describe(ex);
            log.Error($"{method} {Origin(url)} threw", ex);
            return new AiHttpReply(0, "", new AiFailure(AiError.Unknown, detail));
        }
    }

    /// <summary>Scheme, host and path with no query, so a stray ?key= can never reach the log.</summary>
    public static string Origin(string url)
    {
        var cut = url.IndexOf('?');
        return cut < 0 ? url : url[..cut];
    }

    private static readonly Regex LocationWords = new(@"(?i)(location|region|country|territor)", RegexOptions.Compiled);
    private static readonly Regex UnavailableWords = new(@"(?i)(not\s+supported|not\s+available|unavailable|restricted|blocked)", RegexOptions.Compiled);
    private static readonly Regex KeyWords = new(@"(?i)(api[\s_-]?key[\s_-]*(is\s+)?(not\s+valid|invalid|expired)|invalid[\s_-]*api[\s_-]*key|api_key_invalid|unauthori[sz]ed|invalid\s+credentials|leaked)", RegexOptions.Compiled);
    private static readonly Regex ModelWords = new(@"(?i)model", RegexOptions.Compiled);
    private static readonly Regex NotFoundWords = new(@"(?i)(not\s+found|does\s+not\s+exist|no\s+such|unknown\s+model|not\s+supported\s+for|model_not_found)", RegexOptions.Compiled);

    /// <summary>The one status-and-body table every provider uses (the provider docs behind it are listed in docs/ai-providers.md).</summary>
    public static AiFailure MapStatus(int status, string body, AiLog log)
    {
        var message = log.Redact(ErrorMessage(body));
        var detail = $"HTTP {status}" + (message.Length > 0 ? ": " + AiLog.Clip(message, 200) : "");
        var text = message + " " + AiLog.Clip(body, 400);

        AiError error;
        if (status is 400 or 403 or 451 && LocationWords.IsMatch(text) && UnavailableWords.IsMatch(text)) error = AiError.RegionUnavailable;
        else if (status == 401) error = AiError.BadKey;
        else if (status is 400 or 403 && KeyWords.IsMatch(text)) error = AiError.BadKey;
        else if (status == 403) error = AiError.BadKey;
        else if (status == 404 || (status == 400 && ModelWords.IsMatch(text) && NotFoundWords.IsMatch(text))) error = AiError.ModelNotFound;
        else if (status is 429 or 402) error = AiError.RateLimit;
        else if (status is 408 or 504) error = AiError.Timeout;
        else if (status >= 500) error = AiError.ServerError;
        else if (status >= 400) error = AiError.BadRequest;
        else error = AiError.Unknown;
        return new AiFailure(error, detail, status);
    }

    /// <summary>The human message inside an error body, in either provider's shape, or the body itself when it is not JSON.</summary>
    public static string ErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            if (JsonNode.Parse(body) is JsonObject root)
            {
                if (root["error"] is JsonObject err && err["message"] is JsonValue m && m.TryGetValue<string>(out var s)) return s;
                if (root["error"] is JsonValue ev && ev.TryGetValue<string>(out var es)) return es;
                if (root["message"] is JsonValue mv && mv.TryGetValue<string>(out var ms)) return ms;
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { }
        return AiLog.Clip(body, 200);
    }

    /// <summary>True for a web address that is safe to send a key to: https, or http only to this machine.</summary>
    public static bool IsSafeBaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        return uri.Scheme == Uri.UriSchemeHttp && (uri.IsLoopback || uri.Host == "localhost");
    }
}

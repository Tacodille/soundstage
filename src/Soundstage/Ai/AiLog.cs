using System.Text.RegularExpressions;

namespace Soundstage.Ai;

/// <summary>
/// The only way AI code writes a log line. Every line, and every exception text, goes through <see cref="Redact"/> first,
/// which removes the saved keys themselves (raw and URL-escaped) and anything shaped like an Authorization or
/// x-goog-api-key header, a ?key= parameter or a well-known key prefix. The HTTP client is a plain one with no logging
/// handler, so nothing else writes request details. Plan decision 8.
/// </summary>
public sealed class AiLog
{
    public const string Prefix = "[Soundstage/AI]";
    public const string Mask = "[key hidden]";

    private static readonly Regex[] Shapes =
    {
        new(@"(?i)(authorization\s*[:=]\s*)(bearer\s+)?[^\s"",;]+", RegexOptions.Compiled),
        new(@"(?i)(x-goog-api-key\s*[:=]\s*)[^\s"",;]+", RegexOptions.Compiled),
        new(@"(?i)([?&](?:key|api_key|apikey)=)[^&\s""]+", RegexOptions.Compiled),
        new(@"AIza[0-9A-Za-z_\-]{20,}", RegexOptions.Compiled),
        new(@"sk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled),
    };

    private readonly Action<string> sink;
    private readonly Func<IEnumerable<string>> secrets;
    private readonly bool redact;

    /// <param name="sink">Where lines end up (the plugin log). It receives only redacted text.</param>
    /// <param name="secrets">The keys currently saved, asked for on every line so a key pasted a second ago is covered.</param>
    /// <param name="redact">Only the self-check's negative control passes false, to show the leak check can fail.</param>
    public AiLog(Action<string> sink, Func<IEnumerable<string>> secrets, bool redact = true)
    {
        this.sink = sink;
        this.secrets = secrets;
        this.redact = redact;
    }

    public void Info(string message) => sink($"{Prefix} {Redact(message)}");

    public void Error(string message, Exception? ex = null) =>
        sink($"{Prefix} {Redact(ex == null ? message : message + " " + Describe(ex))}");

    /// <summary>Type name and message only (no stack, no inner chain), redacted. Safe to put in a log line or a detail string.</summary>
    public string Describe(Exception ex) => Redact($"{ex.GetType().Name}: {ex.Message}");

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        if (!redact) return text;
        var result = text;
        foreach (var secret in secrets())
        {
            if (string.IsNullOrEmpty(secret) || secret.Length < 6) continue;
            result = result.Replace(secret, Mask, StringComparison.Ordinal);
            var escaped = Uri.EscapeDataString(secret);
            if (escaped != secret) result = result.Replace(escaped, Mask, StringComparison.Ordinal);
        }
        foreach (var shape in Shapes)
            result = shape.Replace(result, m => m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value + Mask : Mask);
        return result;
    }

    /// <summary>A short, single-line version of a reply body for a log line.</summary>
    public static string Clip(string? text, int max = 240)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var flat = Regex.Replace(text, @"\s+", " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "...";
    }
}

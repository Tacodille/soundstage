namespace Soundstage.Ai;

/// <summary>One yes, remembered by provider and by the version of the wording that was shown.</summary>
public sealed class ConsentRecord
{
    public string ProviderId { get; set; } = "";
    public int TextVersion { get; set; }
    public string Model { get; set; } = "";
    public string AgreedUtc { get; set; } = "";
}

/// <summary>
/// The one-time privacy note shown before the first picture is uploaded. A yes is remembered per provider and per wording
/// version, so changing provider asks again and so does a reworded note. The wording comes from each provider's own
/// pages (read 21/11/2026, listed in docs/ai-providers.md); where a page could not be read the note says "check" rather
/// than state a rule.
/// </summary>
public static class Consent
{
    /// <summary>
    /// Bump this whenever <see cref="Text"/> changes in meaning, and every user is asked again.
    /// 1: photos you choose (part 6). 2: adds the small pictures of your own character that Tag with AI sends (part 14).
    /// </summary>
    public const int TextVersion = 2;

    /// <summary>The key a yes is stored under: the provider id, and for a custom endpoint also its host, so a new address asks again.</summary>
    public static string IdFor(string providerId, string baseUrl)
    {
        if (providerId != OpenAiCompatProvider.CustomId) return providerId;
        return Uri.TryCreate(baseUrl, UriKind.Absolute, out var u) ? "custom:" + u.Host.ToLowerInvariant() : "custom:";
    }

    public static bool Has(AiSettings settings, string consentId) =>
        settings.Consents.Any(c => c.ProviderId == consentId && c.TextVersion == TextVersion);

    public static void Record(AiSettings settings, string consentId, string model, DateTime utcNow)
    {
        settings.Consents.RemoveAll(c => c.ProviderId == consentId);
        settings.Consents.Add(new ConsentRecord { ProviderId = consentId, TextVersion = TextVersion, Model = model, AgreedUtc = utcNow.ToString("o") });
    }

    public static void Withdraw(AiSettings settings, string consentId) => settings.Consents.RemoveAll(c => c.ProviderId == consentId);

    /// <summary>The words in the dialog. Plain, short, Australian.</summary>
    public static string Text(string providerId, string providerName, string model, string baseUrl)
    {
        var where = string.IsNullOrWhiteSpace(model) ? providerName : $"{providerName} ({model})";
        var note = providerId switch
        {
            GeminiProvider.ProviderId =>
                "On Google's free tier, Google may use what you send and what it replies to improve its products, and people there may read it " +
                "(they are cut off from your account first). If you are in the EEA, UK or Switzerland, Google says its paid-service data terms " +
                "apply instead. Google's own advice is not to send sensitive, confidential or personal pictures on the free tier.",
            OpenAiCompatProvider.OpenRouterId =>
                "OpenRouter passes your picture on to whoever runs the model you pick. Some of them, free models especially, may keep what you send or use it " +
                "for training. Check the model's page on openrouter.ai before you upload anything you care about.",
            _ =>
                $"This goes to the address you typed ({baseUrl}). Soundstage cannot tell you what that service does with a picture once it has it.",
        };
        return $"Soundstage will send the picture you choose to {where}. It leaves your computer, over the internet.\n\n{note}\n\n" +
               "The Poses tab's Tag with AI button sends something different: a small picture (256 pixels) of your own character holding each pose you choose, one at a time, " +
               "made by Make thumbnails, so the model can write tags for the pose. Those pictures go to the same place and the same note covers them. " +
               "Only the picture and a short instruction go to them (plus your key, so they know it is you), and nothing is sent until you press the button that uses AI. " +
               "Say no and the rest of Soundstage works exactly as before.";
    }
}

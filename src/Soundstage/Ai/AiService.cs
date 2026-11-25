using System.Text.Json.Nodes;

namespace Soundstage.Ai;

/// <summary>
/// The one door the rest of the plugin uses for AI (Studio.Ai). It owns the settings, the key store and the log wrapper,
/// builds the chosen provider, and refuses to send a picture without a key and a recorded consent. With no key nothing
/// here runs and nothing else in the plugin depends on it.
/// </summary>
public sealed class AiService
{
    public const string ConfigKey = "ai";

    public static readonly string[] ProviderIds = { GeminiProvider.ProviderId, OpenAiCompatProvider.OpenRouterId, OpenAiCompatProvider.CustomId };

    private readonly Action saveSettings;
    private readonly HttpClient http;
    private readonly Func<DateTime> now;

    public AiService(AiSettings settings, Action saveSettings, Action<string> logSink, IKeyProtector protector, HttpClient? http = null, Func<DateTime>? now = null, bool redact = true)
    {
        Settings = settings;
        this.saveSettings = saveSettings;
        this.http = http ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        this.now = now ?? (() => DateTime.Now);
        Keys = new KeyStore(settings, protector, saveSettings);
        Log = new AiLog(logSink, () => Keys.AllKeys(), redact);
    }

    public AiSettings Settings { get; }
    public KeyStore Keys { get; }

    /// <summary>Makes share codes refuse the stored key(s) by value (part 7 hook, wired in the integration). <paramref name="service"/> is called lazily, so the AI layer is still built on first use.</summary>
    public static void GuardShareCodes(Func<AiService> service) =>
        ShareCode.SecretValues = () => service().Keys.AllKeys();
    public AiLog Log { get; }

    public string ProviderId
    {
        get => ProviderIds.Contains(Settings.Provider) ? Settings.Provider : GeminiProvider.ProviderId;
        set { Settings.Provider = value; saveSettings(); }
    }

    public static string NameOf(string providerId) => providerId switch
    {
        GeminiProvider.ProviderId => "Google Gemini",
        OpenAiCompatProvider.OpenRouterId => "OpenRouter",
        _ => "your own endpoint",
    };

    public static string KeyPageUrl(string providerId) => providerId switch
    {
        GeminiProvider.ProviderId => "https://aistudio.google.com/apikey",
        OpenAiCompatProvider.OpenRouterId => "https://openrouter.ai/keys",
        _ => "",
    };

    public static string LimitsPageUrl(string providerId) => providerId switch
    {
        GeminiProvider.ProviderId => "https://aistudio.google.com/rate-limit",
        OpenAiCompatProvider.OpenRouterId => "https://openrouter.ai/docs/api-reference/limits",
        _ => "",
    };

    public string BaseUrlFor(string providerId) => providerId switch
    {
        OpenAiCompatProvider.OpenRouterId => OpenAiCompatProvider.OpenRouterBaseUrl,
        OpenAiCompatProvider.CustomId => Settings.For(providerId).BaseUrl,
        _ => GeminiProvider.DefaultBaseUrl,
    };

    private readonly Dictionary<string, IAiProvider> providers = new();

    /// <summary>The provider for an id, built once and kept, so what it learns (an endpoint without structured output) lasts the session.</summary>
    public IAiProvider CreateProvider(string providerId)
    {
        if (!providers.TryGetValue(providerId, out var made)) providers[providerId] = made = Build(providerId);
        return made;
    }

    private IAiProvider Build(string providerId)
    {
        string? KeyNow() => Keys.Get(providerId);
        string ModelNow() => Settings.For(providerId).Model;
        return providerId switch
        {
            GeminiProvider.ProviderId => new GeminiProvider(http, KeyNow, ModelNow, Log),
            OpenAiCompatProvider.OpenRouterId => new OpenAiCompatProvider(providerId, "OpenRouter", http, KeyNow, () => OpenAiCompatProvider.OpenRouterBaseUrl, ModelNow, Log),
            _ => new OpenAiCompatProvider(OpenAiCompatProvider.CustomId, "your endpoint", http, KeyNow, () => Settings.For(OpenAiCompatProvider.CustomId).BaseUrl, ModelNow, Log),
        };
    }

    // ---- consent -------------------------------------------------------------------------------------------------

    public string ConsentId(string providerId) => Consent.IdFor(providerId, BaseUrlFor(providerId));

    public bool HasConsent(string providerId) => Consent.Has(Settings, ConsentId(providerId));

    public string ConsentText(string providerId) =>
        Consent.Text(providerId, NameOf(providerId), Settings.For(providerId).Model, BaseUrlFor(providerId));

    public void GiveConsent(string providerId)
    {
        Consent.Record(Settings, ConsentId(providerId), Settings.For(providerId).Model, DateTime.UtcNow);
        saveSettings();
        Log.Info($"consent recorded for {ConsentId(providerId)}, wording version {Consent.TextVersion}");
    }

    public void WithdrawConsent(string providerId)
    {
        Consent.Withdraw(Settings, ConsentId(providerId));
        saveSettings();
    }

    // ---- usage counter (kept locally, only counts requests Soundstage sent) ---------------------------------------

    public int UsedToday(string providerId)
    {
        if (!Settings.Usage.TryGetValue(providerId, out var u)) return 0;
        return u.Date == Today() ? u.Count : 0;
    }

    private string Today() => now().ToString("yyyy-MM-dd");

    private void CountRequest(string providerId)
    {
        if (!Settings.Usage.TryGetValue(providerId, out var u) || u.Date != Today())
            Settings.Usage[providerId] = u = new UsageDay { Date = Today() };
        u.Count++;
        saveSettings();
    }

    // ---- calls ----------------------------------------------------------------------------------------------------

    /// <summary>Sends one picture to the chosen provider, only if a key is saved and the consent for that provider is on record.</summary>
    public async Task<AiResult<JsonObject>> AnalyseImage(byte[] imageBytes, string mime, JsonObject schema, string instruction, CancellationToken ct = default)
    {
        var id = ProviderId;
        if (!Keys.Has(id)) return AiResult<JsonObject>.Fail(AiError.NoKey);
        if (!HasConsent(id)) return AiResult<JsonObject>.Fail(AiError.ConsentNeeded);
        CountRequest(id);
        return await CreateProvider(id).AnalyseImage(imageBytes, mime, schema, instruction, ct).ConfigureAwait(false);
    }

    /// <summary>Test key for the chosen provider. Sends no picture, so it needs no consent.</summary>
    public async Task<AiTestResult> TestKey(CancellationToken ct = default)
    {
        var id = ProviderId;
        var result = await CreateProvider(id).TestKey(ct).ConfigureAwait(false);
        Log.Info($"Test key for {id}: {(result.Ok ? "ok" : result.Failure?.Error.ToString() ?? "failed")}");
        return result;
    }
}

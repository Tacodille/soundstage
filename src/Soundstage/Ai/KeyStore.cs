using System.Security.Cryptography;
using System.Text;

namespace Soundstage.Ai;

/// <summary>Everything the AI layer keeps in its own config section ("ai"). The only type in the plugin that holds key material.</summary>
public sealed class AiSettings
{
    /// <summary>"gemini", "openrouter" or "custom".</summary>
    public string Provider { get; set; } = GeminiProvider.ProviderId;

    public Dictionary<string, ProviderSettings> Providers { get; set; } = new();
    public List<ConsentRecord> Consents { get; set; } = new();
    public Dictionary<string, UsageDay> Usage { get; set; } = new();

    public ProviderSettings For(string providerId)
    {
        if (!Providers.TryGetValue(providerId, out var p)) Providers[providerId] = p = new ProviderSettings();
        return p;
    }
}

public sealed class ProviderSettings
{
    /// <summary>The key, encrypted for this Windows user (base64). Never plain text.</summary>
    public string ProtectedKey { get; set; } = "";

    /// <summary>Blank for Gemini means "pick one the key can see".</summary>
    public string Model { get; set; } = "";

    /// <summary>OpenRouter's is fixed; "custom" uses what the user typed.</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>The free allowance per day for the local counter. 0 means unknown, and the tab shows only the count used.</summary>
    public int DailyLimit { get; set; }
}

public sealed class UsageDay
{
    public string Date { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Turns a key into bytes only this Windows user can read, and back.</summary>
public interface IKeyProtector
{
    string Protect(string plain);
    string? Unprotect(string stored);
}

/// <summary>Windows DPAPI, current-user scope, with an app-specific extra secret so another program run by the same user is not handed the key for free.</summary>
public sealed class DpapiKeyProtector : IKeyProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Soundstage.Ai.KeyStore.v1");

    public string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser));

    public string? Unprotect(string stored)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return null; }
    }
}

/// <summary>
/// Saved keys, encrypted at rest. The plain text lives only in this object's memory and in the request header. Config
/// holds only <see cref="ProviderSettings.ProtectedKey"/>; if it cannot be decrypted (another Windows user, another PC) the
/// key reads as missing and the user pastes it again.
/// </summary>
public sealed class KeyStore
{
    private readonly AiSettings settings;
    private readonly IKeyProtector protector;
    private readonly Action save;
    private readonly Dictionary<string, string> cache = new();

    public KeyStore(AiSettings settings, IKeyProtector protector, Action save)
    {
        this.settings = settings;
        this.protector = protector;
        this.save = save;
    }

    public void Set(string providerId, string plain)
    {
        plain = plain.Trim();
        if (plain.Length == 0) { Clear(providerId); return; }
        settings.For(providerId).ProtectedKey = protector.Protect(plain);
        cache[providerId] = plain;
        save();
    }

    public string? Get(string providerId)
    {
        if (cache.TryGetValue(providerId, out var hit)) return hit;
        if (!settings.Providers.TryGetValue(providerId, out var p) || p.ProtectedKey.Length == 0) return null;
        var plain = protector.Unprotect(p.ProtectedKey);
        if (plain != null) cache[providerId] = plain;
        return plain;
    }

    public bool Has(string providerId) => Get(providerId) != null;

    public void Clear(string providerId)
    {
        if (settings.Providers.TryGetValue(providerId, out var p)) p.ProtectedKey = "";
        cache.Remove(providerId);
        save();
    }

    /// <summary>Every saved key in plain text, for <see cref="AiLog"/> to scrub. Never log or display the result.</summary>
    public IEnumerable<string> AllKeys() => settings.Providers.Keys.ToList().Select(Get).Where(k => k != null).Select(k => k!);
}

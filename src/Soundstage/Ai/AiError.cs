// No Dalamud types in the Ai folder: the offline self-check compiles these files as shared sources.
namespace Soundstage.Ai;

/// <summary>
/// Every way an AI call can fail, shared by all providers, by <see cref="AiMessages"/> (the plain-language text) and by
/// docs/troubleshooting.md later. One source of truth: a new class here needs a message there, and the self-check asserts it.
/// </summary>
public enum AiError
{
    /// <summary>The service said the key is wrong, expired, revoked or lacks access (401, 403, or 400 "key not valid").</summary>
    BadKey,
    /// <summary>429 or the provider's quota-exceeded status: too fast, or today's free allowance is used up.</summary>
    RateLimit,
    /// <summary>The provider does not serve this country or region (its own location error).</summary>
    RegionUnavailable,
    /// <summary>404 or a model-not-found body: the model name is wrong or the key cannot see it.</summary>
    ModelNotFound,
    /// <summary>No connection: DNS, refused, offline, TLS.</summary>
    NoNetwork,
    /// <summary>The request ran out of time.</summary>
    Timeout,
    /// <summary>The reply was not JSON we could read.</summary>
    NotJson,
    /// <summary>The reply was JSON but did not match the schema we asked for.</summary>
    SchemaMismatch,
    /// <summary>5xx from the provider.</summary>
    ServerError,
    /// <summary>The provider refused the request itself (400 other than the cases above, or a safety block).</summary>
    BadRequest,
    /// <summary>No key has been saved for the chosen provider.</summary>
    NoKey,
    /// <summary>The one-time consent for this provider has not been given, so nothing was sent.</summary>
    ConsentNeeded,
    /// <summary>The settings are incomplete (no model, bad web address).</summary>
    NotConfigured,
    /// <summary>Anything else.</summary>
    Unknown,
}

/// <summary>A failed call: the class, and a short detail that is already scrubbed of the key and safe to log.</summary>
public sealed record AiFailure(AiError Error, string Detail = "", int? HttpStatus = null);

/// <summary>Either a parsed JSON object or a typed failure. Never both.</summary>
public sealed class AiResult<T> where T : class
{
    public T? Value { get; }
    public AiFailure? Failure { get; }
    public bool Ok => Value != null;

    private AiResult(T? value, AiFailure? failure) { Value = value; Failure = failure; }

    public static AiResult<T> Success(T value) => new(value, null);
    public static AiResult<T> Fail(AiError error, string detail = "", int? httpStatus = null) => new(null, new AiFailure(error, detail, httpStatus));
    public static AiResult<T> Fail(AiFailure failure) => new(null, failure);
}

/// <summary>What the Test key button shows.</summary>
public sealed record AiTestResult(bool Ok, string Message, AiFailure? Failure = null, string? ChosenModel = null, IReadOnlyList<string>? Models = null, int? RemainingToday = null);

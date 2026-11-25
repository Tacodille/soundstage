namespace Soundstage.Ai;

/// <summary>
/// The plain-language text for every <see cref="AiError"/>: what happened, then one line on what to do next. No jargon,
/// no status codes (those go in the log). The self-check asserts every enum value has a message.
/// </summary>
public static class AiMessages
{
    public static string For(AiError error, string providerName = "the provider") => error switch
    {
        AiError.BadKey => $"{providerName} did not accept that key. Check you copied the whole key with no spaces, or make a fresh one with Get a key.",
        AiError.RateLimit => $"{providerName} says you have used up its free allowance for now (too many requests, or today's quota). Wait a minute and try again, or come back tomorrow.",
        AiError.RegionUnavailable => $"{providerName} is not available where you are (it refused the location). Try another provider from the list.",
        AiError.ModelNotFound => $"{providerName} does not know that model name, or your key cannot use it. Press Test key to pick one it can see, or fix the name in the Model box.",
        AiError.NoNetwork => "Could not reach the internet. Check your connection and try again.",
        AiError.Timeout => $"{providerName} took too long to answer. Try again in a moment.",
        AiError.NotJson => $"{providerName} replied, but not in a form Soundstage can read. Try again, or try a different model.",
        AiError.SchemaMismatch => $"{providerName} replied, but left out something Soundstage needs. Try again, or try a different model.",
        AiError.ServerError => $"{providerName} is having trouble on its end. Try again in a few minutes.",
        AiError.BadRequest => $"{providerName} refused the request (the model may not accept images, or the picture was blocked). Try a different model or picture.",
        AiError.NoKey => $"There is no key saved for {providerName} yet. Paste one on the AI tab (Get a key opens the page where you make it).",
        AiError.ConsentNeeded => "Nothing was sent. You need to say yes to the privacy note on the AI tab before a picture can be uploaded.",
        AiError.NotConfigured => $"The settings for {providerName} are not complete. Check the web address and model on the AI tab.",
        AiError.Unknown => $"Something unexpected went wrong talking to {providerName}. Try again, and check dalamud.log for a line starting [Soundstage/AI].",
        _ => "Something went wrong.",
    };

    /// <summary>The message for a failure, with the provider's name filled in.</summary>
    public static string For(AiFailure failure, string providerName) => For(failure.Error, providerName);
}

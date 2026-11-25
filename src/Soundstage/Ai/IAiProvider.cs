using System.Text.Json.Nodes;

namespace Soundstage.Ai;

/// <summary>
/// One vision-capable AI service. Every provider takes an image and a small JSON schema and gives back a parsed JSON
/// object that has been checked against that schema, or a typed <see cref="AiFailure"/>. Providers never write stage JSON
/// (plan decision 7): the caller maps the small answer onto a catalogue Scene in plain code.
/// </summary>
public interface IAiProvider
{
    /// <summary>"gemini", "openrouter" or "custom". Stable: it keys the stored key, the consent and the usage counter.</summary>
    string Id { get; }

    /// <summary>The name shown on the tab.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Sends one image with an instruction and asks for JSON matching <paramref name="schema"/> (a JSON Schema subset:
    /// type, properties, required, enum, items, minimum, maximum). Structured output is requested where the service
    /// supports it, and the reply is validated here regardless.
    /// </summary>
    Task<AiResult<JsonObject>> AnalyseImage(byte[] imageBytes, string mime, JsonObject schema, string instruction, CancellationToken ct = default);

    /// <summary>A cheap call that proves the key works (a model list, not a generation), with a plain-language message.</summary>
    Task<AiTestResult> TestKey(CancellationToken ct = default);
}

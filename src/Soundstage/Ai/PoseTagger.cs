// No Dalamud types in the Ai folder: the offline self-check compiles these files as shared sources.
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundstage.Poses;

namespace Soundstage.Ai;

/// <summary>
/// The words the pose tagger may use. The shipped list is <c>vocabulary.json</c> (beside the dll); a <c>pose-vocabulary.json</c> in the config
/// folder adds to it and survives updates. Every word goes through the same rules as a hand-typed tag, so the store never holds a word the
/// shelf could not have produced. A missing or damaged file falls back to the built-in list and says so once.
/// </summary>
public sealed class PoseVocabulary
{
    public const string ShippedFile = "vocabulary.json";
    public const string UserFile = "pose-vocabulary.json";

    private readonly HashSet<string> set;

    public PoseVocabulary(IEnumerable<string> words)
    {
        var list = new List<string>();
        set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var w in words)
        {
            var n = PoseTags.Normalise(w);
            if (n != null && set.Add(n)) list.Add(n);
        }
        Words = list;
    }

    public IReadOnlyList<string> Words { get; }
    public string Note { get; private set; } = "";

    public bool Contains(string normalised) => set.Contains(normalised);

    /// <summary>The list used when no file can be read.</summary>
    public static PoseVocabulary BuiltIn() => new(new[]
    {
        "standing", "sitting", "kneeling", "lying", "leaning", "walking", "arms crossed", "hands on hips", "looking back", "looking up",
        "combat", "cute", "elegant", "dynamic",
    });

    public static PoseVocabulary Load(string shippedPath, string userPath)
    {
        var words = new List<string>();
        var notes = new List<string>();
        var shippedOk = TryRead(shippedPath, words, notes, mustExist: true);
        if (!shippedOk || words.Count == 0)
        {
            words.Clear();
            words.AddRange(BuiltIn().Words);
            notes.Add("using the built-in tag list");
        }
        TryRead(userPath, words, notes, mustExist: false);
        return new PoseVocabulary(words) { Note = string.Join("; ", notes) };
    }

    private static bool TryRead(string path, List<string> into, List<string> notes, bool mustExist)
    {
        try
        {
            if (!File.Exists(path))
            {
                if (mustExist) notes.Add(Path.GetFileName(path) + " is missing");
                return !mustExist;
            }
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root?["words"] is not JsonArray arr) { notes.Add(Path.GetFileName(path) + " has no words list"); return false; }
            foreach (var n in arr)
                if (n is JsonValue v && v.TryGetValue<string>(out var s)) into.Add(s);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            notes.Add(Path.GetFileName(path) + " could not be read (" + ex.Message + ")");
            return false;
        }
    }
}

/// <summary>What one reply turned into: the tags to store, and what was left out.</summary>
public sealed record PoseTagResult(IReadOnlyList<string> Tags, IReadOnlyList<string> Free, IReadOnlyList<string> Dropped);

/// <summary>The prompt, the reply schema and the strict reading of a reply for "Tag with AI".</summary>
public static class PoseTagger
{
    public const int MaxFreeWords = 3;
    public const string LogPrefix = "[Soundstage/AI]";

    /// <summary>Plain strings, no enum: a provider that validates a strict enum would fail the whole request over one odd word. The vocabulary is enforced in <see cref="Read"/>.</summary>
    public static JsonObject Schema() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["words"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("tags"),
    };

    public static string Instruction(PoseVocabulary vocab) =>
        "This is a small picture of a video game character holding a pose in a plain grey studio. Describe the pose only, never who the character is or what they wear.\n" +
        "Fill \"tags\" with every word from this list that fits, usually two to five, copied exactly: " + string.Join(", ", vocab.Words) + ".\n" +
        $"You may add up to {MaxFreeWords} short words of your own in \"words\" for anything the list misses. Words in \"tags\" that are not on the list are thrown away.\n" +
        "Reply with JSON only.";

    /// <summary>
    /// Reads a reply. <c>tags</c> must come from the vocabulary: others are dropped, with one log line for the whole reply. <c>words</c> are free words,
    /// normalised, not repeating a vocabulary tag, at most <see cref="MaxFreeWords"/>.
    /// </summary>
    public static PoseTagResult Read(JsonObject reply, PoseVocabulary vocab, Action<string> log)
    {
        var tags = new List<string>();
        var dropped = new List<string>();
        foreach (var raw in Strings(reply["tags"]))
        {
            var n = PoseTags.Normalise(raw);
            if (n != null && vocab.Contains(n)) { if (!tags.Contains(n)) tags.Add(n); }
            else dropped.Add(raw.Length > 40 ? raw[..40] : raw);
        }

        var free = new List<string>();
        var extra = 0;
        foreach (var raw in Strings(reply["words"]))
        {
            var n = PoseTags.Normalise(raw);
            if (n == null || vocab.Contains(n) || free.Contains(n)) continue;
            if (free.Count >= MaxFreeWords) { extra++; continue; }
            free.Add(n);
        }

        if (dropped.Count > 0 || extra > 0)
            log($"{LogPrefix} pose tags: dropped {dropped.Count} outside the vocabulary" + (dropped.Count > 0 ? " (" + string.Join(", ", dropped.Take(5)) + ")" : "") + (extra > 0 ? $", and {extra} free word(s) over the limit of {MaxFreeWords}" : "") + ".");
        return new PoseTagResult(tags, free, dropped);
    }

    private static IEnumerable<string> Strings(JsonNode? node)
    {
        if (node is not JsonArray arr) yield break;
        foreach (var n in arr)
            if (n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) yield return s;
    }
}

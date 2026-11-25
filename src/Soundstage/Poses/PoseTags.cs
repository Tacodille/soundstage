// No Dalamud types in this file, so the self-check compiles it as a source file.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Poses;

/// <summary>
/// The pose tag store: <c>pose-tags.json</c> in the plugin's config folder, keyed by the SHA-256 of the pose file's
/// bytes, so renaming or moving a file keeps its tags. Hand-added tags and (from part 14) vision-model tags go in the
/// same list. Tags are plain lower-case words, at most <see cref="MaxTagLength"/> characters each.
/// Thread-safe: the shelf reads it from the draw thread while a scan hashes files on a worker.
/// </summary>
public sealed class PoseTags
{
    public const int MaxTagLength = 24;
    public const int FormatVersion = 1;
    public const string FileName = "pose-tags.json";

    private readonly object gate = new();
    private readonly Dictionary<string, List<string>> tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Length, long Ticks, string Hash)> hashCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where this store reads and writes. Empty means in memory only (the self-check).</summary>
    public string StorePath { get; }

    /// <summary>One plain sentence about how the load went ("" when it was fine or the file did not exist yet).</summary>
    public string LoadNote { get; private set; } = "";

    public PoseTags(string storePath = "") => StorePath = storePath;

    // ---- Text rules ----

    /// <summary>
    /// A tag in its stored form: lower case, letters and digits and single spaces or hyphens, trimmed, cut to
    /// <see cref="MaxTagLength"/>. Anything else becomes a space. Null when nothing is left.
    /// </summary>
    public static string? Normalise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '-') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
        }
        var text = sb.ToString().Trim(' ', '-');
        if (text.Length > MaxTagLength) text = text[..MaxTagLength].TrimEnd(' ', '-');
        return text.Length == 0 ? null : text;
    }

    /// <summary>The SHA-256 of the file's bytes as 64 lower-case hex digits. Throws what File throws.</summary>
    public static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>
    /// <see cref="HashFile"/> with a cache keyed on the path, size and write time, so a rescan does not read every file
    /// again. Null when the file cannot be read.
    /// </summary>
    public string? HashFor(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            lock (gate)
            {
                if (hashCache.TryGetValue(path, out var hit) && hit.Length == info.Length && hit.Ticks == info.LastWriteTimeUtc.Ticks) return hit.Hash;
            }
            var hash = HashFile(path);
            lock (gate) hashCache[path] = (info.Length, info.LastWriteTimeUtc.Ticks, hash);
            return hash;
        }
        catch (Exception) { return null; }
    }

    // ---- Editing and reading ----

    /// <summary>Adds a tag (normalised). False when it was empty after normalising, or already there.</summary>
    public bool Add(string hash, string tag)
    {
        var norm = Normalise(tag);
        if (norm == null || hash.Length == 0) return false;
        lock (gate)
        {
            if (!tags.TryGetValue(hash, out var list)) tags[hash] = list = new List<string>();
            if (list.Contains(norm)) return false;
            list.Add(norm);
            list.Sort(StringComparer.Ordinal);
            return true;
        }
    }

    public bool Remove(string hash, string tag)
    {
        var norm = Normalise(tag);
        if (norm == null) return false;
        lock (gate)
        {
            if (!tags.TryGetValue(hash, out var list) || !list.Remove(norm)) return false;
            if (list.Count == 0) tags.Remove(hash);
            return true;
        }
    }

    public IReadOnlyList<string> TagsFor(string? hash)
    {
        if (hash == null) return Array.Empty<string>();
        lock (gate) return tags.TryGetValue(hash, out var list) ? list.ToArray() : Array.Empty<string>();
    }

    /// <summary>Every tag in use with the number of poses carrying it, most used first, then A to Z.</summary>
    public IReadOnlyList<(string Tag, int Count)> AllTags()
    {
        lock (gate)
        {
            return tags.Values.SelectMany(l => l).GroupBy(t => t)
                .Select(g => (Tag: g.Key, Count: g.Count()))
                .OrderByDescending(x => x.Count).ThenBy(x => x.Tag, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>
    /// True when the pose carries every word of the filter. The filter is split on spaces and commas; each word has to
    /// match the start of some tag of the pose (or of a word inside a tag), so "sit" finds "sitting". An empty filter
    /// matches everything.
    /// </summary>
    public bool Matches(string? hash, string filter)
    {
        var words = filter.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return true;
        var own = TagsFor(hash);
        return words.All(w => own.Any(t => t.StartsWith(w, StringComparison.OrdinalIgnoreCase)
                                        || t.Split(' ').Any(p => p.StartsWith(w, StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>How many poses have at least one tag.</summary>
    public int TaggedCount { get { lock (gate) return tags.Count; } }

    // ---- Disk ----

    private sealed class FileShape
    {
        [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; } = PoseTags.FormatVersion;
        [JsonPropertyName("tags")] public Dictionary<string, List<string>> Tags { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>
    /// Reads the file. A missing file is an empty store. A damaged file is kept aside as <c>pose-tags.json.bad</c>, the
    /// store starts empty, and <see cref="LoadNote"/> says so, so one bad byte never takes the shelf down.
    /// </summary>
    public static PoseTags Load(string path)
    {
        var store = new PoseTags(path);
        if (!File.Exists(path)) return store;
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path, Encoding.UTF8)) ?? new FileShape();
            foreach (var (hash, list) in shape.Tags)
                foreach (var t in list ?? new List<string>()) store.Add(hash, t);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            store.LoadNote = "pose-tags.json could not be read (" + ex.Message + "); starting with no tags.";
            try { File.Copy(path, path + ".bad", overwrite: true); } catch { /* best effort */ }
            store.tags.Clear();
        }
        return store;
    }

    /// <summary>Writes the file through a temp file, so a crash mid-write leaves the old one. No-op when <see cref="StorePath"/> is empty.</summary>
    public void Save()
    {
        if (StorePath.Length == 0) return;
        FileShape shape;
        lock (gate) shape = new FileShape { Tags = tags.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value), StringComparer.Ordinal) };
        var dir = Path.GetDirectoryName(StorePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = StorePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(shape, Pretty), new UTF8Encoding(false));
        File.Move(tmp, StorePath, overwrite: true);
    }
}

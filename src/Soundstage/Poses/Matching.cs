// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Poses;

/// <summary>A pose the matcher can rank: something to load it by, and its tags.</summary>
public sealed record PoseCandidate(string Key, string Name, string Pack, IReadOnlyList<string> Tags);

/// <summary>One ranked pose: how many of the Scene's tags it shares, and which.</summary>
public sealed record PoseMatch(PoseCandidate Pose, int Overlap, double Score, IReadOnlyList<string> Shared);

/// <summary>"Poses that fit": a Scene's <c>pose.tags</c> against the tags on the shelf. Pure, so it is checked offline.</summary>
public static class Matching
{
    public const int MinPrefix = 3;

    /// <summary>
    /// Two tags agree when they are equal, or one starts with the other and the shorter has at least <see cref="MinPrefix"/> letters
    /// ("sit" agrees with "sitting", "a" agrees with nothing but "a").
    /// </summary>
    public static bool Agree(string a, string b)
    {
        if (a == b) return true;
        var (shortTag, longTag) = a.Length <= b.Length ? (a, b) : (b, a);
        return shortTag.Length >= MinPrefix && longTag.StartsWith(shortTag, StringComparison.Ordinal);
    }

    /// <summary>
    /// Poses sharing at least one tag with the Scene, best first: most shared tags, then the tidier tag list (shared tags over the
    /// square root of the product of both list lengths), then name. At most <paramref name="max"/> are returned.
    /// </summary>
    public static IReadOnlyList<PoseMatch> Rank(IEnumerable<string> sceneTags, IEnumerable<PoseCandidate> poses, int max = 12)
    {
        var wanted = sceneTags.Select(PoseTags.Normalise).Where(t => t != null).Select(t => t!).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0 || max <= 0) return Array.Empty<PoseMatch>();

        var found = new List<PoseMatch>();
        foreach (var pose in poses)
        {
            var own = pose.Tags.Distinct(StringComparer.Ordinal).ToList();
            if (own.Count == 0) continue;
            var shared = wanted.Where(w => own.Any(o => Agree(w, o))).ToList();
            if (shared.Count == 0) continue;
            var score = shared.Count / Math.Sqrt((double)wanted.Count * own.Count);
            found.Add(new PoseMatch(pose, shared.Count, score, shared));
        }
        return found
            .OrderByDescending(m => m.Overlap).ThenByDescending(m => m.Score)
            .ThenBy(m => m.Pose.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Pose.Key, StringComparer.Ordinal)
            .Take(max).ToList();
    }
}

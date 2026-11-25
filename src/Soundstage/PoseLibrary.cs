using System.Text.Json;
using System.Text.Json.Nodes;

namespace Soundstage;

// No Dalamud types in this file, so the self-check compiles it as a source file (as it does Presets.cs).

/// <summary>One pose file found by a scan. <see cref="Pack"/> is the folder it sits in, relative to the scanned folder.</summary>
public sealed record PoseEntry(string Path, string Pack, string Name, bool IsCmp);

public static class PoseLibrary
{
    /// <summary>A scan stops here and says so, so a wrong folder (a whole drive) cannot freeze the plugin.</summary>
    public const int MaxFiles = 5000;

    /// <summary>
    /// Finds every *.pose and *.cmp under <paramref name="folders"/>, recursively. Plain strings throughout: brackets,
    /// hearts and apostrophes in folder names are not special. Folders that do not exist are logged and skipped.
    /// Meant to run off the game thread. A file reachable through two folders is listed once.
    /// </summary>
    public static List<PoseEntry> Scan(IEnumerable<string> folders, Action<string> log, out bool capped, CancellationToken cancel = default)
    {
        var found = new List<PoseEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        capped = false;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            AttributesToSkip = FileAttributes.System,
        };

        foreach (var raw in folders)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var root = raw.Trim().TrimEnd('\\', '/');
            if (!Directory.Exists(root)) { log($"scan: folder not found, skipped: {root}"); continue; }
            var rootName = System.IO.Path.GetFileName(root);
            if (rootName.Length == 0) rootName = root;
            var before = found.Count;

            foreach (var pattern in new[] { "*.pose", "*.cmp" })
            {
                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(root, pattern, options); }
                catch (Exception ex) { log($"scan: could not read {root}: {ex.Message}"); continue; }

                foreach (var file in files)
                {
                    cancel.ThrowIfCancellationRequested();
                    var ext = System.IO.Path.GetExtension(file);
                    var isCmp = ext.Equals(".cmp", StringComparison.OrdinalIgnoreCase);
                    if (!isCmp && !ext.Equals(".pose", StringComparison.OrdinalIgnoreCase)) continue;   // short-name wildcard quirk
                    if (!seen.Add(file)) continue;
                    if (found.Count >= MaxFiles) { capped = true; break; }

                    var dir = System.IO.Path.GetDirectoryName(file) ?? root;
                    var rel = System.IO.Path.GetRelativePath(root, dir);
                    var pack = rel == "." ? rootName : rel.Replace('\\', '/');
                    found.Add(new PoseEntry(file, pack, System.IO.Path.GetFileNameWithoutExtension(file), isCmp));
                }
                if (capped) break;
            }
            log($"scan: {root} -> {found.Count - before} file(s)");
            if (capped) { log($"scan: capped at {MaxFiles} files, the rest were not listed."); break; }
        }

        found.Sort((a, b) =>
        {
            var c = string.Compare(a.Pack, b.Pack, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return found;
    }

    /// <summary>
    /// The embedded preview picture of a .pose file, or null. Brio's exporter stores it as a base64 string in the
    /// "Base64Image" property (JsonDocumentBase.cs line 23) and its library decodes it with Convert.FromBase64String
    /// (FileSource.cs lines 254-255), so the bytes are an ordinary image file (PNG or JPEG).
    /// </summary>
    public static byte[]? ExtractPreviewImage(string poseFilePath)
    {
        try
        {
            if (!poseFilePath.EndsWith(".pose", StringComparison.OrdinalIgnoreCase)) return null;
            using var stream = File.OpenRead(poseFilePath);
            var node = JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            var b64 = node?["Base64Image"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(b64)) return null;
            var bytes = Convert.FromBase64String(b64);
            return bytes.Length > 8 ? bytes : null;
        }
        catch { return null; }
    }
}

/// <summary>
/// Splits a .pose file into its body and expression parts, the way Brio's own importer would, because Brio's IPC
/// load calls take no importer options (see docs\brio-notes.md). The IPC applies whatever bones are in the JSON it is
/// given, so a part is chosen by deleting the other part's bones from the JSON first.
/// </summary>
public static class PoseFilter
{
    // Brio's BodyOptions switch these filter categories off, ExpressionOptions switch only these on
    // (PosingService.cs lines 65-75 and 77-85). "head" is j_kao and j_f_face.
    private static readonly string[] BodyOff = { "weapon", "ears", "hair", "face", "eyes", "lips", "jaw", "head", "legacy", "ex" };
    private static readonly string[] ExpressionOn = { "head", "ears", "hair", "face", "eyes", "lips", "jaw" };
    private const string ExcludedPrefix = "n_throw";   // BoneFilter.cs line 38, excluded by every filter

    /// <summary>BoneFilter.IsBoneValidUncached (BoneFilter.cs lines 127-148) for a character-slot bone.</summary>
    public static bool IsBoneAllowed(string boneName, ISet<string> allowedCategories, bool otherAllowed)
    {
        if (boneName.StartsWith(ExcludedPrefix, StringComparison.Ordinal)) return false;
        var found = false;
        foreach (var (id, prefixes) in BrioBoneCategories.Filters)
        {
            foreach (var p in prefixes)
            {
                if (!boneName.StartsWith(p, StringComparison.Ordinal)) continue;
                found = true;
                if (allowedCategories.Contains(id)) return true;
                break;
            }
        }
        return !found && otherAllowed;
    }

    private static HashSet<string> AllCategories() => BrioBoneCategories.Filters.Select(f => f.Id).ToHashSet();

    public static bool IsBodyBone(string boneName)
    {
        var allowed = AllCategories();
        foreach (var off in BodyOff) allowed.Remove(off);
        return IsBoneAllowed(boneName, allowed, otherAllowed: true);
    }

    public static bool IsExpressionBone(string boneName)
        => IsBoneAllowed(boneName, new HashSet<string>(ExpressionOn), otherAllowed: false);

    /// <summary>
    /// Returns the pose JSON reduced to the chosen parts, or null with <paramref name="error"/> saying why nothing
    /// is left to apply. Body keeps every bone Brio's body import would, plus Prop and Ornament, and drops weapons.
    /// Expression keeps only the head, hair, ears, face, eyes, lips and jaw bones and nothing else.
    /// </summary>
    public static string? Reduce(string poseJson, bool body, bool expression, out string error, out int bonesKept, out int bonesTotal)
    {
        error = "";
        bonesKept = 0;
        bonesTotal = 0;
        JsonNode? root;
        try { root = JsonNode.Parse(poseJson, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (Exception ex) { error = "The pose file is not valid JSON: " + ex.Message; return null; }
        if (root is not JsonObject obj || obj["Bones"] is not JsonObject bones) { error = "The pose file has no Bones section."; return null; }
        if (!body && !expression) { error = "Tick Body or Expression."; return null; }

        bonesTotal = bones.Count;
        var drop = new List<string>();
        foreach (var kv in bones)
        {
            var keep = (body && IsBodyBone(kv.Key)) || (expression && IsExpressionBone(kv.Key));
            if (!keep) drop.Add(kv.Key);
        }
        foreach (var k in drop) bones.Remove(k);
        bonesKept = bones.Count;

        if (!body)
        {
            // Expression only: Brio's ExpressionOptions allow no weapons, props or ornaments.
            foreach (var key in new[] { "MainHand", "OffHand", "Prop", "Ornament" })
                if (obj[key] is JsonObject o) o.Clear();
        }
        else
        {
            foreach (var key in new[] { "MainHand", "OffHand" })
                if (obj[key] is JsonObject o) o.Clear();
        }

        if (bones.Count == 0)
        {
            error = expression && !body ? "This file has no expression bones." : "This file has no bones for the chosen parts.";
            return null;
        }
        return obj.ToJsonString();
    }
}

// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Soundstage;

/// <summary>The outcome of reading a share code: a Scene, or the plain reason it was refused. Never an exception.</summary>
public sealed record ShareImport(Scene? Scene, string? Error, List<string> Notes);

/// <summary>
/// Part 7.4: <c>ss1:</c> plus URL-safe Base64 of the deflated Scene JSON, at most <see cref="MaxCodeChars"/> characters.
/// A code is read with the same rules as a scene file (<see cref="SceneStore.Parse"/>: unknown blocks ignored, a light cap, the
/// same validation) and with extra limits, because the text comes from a stranger. Nothing that could be a key leaves this
/// machine in a code: <see cref="Strip"/> removes secret-named fields from the free-form part of a Scene, a code with one left
/// in is refused at encoding, and <see cref="SecretValues"/> lets the AI layer (part 6) name the stored key so that a code
/// containing that exact text is refused too.
/// </summary>
public static class ShareCode
{
    public const string Prefix = "ss1:";
    public const int MaxCodeChars = 8 * 1024;

    /// <summary>The most a code may unpack to. A small code that inflates past this is a decompression bomb.</summary>
    public const int MaxInflatedBytes = 256 * 1024;

    /// <summary>
    /// Values that must never appear in a code, evaluated each time. Part 6's key store sets this to return the key it holds
    /// (and the self-check sets it to a sentinel). Default: none, so a build without the AI layer has nothing to leak.
    /// </summary>
    public static Func<IEnumerable<string>> SecretValues { get; set; } = () => Array.Empty<string>();

    /// <summary>A field whose name, lower-cased with dashes, underscores and spaces removed, contains one of these is a secret.</summary>
    private static readonly string[] SecretNameParts =
        { "apikey", "secret", "token", "password", "passwd", "authorization", "bearer", "credential", "privatekey", "accesskey" };

    /// <summary>Shapes of keys the supported providers issue, as a second line of defence for a key pasted into a free-text field.</summary>
    private static readonly Regex[] KeyShapes =
    {
        new(@"AIza[0-9A-Za-z_\-]{30,}", RegexOptions.Compiled),
        new(@"\bsk-[A-Za-z0-9_\-]{20,}", RegexOptions.Compiled),
        new(@"\bBearer\s+[A-Za-z0-9._\-]{16,}", RegexOptions.Compiled | RegexOptions.IgnoreCase),
    };

    private static readonly JsonSerializerOptions Compact = new(SceneStore.WriteOptions) { WriteIndented = false };

    // ---- Strip ----

    /// <summary>
    /// A copy of the Scene with everything that must not travel removed: secret-named fields in <see cref="Scene.References"/>
    /// (the one free-form part of the schema; the typed fields cannot hold an unknown field at all), a pose file path, and the
    /// legacy ids. <paramref name="sensitive"/> lists what was secret-named, so a caller can refuse instead of quietly shipping less.
    /// </summary>
    public static Scene Strip(Scene scene, out List<string> sensitive)
    {
        sensitive = new List<string>();
        var copy = scene.Clone();
        copy.LegacyIds = new();
        if (copy.Pose != null) copy.Pose.PoseFile = null;
        if (copy.References is { } refs)
        {
            var node = JsonNode.Parse(refs.GetRawText());
            RemoveSecretFields(node, "references", sensitive);
            copy.References = node == null ? null : JsonSerializer.Deserialize<JsonElement>(node.ToJsonString());
        }
        return copy;
    }

    private static bool IsSecretName(string name)
    {
        var n = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return SecretNameParts.Any(n.Contains);
    }

    private static void RemoveSecretFields(JsonNode? node, string path, List<string> found)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(p => p.Key).ToList())
                {
                    if (IsSecretName(key)) { o.Remove(key); found.Add(path + "." + key); }
                    else RemoveSecretFields(o[key], path + "." + key, found);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++) RemoveSecretFields(a[i], path + "[" + i + "]", found);
                break;
        }
    }

    // ---- Encode ----

    /// <summary>Makes a share code for the Scene. False, with the reason, if it would carry a secret or is too big.</summary>
    public static bool TryEncode(Scene scene, out string code, out string error)
    {
        code = "";
        try
        {
            var clean = Strip(scene, out var sensitive);
            if (sensitive.Count > 0)
            {
                error = "Not shared: the Scene has a field that looks like a key (" + string.Join(", ", sensitive) + ").";
                return false;
            }
            var json = JsonSerializer.Serialize(new SceneFile { FormatVersion = SceneStore.FormatVersion, Scenes = { clean } }, Compact);
            var leak = FindSecret(json);
            if (leak != null) { error = "Not shared: the Scene contains " + leak + "."; return false; }

            var packed = Deflate(Encoding.UTF8.GetBytes(json));
            var text = Prefix + ToBase64Url(packed);
            if (text.Length > MaxCodeChars) { error = $"That Scene makes a code of {text.Length:N0} characters; the limit is {MaxCodeChars:N0}."; return false; }
            code = text;
            error = "";
            return true;
        }
        catch (Exception ex) { error = "Could not make a code: " + ex.Message; return false; }
    }

    /// <summary>The name of the first secret found in the text, or null: a value from <see cref="SecretValues"/>, or text shaped like a provider key.</summary>
    public static string? FindSecret(string text)
    {
        foreach (var v in SecretValues().Where(v => !string.IsNullOrEmpty(v) && v.Length >= 4))
            if (text.Contains(v, StringComparison.Ordinal)) return "your stored key";
        foreach (var rx in KeyShapes)
            if (rx.IsMatch(text)) return "text shaped like an API key";
        return null;
    }

    // ---- Decode ----

    /// <summary>Reads a pasted code. Refusals come back as <see cref="ShareImport.Error"/>; this never throws.</summary>
    public static ShareImport Decode(string? pasted)
    {
        var notes = new List<string>();
        try
        {
            if (string.IsNullOrWhiteSpace(pasted)) return Refuse("Nothing to read: paste a share code first.", notes);
            var text = new string(pasted.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (text.Length > MaxCodeChars) return Refuse($"That code is {text.Length:N0} characters; the limit is {MaxCodeChars:N0}.", notes);
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return Refuse("That is not a Soundstage share code (it should start with ss1:).", notes);

            byte[] packed;
            try { packed = FromBase64Url(text[Prefix.Length..]); }
            catch (FormatException) { return Refuse("That code is damaged: it has characters a share code cannot have.", notes); }

            byte[]? raw;
            try { raw = Inflate(packed); }
            catch (InvalidDataException) { return Refuse("That code is damaged: it does not unpack.", notes); }
            if (raw == null) return Refuse($"That code unpacks to more than {MaxInflatedBytes / 1024} KB, so it was refused.", notes);

            string json;
            try { json = new UTF8Encoding(false, true).GetString(raw); }
            catch (DecoderFallbackException) { return Refuse("That code is damaged: the text inside is not readable.", notes); }

            var logs = new List<string>();
            var scenes = SceneStore.Parse(json, "share code", logs.Add);
            foreach (var l in logs.Where(l => l.Contains("unknown block"))) notes.Add("Ignored a part this build does not know: " + Tail(l));
            if (scenes.Count == 0) return Refuse("That code does not hold a usable Scene" + (logs.Count > 0 ? ": " + Tail(logs[^1]) : "."), notes);
            if (scenes.Count > 1) return Refuse("That code holds more than one Scene; a share code holds exactly one.", notes);

            var scene = Strip(scenes[0], out var sensitive);
            foreach (var s in sensitive) notes.Add("Removed a field that looked like a key (" + s + ").");
            var limit = CheckLimits(scene);
            if (limit != null) return Refuse(limit, notes);
            if (FindSecret(JsonSerializer.Serialize(scene, Compact)) is { } leak) return Refuse("That code contains " + leak + ", so it was refused.", notes);

            scene.Id = SceneStore.Slug(scene.Id);
            scene.Category = "Mine";
            return new ShareImport(scene, null, notes);
        }
        catch (Exception ex) { return Refuse("That code could not be read: " + ex.Message, notes); }
    }

    private static ShareImport Refuse(string why, List<string> notes) => new(null, why, notes);

    private static string Tail(string logLine)
    {
        var i = logLine.IndexOf("share code", StringComparison.Ordinal);
        return i >= 0 ? logLine[(i + "share code".Length)..].Trim(' ', ':') : logLine;
    }

    /// <summary>Size limits on the free-text parts of a Scene that came from a stranger. Null when fine.</summary>
    public static string? CheckLimits(Scene s)
    {
        if (s.Name.Length > 120 || s.Id.Length > 120) return "That Scene's name is far too long.";
        if (s.Description.Length > 4000 || s.BestIn.Length > 1000) return "That Scene's text is far too long.";
        if (s.Tags.Count > 60 || s.Palettes.Count > 24) return "That Scene has too many tags or palettes.";
        if (s.Pose != null && (s.Pose.PenumbraMods.Count > 24 || s.Pose.Tags.Count > 60)) return "That Scene's pose block has too many entries.";
        return null;
    }

    /// <summary>What the user sees before saving an import: one line per thing the Scene holds.</summary>
    public static List<string> Describe(Scene s, ISet<string> appliedBlocks)
    {
        var lines = new List<string>
        {
            $"Name: {s.Name}" + (s.Rig.Length > 0 ? $" ({s.Rig})" : ""),
            $"{s.Lights.Count} light(s)" + (s.Backdrop != null ? " and a backdrop" : "") + $", {Math.Max(1, s.Palettes.Count)} palette(s)",
        };
        if (!string.IsNullOrWhiteSpace(s.Description)) lines.Add(s.Description);
        var blocks = s.BlocksPresent().ToList();
        if (blocks.Count > 0)
        {
            lines.Add("Also carries: " + string.Join(", ", blocks));
            var skipped = blocks.Where(b => !appliedBlocks.Contains(b)).ToList();
            if (skipped.Count > 0) lines.Add("This build does not apply: " + string.Join(", ", skipped) + " (kept in the Scene, ignored for now).");
        }
        if (s.Pose is { HasPoseOrOutfit: true } p)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(p.PoseName)) parts.Add($"a Brio pose ({p.PosePack}/{p.PoseName})");
            if (!string.IsNullOrWhiteSpace(p.GlamourerDesign)) parts.Add($"a Glamourer design ({p.GlamourerDesign})");
            if (p.PenumbraMods.Count > 0) parts.Add($"{p.PenumbraMods.Count} Penumbra mod setting(s)");
            lines.Add("Pose and outfit: " + string.Join(", ", parts) + ". Nothing changes on your character unless you tick \"Apply pose and outfit\" for this Scene.");
        }
        return lines;
    }

    // ---- Base64url and deflate ----

    public static string ToBase64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    /// <summary>The unpacked bytes, or null if they run past <see cref="MaxInflatedBytes"/>.</summary>
    private static byte[]? Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var z = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        while ((n = z.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, n);
            if (output.Length > MaxInflatedBytes) return null;
        }
        return output.ToArray();
    }
}

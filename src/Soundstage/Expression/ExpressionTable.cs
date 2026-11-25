// No Dalamud types in this file: the offline self-check compiles Expression/*.cs as source files.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Expression;

/// <summary>Which face the table belongs to: race, clan, gender and face id from the actor's customise data. Bone names differ per race, so a table is never shared between keys.</summary>
public sealed record FaceKey(int Race, int Tribe, int Gender, int Face)
{
    public string Id => $"race{Race}-clan{Tribe}-g{Gender}-face{Face}";
    public override string ToString() => Id;
}

/// <summary>One thing the calibration moves: one axis of one bone, by position or by rotation.</summary>
public sealed class ChannelRecord
{
    public string Bone { get; set; } = "";

    /// <summary>"pos" (added to the bone's Position, in the pose file's units) or "rot" (degrees, turned about the bone's own axis).</summary>
    public string Kind { get; set; } = "pos";

    /// <summary>"x", "y" or "z".</summary>
    public string Axis { get; set; } = "x";

    /// <summary>The offset the response was measured at, in the unit of <see cref="Kind"/>.</summary>
    public float Step { get; set; }

    /// <summary>The largest offset the solver may use either way. This is the bone's safe range, so a face never distorts.</summary>
    public float Max { get; set; }

    /// <summary>Blendshape change measured at +Step, by name (only changes over 0.0001 are kept). Null means not measured yet.</summary>
    public Dictionary<string, float>? Plus { get; set; }

    /// <summary>The same at -Step.</summary>
    public Dictionary<string, float>? Minus { get; set; }

    [JsonIgnore] public string Id => $"{Bone}|{Kind}|{Axis}";
    [JsonIgnore] public bool Measured => Plus != null && Minus != null;
}

/// <summary>The measured response of one face to its own expression bones. Stored as expression-tables/&lt;faceKey&gt;.json beside the plugin's config.</summary>
public sealed class ExpressionTable
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public int Race { get; set; }
    public int Tribe { get; set; }
    public int Gender { get; set; }
    public int Face { get; set; }

    /// <summary>The blendshapes the analyser gave on the expression the calibration started from. Every response is a change from this.</summary>
    public Dictionary<string, float> Baseline { get; set; } = new();

    public List<ChannelRecord> Channels { get; set; } = new();

    /// <summary>When the table was last saved (UTC, ISO 8601). Text, so a test can fix it.</summary>
    public string SavedUtc { get; set; } = "";

    [JsonIgnore] public FaceKey Key => new(Race, Tribe, Gender, Face);
    [JsonIgnore] public int MeasuredCount => Channels.Count(c => c.Measured);
    [JsonIgnore] public bool Complete => Channels.Count > 0 && Channels.All(c => c.Measured) && Baseline.Count > 0;

    public static ExpressionTable For(FaceKey key) => new() { Race = key.Race, Tribe = key.Tribe, Gender = key.Gender, Face = key.Face };

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ExpressionTable? TryParse(string json, out string problem)
    {
        problem = "";
        try
        {
            var t = JsonSerializer.Deserialize<ExpressionTable>(json);
            if (t == null) { problem = "the file is empty"; return null; }
            if (t.Version != CurrentVersion) { problem = $"the table is version {t.Version}, this build reads {CurrentVersion}"; return null; }
            return t;
        }
        catch (JsonException ex) { problem = ex.Message; return null; }
    }
}

/// <summary>Reads and writes tables in a folder. A write goes to a temporary name first, so a crash never leaves half a table.</summary>
public sealed class ExpressionTableStore
{
    public ExpressionTableStore(string folder) => Folder = folder;

    public string Folder { get; }

    public string PathFor(FaceKey key) => System.IO.Path.Combine(Folder, key.Id + ".json");

    public ExpressionTable? Load(FaceKey key, out string problem)
    {
        problem = "";
        var p = PathFor(key);
        if (!File.Exists(p)) return null;
        try { return ExpressionTable.TryParse(File.ReadAllText(p), out problem); }
        catch (IOException ex) { problem = ex.Message; return null; }
    }

    public bool Save(ExpressionTable table, out string problem)
    {
        problem = "";
        try
        {
            Directory.CreateDirectory(Folder);
            var p = PathFor(table.Key);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, table.ToJson());
            File.Move(tmp, p, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = ex.Message; return false; }
    }

    public bool Delete(FaceKey key)
    {
        try { var p = PathFor(key); if (File.Exists(p)) File.Delete(p); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}

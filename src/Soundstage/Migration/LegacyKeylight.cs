// No Dalamud types in this file: the offline self-check compiles it as a shared file.
//
// Soundstage was Keylight until 21/11/2026. Dalamud keys saved config by the plugin's internal name and writes
// "$type": "Keylight.Configuration, Keylight" into it, so its own loader cannot read the old file once the namespace and
// assembly change. This reads the old files as raw JSON (ignoring $type) and hands back plain settings.
using System.Text.Json;

namespace Soundstage.Migration;

/// <summary>Every field the old Keylight Configuration had, as plain values.</summary>
public sealed class LegacySettings
{
    public int Version { get; set; } = 3;
    public bool FollowPlayer { get; set; }
    public bool ShowLightMarkers { get; set; } = true;
    public string SelectedPresetId { get; set; } = "";
    public bool ShowBackdrop { get; set; } = true;
    public BackdropAdjust Backdrop { get; set; } = new();
    public float CanvasYawOffsetDeg { get; set; }
    public float CanvasHeightOffset { get; set; }
    public Dictionary<string, TestResult> TestResults { get; set; } = new();
    public List<string> PoseFolders { get; set; } = new();
    public bool PoseFoldersInitialised { get; set; }
    public bool IncludeBrioLibraryFolders { get; set; } = true;
    public bool PoseBody { get; set; } = true;
    public bool PoseExpression { get; set; } = true;
    public bool PoseHoverPreview { get; set; } = true;
    public List<string> FavouritePoses { get; set; } = new();
    public List<string> RecentPoses { get; set; } = new();
}

public enum MigrationOutcome
{
    /// <summary>Soundstage.json already exists, so nothing runs (this is what makes a second run a no-op).</summary>
    NotNeeded,
    /// <summary>No Keylight files at all: a fresh install.</summary>
    NothingToMigrate,
    /// <summary>The old files were read and mapped.</summary>
    Migrated,
    /// <summary>Keylight.json existed but could not be read; settings are defaults and one line was logged.</summary>
    DefaultsAfterReadFailure,
}

public sealed record MigrationResult(MigrationOutcome Outcome, LegacySettings? Settings, bool PresetsCopied);

public static class LegacyKeylight
{
    public const string OldConfigName = "Keylight.json";
    public const string NewConfigName = "Soundstage.json";
    public const string OldFolderName = "Keylight";
    public const string NewFolderName = "Soundstage";
    public const string UserPresetsName = "user-presets.json";

    /// <summary>
    /// Runs the migration against the Dalamud pluginConfigs folder. Does file work only when Soundstage.json does not
    /// exist. Copies (never moves) the old user presets, never overwriting one that is already there. The old files are
    /// never changed. The caller saves the returned settings, which creates Soundstage.json and ends the migration.
    /// </summary>
    public static MigrationResult Run(string pluginConfigsDir, Action<string> log)
    {
        if (File.Exists(Path.Combine(pluginConfigsDir, NewConfigName)))
            return new MigrationResult(MigrationOutcome.NotNeeded, null, false);

        var oldConfig = Path.Combine(pluginConfigsDir, OldConfigName);
        var oldPresets = Path.Combine(pluginConfigsDir, OldFolderName, UserPresetsName);
        var newPresets = Path.Combine(pluginConfigsDir, NewFolderName, UserPresetsName);
        var haveConfig = File.Exists(oldConfig);
        var havePresets = File.Exists(oldPresets);
        if (!haveConfig && !havePresets)
            return new MigrationResult(MigrationOutcome.NothingToMigrate, null, false);

        var copied = false;
        if (havePresets && !File.Exists(newPresets))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPresets)!);
                File.Copy(oldPresets, newPresets, overwrite: false);
                copied = true;
            }
            catch (Exception ex)
            {
                log($"Could not copy your old Keylight presets ({ex.Message}). They are still in {oldPresets}.");
            }
        }

        if (!haveConfig)
            return new MigrationResult(MigrationOutcome.Migrated, new LegacySettings(), copied);

        string text;
        try { text = File.ReadAllText(oldConfig); }
        catch (Exception ex)
        {
            log($"Could not read the old Keylight settings ({ex.Message}); starting from defaults. The old file is untouched.");
            return new MigrationResult(MigrationOutcome.DefaultsAfterReadFailure, new LegacySettings(), copied);
        }

        var settings = ParseConfig(text, log);
        return settings == null
            ? new MigrationResult(MigrationOutcome.DefaultsAfterReadFailure, new LegacySettings(), copied)
            : new MigrationResult(MigrationOutcome.Migrated, settings, copied);
    }

    /// <summary>
    /// Maps the old config JSON onto plain settings by property name, ignoring every "$type". A missing or wrongly typed
    /// field keeps its default. Returns null (after one log line) when the text is not usable JSON at all.
    /// </summary>
    public static LegacySettings? ParseConfig(string json, Action<string> log)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true }); }
        catch (JsonException ex)
        {
            log($"The old Keylight settings file is damaged ({ex.Message}); starting from defaults. The old file is untouched.");
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                log("The old Keylight settings file is not a settings object; starting from defaults. The old file is untouched.");
                return null;
            }

            var s = new LegacySettings();
            s.Version = Int(root, "Version", s.Version);
            s.FollowPlayer = Bool(root, "FollowPlayer", s.FollowPlayer);
            s.ShowLightMarkers = Bool(root, "ShowLightMarkers", s.ShowLightMarkers);
            s.SelectedPresetId = Str(root, "SelectedPresetId", s.SelectedPresetId);
            s.ShowBackdrop = Bool(root, "ShowBackdrop", s.ShowBackdrop);
            if (Obj(root, "Backdrop") is { } b)
            {
                s.Backdrop = new BackdropAdjust
                {
                    ScaleX = Float(b, "ScaleX", 1f),
                    ScaleY = Float(b, "ScaleY", 1f),
                    DistanceOffset = Float(b, "DistanceOffset", 0f),
                    YawOffsetDeg = Float(b, "YawOffsetDeg", 0f),
                    HeightOffset = Float(b, "HeightOffset", 0f),
                };
            }
            s.CanvasYawOffsetDeg = Float(root, "CanvasYawOffsetDeg", s.CanvasYawOffsetDeg);
            s.CanvasHeightOffset = Float(root, "CanvasHeightOffset", s.CanvasHeightOffset);
            if (Obj(root, "TestResults") is { } tr)
            {
                foreach (var p in tr.EnumerateObject())
                {
                    if (p.Name == "$type" || p.Value.ValueKind != JsonValueKind.Object) continue;
                    s.TestResults[p.Name] = new TestResult
                    {
                        Status = Str(p.Value, "Status", ""),
                        Note = Str(p.Value, "Note", ""),
                        When = Str(p.Value, "When", ""),
                    };
                }
            }
            s.PoseFolders = Strings(root, "PoseFolders");
            s.PoseFoldersInitialised = Bool(root, "PoseFoldersInitialised", s.PoseFoldersInitialised);
            s.IncludeBrioLibraryFolders = Bool(root, "IncludeBrioLibraryFolders", s.IncludeBrioLibraryFolders);
            s.PoseBody = Bool(root, "PoseBody", s.PoseBody);
            s.PoseExpression = Bool(root, "PoseExpression", s.PoseExpression);
            s.PoseHoverPreview = Bool(root, "PoseHoverPreview", s.PoseHoverPreview);
            s.FavouritePoses = Strings(root, "FavouritePoses");
            s.RecentPoses = Strings(root, "RecentPoses");
            return s;
        }
    }

    // ---- tolerant readers: a missing or wrongly typed property gives the default ----

    private static bool TryGet(JsonElement o, string name, out JsonElement v)
    {
        v = default;
        return o.ValueKind == JsonValueKind.Object && o.TryGetProperty(name, out v);
    }

    private static bool Bool(JsonElement o, string name, bool def) =>
        TryGet(o, name, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : def;

    private static int Int(JsonElement o, string name, int def) =>
        TryGet(o, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : def;

    private static float Float(JsonElement o, string name, float def) =>
        TryGet(o, name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : def;

    private static string Str(JsonElement o, string name, string def) =>
        TryGet(o, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    private static JsonElement? Obj(JsonElement o, string name) =>
        TryGet(o, name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static List<string> Strings(JsonElement o, string name)
    {
        var list = new List<string>();
        if (!TryGet(o, name, out var v) || v.ValueKind != JsonValueKind.Array) return list;
        foreach (var e in v.EnumerateArray())
            if (e.ValueKind == JsonValueKind.String && e.GetString() is { } str) list.Add(str);
        return list;
    }
}

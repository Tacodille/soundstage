using Dalamud.Configuration;
using Soundstage.Migration;

namespace Soundstage;

/// <summary>Saved by Dalamud to pluginConfigs\Soundstage.json.</summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    /// <summary>
    /// 2: intensity and range defaults raised to Stagehand's editor defaults.
    /// 3 (beta): the key/fill sliders now edit the applied preset, so the old KeyIntensity/KeyRange/Fill*/Key* values are gone.
    /// 4 (Soundstage): adds <see cref="Sections"/>, <see cref="DevMode"/> and <see cref="MigratedFromKeylight"/>. Nothing is reset.
    /// </summary>
    public const int CurrentVersion = 4;

    public int Version { get; set; } = CurrentVersion;

    public bool FollowPlayer { get; set; } = false;

    /// <summary>Draw on-screen markers for every placed light.</summary>
    public bool ShowLightMarkers { get; set; } = true;

    /// <summary>The preset shown in the dropdown (not necessarily applied).</summary>
    public string SelectedPresetId { get; set; } = "";

    /// <summary>Show the applied preset's backdrop. Off keeps the lights and drops the panel (beta feedback 18/11/2026).</summary>
    public bool ShowBackdrop { get; set; } = true;

    /// <summary>Live backdrop fixes, kept between sessions.</summary>
    public BackdropAdjust Backdrop { get; set; } = new();

    /// <summary>Canvas test: yaw offset (0/90/180/270) and height offset in metres.</summary>
    public float CanvasYawOffsetDeg { get; set; }
    public float CanvasHeightOffset { get; set; }

    /// <summary>
    /// Test checklist results. Key is "area/step" (for example "core/4", or "core/3:presetId" for one preset in the core
    /// preset step). Results carried over from Keylight keep their old keys ("1", "3:neon-green-rim") and count for nothing here.
    /// </summary>
    public Dictionary<string, TestResult> TestResults { get; set; } = new();

    // ---- Poses shelf ----

    /// <summary>Folders scanned (recursively) for .pose and .cmp files.</summary>
    public List<string> PoseFolders { get; set; } = new();

    /// <summary>False until the default folder (Documents\FFXIV Mods) has been put in <see cref="PoseFolders"/> once.</summary>
    public bool PoseFoldersInitialised { get; set; }

    /// <summary>Also scan Documents\Brio\Poses and Documents\Anamnesis\Poses when they exist (Brio's own library folders).</summary>
    public bool IncludeBrioLibraryFolders { get; set; } = true;

    /// <summary>Which parts of a pose to apply. A .cmp has no expression part.</summary>
    public bool PoseBody { get; set; } = true;
    public bool PoseExpression { get; set; } = true;

    /// <summary>Load a pose after the mouse rests on its row, and put the old pose back when the mouse leaves the list.</summary>
    public bool PoseHoverPreview { get; set; } = true;

    /// <summary>Full paths of starred poses.</summary>
    public List<string> FavouritePoses { get; set; } = new();

    /// <summary>Full paths of poses loaded by a click, newest first (at most <see cref="MaxRecentPoses"/>).</summary>
    public List<string> RecentPoses { get; set; } = new();
    public const int MaxRecentPoses = 8;

    // ---- Soundstage ----

    /// <summary>Set once the old Keylight config was carried over, so it never runs twice.</summary>
    public bool MigratedFromKeylight { get; set; }

    /// <summary>Shows the Tools tab. Toggled by /soundstage dev.</summary>
    public bool DevMode { get; set; }

    /// <summary>Per-feature settings as JSON text, keyed by feature. Use <see cref="Section{T}"/> and <see cref="SetSection{T}"/>.</summary>
    public Dictionary<string, string> Sections { get; set; } = new();

    public T Section<T>(string key) where T : new() => ConfigSections.Get<T>(Sections, key);

    public void SetSection<T>(string key, T value) => ConfigSections.Set(Sections, key, value);

    /// <summary>Copies every old Keylight field over and marks the migration done.</summary>
    public void ApplyLegacy(LegacySettings s)
    {
        FollowPlayer = s.FollowPlayer;
        ShowLightMarkers = s.ShowLightMarkers;
        SelectedPresetId = s.SelectedPresetId;
        ShowBackdrop = s.ShowBackdrop;
        Backdrop = s.Backdrop;
        CanvasYawOffsetDeg = s.CanvasYawOffsetDeg;
        CanvasHeightOffset = s.CanvasHeightOffset;
        TestResults = s.TestResults;
        PoseFolders = s.PoseFolders;
        PoseFoldersInitialised = s.PoseFoldersInitialised;
        IncludeBrioLibraryFolders = s.IncludeBrioLibraryFolders;
        PoseBody = s.PoseBody;
        PoseExpression = s.PoseExpression;
        PoseHoverPreview = s.PoseHoverPreview;
        FavouritePoses = s.FavouritePoses;
        RecentPoses = s.RecentPoses;
        Version = CurrentVersion;
        MigratedFromKeylight = true;
    }

    public bool MigrateIfOld()
    {
        if (Version >= CurrentVersion) return false;
        Version = CurrentVersion;
        return true;
    }
}

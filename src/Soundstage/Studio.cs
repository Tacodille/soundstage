using Dalamud.Plugin.Services;
using Soundstage.Look;
using Soundstage.Tabs;

namespace Soundstage;

/// <summary>
/// The one context object handed to every tab and checklist hook: the bridges, the config and the loaded presets.
/// A later part that adds a service (a camera, a capture service, an AI client) adds one property here and one line in
/// the Plugin constructor, and nothing else in the shared files.
/// </summary>
public sealed class Studio
{
    public Studio(Plugin plugin, BrioBridge brio, IPluginLog log)
    {
        Plugin = plugin;
        Brio = brio;
        Log = log;
        Soundstage.Ai.AiService.GuardShareCodes(() => Ai);   // share codes refuse the stored AI key by value
    }

    /// <summary>The plugin itself: apply, place, save and the rest of what the first prototype's single window called directly.</summary>
    public Plugin Plugin { get; }

    public BrioBridge Brio { get; }
    public IPluginLog Log { get; }

    /// <summary>Added in Part 2 so a tab can reach the local player (the probe needs it for Brio route B). Set by the Plugin constructor.</summary>
    public IObjectTable? Objects { get; init; }

    /// <summary>Added in Part 10: the camera service, turntable and thirds overlay. Set by the Plugin constructor.</summary>
    public CameraTools.CameraHost? Camera { get; init; }

    /// <summary>Added in Part 11: applies a Scene's look through the ReShade add-on and drives auto focus. Set by the Plugin constructor.</summary>
    public LookService Look { get; init; } = null!;

    public Configuration Config => Plugin.Config;
    public StagehandBridge Bridge => Plugin.Bridge;
    public PoseShelf Poses => Plugin.Poses;
    public IReadOnlyList<Scene> Presets => Plugin.Presets;
    public IReadOnlyList<Scene> Scenes => Plugin.Scenes;

    /// <summary>Part 7: undo, snapshot, user Scenes, share codes and the pose and outfit block.</summary>
    public SceneDesk Desk => Plugin.Desk;

    /// <summary>The working copy of the applied preset.</summary>
    public Scene? Current => Plugin.Current;

    /// <summary>The optional AI layer (part 6). Built on first use; nothing else in the plugin needs it.</summary>
    public Soundstage.Ai.AiService Ai => ai ??= CreateAi();
    private Soundstage.Ai.AiService? ai;

    private Soundstage.Ai.AiService CreateAi()
    {
        var settings = Config.Section<Soundstage.Ai.AiSettings>(Soundstage.Ai.AiService.ConfigKey);
        return new Soundstage.Ai.AiService(settings,
            () => { Config.SetSection(Soundstage.Ai.AiService.ConfigKey, settings); Save(); },
            line => Log.Information("{Line:l}", line),
            new Soundstage.Ai.DpapiKeyProtector());
    }

    /// <summary>The one face helper handle the Face tab, the Expression tab and the body retarget share (one helper process, not one per tab).</summary>
    public Soundstage.Face.FaceServices Face => faces.Get();
    private Soundstage.Face.SharedFace? facesBacking;
    private Soundstage.Face.SharedFace faces => facesBacking ??= new Soundstage.Face.SharedFace(() => new Soundstage.Face.FaceServices(
        Plugin.ConfigDirectory, Config.Section<Soundstage.Face.FaceSettings>(Soundstage.Face.FaceSettings.Key), line => Log.Information("{Line:l}", line)));

    public void Save() => Plugin.Save();

    public void OpenMainWindow() => Plugin.OpenMainWindow();

    public void OpenChecklist() => Plugin.OpenChecklist();
}

using System.Runtime.CompilerServices;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Soundstage.Capture;
using Soundstage.GameAccess;
using Soundstage.Look;
using Soundstage.Overlays;

namespace Soundstage.Workflow;

/// <summary>Read by the plugin's own windows, which skip drawing while a capture has them hidden.</summary>
public static class WorkflowState
{
    public static volatile bool UiHidden;
}

/// <summary>
/// Everything Part 12 owns that touches Dalamud: the capture service, the booth, the contact sheet and the onion skin. It is found from a
/// Studio with <see cref="For"/> (no new Studio property), and the plugin attaches it with one line that hooks the UI draw event, which
/// is what ticks the booth and the sheet and draws the onion skin.
/// </summary>
public sealed class WorkflowHost
{
    private static readonly ConditionalWeakTable<Studio, WorkflowHost> Hosts = new();

    public static WorkflowHost For(Studio studio) => Hosts.GetValue(studio, s => new WorkflowHost(s));

    private readonly Studio studio;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly ReShadePipe pipe = new(new ReShadeProbe());
    private IGameGui? gameGui;
    private bool attached;
    private bool markersWere = true;
    private (bool thirds, bool golden, bool cross, bool margin) overlaysWere;
    private bool weHidGameUi;
    private SheetRestore? sheetRestore;

    private sealed record SheetRestore(Scene? Scene, string Palette, CameraState? Camera);

    private WorkflowHost(Studio studio)
    {
        this.studio = studio;
        Settings = studio.Config.Section<WorkflowSettings>(WorkflowSettings.Key);
        GameFolder = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        Capture = new CaptureService(BuildRoutes, () => TimeSpan.FromSeconds(Math.Clamp(Settings.TimeoutSeconds, 2, 60)), Log);
        Hider = new UiHider(BuildTargets, Log);
        Booth = new PhotoBooth(Hider, Capture.CaptureAsync, InGpose, () => clock.ElapsedMilliseconds, () => Settings.HideSettleMs, Log);
        Sheet = new ContactSheet(Hider, Capture.CaptureAsync, InGpose, () => clock.ElapsedMilliseconds,
            () => Settings.SettleMs, () => Settings.HideSettleMs, SaveSheetState, RestoreSheetState, Log);
        Onion = new OnionSkin(() => Settings.Onion, () => Hider.IsHidden);
    }

    public WorkflowSettings Settings { get; }
    public string GameFolder { get; }
    public CaptureService Capture { get; }
    public UiHider Hider { get; }
    public PhotoBooth Booth { get; }
    public ContactSheet Sheet { get; }
    public OnionSkin Onion { get; }

    /// <summary>The one line the plugin calls: hooks the draw event and hands over the two Dalamud services this part needs.</summary>
    public void Attach(IDalamudPluginInterface pi, ITextureProvider textures, IGameGui gui)
    {
        if (attached) return;
        attached = true;
        gameGui = gui;
        Onion.Textures = textures;
        pi.UiBuilder.Draw += Draw;
    }

    public void Persist()
    {
        studio.Config.SetSection(WorkflowSettings.Key, Settings);
        studio.Save();
    }

    public string OutputFolder =>
        string.IsNullOrWhiteSpace(Settings.OutputFolder)
            ? ScreenshotFolder.DefaultOutput(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments))
            : Settings.OutputFolder;

    public string NextPath(string prefix = "Soundstage") => Path.Combine(OutputFolder, CaptureService.DefaultName(DateTime.Now, prefix));

    public bool InGpose()
    {
        try { return GameAccessHost.Guard.InGpose(); }
        catch (Exception) { return false; }
    }

    /// <summary>Runs from UiBuilder.Draw, so it ticks with the main window closed.</summary>
    private void Draw()
    {
        try
        {
            Booth.Tick();
            Sheet.Tick();
            Onion.Draw();
        }
        catch (Exception ex) { Log("tick failed: " + ex.Message); }
    }

    private void Log(string line) => studio.Log.Information("{Line:l}", line.StartsWith("[Soundstage/") ? line : CaptureService.LogPrefix + " " + line);

    // ---- routes ----

    private IReadOnlyList<ICaptureRoute> BuildRoutes()
    {
        var list = new List<ICaptureRoute>();
        foreach (var id in Settings.RouteOrder)
        {
            switch (id)
            {
                case "addon": list.Add(new AddonShotRoute(pipe)); break;
                case "key": list.Add(new KeyScreenshotRoute(ShotFolder, () => Settings.ScreenshotKey)); break;
                case "window": list.Add(new WindowCaptureRoute()); break;
            }
        }
        return list;
    }

    private string ShotFolder() => ScreenshotFolder.Resolve(Settings.ScreenshotFolder, GameFolder,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), ReShadeIni.TryLoad(GameFolder));

    // ---- hiding ----

    private IReadOnlyList<HideTarget> BuildTargets()
    {
        var cam = studio.Camera?.Settings;
        var list = new List<HideTarget>
        {
            new("the Soundstage windows", () => WorkflowState.UiHidden = true, () => WorkflowState.UiHidden = false),
            new("the light markers",
                () => { markersWere = studio.Config.ShowLightMarkers; studio.Config.ShowLightMarkers = false; },
                () => studio.Config.ShowLightMarkers = markersWere),
        };
        if (cam != null)
            list.Add(new("the composition overlay",
                () =>
                {
                    overlaysWere = (cam.Thirds, cam.Golden, cam.CentreCross, cam.SafeMargin);
                    cam.Thirds = cam.Golden = cam.CentreCross = cam.SafeMargin = false;
                },
                () => (cam.Thirds, cam.Golden, cam.CentreCross, cam.SafeMargin) = overlaysWere));
        if (Settings.HideGameUi && gameGui != null)
        {
            var gui = gameGui;
            list.Add(new("the game UI",
                () =>
                {
                    weHidGameUi = false;
                    if (gui.GameUiHidden) return;   // already hidden by the player: leave it that way afterwards
                    NativeKeys.Tap(NativeKeys.VkScrollLock);
                    weHidGameUi = true;
                },
                () =>
                {
                    if (weHidGameUi && gui.GameUiHidden) NativeKeys.Tap(NativeKeys.VkScrollLock);
                    weHidGameUi = false;
                },
                () => gui.GameUiHidden));
        }
        return list;
    }

    // ---- contact sheet ----

    private void SaveSheetState()
    {
        CameraState? cam = null;
        try
        {
            var r = GameAccessHost.Guard.ReadCamera();
            if (r.Ok) cam = r.Value;
        }
        catch (Exception) { }
        sheetRestore = new SheetRestore(studio.Current?.Clone(), studio.Plugin.SelectedPalette, cam);
    }

    /// <summary>
    /// Puts the Scene back through the plugin's own undo path (<c>RestoreWorking</c>), which re-applies the look but not the camera, then writes the
    /// camera back by hand. The applies made during the sheet stay in the undo list as ordinary steps.
    /// </summary>
    private void RestoreSheetState()
    {
        var r = sheetRestore;
        sheetRestore = null;
        if (r == null) return;
        if (r.Scene != null) studio.Plugin.RestoreWorking(r.Scene, r.Palette);
        if (r.Camera is { } c)
            GameAccessHost.Guard.WriteCamera(new CameraWrite(c.Yaw, c.Pitch, c.Distance, c.AddedFov, c.Position));
    }

    public IReadOnlyList<SheetItem> ItemsFor(IEnumerable<string> ids)
    {
        var items = new List<SheetItem>();
        foreach (var id in ids)
        {
            var scene = SceneStore.Resolve(studio.Scenes, id, out var palette);
            if (scene == null) continue;
            items.Add(new SheetItem(scene.Id, scene.Name, () => studio.Plugin.Apply(scene, palette)));
        }
        return items;
    }
}

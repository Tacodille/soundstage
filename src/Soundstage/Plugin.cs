using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Soundstage.Checklist;
using Soundstage.GameAccess;
using Soundstage.Look;
using Soundstage.Migration;
using Soundstage.Tabs;

namespace Soundstage;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/soundstage";
    private const string MigrateLogPrefix = "[Soundstage/MIGRATE]";
    private const float MoveThresholdMetres = 0.02f;
    private const float TurnThresholdRadians = 1f * MathF.PI / 180f;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly ICommandManager commands;
    private readonly IFramework framework;
    private readonly IObjectTable objects;
    private readonly IGameGui gameGui;
    private readonly IPluginLog log;
    private readonly IChatGui chat;
    private readonly Configuration config;
    private readonly StagehandBridge bridge;
    private readonly BrioBridge brio;
    private readonly PoseShelf poses;
    private readonly SceneDesk desk;
    private readonly LookService look;
    private readonly LightingService lighting;
    private readonly WindowSystem windows = new("Soundstage");
    private readonly MainWindow window;
    private readonly ChecklistWindow checklist;
    private readonly Studio studio;
    private readonly CameraTools.CameraHost cameraHost;   // part 10
    private readonly byte[]? canvasTex;
    private readonly string shippedScenesPath;
    private readonly string userScenesPath;
    private readonly string legacyUserPresetsPath;

    private List<Scene> presets = new();
    private int nextPresetIndex;

    /// <summary>All loaded Scenes: built-in, shipped, then "Mine".</summary>
    public IReadOnlyList<Scene> Scenes => presets;

    /// <summary>Old name of <see cref="Scenes"/>, kept until the Lighting tab (part 8) stops using it.</summary>
    public IReadOnlyList<Scene> Presets => presets;

    /// <summary>Part 7: undo, snapshot, user Scenes, share codes and the pose and outfit block.</summary>
    public SceneDesk Desk => desk;

    /// <summary>The working copy of the applied preset. The sliders edit this; Apply replaces it.</summary>
    public Scene? Current { get; private set; }

    /// <summary>One line for the main window: the result of the last Save / Reload / Apply.</summary>
    public string Message { get; private set; } = "";

    public Configuration Config => config;
    public StagehandBridge Bridge => bridge;
    public PoseShelf Poses => poses;

    /// <summary>Face aim, animation and the exposure settings (part 8).</summary>
    public LightingService Lighting => lighting;

    /// <summary>The plugin's folder under pluginConfigs (user presets, pose tags).</summary>
    public string ConfigDirectory => pluginInterface.GetPluginConfigDirectory();

    private Vector3 lastSentPosition;
    private float lastSentYaw;

    // Set by Stagehand's LocationChanged (which can arrive off the framework thread); read on the framework tick.
    private const double RebuildGiveUpSeconds = 10.0;
    private volatile bool rebuildPending;
    private DateTime rebuildDeadline;
    private DateTime rebuildNextLog;
    private int rebuildAttempts;

    public Plugin(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commands,
        IFramework framework,
        IPluginLog log,
        IObjectTable objects,
        IGameGui gameGui,
        IChatGui chat,
        IClientState clientState,
        ITargetManager targetManager,
        ITextureProvider textureProvider)
    {
        this.gameGui = gameGui;
        this.log = log;
        this.chat = chat;
        this.pluginInterface = pluginInterface;
        this.commands = commands;
        this.framework = framework;
        this.objects = objects;

        // Before the first GetPluginConfig: Dalamud cannot read the old Keylight.json (its $type names the old assembly).
        var migration = LegacyKeylight.Run(pluginInterface.ConfigFile.DirectoryName ?? "", line => log.Information("{P} {Line}", MigrateLogPrefix, line));
        if (migration.Settings != null)
        {
            config = new Configuration();
            config.ApplyLegacy(migration.Settings);
            pluginInterface.SavePluginConfig(config);
            log.Information("{P} Carried your Keylight settings over ({Outcome}, presets copied: {Copied}). The old files are untouched.",
                MigrateLogPrefix, migration.Outcome, migration.PresetsCopied);
        }
        else
        {
            config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        if (config.MigrateIfOld())
        {
            log.Information("{P} Config was an older version: intensity and range values reset to the new defaults.", StagehandBridge.LogPrefix);
            pluginInterface.SavePluginConfig(config);
        }
        bridge = new StagehandBridge(pluginInterface, log);
        bridge.LocationChanged += OnLocationChanged;
        brio = new BrioBridge(pluginInterface, log);
        poses = new PoseShelf(config, Save, brio, clientState, targetManager, objects, textureProvider, log);

        shippedScenesPath = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName ?? "", "scenes.json");
        userScenesPath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "user-scenes.json");
        legacyUserPresetsPath = Path.Combine(pluginInterface.GetPluginConfigDirectory(), "user-presets.json");
        canvasTex = LoadCanvasTexture();
        desk = new SceneDesk(this, pluginInterface, brio, poses, log, userScenesPath);
        // Before the first catalogue load, so the "does not apply" log line leaves out the animation block this build plays.
        lighting = new LightingService(this, objects, log, Path.Combine(pluginInterface.AssemblyLocation.DirectoryName ?? "", "gels.json"));
        SetCatalogue.Load(pluginInterface.AssemblyLocation.DirectoryName ?? "", line => log.Information("{P} {Line}", SetCatalogue.LogPrefix, line));   // part 9: kits and atmosphere items, before the first scene is built
        ReloadScenes();

        cameraHost = new CameraTools.CameraHost(config, Save, objects, gameGui, log);   // part 10

        look = new LookService(new ReShadePipe(new ReShadeProbe()), Path.GetDirectoryName(Environment.ProcessPath) ?? "",
            () => config.Section<LookSettings>(LookSettings.Key), s => { config.SetSection(LookSettings.Key, s); Save(); },
            line => log.Information("{Line}", line)) { FocusSource = LookFocusSource.Read };
        _ = look.ProbeAsync();

        studio = new Studio(this, brio, log) { Objects = objects, Camera = cameraHost, Look = look };
        window = new MainWindow(studio, new TabHost(log));
        checklist = new ChecklistWindow(studio);
        windows.AddWindow(window);
        windows.AddWindow(checklist);

        // Stagehand does the same, so the window stays up in GPose. DisableCutsceneUiHide and DisableUserUiHide are
        // left at their defaults: cutscenes and the scroll-lock UI hide are times you want the window out of the way.
        pluginInterface.UiBuilder.DisableGposeUiHide = true;

        pluginInterface.UiBuilder.Draw += windows.Draw;
        pluginInterface.UiBuilder.Draw += DrawMarkers;   // its own handler, so it runs with the main window closed
        pluginInterface.UiBuilder.Draw += cameraHost.Overlay.Draw;   // part 10: thirds overlay
        Soundstage.Workflow.WorkflowHost.For(studio).Attach(pluginInterface, textureProvider, gameGui);   // part 12: ticks the booth and contact sheet, draws the onion skin
        pluginInterface.UiBuilder.OpenConfigUi += OpenWindow;
        pluginInterface.UiBuilder.OpenMainUi += OpenWindow;
        framework.Update += OnFrameworkUpdate;

        commands.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Soundstage window. \"/soundstage test\" opens the test checklist. \"/soundstage dev\" shows or hides the Tools tab. \"/soundstage markers\" toggles the light markers (\"on\" or \"off\" to set them); "
                        + "put it on a macro to flip them before a shot. \"/soundstage survey 3\" logs the game's lights within 25 m, labelled level 3.",
        });
    }

    public void Dispose()
    {
        commands.RemoveHandler(CommandName);
        framework.Update -= OnFrameworkUpdate;
        pluginInterface.UiBuilder.OpenMainUi -= OpenWindow;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenWindow;
        pluginInterface.UiBuilder.Draw -= DrawMarkers;
        pluginInterface.UiBuilder.Draw -= cameraHost.Overlay.Draw;
        cameraHost.Dispose();   // stops a running turntable and puts the camera back
        pluginInterface.UiBuilder.Draw -= windows.Draw;
        windows.RemoveAllWindows();
        poses.Dispose();   // puts a previewed pose back first
        desk.ClearUndo();   // memory only
        look.Dispose();   // puts your own ReShade preset back, then frees the add-on pipe
        bridge.LocationChanged -= OnLocationChanged;
        bridge.Dispose();   // destroys the stage if it is placed
    }

    private void OpenWindow() => window.IsOpen = true;

    public void OpenChecklist() => checklist.IsOpen = true;

    public void OpenMainWindow() => window.IsOpen = true;

    public void SetMessage(string message) => Message = message;

    /// <summary>The folder Soundstage.dll was loaded from (scenes.json and the dev test files sit beside it).</summary>
    public string AssemblyDirectory => pluginInterface.AssemblyLocation.DirectoryName ?? "";

    /// <summary>Dev test: applies the six-light Scene (six-lights.json beside the dll, written by tools/gen_scenes.py).</summary>
    public bool ApplySixLightTest()
    {
        var scenes = SceneStore.LoadFile(Path.Combine(AssemblyDirectory, "six-lights.json"), bridge.Info);
        if (scenes.Count == 0) { Message = "six-lights.json not found beside Soundstage.dll."; return false; }
        return Apply(scenes[0]);
    }

    public void Save() => pluginInterface.SavePluginConfig(config);

    private void OnCommand(string command, string args)
    {
        var parts = args.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && parts[0] == "test")
        {
            checklist.IsOpen = true;
            return;
        }
        if (parts.Length > 0 && parts[0] == "dev")
        {
            config.DevMode = parts.Length > 1 && parts[1] is "on" or "off" ? parts[1] == "on" : !config.DevMode;
            pluginInterface.SavePluginConfig(config);
            chat.Print($"Soundstage dev tools are {(config.DevMode ? "on: the Tools tab is showing" : "off")}.");
            return;
        }
        if (parts.Length > 0 && parts[0] == "survey")
        {
            RunSurvey(parts.Length > 1 ? "level " + string.Join(' ', parts.Skip(1)) : "unlabelled", 25f);
            return;
        }
        if (parts.Length == 0 || parts[0] != "markers")
        {
            window.Toggle();
            return;
        }
        config.ShowLightMarkers = parts.Length > 1 && parts[1] is "on" or "off" ? parts[1] == "on" : !config.ShowLightMarkers;
        pluginInterface.SavePluginConfig(config);
    }

    private void DrawMarkers()
    {
        // Hidden while the game's own UI is hidden (Scroll Lock / the GPose hide-UI toggle), so they stay out of shots.
        if (!config.ShowLightMarkers || gameGui.GameUiHidden || !bridge.AnyPlaced) return;
        try
        {
            var markers = bridge.GetMarkers();
            if (markers.Count == 0) return;

            var dl = ImGui.GetBackgroundDrawList();
            const uint white = 0xFFFFFFFF, black = 0xFF000000, shadow = 0xC0000000;

            foreach (var m in markers)
            {
                if (!gameGui.WorldToScreen(m.WorldPosition, out var p)) continue;   // behind the camera
                var col = ImGui.ColorConvertFloat4ToU32(new Vector4(
                    Math.Clamp(m.Colour.X, 0f, 1f), Math.Clamp(m.Colour.Y, 0f, 1f), Math.Clamp(m.Colour.Z, 0f, 1f), 1f));

                // Dashed drop line to the floor (the stage origin's Y, which is the player's feet).
                var floor = new Vector3(m.WorldPosition.X, m.FloorY, m.WorldPosition.Z);
                if (gameGui.WorldToScreen(floor, out var fp))
                {
                    const int dashes = 8;
                    for (var i = 0; i < dashes; i += 2)
                    {
                        var a = Vector2.Lerp(p, fp, i / (float)dashes);
                        var b = Vector2.Lerp(p, fp, (i + 1) / (float)dashes);
                        dl.AddLine(a, b, 0xB0FFFFFF, 1.5f);
                    }
                    dl.AddCircleFilled(fp, 3f, 0x80FFFFFF);
                }

                // Spot beam: 1 m along the light's +Z, with an end dot.
                if (m.WorldBeamDirection is { } dir && gameGui.WorldToScreen(m.WorldPosition + dir, out var ep))
                {
                    dl.AddLine(p, ep, black, 4f);
                    dl.AddLine(p, ep, col, 2f);
                    dl.AddCircleFilled(ep, 4f, col);
                    dl.AddCircle(ep, 4f, white, 12, 1.5f);
                }

                dl.AddCircleFilled(p, 8f, col);
                dl.AddCircle(p, 9f, black, 24, 3f);
                dl.AddCircle(p, 8f, white, 24, 1.5f);

                var label = $"{m.Label} {m.Intensity:0.#}";
                var pos = new Vector2(p.X + 12f, p.Y - 8f);
                dl.AddText(pos + new Vector2(1f, 1f), shadow, label);
                dl.AddText(pos, white, label);
            }
        }
        catch (Exception ex)
        {
            bridge.Info($"DrawMarkers exception: {ex.Message}");
        }
    }

    private byte[]? LoadCanvasTexture()
    {
        try
        {
            using var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Soundstage.canvas_test_d.tex");
            if (stream == null) { bridge.Info("Canvas test texture resource is missing from the dll."); return null; }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            bridge.Info($"Canvas test texture loaded: {ms.Length} bytes (.tex, B8G8R8A8 512x512).");
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            bridge.Info($"Canvas test texture could not be loaded: {ex.Message}");
            return null;
        }
    }

    // ---- Presets ----

    /// <summary>Old name of <see cref="ReloadScenes"/>.</summary>
    public void ReloadPresets() => ReloadScenes();

    public void ReloadScenes()
    {
        presets = SceneStore.LoadAll(shippedScenesPath, userScenesPath, legacyUserPresetsPath, bridge.Info, new[] { SceneStore.BuiltInWarmKeyCoolFill() });
        if (nextPresetIndex >= presets.Count) nextPresetIndex = 0;
        // An old id (neon-orange-teal-split, say) now means a Scene and a palette: switch the saved selection over to them.
        var found = SceneStore.Resolve(presets, config.SelectedPresetId, out var legacyPalette);
        if (found != null)
        {
            if (found.Id != config.SelectedPresetId) SelectScene(found.Id, legacyPalette);
        }
        else config.SelectedPresetId = presets.Count > 0 ? presets[0].Id : "";
        Message = $"Loaded {presets.Count} scenes.";
    }

    /// <summary>The palette chosen for the selected Scene, or "" for the Scene's own default.</summary>
    public string SelectedPalette => config.Section<ScenesSettings>(ScenesSettings.Key).SelectedPalette;

    public void SelectScene(string id, string? palette)
    {
        config.SelectedPresetId = id;
        config.SetSection(ScenesSettings.Key, new ScenesSettings { SelectedPalette = palette ?? "" });
        Save();
    }

    public Scene? SelectedScene() => SceneStore.Resolve(presets, config.SelectedPresetId, out _);

    /// <summary>Old name of <see cref="SelectedScene"/>.</summary>
    public Scene? SelectedPreset() => SelectedScene();

    /// <summary>Makes a working copy of the Scene in the named palette (null: the default), then creates or updates the stage from it.</summary>
    public bool Apply(Scene preset, string? palette = null)
    {
        config.SelectedPresetId = preset.Id;
        Current = SceneStore.ApplyPalette(preset, palette);
        bridge.Info($"Apply scene '{preset.Id}' palette '{Current.DefaultPalette}' ({preset.Lights.Count} lights, backdrop {(preset.Backdrop != null ? "yes" : "no")}).");
        var ok = PlaceNow();
        look.ApplyScene(preset);
        Message = ok ? $"Applied '{preset.Name}'." : "Apply failed: " + bridge.LastError;
        if (ok) desk.OnApplied(preset, Current, Current.DefaultPalette);
        if (ok) cameraHost.OnSceneApplied(preset);   // part 10: the camera block, when "Apply camera" is on
        return ok;
    }

    /// <summary>Undo's way back: puts a remembered working Scene (slider tweaks included) on stage without recording a new step.</summary>
    public bool RestoreWorking(Scene working, string palette)
    {
        Current = working.Clone();
        var found = SceneStore.Resolve(presets, working.Id, out _);
        if (found != null) SelectScene(found.Id, palette);
        var ok = PlaceNow();
        look.ApplyScene(working);   // undo puts the look back too; the camera is left alone so undo does not move your view
        Message = ok ? $"Restored '{working.Name}'." : "Restore failed: " + bridge.LastError;
        return ok;
    }

    /// <summary>The next preset in the list, applied. Returns it (also when the apply failed), or null if there are none.</summary>
    public Scene? ApplyNext()
    {
        if (presets.Count == 0) return null;
        var p = presets[nextPresetIndex % presets.Count];
        nextPresetIndex = (nextPresetIndex + 1) % presets.Count;
        Apply(p);
        return p;
    }

    /// <summary>The user-presets file only: every loaded preset in "Mine".</summary>
    public bool SaveCurrentAs(string name)
    {
        name = name.Trim();
        if (Current == null) { Message = "Nothing applied yet: apply a preset first."; return false; }
        if (name.Length == 0) { Message = "Type a name first."; return false; }
        try
        {
            var copy = Current.Clone();
            copy.Name = name;
            copy.Category = "Mine";
            if (!config.ShowBackdrop) copy.Backdrop = null;
            copy.Id = SceneStore.Slug(name);
            copy.LegacyIds = new();
            copy.BakeDefaultPalette();
            var mine = presets.Where(p => p.Category == "Mine" && p.Id != copy.Id).ToList();
            mine.Add(copy);
            SceneStore.SaveUser(userScenesPath, mine);
            bridge.Info($"Saved user scene '{copy.Id}' to {userScenesPath}.");
            ReloadPresets();
            config.SelectedPresetId = copy.Id;
            Message = $"Saved '{name}' to Mine.";
            return true;
        }
        catch (Exception ex)
        {
            Message = "Save failed: " + ex.Message;
            bridge.Info("Save user preset -> EXCEPTION " + ex);
            return false;
        }
    }

    public void RemoveAll()
    {
        bridge.RemovePreview();
        bridge.RemoveSmoke();
        bridge.RemoveCanvas();
        look.RemoveLook();
        bridge.Info("Remove all: preset, smoke and canvas stages destroyed.");
        Message = "Everything removed.";
    }

    // ---- Light survey ----

    /// <summary>
    /// Logs every game light within <paramref name="radius"/> metres, tagged with <paramref name="label"/> (for example
    /// "level 3" for the housing brightness setting). Read-only; runs on the framework thread.
    /// </summary>
    public void RunSurvey(string label, float radius)
    {
        label = label.Trim();
        framework.RunOnFrameworkThread(() =>
        {
            var local = objects.LocalPlayer;
            if (local == null) { Message = "Survey: not logged in."; return; }
            try
            {
                var all = LightSurvey.Take(local.Position, out var nodes);
                var near = all.Where(f => f.Distance <= radius).ToList();
                log.Information("{P} BEGIN label='{L}' radius={R} lightsInWorld={A} lightsInRadius={N} nodesVisited={V} player={Pos}",
                    LightSurvey.LogPrefix, label, radius, all.Count, near.Count, nodes, local.Position);
                for (var i = 0; i < near.Count; i++)
                    log.Information("{P} label='{L}' #{I} {Line}", LightSurvey.LogPrefix, label, i, near[i].Line);
                log.Information("{P} END label='{L}'", LightSurvey.LogPrefix, label);
                Message = $"Survey '{label}': {near.Count} light(s) within {radius:0} m ({all.Count} in the zone). Logged.";
            }
            catch (Exception ex)
            {
                log.Information("{P} EXCEPTION label='{L}' {E}", LightSurvey.LogPrefix, label, ex);
                Message = "Survey failed: " + ex.Message;
            }
        });
    }

    // ---- Placing ----

    /// <summary>Create or update the preset stage at the player's current position and yaw.</summary>
    /// <param name="quiet">True for the animation and face-aim re-sends: they run up to 15 times a second, so only failures are logged.</param>
    public bool PlaceNow(bool quiet = false)
    {
        if (Current == null) return bridge.Skip("Place preset", "No preset applied yet.");
        var local = objects.LocalPlayer;
        if (local == null) return bridge.Skip("Place preset", "LocalPlayer is null (not logged in or still loading).");
        lastSentPosition = local.Position;
        lastSentYaw = local.Rotation;
        var face = lighting.ResolveFace(Current);   // null: head not readable or no face-aimed light, lights go where the Scene put them
        var preset = lighting.Frame(Current);        // the Scene with its animation applied for this moment, or Current itself
        if (!config.ShowBackdrop && (preset.Backdrop != null || preset.Set?.Kind == "sweep"))
        {
            preset = preset.Clone();
            preset.Backdrop = null;
            if (preset.Set?.Kind == "sweep") preset.Set.Kind = "none";
        }
        return bridge.PlacePreview(preset, config.Backdrop, lastSentPosition, lastSentYaw, face, quiet);
    }

    /// <summary>The known-good red point light, 1.5 m in front of the player at 1.5 m height.</summary>
    public bool PlaceSmoke()
    {
        var local = objects.LocalPlayer;
        if (local == null) return bridge.Skip("Place smoke test", "LocalPlayer is null (not logged in or still loading).");
        return bridge.PlaceSmoke(local.Position, local.Rotation);
    }

    public void RemoveSmoke() => bridge.RemoveSmoke();

    public bool PlaceCanvas()
    {
        if (canvasTex == null) return bridge.Skip("Place canvas test", "The test texture is not embedded in the dll.");
        var local = objects.LocalPlayer;
        if (local == null) return bridge.Skip("Place canvas test", "LocalPlayer is null (not logged in or still loading).");
        return bridge.PlaceCanvas(canvasTex, config.CanvasYawOffsetDeg, config.CanvasHeightOffset, local.Position, local.Rotation);
    }

    public void RemoveCanvas() => bridge.RemoveCanvas();

    // Stagehand has just destroyed every temporary live stage. Re-send the full create for whatever we placed,
    // from the framework tick, retrying until LocalPlayer exists or the deadline passes.
    private void OnLocationChanged()
    {
        if (!bridge.AnyPlaced)
        {
            bridge.Info("LocationChanged: nothing placed, nothing to rebuild.");
            return;
        }
        rebuildDeadline = DateTime.UtcNow.AddSeconds(RebuildGiveUpSeconds);
        rebuildNextLog = DateTime.MinValue;
        rebuildAttempts = 0;
        rebuildPending = true;
        bridge.Info("LocationChanged: rebuild queued for the next framework ticks.");
    }

    private void TryRebuild()
    {
        var now = DateTime.UtcNow;
        rebuildAttempts++;
        if (now > rebuildDeadline)
        {
            rebuildPending = false;
            bridge.Skip("Rebuild", $"Gave up after {RebuildGiveUpSeconds:F0} s and {rebuildAttempts} attempts; press Apply, Smoke test or the canvas test to bring them back.");
            return;
        }
        if (objects.LocalPlayer == null)
        {
            if (now >= rebuildNextLog)
            {
                bridge.Info($"Rebuild retry {rebuildAttempts}: LocalPlayer is null, waiting.");
                rebuildNextLog = now.AddSeconds(1);
            }
            return;
        }

        var allOk = true;
        if (bridge.Placed) allOk &= PlaceNow();
        if (bridge.SmokePlaced) allOk &= PlaceSmoke();
        if (bridge.CanvasPlaced) allOk &= PlaceCanvas();
        if (allOk)
        {
            rebuildPending = false;
            bridge.Info($"Rebuild done on attempt {rebuildAttempts}.");
        }
        else if (now >= rebuildNextLog)
        {
            bridge.Info($"Rebuild retry {rebuildAttempts}: a create call returned false, will try again.");
            rebuildNextLog = now.AddSeconds(1);
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        poses.Tick();
        desk.Tick();
        cameraHost.OnFrame((float)framework.UpdateDelta.TotalSeconds);   // part 10
        look.Tick();
        if (rebuildPending)
        {
            TryRebuild();
            return;
        }
        lighting.Tick();
        if (!config.FollowPlayer || !bridge.AnyPlaced) return;
        var local = objects.LocalPlayer;
        if (local == null) return;

        var pos = local.Position;
        var yaw = local.Rotation;
        var turned = MathF.Abs(MathF.IEEERemainder(yaw - lastSentYaw, 2f * MathF.PI));
        if (Vector3.Distance(pos, lastSentPosition) <= MoveThresholdMetres && turned <= TurnThresholdRadians) return;

        lastSentPosition = pos;
        lastSentYaw = yaw;
        if (bridge.Placed) bridge.SetTransform(StagehandBridge.PreviewId, pos, yaw);
        if (bridge.SmokePlaced) bridge.SetTransform(StagehandBridge.SmokeId, pos, yaw);
        if (bridge.CanvasPlaced) bridge.SetTransform(StagehandBridge.CanvasId, pos, yaw);
    }
}

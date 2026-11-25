using Dalamud.Plugin.Services;
using Soundstage.CameraTools;
using Soundstage.GameAccess;
using Soundstage.Overlays;

namespace Soundstage.CameraTools;

/// <summary>
/// Everything Part 10 owns that touches Dalamud: the camera service over the shared game access, the turntable, the thirds overlay and the
/// settings section. The plugin holds one of these and calls <see cref="OnFrame"/> from its framework tick and <see cref="Thirds"/>'s Draw
/// from the UI draw event, so no later part touches those lines again.
/// </summary>
public sealed class CameraHost
{
    private readonly Configuration config;
    private readonly Action save;

    public CameraHost(Configuration config, Action save, IObjectTable objects, IGameGui gameGui, IPluginLog log)
    {
        this.config = config;
        this.save = save;
        GameAccessHost.Use(log);
        Settings = config.Section<CameraSettings>(CameraSettings.Key);
        Service = new CameraService(GameAccessHost.Guard, () => objects.LocalPlayer?.Rotation, line => log.Information("{Line:l}", line), Settings);
        Turntable = new Turntable(Service);
        Overlay = new Thirds(gameGui, () => Settings);
    }

    public CameraSettings Settings { get; }
    public CameraService Service { get; }
    public Turntable Turntable { get; }
    public Thirds Overlay { get; }

    /// <summary>Writes the settings back to the config section and saves.</summary>
    public void Persist()
    {
        config.SetSection(CameraSettings.Key, Settings);
        save();
    }

    /// <summary>Once a frame on the framework thread, with the frame time in seconds.</summary>
    public void OnFrame(float dt)
    {
        try
        {
            Service.OnFrame();
            Turntable.Tick(dt);
        }
        catch (Exception)
        {
            Turntable.Stop();   // nothing here may throw into the framework loop
        }
    }

    /// <summary>A Scene was applied: move the camera if it has a camera block and "Apply camera" is on. Logs a skip in plain words.</summary>
    public void OnSceneApplied(Scene scene)
    {
        if (scene.Camera == null || !Settings.ApplyCamera) return;
        if (!Service.ApplyScene(scene.Camera, out var why))
            Service.LogLine($"scene '{scene.Id}' camera skipped: {why}");
    }

    public void Dispose()
    {
        Turntable.StopAndRestore();
    }
}

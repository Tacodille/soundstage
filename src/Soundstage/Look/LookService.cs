// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Look;

/// <summary>How a Scene's look reaches ReShade.</summary>
public enum LookRoute
{
    /// <summary>Not probed yet.</summary>
    Unknown,

    /// <summary>The add-on answers on its pipe: presets and uniforms are switched for the user.</summary>
    Pipe,

    /// <summary>
    /// No add-on (not copied in, game not restarted, or the proof failed). Soundstage only says which preset to pick and offers a
    /// copy button. It never sends a key press: binding a ReShade shortcut and pressing it is a manual option, see docs/look.md.
    /// </summary>
    Manual,
}

/// <summary>
/// Applies a Scene's look slot through the add-on pipe, remembers the user's own preset so removing the Scene puts it back,
/// and drives auto focus. Every pipe call runs off the caller's thread and in order; nothing here throws at the caller.
/// </summary>
public sealed class LookService : IDisposable
{
    public const string LogPrefix = "[Soundstage/LOOK]";

    private readonly ReShadePipe pipe;
    private readonly Func<LookSettings> getSettings;
    private readonly Action<LookSettings> saveSettings;
    private readonly Action<string> log;
    private readonly Func<DateTime> clock;
    private readonly object gate = new();
    private Task chain = Task.CompletedTask;
    private readonly AutoFocus focus = new();
    private volatile bool focusInFlight;
    private volatile bool sceneWantsFocus;
    private DateTime focusRetryAt = DateTime.MinValue;

    public LookService(ReShadePipe pipe, string gameFolder, Func<LookSettings> getSettings, Action<LookSettings> saveSettings, Action<string> log, Func<DateTime>? clock = null)
    {
        this.pipe = pipe;
        GameFolder = gameFolder;
        this.getSettings = getSettings;
        this.saveSettings = saveSettings;
        this.log = line => log($"{LogPrefix} {line}");
        this.clock = clock ?? (() => DateTime.UtcNow);
    }

    public string GameFolder { get; }

    private LookSettings? cache;

    /// <summary>The settings object, read from the config once and then shared, so the tab and the service see each other's changes. Call <see cref="Commit"/> after changing it.</summary>
    public LookSettings Settings => cache ??= getSettings();

    /// <summary>Writes <see cref="Settings"/> back to the plugin config.</summary>
    public void Commit() => saveSettings(Settings);

    public volatile LookRoute Route = LookRoute.Unknown;

    /// <summary>Why the route is what it is, in a plain sentence for the tab.</summary>
    public volatile string RouteReason = "Not checked yet.";

    /// <summary>The last thing the service did or refused to do, for the tab's status line.</summary>
    public volatile string Status = "";

    public volatile bool StatusOk = true;

    /// <summary>The preset path to pick by hand on the manual route (the last one a Scene asked for).</summary>
    public volatile string ManualPreset = "";

    /// <summary>Session-only: run auto focus without a Scene asking for it, so the Look tab can try it.</summary>
    public volatile bool PreviewFocus;

    /// <summary>Where the camera and head are right now (set by the plugin; null outside GPose or when a read fails). Called on the framework thread.</summary>
    public Func<AutoFocusInput?>? FocusSource { get; set; }

    public int? AddonVersion => pipe.AddonVersion;

    public bool AutoFocusActive => Route == LookRoute.Pipe && (sceneWantsFocus || PreviewFocus) && Settings.FocusUniform.Length > 0;

    /// <summary>True while a Scene has changed the user's preset and not yet put it back.</summary>
    public bool LookApplied => Settings.PreviousPreset.Length > 0;

    public string PreviousPreset => Settings.PreviousPreset;

    // ---- Route ----

    /// <summary>Picks the route from the proof's verdict (stored data) and a live ping. Safe to call again; the plugin calls it once at start.</summary>
    public Task ProbeAsync() => Enqueue(DoProbe);

    private async Task DoProbe()
    {
        var settings = Settings;
        if (string.Equals(settings.Verdict, "fail", StringComparison.OrdinalIgnoreCase))
        {
            SetRoute(LookRoute.Manual, "The add-on proof failed on this machine, so Soundstage tells you which preset to pick instead of switching it.");
            return;
        }
        var reply = await pipe.PingAsync().ConfigureAwait(false);
        if (reply.Ok && pipe.AddonVersion is int v)
        {
            var old = v < ReShadeProtocol.PluginVersion ? $" It is an older add-on (version {v}); auto focus and restoring uniforms need version {ReShadeProtocol.PluginVersion}." : "";
            SetRoute(LookRoute.Pipe, $"The ReShade add-on answered (version {v}).{old}");
        }
        else SetRoute(LookRoute.Manual, reply.Ok ? "Something answered on the pipe but it is not the Soundstage add-on." : reply.Text);
    }

    private void SetRoute(LookRoute route, string reason)
    {
        var changed = Route != route;
        Route = route;
        RouteReason = reason;
        if (changed) log($"route {route}: {reason}");
    }

    // ---- Apply and remove ----

    /// <summary>Applies the Scene's look slot (or restores the user's preset when the Scene has none). Returns at once; the result lands in <see cref="Status"/>.</summary>
    public void ApplyScene(Scene? scene) => _ = Enqueue(() => DoApply(scene));

    /// <summary>Puts the user's own preset back and turns auto focus off. Call when a Scene is removed.</summary>
    public void RemoveLook() => _ = Enqueue(DoRemove);

    public Task ApplyAsync(Scene? scene) => Enqueue(() => DoApply(scene));
    public Task RemoveAsync() => Enqueue(DoRemove);

    private async Task DoApply(Scene? scene)
    {
        var look = scene?.Look;
        var slot = look?.Slot?.Trim() ?? "";
        sceneWantsFocus = look?.AutoFocus == true;
        focus.Reset();
        if (slot.Length == 0)
        {
            // A Scene with no look should not inherit the previous Scene's.
            if (LookApplied) await DoRemove().ConfigureAwait(false);
            else SetStatus(true, "This Scene has no look slot, so the look is unchanged.");
            return;
        }

        var settings = Settings;
        var path = Slots.Resolve(slot, settings);
        if (path == null)
        {
            SetStatus(false, $"The look slot \"{slot}\" is not mapped to a preset yet, so nothing changed. Map it on the Look tab.");
            return;
        }
        ManualPreset = path;

        if (Route != LookRoute.Pipe) await DoProbe().ConfigureAwait(false);
        if (Route != LookRoute.Pipe)
        {
            SetStatus(false, $"No add-on to switch it for you. Pick {path} in ReShade's own preset list (the Look tab has a copy button).");
            return;
        }
        if (!Slots.PresetExists(GameFolder, path))
        {
            SetStatus(false, $"The preset mapped to \"{slot}\" is not in the game folder any more ({path}), so nothing changed. Map the slot again.");
            return;
        }

        // Keep the first preset we replaced, so a run of Scenes still puts back the user's own.
        if (!LookApplied)
        {
            var current = await pipe.SendAsync(ReShadeProtocol.Status()).ConfigureAwait(false);
            if (!current.Ok || !current.Text.StartsWith("preset=", StringComparison.Ordinal) || current.Text.Length <= 7)
            {
                SetStatus(false, "Could not read your current ReShade preset, so Soundstage left the look alone: " + (current.Ok ? "the add-on's reply was not understood." : current.Text));
                return;
            }
            var previous = current.Text[7..];
            settings.PreviousPreset = previous;
            Commit();
            log($"remembered your preset {previous}");
        }

        var reply = await pipe.SendAsync(ReShadeProtocol.Preset(path)).ConfigureAwait(false);
        if (reply.Ok)
        {
            SetStatus(true, $"Look \"{slot}\" applied ({path}).");
            log($"slot {slot} -> {path}");
        }
        else
        {
            SetStatus(false, $"ReShade did not switch to {path}: {reply.Text}");
            log($"slot {slot} -> {path} FAILED: {reply.Text}");
            if (settings.PreviousPreset.Length > 0 && await CurrentIsPreviousAsync(settings.PreviousPreset).ConfigureAwait(false))
            {
                settings.PreviousPreset = "";   // nothing changed, so there is nothing to restore
                Commit();
            }
        }
    }

    private async Task<bool> CurrentIsPreviousAsync(string previous)
    {
        var current = await pipe.SendAsync(ReShadeProtocol.Status()).ConfigureAwait(false);
        return current.Ok && current.Text == "preset=" + previous;
    }

    private async Task DoRemove()
    {
        sceneWantsFocus = false;
        focus.Reset();
        var settings = Settings;
        var previous = settings.PreviousPreset;
        if (previous.Length == 0) { SetStatus(true, "No Scene look to take off."); return; }

        if (Route != LookRoute.Pipe) await DoProbe().ConfigureAwait(false);
        if (Route != LookRoute.Pipe)
        {
            SetStatus(false, $"No add-on, so Soundstage cannot switch back. Your own preset was {previous}.");
            return;
        }

        // Uniform tweaks first (an older add-on says so and we carry on), then the preset.
        var restored = await pipe.SendAsync(ReShadeProtocol.Restore()).ConfigureAwait(false);
        if (!restored.Ok) log($"restore uniforms: {restored.Text}");
        var reply = await pipe.SendAsync(ReShadeProtocol.Preset(previous)).ConfigureAwait(false);
        if (reply.Ok)
        {
            settings.PreviousPreset = "";
            Commit();
            SetStatus(true, $"Your own preset is back ({previous}).");
            log($"restored your preset {previous}");
        }
        else
        {
            // Keep the record, so the Look tab can offer the restore again.
            SetStatus(false, $"Could not put your preset back ({previous}): {reply.Text}");
            log($"restore of {previous} FAILED: {reply.Text}");
        }
    }

    /// <summary>The Look tab's "preview" button: apply a mapped slot without a Scene.</summary>
    public void PreviewSlot(string slot) => ApplyScene(new Scene { Look = new SceneLook { Slot = slot } });

    // ---- Auto focus ----

    /// <summary>The names ReShade's effects expose (from the add-on's "uniforms"), for the Look tab's focus picker. Empty until asked.</summary>
    public volatile string[] Uniforms = Array.Empty<string>();

    public void RefreshUniforms() => _ = Enqueue(async () =>
    {
        var reply = await pipe.SendAsync(ReShadeProtocol.Uniforms()).ConfigureAwait(false);
        if (reply.Ok)
        {
            Uniforms = reply.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            SetStatus(true, $"ReShade lists {Uniforms.Length} settings.");
        }
        else SetStatus(false, "Could not list ReShade's settings: " + reply.Text);
    });

    /// <summary>The Look tab's per-Scene switch for the Scene that is applied now. Off by default for every Scene.</summary>
    public void SetSceneAutoFocus(bool on)
    {
        sceneWantsFocus = on;
        focus.Reset();
    }

    public bool SceneAutoFocus => sceneWantsFocus;

    /// <summary>Called every frame on the framework thread. Cheap when auto focus is off.</summary>
    public void Tick()
    {
        if (focusInFlight || !AutoFocusActive || FocusSource == null || clock() < focusRetryAt) return;
        AutoFocusInput? input;
        try { input = FocusSource(); }
        catch (Exception) { return; }
        if (input is not AutoFocusInput point) return;

        var settings = Settings;
        var now = clock();
        if (focus.Next(point, settings, now) is not AutoFocusUpdate update) return;
        focusInFlight = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var reply = await pipe.SendAsync(ReShadeProtocol.Focus(settings.FocusUniform, update.Value)).ConfigureAwait(false);
                if (reply.Ok) focus.Sent(update, clock());
                else
                {
                    // Say it once, and do not hammer a pipe that is refusing: wait before trying again.
                    focusRetryAt = clock().AddSeconds(2);
                    var message = "Auto focus: " + reply.Text;
                    if (Status != message) SetStatus(false, message);
                }
            }
            catch (ArgumentException ex) { focusRetryAt = clock().AddSeconds(2); SetStatus(false, "Auto focus: " + ex.Message); }
            finally { focusInFlight = false; }
        });
    }

    // ---- Plumbing ----

    private void SetStatus(bool ok, string text)
    {
        StatusOk = ok;
        Status = text;
        if (!ok) log(text);
    }

    private Task Enqueue(Func<Task> work)
    {
        lock (gate)
        {
            chain = chain.ContinueWith(async _ =>
            {
                try { await work().ConfigureAwait(false); }
                catch (Exception ex) { SetStatus(false, "Look error: " + ex.Message); }
            }, TaskScheduler.Default).Unwrap();
            return chain;
        }
    }

    /// <summary>On plugin unload: put the user's preset and uniforms back, then tell the add-on to let go of the pipe. Waits a few seconds at most.</summary>
    public void Dispose()
    {
        sceneWantsFocus = false;
        PreviewFocus = false;
        try
        {
            var done = Enqueue(async () =>
            {
                if (LookApplied && Route == LookRoute.Pipe) await DoRemove().ConfigureAwait(false);
                if (Route == LookRoute.Pipe && (pipe.AddonVersion ?? 0) >= ReShadeProtocol.MinAddonVersion("unload"))
                    await pipe.SendAsync(ReShadeProtocol.Unload()).ConfigureAwait(false);
            });
            done.Wait(TimeSpan.FromSeconds(4));
        }
        catch (Exception) { /* unloading must never throw */ }
    }
}

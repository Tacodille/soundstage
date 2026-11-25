using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Soundstage.Tabs;

namespace Soundstage;

/// <summary>
/// Part 7: what the Scenes tab drives. Undo, the pending snapshot, the user's own Scenes, share codes and the pose and outfit
/// block. It sits beside <see cref="Plugin"/> (which still applies and places Scenes) so the plugin class gains a few hooks
/// and no feature code. All calls are on the framework thread. Every log line starts with <see cref="LogPrefix"/>.
/// </summary>
public sealed class SceneDesk
{
    public const string LogPrefix = "[Soundstage/SCENES]";

    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly UserScenes mine;
    private readonly BrioPosePort posePort;

    public UndoStack Undo { get; } = new();
    public BrioPosePort Poses => posePort;
    public GlamourerBridge Glamourer { get; }
    public PenumbraBridge Penumbra { get; }

    /// <summary>A snapshot taken and not yet saved or discarded. While it is set, applying another Scene asks first.</summary>
    public Scene? PendingSnapshot { get; private set; }

    /// <summary>One line for the tab: the result of the last snapshot, undo, save, copy or import.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The lines the last pose and outfit block produced (empty when the Scene had none).</summary>
    public IReadOnlyList<string> BlockLines { get; private set; } = Array.Empty<string>();

    public SceneDesk(Plugin plugin, IDalamudPluginInterface pi, BrioBridge brio, PoseShelf poses, IPluginLog log, string userScenesPath)
    {
        this.plugin = plugin;
        this.log = log;
        mine = new UserScenes(userScenesPath, s => plugin.Bridge.Info(s));
        Glamourer = new GlamourerBridge(pi, log);
        Penumbra = new PenumbraBridge(pi, log);
        posePort = new BrioPosePort(poses, brio);
        SceneStore.AppliedBlocks.Add("pose");
    }

    private void Say(string line)
    {
        Status = line;
        log.Information("{P} {Line}", LogPrefix, line);
    }

    // ---- Apply, undo and tweaks ----

    /// <summary>Called by <see cref="Plugin.Apply"/> after a Scene was placed: remembers the state and runs the pose block if allowed.</summary>
    public void OnApplied(Scene applied, Scene working, string palette)
    {
        Undo.Push(working, palette, "apply " + applied.Id);
        log.Information("{P} Apply '{Id}' palette '{Pal}' -> undo depth {N}", LogPrefix, applied.Id, palette, Undo.Count);
        ApplyBlock(applied);
    }

    /// <summary>Framework tick: records slider tweaks once they settle.</summary>
    public void Tick()
    {
        var cur = plugin.Current;
        if (cur == null) return;
        if (Undo.Observe(cur, cur.DefaultPalette, DateTime.UtcNow))
            log.Information("{P} Tweak recorded -> undo depth {N}", LogPrefix, Undo.Count);
    }

    public bool DoUndo()
    {
        var state = Undo.Undo(plugin.Current);
        if (state == null) { Say("Nothing to undo."); return false; }
        var ok = plugin.RestoreWorking(state.Scene, state.Palette);
        Say(ok ? $"Undone: back to '{state.Scene.Name}'{(state.Label == "tweak" ? " (a tweak)" : "")}. {Undo.Count - 1} more step(s) back." : "Undo failed: " + plugin.Bridge.LastError);
        log.Information("{P} Undo -> '{Id}' ok={Ok}", LogPrefix, state.Scene.Id, ok);
        return ok;
    }

    public void ClearUndo() => Undo.Clear();

    // ---- Pose and outfit ----

    public bool WantsPoseOutfit(Scene s) => plugin.Config.Section<ScenesTabSettings>(ScenesTabSettings.Key).WantsPoseOutfit(s.Id);

    public void SetWantsPoseOutfit(Scene s, bool on)
    {
        var settings = plugin.Config.Section<ScenesTabSettings>(ScenesTabSettings.Key);
        settings.ApplyPoseOutfitFor.RemoveAll(x => string.Equals(x, s.Id, StringComparison.OrdinalIgnoreCase));
        if (on) settings.ApplyPoseOutfitFor.Add(s.Id);
        plugin.Config.SetSection(ScenesTabSettings.Key, settings);
        plugin.Save();
    }

    private void ApplyBlock(Scene applied)
    {
        var lines = PoseBlockApplier.Apply(applied.Pose, WantsPoseOutfit(applied), posePort, Glamourer, Penumbra);
        BlockLines = lines;
        foreach (var l in lines) log.Information("{P} Pose block of '{Id}': {Line}", LogPrefix, applied.Id, l);
    }

    // ---- Snapshot and user Scenes ----

    /// <summary>Reads what is placed into a pending Scene. False with <see cref="Status"/> set if nothing is applied.</summary>
    public bool TakeSnapshot()
    {
        var cur = plugin.Current;
        if (cur == null) { Say("Nothing applied yet: apply a Scene first."); return false; }
        var stage = plugin.Bridge.PlacedDefinition(StagehandBridge.PreviewId);
        var fromPlaced = stage != null;
        if (stage == null)
        {
            var shown = cur;
            if (!plugin.Config.ShowBackdrop && cur.Backdrop != null) { shown = cur.Clone(); shown.Backdrop = null; }
            stage = SceneConverter.Build(shown, plugin.Config.Backdrop);
        }
        var snap = Snapshot.FromStage(stage, cur, plugin.Config.Backdrop);
        snap.Id = "snapshot";
        snap.Name = "Snapshot of " + cur.Name;
        snap.Category = "Mine";
        snap.LegacyIds = new();
        snap.BakeDefaultPalette();
        PendingSnapshot = snap;
        Say($"Snapshot taken from {(fromPlaced ? "the placed stage" : "the working Scene")}: {snap.Lights.Count} light(s){(snap.Backdrop != null ? " and a backdrop" : "")}. Name it and save it, or discard it.");
        return true;
    }

    public void DiscardSnapshot()
    {
        PendingSnapshot = null;
        Say("Snapshot discarded.");
    }

    /// <summary>Saves the pending snapshot (or, with none pending, a fresh snapshot of the working Scene) to Mine.</summary>
    public Scene? SaveSnapshotAs(string name)
    {
        if (PendingSnapshot == null && !TakeSnapshot()) return null;
        var saved = mine.Save(PendingSnapshot!, name, plugin.Scenes.Select(s => s.Id), out var error);
        if (saved == null) { Say(error ?? "Save failed."); return null; }
        PendingSnapshot = null;
        plugin.ReloadScenes();
        plugin.SelectScene(saved.Id, null);
        Say($"Saved '{saved.Name}' to Mine.");
        return saved;
    }

    public bool RenameMine(Scene s, string newName)
    {
        if (!mine.Rename(s.Id, newName, out var error)) { Say(error ?? "Rename failed."); return false; }
        plugin.ReloadScenes();
        Say($"Renamed to '{newName.Trim()}'.");
        return true;
    }

    public bool DeleteMine(Scene s)
    {
        if (!mine.Delete(s.Id, out var error)) { Say(error ?? "Delete failed."); return false; }
        plugin.ReloadScenes();
        Say($"Deleted '{s.Name}' from Mine.");
        return true;
    }

    // ---- Share codes ----

    /// <summary>The share code for a Scene, in the palette it is shown in. Null with <see cref="Status"/> set if it was refused.</summary>
    public string? MakeCode(Scene s, string? palette)
    {
        var shown = SceneStore.ApplyPalette(s, palette);
        shown.BakeDefaultPalette();
        if (!ShareCode.TryEncode(shown, out var code, out var error)) { Say(error); return null; }
        Say($"Copied a share code for '{s.Name}' ({code.Length:N0} characters).");
        return code;
    }

    public ShareImport ReadCode(string pasted)
    {
        var r = ShareCode.Decode(pasted);
        if (r.Scene == null) Say(r.Error ?? "That code could not be read.");
        else Say($"Read '{r.Scene.Name}'. Check what it holds, then save it.");
        return r;
    }

    /// <summary>Saves an imported Scene to Mine and returns it.</summary>
    public Scene? SaveImport(Scene imported, string name)
    {
        var saved = mine.Save(imported, name, plugin.Scenes.Select(s => s.Id), out var error);
        if (saved == null) { Say(error ?? "Save failed."); return null; }
        plugin.ReloadScenes();
        plugin.SelectScene(saved.Id, null);
        Say($"Saved '{saved.Name}' to Mine.");
        return saved;
    }
}

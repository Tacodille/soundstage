using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;

namespace Soundstage;

/// <summary>
/// Penumbra's IPC, for a Scene's mod settings: enable a mod and pick an option per group in the collection that affects the
/// player. Penumbra.Api 5.19.2 (labels "Penumbra.ApiVersion.V5", "Penumbra.GetCollectionForObject.V5", "Penumbra.TrySetMod.V5",
/// "Penumbra.TrySetModSettings.V5"); docs\integrations.md records the rest. Nothing here throws. Framework thread only.
/// These changes are saved in the collection (the settings calls used here are not temporary), which is why a Scene only
/// reaches this code after the user ticked "Apply pose and outfit" for it.
/// </summary>
public sealed class PenumbraBridge : IModsPort
{
    public const string LogPrefix = "[Soundstage/SCENES]";

    private readonly IPluginLog log;
    private readonly Penumbra.Api.IpcSubscribers.ApiVersion apiVersion;
    private readonly GetCollectionForObject collectionForObject;
    private readonly TrySetMod setMod;
    private readonly TrySetModSettings setSettings;

    public string PluginName => "Penumbra";
    public bool Available { get; private set; }
    public string Version { get; private set; } = "n/a";
    public string? LastError { get; private set; }

    public PenumbraBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        apiVersion = new Penumbra.Api.IpcSubscribers.ApiVersion(pi);
        collectionForObject = new GetCollectionForObject(pi);
        setMod = new TrySetMod(pi);
        setSettings = new TrySetModSettings(pi);
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var v = apiVersion.Invoke();
            Version = $"{v.Breaking}.{v.Features}";
            Available = v.Breaking >= 5;
            LastError = null;
            log.Information("{P} Penumbra ApiVersion -> {V} (available={A})", LogPrefix, Version, Available);
        }
        catch (Exception ex)
        {
            Available = false;
            Version = "n/a";
            LastError = ex.Message;
            log.Information("{P} Penumbra ApiVersion -> not available ({Ex})", LogPrefix, ex.Message);
        }
    }

    public bool TrySet(IReadOnlyList<PenumbraModChoice> mods, out string message)
    {
        message = "";
        if (!Available) Refresh();
        if (!Available) { message = "Penumbra is not running."; return false; }
        try
        {
            var (valid, _, collection) = collectionForObject.Invoke(GlamourerBridge.PlayerObjectIndex);
            if (!valid || collection.Id == Guid.Empty) { message = "Penumbra has no collection for your character."; return false; }

            var done = 0;
            var problems = new List<string>();
            foreach (var m in mods)
            {
                var label = string.IsNullOrWhiteSpace(m.ModName) ? m.ModDirectory : m.ModName;
                var ec = setMod.Invoke(collection.Id, m.ModDirectory, m.Enabled, m.ModName);
                log.Information("{P} Penumbra TrySetMod('{Mod}', enabled={E}) -> {Ec}", LogPrefix, label, m.Enabled, ec);
                if (ec is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged)) { problems.Add($"{label}: {ec}"); continue; }
                foreach (var (group, options) in m.Options)
                {
                    var gec = setSettings.Invoke(collection.Id, m.ModDirectory, group, options, m.ModName);
                    log.Information("{P} Penumbra TrySetModSettings('{Mod}', '{Group}') -> {Ec}", LogPrefix, label, group, gec);
                    if (gec is not (PenumbraApiEc.Success or PenumbraApiEc.NothingChanged)) problems.Add($"{label}, group '{group}': {gec}");
                }
                done++;
            }
            message = $"{done} of {mods.Count} mod(s) set in '{collection.Name}'" + (problems.Count > 0 ? "; " + string.Join("; ", problems) : ".");
            return done > 0;
        }
        catch (Exception ex)
        {
            message = "Penumbra call failed: " + ex.Message;
            LastError = ex.Message;
            log.Information("{P} Penumbra mod settings -> EXCEPTION {Ex}", LogPrefix, ex.Message);
            return false;
        }
    }
}

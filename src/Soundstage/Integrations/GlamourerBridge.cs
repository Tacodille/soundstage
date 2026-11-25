using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;

namespace Soundstage;

/// <summary>
/// Glamourer's IPC, for a Scene's outfit. Glamourer.Api 2.8.2 (labels "Glamourer.ApiVersion.V2", "Glamourer.GetDesignList.V2",
/// "Glamourer.ApplyDesign.V2"); docs\integrations.md records where each comes from. Nothing here throws: a call that fails
/// records <see cref="LastError"/> and returns false. Framework thread only.
/// </summary>
public sealed class GlamourerBridge : IOutfitPort
{
    public const string LogPrefix = "[Soundstage/SCENES]";

    /// <summary>The player's object index. Glamourer addresses an actor by object table index; 0 is the local player.</summary>
    public const int PlayerObjectIndex = 0;

    private readonly IPluginLog log;
    private readonly Glamourer.Api.IpcSubscribers.ApiVersion apiVersion;
    private readonly GetDesignList designList;
    private readonly ApplyDesign applyDesign;

    public string PluginName => "Glamourer";
    public bool Available { get; private set; }
    public string Version { get; private set; } = "n/a";
    public string? LastError { get; private set; }

    public GlamourerBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        apiVersion = new Glamourer.Api.IpcSubscribers.ApiVersion(pi);
        designList = new GetDesignList(pi);
        applyDesign = new ApplyDesign(pi);
        Refresh();
    }

    public void Refresh()
    {
        try
        {
            var v = apiVersion.Invoke();
            Version = $"{v.Major}.{v.Minor}";
            Available = v.Major >= 1;
            LastError = null;
            log.Information("{P} Glamourer ApiVersion -> {V} (available={A})", LogPrefix, Version, Available);
        }
        catch (Exception ex)
        {
            Available = false;
            Version = "n/a";
            LastError = ex.Message;
            log.Information("{P} Glamourer ApiVersion -> not available ({Ex})", LogPrefix, ex.Message);
        }
    }

    /// <summary>Design id to name, or null when Glamourer cannot be asked.</summary>
    public Dictionary<Guid, string>? Designs()
    {
        try { return designList.Invoke(); }
        catch (Exception ex) { LastError = ex.Message; log.Information("{P} Glamourer GetDesignList -> EXCEPTION {Ex}", LogPrefix, ex.Message); return null; }
    }

    public bool TryApplyDesign(string designName, out string message)
    {
        message = "";
        if (!Available) Refresh();
        if (!Available) { message = "Glamourer is not running."; return false; }
        var all = Designs();
        if (all == null) { message = "Glamourer did not answer: " + LastError; return false; }
        var match = all.Where(d => string.Equals(d.Value, designName, StringComparison.OrdinalIgnoreCase)).Select(d => (Guid?)d.Key).FirstOrDefault();
        if (match == null) { message = $"Glamourer has no design called '{designName}'."; return false; }
        try
        {
            var ec = applyDesign.Invoke(match.Value, PlayerObjectIndex, 0u, ApplyFlagEx.DesignDefault);
            log.Information("{P} Glamourer ApplyDesign('{Name}') -> {Ec}", LogPrefix, designName, ec);
            if (ec != GlamourerApiEc.Success) { message = $"Glamourer said {ec}."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            message = "Glamourer call failed: " + ex.Message;
            LastError = ex.Message;
            log.Information("{P} Glamourer ApplyDesign -> EXCEPTION {Ex}", LogPrefix, ex.Message);
            return false;
        }
    }
}

using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Soundstage;

/// <summary>
/// Everything that touches Brio's IPC lives here, and nothing in here throws: each call catches, records the result
/// and the exception message, logs it, and returns a failure value. Every log line starts with <see cref="LogPrefix"/>.
/// The labels and signatures are Brio.API 3.0.1 (Brio v0.8.0.12); docs\brio-notes.md cites the source for each.
/// All calls must be made on the framework thread (the ImGui draw is on it).
/// </summary>
public sealed class BrioBridge
{
    public const string LogPrefix = "[Soundstage/BRIO]";

    /// <summary>Brio.MajorAPIVersion in Brio v0.8.0.12 (Brio.cs line 49). The ".V3" in every label is this number.</summary>
    public const int ExpectedMajor = 3;

    private readonly IPluginLog log;
    private readonly Brio.API.ApiVersion apiVersion;
    private readonly Brio.API.LoadPoseFromFile loadFile;
    private readonly Brio.API.LoadPoseFromJson loadJson;
    private readonly Brio.API.GetPoseAsJson getJson;

    public bool Available { get; private set; }
    public string Version { get; private set; } = "n/a";
    public string? LastError { get; private set; }

    public BrioBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        // These only look the call gates up; they do not need Brio to be loaded yet.
        apiVersion = new Brio.API.ApiVersion(pi);
        loadFile = new Brio.API.LoadPoseFromFile(pi);
        loadJson = new Brio.API.LoadPoseFromJson(pi);
        getJson = new Brio.API.GetPoseAsJson(pi);
        Refresh();
    }

    /// <summary>Asks Brio for its API version. Brio off, not loaded, or IPC switched off in its settings all land here as "not available".</summary>
    public void Refresh()
    {
        try
        {
            var v = apiVersion.Invoke();
            Version = $"{v.Breaking}.{v.Feature}";
            Available = v.Breaking == ExpectedMajor;
            LastError = Available ? null : $"Brio's API is version {Version}, Soundstage speaks {ExpectedMajor}.x.";
            log.Information("{P} ApiVersion -> {V} (available={A})", LogPrefix, Version, Available);
        }
        catch (Exception ex)
        {
            Available = false;
            Version = "n/a";
            LastError = ex.Message;
            log.Information("{P} ApiVersion -> EXCEPTION {Ex}", LogPrefix, ex.Message);
        }
    }

    /// <summary>Brio.LoadPoseFromFile.V3(actor, path). Brio's own default import (rotation only, no weapons). Handles .pose and .cmp by extension.</summary>
    public bool LoadFromFile(IGameObject actor, string path)
    {
        try
        {
            var ok = loadFile.Invoke(actor, path);
            log.Information("{P} LoadPoseFromFile(actor={A}, path='{Path}') -> {R}", LogPrefix, Describe(actor), path, ok);
            LastError = ok ? null : "Brio returned false (not in GPose, or the actor is not one Brio is posing).";
            return ok;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Information("{P} LoadPoseFromFile(path='{Path}') -> EXCEPTION {Ex}", LogPrefix, path, ex.Message);
            return false;
        }
    }

    /// <summary>Brio.LoadPoseFromJson.V3(actor, isLegacyCMToolPose, json). Brio's IPC import options: every transform component, every bone in the JSON.</summary>
    public bool LoadFromJson(IGameObject actor, string json, string what, bool legacyCm = false)
    {
        try
        {
            var ok = loadJson.Invoke(actor, json, legacyCm);
            log.Information("{P} LoadPoseFromJson(actor={A}, {W}, {N} chars, legacyCm={L}) -> {R}", LogPrefix, Describe(actor), what, json.Length, legacyCm, ok);
            LastError = ok ? null : "Brio returned false (not in GPose, or the actor is not one Brio is posing).";
            return ok;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Information("{P} LoadPoseFromJson({W}) -> EXCEPTION {Ex}", LogPrefix, what, ex.Message);
            return false;
        }
    }

    /// <summary>Brio.GetPoseAsJson.V3(actor): the actor's current pose as a Brio .pose JSON string, or null.</summary>
    public string? GetPoseJson(IGameObject actor)
    {
        try
        {
            var json = getJson.Invoke(actor);
            log.Information("{P} GetPoseAsJson(actor={A}) -> {R}", LogPrefix, Describe(actor), json == null ? "null" : json.Length + " chars");
            if (json == null) LastError = "Brio could not read the current pose.";
            return json;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Information("{P} GetPoseAsJson -> EXCEPTION {Ex}", LogPrefix, ex.Message);
            return null;
        }
    }

    private static string Describe(IGameObject o)
    {
        try { return $"{o.Name.TextValue}#{o.ObjectIndex}"; }
        catch { return "?"; }
    }
}

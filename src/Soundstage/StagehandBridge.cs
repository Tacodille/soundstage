using System.Globalization;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Stagehand.Api;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage;

/// <summary>
/// Everything that touches Stagehand lives here, and nothing in here throws: each call catches, records the
/// result and the exception message in the public properties, logs it, and returns false.
/// Every log line starts with <see cref="LogPrefix"/> so it can be grepped out of dalamud.log.
/// </summary>
public sealed class StagehandBridge : IDisposable
{
    public const string LogPrefix = "[Soundstage/SH]";
    public const string PreviewId = "Soundstage.Preset";
    public const string SmokeId = "Soundstage.Smoke";
    public const string CanvasId = "Soundstage.Canvas";

    // Assumed FFXIV convention (to check in game): a character with rotation 0 faces +Z, and its left is +X.
    // Player rotation is a yaw in radians about Y, so facing = (sin r, 0, cos r), which is what
    // Quaternion.CreateFromYawPitchRoll(r, 0, 0) does to +Z. The stage is placed with that quaternion, so inside the
    // stage +Z is the player's forward and +X is the player's left.

    private readonly IStagehandApi? api;
    private readonly IPluginLog log;

    // The stage transform and definition last sent successfully, per stage id, so the on-screen markers are
    // computed from exactly what the lights were given. Guarded by sentLock (sends and draws may be on different threads).
    private readonly object sentLock = new();
    private readonly Dictionary<string, SentStage> sent = new();

    private sealed record SentStage(StageDefinition Definition, Vector3 Position, Quaternion Rotation, float Scale);

    /// <summary>Part 7.2: the definition last sent successfully for a stage id (what is placed now), or null. Read-only: do not change it.</summary>
    public StageDefinition? PlacedDefinition(string id)
    {
        lock (sentLock) return sent.TryGetValue(id, out var s) ? s.Definition : null;
    }

    /// <summary>One light to draw: world position, beam direction (null for a point light), colour and label.</summary>
    public readonly record struct LightMarker(string Label, Vector3 WorldPosition, Vector3? WorldBeamDirection, Vector3 Colour, float Intensity, float FloorY);

    /// <summary>The one place the stage rotation is made. Send, SetTransform and the markers all use it.</summary>
    public static Quaternion StageRotation(float yawRadians) => Quaternion.CreateFromYawPitchRoll(yawRadians, 0f, 0f);

    /// <summary>
    /// World-space markers for every placed light, using Stagehand's ApplyParentTransform maths (notes, section 2):
    /// position = Transform(local, stageRotation) * stageScale + stageTranslation, and the light's world rotation is
    /// stageRotation * localRotation, where localRotation = CreateFromYawPitchRoll(yaw=Y, pitch=X, roll=Z) in radians.
    /// A spot shines along its local +Z.
    /// </summary>
    public List<LightMarker> GetMarkers()
    {
        var result = new List<LightMarker>();
        lock (sentLock)
        {
            foreach (var st in sent.Values)
            {
                foreach (var obj in st.Definition.Objects.Values)
                {
                    if (obj is not LightDefinition l) continue;
                    var world = Vector3.Transform(l.Position, st.Rotation) * st.Scale + st.Position;
                    Vector3? dir = null;
                    if (l.Shape == LightShape.Spot || l.Shape == LightShape.Flat)
                    {
                        const float deg = MathF.PI / 180f;
                        var r = l.RotationPitchYawRollDegrees;
                        var local = Quaternion.CreateFromYawPitchRoll(r.Y * deg, r.X * deg, r.Z * deg);
                        dir = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, st.Rotation * local));
                    }
                    result.Add(new LightMarker(l.DisplayName ?? "Light", world, dir, l.Color, l.Intensity, st.Position.Y));
                }
            }
        }
        return result;
    }

    public StagehandApiAvailability Availability { get; private set; } = StagehandApiAvailability.StagehandMissing;
    public string Revision { get; private set; } = "n/a";
    public string LastCall { get; private set; } = "(none yet)";
    public bool? LastResult { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>The preview stage is wanted: true from a successful place until Remove. Survives a location change.</summary>
    public bool Placed { get; private set; }

    /// <summary>The smoke-test stage is wanted, as above.</summary>
    public bool SmokePlaced { get; private set; }

    /// <summary>The canvas-test stage is wanted, as above.</summary>
    public bool CanvasPlaced { get; private set; }

    public bool AnyPlaced => Placed || SmokePlaced || CanvasPlaced;

    /// <summary>Raised on Stagehand's LocationChanged, after it destroys every temporary live stage.</summary>
    public event Action? LocationChanged;

    public StagehandBridge(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.log = log;
        try
        {
            api = StagehandApi.CreateIpcClient(pi);
            log.Information("{P} CreateIpcClient -> ok", LogPrefix);
            api.LocationChanged += HandleLocationChanged;
            Refresh();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            api = null;
            log.Information("{P} CreateIpcClient -> EXCEPTION {Ex}", LogPrefix, ex.ToString());
        }
    }

    public bool Available => api != null && Availability == StagehandApiAvailability.Available;

    /// <summary>Re-asks Stagehand whether it is there. Cheap enough to call from a button.</summary>
    public void Refresh()
    {
        if (api == null)
        {
            Skip("CheckApiAvailability", "Stagehand client was not created (see the CreateIpcClient line in the log).");
            return;
        }
        try
        {
            Availability = api.CheckApiAvailability();
            Revision = Availability == StagehandApiAvailability.StagehandMissing ? "n/a" : api.GetPluginApiRevision().ToString();
            LastError = null;
            log.Information("{P} CheckApiAvailability -> {A}, revision {R}", LogPrefix, Availability, Revision);
        }
        catch (Exception ex)
        {
            Availability = StagehandApiAvailability.StagehandMissing;
            Revision = "n/a";
            LastError = ex.Message;
            log.Information("{P} CheckApiAvailability -> EXCEPTION {Ex}", LogPrefix, ex.ToString());
        }
    }

    /// <summary>
    /// A hand-made working light, from Stagehand's autosave: a red point light at the editor's point defaults.
    /// It sits 1.5 m in front of the stage origin at 1.5 m height (stage placed at the player's position and yaw).
    /// </summary>
    public static StageDefinition BuildSmokeStage()
    {
        var light = new LightDefinition
        {
            DisplayName = "Smoke",
            Shape = LightShape.Point,
            Color = new Vector3(1f, 0f, 0f),
            Intensity = 4f,
            Range = 15f,
            FalloffFunction = LightFalloffFunction.Quadratic,
            FalloffFactor = 1f,
            EnableSpecularHighlights = true,
            EnableDynamicShadows = true,
            EnableCharacterShadows = true,
            EnableObjectShadows = true,
            ShadowPlaneNear = 0.25f,
            ShadowPlaneFar = 100f,
            CharacterShadowRange = 50f,
            Position = new Vector3(0f, 1.5f, 1.5f),
        };
        var stage = new StageDefinition();
        stage.Info = new StageInfo { Name = "Soundstage smoke test", AuthorName = "Tacodile" };
        stage.Objects["smoke"] = light;
        return stage;
    }

    /// <summary>
    /// Creates or updates the preset stage (stage id <see cref="PreviewId"/>). In Stagehand 0.5.5 this makes the stage live
    /// straight away, so there is no separate SetVisible call (notes, section 1).
    /// </summary>
    /// <param name="faceLocal">The face in stage-local space for lights flagged aimAt "face" (part 8). Null places every light as authored.</param>
    /// <param name="quiet">True for the animation and face-aim re-sends, which run up to 15 times a second: only failures are logged.</param>
    public bool PlacePreview(Scene preset, BackdropAdjust backdrop, Vector3 position, float yawRadians, Vector3? faceLocal = null, bool quiet = false)
    {
        var stage = SceneConverter.Build(preset, backdrop, faceLocal);
        if (preset.Backdrop != null && !quiet) log.Information("{P} Backdrop values: preset='{Id}' color={C} presetScale={S} distanceBehind={D}; live adjust: {A}",
            LogPrefix, preset.Id, FmtArr(preset.Backdrop.Color), FmtArr(preset.Backdrop.Scale), preset.Backdrop.DistanceBehind, backdrop);
        var ok = Send(PreviewId, stage, position, yawRadians, "Soundstage preset", quiet);
        if (ok) Placed = true;
        return ok;
    }

    /// <summary>The picture canvas test: the portrait prop with its diffuse texture replaced by an embedded modpack.</summary>
    public bool PlaceCanvas(byte[] diffuseTex, float yawOffsetDeg, float heightOffset, Vector3 position, float yawRadians)
    {
        var ok = Send(CanvasId, SceneConverter.BuildCanvasTest(diffuseTex, yawOffsetDeg, heightOffset), position, yawRadians, "Soundstage canvas test");
        if (ok) CanvasPlaced = true;
        return ok;
    }

    public bool PlaceSmoke(Vector3 position, float yawRadians)
    {
        var ok = Send(SmokeId, BuildSmokeStage(), position, yawRadians, "Soundstage smoke test");
        if (ok) SmokePlaced = true;
        return ok;
    }

    private bool Send(string id, StageDefinition stage, Vector3 position, float yawRadians, string debugName, bool quiet = false)
    {
        const string call = "CreateOrUpdate+Transform";
        if (api == null) return Skip(call, $"Stagehand client not created, so '{id}' was not sent.");
        var summary = quiet ? "(quiet)" : Summarise(stage);
        try
        {
            var def = stage.ToDefinitionString();
            var rot = StageRotation(yawRadians);
            var ok = api.TryCreateOrUpdateTemporaryStageWithTransform(def, id, position, rot, 1f, debugName);
            if (ok) lock (sentLock) sent[id] = new SentStage(stage, position, rot, 1f);
            if (!quiet || !ok)
                log.Information("{P} TryCreateOrUpdateTemporaryStageWithTransform id={Id} pos={Pos} yaw={Yaw:F3}rad defLen={Len} {Summary} -> {Ok}",
                    LogPrefix, id, Fmt(position), yawRadians, def.Length, summary, ok);
            Record(call + " " + id, ok);
            if (!ok) LastError = "Stagehand returned false (definition string rejected or stage not stored).";
            return ok;
        }
        catch (Exception ex)
        {
            log.Information("{P} TryCreateOrUpdateTemporaryStageWithTransform id={Id} pos={Pos} yaw={Yaw:F3}rad {Summary} -> EXCEPTION {Ex}",
                LogPrefix, id, Fmt(position), yawRadians, summary, ex.ToString());
            return Fail(call + " " + id, ex.Message);
        }
    }

    public bool SetTransform(string id, Vector3 position, float yawRadians)
    {
        const string call = "SetTransform";
        if (api == null) return Skip(call, "Stagehand client not created.");
        try
        {
            var rot = StageRotation(yawRadians);
            var ok = api.TrySetTemporaryStageTransform(id, position, rot, 1f);
            if (ok) lock (sentLock) { if (sent.TryGetValue(id, out var prev)) sent[id] = prev with { Position = position, Rotation = rot }; }
            log.Information("{P} TrySetTemporaryStageTransform id={Id} pos={Pos} yaw={Yaw:F3}rad -> {Ok}",
                LogPrefix, id, Fmt(position), yawRadians, ok);
            Record(call + " " + id, ok);
            return ok;
        }
        catch (Exception ex)
        {
            log.Information("{P} TrySetTemporaryStageTransform id={Id} -> EXCEPTION {Ex}", LogPrefix, id, ex.ToString());
            return Fail(call + " " + id, ex.Message);
        }
    }

    public bool RemovePreview()
    {
        Placed = false;
        return Destroy(PreviewId);
    }

    public bool RemoveSmoke()
    {
        SmokePlaced = false;
        return Destroy(SmokeId);
    }

    public bool RemoveCanvas()
    {
        CanvasPlaced = false;
        return Destroy(CanvasId);
    }

    private bool Destroy(string id)
    {
        const string call = "Destroy";
        if (api == null) return Skip(call, $"Stagehand client not created, so '{id}' was not destroyed.");
        try
        {
            lock (sentLock) sent.Remove(id);
            var ok = api.TryDestroyTemporaryStage(id);
            log.Information("{P} TryDestroyTemporaryStage id={Id} -> {Ok}", LogPrefix, id, ok);
            Record(call + " " + id, ok);
            return ok;
        }
        catch (Exception ex)
        {
            log.Information("{P} TryDestroyTemporaryStage id={Id} -> EXCEPTION {Ex}", LogPrefix, id, ex.ToString());
            return Fail(call + " " + id, ex.Message);
        }
    }

    /// <summary>Records and logs an early return: the reason goes to the log and to the window's status line.</summary>
    public bool Skip(string call, string reason)
    {
        LastCall = call;
        LastResult = false;
        LastError = reason;
        log.Information("{P} {Call} skipped: {Reason}", LogPrefix, call, reason);
        return false;
    }

    /// <summary>Called by the plugin for its own log lines that belong in the same greppable stream.</summary>
    public void Info(string message) => log.Information("{P} {M}", LogPrefix, message);

    private void HandleLocationChanged(StageLocation location)
    {
        try
        {
            log.Information("{P} LocationChanged received: {Loc} (Stagehand has destroyed live temporary stages; preset wanted={Pw}, smoke wanted={Sw}, canvas wanted={Cw})",
                LogPrefix, location, Placed, SmokePlaced, CanvasPlaced);
            LocationChanged?.Invoke();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            log.Information("{P} LocationChanged handler -> EXCEPTION {Ex}", LogPrefix, ex.ToString());
        }
    }

    private static string Summarise(StageDefinition stage)
    {
        var parts = new List<string>();
        foreach (var kv in stage.Objects)
        {
            if (kv.Value is LightDefinition l)
                parts.Add($"{kv.Key}:{l.Shape} I={l.Intensity.ToString("F2", CultureInfo.InvariantCulture)} R={l.Range.ToString("F1", CultureInfo.InvariantCulture)} col={Fmt(l.Color)}");
            else if (kv.Value is BgObjectDefinition bg)
                parts.Add($"{kv.Key}:Bg {bg.ModelGamePath} pos={Fmt(bg.Position)} rotPYR={Fmt(bg.RotationPitchYawRollDegrees)} scale={Fmt(bg.Scale)} dye=({bg.DyeColor.X:F2},{bg.DyeColor.Y:F2},{bg.DyeColor.Z:F2},{bg.DyeColor.W:F2}) modpack='{bg.ModpackId}'");
            else
                parts.Add($"{kv.Key}:{kv.Value.GetType().Name}");
        }
        var packs = stage.EmbeddedModpacks.Count == 0 ? "" : " modpacks=[" + string.Join("; ", stage.EmbeddedModpacks.Select(m =>
            m.Key + ": " + string.Join(", ", m.Value.ModdedResources.Select(r => r.Key + " (" + r.Value.GetType().Name + ")")))) + "]";
        return $"objects={stage.Objects.Count} [{string.Join("; ", parts)}]{packs}";
    }

    private static string FmtArr(float[] a) => "[" + string.Join(",", a.Select(x => x.ToString("F2", CultureInfo.InvariantCulture))) + "]";

    private static string Fmt(Vector3 v) =>
        string.Create(CultureInfo.InvariantCulture, $"({v.X:F2},{v.Y:F2},{v.Z:F2})");

    private void Record(string call, bool ok)
    {
        LastCall = call;
        LastResult = ok;
        LastError = null;
    }

    private bool Fail(string call, string message)
    {
        LastCall = call;
        LastResult = false;
        LastError = message;
        return false;
    }

    public void Dispose()
    {
        try { if (api != null) api.LocationChanged -= HandleLocationChanged; } catch { /* fail soft */ }
        if (Placed) RemovePreview();
        if (SmokePlaced) RemoveSmoke();
        if (CanvasPlaced) RemoveCanvas();
    }
}

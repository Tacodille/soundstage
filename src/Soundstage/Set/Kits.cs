// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stagehand.Definitions.Objects;

namespace Soundstage;

/// <summary>
/// A prop kit: a handful of housing furniture models arranged around an anchor. Kits are data (kits/*.json beside the
/// dll), so a new one is a file and the model paths never sit in code. Coordinates are metres in the stage frame
/// (+Z forward, +X the subject's left). Object offsets are in the kit's own frame, which the anchor and the placement
/// turn and move as one piece.
/// </summary>
public sealed class Kit
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>"untested" until it has been seen in game, then "ok" or "dropped". Shown in the Set tab.</summary>
    public string Status { get; set; } = "untested";

    /// <summary>Where the kit stands by default. Placement overrides are added on top.</summary>
    public KitPlacement Anchor { get; set; } = new();

    public List<KitObject> Objects { get; set; } = new();
}

public sealed class KitObject
{
    public string Name { get; set; } = "";

    /// <summary>The game path of a housing furniture model, lower case with forward slashes, ending in .mdl.</summary>
    public string Model { get; set; } = "";

    /// <summary>x, y, z in the kit's frame.</summary>
    public float[] Offset { get; set; } = { 0f, 0f, 0f };

    /// <summary>Turn about the vertical axis, degrees, in the kit's frame. 0 leaves the model facing +Z.</summary>
    public float YawDeg { get; set; }

    /// <summary>Uniform scale.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Optional dye colour (r, g, b, each 0 to 1). Only dyeable models respond.</summary>
    public float[]? Dye { get; set; }
}

public static class KitStore
{
    /// <summary>Stage size is not measured, so this cap is the guard (plan 9.3).</summary>
    public const int MaxObjects = 12;

    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex ModelPattern = new(@"^[a-z0-9_/]+\.mdl$", RegexOptions.Compiled);

    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Null when the kit is fine, otherwise one sentence saying what is wrong with it.</summary>
    public static string? Validate(Kit kit)
    {
        if (kit.FormatVersion != 1) return $"formatVersion {kit.FormatVersion} is not 1.";
        if (!IdPattern.IsMatch(kit.Id ?? "")) return "id must be lower-case letters, digits and dashes.";
        if (string.IsNullOrWhiteSpace(kit.Name)) return "name is empty.";
        if (kit.Objects == null || kit.Objects.Count == 0) return "a kit needs at least one object.";
        if (kit.Objects.Count > MaxObjects) return $"{kit.Objects.Count} objects; a kit holds at most {MaxObjects}.";
        if (kit.Anchor == null || !Finite(kit.Anchor.X, kit.Anchor.Z, kit.Anchor.YawDeg)) return "anchor has a number that is not finite.";
        for (var i = 0; i < kit.Objects.Count; i++)
        {
            var o = kit.Objects[i];
            if (!ModelPattern.IsMatch(o.Model ?? "")) return $"object {i + 1}: model path '{o.Model}' must be lower case with forward slashes and end in .mdl.";
            if (o.Offset == null || o.Offset.Length != 3 || !Finite(o.Offset) || o.Offset.Any(v => MathF.Abs(v) > 15f)) return $"object {i + 1}: offset needs three numbers within 15 m.";
            if (!Finite(o.YawDeg)) return $"object {i + 1}: yawDeg is not a number.";
            if (!Finite(o.Scale) || o.Scale < 0.1f || o.Scale > 5f) return $"object {i + 1}: scale must be between 0.1 and 5.";
            if (o.Dye != null && (o.Dye.Length < 3 || !Finite(o.Dye) || o.Dye.Any(v => v < 0f || v > 1f))) return $"object {i + 1}: dye needs r, g, b between 0 and 1.";
        }
        return null;
    }

    private static bool Finite(params float[] v) => v.All(float.IsFinite);

    public static Kit? Parse(string json, Action<string> log, string label)
    {
        try
        {
            var kit = JsonSerializer.Deserialize<Kit>(json, Options);
            if (kit == null) { log($"kit {label}: empty file, skipped."); return null; }
            var why = Validate(kit);
            if (why != null) { log($"kit {label}: {why} Skipped."); return null; }
            return kit;
        }
        catch (Exception ex) { log($"kit {label}: could not read it ({ex.Message}). Skipped."); return null; }
    }

    /// <summary>Every valid kit in a folder, by id. A bad file is skipped with one log line; a repeated id keeps the first.</summary>
    public static List<Kit> LoadDirectory(string dir, Action<string> log)
    {
        var kits = new List<Kit>();
        if (!Directory.Exists(dir)) { log($"kits: folder {dir} not found, no kits."); return kits; }
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var kit = Parse(File.ReadAllText(file), log, Path.GetFileName(file));
            if (kit == null) continue;
            if (kits.Any(k => k.Id == kit.Id)) { log($"kit {Path.GetFileName(file)}: id '{kit.Id}' is already loaded, skipped."); continue; }
            kits.Add(kit);
        }
        return kits;
    }
}

public static class KitBuilder
{
    private const float Deg = MathF.PI / 180f;

    /// <summary>Rotates an (x, z) offset by a yaw, using the stage's convention that a positive yaw turns +Z towards +X.</summary>
    public static Vector3 Rotate(Vector3 v, float yawDeg)
    {
        var a = yawDeg * Deg;
        var s = MathF.Sin(a);
        var c = MathF.Cos(a);
        return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
    }

    /// <summary>One placed object: its stage id and the finished game object.</summary>
    public readonly record struct Placed(string Id, BgObjectDefinition Object);

    public static IReadOnlyList<Placed> Build(Kit kit, KitPlacement? placement)
    {
        placement ??= new KitPlacement();
        var yaw = kit.Anchor.YawDeg + placement.YawDeg;
        var origin = new Vector3(kit.Anchor.X + placement.X, 0f, kit.Anchor.Z + placement.Z);
        var list = new List<Placed>();
        for (var i = 0; i < kit.Objects.Count; i++)
        {
            var o = kit.Objects[i];
            var offset = Rotate(new Vector3(o.Offset[0], o.Offset[1], o.Offset[2]), yaw);
            var def = new BgObjectDefinition
            {
                DisplayName = string.IsNullOrWhiteSpace(o.Name) ? kit.Name : o.Name,
                ModelGamePath = o.Model,
                Position = origin + offset,
                RotationPitchYawRollDegrees = new Vector3(0f, o.YawDeg + yaw, 0f),
                Scale = new Vector3(o.Scale),
                Opacity = 1f,
                DyeColor = o.Dye is { Length: >= 3 } d ? new Vector4(d[0], d[1], d[2], d.Length > 3 ? d[3] : 1f) : Vector4.One,
            };
            list.Add(new Placed($"kit-{kit.Id}-{i}", def));
        }
        return list;
    }
}

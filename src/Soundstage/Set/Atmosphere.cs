// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Stagehand.Definitions.Objects;

namespace Soundstage;

/// <summary>One thing the Set tab can switch on: a game VFX or a game sound, with a path, offset and scale. Data in atmosphere.json.</summary>
public sealed class AtmosphereItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>"vfx" (an .avfx effect) or "sound" (an .scd file).</summary>
    public string Kind { get; set; } = "vfx";

    public string Path { get; set; } = "";

    /// <summary>x, y, z from the subject's feet, stage frame. Ignored for a sound that is not positional.</summary>
    public float[] Offset { get; set; } = { 0f, 0f, 0f };

    /// <summary>Uniform scale of a visual effect.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>For a sound: which sound in the file, how loud (1 is the file's own level), and whether it comes from a place.</summary>
    public int SoundIndex { get; set; }
    public float Volume { get; set; } = 1f;
    public bool Positional { get; set; }

    /// <summary>"untested" until it has been seen or heard in game, then "ok" or "dropped".</summary>
    public string Status { get; set; } = "untested";

    public string Note { get; set; } = "";
}

public sealed class AtmosphereFile
{
    public int FormatVersion { get; set; } = 1;
    public List<AtmosphereItem> Items { get; set; } = new();
}

public static class AtmosphereStore
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex VfxPattern = new(@"^vfx/[a-z0-9_/]+\.avfx$", RegexOptions.Compiled);
    private static readonly Regex SoundPattern = new(@"^(music|sound)/[a-z0-9_/]+\.scd$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? Validate(AtmosphereItem i)
    {
        if (!IdPattern.IsMatch(i.Id ?? "")) return "id must be lower-case letters, digits and dashes.";
        if (string.IsNullOrWhiteSpace(i.Name)) return "name is empty.";
        if (i.Kind == "vfx")
        {
            if (!VfxPattern.IsMatch(i.Path ?? "")) return $"vfx path '{i.Path}' must be lower case, start with vfx/ and end in .avfx.";
            if (!float.IsFinite(i.Scale) || i.Scale < 0.1f || i.Scale > 10f) return "scale must be between 0.1 and 10.";
        }
        else if (i.Kind == "sound")
        {
            if (!SoundPattern.IsMatch(i.Path ?? "")) return $"sound path '{i.Path}' must start with music/ or sound/ and end in .scd.";
            if (i.SoundIndex < 0 || i.SoundIndex > 255) return "soundIndex must be 0 to 255.";
            if (!float.IsFinite(i.Volume) || i.Volume < 0f || i.Volume > 2f) return "volume must be between 0 and 2.";
        }
        else return $"kind '{i.Kind}' is not vfx or sound.";
        if (i.Offset == null || i.Offset.Length != 3 || i.Offset.Any(v => !float.IsFinite(v) || MathF.Abs(v) > 15f)) return "offset needs three numbers within 15 m.";
        return null;
    }

    public static List<AtmosphereItem> Load(string path, Action<string> log)
    {
        var items = new List<AtmosphereItem>();
        if (!File.Exists(path)) { log($"atmosphere: {path} not found, no atmosphere items."); return items; }
        try
        {
            var file = JsonSerializer.Deserialize<AtmosphereFile>(File.ReadAllText(path), KitStore.Options);
            if (file == null || file.FormatVersion != 1) { log("atmosphere: formatVersion is not 1, no items."); return items; }
            foreach (var item in file.Items)
            {
                var why = Validate(item);
                if (why != null) { log($"atmosphere item '{item.Id}': {why} Skipped."); continue; }
                if (items.Any(x => x.Id == item.Id)) { log($"atmosphere item '{item.Id}': repeated id, skipped."); continue; }
                items.Add(item);
            }
        }
        catch (Exception ex) { log($"atmosphere: could not read {path} ({ex.Message})."); }
        return items;
    }
}

public static class AtmosphereBuilder
{
    /// <summary>
    /// The choices that are really on for a Scene: its explicit list, plus the old haze, dust and sun flags where the list
    /// does not already name that item. Order is kept, so the output is the same each time.
    /// </summary>
    public static List<AtmosphereChoice> Effective(SceneAtmosphere a)
    {
        var list = a.Items.Where(c => c.Enabled).ToList();
        bool Named(string id) => a.Items.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (a.Haze is "light" or "heavy" && !Named("haze")) list.Add(new AtmosphereChoice { Id = "haze", Amount = a.Haze == "heavy" ? 1.8f : 1f });
        if (a.Dust && !Named("dust")) list.Add(new AtmosphereChoice { Id = "dust" });
        if (a.Sun && !Named("sun")) list.Add(new AtmosphereChoice { Id = "sun" });
        return list;
    }

    public readonly record struct Placed(string Id, ObjectDefinition Object);

    /// <summary>Game objects for the Scene's atmosphere. A choice naming an id the catalogue lacks is skipped and listed in <paramref name="missing"/>.</summary>
    public static IReadOnlyList<Placed> Build(SceneAtmosphere a, IReadOnlyList<AtmosphereItem> catalogue, List<string>? missing = null)
    {
        var list = new List<Placed>();
        foreach (var choice in Effective(a))
        {
            var item = catalogue.FirstOrDefault(i => string.Equals(i.Id, choice.Id, StringComparison.OrdinalIgnoreCase));
            if (item == null) { missing?.Add(choice.Id); continue; }
            var amount = float.IsFinite(choice.Amount) ? Math.Clamp(choice.Amount, 0.1f, 3f) : 1f;
            var pos = new Vector3(item.Offset[0], item.Offset[1], item.Offset[2]);
            if (item.Kind == "vfx")
            {
                list.Add(new Placed("atmos-" + item.Id, new VfxObjectDefinition
                {
                    DisplayName = item.Name,
                    VfxGamePath = item.Path,
                    Position = pos,
                    Scale = new Vector3(item.Scale * amount),
                }));
            }
            else
            {
                list.Add(new Placed("atmos-" + item.Id, new SoundObjectDefinition
                {
                    DisplayName = item.Name,
                    SoundGamePath = item.Path,
                    SoundIndex = item.SoundIndex,
                    Volume = item.Volume * amount,
                    IsPositional = item.Positional,
                    Position = pos,
                }));
            }
        }
        return list;
    }
}

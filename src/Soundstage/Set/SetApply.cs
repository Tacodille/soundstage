// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;
using System.Runtime.CompilerServices;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage;

/// <summary>
/// The kits and atmosphere items this build knows about. The plugin loads them once at start (one line in the Plugin
/// constructor); the converter reads them each time it builds a stage. Offline checks pass their own through the
/// overloads and never touch this.
/// </summary>
public static class SetCatalogue
{
    public const string LogPrefix = "[Soundstage/SET]";

    /// <summary>Where [Soundstage/SET] lines go. Null in the offline checks.</summary>
    public static Action<string>? Logger { get; private set; }

    public static IReadOnlyList<Kit> Kits { get; private set; } = Array.Empty<Kit>();
    public static IReadOnlyList<AtmosphereItem> Atmosphere { get; private set; } = Array.Empty<AtmosphereItem>();

    /// <summary>Loads kits/*.json and atmosphere.json from the folder the dll sits in.</summary>
    public static void Load(string pluginDirectory, Action<string> log)
    {
        Logger = log;
        SetBlocks.Register();   // idempotent; the module initializer already ran, this is the belt to its braces
        Kits = KitStore.LoadDirectory(Path.Combine(pluginDirectory, "kits"), log);
        Atmosphere = AtmosphereStore.Load(Path.Combine(pluginDirectory, "atmosphere.json"), log);
        log($"set: {Kits.Count} kit(s), {Atmosphere.Count} atmosphere item(s) loaded.");
    }

    public static Kit? FindKit(string id) => Kits.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Tells the Scene store that this build acts on the set and atmosphere blocks. Runs when the assembly loads, so no shared file changes.</summary>
internal static class SetBlocks
{
#pragma warning disable CA2255 // intended: this registers a feature with a shared store without editing the store's file
    [ModuleInitializer]
    internal static void Register()
    {
        SceneStore.AppliedBlocks.Add("set");
        SceneStore.AppliedBlocks.Add("atmosphere");
    }
#pragma warning restore CA2255
}

/// <summary>Adds a Scene's set and atmosphere (sweep, kits, picture, effects, sound) to the stage definition the converter is building.</summary>
public static class SetApply
{
    /// <summary>Where the sweep's wall stands when neither the Set block nor the Scene's old backdrop says.</summary>
    public const float DefaultSweepDistance = 2.5f;

    /// <summary>What the last build did, for the log and the Set tab. Read on the same thread that built.</summary>
    public static string LastSummary { get; private set; } = "";

    // The last picture converted, so a re-send after a zone change does not decode the image again.
    private static (string Path, long Ticks, long Length, byte[] Tex, int W, int H)? pictureCache;

    /// <summary>The stage-wide sweep colour: palette role first, then the fixed colour, then the old panel's colour, then white.</summary>
    public static Vector4 SweepColour(Scene p)
    {
        var set = p.Set;
        if (set != null && !string.IsNullOrWhiteSpace(set.ColorRole)
            && p.Palettes.TryGetValue(p.EffectiveDefaultPalette, out var pal))
        {
            foreach (var role in new[] { set.ColorRole, "backdrop", "bg" })
                if (pal.TryGetValue(role, out var c) && c.Length >= 3 && c.All(float.IsFinite)) return new Vector4(c[0], c[1], c[2], 1f);
        }
        if (set?.Color is { Length: >= 3 } fixedColour && fixedColour.All(float.IsFinite)) return new Vector4(fixedColour[0], fixedColour[1], fixedColour[2], 1f);
        if (p.Backdrop?.Color is { Length: >= 3 } old) return new Vector4(old[0], old[1], old[2], 1f);
        return Vector4.One;
    }

    public static SweepSpec SweepSpecFor(Scene p)
    {
        var set = p.Set ?? new SceneSet();
        var distance = set.SweepDistance > 0f ? set.SweepDistance : p.Backdrop?.DistanceBehind ?? DefaultSweepDistance;
        return new SweepSpec(set.SweepWidth, distance, set.SweepCove);
    }

    /// <summary>Uses the loaded catalogue and the user's picture file if there is one.</summary>
    public static void AddTo(StageDefinition stage, Scene p) =>
        AddTo(stage, p, SetCatalogue.Kits, SetCatalogue.Atmosphere, LoadPictureFromDisk);

    /// <summary>The testable form: catalogue and picture loader passed in. <paramref name="loadPicture"/> maps a path to (tex, width, height), or null.</summary>
    public static void AddTo(StageDefinition stage, Scene p, IReadOnlyList<Kit> kits, IReadOnlyList<AtmosphereItem> atmosphere,
        Func<string, (byte[] Tex, int W, int H)?> loadPicture)
    {
        var notes = new List<string>();

        if (p.Set is { } set)
        {
            if (set.Kind == "sweep")
            {
                var colour = SweepColour(p);
                var pieces = SweepBuilder.Layout(SweepSpecFor(p));
                foreach (var piece in pieces)
                {
                    stage.Objects[piece.Id] = new BgObjectDefinition
                    {
                        DisplayName = "Sweep " + piece.Part,
                        ModelGamePath = SceneConverter.BackdropModel,
                        DyeColor = colour,
                        Opacity = 1f,
                        Position = piece.Position,
                        RotationPitchYawRollDegrees = piece.PitchYawRollDeg,
                        Scale = piece.Scale,
                    };
                }
                notes.Add($"sweep {pieces.Count} piece(s)");
            }

            foreach (var id in set.Props.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var kit = kits.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));
                if (kit == null) { notes.Add($"kit '{id}' not found"); continue; }
                set.KitPlacements.TryGetValue(kit.Id, out var placement);
                var placed = KitBuilder.Build(kit, placement);
                foreach (var o in placed) stage.Objects[o.Id] = o.Object;
                notes.Add($"kit {kit.Id} {placed.Count} object(s)");
            }

            if (set.Picture is { } pic && !string.IsNullOrWhiteSpace(pic.Path))
            {
                var loaded = loadPicture(pic.Path);
                if (loaded is { } l)
                {
                    var (obj, pack) = PictureProp.Build(pic, l.Tex, l.W, l.H);
                    stage.Objects["picture"] = obj;
                    stage.EmbeddedModpacks[PictureProp.ModpackId] = pack;
                    notes.Add($"picture {l.W}x{l.H}");
                }
                else notes.Add("picture file could not be read");
            }
        }

        if (p.Atmosphere is { } atmos)
        {
            var missing = new List<string>();
            var placed = AtmosphereBuilder.Build(atmos, atmosphere, missing);
            foreach (var a in placed) stage.Objects[a.Id] = a.Object;
            if (placed.Count > 0) notes.Add($"atmosphere {placed.Count} item(s)");
            if (missing.Count > 0) notes.Add($"atmosphere item(s) not in the catalogue: {string.Join(", ", missing)}");
        }

        var summary = notes.Count == 0 ? "" : string.Join("; ", notes);
        if (summary != LastSummary) SetCatalogue.Logger?.Invoke($"scene '{p.Id}' built: {(summary.Length == 0 ? "no set or atmosphere" : summary)}");
        LastSummary = summary;
    }

    private static (byte[] Tex, int W, int H)? LoadPictureFromDisk(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return null;
            if (pictureCache is { } c && c.Path == path && c.Ticks == info.LastWriteTimeUtc.Ticks && c.Length == info.Length)
                return (c.Tex, c.W, c.H);
            var (tex, w, h) = PictureProp.FromFile(path);
            pictureCache = (path, info.LastWriteTimeUtc.Ticks, info.Length, tex, w, h);
            return (tex, w, h);
        }
        catch { return null; }
    }
}

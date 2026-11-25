// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Text.Json;
using System.Text.Json.Serialization;
using Stagehand.Definitions.Objects;

namespace Soundstage;

// ---- Loading and saving ----------------------------------------------------------------------------------------

public static class SceneStore
{
    /// <summary>Raised from 4 (Keylight's own number) to 6, for rigs with extras and animated lights. The six-light test in game decides if it stays.</summary>
    public const int MaxLights = 6;

    public const int FormatVersion = 2;

    /// <summary>
    /// Optional blocks this build applies. Empty in part 4: a Scene's set, atmosphere, camera, look, pose and animation
    /// are carried and written back but nothing acts on them. Each later part adds its block name here when it lands.
    /// </summary>
    public static readonly HashSet<string> AppliedBlocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "camera",   // part 10
        "look",   // part 11
    };

    public static readonly string[] BlockNames = { "set", "atmosphere", "camera", "look", "pose", "animation" };

    /// <summary>Role names given to the lights of a v1 preset, which has none, by light order.</summary>
    private static readonly string[] LegacyRoles = { "key", "fill", "rim", "kicker", "kicker2", "kicker3" };

    /// <summary>The rule that sets every range (measured 18/11/2026): brightest channel x intensity / (falloff factor x range^n) = this.</summary>
    public const double GameCutoff = 0.0196;

    public static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static readonly JsonSerializerOptions WriteOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> KnownProperties = new(
        typeof(Scene).GetProperties().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

    // ---- Brightness rule ----

    /// <summary>The range the game's own rule gives this light, to 0.1 m.</summary>
    public static float GameRange(SceneLight l)
    {
        var n = l.Falloff.Trim().ToLowerInvariant() switch { "linear" => 1, "cubic" => 3, _ => 2 };
        var max = Math.Max(l.Color[0], Math.Max(l.Color[1], l.Color[2]));
        var strength = max * l.Intensity / (l.FalloffFactor * GameCutoff);
        return (float)Math.Round(Math.Pow(strength, 1.0 / n) + 0.05, 1);
    }

    // ---- Reading ----

    /// <summary>
    /// Loads one scene file (v2 "scenes" or the old v1 "presets"). A file that cannot be read, or has neither array,
    /// yields an empty list and a logged reason; a bad Scene inside a good file is skipped with its own reason.
    /// </summary>
    public static List<Scene> LoadFile(string path, Action<string> log)
    {
        var result = new List<Scene>();
        if (!File.Exists(path)) { log($"scenes: {path} does not exist, skipped."); return result; }
        try { return Parse(File.ReadAllText(path), path, log); }
        catch (Exception ex) { log($"scenes: could not read {path}: {ex.Message}"); return result; }
    }

    public static List<Scene> Parse(string json, string source, Action<string> log)
    {
        var result = new List<Scene>();
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (Exception ex) { log($"scenes: {source} is not valid JSON: {ex.Message}"); return result; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) { log($"scenes: {source} root is not an object."); return result; }
            JsonElement scenes = default, presets = default;
            var haveScenes = false; var havePresets = false;
            foreach (var p in root.EnumerateObject())
            {
                if (string.Equals(p.Name, "scenes", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) { scenes = p.Value; haveScenes = true; }
                else if (string.Equals(p.Name, "presets", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Array) { presets = p.Value; havePresets = true; }
                else if (string.Equals(p.Name, "formatVersion", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var v) && v > FormatVersion)
                    log($"scenes: {source} has formatVersion {v}, newer than this build understands ({FormatVersion}); reading it anyway.");
            }
            if (!haveScenes && !havePresets) { log($"scenes: {source} has no \"scenes\" or \"presets\" array."); return result; }
            var arr = haveScenes ? scenes : presets;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unknownLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var index = 0;
            foreach (var el in arr.EnumerateArray())
            {
                index++;
                Scene? scene;
                try { scene = el.Deserialize<Scene>(ReadOptions); }
                catch (Exception ex) { log($"scenes: {source} scene #{index} skipped, cannot parse: {ex.Message}"); continue; }
                if (scene == null) { log($"scenes: {source} scene #{index} skipped, it is null."); continue; }

                // A property this build has never heard of is ignored, with one log line per name per file.
                if (el.ValueKind == JsonValueKind.Object)
                    foreach (var prop in el.EnumerateObject())
                        if (!KnownProperties.Contains(prop.Name) && unknownLogged.Add(prop.Name))
                            log($"scenes: {source} has an unknown block \"{prop.Name}\" (first seen in scene #{index}); ignored.");

                Normalise(scene);
                var why = Validate(scene);
                if (why != null) { log($"scenes: {source} scene #{index} ('{scene.Id}') skipped: {why}"); continue; }
                if (!seen.Add(scene.Id)) { log($"scenes: {source} scene #{index} ('{scene.Id}') skipped: duplicate id."); continue; }
                result.Add(scene);
            }
        }
        return result;
    }

    /// <summary>
    /// Fills what an older file lacks: a role for every light (by order: key, fill, rim, then kicker) and one default
    /// palette holding the lights' own colours. A v1 preset therefore loads as a v2 Scene with a single palette.
    /// </summary>
    public static void Normalise(Scene s)
    {
        s.Lights ??= new();
        s.Palettes ??= new();
        s.LegacyIds ??= new();
        s.Tags ??= new();
        if (s.Lights.Any(l => string.IsNullOrWhiteSpace(l.Role)))
        {
            for (var i = 0; i < s.Lights.Count; i++)
                if (string.IsNullOrWhiteSpace(s.Lights[i].Role))
                    s.Lights[i].Role = i < LegacyRoles.Length ? LegacyRoles[i] : "extra" + (i + 1);
        }
        if (s.Backdrop?.Light != null && string.IsNullOrWhiteSpace(s.Backdrop.Light.Role)) s.Backdrop.Light.Role = "bg";
        if (s.Palettes.Count == 0 && s.Lights.All(l => l.Color != null && l.Color.Length >= 3) && s.Lights.Select(l => l.Role).Distinct().Count() == s.Lights.Count)
            s.BakeDefaultPalette();
    }

    /// <summary>Returns a reason the Scene cannot be used, or null if it is fine.</summary>
    public static string? Validate(Scene p)
    {
        if (string.IsNullOrWhiteSpace(p.Id)) return "no id.";
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = p.Id;
        if (string.IsNullOrWhiteSpace(p.Category)) p.Category = "Other";
        if (p.Lights == null || p.Lights.Count == 0) return "no lights.";
        if (p.Lights.Count > MaxLights) return $"{p.Lights.Count} lights, the limit is {MaxLights}.";
        for (var i = 0; i < p.Lights.Count; i++)
        {
            var why = ValidateLight(p.Lights[i]);
            if (why != null) return $"light {i + 1}: {why}";
        }
        var roles = p.Lights.Select(l => l.Role).ToList();
        if (p.Backdrop?.Light != null) roles.Add(p.Backdrop.Light.Role);
        if (roles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != roles.Count) return "two lights share a role.";
        if (p.Backdrop != null)
        {
            if (p.Backdrop.Color == null || p.Backdrop.Color.Length < 3) return "backdrop colour needs 3 numbers.";
            if (p.Backdrop.Scale == null || p.Backdrop.Scale.Length < 3) return "backdrop scale needs 3 numbers.";
            if (p.Backdrop.Light != null)
            {
                var why = ValidateLight(p.Backdrop.Light);
                if (why != null) return $"backdrop light: {why}";
            }
        }
        foreach (var (pname, map) in p.Palettes)
        {
            if (map == null) return $"palette '{pname}' is empty.";
            foreach (var (role, c) in map)
                if (c == null || c.Length < 3) return $"palette '{pname}', role '{role}': colour needs 3 numbers.";
        }
        return null;
    }

    private static string? ValidateLight(SceneLight l)
    {
        if (!TryShape(l.Shape, out _)) return $"unknown shape '{l.Shape}' (Point, Spot or Flat).";
        if (!TryFalloff(l.Falloff, out _)) return $"unknown falloff '{l.Falloff}' (Linear, Quadratic or Cubic).";
        if (l.Color == null || l.Color.Length < 3) return "colour needs 3 numbers.";
        return null;
    }

    public static bool TryShape(string? s, out LightShape shape)
    {
        shape = LightShape.Point;
        if (!Enum.TryParse(s?.Trim(), true, out shape)) return false;
        return shape is LightShape.Point or LightShape.Spot or LightShape.Flat;   // no Ambient: outdoors only
    }

    public static bool TryFalloff(string? s, out LightFalloffFunction f) => Enum.TryParse(s?.Trim(), true, out f) && Enum.IsDefined(f);

    // ---- Aliases and palettes ----

    /// <summary>
    /// Finds a Scene by id, or by one of its legacy ids (then <paramref name="palette"/> is the palette that old id selected,
    /// or null for the default). Returns null when nothing matches.
    /// </summary>
    public static Scene? Resolve(IEnumerable<Scene> all, string? id, out string? palette)
    {
        palette = null;
        if (string.IsNullOrWhiteSpace(id)) return null;
        var list = all as IList<Scene> ?? all.ToList();
        var direct = list.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        if (direct != null) return direct;
        foreach (var s in list)
        {
            foreach (var (old, pal) in s.LegacyIds)
            {
                if (!string.Equals(old, id, StringComparison.OrdinalIgnoreCase)) continue;
                palette = string.IsNullOrWhiteSpace(pal) ? null : pal;
                return s;
            }
        }
        return null;
    }

    /// <summary>
    /// A copy of the Scene wearing the named palette (null or unknown means the default). A role the palette lists takes
    /// its colour; any other role keeps the light's own. When the palette is not the default, the range of every light that
    /// changed colour is recomputed by the brightness rule, because the brightest channel sets the range.
    /// </summary>
    public static Scene ApplyPalette(Scene scene, string? paletteName)
    {
        var copy = scene.Clone();
        var def = copy.EffectiveDefaultPalette;
        var name = string.IsNullOrWhiteSpace(paletteName) || !copy.Palettes.ContainsKey(paletteName) ? def : paletteName;
        if (name.Length == 0 || !copy.Palettes.TryGetValue(name, out var map)) return copy;
        var recompute = !string.Equals(name, def, StringComparison.Ordinal);

        void Paint(SceneLight l)
        {
            if (!map.TryGetValue(l.Role, out var c)) return;
            l.Color = new[] { c[0], c[1], c[2] };
            if (recompute) l.Range = GameRange(l);
        }
        foreach (var l in copy.Lights) Paint(l);
        if (copy.Backdrop != null)
        {
            if (map.TryGetValue("backdrop", out var bc)) copy.Backdrop.Color = new[] { bc[0], bc[1], bc[2] };
            if (copy.Backdrop.Light != null) Paint(copy.Backdrop.Light);
        }
        copy.DefaultPalette = name;
        return copy;
    }

    // ---- Loading everything ----

    /// <summary>
    /// Shipped first, then the user's Scenes (category forced to "Mine"). A user id that clashes gets "user-" in front.
    /// The user's Scenes come from <paramref name="userPath"/> (user-scenes.json). If that does not exist yet and the old
    /// <paramref name="legacyUserPath"/> (user-presets.json) does, the old file is read, converted and written to
    /// user-scenes.json once. The old file is never changed or deleted.
    /// </summary>
    public static List<Scene> LoadAll(string shippedPath, string userPath, string? legacyUserPath, Action<string> log, IEnumerable<Scene>? builtIn = null)
    {
        var all = new List<Scene>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in builtIn ?? Array.Empty<Scene>()) if (ids.Add(p.Id)) all.Add(p);
        foreach (var p in LoadFile(shippedPath, log))
        {
            all.RemoveAll(x => x.Category == "Built-in" && string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase));   // a shipped Scene replaces a built-in one
            if (ids.Add(p.Id) || all.All(x => !string.Equals(x.Id, p.Id, StringComparison.OrdinalIgnoreCase))) all.Add(p);
        }

        List<Scene> mine;
        if (File.Exists(userPath)) mine = LoadFile(userPath, log);
        else if (!string.IsNullOrEmpty(legacyUserPath) && File.Exists(legacyUserPath))
        {
            mine = LoadFile(legacyUserPath, log);
            foreach (var m in mine) m.Category = "Mine";
            try
            {
                SaveUser(userPath, mine);
                log($"scenes: carried {mine.Count} of your old presets over to {userPath}. {legacyUserPath} is untouched.");
            }
            catch (Exception ex) { log($"scenes: could not write {userPath} ({ex.Message}); your old presets still load from {legacyUserPath}."); }
        }
        else mine = new();

        foreach (var p in mine)
        {
            p.Category = "Mine";
            if (!ids.Add(p.Id)) { p.Id = "user-" + p.Id; ids.Add(p.Id); }
            all.Add(p);
        }
        var applied = AppliedBlocks.Count == 0 ? "none" : string.Join(", ", AppliedBlocks.OrderBy(x => x));
        var carried = BlockNames.Where(b => all.Any(s => s.BlocksPresent().Contains(b)) && !AppliedBlocks.Contains(b)).ToList();
        log($"scenes: loaded {all.Count} ({all.Count(x => x.Category == "Mine")} of them in Mine).");
        if (carried.Count > 0) log($"scenes: this build does not apply the {string.Join(", ", carried)} block(s) yet (applied: {applied}); they are kept and ignored.");
        return all;
    }

    public static void SaveUser(string path, IEnumerable<Scene> userScenes)
    {
        var file = new SceneFile { FormatVersion = FormatVersion, Scenes = userScenes.ToList() };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, WriteOptions));
        File.Move(tmp, path, true);
    }

    /// <summary>Lower-case, dashes for anything else. "My look!" becomes "my-look".</summary>
    public static string Slug(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) && ch < 128 ? ch : '-');
        var s = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-+", "-").Trim('-');
        return s.Length == 0 ? "scene" : s;
    }

    /// <summary>The old two-light preview, kept so the plugin works with no scenes.json beside the dll.</summary>
    public static Scene BuiltInWarmKeyCoolFill()
    {
        var s = new Scene
        {
            Id = "builtin-warm-key-cool-fill",
            Name = "Warm key + cool fill",
            Category = "Built-in",
            Description = "A warm spot from front-left and a soft cool point light from front-right. The original preview preset.",
            BestIn = "Anywhere. A plain starting point.",
            Lights =
            {
                new SceneLight { Name = "Key", Shape = "Spot", Color = new[] { 1f, 0.95f, 0.84f }, Intensity = 4f, Range = 15f, Falloff = "Quadratic",
                    ConeDeg = 30f, AngularFalloffDeg = 5f, AzimuthDeg = 45f, Distance = 1.6f, Height = 1.8f, AimHeight = 1.6f,
                    CharacterShadows = true, ObjectShadows = true, Specular = true },
                new SceneLight { Name = "Fill", Shape = "Point", Color = new[] { 0.75f, 0.85f, 1f }, Intensity = 2f, Range = 12f, Falloff = "Quadratic",
                    AzimuthDeg = -45f, Distance = 1.697f, Height = 1.4f, CharacterShadows = false, ObjectShadows = false, Specular = true },
            },
        };
        Normalise(s);
        return s;
    }
}

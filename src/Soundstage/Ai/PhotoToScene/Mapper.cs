// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Ai.PhotoToScene;

/// <summary>
/// The tuning tables. Edit the numbers here and rebuild to change how words become degrees. Azimuth follows the Scene
/// format (0 front, +90 the subject's left, -90 the subject's right, 180 behind), but the words are the photo's own: a light
/// on the left of the picture is on the subject's right when the subject faces the camera, so "left" is negative.
/// </summary>
public static class PhotoTables
{
    public static readonly Dictionary<string, float> AzimuthDeg = new(StringComparer.OrdinalIgnoreCase)
    {
        ["front"] = 0f,
        ["front-left"] = -45f,
        ["left"] = -90f,
        ["back-left"] = -135f,
        ["back"] = 180f,
        ["back-right"] = 135f,
        ["right"] = 90f,
        ["front-right"] = 45f,
    };

    /// <summary>Degrees above (positive) or below (negative) the eye line, seen from the face.</summary>
    public static readonly Dictionary<string, float> ElevationDeg = new(StringComparer.OrdinalIgnoreCase)
    {
        ["above"] = 35f,
        ["level"] = 5f,
        ["below"] = -25f,
    };

    /// <summary>Spot cone and edge falloff, in degrees. A narrow cone with a small falloff is a hard edge.</summary>
    public static readonly Dictionary<string, (float ConeDeg, float FalloffDeg)> Softness = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hard"] = (25f, 5f),
        ["medium"] = (45f, 12f),
        ["soft"] = (70f, 25f),
    };

    /// <summary>Where a rim light goes when the photo shows one and the Scene has none, opposite the key's side.</summary>
    public const float RimAzimuthDeg = 160f;
    public const float RimHeight = 2.2f;
    public const float RimDistance = 1.8f;
    public const float RimIntensity = 6f;

    public static readonly string[] RimRoles = { "rim", "rim2", "kicker" };
}

/// <summary>What the mapper made: a normal Scene, or why not.</summary>
public sealed record MapResult(Scene? Scene, string? Error, IReadOnlyList<string> Notes, string BaseSceneId);

/// <summary>Turns an analysis into a Scene in plain code. The model never writes a Scene; it only chooses words, a catalogue id and colours.</summary>
public static class PhotoMapper
{
    public const string PhotoPalette = "Photo";

    /// <summary>The Scene the photo is closest to: the named id, else the best rig and mood match, else the first Scene with a key light.</summary>
    public static Scene? PickBase(PhotoAnalysis a, IReadOnlyList<Scene> catalogue, out string how)
    {
        if (a.ClosestScene.Length > 0)
        {
            var named = SceneStore.Resolve(catalogue, a.ClosestScene, out _);
            if (named != null) { how = "the Scene the model named"; return named; }
        }
        var pool = catalogue.Where(s => SceneStore.Validate(s.Clone()) == null).ToList();
        if (pool.Count == 0) { how = "none"; return null; }
        int Score(Scene s)
        {
            var score = a.Mood.Count(m => s.Tags.Contains(m, StringComparer.OrdinalIgnoreCase)) * 2;
            if (a.Rig.Length > 0 && string.Equals(s.Rig, a.Rig, StringComparison.OrdinalIgnoreCase)) score += 5;
            if (a.KeySoftness is "soft" or "hard" && s.Tags.Contains(a.KeySoftness, StringComparer.OrdinalIgnoreCase)) score += 1;
            if (s.Lights.Any(l => l.Role == "key")) score += 1;
            return score;
        }
        how = "the best match by rig and mood";
        return pool.OrderByDescending(Score).ThenBy(s => s.Id, StringComparer.Ordinal).First();
    }

    public static MapResult Map(PhotoAnalysis a, IReadOnlyList<Scene> catalogue)
    {
        var notes = new List<string>(a.Notes);
        var basis = PickBase(a, catalogue, out var how);
        if (basis == null) return new MapResult(null, "There are no Scenes to start from.", notes, "");
        notes.Add($"Started from {basis.Name}, {how}.");

        var scene = basis.Clone();
        scene.Id = "from-photo";
        scene.Name = "From photo (" + basis.Name + ")";
        scene.Category = "Mine";
        scene.LegacyIds = new();
        scene.Description = "Built from a photo: key from the " + a.KeyDirection + ", " + a.KeyElevation + ", " + a.KeySoftness + ". Starts from " + basis.Name + ".";
        scene.Tags = scene.Tags.Concat(a.Mood).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var key = FindKey(scene);
        if (key == null) notes.Add("The Scene has no key light to aim, so only colours were changed.");
        else AimKey(key, a);

        foreach (var l in scene.Lights)
        {
            var c = l.Role switch
            {
                "key" or "key2" => a.KeyColour,
                "fill" => a.FillColour,
                _ when PhotoTables.RimRoles.Contains(l.Role) => a.RimColour,
                _ => null,
            };
            if (c != null) Paint(l, c);
        }

        ApplyRim(scene, a, key, notes);
        ApplyBackdrop(scene, a);

        scene.Palettes = new() { [PhotoPalette] = new() };
        scene.DefaultPalette = PhotoPalette;
        scene.BakeDefaultPalette();

        var error = SceneStore.Validate(scene);
        if (error != null) return new MapResult(null, "The Scene made from the photo was not valid: " + error, notes, basis.Id);
        return new MapResult(scene, null, notes, basis.Id);
    }

    private static SceneLight? FindKey(Scene s) =>
        s.Lights.FirstOrDefault(l => l.Role == "key")
        ?? s.Lights.FirstOrDefault(l => l.Role is not ("fill" or "bg") && !PhotoTables.RimRoles.Contains(l.Role))
        ?? s.Lights.FirstOrDefault();

    private static void AimKey(SceneLight key, PhotoAnalysis a)
    {
        if (PhotoTables.AzimuthDeg.TryGetValue(a.KeyDirection, out var az)) key.AzimuthDeg = az;
        if (PhotoTables.ElevationDeg.TryGetValue(a.KeyElevation, out var el))
        {
            var aim = key.AimHeight ?? AimSolver.DefaultHeadHeight;
            key.Height = MathF.Round(aim + MathF.Sin(el * MathF.PI / 180f) * key.Distance, 2);
        }
        if (PhotoTables.Softness.TryGetValue(a.KeySoftness, out var soft))
        {
            key.ConeDeg = soft.ConeDeg;
            key.AngularFalloffDeg = soft.FalloffDeg;
        }
    }

    private static void Paint(SceneLight l, float[] c)
    {
        l.Color = new[] { c[0], c[1], c[2] };
        l.Range = SceneStore.GameRange(l);
    }

    private static void ApplyRim(Scene scene, PhotoAnalysis a, SceneLight? key, List<string> notes)
    {
        if (a.HasRim == null) return;
        var rims = scene.Lights.Where(l => PhotoTables.RimRoles.Contains(l.Role)).ToList();
        if (a.HasRim == false)
        {
            if (rims.Count > 0 && rims.Count < scene.Lights.Count)
            {
                foreach (var r in rims) scene.Lights.Remove(r);
                notes.Add("The photo shows no rim light, so the Scene's rim light was taken out.");
            }
            return;
        }
        if (rims.Count > 0 || scene.Lights.Count >= SceneStore.MaxLights) return;
        var side = key != null && key.AzimuthDeg < 0 ? 1f : -1f;
        var rim = new SceneLight
        {
            Name = "Rim",
            Role = "rim",
            Shape = "Spot",
            Intensity = PhotoTables.RimIntensity,
            ConeDeg = 40f,
            AngularFalloffDeg = 15f,
            AzimuthDeg = side * PhotoTables.RimAzimuthDeg,
            Distance = PhotoTables.RimDistance,
            Height = PhotoTables.RimHeight,
            AimHeight = 1.6f,
            AimAt = AimSolver.Face,
            CharacterShadows = false,
            ObjectShadows = false,
            Specular = false,
            Color = a.RimColour ?? new[] { 1f, 1f, 1f },
        };
        rim.Range = SceneStore.GameRange(rim);
        scene.Lights.Add(rim);
        notes.Add("The photo shows a rim light and the Scene had none, so one was added behind.");
    }

    private static void ApplyBackdrop(Scene scene, PhotoAnalysis a)
    {
        var c = a.BackdropColour;
        if (c == null) return;
        if (scene.Set != null && scene.Set.Kind != "none")
        {
            scene.Set.ColorRole = "";
            scene.Set.Color = new[] { c[0], c[1], c[2] };
        }
        if (scene.Backdrop == null)
        {
            if (scene.Set == null || scene.Set.Kind == "none")
                scene.Backdrop = new SceneBackdrop { Color = new[] { c[0], c[1], c[2] }, DistanceBehind = 2f, Scale = new[] { 4f, 3f, 1f } };
            return;
        }
        scene.Backdrop.Color = new[] { c[0], c[1], c[2] };
        if (scene.Backdrop.Light != null) Paint(scene.Backdrop.Light, new[] { c[0], c[1], c[2] });
    }
}

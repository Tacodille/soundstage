// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage;

/// <summary>
/// Part 7.2: reads what is placed back into a Scene. The placed stage is a <see cref="StageDefinition"/> in stage-local
/// coordinates (the plugin moves the whole stage to the player), so a light's position is turned back into azimuth,
/// distance and height with the inverse of <see cref="SceneConverter.LightPosition"/>, and a spot's aim back into an aim height.
/// What a stage cannot hold (roles, palettes, tags, the optional blocks) comes from the Scene it was built from.
/// </summary>
public static class Snapshot
{
    /// <summary>Round-trip tolerance from the plan: 1 cm on positions, 0.5 degrees on angles.</summary>
    public const float PositionToleranceMetres = 0.01f;
    public const float AngleToleranceDeg = 0.5f;

    private const float Deg = MathF.PI / 180f;
    private static readonly string[] SpareRoles = { "key", "fill", "rim", "kicker", "kicker2", "kicker3" };

    /// <summary>The inverse of <see cref="SceneConverter.LightPosition"/>: (azimuth degrees, distance, height).</summary>
    public static (float AzimuthDeg, float Distance, float Height) InvertLightPosition(Vector3 p) =>
        (MathF.Atan2(p.X, p.Z) / Deg, MathF.Sqrt(p.X * p.X + p.Z * p.Z), p.Y);

    /// <summary>The inverse of the aim in <see cref="SceneConverter.AimPitchYawRoll"/> for a light aimed at (0, y, 0): the height it looks at.</summary>
    public static float InvertAimHeight(Vector3 position, float pitchDeg)
    {
        var horizontal = MathF.Sqrt(position.X * position.X + position.Z * position.Z);
        return position.Y - horizontal * MathF.Tan(pitchDeg * Deg);
    }

    /// <summary>One light read back out of its definition. <paramref name="basis"/> supplies the name and role the definition cannot hold.</summary>
    public static SceneLight ReadLight(LightDefinition def, SceneLight? basis, string fallbackRole)
    {
        var (az, dist, height) = InvertLightPosition(def.Position);
        var l = new SceneLight
        {
            Name = !string.IsNullOrWhiteSpace(basis?.Name) ? basis!.Name : def.DisplayName,
            Role = !string.IsNullOrWhiteSpace(basis?.Role) ? basis!.Role : fallbackRole,
            Shape = def.Shape.ToString(),
            Color = new[] { def.Color.X, def.Color.Y, def.Color.Z },
            Intensity = def.Intensity,
            Range = def.Range,
            Falloff = def.FalloffFunction.ToString(),
            FalloffFactor = def.FalloffFactor,
            ConeDeg = def.SpotLightAngleDegrees,
            AngularFalloffDeg = def.AngularFalloffDegrees,
            AzimuthDeg = az,
            Distance = dist,
            Height = height,
            CharacterShadows = def.EnableCharacterShadows,
            ObjectShadows = def.EnableObjectShadows,
            Specular = def.EnableSpecularHighlights,
        };
        if (def.Shape is LightShape.Spot or LightShape.Flat)
        {
            var aim = InvertAimHeight(def.Position, def.RotationPitchYawRollDegrees.X);
            // Aiming at the light's own height is what a null aim height means; keep an explicit one that says the same.
            var level = MathF.Abs(aim - height) <= PositionToleranceMetres;
            l.AimHeight = level ? (basis?.AimHeight is { } b && MathF.Abs(b - height) <= PositionToleranceMetres ? b : null) : aim;
        }
        return l;
    }

    /// <summary>
    /// A Scene built from the stage <paramref name="stage"/>. Lights come back in "light0", "light1" order. The backdrop's
    /// scale and distance in the stage include the live <paramref name="adjust"/> (it is a global setting, applied again whenever
    /// a Scene is placed), so it is taken back out here and the Scene holds the values it was built from.
    /// <paramref name="basis"/> gives everything a stage does not hold.
    /// </summary>
    public static Scene FromStage(StageDefinition stage, Scene basis, BackdropAdjust? adjust = null)
    {
        adjust ??= new BackdropAdjust();
        var scene = basis.Clone();
        scene.Lights = new List<SceneLight>();
        var keys = stage.Objects.Keys.Where(k => k.StartsWith("light", StringComparison.Ordinal) && int.TryParse(k.AsSpan(5), out _))
            .OrderBy(k => int.Parse(k.AsSpan(5))).ToList();
        for (var i = 0; i < keys.Count; i++)
        {
            if (stage.Objects[keys[i]] is not LightDefinition def) continue;
            var b = i < basis.Lights.Count ? basis.Lights[i] : null;
            scene.Lights.Add(ReadLight(def, b, i < SpareRoles.Length ? SpareRoles[i] : "extra" + (i + 1)));
        }

        if (stage.Objects.TryGetValue("backdrop", out var o) && o is BgObjectDefinition bg)
        {
            var sb = scene.Backdrop ?? new SceneBackdrop();
            sb.Color = new[] { bg.DyeColor.X, bg.DyeColor.Y, bg.DyeColor.Z };
            sb.Scale = new[] { adjust.ScaleX != 0f ? bg.Scale.X / adjust.ScaleX : bg.Scale.X, adjust.ScaleY != 0f ? bg.Scale.Y / adjust.ScaleY : bg.Scale.Y, bg.Scale.Z };
            sb.DistanceBehind = -bg.Position.Z - adjust.DistanceOffset;
            if (stage.Objects.TryGetValue("backdropLight", out var bl) && bl is LightDefinition bld)
                sb.Light = ReadLight(bld, basis.Backdrop?.Light, "bg");
            scene.Backdrop = sb;
        }
        else if (basis.Set?.Kind == "sweep" && scene.Backdrop != null)
        {
            // Part 9 integration: a sweep replaces the panel, so the stage holds no "backdrop" object. The Scene keeps its backdrop
            // block (colour, scale, light) as it was; only the light can have been edited on the stage.
            if (stage.Objects.TryGetValue("backdropLight", out var sl) && sl is LightDefinition sld)
                scene.Backdrop.Light = ReadLight(sld, basis.Backdrop?.Light, "bg");
        }
        else scene.Backdrop = null;
        return scene;
    }

    /// <summary>Everything that differs between two Scenes beyond the tolerances. Empty means they are the same Scene.</summary>
    public static List<string> Differences(Scene a, Scene b)
    {
        var d = new List<string>();
        if (a.Lights.Count != b.Lights.Count) { d.Add($"light count {a.Lights.Count} vs {b.Lights.Count}"); return d; }
        for (var i = 0; i < a.Lights.Count; i++) DiffLight(a.Lights[i], b.Lights[i], "light " + (i + 1), d);
        if ((a.Backdrop == null) != (b.Backdrop == null)) d.Add("backdrop present on one side only");
        else if (a.Backdrop != null && b.Backdrop != null)
        {
            if (!Same3(a.Backdrop.Color, b.Backdrop.Color, 1e-4f)) d.Add("backdrop colour");
            if (!Same3(a.Backdrop.Scale, b.Backdrop.Scale, 1e-4f)) d.Add("backdrop scale");
            if (MathF.Abs(a.Backdrop.DistanceBehind - b.Backdrop.DistanceBehind) > PositionToleranceMetres) d.Add("backdrop distance");
            if ((a.Backdrop.Light == null) != (b.Backdrop.Light == null)) d.Add("backdrop light present on one side only");
            else if (a.Backdrop.Light != null && b.Backdrop.Light != null) DiffLight(a.Backdrop.Light, b.Backdrop.Light, "backdrop light", d);
        }
        return d;
    }

    private static void DiffLight(SceneLight a, SceneLight b, string label, List<string> d)
    {
        var pa = SceneConverter.LightPosition(a);
        var pb = SceneConverter.LightPosition(b);
        if (Vector3.Distance(pa, pb) > PositionToleranceMetres) d.Add($"{label} position {Vector3.Distance(pa, pb) * 100f:F2} cm apart");
        if (AngleBetween(a.AzimuthDeg, b.AzimuthDeg) > AngleToleranceDeg && a.Distance > 0.05f) d.Add($"{label} azimuth");
        if (a.Shape != b.Shape) d.Add(label + " shape");
        if (a.Role != b.Role) d.Add(label + " role");
        if (!Same3(a.Color, b.Color, 1e-4f)) d.Add(label + " colour");
        foreach (var (n, x, y) in new[] { ("intensity", a.Intensity, b.Intensity), ("range", a.Range, b.Range), ("falloff factor", a.FalloffFactor, b.FalloffFactor), ("cone", a.ConeDeg, b.ConeDeg), ("angular falloff", a.AngularFalloffDeg, b.AngularFalloffDeg) })
            if (MathF.Abs(x - y) > 1e-3f * MathF.Max(1f, MathF.Abs(x))) d.Add($"{label} {n} {x} vs {y}");
        if (!string.Equals(a.Falloff, b.Falloff, StringComparison.OrdinalIgnoreCase)) d.Add(label + " falloff");
        if (a.CharacterShadows != b.CharacterShadows || a.ObjectShadows != b.ObjectShadows || a.Specular != b.Specular) d.Add(label + " shadows or specular");
        if (a.Shape is "Spot" or "Flat")
        {
            // Aim angle as the pitch it produces, so a null aim height and an explicit one at the same height agree.
            var pitchA = SceneConverter.AimPitchYawRoll(pa, new Vector3(0f, a.AimHeight ?? pa.Y, 0f)).X;
            var pitchB = SceneConverter.AimPitchYawRoll(pb, new Vector3(0f, b.AimHeight ?? pb.Y, 0f)).X;
            if (MathF.Abs(pitchA - pitchB) > AngleToleranceDeg) d.Add($"{label} aim {pitchA:F2} vs {pitchB:F2} degrees");
        }
    }

    private static float AngleBetween(float a, float b)
    {
        var d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    private static bool Same3(float[] a, float[] b, float tol) =>
        a.Length >= 3 && b.Length >= 3 && MathF.Abs(a[0] - b[0]) <= tol && MathF.Abs(a[1] - b[1]) <= tol && MathF.Abs(a[2] - b[2]) <= tol;
}

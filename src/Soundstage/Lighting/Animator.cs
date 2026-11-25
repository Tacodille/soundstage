// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
using System.Text;

namespace Soundstage;

/// <summary>
/// The waveforms of a Scene's animation block, as pure functions of time. Nothing here reads a clock or the game, so the
/// same seed gives the same sequence every run. <see cref="LightingService"/> (Lighting/Live) calls it each throttled tick.
/// Kinds: flicker (seeded noise that only dips), strobe (square, capped), pulse (sine on intensity or hue), drift (smooth noise).
/// </summary>
public static class Animator
{
    /// <summary>The ceiling for whole-stage re-sends per second, from the plan.</summary>
    public const float HardMaxHz = 15f;

    public const string BlockName = "animation";

    /// <summary>Tells the Scene loader this build plays the animation block. Called by the plugin before the catalogue loads.</summary>
    public static void RegisterBlock() => SceneStore.AppliedBlocks.Add(BlockName);

    public static bool IsKnownKind(string kind) => kind is "pulse" or "drift" or "flicker" or "strobe";

    /// <summary>True when the Scene has at least one enabled effect with a kind this build plays.</summary>
    public static bool HasActive(Scene s) =>
        s.Animation != null && s.Animation.Effects.Any(e => e.Enabled && IsKnownKind(e.Kind) && TargetExists(s, e.Target));

    public static bool HasStrobe(Scene s) => s.Animation != null && s.Animation.Effects.Any(e => e.Kind == "strobe");

    private static bool TargetExists(Scene s, string role) =>
        s.Lights.Any(l => l.Role == role) || s.Backdrop?.Light?.Role == role;

    /// <summary>The rate a strobe really flashes at: its own, never above the cap.</summary>
    public static float EffectiveRate(SceneAnimationEffect e, float strobeMaxHz) =>
        e.Kind == "strobe" ? MathF.Min(MathF.Max(e.RateHz, 0f), strobeMaxHz) : MathF.Max(e.RateHz, 0f);

    /// <summary>Stable across runs and machines (string.GetHashCode is not): FNV-1a over the Scene id, target and kind.</summary>
    public static int SeedFor(string sceneId, SceneAnimationEffect e)
    {
        if (e.Seed != null) return e.Seed.Value;
        unchecked
        {
            var h = 2166136261u;
            foreach (var b in Encoding.UTF8.GetBytes(sceneId + "|" + e.Target + "|" + e.Kind)) { h ^= b; h *= 16777619u; }
            return (int)h;
        }
    }

    /// <summary>A number in [0, 1) from a seed and a step. SplitMix-style mixing, so neighbouring steps are unrelated.</summary>
    public static float Noise(int seed, long step)
    {
        unchecked
        {
            var x = (ulong)(long)seed * 0x9E3779B97F4A7C15UL + (ulong)step * 0xBF58476D1CE4E5B9UL;
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
            x ^= x >> 27; x *= 0x94D049BB133111EBUL;
            x ^= x >> 31;
            return (float)((x >> 40) / (double)(1UL << 24));
        }
    }

    /// <summary>
    /// What one effect does at time <paramref name="t"/> seconds: a multiplier for intensity (1 = unchanged) or, for a hue
    /// pulse, a hue rotation in degrees. A disabled or unknown effect returns the identity.
    /// </summary>
    public static (float Intensity, float HueDeg) Evaluate(SceneAnimationEffect e, double t, int seed, float strobeMaxHz)
    {
        if (!e.Enabled) return (1f, 0f);
        var rate = EffectiveRate(e, strobeMaxHz);
        var amount = Math.Clamp(e.Amount, 0f, 1f);
        switch (e.Kind)
        {
            case "pulse":
            {
                var s = (float)Math.Sin(2.0 * Math.PI * rate * t);
                return string.Equals(e.Property, "hue", StringComparison.OrdinalIgnoreCase)
                    ? (1f, amount * 360f * s)
                    : (MathF.Max(0f, 1f + amount * s), 0f);
            }
            case "drift":
            {
                var x = t * rate;
                var i = (long)Math.Floor(x);
                var f = (float)(x - i);
                var smooth = f * f * (3f - 2f * f);
                var n = (Noise(seed, i) * (1f - smooth) + Noise(seed, i + 1) * smooth) * 2f - 1f;   // -1 to 1
                return (MathF.Max(0f, 1f + amount * n), 0f);
            }
            case "flicker":
            {
                // One new random level per 1/rate seconds. Squared so most steps are small dips and a few are deep, like a flame.
                var u = Noise(seed, (long)Math.Floor(t * rate));
                return (MathF.Max(0f, 1f - amount * u * u), 0f);
            }
            case "strobe":
            {
                if (rate <= 0f) return (1f, 0f);
                var phase = t * rate - Math.Floor(t * rate);
                return (phase < 0.5 ? 1f : MathF.Max(0f, 1f - amount), 0f);
            }
            default:
                return (1f, 0f);
        }
    }

    /// <summary>
    /// A copy of the Scene with every enabled effect applied at time <paramref name="t"/>. Only intensity and colour change;
    /// range is left alone so the light does not grow and shrink with the flicker. The Scene passed in is not touched.
    /// </summary>
    public static Scene Apply(Scene source, double t, float strobeMaxHz)
    {
        if (!HasActive(source)) return source;
        var frame = source.Clone();
        foreach (var e in frame.Animation!.Effects)
        {
            if (!e.Enabled || !IsKnownKind(e.Kind)) continue;
            var (mul, hue) = Evaluate(e, t, SeedFor(source.Id, e), strobeMaxHz);
            foreach (var l in frame.Lights.Concat(frame.Backdrop?.Light is { } bl ? new[] { bl } : Array.Empty<SceneLight>()))
            {
                if (l.Role != e.Target) continue;
                l.Intensity *= mul;
                if (hue != 0f) l.Color = Harmony.RotateHue(l.Color, hue);
            }
        }
        return frame;
    }
}

/// <summary>
/// Picks the re-send rate. Each evaluation looks at the average cost of recent whole-stage re-sends: over budget, the rate
/// drops by a quarter (never below <see cref="MinHz"/>); well under budget, it climbs back by a quarter (never above the cap).
/// </summary>
public sealed class AnimationThrottle
{
    public const float MinHz = 2f;
    public float Hz { get; private set; }
    public float Cap { get; }

    public AnimationThrottle(float cap)
    {
        Cap = Math.Clamp(cap, MinHz, Animator.HardMaxHz);
        Hz = Cap;
    }

    /// <summary>Returns true if the rate changed.</summary>
    public bool Evaluate(double averageResendMs, double budgetMs)
    {
        var before = Hz;
        if (averageResendMs > budgetMs) Hz = MathF.Max(MinHz, Hz * 0.75f);
        else if (averageResendMs < budgetMs / 3.0) Hz = MathF.Min(Cap, Hz * 1.25f);
        return MathF.Abs(Hz - before) > 1e-4f;
    }
}

/// <summary>Running average and worst of a stream of millisecond readings.</summary>
public sealed class CostMeter
{
    public int Count { get; private set; }
    public double Total { get; private set; }
    public double Worst { get; private set; }
    public double Average => Count == 0 ? 0 : Total / Count;

    public void Add(double ms)
    {
        Count++;
        Total += ms;
        if (ms > Worst) Worst = ms;
    }

    public void Reset() { Count = 0; Total = 0; Worst = 0; }

    public override string ToString() =>
        Count == 0 ? "no samples" : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"avg {Average:F2} ms, worst {Worst:F2} ms, n={Count}");
}

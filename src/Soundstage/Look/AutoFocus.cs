// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;

namespace Soundstage.Look;

/// <summary>Where the camera and the subject's head are, in world metres, at one moment.</summary>
public readonly record struct AutoFocusInput(Vector3 Camera, Vector3 Head);

/// <summary>One focus value to send: the distance it came from and the uniform value after the user's mapping.</summary>
public readonly record struct AutoFocusUpdate(float Metres, float Value);

/// <summary>
/// Decides when auto focus has something to send. The distance is camera to head; the uniform value is
/// distance x scale + offset between min and max (the user sets those, because shader packs scale focus differently).
/// It sends only when the value moved by more than <see cref="MinStep"/> and not more often than <see cref="MinIntervalMs"/>,
/// so a still camera sends nothing and a moving one does not flood the pipe. Pure: the caller supplies the clock.
/// </summary>
public sealed class AutoFocus
{
    public const float MinStep = 0.01f;
    public const int MinIntervalMs = 100;

    private float? lastValue;
    private DateTime lastSent = DateTime.MinValue;

    /// <summary>The camera-to-head distance, or null when either point is not a finite number.</summary>
    public static float? Distance(AutoFocusInput input)
    {
        if (!IsFinite(input.Camera) || !IsFinite(input.Head)) return null;
        var d = Vector3.Distance(input.Camera, input.Head);
        return float.IsFinite(d) ? d : null;
    }

    public static float Map(float metres, LookSettings s)
    {
        var lo = Math.Max(0f, Math.Min(s.FocusMin, s.FocusMax));
        var hi = Math.Max(lo, Math.Max(s.FocusMin, s.FocusMax));
        var v = metres * s.FocusScale + s.FocusOffset;
        return float.IsFinite(v) ? Math.Clamp(v, lo, hi) : lo;
    }

    /// <summary>The update to send now, or null when nothing changed enough or it is too soon. Call <see cref="Sent"/> once it went out.</summary>
    public AutoFocusUpdate? Next(AutoFocusInput input, LookSettings settings, DateTime now)
    {
        if (Distance(input) is not float metres) return null;
        if ((now - lastSent).TotalMilliseconds < MinIntervalMs) return null;
        var value = Map(metres, settings);
        if (lastValue is float last && MathF.Abs(value - last) < MinStep) return null;
        return new AutoFocusUpdate(metres, value);
    }

    public void Sent(AutoFocusUpdate update, DateTime now)
    {
        lastValue = update.Value;
        lastSent = now;
    }

    /// <summary>Forget the last value so the next call sends again (a Scene was applied or removed).</summary>
    public void Reset() => lastValue = null;

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}

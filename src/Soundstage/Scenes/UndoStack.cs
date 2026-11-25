// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Globalization;
using System.Text;

namespace Soundstage;

/// <summary>One remembered state: the working Scene as it was (slider tweaks included) and the palette it wears.</summary>
public sealed record UndoState(Scene Scene, string Palette, string Label, string Fingerprint);

/// <summary>
/// Part 7.3. The last <see cref="Capacity"/> applied states, the newest being the one on screen now. Undo re-applies the
/// one before it. Slider tweaks are noticed by <see cref="Observe"/>, which records one only after the sliders have been
/// still for a moment, so dragging a slider is one step, not sixty. Memory only: nothing here is saved, and the plugin
/// clears it on unload.
/// </summary>
public sealed class UndoStack
{
    public const int Capacity = 20;

    private readonly List<UndoState> states = new();
    private string? pendingFingerprint;
    private DateTime pendingSince;

    public int Count => states.Count;

    /// <summary>The state on screen now (the last one recorded), or null before the first apply.</summary>
    public UndoState? Top => states.Count > 0 ? states[^1] : null;

    public bool CanUndo => states.Count > 1 || pendingFingerprint != null;

    /// <summary>States oldest first, for the tests and for a history readout.</summary>
    public IReadOnlyList<UndoState> States => states;

    public void Clear()
    {
        states.Clear();
        pendingFingerprint = null;
    }

    /// <summary>Records a state (a copy of the Scene is kept). The oldest falls off past <see cref="Capacity"/>.</summary>
    public UndoState Push(Scene working, string palette, string label)
    {
        var s = new UndoState(working.Clone(), palette ?? "", label, Fingerprint(working));
        states.Add(s);
        while (states.Count > Capacity) states.RemoveAt(0);
        pendingFingerprint = null;
        return s;
    }

    /// <summary>
    /// Call every frame (or tick) with the live working Scene. Returns true when it recorded a tweak: the Scene differs from
    /// the top state and has stayed the same for <paramref name="settleSeconds"/>.
    /// </summary>
    public bool Observe(Scene working, string palette, DateTime now, double settleSeconds = 0.6)
    {
        if (Top == null) return false;
        var fp = Fingerprint(working);
        if (fp == Top.Fingerprint) { pendingFingerprint = null; return false; }
        if (fp != pendingFingerprint) { pendingFingerprint = fp; pendingSince = now; return false; }
        if ((now - pendingSince).TotalSeconds < settleSeconds) return false;
        Push(working, palette, "tweak");
        return true;
    }

    /// <summary>
    /// The state to re-apply, or null when there is nothing to go back to. If the working Scene has moved on from the top
    /// state (a tweak not yet recorded) the top state is the answer and nothing is removed; otherwise the top is dropped
    /// and the one before it is the answer.
    /// </summary>
    public UndoState? Undo(Scene? working)
    {
        if (Top == null) return null;
        pendingFingerprint = null;
        if (working != null && Fingerprint(working) != Top.Fingerprint) return Top;
        if (states.Count < 2) return null;
        states.RemoveAt(states.Count - 1);
        return Top;
    }

    /// <summary>Every number a slider or the backdrop adjuster can change, as one string. Two Scenes with the same text look the same.</summary>
    public static string Fingerprint(Scene s)
    {
        var sb = new StringBuilder();
        sb.Append(s.Id).Append('|');
        foreach (var l in s.Lights) AppendLight(sb, l);
        if (s.Backdrop is { } b)
        {
            sb.Append("bd:");
            foreach (var v in b.Color) sb.Append(F(v)).Append(',');
            foreach (var v in b.Scale) sb.Append(F(v)).Append(',');
            sb.Append(F(b.DistanceBehind)).Append(';');
            if (b.Light != null) AppendLight(sb, b.Light);
        }
        return sb.ToString();
    }

    private static void AppendLight(StringBuilder sb, SceneLight l)
    {
        sb.Append(l.Role).Append(':').Append(l.Shape).Append(',');
        foreach (var v in l.Color) sb.Append(F(v)).Append(',');
        foreach (var v in new[] { l.Intensity, l.Range, l.FalloffFactor, l.ConeDeg, l.AngularFalloffDeg, l.AzimuthDeg, l.Distance, l.Height, l.AimHeight ?? float.NaN })
            sb.Append(F(v)).Append(',');
        sb.Append(l.CharacterShadows ? 'c' : '-').Append(l.ObjectShadows ? 'o' : '-').Append(l.Specular ? 's' : '-').Append(';');
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}

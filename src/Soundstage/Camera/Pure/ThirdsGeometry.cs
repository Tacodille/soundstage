// No Dalamud types in this file: the offline self-check compiles Camera/Pure/*.cs.
namespace Soundstage.CameraTools;

/// <summary>One line segment of the overlay in screen pixels. Kind picks its colour: thirds, golden, cross or margin.</summary>
public readonly record struct OverlayLine(float X1, float Y1, float X2, float Y2, OverlayKind Kind);

public enum OverlayKind { Thirds, Golden, Cross, Margin }

/// <summary>The overlay's lines for a screen size and the switches. Pure so the self-check can measure them.</summary>
public static class ThirdsGeometry
{
    /// <summary>1 / phi: the golden-ratio gridlines sit at 0.382 and 0.618 of the width and height.</summary>
    public const float Phi = 0.6180340f;

    public static List<OverlayLine> Build(float width, float height, CameraSettings s)
    {
        var lines = new List<OverlayLine>();
        if (width <= 0 || height <= 0) return lines;
        if (s.Thirds) Grid(lines, width, height, 1f / 3f, 2f / 3f, OverlayKind.Thirds);
        if (s.Golden) Grid(lines, width, height, 1f - Phi, Phi, OverlayKind.Golden);
        if (s.CentreCross)
        {
            lines.Add(new(width / 2f, 0, width / 2f, height, OverlayKind.Cross));
            lines.Add(new(0, height / 2f, width, height / 2f, OverlayKind.Cross));
        }
        if (s.SafeMargin)
        {
            var m = Math.Clamp(s.SafeMarginPercent, 0f, 25f) / 100f;
            float l = width * m, r = width * (1f - m), t = height * m, b = height * (1f - m);
            lines.Add(new(l, t, r, t, OverlayKind.Margin));
            lines.Add(new(r, t, r, b, OverlayKind.Margin));
            lines.Add(new(r, b, l, b, OverlayKind.Margin));
            lines.Add(new(l, b, l, t, OverlayKind.Margin));
        }
        return lines;
    }

    private static void Grid(List<OverlayLine> lines, float w, float h, float a, float b, OverlayKind kind)
    {
        lines.Add(new(w * a, 0, w * a, h, kind));
        lines.Add(new(w * b, 0, w * b, h, kind));
        lines.Add(new(0, h * a, w, h * a, kind));
        lines.Add(new(0, h * b, w, h * b, kind));
    }
}

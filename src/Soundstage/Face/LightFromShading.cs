// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>
/// Where the key light sits, read from the shading of one face. Azimuth uses the Scene convention (0 in front, + toward the picture's right
/// which is the subject's left, 90 is level with the face), elevation is + above.
/// </summary>
public sealed record LightEstimate(bool Ok, string Reason, float AzimuthDeg, float ElevationDeg,
    float Left, float Right, float Top, float Bottom, string Text);

/// <summary>
/// Compares the mean linear brightness of the two cheeks and of forehead and chin. For a point light at angle phi from the face's front and
/// two surfaces tilted +/-theta away from it, Lambert shading gives (R - L) / (R + L) = tan(theta) x tan(phi) while both stay lit, so
/// phi = atan(d / tan(theta)). Forehead against chin is the same with the up and down tilt. A pure function of a <see cref="FaceResult"/> and
/// the picture. The model is a first-order one: it ignores bounce, shadows from the nose and brow, and skin that is not the same tone on both
/// sides, which is why docs/face-model-decision.md carries a measured error table rather than a claim.
/// </summary>
public static class LightFromShading
{
    // MediaPipe face mesh ids. 234 and 454 are the face edges (picture left, picture right); the cheek points sit below the eyes.
    public const int EdgeLeft = 234, EdgeRight = 454;
    public static readonly int[] CheekLeft = { 205, 50 };
    public static readonly int[] CheekRight = { 425, 280 };
    public const int ForeheadTop = 10, ForeheadLow = 151, ChinTip = 152, ChinLow = 199;

    public const float MinBrightness = 0.004f;   // linear; below this the face is too dark to read a direction

    public static LightEstimate Estimate(FaceResult face, FaceImage image, FaceSettings settings)
    {
        if (!face.Available) return Fail(face.Reason);
        if (face.LandmarkCount < 455) return Fail("The model gave too few face points to read the shading.");

        var (ex0, _) = face.Point(EdgeLeft);
        var (ex1, _) = face.Point(EdgeRight);
        var half = Math.Max(2, (int)(MathF.Abs(ex1 - ex0) * image.Width * 0.06f));

        var left = Mean(face, image, CheekLeft, half);
        var right = Mean(face, image, CheekRight, half);
        var forehead = Mean(face, image, new[] { ForeheadLow }, half, Blend(face, ForeheadTop, ForeheadLow));
        var chin = Mean(face, image, new[] { ChinLow }, half);
        if (float.IsNaN(left) || float.IsNaN(right) || float.IsNaN(forehead) || float.IsNaN(chin))
            return Fail("Part of the face is outside the picture, so the shading cannot be read.");
        if (left + right < MinBrightness || forehead + chin < MinBrightness)
            return Fail("The face is too dark in this picture to read a light direction.");

        var dH = (right - left) / (right + left);
        var dV = (forehead - chin) / (forehead + chin);
        var tc = MathF.Tan(Math.Clamp(settings.CheekAngleDeg, 5f, 80f) * MathF.PI / 180f);
        var tv = MathF.Tan(Math.Clamp(settings.ForeheadChinAngleDeg, 5f, 80f) * MathF.PI / 180f);
        var az = MathF.Atan(dH / tc) * 180f / MathF.PI;
        var el = MathF.Atan(dV / tv) * 180f / MathF.PI;

        var side = MathF.Abs(az) < 4f ? "from the front" : az > 0 ? $"{az:0} degrees toward the picture's right (the subject's left)" : $"{-az:0} degrees toward the picture's left (the subject's right)";
        var up = MathF.Abs(el) < 4f ? "level with the face" : el > 0 ? $"{el:0} degrees above" : $"{-el:0} degrees below";
        return new LightEstimate(true, "", az, el, left, right, forehead, chin, $"Key light looks to be {side}, {up}.");
    }

    private static LightEstimate Fail(string why) => new(false, why, 0, 0, 0, 0, 0, 0, why);

    private static (float X, float Y) Blend(FaceResult f, int a, int b)
    {
        var (ax, ay) = f.Point(a);
        var (bx, by) = f.Point(b);
        return ((ax + bx) / 2, (ay + by) / 2);
    }

    /// <summary>Mean luminance of squares centred on each landmark (or on <paramref name="overridePoint"/>); NaN when a square leaves the picture.</summary>
    private static float Mean(FaceResult face, FaceImage img, int[] ids, int half, (float X, float Y)? overridePoint = null)
    {
        double sum = 0; long n = 0;
        var points = overridePoint is { } o ? new[] { o } : ids.Select(face.Point).ToArray();
        foreach (var (px, py) in points)
        {
            int cx = (int)(px * img.Width), cy = (int)(py * img.Height);
            if (cx - half < 0 || cy - half < 0 || cx + half >= img.Width || cy + half >= img.Height) return float.NaN;
            for (var y = cy - half; y <= cy + half; y++)
                for (var x = cx - half; x <= cx + half; x++) { sum += img.Luminance(x, y); n++; }
        }
        return n == 0 ? float.NaN : (float)(sum / n);
    }
}

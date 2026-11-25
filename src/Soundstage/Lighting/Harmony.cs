// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
namespace Soundstage;

/// <summary>Colour harmony in HSV. One base colour in, a role-to-colour palette out.</summary>
public static class Harmony
{
    public static readonly string[] Kinds = { "complementary", "split complementary", "triadic", "analogous" };

    /// <summary>Hue offsets in degrees, in the order the roles take them. The first is always 0 (the base colour itself).</summary>
    public static float[] Offsets(string kind) => kind.Trim().ToLowerInvariant() switch
    {
        "complementary" => new[] { 0f, 180f },
        "split complementary" or "split-complementary" or "split" => new[] { 0f, 150f, 210f },
        "triadic" => new[] { 0f, 120f, 240f },
        "analogous" => new[] { 0f, 30f, -30f },
        _ => new[] { 0f, 180f },
    };

    public static (float H, float S, float V) ToHsv(float[] rgb)
    {
        float r = rgb[0], g = rgb[1], b = rgb[2];
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var d = max - min;
        float h;
        if (d < 1e-6f) h = 0f;
        else if (max == r) h = 60f * (((g - b) / d) % 6f);
        else if (max == g) h = 60f * (((b - r) / d) + 2f);
        else h = 60f * (((r - g) / d) + 4f);
        if (h < 0f) h += 360f;
        return (h, max <= 1e-6f ? 0f : d / max, max);
    }

    public static float[] FromHsv(float h, float s, float v)
    {
        h = ((h % 360f) + 360f) % 360f;
        s = Math.Clamp(s, 0f, 1f);
        var c = v * s;
        var x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        var m = v - c;
        float r, g, b;
        switch ((int)(h / 60f))
        {
            case 0: (r, g, b) = (c, x, 0f); break;
            case 1: (r, g, b) = (x, c, 0f); break;
            case 2: (r, g, b) = (0f, c, x); break;
            case 3: (r, g, b) = (0f, x, c); break;
            case 4: (r, g, b) = (x, 0f, c); break;
            default: (r, g, b) = (c, 0f, x); break;
        }
        return new[] { r + m, g + m, b + m };
    }

    public static float[] RotateHue(float[] rgb, float degrees)
    {
        var (h, s, v) = ToHsv(rgb);
        return FromHsv(h + degrees, s, v);
    }

    /// <summary>
    /// A palette for the given roles from one base colour. The first role (the key) takes the base colour; later roles take the
    /// next hue offsets in turn. A fill keeps its hue but is softer (less saturated); the backdrop takes the second hue, darker.
    /// Every colour keeps the base colour's value, so the brightest channel (and with it the brightness rule) does not move.
    /// </summary>
    public static Dictionary<string, float[]> Palette(string kind, float[] baseColour, IReadOnlyList<string> roles)
    {
        var offsets = Offsets(kind);
        var (h, s, v) = ToHsv(baseColour);
        if (s < 0.05f) s = 0.6f;   // a grey base has no hue to harmonise; give it a usable one
        var result = new Dictionary<string, float[]>();
        var k = 0;   // position among the non-backdrop roles; the first one is the key
        for (var i = 0; i < roles.Count; i++)
        {
            var role = roles[i];
            if (role == "backdrop") continue;   // handled last
            var off = k == 0 ? 0f : offsets[1 + (k - 1) % (offsets.Length - 1)];
            k++;
            var sat = role.StartsWith("fill", StringComparison.Ordinal) ? s * 0.6f : s;
            result[role] = FromHsv(h + off, sat, v);
        }
        if (roles.Contains("backdrop"))
        {
            var off = offsets.Length > 1 ? offsets[1] : 0f;
            result["backdrop"] = FromHsv(h + off, s, v * 0.4f);
        }
        return result;
    }
}

// No Dalamud types in this file.
using StbImageSharp;

namespace Soundstage.Face;

/// <summary>A decoded picture as straight 8-bit RGBA, read on this machine only.</summary>
public sealed class FaceImage
{
    public FaceImage(int width, int height, byte[] rgba)
    {
        Width = width; Height = height; Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public static FaceImage? TryLoad(string path, out string reason)
    {
        reason = "";
        try
        {
            using var s = File.OpenRead(path);
            var img = ImageResult.FromStream(s, ColorComponents.RedGreenBlueAlpha);
            return new FaceImage(img.Width, img.Height, img.Data);
        }
        catch (Exception e)
        {
            reason = "That picture could not be read: " + e.Message;
            return null;
        }
    }

    /// <summary>Linear-light luminance (Rec. 709) of one pixel, 0 to 1.</summary>
    public float Luminance(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return 0.2126f * ToLinear(Rgba[i]) + 0.7152f * ToLinear(Rgba[i + 1]) + 0.0722f * ToLinear(Rgba[i + 2]);
    }

    private static float ToLinear(byte v)
    {
        var c = v / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}

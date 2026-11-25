// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Soundstage.Overlays;

/// <summary>A plain Sobel edge filter for the onion skin's edge-only mode. No AI, no libraries.</summary>
public static class EdgeFilter
{
    /// <summary>
    /// <paramref name="rgba"/> is w*h*4 bytes (R, G, B, A). Returns w*h*4 bytes: white and opaque where the gradient is at least
    /// <paramref name="threshold"/>, fully transparent elsewhere. The one-pixel border is always transparent.
    /// </summary>
    public static byte[] Sobel(byte[] rgba, int w, int h, int threshold = 80)
    {
        if (rgba.Length != w * h * 4) throw new ArgumentException("The pixel buffer does not match the size.");
        var luma = new float[w * h];
        for (var i = 0; i < w * h; i++)
        {
            var a = rgba[i * 4 + 3] / 255f;
            luma[i] = (0.299f * rgba[i * 4] + 0.587f * rgba[i * 4 + 1] + 0.114f * rgba[i * 4 + 2]) * a;
        }
        var outPx = new byte[w * h * 4];
        for (var y = 1; y < h - 1; y++)
        {
            for (var x = 1; x < w - 1; x++)
            {
                float P(int dx, int dy) => luma[(y + dy) * w + x + dx];
                var gx = -P(-1, -1) - 2 * P(-1, 0) - P(-1, 1) + P(1, -1) + 2 * P(1, 0) + P(1, 1);
                var gy = -P(-1, -1) - 2 * P(0, -1) - P(1, -1) + P(-1, 1) + 2 * P(0, 1) + P(1, 1);
                if (MathF.Sqrt(gx * gx + gy * gy) < threshold) continue;
                var o = (y * w + x) * 4;
                outPx[o] = outPx[o + 1] = outPx[o + 2] = outPx[o + 3] = 255;
            }
        }
        return outPx;
    }

    /// <summary>Loads an image, runs <see cref="Sobel"/> and returns a PNG of the edges on transparency.</summary>
    public static byte[] EdgePng(string imagePath, int threshold = 80)
    {
        using var src = new Bitmap(imagePath);
        var w = src.Width;
        var h = src.Height;
        var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bgra = new byte[w * h * 4];
        try
        {
            for (var y = 0; y < h; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * w * 4, w * 4);
        }
        finally { src.UnlockBits(data); }
        var rgba = new byte[bgra.Length];
        for (var i = 0; i < w * h; i++)
        {
            rgba[i * 4] = bgra[i * 4 + 2];
            rgba[i * 4 + 1] = bgra[i * 4 + 1];
            rgba[i * 4 + 2] = bgra[i * 4];
            rgba[i * 4 + 3] = bgra[i * 4 + 3];
        }
        var edges = Sobel(rgba, w, h, threshold);
        using var result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var rd = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var outBgra = new byte[edges.Length];
            for (var i = 0; i < w * h; i++)
            {
                outBgra[i * 4] = edges[i * 4 + 2];
                outBgra[i * 4 + 1] = edges[i * 4 + 1];
                outBgra[i * 4 + 2] = edges[i * 4];
                outBgra[i * 4 + 3] = edges[i * 4 + 3];
            }
            for (var y = 0; y < h; y++)
                Marshal.Copy(outBgra, y * w * 4, rd.Scan0 + y * rd.Stride, w * 4);
        }
        finally { result.UnlockBits(rd); }
        using var ms = new MemoryStream();
        result.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}

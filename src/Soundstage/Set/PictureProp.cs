// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Buffers.Binary;
using System.Numerics;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using Stagehand.Definitions.Objects;
using StbImageSharp;

namespace Soundstage;

/// <summary>
/// The picture prop: the portrait of the Admiral (fun_b0_m0204) with its picture texture swapped for the user's image,
/// through an embedded modpack on that one object, the way the canvas test does it. Conversion is bytes in and bytes
/// out: RGBA pixels become an uncompressed B8G8R8A8 .tex with a full mip chain. The result matches the header and
/// pixels of the test texture the canvas test ships (checked in SetChecks).
/// </summary>
public static class PictureProp
{
    /// <summary>The picture is shrunk until its long side is at most this many pixels (plan 9.6).</summary>
    public const int MaxSide = 1024;

    public const string Model = "bgcommon/hou/indoor/general/0204/bgparts/fun_b0_m0204.mdl";
    public const string DiffusePath = "bgcommon/hou/indoor/general/0204/texture/fun_b0_m0204_0b_d.tex";
    public const string ModpackId = "soundstage-picture";

    /// <summary>Size of the picture quad inside the model at scale 1 (mesh 0, measured by tools/measure_mdl.py): 1.2696 m wide, 2.0415 m high, centre 0.045 m above the pivot.</summary>
    public const float QuadWidth = 1.2696f;
    public const float QuadHeight = 2.0415f;
    public const float QuadCentreY = 0.045f;

    private const uint TexFlags = 0x00800000;   // attribute flags of a stock 2D texture
    private const uint FormatB8G8R8A8 = 0x1450;
    private const int HeaderSize = 80;

    /// <summary>The size an image of this shape ends up at: long side capped at <see cref="MaxSide"/>, both sides at least 1.</summary>
    public static (int W, int H) FitSize(int w, int h)
    {
        if (w < 1 || h < 1) throw new ArgumentException("Image has no pixels.");
        var longSide = Math.Max(w, h);
        if (longSide <= MaxSide) return (w, h);
        var f = (double)MaxSide / longSide;
        return (Math.Max(1, (int)Math.Round(w * f)), Math.Max(1, (int)Math.Round(h * f)));
    }

    /// <summary>Area-average resize of RGBA pixels, each channel on its own (no alpha premultiply, which crushes colour where alpha is low).</summary>
    public static byte[] Resize(byte[] rgba, int w, int h, int nw, int nh)
    {
        if (nw == w && nh == h) return (byte[])rgba.Clone();
        var outp = new byte[nw * nh * 4];
        for (var y = 0; y < nh; y++)
        {
            var y0 = (int)((long)y * h / nh);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * h / nh));
            for (var x = 0; x < nw; x++)
            {
                var x0 = (int)((long)x * w / nw);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * w / nw));
                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var yy = y0; yy < Math.Min(y1, h); yy++)
                    for (var xx = x0; xx < Math.Min(x1, w); xx++)
                    {
                        var i = (yy * w + xx) * 4;
                        r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; a += rgba[i + 3]; n++;
                    }
                var o = (y * nw + x) * 4;
                outp[o] = (byte)((r + n / 2) / n);
                outp[o + 1] = (byte)((g + n / 2) / n);
                outp[o + 2] = (byte)((b + n / 2) / n);
                outp[o + 3] = (byte)((a + n / 2) / n);
            }
        }
        return outp;
    }

    /// <summary>RGBA (row 0 is the top) to an uncompressed B8G8R8A8 .tex with the mip chain down to 1x1 (at most 13 levels).</summary>
    public static byte[] ToTex(byte[] rgba, int w, int h)
    {
        if (rgba.Length != w * h * 4) throw new ArgumentException("Pixel buffer does not match width x height x 4.");
        var mips = new List<byte[]>();
        var level = rgba;
        int lw = w, lh = h;
        while (true)
        {
            mips.Add(ToBgra(level));
            if ((lw == 1 && lh == 1) || mips.Count == 13) break;
            var nw = Math.Max(1, lw / 2);
            var nh = Math.Max(1, lh / 2);
            level = Resize(rgba, w, h, nw, nh);   // each level from the full-size image, like the test texture's builder
            lw = nw; lh = nh;
        }

        var header = new byte[HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0), TexFlags);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), FormatB8G8R8A8);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), (ushort)w);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), (ushort)h);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), 1);
        header[14] = (byte)mips.Count;
        var offset = HeaderSize;
        for (var i = 0; i < mips.Count; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28 + 4 * i), (uint)offset);
            offset += mips[i].Length;
        }
        var result = new byte[offset];
        header.CopyTo(result, 0);
        var at = HeaderSize;
        foreach (var m in mips) { m.CopyTo(result, at); at += m.Length; }
        return result;
    }

    private static byte[] ToBgra(byte[] rgba)
    {
        var o = new byte[rgba.Length];
        for (var i = 0; i < rgba.Length; i += 4) { o[i] = rgba[i + 2]; o[i + 1] = rgba[i + 1]; o[i + 2] = rgba[i]; o[i + 3] = rgba[i + 3]; }
        return o;
    }

    /// <summary>The mip 0 pixels of a B8G8R8A8 .tex as RGBA, or null if it is any other format.</summary>
    public static (byte[] Rgba, int W, int H)? ReadTex(byte[] tex)
    {
        if (tex.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(tex.AsSpan(4)) != FormatB8G8R8A8) return null;
        int w = BinaryPrimitives.ReadUInt16LittleEndian(tex.AsSpan(8)), h = BinaryPrimitives.ReadUInt16LittleEndian(tex.AsSpan(10));
        var off = (int)BinaryPrimitives.ReadUInt32LittleEndian(tex.AsSpan(28));
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = tex[off + i + 2]; rgba[i + 1] = tex[off + i + 1]; rgba[i + 2] = tex[off + i]; rgba[i + 3] = tex[off + i + 3]; }
        return (rgba, w, h);
    }

    /// <summary>Decodes a PNG or JPG and turns it into a .tex capped at <see cref="MaxSide"/>. Returns the tex and the final pixel size.</summary>
    public static (byte[] Tex, int W, int H) FromImageBytes(byte[] image)
    {
        var img = ImageResult.FromMemory(image, ColorComponents.RedGreenBlueAlpha);
        var (nw, nh) = FitSize(img.Width, img.Height);
        var rgba = Resize(img.Data, img.Width, img.Height, nw, nh);
        return (ToTex(rgba, nw, nh), nw, nh);
    }

    /// <summary>Same, from a file on disk.</summary>
    public static (byte[] Tex, int W, int H) FromFile(string path) => FromImageBytes(File.ReadAllBytes(path));

    /// <summary>
    /// The canvas object for a picture and the modpack that carries its texture. Scale makes the quad the picture's shape at
    /// the requested height; the frame stretches with it. The picture faces the subject at the stage origin, turned by the extra yaw.
    /// </summary>
    public static (BgObjectDefinition Object, EmbeddedModpackDefinition Pack) Build(ScenePicture p, byte[] tex, int pixelW, int pixelH)
    {
        var height = Math.Clamp(p.Height, 0.2f, 4f);
        var sy = height / QuadHeight;
        var sx = height * pixelW / pixelH / QuadWidth;
        var pos = new Vector3(p.X, p.CentreHeight - QuadCentreY * sy, p.Z);
        // Model +Z is assumed to be its front (the canvas test's assumption, checked in game by step 9.5).
        var yaw = MathF.Atan2(-pos.X, -pos.Z) / (MathF.PI / 180f) + p.YawDeg;
        var obj = new BgObjectDefinition
        {
            DisplayName = "Picture",
            ModelGamePath = Model,
            Position = pos,
            RotationPitchYawRollDegrees = new Vector3(0f, yaw, 0f),
            Scale = new Vector3(Math.Clamp(sx, 0.1f, 8f), sy, 1f),
            Opacity = 1f,
            DyeColor = Vector4.One,
            ModpackId = ModpackId,
        };
        var pack = new EmbeddedModpackDefinition { DisplayName = "Soundstage picture" };
        pack.ModdedResources[DiffusePath] = new EmbeddedModResourceDefinition
        {
            CompressionScheme = ModCompressionScheme.Zlib,
            CompressedDataBytes = EmbeddedModResourceDefinition.CompressDataBytes(tex, ModCompressionScheme.Zlib),
        };
        return (obj, pack);
    }
}

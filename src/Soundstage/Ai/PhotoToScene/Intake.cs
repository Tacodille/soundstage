// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using StbImageSharp;
using StbImageWriteSharp;

namespace Soundstage.Ai.PhotoToScene;

public enum IntakeError { None, NoPath, NotFound, TooBig, TooManyPixels, Unreadable, Unsupported }

/// <summary>A photo ready to send: re-encoded JPEG with no metadata, at most 1024 pixels on the long side, plus a small preview.</summary>
public sealed class PreparedImage
{
    public byte[] Jpeg { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }

    /// <summary>Tightly packed RGB for the on-screen preview (long side at most <see cref="PhotoIntake.ThumbLongSide"/>).</summary>
    public byte[] ThumbRgb { get; init; } = Array.Empty<byte>();
    public int ThumbWidth { get; init; }
    public int ThumbHeight { get; init; }

    public IntakeError Error { get; init; }
    public string Message { get; init; } = "";
    public bool Ok => Error == IntakeError.None;
}

/// <summary>
/// Loads a photo for the photo-to-scene tab. The original file is only ever opened for reading: nothing is written back,
/// copied or moved. The picture is turned upright using its own EXIF orientation, shrunk to at most 1024 pixels on the
/// long side, and written out again as a fresh JPEG, which is how EXIF (camera, time, GPS) is dropped.
/// </summary>
public static class PhotoIntake
{
    public const int MaxLongSide = 1024;
    public const int ThumbLongSide = 48;
    public const long MaxFileBytes = 25L * 1024 * 1024;
    public const long MaxPixels = 40L * 1000 * 1000;
    public const int JpegQuality = 85;

    public static PreparedImage LoadFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Fail(IntakeError.NoPath, "Pick a photo first.");
        path = path.Trim().Trim('"');
        if (!File.Exists(path)) return Fail(IntakeError.NotFound, "That file was not found.");
        try
        {
            var length = new FileInfo(path).Length;
            if (length > MaxFileBytes) return Fail(IntakeError.TooBig, $"That file is {length / 1048576} MB. The limit is {MaxFileBytes / 1048576} MB.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bytes = new byte[length];
            var read = 0;
            while (read < bytes.Length)
            {
                var n = stream.Read(bytes, read, bytes.Length - read);
                if (n == 0) break;
                read += n;
            }
            return Prepare(read == bytes.Length ? bytes : bytes[..read]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail(IntakeError.Unreadable, "That file could not be opened.");
        }
    }

    public static PreparedImage Prepare(byte[] fileBytes)
    {
        if (fileBytes.Length > MaxFileBytes) return Fail(IntakeError.TooBig, "That file is too big.");
        try
        {
            ImageInfo? info;
            using (var probe = new StrictStream(fileBytes)) info = ImageInfo.FromStream(probe);
            if (info == null) return Fail(IntakeError.Unsupported, "That is not a picture Soundstage can read. Use a JPEG, PNG, BMP, GIF or TGA file.");
            if ((long)info.Value.Width * info.Value.Height > MaxPixels) return Fail(IntakeError.TooManyPixels, "That picture is bigger than 40 megapixels. Save a smaller copy and try again.");

            ImageResult decoded;
            using (var strict = new StrictStream(fileBytes)) decoded = ImageResult.FromStream(strict, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            var rgb = FlattenToRgb(decoded.Data, decoded.Width, decoded.Height);
            var (w, h) = (decoded.Width, decoded.Height);

            var orientation = ReadExifOrientation(fileBytes);
            if (orientation is >= 2 and <= 8) (rgb, w, h) = Orient(rgb, w, h, orientation);
            var (ow, oh) = (w, h);

            if (Math.Max(w, h) > MaxLongSide)
            {
                var scale = (double)MaxLongSide / Math.Max(w, h);
                (rgb, w, h) = Resize(rgb, w, h, Math.Max(1, (int)Math.Round(w * scale)), Math.Max(1, (int)Math.Round(h * scale)));
            }

            var tScale = Math.Min(1.0, (double)ThumbLongSide / Math.Max(w, h));
            var (thumb, tw, th) = tScale >= 1.0 ? (rgb, w, h) : Resize(rgb, w, h, Math.Max(1, (int)Math.Round(w * tScale)), Math.Max(1, (int)Math.Round(h * tScale)));

            using var output = new MemoryStream();
            new ImageWriter().WriteJpg(rgb, w, h, StbImageWriteSharp.ColorComponents.RedGreenBlue, output, JpegQuality);
            return new PreparedImage
            {
                Jpeg = output.ToArray(),
                Width = w,
                Height = h,
                OriginalWidth = ow,
                OriginalHeight = oh,
                ThumbRgb = thumb,
                ThumbWidth = tw,
                ThumbHeight = th,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException or IndexOutOfRangeException or OutOfMemoryException or InvalidDataException)
        {
            return Fail(IntakeError.Unreadable, "That picture could not be read.");
        }
    }

    /// <summary>
    /// A read-only stream over the bytes that throws when a read is asked for past the end. The stb decoders treat the end of
    /// a stream as a stream of zero or 0xFF bytes and can spin forever on a cut-off file; this turns that into an exception.
    /// </summary>
    private sealed class StrictStream : Stream
    {
        private readonly byte[] data;
        private int pos;
        public StrictStream(byte[] data) { this.data = data; }
        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => pos; set => pos = (int)Math.Clamp(value, 0, data.Length); }
        public override void Flush() { }
        public override int ReadByte()
        {
            if (pos >= data.Length) throw new InvalidDataException("The picture ends early.");
            return data[pos++];
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            if (pos >= data.Length) throw new InvalidDataException("The picture ends early.");
            var n = Math.Min(count, data.Length - pos);
            Array.Copy(data, pos, buffer, offset, n);
            pos += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => pos + offset, _ => data.Length + offset };
            return pos;
        }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static PreparedImage Fail(IntakeError error, string message) => new() { Error = error, Message = message };

    /// <summary>Alpha onto mid grey, so a transparent PNG does not turn black. Returns tightly packed RGB.</summary>
    private static byte[] FlattenToRgb(byte[] rgba, int w, int h)
    {
        var rgb = new byte[w * h * 3];
        for (int i = 0, o = 0; i < w * h; i++, o += 3)
        {
            int a = rgba[i * 4 + 3];
            for (var c = 0; c < 3; c++) rgb[o + c] = (byte)((rgba[i * 4 + c] * a + 128 * (255 - a) + 127) / 255);
        }
        return rgb;
    }

    /// <summary>The EXIF orientation (1 to 8) of a JPEG, or 1 when there is none or the file is not a JPEG.</summary>
    public static int ReadExifOrientation(byte[] jpeg)
    {
        try
        {
            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return 1;
            var pos = 2;
            while (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF)
            {
                var marker = jpeg[pos + 1];
                if (marker == 0xFF) { pos++; continue; }
                if (marker == 0xDA || marker == 0xD9) break;
                var len = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
                if (len < 2) break;
                if (marker == 0xE1 && pos + 4 + 6 <= jpeg.Length && jpeg[pos + 4] == 'E' && jpeg[pos + 5] == 'x' && jpeg[pos + 6] == 'i' && jpeg[pos + 7] == 'f')
                {
                    var tiff = pos + 10;
                    var end = Math.Min(jpeg.Length, pos + 2 + len);
                    if (tiff + 8 > end) return 1;
                    var little = jpeg[tiff] == 'I';
                    int U16(int at) => little ? jpeg[at] | (jpeg[at + 1] << 8) : (jpeg[at] << 8) | jpeg[at + 1];
                    long U32(int at) => little
                        ? jpeg[at] | ((long)jpeg[at + 1] << 8) | ((long)jpeg[at + 2] << 16) | ((long)jpeg[at + 3] << 24)
                        : ((long)jpeg[at] << 24) | ((long)jpeg[at + 1] << 16) | ((long)jpeg[at + 2] << 8) | jpeg[at + 3];
                    var ifd = tiff + (int)Math.Min(U32(tiff + 4), int.MaxValue / 2);
                    if (ifd + 2 > end) return 1;
                    var count = U16(ifd);
                    for (var i = 0; i < count; i++)
                    {
                        var entry = ifd + 2 + i * 12;
                        if (entry + 12 > end) break;
                        if (U16(entry) == 0x0112)
                        {
                            var v = U16(entry + 8);
                            return v is >= 1 and <= 8 ? v : 1;
                        }
                    }
                    return 1;
                }
                pos += 2 + len;
            }
        }
        catch (IndexOutOfRangeException) { }
        return 1;
    }

    /// <summary>Applies an EXIF orientation (2 to 8) to tightly packed RGB so the picture is upright.</summary>
    public static (byte[] Rgb, int W, int H) Orient(byte[] src, int w, int h, int orientation)
    {
        var swap = orientation >= 5;
        var (ow, oh) = swap ? (h, w) : (w, h);
        var dst = new byte[ow * oh * 3];
        for (var y = 0; y < oh; y++)
            for (var x = 0; x < ow; x++)
            {
                var (sx, sy) = orientation switch
                {
                    2 => (w - 1 - x, y),
                    3 => (w - 1 - x, h - 1 - y),
                    4 => (x, h - 1 - y),
                    5 => (y, x),
                    6 => (y, h - 1 - x),
                    7 => (w - 1 - y, h - 1 - x),
                    8 => (w - 1 - y, x),
                    _ => (x, y),
                };
                Array.Copy(src, (sy * w + sx) * 3, dst, (y * ow + x) * 3, 3);
            }
        return (dst, ow, oh);
    }

    /// <summary>Box-filter shrink (each new pixel is the average of the source pixels it covers).</summary>
    public static (byte[] Rgb, int W, int H) Resize(byte[] src, int w, int h, int nw, int nh)
    {
        var dst = new byte[nw * nh * 3];
        for (var y = 0; y < nh; y++)
        {
            var y0 = (int)((long)y * h / nh);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * h / nh));
            for (var x = 0; x < nw; x++)
            {
                var x0 = (int)((long)x * w / nw);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * w / nw));
                long r = 0, g = 0, b = 0;
                var n = 0;
                for (var yy = y0; yy < Math.Min(y1, h); yy++)
                    for (var xx = x0; xx < Math.Min(x1, w); xx++)
                    {
                        var i = (yy * w + xx) * 3;
                        r += src[i]; g += src[i + 1]; b += src[i + 2];
                        n++;
                    }
                if (n == 0) n = 1;
                var o = (y * nw + x) * 3;
                dst[o] = (byte)(r / n); dst[o + 1] = (byte)(g / n); dst[o + 2] = (byte)(b / n);
            }
        }
        return (dst, nw, nh);
    }
}

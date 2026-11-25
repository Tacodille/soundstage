// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Soundstage.Capture;
using Soundstage.Workflow;

namespace Soundstage.Poses;

/// <summary>One pose to render: the content hash its thumbnail is cached under, and the name shown in the progress line.</summary>
public sealed record ThumbItem(string Hash, string Name, string Path);

/// <summary>
/// The thumbnail cache: one small PNG per pose file, named by the SHA-256 of the pose file's bytes (the same key the tag store uses),
/// so renaming or moving a pose keeps its picture. Written through a temp file so a crash never leaves half a picture.
/// </summary>
public sealed class ThumbnailCache
{
    public const string FolderName = "pose-thumbnails";

    public ThumbnailCache(string folder) => Folder = folder;

    public string Folder { get; }

    public string PathFor(string hash) => System.IO.Path.Combine(Folder, hash + ".png");

    public bool Has(string hash) => hash.Length > 0 && File.Exists(PathFor(hash));

    public byte[]? Read(string hash)
    {
        try { return Has(hash) ? File.ReadAllBytes(PathFor(hash)) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Save(string hash, byte[] png)
    {
        Directory.CreateDirectory(Folder);
        var tmp = PathFor(hash) + ".tmp";
        File.WriteAllBytes(tmp, png);
        File.Move(tmp, PathFor(hash), overwrite: true);
    }

    /// <summary>How many of these hashes already have a picture.</summary>
    public int CountHits(IEnumerable<string?> hashes) => hashes.Count(h => !string.IsNullOrEmpty(h) && Has(h));
}

/// <summary>A decoded picture as raw 8-bit B, G, R, A rows with no padding.</summary>
public sealed record Raster(int Width, int Height, byte[] Bgra);

/// <summary>Turns a full-screen capture into the small square picture the shelf and the tagger use. Pure maths on <see cref="Raster"/>, plus PNG in and out.</summary>
public static class ThumbnailImage
{
    public const int Size = 256;

    /// <summary>A pixel counts as "not background" when any colour channel is this far from the background colour.</summary>
    public const int Difference = 28;

    public static Raster? Load(string path)
    {
        try
        {
            using var bmp = new Bitmap(path);
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[bmp.Width * bmp.Height * 4];
                for (var y = 0; y < bmp.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * bmp.Width * 4, bmp.Width * 4);
                return new Raster(bmp.Width, bmp.Height, bytes);
            }
            finally { bmp.UnlockBits(data); }
        }
        catch (Exception) { return null; }
    }

    public static byte[] EncodePng(Raster r)
    {
        using var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < r.Height; y++)
                Marshal.Copy(r.Bgra, y * r.Width * 4, data.Scan0 + y * data.Stride, r.Width * 4);
        }
        finally { bmp.UnlockBits(data); }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>
    /// The box around everything that differs from the background, where the background is the average colour of the picture's outer ring.
    /// A row or column needs at least two differing pixels to count, so a stray speck does not stretch the box. Null when nothing differs.
    /// </summary>
    public static (int X, int Y, int W, int H)? ActorBounds(Raster r)
    {
        int w = r.Width, h = r.Height;
        if (w < 8 || h < 8) return null;
        long sb = 0, sg = 0, sr = 0, n = 0;
        void Take(int x, int y) { var i = (y * w + x) * 4; sb += r.Bgra[i]; sg += r.Bgra[i + 1]; sr += r.Bgra[i + 2]; n++; }
        for (var x = 0; x < w; x++) { Take(x, 0); Take(x, h - 1); }
        for (var y = 1; y < h - 1; y++) { Take(0, y); Take(w - 1, y); }
        int bb = (int)(sb / n), bg = (int)(sg / n), br = (int)(sr / n);

        var rowCount = new int[h];
        var colCount = new int[w];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                if (Math.Abs(r.Bgra[i] - bb) > Difference || Math.Abs(r.Bgra[i + 1] - bg) > Difference || Math.Abs(r.Bgra[i + 2] - br) > Difference)
                {
                    rowCount[y]++;
                    colCount[x]++;
                }
            }
        int top = Array.FindIndex(rowCount, c => c >= 2);
        int bottom = Array.FindLastIndex(rowCount, c => c >= 2);
        int left = Array.FindIndex(colCount, c => c >= 2);
        int right = Array.FindLastIndex(colCount, c => c >= 2);
        if (top < 0 || left < 0 || bottom < top || right < left) return null;
        return (left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// A square window around the box with a little air on every side, slid back inside the picture if it overhangs. When the box is missing or
    /// under 1% of the picture the window is the middle of the picture instead (a capture with no actor in it still gives a thumbnail).
    /// </summary>
    public static (int X, int Y, int Side) SquareWindow(Raster r, (int X, int Y, int W, int H)? box, out string note)
    {
        var maxSide = Math.Min(r.Width, r.Height);
        if (box is not { } b || (long)b.W * b.H * 100 < (long)r.Width * r.Height)
        {
            note = "no actor found, centre crop";
            var side = maxSide;
            return ((r.Width - side) / 2, (r.Height - side) / 2, side);
        }
        note = "cropped to the actor";
        var want = (int)Math.Ceiling(Math.Max(b.W, b.H) * 1.12);
        var s = Math.Clamp(want, Math.Min(32, maxSide), maxSide);
        var cx = b.X + b.W / 2;
        var cy = b.Y + b.H / 2;
        var x = Math.Clamp(cx - s / 2, 0, r.Width - s);
        var y = Math.Clamp(cy - s / 2, 0, r.Height - s);
        return (x, y, s);
    }

    /// <summary>Area-average the square window down (or up) to <paramref name="size"/> pixels square.</summary>
    public static Raster Resample(Raster r, int x0, int y0, int side, int size)
    {
        var outBytes = new byte[size * size * 4];
        for (var oy = 0; oy < size; oy++)
        {
            var sy0 = y0 + (int)((long)oy * side / size);
            var sy1 = Math.Max(sy0 + 1, y0 + (int)((long)(oy + 1) * side / size));
            for (var ox = 0; ox < size; ox++)
            {
                var sx0 = x0 + (int)((long)ox * side / size);
                var sx1 = Math.Max(sx0 + 1, x0 + (int)((long)(ox + 1) * side / size));
                long b = 0, g = 0, rr = 0, a = 0, count = 0;
                for (var sy = sy0; sy < sy1 && sy < r.Height; sy++)
                    for (var sx = sx0; sx < sx1 && sx < r.Width; sx++)
                    {
                        var i = (sy * r.Width + sx) * 4;
                        b += r.Bgra[i]; g += r.Bgra[i + 1]; rr += r.Bgra[i + 2]; a += r.Bgra[i + 3]; count++;
                    }
                if (count == 0) count = 1;
                var o = (oy * size + ox) * 4;
                outBytes[o] = (byte)(b / count); outBytes[o + 1] = (byte)(g / count); outBytes[o + 2] = (byte)(rr / count); outBytes[o + 3] = 255;
            }
        }
        return new Raster(size, size, outBytes);
    }

    /// <summary>The whole job for one capture: load, find the actor, crop, shrink, encode. Null (with the reason) when the file cannot be read.</summary>
    public static byte[]? Make(string capturePath, out string note)
    {
        var raster = Load(capturePath);
        if (raster == null) { note = "could not read the capture"; return null; }
        var (x, y, side) = SquareWindow(raster, ActorBounds(raster), out note);
        return EncodePng(Resample(raster, x, y, side, Size));
    }
}

/// <summary>The plain Scene the thumbnails are rendered in: the High key Scene's lights without the coloured edge light, a mid-grey sweep, and no look, camera, pose or atmosphere.</summary>
public static class ThumbnailStage
{
    public const string SceneId = "pose-thumbnails";

    public static Scene Build(IEnumerable<Scene> catalogue)
    {
        var all = catalogue.ToList();
        var basis = all.FirstOrDefault(s => s.Id == "high-key") ?? all.FirstOrDefault(s => s.Lights.Count > 0)
                    ?? throw new InvalidOperationException("There is no Scene to base the thumbnail stage on.");
        var scene = basis.Clone();
        scene.Id = SceneId;
        scene.Name = "Pose thumbnails (plain grey)";
        scene.Category = "Mine";
        scene.Tags = new List<string>();
        scene.Backdrop = null;
        scene.Atmosphere = null;
        scene.Camera = null;
        scene.Look = null;
        scene.Pose = null;
        scene.Animation = null;
        scene.Set = new SceneSet { Kind = "sweep", ColorRole = "", Color = new[] { 0.5f, 0.5f, 0.5f }, SweepWidth = 8f };
        scene.Lights.RemoveAll(l => l.Role is "kicker" or "rim" or "bg");
        if (scene.Lights.Count == 0) scene.Lights.AddRange(basis.Clone().Lights.Take(1));
        return scene;
    }
}

public enum ThumbState { Idle, Hiding, Settling, Capturing }

/// <summary>
/// Renders each listed pose on the GPose actor in the plain stage, captures it through the capture service, crops it and caches the result.
/// Tick-driven like the contact sheet, because posing has to happen on the game's frame. The caller supplies <c>begin</c> (remember the pose and
/// stage, put the plain stage up) and <c>end</c> (put both back); <c>end</c> runs on every way out. A pose that will not load is skipped and
/// counted; a failed capture stops the job, because the next ones would fail the same way.
/// </summary>
public sealed class ThumbnailJob
{
    private const long HideGiveUpMs = 1500;
    private const int MinFrames = 2;

    private readonly UiHider hider;
    private readonly ThumbnailCache cache;
    private readonly Func<string, CancellationToken, Task<CaptureResult>> capture;
    private readonly Func<bool> inGpose;
    private readonly Func<long> nowMs;
    private readonly Func<int> settleMs;
    private readonly Func<int> hideSettleMs;
    private readonly Func<string?> begin;
    private readonly Func<ThumbItem, bool> apply;
    private readonly Action end;
    private readonly Action<string> log;

    private IReadOnlyList<ThumbItem> items = Array.Empty<ThumbItem>();
    private int index;
    private string tempDir = "";
    private long phaseStartMs;
    private int frames;
    private Task<CaptureResult>? running;
    private CancellationTokenSource? cancel;

    public ThumbnailJob(UiHider hider, ThumbnailCache cache, Func<string, CancellationToken, Task<CaptureResult>> capture, Func<bool> inGpose,
        Func<long> nowMs, Func<int> settleMs, Func<int> hideSettleMs, Func<string?> begin, Func<ThumbItem, bool> apply, Action end, Action<string> log)
    {
        this.hider = hider;
        this.cache = cache;
        this.capture = capture;
        this.inGpose = inGpose;
        this.nowMs = nowMs;
        this.settleMs = settleMs;
        this.hideSettleMs = hideSettleMs;
        this.begin = begin;
        this.apply = apply;
        this.end = end;
        this.log = log;
    }

    public ThumbState State { get; private set; } = ThumbState.Idle;
    public bool Busy => State != ThumbState.Idle;
    public string Status { get; private set; } = "";
    public int Total => items.Count;
    public int Position => Math.Min(index, items.Count);
    public int Made { get; private set; }
    public int Skipped { get; private set; }

    /// <summary>Starts the job. Items that already have a picture are left out here, so a second press only does what is missing.</summary>
    public bool Start(IReadOnlyList<ThumbItem> wanted)
    {
        if (Busy) { Status = "Already running."; return false; }
        if (!inGpose()) { Status = "Thumbnails are made in GPose, on your own character."; return false; }
        var todo = wanted.Where(i => i.Hash.Length > 0 && !cache.Has(i.Hash)).ToList();
        if (todo.Count == 0) { Status = wanted.Count == 0 ? "No poses to render." : "Every pose in that list already has a thumbnail."; return false; }
        items = todo;
        index = 0;
        Made = 0;
        Skipped = 0;
        tempDir = Path.Combine(Path.GetTempPath(), "Soundstage-thumbs-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tempDir);
            var why = begin();
            if (why != null) { Status = why; end(); Cleanup(); return false; }
        }
        catch (Exception ex)
        {
            Status = "Could not start: " + ex.Message;
            try { end(); } catch (Exception) { }
            Cleanup();
            return false;
        }
        hider.Hide();
        phaseStartMs = nowMs();
        frames = 0;
        State = ThumbState.Hiding;
        Status = "Hiding the UI...";
        log($"[Soundstage/POSE] thumbnails start, {items.Count} pose(s)");
        return true;
    }

    public void Cancel(string why = "Cancelled.")
    {
        if (Busy) Finish(why);
    }

    public void Tick()
    {
        if (!Busy) return;
        try
        {
            if (!inGpose()) { Finish("Left GPose, so the thumbnails stopped."); return; }
            switch (State)
            {
                case ThumbState.Hiding:
                    frames++;
                    var waited = nowMs() - phaseStartMs;
                    var ready = waited >= hideSettleMs() && frames >= MinFrames && hider.AllSettled();
                    if (!ready && waited < HideGiveUpMs + hideSettleMs()) return;
                    ApplyCurrent();
                    return;

                case ThumbState.Settling:
                    frames++;
                    if (nowMs() - phaseStartMs < settleMs() || frames < MinFrames) return;
                    cancel = new CancellationTokenSource();
                    running = capture(Path.Combine(tempDir, $"{index + 1:0000}.png"), cancel.Token);
                    State = ThumbState.Capturing;
                    Status = $"Capturing {items[index].Name} ({index + 1} of {items.Count})...";
                    return;

                case ThumbState.Capturing:
                    if (running == null || !running.IsCompleted) return;
                    if (running.IsFaulted) { Finish($"The capture for '{items[index].Name}' failed: " + (running.Exception?.GetBaseException().Message ?? "unknown")); return; }
                    if (running.IsCanceled) { Finish($"The capture for '{items[index].Name}' was cancelled."); return; }
                    var r = running.Result;
                    running = null;
                    if (!r.Ok) { Finish($"Stopped at '{items[index].Name}': {r.Message}"); return; }
                    var png = ThumbnailImage.Make(r.Path, out var note);
                    try { File.Delete(r.Path); } catch (Exception) { }
                    if (png == null) { Skipped++; log($"[Soundstage/POSE] thumbnail for '{items[index].Name}' skipped: {note}"); }
                    else
                    {
                        cache.Save(items[index].Hash, png);
                        Made++;
                        log($"[Soundstage/POSE] thumbnail for '{items[index].Name}' saved ({note})");
                    }
                    index++;
                    if (index >= items.Count) { Finish($"Made {Made} thumbnail(s)" + (Skipped > 0 ? $", skipped {Skipped}" : "") + "."); return; }
                    ApplyCurrent();
                    return;
            }
        }
        catch (Exception ex)
        {
            Finish("The thumbnails failed: " + ex.Message);
        }
    }

    /// <summary>Poses the actor with the next item, skipping any that will not load, and moves on to settling.</summary>
    private void ApplyCurrent()
    {
        while (index < items.Count)
        {
            bool ok;
            try { ok = apply(items[index]); }
            catch (Exception ex) { ok = false; log($"[Soundstage/POSE] '{items[index].Name}' threw while loading: {ex.Message}"); }
            if (ok)
            {
                phaseStartMs = nowMs();
                frames = 0;
                State = ThumbState.Settling;
                Status = $"Settling {items[index].Name} ({index + 1} of {items.Count})...";
                return;
            }
            Skipped++;
            log($"[Soundstage/POSE] '{items[index].Name}' would not load, skipped");
            index++;
        }
        Finish($"Made {Made} thumbnail(s)" + (Skipped > 0 ? $", skipped {Skipped}" : "") + ".");
    }

    private void Finish(string status)
    {
        try { cancel?.Cancel(); } catch (Exception) { }
        var notes = new List<string>();
        try { hider.Restore(); } catch (Exception ex) { notes.Add("UI: " + ex.Message); }
        try { end(); } catch (Exception ex) { notes.Add("pose and stage: " + ex.Message); }
        cancel = null;
        running = null;
        State = ThumbState.Idle;
        Cleanup();
        var extra = new List<string>(notes);
        if (hider.Problems.Count > 0) extra.Add("could not put back " + string.Join(", ", hider.Problems));
        Status = extra.Count > 0 ? status + " Restore problems: " + string.Join("; ", extra) + "." : status;
        log("[Soundstage/POSE] thumbnails finished: " + Status);
    }

    private void Cleanup()
    {
        try { if (tempDir.Length > 0 && Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (Exception) { }
        tempDir = "";
    }
}

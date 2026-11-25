// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Soundstage.Look;

namespace Soundstage.Capture;

/// <summary>Route "addon": the ReShade add-on's <c>shot</c> command writes the back buffer (after effects) to the path we choose.</summary>
public sealed class AddonShotRoute : ICaptureRoute
{
    private readonly ReShadePipe pipe;
    public AddonShotRoute(ReShadePipe pipe) => this.pipe = pipe;

    public string Id => "addon";
    public string Label => "ReShade add-on";

    public async Task<RouteResult> CaptureAsync(string path, CancellationToken cancel)
    {
        string line;
        try { line = ReShadeProtocol.Shot(path); }
        catch (ArgumentException ex) { return RouteResult.Fail(ex.Message); }
        var reply = await pipe.SendAsync(line, cancel).ConfigureAwait(false);
        return reply.Ok ? RouteResult.Success(reply.Text) : RouteResult.Fail(reply.Text);
    }
}

/// <summary>
/// Route "key": sends the screenshot key to the game, then takes the newest image that appears in the screenshot folder.
/// Whatever the game wrote (often a JPG or BMP) is converted to PNG.
/// </summary>
public sealed class KeyScreenshotRoute : ICaptureRoute
{
    private readonly Func<string> folder;
    private readonly Func<int> key;
    private readonly Action<int> sendKey;

    public KeyScreenshotRoute(Func<string> folder, Func<int> key, Action<int>? sendKey = null)
    {
        this.folder = folder;
        this.key = key;
        this.sendKey = sendKey ?? NativeKeys.Tap;
    }

    public string Id => "key";
    public string Label => "Game screenshot key";

    public async Task<RouteResult> CaptureAsync(string path, CancellationToken cancel)
    {
        var dir = folder();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return RouteResult.Fail("the screenshot folder was not found: " + dir);
        var before = DateTime.UtcNow.AddMilliseconds(-50);
        sendKey(key());
        while (!cancel.IsCancellationRequested)
        {
            await Task.Delay(120, cancel).ConfigureAwait(false);
            var fresh = Newest(dir, before);
            if (fresh == null) continue;
            await Task.Delay(150, cancel).ConfigureAwait(false);   // let the game finish writing it
            try
            {
                using var src = new Bitmap(fresh);
                src.Save(path, ImageFormat.Png);
                return RouteResult.Success(Path.GetFileName(fresh));
            }
            catch (Exception) { /* still being written: look again */ }
        }
        return RouteResult.Fail("no new screenshot appeared in " + dir);
    }

    public static string? Newest(string dir, DateTime afterUtc)
    {
        string? best = null;
        var bestTime = afterUtc;
        foreach (var f in Directory.EnumerateFiles(dir))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp")) continue;
            var t = File.GetLastWriteTimeUtc(f);
            if (t > bestTime) { best = f; bestTime = t; }
        }
        return best;
    }
}

/// <summary>Route "window": copies the game window's client area off the screen. Last resort: it includes anything drawn on top, Dalamud included.</summary>
public sealed class WindowCaptureRoute : ICaptureRoute
{
    public string Id => "window";
    public string Label => "Window capture";

    public Task<RouteResult> CaptureAsync(string path, CancellationToken cancel)
    {
        try
        {
            var hwnd = Process.GetCurrentProcess().MainWindowHandle;
            if (hwnd == IntPtr.Zero) return Task.FromResult(RouteResult.Fail("the game window handle was not found"));
            if (!NativeKeys.GetClientRect(hwnd, out var rc)) return Task.FromResult(RouteResult.Fail("could not read the window size"));
            var origin = new NativeKeys.POINT();
            if (!NativeKeys.ClientToScreen(hwnd, ref origin)) return Task.FromResult(RouteResult.Fail("could not find the window on screen"));
            var w = rc.Right - rc.Left;
            var h = rc.Bottom - rc.Top;
            if (w < 2 || h < 2) return Task.FromResult(RouteResult.Fail("the window is minimised"));
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(origin.X, origin.Y, 0, 0, new Size(w, h));
            bmp.Save(path, ImageFormat.Png);
            return Task.FromResult(RouteResult.Success($"{w}x{h}"));
        }
        catch (Exception ex) { return Task.FromResult(RouteResult.Fail(ex.Message)); }
    }
}

/// <summary>The few user32 calls the routes and the UI hider need.</summary>
public static class NativeKeys
{
    public const int VkScrollLock = 0x91;

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT point);

    /// <summary>Presses and releases a key. The game has focus while it is being played, so it lands there.</summary>
    public static void Tap(int vk)
    {
        keybd_event((byte)vk, 0, 0, UIntPtr.Zero);
        keybd_event((byte)vk, 0, 2, UIntPtr.Zero);   // KEYEVENTF_KEYUP
    }
}

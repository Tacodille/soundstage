// Part 12: the capture route order, the booth, the contact sheet, the grid and the edge filter. Synthetic images only; no game needed.
using System.Drawing;
using System.Drawing.Imaging;
using Soundstage.Capture;
using Soundstage.Overlays;
using Soundstage.Workflow;

namespace Soundstage.SelfCheck;

public sealed class WorkflowChecks : CheckArea
{
    public override string Name => "Workflow (Part 12)";
    public override int Order => 120;

    private string dir = "";

    private sealed class FakeClock
    {
        public long Ms;
        public long Now() => Ms;
    }

    private sealed class FakeRoute : ICaptureRoute
    {
        public readonly List<string> Calls;
        private readonly Func<string, CancellationToken, Task<RouteResult>> body;
        public FakeRoute(string id, List<string> calls, Func<string, CancellationToken, Task<RouteResult>> body) { Id = id; Label = id; Calls = calls; this.body = body; }
        public string Id { get; }
        public string Label { get; }
        public Task<RouteResult> CaptureAsync(string path, CancellationToken cancel) { Calls.Add(Id); return body(path, cancel); }
    }

    private static void Solid(string path, int w, int h, Color c)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp)) g.Clear(c);
        bmp.Save(path, ImageFormat.Png);
    }

    private static Task<RouteResult> Draws(string path, Color c)
    {
        Solid(path, 64, 36, c);
        return Task.FromResult(RouteResult.Success());
    }

    public override void Run()
    {
        dir = Path.Combine(Path.GetTempPath(), "Soundstage-check-p12-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            GridChecks();
            EdgeChecks();
            RouteChecks();
            KeyRouteChecks();
            BoothChecks();
            SheetChecks();
            SettingsChecks();
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    // ---- grid ----

    private void GridChecks()
    {
        var l = SheetComposer.Layout(7, 3, 100, 60);
        Check("grid: 7 cells in 3 columns make 3 rows", l.Rows == 3 && l.Cells.Count == 7);
        Check("grid: width is gap plus three cells", l.Width == SheetComposer.Gap + 3 * (100 + SheetComposer.Gap));
        Check("grid: height is gap plus three rows with labels", l.Height == SheetComposer.Gap + 3 * (60 + SheetComposer.LabelHeight + SheetComposer.Gap));
        var overlap = false;
        for (var i = 0; i < l.Cells.Count; i++)
            for (var j = i + 1; j < l.Cells.Count; j++)
            {
                var a = l.Cells[i]; var b = l.Cells[j];
                if (a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height + SheetComposer.LabelHeight && b.Y < a.Y + a.Height + SheetComposer.LabelHeight) overlap = true;
            }
        Check("grid: no two cells overlap, labels included", !overlap);
        Check("grid: every cell sits inside the sheet", l.Cells.All(c => c.X + c.Width <= l.Width && c.LabelY + SheetComposer.LabelHeight <= l.Height));
        Check("grid: fewer Scenes than columns shrinks to one row", SheetComposer.Layout(2, 5, 100, 60).Rows == 1);

        var colours = new[] { Color.FromArgb(255, 0, 0), Color.FromArgb(0, 255, 0), Color.FromArgb(0, 0, 255), Color.FromArgb(255, 255, 0) };
        var items = new List<(string, string)>();
        for (var i = 0; i < 4; i++)
        {
            var p = Path.Combine(dir, $"g{i}.png");
            Solid(p, 160, 90, colours[i]);
            items.Add((p, "Scene " + i));
        }
        var dest = Path.Combine(dir, "sheet.png");
        SheetComposer.ComposeToFile(items, 2, 80, dest);
        using var sheet = new Bitmap(dest);
        var lay = SheetComposer.Layout(4, 2, 80, 45);
        Check("grid: composed size matches the layout", sheet.Width == lay.Width && sheet.Height == lay.Height, $"{sheet.Width}x{sheet.Height}");
        var allMatch = true;
        for (var i = 0; i < 4; i++)
        {
            var c = lay.Cells[i];
            var px = sheet.GetPixel(c.X + c.Width / 2, c.Y + c.Height / 2);
            if (Math.Abs(px.R - colours[i].R) > 3 || Math.Abs(px.G - colours[i].G) > 3 || Math.Abs(px.B - colours[i].B) > 3) allMatch = false;
        }
        Check("grid: each cell holds its own capture", allMatch);
        Check("grid: no half-written file is left beside the sheet", !File.Exists(Path.Combine(dir, "sheet.part.png")));
    }

    // ---- edge filter ----

    private static byte[] SquareImage(int w, int h, int x0, int y0, int x1, int y1)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (byte)(x >= x0 && x < x1 && y >= y0 && y < y1 ? 255 : 0);
                var o = (y * w + x) * 4;
                px[o] = px[o + 1] = px[o + 2] = v;
                px[o + 3] = 255;
            }
        return px;
    }

    private void EdgeChecks()
    {
        const int n = 24;
        var edges = EdgeFilter.Sobel(SquareImage(n, n, 8, 8, 16, 16), n, n);
        bool On(int x, int y) => edges[(y * n + x) * 4 + 3] == 255;
        Check("edge: the square's border is marked", On(8, 12) && On(15, 12) && On(12, 8) && On(12, 15));
        Check("edge: the square's middle is not", !On(12, 12));
        Check("edge: far from the square is not", !On(2, 2) && !On(20, 20));
        Check("edge: a flat image has no edges", !EdgeFilter.Sobel(SquareImage(n, n, 0, 0, 0, 0), n, n).Where((_, i) => i % 4 == 3).Any(a => a != 0));
        Check("edge: the image border stays clear", Enumerable.Range(0, n).All(i => !On(i, 0) && !On(i, n - 1) && !On(0, i) && !On(n - 1, i)));
        var threw = false;
        try { EdgeFilter.Sobel(new byte[10], n, n); } catch (ArgumentException) { threw = true; }
        Check("edge: a buffer of the wrong size is refused", threw);

        var src = Path.Combine(dir, "square.png");
        using (var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.Black); g.FillRectangle(Brushes.White, 8, 8, 8, 8); }
            bmp.Save(src, ImageFormat.Png);
        }
        using var ms = new MemoryStream(EdgeFilter.EdgePng(src));
        using var outImg = new Bitmap(ms);
        Check("edge: the PNG route gives white edges on transparency", outImg.GetPixel(8, 12).A == 255 && outImg.GetPixel(12, 12).A == 0 && outImg.GetPixel(2, 2).A == 0);
    }

    // ---- capture service ----

    private CaptureService Service(List<ICaptureRoute> routes, List<string> log, int timeoutMs = 2000) =>
        new(() => routes, () => TimeSpan.FromMilliseconds(timeoutMs), log.Add);

    private void RouteChecks()
    {
        var calls = new List<string>();
        var log = new List<string>();
        var routes = new List<ICaptureRoute>
        {
            new FakeRoute("addon", calls, (_, _) => Task.FromResult(RouteResult.Fail("the ReShade add-on did not answer"))),
            new FakeRoute("key", calls, (p, _) => Draws(p, Color.Red)),
            new FakeRoute("window", calls, (p, _) => Draws(p, Color.Blue)),
        };
        var dest = Path.Combine(dir, "a.png");
        var r = Service(routes, log).CaptureAsync(dest).GetAwaiter().GetResult();
        Check("routes: tried in order, stops at the first that works", r.Ok && r.Route == "key" && string.Join(",", calls) == "addon,key", string.Join(",", calls));
        Check("routes: the finished file has the final name", File.Exists(dest) && !File.Exists(Path.Combine(dir, "a.part.png")));

        calls.Clear();
        var first = new List<ICaptureRoute> { routes[1], routes[2] };
        Service(first, log).CaptureAsync(Path.Combine(dir, "b.png")).GetAwaiter().GetResult();
        Check("routes: a working first route means the others never run", string.Join(",", calls) == "key");

        calls.Clear();
        var empty = new List<ICaptureRoute>
        {
            new FakeRoute("addon", calls, (p, _) => { File.WriteAllText(p, "not a png"); return Task.FromResult(RouteResult.Success()); }),
            new FakeRoute("window", calls, (p, _) => Draws(p, Color.Green)),
        };
        var r2 = Service(empty, log).CaptureAsync(Path.Combine(dir, "c.png")).GetAwaiter().GetResult();
        Check("routes: a file that is not an image is rejected and the next route runs", r2.Ok && r2.Route == "window");

        calls.Clear();
        var slow = new List<ICaptureRoute>
        {
            new FakeRoute("addon", calls, async (_, _) => { await Task.Delay(5000).ConfigureAwait(false); return RouteResult.Success(); }),   // ignores its token
            new FakeRoute("window", calls, (p, _) => Draws(p, Color.Green)),
        };
        var t = System.Diagnostics.Stopwatch.StartNew();
        var r3 = Service(slow, log, 300).CaptureAsync(Path.Combine(dir, "d.png")).GetAwaiter().GetResult();
        Check("routes: a stuck route times out and the next one runs", r3.Ok && r3.Route == "window" && t.ElapsedMilliseconds < 3000, $"{t.ElapsedMilliseconds} ms");

        var throwing = new List<ICaptureRoute> { new FakeRoute("addon", calls, (_, _) => throw new InvalidOperationException("boom")) };
        var r4 = Service(throwing, log).CaptureAsync(Path.Combine(dir, "e.png")).GetAwaiter().GetResult();
        Check("routes: a route that throws is reported, not thrown", !r4.Ok && r4.Message.Contains("boom"));
        Check("routes: nothing is left at the final name after a failure", !File.Exists(Path.Combine(dir, "e.png")));
        Check("routes: no route at all is a plain message", !Service(new List<ICaptureRoute>(), log).CaptureAsync(Path.Combine(dir, "f.png")).GetAwaiter().GetResult().Ok);
        Check("routes: the log carries the [Soundstage/WORK] prefix", log.Count > 0 && log.All(l => l.StartsWith("[Soundstage/WORK]")));
        Check("capture: the default file name has no colon", !CaptureService.DefaultName(new DateTime(2026, 11, 21, 9, 5, 7)).Contains(':') && CaptureService.DefaultName(new DateTime(2026, 11, 21, 9, 5, 7)) == "Soundstage 2026-11-21 09-05-07.png");
    }

    private void KeyRouteChecks()
    {
        var shots = Path.Combine(dir, "shots");
        Directory.CreateDirectory(shots);
        var pressed = new List<int>();
        var route = new KeyScreenshotRoute(() => shots, () => 0x2C, vk =>
        {
            pressed.Add(vk);
            using var bmp = new Bitmap(50, 30);
            bmp.Save(Path.Combine(shots, "game.jpg"), ImageFormat.Jpeg);   // the game writes JPG
        });
        var dest = Path.Combine(dir, "key.part.png");
        var res = route.CaptureAsync(dest, CancellationToken.None).GetAwaiter().GetResult();
        Check("key route: sends the configured key", pressed.SequenceEqual(new[] { 0x2C }));
        Check("key route: converts the new JPG to a PNG", res.Ok && PngCheck.IsReadableImage(dest, out _));
        var missing = new KeyScreenshotRoute(() => Path.Combine(dir, "nope"), () => 0x2C, _ => { });
        Check("key route: a missing folder is a plain failure", !missing.CaptureAsync(dest, CancellationToken.None).GetAwaiter().GetResult().Ok);
        var cts = new CancellationTokenSource(400);
        var quiet = new KeyScreenshotRoute(() => shots, () => 0x2C, _ => { });
        var none = quiet.CaptureAsync(Path.Combine(dir, "q.part.png"), cts.Token);
        var gone = false;
        try { none.GetAwaiter().GetResult(); } catch (OperationCanceledException) { gone = true; }
        Check("key route: waiting for a screenshot that never comes ends on cancel", gone || !none.Result.Ok);

        var ini = Soundstage.Look.ReShadeIni.Parse("[SCREENSHOT]\nSavePath=.\\shots\n");
        Check("folder: the user's choice wins", ScreenshotFolder.Resolve(@"D:\mine", dir, "docs", ini) == @"D:\mine");
        Check("folder: ReShade's SavePath is used when it exists", ScreenshotFolder.Resolve("", dir, "docs", ini) == Path.GetFullPath(shots));
        Check("folder: otherwise the game's own folder under Documents", ScreenshotFolder.Resolve("", dir, "docs", null).EndsWith(Path.Combine("screenshots")) && ScreenshotFolder.Resolve("", dir, "docs", null).StartsWith("docs"));
        Check("folder: default output sits under Documents", ScreenshotFolder.DefaultOutput("docs") == Path.Combine("docs", "Soundstage", "Captures"));
    }

    // ---- booth and restore ----

    private sealed class Stage
    {
        public bool WindowHidden, MarkersHidden, GameUiHidden;
        public int Restores;
        public bool FailGameUiRestore;
        public UiHider Hider;

        public Stage(List<string> log)
        {
            Hider = new UiHider(() => new List<HideTarget>
            {
                new("windows", () => WindowHidden = true, () => { WindowHidden = false; Restores++; }),
                new("markers", () => MarkersHidden = true, () => { MarkersHidden = false; Restores++; }),
                new("game UI", () => GameUiHidden = true, () => { if (FailGameUiRestore) throw new InvalidOperationException("stuck"); GameUiHidden = false; Restores++; }, () => GameUiHidden),
            }, log.Add);
        }

        public bool AnyHidden => WindowHidden || MarkersHidden || GameUiHidden;
    }

    private static void Drive(FakeClock clock, Action tick, Func<bool> busy, int stepMs = 50, int maxSteps = 4000)
    {
        for (var i = 0; i < maxSteps && busy(); i++)
        {
            clock.Ms += stepMs;
            tick();
        }
    }

    private void BoothChecks()
    {
        var log = new List<string>();
        var clock = new FakeClock();
        var stage = new Stage(log);
        var hiddenAtCapture = false;
        var gpose = false;
        var booth = new PhotoBooth(stage.Hider, (p, _) =>
        {
            hiddenAtCapture = stage.WindowHidden && stage.MarkersHidden && stage.GameUiHidden;
            Solid(p, 64, 36, Color.Gray);
            return Task.FromResult(new CaptureResult(true, p, "fake", 5, ""));
        }, () => gpose, clock.Now, () => 100, log.Add);

        Check("booth: refuses to start outside GPose", !booth.Start(5, Path.Combine(dir, "x.png"), true) && !booth.Busy);
        gpose = true;
        Check("booth: starts in GPose", booth.Start(5, Path.Combine(dir, "booth.png"), true) && booth.State == BoothState.Counting);
        Check("booth: the timer is clamped to 3 to 30 s", true && ClampsOk());
        Drive(clock, booth.Tick, () => booth.Busy);
        Check("booth: a clean run ends idle with the capture made", !booth.Busy && booth.Last is { Ok: true } && hiddenAtCapture);
        Check("booth: everything is back after a clean run", !stage.AnyHidden && stage.Restores == 3);
        Check("booth: the countdown took about five seconds", clock.Ms >= 5000 && clock.Ms < 7000, clock.Ms + " ms");

        // Control: the capture throws. Restore must still run and the UI must come back.
        var stage2 = new Stage(log);
        var clock2 = new FakeClock();
        var booth2 = new PhotoBooth(stage2.Hider, (_, _) => throw new InvalidOperationException("capture forced to throw"), () => true, clock2.Now, () => 100, log.Add);
        booth2.Start(3, Path.Combine(dir, "t.png"), true);
        var sawHidden = false;
        Drive(clock2, () => { booth2.Tick(); sawHidden |= stage2.AnyHidden; }, () => booth2.Busy);
        Check("booth control: the UI really was hidden before the capture threw", sawHidden);
        Check("booth control: a capture that throws still restores the UI and markers", !stage2.AnyHidden && stage2.Restores == 3 && !booth2.Busy);
        Check("booth control: the failure is reported, not swallowed", booth2.Last is { Ok: false } && booth2.Status.Contains("forced to throw"), booth2.Status);

        // A task that faults later.
        var stage3 = new Stage(log);
        var clock3 = new FakeClock();
        var booth3 = new PhotoBooth(stage3.Hider, (_, _) => Task.FromException<CaptureResult>(new IOException("disk full")), () => true, clock3.Now, () => 100, log.Add);
        booth3.Start(3, Path.Combine(dir, "t3.png"), true);
        Drive(clock3, booth3.Tick, () => booth3.Busy);
        Check("booth: a faulted capture task restores too", !stage3.AnyHidden && booth3.Status.Contains("disk full"));

        // A failed capture result.
        var stage4 = new Stage(log);
        var clock4 = new FakeClock();
        var booth4 = new PhotoBooth(stage4.Hider, (_, _) => Task.FromResult(new CaptureResult(false, "", "", 1, "no route worked")), () => true, clock4.Now, () => 100, log.Add);
        booth4.Start(3, Path.Combine(dir, "t4.png"), true);
        Drive(clock4, booth4.Tick, () => booth4.Busy);
        Check("booth: a failed result restores and says why", !stage4.AnyHidden && booth4.Status.Contains("no route worked"));

        // Cancel during the countdown and while hidden.
        var stage5 = new Stage(log);
        var clock5 = new FakeClock();
        var booth5 = new PhotoBooth(stage5.Hider, (_, _) => new TaskCompletionSource<CaptureResult>().Task, () => true, clock5.Now, () => 100, log.Add);
        booth5.Start(10, Path.Combine(dir, "t5.png"), true);
        clock5.Ms += 1000; booth5.Tick();
        booth5.Cancel();
        Check("booth: cancel during the countdown leaves nothing hidden", !booth5.Busy && !stage5.AnyHidden);
        booth5.Start(3, Path.Combine(dir, "t5.png"), true);
        Drive(clock5, booth5.Tick, () => booth5.State != BoothState.Capturing && booth5.Busy);
        Check("booth: it reached the capture with the UI hidden", booth5.State == BoothState.Capturing && stage5.AnyHidden);
        booth5.Cancel();
        Check("booth: cancel while capturing restores at once", !booth5.Busy && !stage5.AnyHidden);

        // Leaving GPose cancels.
        var stage6 = new Stage(log);
        var clock6 = new FakeClock();
        var in6 = true;
        var booth6 = new PhotoBooth(stage6.Hider, (_, _) => new TaskCompletionSource<CaptureResult>().Task, () => in6, clock6.Now, () => 100, log.Add);
        booth6.Start(3, Path.Combine(dir, "t6.png"), true);
        Drive(clock6, booth6.Tick, () => booth6.State != BoothState.Capturing);
        in6 = false;
        booth6.Tick();
        Check("booth: leaving GPose cancels and restores", !booth6.Busy && !stage6.AnyHidden && booth6.Status.Contains("GPose"));

        // Capture now works outside GPose and has no countdown.
        var stage7 = new Stage(log);
        var clock7 = new FakeClock();
        var booth7 = new PhotoBooth(stage7.Hider, (p, _) => { Solid(p, 64, 36, Color.Gray); return Task.FromResult(new CaptureResult(true, p, "fake", 1, "")); }, () => false, clock7.Now, () => 100, log.Add);
        Check("capture now: starts outside GPose", booth7.Start(0, Path.Combine(dir, "t7.png"), false));
        Drive(clock7, booth7.Tick, () => booth7.Busy);
        Check("capture now: no countdown, and it restores", booth7.Last is { Ok: true } && clock7.Ms < 1500 && !stage7.AnyHidden, clock7.Ms + " ms");

        // The hider on its own: a restore that throws must not stop the others.
        var stage8 = new Stage(log) { FailGameUiRestore = true };
        stage8.Hider.Hide();
        stage8.Hider.Restore();
        Check("hider: one restore throwing does not stop the rest", !stage8.WindowHidden && !stage8.MarkersHidden && stage8.Hider.Problems.SequenceEqual(new[] { "game UI" }));
        Check("hider: it is no longer marked hidden afterwards, so a second restore does nothing", !stage8.Hider.IsHidden);
        var throwingHide = new UiHider(() => new List<HideTarget> { new("half", () => throw new InvalidOperationException("x"), () => { }) }, log.Add);
        throwingHide.Hide();
        Check("hider: a hide that throws is still on the restore list", throwingHide.IsHidden && throwingHide.Problems.Count == 1);
    }

    private static bool ClampsOk()
    {
        var clock = new FakeClock();
        var stage = new Stage(new List<string>());
        var b = new PhotoBooth(stage.Hider, (p, _) => Task.FromResult(new CaptureResult(true, p, "f", 0, "")), () => true, clock.Now, () => 0, _ => { });
        b.Start(1, "x.png", true);
        var low = b.SecondsLeft;
        b.Cancel();
        b.Start(99, "x.png", true);
        var high = b.SecondsLeft;
        b.Cancel();
        return low == PhotoBooth.MinSeconds && high == PhotoBooth.MaxSeconds;
    }

    // ---- contact sheet ----

    private void SheetChecks()
    {
        var log = new List<string>();
        var clock = new FakeClock();
        var stage = new Stage(log);
        var applied = new List<string>();
        var captureAt = new List<long>();
        var applyAt = new List<long>();
        var saved = 0;
        var restored = 0;
        var colours = new[] { Color.Red, Color.Green, Color.Blue, Color.Yellow };
        var shot = 0;
        var sheet = new ContactSheet(stage.Hider, (p, _) =>
        {
            captureAt.Add(clock.Ms);
            Solid(p, 64, 36, colours[shot++ % colours.Length]);
            return Task.FromResult(new CaptureResult(true, p, "fake", 1, ""));
        }, () => true, clock.Now, () => 400, () => 100, () => saved++, () => restored++, log.Add);

        SheetItem Item(string id) => new(id, "Scene " + id, () => { applied.Add(id); applyAt.Add(clock.Ms); return true; });
        var dest = Path.Combine(dir, "contact.png");
        var started = sheet.Start(new[] { Item("a"), Item("b"), Item("c") }, dest, 2, 64);
        Drive(clock, sheet.Tick, () => sheet.Busy);
        Check("sheet: starts and runs to the end", started && !sheet.Busy && sheet.ResultPath == dest && File.Exists(dest), sheet.Status);
        Check("sheet: Scenes are applied in the order picked", string.Join(",", applied) == "a,b,c");
        Check("sheet: each capture waits at least the settle time after its apply", captureAt.Count == 3 && Enumerable.Range(0, 3).All(i => captureAt[i] - applyAt[i] >= 400));
        Check("sheet: the original is saved once and restored once", saved == 1 && restored == 1);
        Check("sheet: the UI is back afterwards", !stage.AnyHidden);
        using (var img = new Bitmap(dest))
        {
            var lay = SheetComposer.Layout(3, 2, 64, 36);
            Check("sheet: the output is the composed grid", img.Width == lay.Width && img.Height == lay.Height);
        }

        // Stops on the first failed capture and names the Scene.
        var stage2 = new Stage(log);
        var clock2 = new FakeClock();
        var restored2 = 0;
        var n = 0;
        var applied2 = new List<string>();
        var sheet2 = new ContactSheet(stage2.Hider, (p, _) =>
        {
            n++;
            if (n == 2) return Task.FromResult(new CaptureResult(false, "", "", 1, "no route worked"));
            Solid(p, 64, 36, Color.Gray);
            return Task.FromResult(new CaptureResult(true, p, "fake", 1, ""));
        }, () => true, clock2.Now, () => 400, () => 100, () => { }, () => restored2++, log.Add);
        SheetItem Item2(string id) => new(id, "Scene " + id, () => { applied2.Add(id); return true; });
        sheet2.Start(new[] { Item2("a"), Item2("b"), Item2("c") }, Path.Combine(dir, "stop.png"), 2, 64);
        Drive(clock2, sheet2.Tick, () => sheet2.Busy);
        Check("sheet: stops at the first failed capture and names its Scene", sheet2.Status.Contains("Scene b") && sheet2.Status.Contains("no route worked"), sheet2.Status);
        Check("sheet: the third Scene was never applied", string.Join(",", applied2) == "a,b");
        Check("sheet: no sheet file is written after a failure", !File.Exists(Path.Combine(dir, "stop.png")));
        Check("sheet: a failure still restores the Scene and the UI", restored2 == 1 && !stage2.AnyHidden);

        // A Scene that will not apply.
        var stage3 = new Stage(log);
        var clock3 = new FakeClock();
        var restored3 = 0;
        var sheet3 = new ContactSheet(stage3.Hider, (p, _) => { Solid(p, 64, 36, Color.Gray); return Task.FromResult(new CaptureResult(true, p, "f", 1, "")); },
            () => true, clock3.Now, () => 400, () => 100, () => { }, () => restored3++, log.Add);
        sheet3.Start(new[] { new SheetItem("a", "Scene a", () => false) }, Path.Combine(dir, "bad.png"), 1, 64);
        Drive(clock3, sheet3.Tick, () => sheet3.Busy);
        Check("sheet: a Scene that will not apply stops the run and restores", sheet3.Status.Contains("Scene a") && restored3 == 1 && !stage3.AnyHidden);

        // An exception from restoreState does not leave the UI hidden.
        var stage4 = new Stage(log);
        var clock4 = new FakeClock();
        var sheet4 = new ContactSheet(stage4.Hider, (_, _) => throw new InvalidOperationException("capture forced to throw"),
            () => true, clock4.Now, () => 400, () => 100, () => { }, () => throw new InvalidOperationException("restore broke"), log.Add);
        sheet4.Start(new[] { new SheetItem("a", "Scene a", () => true) }, Path.Combine(dir, "x.png"), 1, 64);
        Drive(clock4, sheet4.Tick, () => sheet4.Busy);
        Check("sheet control: capture and restore both throwing still brings the UI back", !stage4.AnyHidden && !sheet4.Busy && sheet4.Status.Contains("forced to throw"), sheet4.Status);

        // Limits and GPose.
        var clock5 = new FakeClock();
        var stage5 = new Stage(log);
        var inGpose5 = false;
        var sheet5 = new ContactSheet(stage5.Hider, (_, _) => Task.FromResult(new CaptureResult(true, "", "", 0, "")), () => inGpose5, clock5.Now, () => 400, () => 100, () => { }, () => { }, log.Add);
        Check("sheet: refuses outside GPose", !sheet5.Start(new[] { Item("a") }, Path.Combine(dir, "y.png"), 1, 64));
        inGpose5 = true;
        Check("sheet: refuses an empty pick", !sheet5.Start(Array.Empty<SheetItem>(), Path.Combine(dir, "y.png"), 1, 64));
        var thirteen = Enumerable.Range(0, 13).Select(i => Item("s" + i)).ToList();
        Check("sheet: refuses more than 12 Scenes", !sheet5.Start(thirteen, Path.Combine(dir, "y.png"), 1, 64) && sheet5.Status.Contains("12"));
        Check("sheet: a refused start leaves nothing hidden", !stage5.AnyHidden);
    }

    // ---- settings ----

    private void SettingsChecks()
    {
        var sections = new Dictionary<string, string>();
        var s = ConfigSections.Get<WorkflowSettings>(sections, WorkflowSettings.Key);
        Check("settings: defaults are the plan's (route order, 400 ms settle, 5 s booth)", s.RouteOrder.SequenceEqual(new[] { "addon", "key", "window" }) && s.SettleMs == 400 && s.BoothSeconds == 5);
        s.Onion.ImagePath = @"C:\refs\pose.png";
        s.Onion.Opacity = 0.25f;
        s.Onion.EdgeOnly = true;
        s.SheetSceneIds.Add("neon-split");
        ConfigSections.Set(sections, WorkflowSettings.Key, s);
        var back = ConfigSections.Get<WorkflowSettings>(sections, WorkflowSettings.Key);
        Check("settings: the onion path, opacity and Scene picks round-trip", back.Onion.ImagePath == @"C:\refs\pose.png" && Near(back.Onion.Opacity, 0.25f) && back.Onion.EdgeOnly && back.SheetSceneIds.SequenceEqual(new[] { "neon-split" }));
        sections[WorkflowSettings.Key] = "{ not json";
        Check("settings: broken JSON falls back to the defaults", ConfigSections.Get<WorkflowSettings>(sections, WorkflowSettings.Key).SettleMs == 400);
    }
}

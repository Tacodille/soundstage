// Part 16: the face analyser's protocol, installer, camera and light functions. No game, no Python and no network: the helper is a fake
// process, the downloads are fake fetchers over tiny zips, and the light function is fed pictures painted from the Lambert model plus one
// recorded helper reply.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed class FaceChecks : CheckArea
{
    public override string Name => "Face (Part 16)";
    public override int Order => 160;

    public override void Run()
    {
        Manifest();
        Parser();
        Installer();
        Analyser();
        Camera();
        Light();
        Recorded();
    }

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    // ---- the pinned list ----

    private void Manifest()
    {
        var path = Path.Combine(root, "Face", "face-pack.json");
        var json = File.ReadAllText(path);
        var m = FacePackManifest.TryParse(json, out var problem);
        Check("face pack: the shipped list parses", m != null, problem);
        if (m == null) return;
        Check("face pack: one Python, one model, wheels for the rest", m.Files.Count(f => f.Kind == "python") == 1 && m.Files.Count(f => f.Kind == "model") == 1 && m.Files.Count(f => f.Kind == "wheel") >= 5, $"{m.Files.Count} files");
        Check("face pack: every entry has a SHA-256 and a size", m.Files.All(f => f.Sha256.Length == 64 && f.Size > 0));
        Check("face pack: nothing is fetched from outside the allowed sites", m.Files.All(f => FacePackManifest.AllowedHosts.Contains(new Uri(f.Url).Host)));
        Check("face pack: the model is Google's published file", m.Files.Single(f => f.Kind == "model").Url.StartsWith("https://storage.googleapis.com/mediapipe-models/", StringComparison.Ordinal));
        Check("face pack: licences are recorded", m.Licences.ContainsKey("face_landmarker.task") && m.Licences.ContainsKey("mediapipe"));

        // Control: each defect must be refused.
        var bad = json.Replace("https://www.python.org", "http://www.python.org");
        Check("face pack control: an http address is refused", FacePackManifest.TryParse(bad, out _) == null);
        bad = json.Replace("https://storage.googleapis.com", "https://example.com");
        Check("face pack control: an unknown host is refused", FacePackManifest.TryParse(bad, out _) == null);
        bad = json.Replace(m.Files[1].Sha256, "zz" + m.Files[1].Sha256[2..]);
        Check("face pack control: a hash that is not hex is refused", FacePackManifest.TryParse(bad, out _) == null);
        Check("face pack control: rubbish is refused, not thrown", FacePackManifest.TryParse("{not json", out _) == null);
    }

    // ---- the helper's reply ----

    private void Parser()
    {
        var good = "{\"id\":3,\"ok\":true,\"yawDeg\":12.5,\"pitchDeg\":-4,\"rollDeg\":1,\"confidence\":0.8,\"blendshapes\":{\"jawOpen\":0.5,\"_neutral\":0.1},\"landmarks\":[[0.1,0.2,0.3],[0.4,0.5,0.6]]}";
        var r = FaceReplyParser.Parse(good);
        Check("reply: a good reply is available with its angles", r.Available && Near(r.YawDeg, 12.5f) && Near(r.PitchDeg, -4f) && Near(r.RollDeg, 1f), r.Reason);
        Check("reply: blendshapes and landmarks come through", Near(r.Blend("jawOpen"), 0.5f) && r.LandmarkCount == 2 && Near(r.Point(1).X, 0.4f));
        var none = FaceReplyParser.Parse("{\"id\":3,\"ok\":false,\"reason\":\"No face found in the picture.\"}");
        Check("reply: no face is unavailable with the helper's sentence", !none.Available && none.Reason == "No face found in the picture.");
        Check("reply: rubbish is unavailable, not thrown", !FaceReplyParser.Parse("not json at all").Available && !FaceReplyParser.Parse("[1,2]").Available);
        Check("reply: a reply with no angles is unavailable", !FaceReplyParser.Parse("{\"ok\":true,\"blendshapes\":{}}").Available);
        Check("reply control: confidence is clamped to 0..1", FaceReplyParser.Parse(good.Replace("0.8", "7")).Confidence == 1f);
    }

    // ---- the installer ----

    private sealed class FakeFetcher : IFileFetcher
    {
        public readonly Dictionary<string, byte[]> Files = new();
        public readonly List<string> Fetched = new();

        public void Fetch(string url, string destination, Action<long> bytesSoFar, CancellationToken ct)
        {
            Fetched.Add(url);
            if (!Files.TryGetValue(url, out var data)) throw new IOException("404 " + url);
            File.WriteAllBytes(destination, data);
            bytesSoFar(data.Length);
        }
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in entries)
            {
                var e = z.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        return ms.ToArray();
    }

    private static (FacePackManifest m, FakeFetcher f) FakePack(string wheelEntry = "fakepkg/__init__.py")
    {
        var f = new FakeFetcher();
        var files = new List<FacePackFile>();
        void Add(string name, string kind, byte[] data)
        {
            var url = "https://files.pythonhosted.org/x/" + name;
            f.Files[url] = data;
            files.Add(new FacePackFile { Name = name, Version = "1", File = name, Url = url, Sha256 = Hex(data), Size = data.Length, Kind = kind });
        }
        Add("python-embed.zip", "python", Zip(("python.exe", "exe"), ("python314._pth", "python314.zip\n"), ("python314.zip", "stdlib")));
        Add("fakepkg-1-py3-none-any.whl", "wheel", Zip((wheelEntry, "x = 1\n"), ("fakepkg-1.dist-info/METADATA", "m")));
        Add("face_landmarker.task", "model", Encoding.ASCII.GetBytes("model-bytes"));
        return (new FacePackManifest { FormatVersion = 1, HelperVersion = "t1", Files = files }, f);
    }

    private void Installer()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "soundstage-face-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var helper = new Dictionary<string, string> { ["helper.py"] = "print('h')\n", ["face_core.py"] = "x=1\n" };
            var (m, f) = FakePack();
            var layout = new FacePackLayout(Path.Combine(tmp, "ok"));
            var prog = new FaceInstallProgress();
            var logs = new List<string>();
            var r = FacePackInstaller.Install(m, layout, f, helper, prog, logs.Add, CancellationToken.None);
            Check("install: a verified pack installs", r.Ok, r.Reason);
            Check("install: python, packages, model and helper land where the layout says", File.Exists(layout.PythonExe) && File.Exists(Path.Combine(layout.SitePackages, "fakepkg", "__init__.py")) && File.Exists(layout.ModelFile) && File.Exists(layout.HelperScript));
            Check("install: the Python search path lists site-packages", File.ReadAllText(Directory.GetFiles(layout.PythonDir, "python*._pth")[0]).Contains("site-packages"));
            Check("install: marker says installed for this version only", layout.IsInstalled("t1") && !layout.IsInstalled("t2"));
            Check("install: downloads folder is cleared afterwards", !Directory.Exists(layout.DownloadDir));
            Check("install: progress finished and not running", !prog.Running && prog.DoneBytes == m.TotalDownloadBytes, $"{prog.DoneBytes} of {m.TotalDownloadBytes}");

            // Control: tampered download.
            var (m2, f2) = FakePack();
            var wheelUrl = m2.Files.First(x => x.Kind == "wheel").Url;
            f2.Files[wheelUrl] = Zip(("evil/__init__.py", "x"));   // right name, wrong bytes
            var layout2 = new FacePackLayout(Path.Combine(tmp, "tampered"));
            var logs2 = new List<string>();
            var r2 = FacePackInstaller.Install(m2, layout2, f2, helper, new FaceInstallProgress(), logs2.Add, CancellationToken.None);
            Check("install control: a file that fails its hash stops the install", !r2.Ok && r2.Reason.Contains("pinned hash"), r2.Reason);
            Check("install control: nothing is installed after a hash failure", !layout2.IsInstalled("t1") && !Directory.Exists(layout2.PythonDir) && !File.Exists(Path.Combine(layout2.DownloadDir, "fakepkg-1-py3-none-any.whl")));

            // Control: a wheel that tries to write outside its folder.
            var (m3, f3) = FakePack("../escaped.txt");
            var layout3 = new FacePackLayout(Path.Combine(tmp, "slip"));
            var r3 = FacePackInstaller.Install(m3, layout3, f3, helper, new FaceInstallProgress(), _ => { }, CancellationToken.None);
            Check("install control: a zip entry outside the folder is refused", !r3.Ok && !File.Exists(Path.Combine(layout3.Root, "python", "escaped.txt")) && !File.Exists(Path.Combine(layout3.PythonDir, "escaped.txt")), r3.Reason);

            // Control: a download that errors.
            var (m4, f4) = FakePack();
            f4.Files.Remove(m4.Files[0].Url);
            var r4 = FacePackInstaller.Install(m4, new FacePackLayout(Path.Combine(tmp, "offline")), f4, helper, new FaceInstallProgress(), _ => { }, CancellationToken.None);
            Check("install control: a failed download is a result, not an exception", !r4.Ok && r4.Reason.Length > 0, r4.Reason);

            // Re-run of a finished install fetches nothing it already has? (downloads are cleared, so it fetches again; this only checks it still succeeds)
            f.Fetched.Clear();
            var again = FacePackInstaller.Install(m, layout, f, helper, new FaceInstallProgress(), _ => { }, CancellationToken.None);
            Check("install: installing over an installed pack works", again.Ok && layout.IsInstalled("t1"), again.Reason);

            Check("remove: the helper folder goes", FacePackInstaller.Remove(layout, out _) && !Directory.Exists(layout.Root) && !layout.IsInstalled("t1"));
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (IOException) { }
        }
    }

    // ---- the analyser over a fake helper process ----

    private sealed class FakeProc : IHelperProcess
    {
        private readonly Queue<string?> replies;
        public bool Exited;
        public bool ThrowOnWrite;
        public readonly List<string> Written = new();
        public FakeProc(params string?[] lines) => replies = new Queue<string?>(lines);
        public bool HasExited => Exited;
        public void WriteLine(string line)
        {
            if (ThrowOnWrite) throw new IOException("pipe closed");
            Written.Add(line);
        }
        public string? ReadLine(int timeoutMs) => replies.Count > 0 ? replies.Dequeue() : null;
        public void Kill() => Exited = true;
        public void Dispose() => Exited = true;
    }

    private const string Ready = "{\"ready\":true,\"version\":\"1\"}";
    private const string Good = "{\"id\":1,\"ok\":true,\"yawDeg\":20,\"pitchDeg\":5,\"rollDeg\":-2,\"confidence\":0.5,\"blendshapes\":{\"jawOpen\":0.2},\"landmarks\":[]}";

    private void Analyser()
    {
        var png = Path.Combine(Path.GetTempPath(), "soundstage-face-check-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(png, new byte[] { 1 });
        try
        {
            var logs = new List<string>();
            var p = new FakeProc(Ready, Good);
            using var a = new HelperFaceAnalyser(() => true, () => p, logs.Add, 0);
            var r = a.Analyse(png);
            Check("analyser: a good reply is returned as a result", r.Available && Near(r.YawDeg, 20f), r.Reason);
            Check("analyser: the request names the picture and an id", p.Written.Count == 1 && p.Written[0].Contains("\"id\":1") && p.Written[0].Contains("soundstage-face-check"), p.Written.FirstOrDefault() ?? "");

            Check("analyser control: not installed is unavailable, no process started", !new HelperFaceAnalyser(() => false, () => throw new InvalidOperationException("must not launch"), _ => { }).Analyse(png).Available);
            Check("analyser control: a missing picture is unavailable", !new HelperFaceAnalyser(() => true, () => new FakeProc(Ready), _ => { }, 0).Analyse(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".png")).Available);
            Check("analyser control: a helper that cannot start is unavailable, not thrown", !new HelperFaceAnalyser(() => true, () => null, _ => { }, 0).Analyse(png).Available);
            Check("analyser control: a launch that throws is unavailable, not thrown", !new HelperFaceAnalyser(() => true, () => throw new InvalidOperationException("boom"), _ => { }, 0).Analyse(png).Available);

            var hang = new HelperFaceAnalyser(() => true, () => new FakeProc((string?)null), _ => { }, 0).Analyse(png);
            Check("analyser control: a helper that never says ready is unavailable", !hang.Available && hang.Reason.Contains("did not start"), hang.Reason);
            var notReady = new HelperFaceAnalyser(() => true, () => new FakeProc("{\"ready\":false,\"reason\":\"no mediapipe\"}"), _ => { }, 0).Analyse(png);
            Check("analyser control: a helper that fails to load says why", !notReady.Available && notReady.Reason.Contains("no mediapipe"), notReady.Reason);

            var dies = new FakeProc(Ready, (string?)null);
            var launches = 0;
            var recover = new HelperFaceAnalyser(() => true, () => { launches++; return launches == 1 ? dies : new FakeProc(Ready, Good.Replace("\"id\":1", "\"id\":2")); }, _ => { }, 0);
            var first = recover.Analyse(png);
            var second = recover.Analyse(png);
            Check("analyser control: a helper that stops answering is dropped and reported", !first.Available, first.Reason);
            Check("analyser: the next call starts a fresh helper and works", second.Available && launches == 2, $"launches {launches}");

            var broken = new FakeProc(Ready) { ThrowOnWrite = true };
            var bw = new HelperFaceAnalyser(() => true, () => broken, _ => { }, 0).Analyse(png);
            Check("analyser control: a closed pipe is unavailable, not thrown", !bw.Available, bw.Reason);

            var noFace = new HelperFaceAnalyser(() => true, () => new FakeProc(Ready, "{\"id\":1,\"ok\":false,\"reason\":\"No face found in the picture.\"}"), _ => { }, 0).Analyse(png);
            Check("analyser: no face is unavailable with the helper's words", !noFace.Available && noFace.Reason.Contains("No face"), noFace.Reason);
            Check("analyser: a stale reply for an earlier id is skipped", new HelperFaceAnalyser(() => true, () => new FakeProc(Ready, Good.Replace("\"id\":1", "\"id\":99"), Good), _ => { }, 0).Analyse(png).Available);
        }
        finally { File.Delete(png); }
    }

    // ---- head angle to camera ----

    private static FaceResult Face(float yaw, float pitch, float roll = 0f, float conf = 0.8f) =>
        new() { Available = true, YawDeg = yaw, PitchDeg = pitch, RollDeg = roll, Confidence = conf };

    private void Camera()
    {
        var s = new FaceSettings();
        var front = CameraFromFace.Solve(Face(0, 0), s);
        Check("camera: a straight-on face is azimuth 0 and no height change", front.Ok && Near(front.AzimuthDeg, 0f) && Near(front.OffsetY, 0f));
        var turned = CameraFromFace.Solve(Face(30, 0), s);
        Check("camera: a face turned to the picture's right puts the camera on the subject's right", Near(turned.AzimuthDeg, 30f) && turned.Text.Contains("right"), turned.Text);
        var left = CameraFromFace.Solve(Face(-45, 0), s);
        Check("camera: the other way is negative", Near(left.AzimuthDeg, -45f) && left.Text.Contains("left"), left.Text);
        var up = CameraFromFace.Solve(Face(0, 20), s);
        Check("camera: a lifted chin drops the camera", up.OffsetY < -0.3f && up.Text.Contains("lower"), $"{up.OffsetY}");
        var down = CameraFromFace.Solve(Face(0, -20), s);
        Check("camera: a lowered chin raises the camera", down.OffsetY > 0.3f && Near(down.OffsetY, -up.OffsetY, 1e-4f));
        Check("camera: yaw past the limit is clamped and says so", Near(CameraFromFace.Solve(Face(120, 0), s).AzimuthDeg, CameraFromFace.MaxAzimuth) && CameraFromFace.Solve(Face(120, 0), s).Text.Contains("past what the camera"));
        Check("camera: the height trim never leaves the slider's range", Near(CameraFromFace.Solve(Face(0, 80), s).OffsetY, -CameraFromFace.MaxOffset));
        Check("camera: pitch bias shifts the height", Near(CameraFromFace.Solve(Face(0, 0), new FaceSettings { PitchBiasDeg = 10 }).OffsetY, CameraFromFace.Solve(Face(0, 10), s).OffsetY, 1e-4f));
        Check("camera: roll is reported, not applied", CameraFromFace.Solve(Face(0, 0, 9), s).Text.Contains("cannot roll"));
        Check("camera: a small face is flagged as rough", CameraFromFace.Solve(Face(10, 0, 0, 0.1f), s).Text.Contains("rough"));
        var none = CameraFromFace.Solve(FaceResult.Unavailable("No face found."), s);
        Check("camera control: an unavailable face gives no camera move", !none.Ok && none.Text == "No face found.");
    }

    // ---- light from shading ----

    private static float[] Landmarks()
    {
        var lm = new float[478 * 3];
        for (var i = 0; i < 478; i++) { lm[i * 3] = 0.5f; lm[i * 3 + 1] = 0.5f; }
        void Set(int id, float x, float y) { lm[id * 3] = x; lm[id * 3 + 1] = y; }
        Set(234, 0.2f, 0.5f); Set(454, 0.8f, 0.5f);
        Set(205, 0.3f, 0.6f); Set(50, 0.32f, 0.58f); Set(425, 0.7f, 0.6f); Set(280, 0.68f, 0.58f);
        Set(10, 0.5f, 0.2f); Set(151, 0.5f, 0.3f); Set(152, 0.5f, 0.9f); Set(199, 0.5f, 0.85f);
        return lm;
    }

    private static FaceResult WithLandmarks(float[] lm) => new() { Available = true, Confidence = 0.8f, Landmarks = lm };

    private static byte Encode(float linear)
    {
        linear = Math.Clamp(linear, 0f, 1f);
        var c = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1f / 2.4f) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(c * 255f), 0, 255);
    }

    private static FaceImage Painted(float[] lm, float left, float right, float forehead, float chin, int size = 400)
    {
        var rgba = new byte[size * size * 4];
        for (var i = 0; i < size * size; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = 40; rgba[i * 4 + 3] = 255; }
        void Paint(float px, float py, float lin)
        {
            int cx = (int)(px * size), cy = (int)(py * size);
            var b = Encode(lin);
            for (var y = cy - 20; y <= cy + 20; y++)
                for (var x = cx - 20; x <= cx + 20; x++)
                {
                    if (x < 0 || y < 0 || x >= size || y >= size) continue;
                    var i = (y * size + x) * 4;
                    rgba[i] = rgba[i + 1] = rgba[i + 2] = b;
                }
        }
        // Each region's squares sit on the landmarks the estimator samples, so a region is one flat value whatever the face layout.
        float X(int id) => lm[id * 3];
        float Y(int id) => lm[id * 3 + 1];
        foreach (var id in LightFromShading.CheekLeft) Paint(X(id), Y(id), left);
        foreach (var id in LightFromShading.CheekRight) Paint(X(id), Y(id), right);
        Paint((X(LightFromShading.ForeheadTop) + X(LightFromShading.ForeheadLow)) / 2, (Y(LightFromShading.ForeheadTop) + Y(LightFromShading.ForeheadLow)) / 2, forehead);
        Paint(X(LightFromShading.ChinLow), Y(LightFromShading.ChinLow), chin);
        return new FaceImage(size, size, rgba);
    }

    private static (float L, float R) LambertH(float phiDeg, float thetaDeg)
    {
        float Lam(float a) => MathF.Max(0f, MathF.Cos(a * MathF.PI / 180f));
        return (Lam(phiDeg + thetaDeg), Lam(phiDeg - thetaDeg));
    }

    private void Light()
    {
        var s = new FaceSettings();
        var lm = Landmarks();
        var face = WithLandmarks(lm);

        foreach (var phi in new[] { -45f, -25f, 0f, 20f, 45f })
        {
            var (l, r) = LambertH(phi, s.CheekAngleDeg);
            var est = LightFromShading.Estimate(face, Painted(lm, 0.02f + 0.9f * l, 0.02f + 0.9f * r, 0.5f, 0.5f), s);
            // The 0.02 floor and 0.9 gain keep both cheeks above the dark limit; a gain does not change the ratio's sign, so the tolerance is 2 degrees.
            Check($"light: a key at {phi:0} degrees reads back within 2 degrees", est.Ok && MathF.Abs(est.AzimuthDeg - phi) <= 2f, est.Ok ? $"read {est.AzimuthDeg:0.0}" : est.Reason);
        }

        foreach (var el in new[] { -25f, 0f, 30f })
        {
            float Lam(float a) => MathF.Max(0f, MathF.Cos(a * MathF.PI / 180f));
            var top = Lam(el - s.ForeheadChinAngleDeg);
            var bottom = Lam(el + s.ForeheadChinAngleDeg);
            var est = LightFromShading.Estimate(face, Painted(lm, 0.5f, 0.5f, 0.02f + 0.9f * top, 0.02f + 0.9f * bottom), s);
            Check($"light: a key {el:0} degrees up reads back within 2 degrees", est.Ok && MathF.Abs(est.ElevationDeg - el) <= 2f, est.Ok ? $"read {est.ElevationDeg:0.0}" : est.Reason);
        }

        var sym = LightFromShading.Estimate(face, Painted(lm, 0.4f, 0.4f, 0.4f, 0.4f), s);
        Check("light: equal brightness reads as from the front", sym.Ok && MathF.Abs(sym.AzimuthDeg) < 0.5f && MathF.Abs(sym.ElevationDeg) < 0.5f && sym.Text.Contains("front"), sym.Text);
        var swapped = LightFromShading.Estimate(face, Painted(lm, 0.7f, 0.2f, 0.4f, 0.4f), s);
        Check("light control: a brighter left cheek gives a negative azimuth (mirror of brighter right)", swapped.Ok && swapped.AzimuthDeg < -10f && LightFromShading.Estimate(face, Painted(lm, 0.2f, 0.7f, 0.4f, 0.4f), s).AzimuthDeg > 10f);
        var dark = LightFromShading.Estimate(face, Painted(lm, 0.0005f, 0.001f, 0.001f, 0.001f), s);
        Check("light control: a face too dark to read is refused with a reason", !dark.Ok && dark.Reason.Contains("dark"), dark.Reason);
        var edge = Landmarks(); edge[205 * 3] = 0.01f;
        Check("light control: a cheek point at the picture edge is refused, not read", !LightFromShading.Estimate(WithLandmarks(edge), Painted(lm, 0.4f, 0.4f, 0.4f, 0.4f), s).Ok);
        Check("light control: no landmarks is refused", !LightFromShading.Estimate(new FaceResult { Available = true }, Painted(lm, 0.4f, 0.4f, 0.4f, 0.4f), s).Ok);
        Check("light control: an unavailable face is refused with its reason", LightFromShading.Estimate(FaceResult.Unavailable("No face found."), Painted(lm, 0.4f, 0.4f, 0.4f, 0.4f), s).Reason == "No face found.");
        var widerCheek = LightFromShading.Estimate(face, Painted(lm, 0.3f, 0.6f, 0.4f, 0.4f), new FaceSettings { CheekAngleDeg = 60 });
        var narrower = LightFromShading.Estimate(face, Painted(lm, 0.3f, 0.6f, 0.4f, 0.4f), new FaceSettings { CheekAngleDeg = 30 });
        Check("light: a larger cheek angle reads the same difference as a smaller light angle", widerCheek.AzimuthDeg < narrower.AzimuthDeg && widerCheek.AzimuthDeg > 0f);
    }

    // ---- one recorded reply from the real helper ----

    private void Recorded()
    {
        var path = Path.Combine(TestRoot, "TestData", "face", "portrait-a.reply.json");
        var r = FaceReplyParser.Parse(File.ReadAllText(path));
        Check("recorded: the real helper reply parses (52 blendshapes, 478 landmarks)", r.Available && r.Blendshapes.Count == 52 && r.LandmarkCount == 478, r.Reason);
        Check("recorded: its angles are the ones the helper printed", Near(r.YawDeg, -9.962f, 0.01f) && Near(r.PitchDeg, -11.221f, 0.01f) && Near(r.RollDeg, -5.372f, 0.01f));
        Check("recorded: the model's blendshape names are the ARKit set plus _neutral", r.Blendshapes.ContainsKey("_neutral") && r.Blendshapes.ContainsKey("jawOpen") && r.Blendshapes.ContainsKey("eyeBlinkLeft") && r.Blendshapes.ContainsKey("mouthSmileRight"));
        Check("recorded: cheek landmarks sit on the sides they should (picture left has the smaller x)", r.Point(LightFromShading.EdgeLeft).X < r.Point(LightFromShading.EdgeRight).X && r.Point(205).X < r.Point(425).X && r.Point(10).Y < r.Point(152).Y);

        var cam = CameraFromFace.Solve(r, new FaceSettings());
        Check("recorded: camera advice follows the angles (yaw -10 is azimuth -10, chin lowered raises the camera)", cam.Ok && Near(cam.AzimuthDeg, -9.962f, 0.01f) && cam.OffsetY > 0.2f, $"az {cam.AzimuthDeg:0.0} y {cam.OffsetY:0.00}");

        // The cheek, forehead and chin brightness the helper's landmarks gave on the real picture (measured by the end-to-end run on 21/11/2026).
        var img = Painted(r.Landmarks.ToArray(), 0.297f, 0.476f, 0.479f, 0.392f, 800);
        var est = LightFromShading.Estimate(r, img, new FaceSettings());
        Check("recorded: the measured cheek and forehead brightness gives the recorded light estimate", est.Ok && Near(est.AzimuthDeg, 15.0f, 1.5f) && Near(est.ElevationDeg, 10.0f, 1.5f), est.Ok ? $"az {est.AzimuthDeg:0.0} el {est.ElevationDeg:0.0}" : est.Reason);
        Check("recorded: the words for the head angle are plain", HeadAngleWords.Describe(r).Contains("toward the picture's left") && HeadAngleWords.Describe(r).Contains("chin lowered"), HeadAngleWords.Describe(r));
    }
}

// Part 18.2: the body model's download and the helper's pose request, over a fake fetcher and a fake helper process.
using System.Security.Cryptography;
using System.Text.Json;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed partial class BodyChecks
{
    private sealed class PoseFetcher : IFileFetcher
    {
        public byte[] Data = Array.Empty<byte>();
        public int Calls;
        public void Fetch(string url, string destination, Action<long> bytesSoFar, CancellationToken ct) { Calls++; File.WriteAllBytes(destination, Data); bytesSoFar(Data.Length); }
    }

    private sealed class PoseProc : IHelperProcess
    {
        private readonly Queue<string?> replies;
        public readonly List<string> Written = new();
        public PoseProc(params string?[] lines) => replies = new Queue<string?>(lines);
        public bool HasExited => false;
        public void WriteLine(string line) => Written.Add(line);
        public string? ReadLine(int timeoutMs) => replies.Count > 0 ? replies.Dequeue() : null;
        public void Kill() { }
        public void Dispose() { }
    }

    private void Pack()
    {
        var ready = "{\"ready\":true,\"version\":\"1\"}";
        using var rec = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRoot, "TestData", "body-recorded.json")));
        var reply = rec.RootElement.GetProperty("p1").GetRawText().Insert(1, "\"id\":1,");
        var img = Path.Combine(Path.GetTempPath(), "soundstage-body-check-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(img, new byte[] { 1 });
        var dir = Path.Combine(Path.GetTempPath(), "soundstage-bodypack-" + Guid.NewGuid().ToString("N"));
        try
        {
            // The helper request and reply.
            var p = new PoseProc(ready, reply);
            using var a = new HelperFaceAnalyser(() => true, () => p, _ => { }, 0, () => true);
            var r = a.AnalysePose(img);
            Check("pose protocol: a body reply comes back as landmarks and an outline", r.Available && r.World.Length == 33 && r.Silhouette is { Size: 64 }, r.Reason);
            Check("pose protocol: the request asks for pose mode and names the picture", p.Written.Count == 1 && p.Written[0].Contains("\"mode\":\"pose\"") && p.Written[0].Contains("\"id\":1"), p.Written.FirstOrDefault() ?? "");
            var f = new PoseProc(ready, "{\"id\":1,\"ok\":true,\"yawDeg\":1,\"pitchDeg\":2,\"rollDeg\":3,\"blendshapes\":{},\"landmarks\":[]}");
            using var b = new HelperFaceAnalyser(() => true, () => f, _ => { }, 0, () => true);
            Check("pose protocol (control): a plain face request carries no mode field, as before", b.Analyse(img).Available && !f.Written[0].Contains("mode"), f.Written.FirstOrDefault() ?? "");

            var launched = false;
            var none = new HelperFaceAnalyser(() => true, () => { launched = true; return new PoseProc(ready); }, _ => { }, 0, () => false);
            var nr = none.AnalysePose(img);
            Check("pose protocol (control): no body model means unavailable, with the way to get it, and no process", !nr.Available && nr.Reason.Contains("download") && !launched && !none.PoseCanTry, nr.Reason);
            Check("pose protocol (control): the face helper missing is unavailable too", !new HelperFaceAnalyser(() => false, () => null, _ => { }, 0, () => true).PoseCanTry);
            var said = new HelperFaceAnalyser(() => true, () => new PoseProc(ready, "{\"id\":1,\"ok\":false,\"reason\":\"No body found in the picture.\"}"), _ => { }, 0, () => true).AnalysePose(img);
            Check("pose protocol: the helper's own no-body answer is passed on", !said.Available && said.Reason.Contains("No body"), said.Reason);
            Check("pose protocol (control): a missing picture is unavailable, not thrown", !new HelperFaceAnalyser(() => true, () => new PoseProc(ready), _ => { }, 0, () => true).AnalysePose(img + ".nope").Available);
            Check("pose protocol (control): a helper that never answers is unavailable, not a hang", !new HelperFaceAnalyser(() => true, () => new PoseProc(ready, null), _ => { }, 0, () => true).AnalysePose(img).Available);

            // The download.
            var bytes = new byte[40_000];
            new Random(5).NextBytes(bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var json = File.ReadAllText(Path.Combine(root, "Face", "pose-pack.json"));
            var real = PosePackManifest.TryParse(json, out _)!;
            var m = PosePackManifest.TryParse(json.Replace(real.Sha256, hash).Replace(real.Size.ToString(), bytes.Length.ToString()), out var prob)!;
            Check("pose pack: a test list with its own hash and size parses", m != null, prob);
            if (m == null) return;

            var layout = new FacePackLayout(dir);
            var files = new Dictionary<string, string> { ["helper.py"] = "# helper\r\n", ["face_core.py"] = "# core\r\n" };
            var progress = new FaceInstallProgress();
            var fetch = new PoseFetcher { Data = bytes };

            var early = PosePackInstaller.Install(m, layout, fetch, files, progress, _ => { }, CancellationToken.None);
            Check("pose install (control): without the face helper it refuses and downloads nothing", !early.Ok && early.Reason.Contains("face helper") && fetch.Calls == 0, early.Reason);

            Directory.CreateDirectory(layout.PythonDir);
            File.WriteAllText(layout.PythonExe, "stand-in");
            File.WriteAllText(Path.Combine(layout.PythonDir, "keep.txt"), "x");
            var ok = PosePackInstaller.Install(m, layout, fetch, files, progress, _ => { }, CancellationToken.None);
            Check("pose install: a file with the right hash installs", ok.Ok && File.Exists(layout.PoseModelFile) && new FileInfo(layout.PoseModelFile).Length == bytes.Length, ok.Reason);
            Check("pose install: the marker names the hash, so Installed() is true", PosePackInstaller.Installed(m, layout));
            Check("pose install: the helper's scripts are rewritten with LF endings", File.ReadAllText(layout.HelperScript) == "# helper\n" && File.ReadAllText(Path.Combine(layout.HelperDir, "face_core.py")) == "# core\n");
            Check("pose install: the face helper's own files are left alone", File.Exists(Path.Combine(layout.PythonDir, "keep.txt")));
            Check("pose install: the download folder is clean afterwards", !File.Exists(Path.Combine(layout.DownloadDir, m.File)));
            Check("pose install (control): a pack with a different hash does not count as installed", !PosePackInstaller.Installed(real, layout));

            Check("pose install: the helper is started with the pose model once it is there", File.Exists(layout.PoseModelFile) && File.ReadAllText(Path.Combine(root, "Face", "HelperFaceAnalyser.cs")).Contains("--pose-model"));

            Check("pose remove: takes the body model away and leaves the face helper", PosePackInstaller.Remove(layout, out _) && !File.Exists(layout.PoseModelFile) && File.Exists(layout.PythonExe) && !PosePackInstaller.Installed(m, layout));

            fetch.Data = bytes.Take(bytes.Length - 1).Concat(new byte[] { (byte)(bytes[^1] ^ 1) }).ToArray();
            var bad = PosePackInstaller.Install(m, layout, fetch, files, progress, _ => { }, CancellationToken.None);
            Check("pose install (control): one flipped bit is refused", !bad.Ok && bad.Reason.Contains("pinned hash"), bad.Reason);
            Check("pose install (control): a refused file is deleted and nothing is installed", !File.Exists(layout.PoseModelFile) && !File.Exists(Path.Combine(layout.DownloadDir, m.File)) && !PosePackInstaller.Installed(m, layout));

            var cts = new CancellationTokenSource(); cts.Cancel();
            fetch.Data = bytes;
            Check("pose install: a cancelled download says so", PosePackInstaller.Install(m, layout, new CancelFetcher(), files, progress, _ => { }, cts.Token) is { Ok: false, Reason: "Cancelled." });
        }
        finally
        {
            try { File.Delete(img); } catch (IOException) { }
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    private sealed class CancelFetcher : IFileFetcher
    {
        public void Fetch(string url, string destination, Action<long> bytesSoFar, CancellationToken ct) => ct.ThrowIfCancellationRequested();
    }
}

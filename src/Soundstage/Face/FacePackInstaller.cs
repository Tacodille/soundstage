// No Dalamud types in this file.
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Soundstage.Face;

/// <summary>Fetches one file to disk. The real one is <see cref="HttpFileFetcher"/>; the self-check uses a fake.</summary>
public interface IFileFetcher
{
    /// <summary>Downloads <paramref name="url"/> to <paramref name="destination"/>, reporting bytes so far. Throws on failure.</summary>
    void Fetch(string url, string destination, Action<long> bytesSoFar, CancellationToken ct);
}

public sealed class HttpFileFetcher : IFileFetcher
{
    private static readonly HttpClient Http = MakeClient();

    private static HttpClient MakeClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Soundstage-face-helper/0.1");
        return c;
    }

    public void Fetch(string url, string destination, Action<long> bytesSoFar, CancellationToken ct)
    {
        using var resp = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).GetAwaiter().GetResult();
        resp.EnsureSuccessStatusCode();
        using var src = resp.Content.ReadAsStream(ct);
        using var dst = File.Create(destination);
        var buf = new byte[1 << 16];
        long total = 0;
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0)
        {
            dst.Write(buf, 0, n);
            total += n;
            bytesSoFar(total);
        }
    }
}

/// <summary>Progress the tab reads while the install runs on a background task.</summary>
public sealed class FaceInstallProgress
{
    private long done;
    public string Step { get; set; } = "";
    public long TotalBytes { get; set; }
    public long DoneBytes { get => Interlocked.Read(ref done); set => Interlocked.Exchange(ref done, value); }
    public bool Running { get; set; }
}

public sealed record FaceInstallResult(bool Ok, string Reason);

/// <summary>
/// Downloads the face helper pack: every file in the manifest, hash-checked against its pinned SHA-256 before it is used, then unpacked
/// under <see cref="FacePackLayout.Root"/>. Nothing is kept from a download that fails its hash. The picture never goes anywhere: this only
/// brings files IN.
/// </summary>
public static class FacePackInstaller
{
    /// <param name="helperFiles">The helper's own source files (helper.py, face_core.py) by file name, written beside the Python.</param>
    public static FaceInstallResult Install(FacePackManifest manifest, FacePackLayout layout, IFileFetcher fetcher,
        IReadOnlyDictionary<string, string> helperFiles, FaceInstallProgress progress, Action<string> log, CancellationToken ct)
    {
        try
        {
            progress.Running = true;
            progress.TotalBytes = manifest.TotalDownloadBytes;
            progress.DoneBytes = 0;
            Directory.CreateDirectory(layout.DownloadDir);
            long before = 0;
            var staging = layout.PythonDir + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            foreach (var f in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                progress.Step = $"Downloading {f.File}";
                var local = Path.Combine(layout.DownloadDir, f.File);
                if (!(File.Exists(local) && HashOf(local) == f.Sha256.ToLowerInvariant()))
                {
                    var baseline = before;
                    fetcher.Fetch(f.Url, local, n => progress.DoneBytes = baseline + n, ct);
                }
                before += f.Size;
                progress.DoneBytes = before;

                var got = HashOf(local);
                if (got != f.Sha256.ToLowerInvariant())
                {
                    File.Delete(local);
                    var why = $"{f.File} did not match its pinned hash, so it was thrown away and nothing was installed.";
                    log($"[Soundstage/FACE] {why} expected {f.Sha256} got {got}");
                    Cleanup(layout, staging);
                    return new FaceInstallResult(false, why);
                }
                log($"[Soundstage/FACE] {f.File} ok ({f.Size / 1048576.0:0.0} MB, sha256 verified).");
            }

            // All downloads verified. Unpack into a staging folder, then swap it in so a half-finished install never looks installed.
            progress.Step = "Unpacking";
            var py = manifest.Files.First(f => f.Kind == "python");
            SafeExtract(Path.Combine(layout.DownloadDir, py.File), staging);
            var site = Path.Combine(staging, "site-packages");
            Directory.CreateDirectory(site);
            foreach (var w in manifest.Files.Where(f => f.Kind == "wheel"))
                SafeExtract(Path.Combine(layout.DownloadDir, w.File), site);
            WritePth(staging);

            if (Directory.Exists(layout.PythonDir)) Directory.Delete(layout.PythonDir, true);
            Directory.Move(staging, layout.PythonDir);

            Directory.CreateDirectory(layout.ModelDir);
            var model = manifest.Files.First(f => f.Kind == "model");
            File.Copy(Path.Combine(layout.DownloadDir, model.File), layout.ModelFile, true);

            Directory.CreateDirectory(layout.HelperDir);
            foreach (var kv in helperFiles) File.WriteAllText(Path.Combine(layout.HelperDir, kv.Key), kv.Value.Replace("\r\n", "\n"));

            File.WriteAllText(layout.Marker, JsonSerializer.Serialize(new
            {
                helperVersion = manifest.HelperVersion,
                installedUtc = DateTime.UtcNow.ToString("o"),
                files = manifest.Files.Select(f => new { f.File, f.Sha256 }).ToArray(),
            }));
            Directory.Delete(layout.DownloadDir, true);
            progress.Step = "Done";
            return new FaceInstallResult(true, "");
        }
        catch (OperationCanceledException)
        {
            return new FaceInstallResult(false, "Cancelled.");
        }
        catch (Exception e)
        {
            log($"[Soundstage/FACE] install failed: {e}");
            return new FaceInstallResult(false, "The download did not finish: " + e.Message);
        }
        finally
        {
            progress.Running = false;
        }
    }

    /// <summary>Removes the whole helper folder. Safe when it is not there.</summary>
    public static bool Remove(FacePackLayout layout, out string reason)
    {
        try { if (Directory.Exists(layout.Root)) Directory.Delete(layout.Root, true); reason = ""; return true; }
        catch (Exception e) { reason = "Could not remove the face helper (is it running?): " + e.Message; return false; }
    }

    public static string HashOf(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    private static void Cleanup(FacePackLayout layout, string staging)
    {
        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch (IOException) { }
    }

    /// <summary>Unzips <paramref name="zip"/> into <paramref name="dest"/>, refusing any entry that would land outside it.</summary>
    public static void SafeExtract(string zip, string dest)
    {
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(dest, e.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException($"{Path.GetFileName(zip)} has an entry outside its folder ({e.FullName}).");
            if (e.FullName.EndsWith('/') || e.Name.Length == 0) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, true);
        }
    }

    /// <summary>The embeddable Python reads python3NN._pth for its search path and ignores the environment. Adds site-packages and this folder.</summary>
    private static void WritePth(string pythonDir)
    {
        var pth = Directory.GetFiles(pythonDir, "python*._pth").FirstOrDefault();
        if (pth == null) throw new IOException("The Python download has no ._pth file, so it cannot be pointed at its packages.");
        var zipName = Directory.GetFiles(pythonDir, "python*.zip").Select(Path.GetFileName).FirstOrDefault() ?? "python.zip";
        File.WriteAllText(pth, string.Join("\n", zipName, ".", "site-packages", "") );
    }
}

// No Dalamud types in this file.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Face;

/// <summary>The one extra file the body retarget needs: Google's pose model. Pinned like every face-helper file (URL, size, SHA-256).</summary>
public sealed class PosePackManifest
{
    public const string ManifestResource = "Soundstage.pose-pack.json";
    public const string AllowedHost = "storage.googleapis.com";

    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("licence")] public string Licence { get; set; } = "";

    public static PosePackManifest? TryParse(string json, out string problem)
    {
        problem = "";
        try
        {
            var m = JsonSerializer.Deserialize<PosePackManifest>(json);
            if (m == null) { problem = "The body model list is empty."; return null; }
            if (m.Sha256.Length != 64 || !m.Sha256.All(Uri.IsHexDigit)) { problem = "The pinned hash is not a SHA-256."; return null; }
            if (!Uri.TryCreate(m.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) { problem = "The address is not https."; return null; }
            if (!string.Equals(u.Host, AllowedHost, StringComparison.OrdinalIgnoreCase)) { problem = $"{u.Host} is not an allowed source."; return null; }
            if (m.File.Length == 0 || m.File.Contains('/') || m.File.Contains('\\') || m.File.Contains("..")) { problem = "The file name is not a plain name."; return null; }
            if (m.Kind != "pose-model") { problem = $"Unknown kind '{m.Kind}'."; return null; }
            if (m.Size <= 0) { problem = "The pinned size is missing."; return null; }
            return m;
        }
        catch (JsonException e) { problem = "The body model list could not be read: " + e.Message; return null; }
    }
}

/// <summary>
/// Downloads the body pose model into the face helper's model folder, hash-checked before it is used, and rewrites the helper's two scripts so an
/// older install learns the pose request. Needs the face helper installed first (it is the Python that runs the model). A file that fails its hash is
/// deleted and nothing changes.
/// </summary>
public static class PosePackInstaller
{
    public static bool Installed(PosePackManifest? m, FacePackLayout layout)
    {
        try
        {
            if (m == null || !File.Exists(layout.PoseModelFile) || !File.Exists(layout.PoseMarker)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(layout.PoseMarker));
            return doc.RootElement.TryGetProperty("sha256", out var h) && string.Equals(h.GetString(), m.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    public static FaceInstallResult Install(PosePackManifest m, FacePackLayout layout, IFileFetcher fetcher, IReadOnlyDictionary<string, string> helperFiles,
        FaceInstallProgress progress, Action<string> log, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(layout.PythonExe)) return new FaceInstallResult(false, "Download the face helper first (Face tab). The body model runs inside it.");
            progress.Running = true;
            progress.TotalBytes = m.Size;
            progress.DoneBytes = 0;
            progress.Step = $"Downloading {m.File}";
            Directory.CreateDirectory(layout.DownloadDir);
            var local = Path.Combine(layout.DownloadDir, m.File);
            if (!(File.Exists(local) && FacePackInstaller.HashOf(local) == m.Sha256.ToLowerInvariant()))
                fetcher.Fetch(m.Url, local, n => progress.DoneBytes = n, ct);
            ct.ThrowIfCancellationRequested();

            var got = FacePackInstaller.HashOf(local);
            if (got != m.Sha256.ToLowerInvariant())
            {
                File.Delete(local);
                var why = $"{m.File} did not match its pinned hash, so it was thrown away and nothing was installed.";
                log($"[Soundstage/FACE] {why} expected {m.Sha256} got {got}");
                return new FaceInstallResult(false, why);
            }
            log($"[Soundstage/FACE] {m.File} ok ({m.Size / 1048576.0:0.0} MB, sha256 verified).");

            Directory.CreateDirectory(layout.ModelDir);
            var tmp = layout.PoseModelFile + ".new";
            File.Copy(local, tmp, true);
            if (File.Exists(layout.PoseModelFile)) File.Delete(layout.PoseModelFile);
            File.Move(tmp, layout.PoseModelFile);

            Directory.CreateDirectory(layout.HelperDir);
            foreach (var kv in helperFiles) File.WriteAllText(Path.Combine(layout.HelperDir, kv.Key), kv.Value.Replace("\r\n", "\n"));

            File.WriteAllText(layout.PoseMarker, JsonSerializer.Serialize(new { name = m.Name, version = m.Version, sha256 = m.Sha256, installedUtc = DateTime.UtcNow.ToString("o") }));
            File.Delete(local);
            progress.Step = "Done";
            return new FaceInstallResult(true, "");
        }
        catch (OperationCanceledException) { return new FaceInstallResult(false, "Cancelled."); }
        catch (Exception e)
        {
            log($"[Soundstage/FACE] body model install failed: {e}");
            return new FaceInstallResult(false, "The download did not finish: " + e.Message);
        }
        finally { progress.Running = false; }
    }

    /// <summary>Removes the body model only. The face helper stays.</summary>
    public static bool Remove(FacePackLayout layout, out string reason)
    {
        try
        {
            if (File.Exists(layout.PoseModelFile)) File.Delete(layout.PoseModelFile);
            if (File.Exists(layout.PoseMarker)) File.Delete(layout.PoseMarker);
            reason = "";
            return true;
        }
        catch (Exception e) { reason = "Could not remove the body model (is the helper running?): " + e.Message; return false; }
    }
}

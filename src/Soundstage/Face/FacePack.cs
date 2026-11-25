// No Dalamud types in this file.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Soundstage.Face;

/// <summary>One file the face helper downloads: where from, how big, and the SHA-256 it must have. Written by tools/face/make_manifest.py.</summary>
public sealed class FacePackFile
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }

    /// <summary>"python" (the embeddable zip), "wheel" (unpacked into site-packages) or "model" (copied to the model folder).</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
}

public sealed class FacePackManifest
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("helperVersion")] public string HelperVersion { get; set; } = "";
    [JsonPropertyName("files")] public List<FacePackFile> Files { get; set; } = new();
    [JsonPropertyName("licences")] public Dictionary<string, string> Licences { get; set; } = new();

    public long TotalDownloadBytes => Files.Sum(f => f.Size);

    /// <summary>Hosts a download may come from. The hash is the real protection; this stops a bad manifest pointing somewhere odd.</summary>
    public static readonly string[] AllowedHosts = { "www.python.org", "files.pythonhosted.org", "storage.googleapis.com" };

    public static FacePackManifest? TryParse(string json, out string problem)
    {
        problem = "";
        try
        {
            var m = JsonSerializer.Deserialize<FacePackManifest>(json);
            if (m == null || m.Files.Count == 0) { problem = "The face helper list is empty."; return null; }
            foreach (var f in m.Files)
            {
                if (f.Sha256.Length != 64 || !f.Sha256.All(Uri.IsHexDigit)) { problem = $"{f.File}: the pinned hash is not a SHA-256."; return null; }
                if (!Uri.TryCreate(f.Url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps) { problem = $"{f.File}: the address is not https."; return null; }
                if (!AllowedHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase)) { problem = $"{f.File}: {u.Host} is not an allowed source."; return null; }
                if (f.File.Contains('/') || f.File.Contains('\\') || f.File.Contains("..")) { problem = $"{f.File}: not a plain file name."; return null; }
                if (f.Kind is not ("python" or "wheel" or "model")) { problem = $"{f.File}: unknown kind '{f.Kind}'."; return null; }
            }
            if (m.Files.Count(f => f.Kind == "python") != 1 || m.Files.Count(f => f.Kind == "model") != 1) { problem = "The list needs exactly one Python and one model."; return null; }
            return m;
        }
        catch (JsonException e) { problem = "The face helper list could not be read: " + e.Message; return null; }
    }
}

/// <summary>Where the downloaded helper lives under the plugin's config folder. One place so the installer, the analyser and the tab agree.</summary>
public sealed class FacePackLayout
{
    public FacePackLayout(string configDirectory) => Root = Path.Combine(configDirectory, "face-helper");

    public string Root { get; }
    public string PythonDir => Path.Combine(Root, "python");
    public string PythonExe => Path.Combine(PythonDir, "python.exe");
    public string SitePackages => Path.Combine(PythonDir, "site-packages");
    public string HelperDir => Path.Combine(Root, "helper");
    public string HelperScript => Path.Combine(HelperDir, "helper.py");
    public string ModelDir => Path.Combine(Root, "model");
    public string ModelFile => Path.Combine(ModelDir, "face_landmarker.task");
    /// <summary>Part 18: the body pose model, a separate on-demand download beside the face model.</summary>
    public string PoseModelFile => Path.Combine(ModelDir, "pose_landmarker_full.task");
    public string PoseMarker => Path.Combine(Root, "pose.json");
    public string DownloadDir => Path.Combine(Root, "downloads");
    public string Marker => Path.Combine(Root, "installed.json");

    /// <summary>True when the marker names this helper version and every file it needs is there.</summary>
    public bool IsInstalled(string helperVersion)
    {
        try
        {
            if (!File.Exists(Marker) || !File.Exists(PythonExe) || !File.Exists(HelperScript) || !File.Exists(ModelFile)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(Marker));
            return doc.RootElement.TryGetProperty("helperVersion", out var v) && v.GetString() == helperVersion;
        }
        catch (Exception) { return false; }
    }

    public long InstalledBytes()
    {
        try { return Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0; }
        catch (Exception) { return 0; }
    }
}

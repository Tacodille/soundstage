namespace Soundstage.SelfCheck;

/// <summary>Finds a Python for the checks that run a generator script. Order: SOUNDSTAGE_PYTHON, then python on PATH, then the per-user Python install folders. Null when none is found.</summary>
internal static class PythonFinder
{
    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("SOUNDSTAGE_PYTHON");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), OperatingSystem.IsWindows() ? "python.exe" : "python3");
                // The Windows Store stub in WindowsApps is not a real Python.
                if (File.Exists(candidate) && !candidate.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return candidate;
            }
            catch { /* a bad PATH entry */ }
        }

        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Python");
        if (Directory.Exists(local))
        {
            foreach (var d in Directory.GetDirectories(local).OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var candidate = Path.Combine(d, "python.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}

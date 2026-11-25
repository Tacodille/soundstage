// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using Soundstage.Look;

namespace Soundstage.Capture;

/// <summary>Works out which folder the "key" route should watch for the new screenshot.</summary>
public static class ScreenshotFolder
{
    /// <summary>The user's choice, else ReShade's SavePath (relative to the game folder), else the game's own folder under Documents.</summary>
    public static string Resolve(string chosen, string gameFolder, string documents, ReShadeIni? ini)
    {
        if (!string.IsNullOrWhiteSpace(chosen)) return chosen;
        var save = ini?.Get("SCREENSHOT", "SavePath");
        if (!string.IsNullOrWhiteSpace(save))
        {
            var full = Path.IsPathRooted(save) ? save : Path.GetFullPath(Path.Combine(gameFolder, save));
            if (Directory.Exists(full)) return full;
        }
        return Path.Combine(documents, "My Games", "FINAL FANTASY XIV - A Realm Reborn", "screenshots");
    }

    /// <summary>Where captures are saved when the user has not chosen a folder.</summary>
    public static string DefaultOutput(string documents) => Path.Combine(documents, "Soundstage", "Captures");
}

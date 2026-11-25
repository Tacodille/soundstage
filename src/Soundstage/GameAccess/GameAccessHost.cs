using Dalamud.Plugin.Services;

namespace Soundstage.GameAccess;

/// <summary>
/// The one shared <see cref="GuardedAccess"/> over the real game reader, so parts 8, 10 and 16 all go through the same
/// "log once per session" memory and the same restore baseline. Call <see cref="Use"/> with the plugin log before the first
/// call (the Probe tab does; a later part that reads the head or camera calls it too, and calling it twice is harmless).
/// </summary>
public static class GameAccessHost
{
    private static readonly object Gate = new();
    private static GuardedAccess? guard;
    private static ClientStructsReader? reader;
    private static IPluginLog? log;

    public static void Use(IPluginLog pluginLog)
    {
        lock (Gate) log ??= pluginLog;
    }

    /// <summary>Writes one line to the plugin log (used by the probe for its action lines).</summary>
    public static void Log(string line)
    {
        lock (Gate) log?.Information("{Line:l}", line);
    }

    public static GuardedAccess Guard
    {
        get
        {
            lock (Gate)
            {
                reader ??= new ClientStructsReader();
                return guard ??= new GuardedAccess(reader, line => log?.Information("{Line:l}", line));
            }
        }
    }

    public static ClientStructsReader Reader
    {
        get { _ = Guard; return reader!; }
    }
}

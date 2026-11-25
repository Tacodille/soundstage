// No Dalamud types here: the offline self-check compiles this file, so the wording and the colour rules are tested.
namespace Soundstage.Status;

public enum StatusLevel { Ok, Warn, Bad }

/// <summary>What the Status tab shows for one thing the plugin leans on.</summary>
/// <param name="Name">Short label ("Stagehand").</param>
/// <param name="Level">Green, amber or red.</param>
/// <param name="Detail">What was found, in one sentence.</param>
/// <param name="Fix">What to do next. Empty when there is nothing to do.</param>
/// <param name="Action">Which button the tab shows for the row.</param>
public sealed record StatusRow(string Name, StatusLevel Level, string Detail, string Fix, StatusAction Action);

public enum StatusAction { None, Recheck, OpenInstaller, OpenAiTab, OpenGameFolder, TestKey }

/// <summary>
/// The live facts the tab gathers from its probes (a Stagehand call, a Brio call, a ReShade pipe ping, the key store). The tab
/// fills this in each time it refreshes; nothing here is read from saved settings.
/// </summary>
public sealed class StatusFacts
{
    public bool StagehandAnswered { get; init; }
    public string StagehandAvailability { get; init; } = "";   // the enum name, "Available" when fine
    public string StagehandRevision { get; init; } = "n/a";
    public string? StagehandError { get; init; }

    public bool BrioAnswered { get; init; }
    public string BrioVersion { get; init; } = "n/a";
    public string? BrioError { get; init; }

    /// <summary>False until the first ping has come back (or failed).</summary>
    public bool AddonChecked { get; init; }
    public bool AddonAnswered { get; init; }
    public int? AddonVersion { get; init; }
    public string AddonText { get; init; } = "";

    public bool HasKey { get; init; }
    public string ProviderName { get; init; } = "";
    /// <summary>Null until a key test has been run since the game started.</summary>
    public bool? KeyTestedOk { get; init; }
    public string KeyTestMessage { get; init; } = "";
    public bool KeyTesting { get; init; }
}

public static class StatusRows
{
    /// <summary>The newest add-on protocol this plugin speaks; an older add-on still answers, but some Look features need the new one.</summary>
    public const int CurrentAddonVersion = 2;

    public static IReadOnlyList<StatusRow> Build(StatusFacts f) => new[] { Stagehand(f), Brio(f), Addon(f), AiKey(f) };

    public static StatusRow Stagehand(StatusFacts f)
    {
        if (f.StagehandAnswered && f.StagehandAvailability is "Available" or "")
            return new("Stagehand", StatusLevel.Ok, $"Connected (API revision {f.StagehandRevision}).", "", StatusAction.None);
        if (f.StagehandAnswered && f.StagehandAvailability.Length > 0 && f.StagehandAvailability != "StagehandMissing")
            return new("Stagehand", StatusLevel.Bad, $"Stagehand answered, but its API is not one Soundstage can use ({f.StagehandAvailability}).",
                "Update Stagehand (and Soundstage) from the plugin installer, then press Recheck.", StatusAction.OpenInstaller);
        return new("Stagehand", StatusLevel.Bad, "Stagehand did not answer. It is either not installed or not switched on.",
            "Install Stagehand 0.5.x from the plugin installer, make sure it is enabled, then press Recheck. Lights, Sets and Scenes need it.", StatusAction.OpenInstaller);
    }

    public static StatusRow Brio(StatusFacts f)
    {
        if (f.BrioAnswered)
            return new("Brio", StatusLevel.Ok, $"Connected (API {f.BrioVersion}).", "", StatusAction.None);
        // A reply with a different major number is a version problem; anything else is "not there or IPC off".
        if (f.BrioVersion != "n/a" && f.BrioError != null && f.BrioError.Contains("version", StringComparison.OrdinalIgnoreCase))
            return new("Brio", StatusLevel.Bad, f.BrioError, "Update Brio (and Soundstage) from the plugin installer, then press Recheck.", StatusAction.OpenInstaller);
        return new("Brio", StatusLevel.Bad, "Brio did not answer. It is not installed, not switched on, or its IPC is off.",
            "Install Brio 0.8.x from the plugin installer, switch IPC on in Brio's settings, then press Recheck. The Poses shelf and the pose tools need it.", StatusAction.OpenInstaller);
    }

    public static StatusRow Addon(StatusFacts f)
    {
        if (!f.AddonChecked)
            return new("ReShade add-on", StatusLevel.Warn, "Checking...", "", StatusAction.None);
        if (!f.AddonAnswered)
            return new("ReShade add-on", StatusLevel.Warn, "No answer from the add-on. It is optional: only the Look tab and the add-on screenshot route use it.",
                "Copy soundstage.addon64 next to dxgi.dll in the game folder, then restart the game. It is a separate download beside the plugin.", StatusAction.OpenGameFolder);
        if (f.AddonVersion is int v && v < CurrentAddonVersion)
            return new("ReShade add-on", StatusLevel.Warn, $"Answering, but it is version {v} and this build speaks {CurrentAddonVersion}. Auto focus and restore need the newer one.",
                "Close the game, copy the newer soundstage.addon64 over the old one, and start the game again.", StatusAction.OpenGameFolder);
        return new("ReShade add-on", StatusLevel.Ok, $"Answering (version {f.AddonVersion?.ToString() ?? "?"}).", "", StatusAction.None);
    }

    public static StatusRow AiKey(StatusFacts f)
    {
        var who = f.ProviderName.Length > 0 ? f.ProviderName : "the provider";
        if (!f.HasKey)
            return new("AI key", StatusLevel.Warn, "No key saved. The AI features are off; everything else works without one.",
                "Optional. SETUP-AI.md walks through getting a free key. Paste it on the AI tab.", StatusAction.OpenAiTab);
        if (f.KeyTesting)
            return new("AI key", StatusLevel.Warn, $"Testing the {who} key...", "", StatusAction.None);
        if (f.KeyTestedOk == true)
            return new("AI key", StatusLevel.Ok, $"A {who} key is saved and it passed its test.", "", StatusAction.None);
        if (f.KeyTestedOk == false)
            return new("AI key", StatusLevel.Bad, f.KeyTestMessage.Length > 0 ? f.KeyTestMessage : $"The {who} key did not pass its test.",
                "Fix it on the AI tab, then press Test key again.", StatusAction.OpenAiTab);
        return new("AI key", StatusLevel.Warn, $"A {who} key is saved but has not been tested since the game started.",
            "Press Test key. It lists models only, so it costs no quota and sends no picture.", StatusAction.TestKey);
    }

    /// <summary>The worst level across rows that are not optional (Stagehand and Brio). The add-on and the key are optional, so they never make the summary red.</summary>
    public static StatusLevel Summary(IReadOnlyList<StatusRow> rows)
    {
        var worst = StatusLevel.Ok;
        foreach (var r in rows)
        {
            var optional = r.Name is "ReShade add-on" or "AI key";
            var level = optional && r.Level == StatusLevel.Bad ? StatusLevel.Warn : r.Level;
            if (level > worst) worst = level;
        }
        return worst;
    }
}

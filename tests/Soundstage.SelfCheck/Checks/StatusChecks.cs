using Soundstage.Status;

namespace Soundstage.SelfCheck;

/// <summary>Part 15: the Status tab's rows. The probes need the game; the colour and wording rules do not.</summary>
public sealed class StatusChecks : CheckArea
{
    public override string Name => "Status rows (offline)";
    public override int Order => 150;

    private static StatusFacts Good() => new()
    {
        StagehandAnswered = true, StagehandAvailability = "", StagehandRevision = "7",
        BrioAnswered = true, BrioVersion = "3.0",
        AddonChecked = true, AddonAnswered = true, AddonVersion = StatusRows.CurrentAddonVersion,
        HasKey = true, ProviderName = "Google Gemini", KeyTestedOk = true,
    };

    public override void Run()
    {
        var good = StatusRows.Build(Good());
        Check("status: four rows, all green when everything answers", good.Count == 4 && good.All(r => r.Level == StatusLevel.Ok) && StatusRows.Summary(good) == StatusLevel.Ok);

        var noSh = StatusRows.Stagehand(new StatusFacts { StagehandAnswered = false });
        Check("status: Stagehand missing is red and has a fix and an installer button", noSh.Level == StatusLevel.Bad && noSh.Fix.Contains("Stagehand 0.5.x") && noSh.Action == StatusAction.OpenInstaller);
        var oddSh = StatusRows.Stagehand(new StatusFacts { StagehandAnswered = true, StagehandAvailability = "SomethingElse" });
        Check("status: a Stagehand that answers with an unusable API is red, not green", oddSh.Level == StatusLevel.Bad && oddSh.Detail.Contains("SomethingElse"));

        var noBrio = StatusRows.Brio(new StatusFacts { BrioAnswered = false, BrioVersion = "n/a", BrioError = "IPC not registered" });
        Check("status: Brio not answering is red and says IPC", noBrio.Level == StatusLevel.Bad && noBrio.Detail.Contains("IPC") && noBrio.Fix.Contains("0.8.x"));
        var oldBrio = StatusRows.Brio(new StatusFacts { BrioAnswered = false, BrioVersion = "2.1", BrioError = "Brio's API is version 2.1, Soundstage speaks 3.x." });
        Check("status: a Brio with another API version says so", oldBrio.Level == StatusLevel.Bad && oldBrio.Detail.Contains("version 2.1"));

        Check("status: add-on not yet checked is amber 'Checking'", StatusRows.Addon(new StatusFacts { AddonChecked = false }) is { Level: StatusLevel.Warn, Detail: "Checking..." });
        var noAddon = StatusRows.Addon(new StatusFacts { AddonChecked = true, AddonAnswered = false });
        Check("status: no add-on is amber (optional) and offers the game folder", noAddon.Level == StatusLevel.Warn && noAddon.Action == StatusAction.OpenGameFolder && noAddon.Fix.Contains("dxgi.dll"));
        var oldAddon = StatusRows.Addon(new StatusFacts { AddonChecked = true, AddonAnswered = true, AddonVersion = 1 });
        Check("status: an older add-on is amber and names both versions", oldAddon.Level == StatusLevel.Warn && oldAddon.Detail.Contains("version 1") && oldAddon.Detail.Contains(StatusRows.CurrentAddonVersion.ToString()));

        Check("status: no key is amber and points at the AI tab", StatusRows.AiKey(new StatusFacts { HasKey = false }) is { Level: StatusLevel.Warn, Action: StatusAction.OpenAiTab });
        Check("status: a saved but untested key is amber with a Test key button", StatusRows.AiKey(new StatusFacts { HasKey = true, ProviderName = "OpenRouter" }) is { Level: StatusLevel.Warn, Action: StatusAction.TestKey });
        var failed = StatusRows.AiKey(new StatusFacts { HasKey = true, KeyTestedOk = false, KeyTestMessage = "Gemini did not accept that key." });
        Check("status: a key that failed its test is red and shows the plain message", failed.Level == StatusLevel.Bad && failed.Detail == "Gemini did not accept that key.");
        Check("status: a tested key is green", StatusRows.AiKey(new StatusFacts { HasKey = true, KeyTestedOk = true }).Level == StatusLevel.Ok);

        // The summary only goes red for what the plugin cannot run without.
        var optionalBad = new[] { StatusRows.Stagehand(Good()), StatusRows.Brio(Good()), noAddon, failed };
        Check("status: a failed key or a missing add-on never turns the summary red", StatusRows.Summary(optionalBad) == StatusLevel.Warn);
        var requiredBad = new[] { noSh, StatusRows.Brio(Good()), noAddon, failed };
        Check("status (control): a missing Stagehand turns the summary red", StatusRows.Summary(requiredBad) == StatusLevel.Bad);

        // No row ever carries a key-looking string or a ban-risk word.
        var all = good.Concat(new[] { noSh, noBrio, noAddon, oldAddon, failed }).Select(r => r.Detail + " " + r.Fix).ToList();
        Check("status: row wording has none of the words the scrub forbids", all.All(s => !System.Text.RegularExpressions.Regex.IsMatch(s, @"\b(ban|banned|terms of service|tos)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        // The troubleshooting page's AI rows come from AiMessages.cs; if a message is edited and the page is not, this fails.
        var py = PythonFinder.Find();
        var script = Path.GetFullPath(Path.Combine(root, "..", "..", "tools", "gen_troubleshooting.py"));
        var page = Path.GetFullPath(Path.Combine(root, "..", "..", "docs", "troubleshooting.md"));
        if (py == null || !File.Exists(script)) Console.WriteLine("      (no Python found; troubleshooting.md freshness check skipped)");
        else
        {
            var (code, text) = RunPython(py, $"\"{script}\" --check");
            Check("troubleshooting.md matches AiMessages.cs and the intake messages", code == 0, text);
            var copy = Path.Combine(Path.GetTempPath(), "soundstage-trouble-" + Guid.NewGuid().ToString("N") + ".md");
            try
            {
                File.WriteAllText(copy, File.ReadAllText(page).Replace("Check you copied the whole key", "Check you copied the key"));
                var (code2, _) = RunPython(py, $"\"{script}\" --check --doc \"{copy}\"");
                Check("troubleshooting.md (control): a page with an edited AI row is reported as out of date", code2 == 1);
            }
            finally { try { File.Delete(copy); } catch { } }
        }
    }

    private static (int Code, string Text) RunPython(string exe, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, text.Trim());
    }
}

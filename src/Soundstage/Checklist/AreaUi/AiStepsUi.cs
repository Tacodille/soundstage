using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using Soundstage.Ai;

namespace Soundstage.Checklist;

/// <summary>Buttons for the AI steps: open the key page, and count the saved key in dalamud.log without ever showing it.</summary>
public sealed class AiStepsUi : IChecklistAreaUi
{
    private ChecklistResult? logCount;

    public string AreaId => "ai";

    public ChecklistResult? AutoResult(ChecklistContext ctx, ChecklistStep step) => step.Id == "10" ? logCount : null;

    public void DrawControls(ChecklistContext ctx, ChecklistStep step)
    {
        var ai = ctx.Studio.Ai;
        switch (step.Id)
        {
            case "2":
                if (ImGui.Button("Open Google AI Studio keys page")) Util.OpenLink(AiService.KeyPageUrl(GeminiProvider.ProviderId));
                break;

            case "10":
                if (ImGui.Button("Count my saved key in dalamud.log"))
                    logCount = Count(ai.Keys.Get(AiService.ProviderIds[0]));
                if (logCount == null) ImGui.TextDisabled("Needs a saved Gemini key (step 3).");
                break;
        }
    }

    private static ChecklistResult Count(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return new ChecklistResult("FAIL", "no Gemini key saved, so nothing to count");
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher");
        var total = 0;
        var read = 0;
        foreach (var name in new[] { "dalamud.log", "dalamud.old.log" })
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                    if (line.Contains(key, StringComparison.Ordinal)) total++;
                read++;
            }
            catch (IOException) { }
        }
        if (read == 0) return new ChecklistResult("FAIL", "could not read dalamud.log");
        return total == 0 ? new ChecklistResult("PASS", $"0 lines hold the key ({read} log file(s) read)") : new ChecklistResult("FAIL", $"{total} line(s) hold the key");
    }
}

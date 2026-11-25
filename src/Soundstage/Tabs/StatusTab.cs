using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundstage.Look;
using Soundstage.Status;

namespace Soundstage.Tabs;

/// <summary>
/// Part 15: what the plugin leans on and whether each piece answers right now. Every row comes from a live probe (a Stagehand
/// call, a Brio call, a ping down the ReShade pipe, the key store, and a key test when asked), never from saved settings.
/// The probes run when the tab opens, every 20 seconds while it stays open, and when you press Recheck. The key is only tested when you press Test key.
/// </summary>
public sealed class StatusTab : ITab
{
    public string Name => "Status";
    public int Order => 80;

    private const double AutoRefreshSeconds = 20;

    private readonly ReShadePipe pipe = new(new ReShadeProbe());
    private double lastRefresh = double.NegativeInfinity;
    private bool wasHidden = true;

    // Written by worker tasks, read by the draw thread.
    private volatile bool addonChecked;
    private volatile bool addonAnswered;
    private volatile bool addonBusy;
    private int addonVersion;      // 0 = unknown
    private volatile string addonText = "";

    private volatile bool keyTesting;
    private volatile int keyState;  // 0 = not tested, 1 = ok, 2 = failed
    private volatile string keyMessage = "";
    private string keyProvider = "";

    private string copied = "";

    public void OnHidden(Studio studio) => wasHidden = true;

    public void Draw(Studio studio)
    {
        var now = ImGui.GetTime();
        if (wasHidden || now - lastRefresh > AutoRefreshSeconds)
        {
            Refresh(studio);
            lastRefresh = now;
            wasHidden = false;
        }

        var facts = Gather(studio);
        var rows = StatusRows.Build(facts);
        var summary = StatusRows.Summary(rows);

        var (word, colour) = summary switch
        {
            StatusLevel.Ok => ("Everything Soundstage needs is answering.", Green),
            StatusLevel.Warn => ("Ready to go. Some optional pieces need a look.", Amber),
            _ => ("Something Soundstage needs is not answering.", Red),
        };
        ImGui.TextColored(colour, word);
        ImGui.SameLine();
        if (ImGui.Button("Recheck")) { Refresh(studio); lastRefresh = now; }
        ImGui.Separator();

        foreach (var row in rows)
        {
            ImGui.PushID(row.Name);
            ImGui.TextColored(ColourOf(row.Level), row.Level switch { StatusLevel.Ok => "[ OK ]", StatusLevel.Warn => "[ .. ]", _ => "[ !! ]" });
            ImGui.SameLine();
            ImGui.TextUnformatted(row.Name);
            ImGui.Indent(24);
            ImGui.TextWrapped(row.Detail);
            if (row.Fix.Length > 0) ImGui.TextDisabled(row.Fix);
            DrawAction(studio, row);
            ImGui.Unindent(24);
            ImGui.PopID();
            ImGui.Spacing();
        }

        if (copied.Length > 0) ImGui.TextDisabled(copied);
        ImGui.Separator();
        ImGui.TextDisabled("Stagehand and Brio are checked by asking them over IPC, so \"did not answer\" covers both not installed and switched off. The add-on and the key are optional.");
    }

    private void DrawAction(Studio studio, StatusRow row)
    {
        switch (row.Action)
        {
            case StatusAction.OpenInstaller:
                if (ImGui.Button("Copy /xlplugins"))
                {
                    ImGui.SetClipboardText("/xlplugins");
                    copied = "Copied /xlplugins. Paste it into the game chat to open the plugin installer.";
                }
                break;
            case StatusAction.OpenAiTab:
                if (ImGui.Button("Open the AI tab")) TabHost.RequestTab("AI");
                break;
            case StatusAction.OpenGameFolder:
                if (ImGui.Button("Open the game folder")) OpenGameFolder(studio);
                break;
            case StatusAction.TestKey:
                if (!keyTesting && ImGui.Button("Test key")) TestKey(studio);
                break;
        }
    }

    private StatusFacts Gather(Studio studio)
    {
        var bridge = studio.Bridge;
        var brio = studio.Brio;
        var ai = studio.Ai;
        var id = ai.ProviderId;
        var hasKey = ai.Keys.Has(id);
        // A different provider is a different key: forget a test result that belonged to the previous one.
        if (keyProvider != id) { keyProvider = id; keyState = 0; keyMessage = ""; }

        return new StatusFacts
        {
            StagehandAnswered = bridge.Availability != Stagehand.Api.StagehandApiAvailability.StagehandMissing,
            StagehandAvailability = bridge.Availability.ToString() == "Available" ? "" : bridge.Availability.ToString(),
            StagehandRevision = bridge.Revision,
            StagehandError = bridge.LastError,
            BrioAnswered = brio.Available,
            BrioVersion = brio.Version,
            BrioError = brio.LastError,
            AddonChecked = addonChecked,
            AddonAnswered = addonAnswered,
            AddonVersion = addonVersion > 0 ? addonVersion : null,
            AddonText = addonText,
            HasKey = hasKey,
            ProviderName = Soundstage.Ai.AiService.NameOf(id),
            KeyTestedOk = keyState == 1 ? true : keyState == 2 ? false : null,
            KeyTestMessage = keyMessage,
            KeyTesting = keyTesting,
        };
    }

    private void Refresh(Studio studio)
    {
        studio.Bridge.Refresh();
        studio.Brio.Refresh();
        PingAddon();
    }

    private void PingAddon()
    {
        if (addonBusy) return;
        addonBusy = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var reply = await pipe.PingAsync().ConfigureAwait(false);
                addonAnswered = reply.Ok;
                addonVersion = pipe.AddonVersion ?? 0;
                addonText = reply.Text;
            }
            catch (Exception ex)
            {
                addonAnswered = false;
                addonVersion = 0;
                addonText = ex.Message;
            }
            finally { addonChecked = true; addonBusy = false; }
        });
    }

    private void TestKey(Studio studio)
    {
        var ai = studio.Ai;
        keyTesting = true;
        keyMessage = "";
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await ai.TestKey().ConfigureAwait(false);
                keyState = result.Ok ? 1 : 2;
                keyMessage = result.Ok ? "" : result.Message;
            }
            catch (Exception ex)
            {
                keyState = 2;
                keyMessage = Soundstage.Ai.AiMessages.For(Soundstage.Ai.AiError.Unknown, Soundstage.Ai.AiService.NameOf(ai.ProviderId));
                ai.Log.Error("Status tab key test threw", ex);
            }
            finally { keyTesting = false; }
        });
    }

    private static void OpenGameFolder(Studio studio)
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (!string.IsNullOrEmpty(dir)) Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) { studio.Log.Information("{P} could not open the game folder: {Msg}", "[Soundstage/STATUS]", ex.Message); }
    }

    private static readonly Vector4 Green = new(0.5f, 0.9f, 0.5f, 1f);
    private static readonly Vector4 Amber = new(1f, 0.78f, 0.35f, 1f);
    private static readonly Vector4 Red = new(1f, 0.5f, 0.45f, 1f);

    private static Vector4 ColourOf(StatusLevel level) => level switch { StatusLevel.Ok => Green, StatusLevel.Warn => Amber, _ => Red };
}

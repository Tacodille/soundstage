using System.Numerics;
using System.Text.Json.Nodes;
using Dalamud.Bindings.ImGui;
using Dalamud.Utility;
using Soundstage.Ai;

namespace Soundstage.Tabs;

/// <summary>
/// The optional AI layer's setup page: pick a provider, paste a key, Test key, give the one-time consent. Nothing in any
/// other tab needs this one to be visited, and with no key the plugin behaves exactly as before.
/// </summary>
public sealed class AiTab : ITab
{
    public string Name => "AI";
    public int Order => 60;

    // The tiny two-colour picture the "Send a test picture" button uploads (32 x 32 PNG, orange left half, blue right half).
    private const string TestPictureBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAIAAAD8GO2jAAAAMElEQVR4nGN4ViFCEhKxOUESYhi1YNSCUQtGLRi1YNSCUQtGLRi1YNSCUQsqhogFADcEFEw1G0BJAAAAAElFTkSuQmCC";

    private static readonly Vector4 Green = new(0.5f, 1f, 0.6f, 1f);
    private static readonly Vector4 Red = new(1f, 0.5f, 0.4f, 1f);

    private string keyInput = "";
    private string lastShownProvider = "";
    private string modelInput = "";
    private string baseUrlInput = "";
    private string status = "";
    private bool statusOk;
    private bool busy;
    private bool openConsent;
    private bool consentThenSend;

    public void Draw(Studio studio)
    {
        var ai = studio.Ai;
        var id = ai.ProviderId;
        SyncInputs(ai, id);

        ImGui.TextWrapped("Optional. Soundstage works fine without any of this. The AI layer reads a picture you give it and suggests a Scene. You bring your own key; it is saved on this PC, encrypted, and only ever sent to the provider you pick.");
        ImGui.Separator();

        ImGui.TextUnformatted("Provider");
        foreach (var pid in AiService.ProviderIds)
        {
            if (ImGui.RadioButton(AiService.NameOf(pid) + "##prov" + pid, id == pid))
            {
                ai.ProviderId = pid;
                id = pid;
                keyInput = "";
                status = "";
                SyncInputs(ai, id);
            }
            ImGui.SameLine();
        }
        ImGui.NewLine();

        var settings = ai.Settings.For(id);
        var hasKey = ai.Keys.Has(id);

        if (id == GeminiProvider.ProviderId)
            ImGui.TextWrapped("Google Gemini has a free tier. Press Get a key, sign in with a Google account, make a key in AI Studio and paste it below.");
        else if (id == OpenAiCompatProvider.OpenRouterId)
            ImGui.TextWrapped("OpenRouter has free models (names ending :free) with a small daily allowance. Press Get a key, make an account and a key, and paste it below.");
        else
            ImGui.TextWrapped("Any service that speaks the OpenAI chat format, including one running on this PC. Type its web address (ending in /v1) and a model that accepts images.");

        if (id == OpenAiCompatProvider.CustomId)
        {
            ImGui.SetNextItemWidth(360);
            if (ImGui.InputText("Web address##base", ref baseUrlInput, 200)) { settings.BaseUrl = baseUrlInput.Trim(); Persist(studio, ai); }
        }

        var page = AiService.KeyPageUrl(id);
        if (page.Length > 0 && ImGui.Button("Get a key"))
            Util.OpenLink(page);
        if (page.Length > 0) ImGui.SameLine();

        ImGui.SetNextItemWidth(360);
        ImGui.InputText("Key##key", ref keyInput, 300, ImGuiInputTextFlags.Password);
        ImGui.SameLine();
        if (ImGui.Button("Save key") && keyInput.Trim().Length > 0)
        {
            ai.Keys.Set(id, keyInput);
            keyInput = "";
            status = "Key saved on this PC, encrypted for your Windows account. Press Test key.";
            statusOk = true;
        }
        ImGui.SameLine();
        if (hasKey && ImGui.Button("Forget key"))
        {
            ai.Keys.Clear(id);
            status = "Key removed.";
            statusOk = true;
        }
        ImGui.TextDisabled(hasKey ? "A key is saved for this provider." : "No key saved for this provider.");

        ImGui.SetNextItemWidth(360);
        var modelLabel = id == GeminiProvider.ProviderId ? "Model (blank picks one your key can use)##model" : "Model##model";
        if (ImGui.InputText(modelLabel, ref modelInput, 120)) { settings.Model = modelInput.Trim(); Persist(studio, ai); }

        ImGui.BeginDisabled(busy || !hasKey);
        if (ImGui.Button(busy ? "Working..." : "Test key")) RunTest(studio, ai);
        ImGui.SameLine();
        if (ImGui.Button("Send a test picture")) RequestTestPicture(studio, ai);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled("(the test picture is a tiny orange and blue square, one request)");

        if (status.Length > 0) ImGui.TextColored(statusOk ? Green : Red, status);

        ImGui.Separator();
        var used = ai.UsedToday(id);
        var limit = settings.DailyLimit;
        if (limit > 0) ImGui.TextUnformatted($"Used today from Soundstage: {used} of {limit}.");
        else ImGui.TextUnformatted($"Used today from Soundstage: {used}. The provider keeps its own count.");
        var limits = AiService.LimitsPageUrl(id);
        if (limits.Length > 0 && ImGui.SmallButton("Check the provider's limits")) Util.OpenLink(limits);

        ImGui.TextDisabled("Setup guide: SETUP-AI.md (comes with the public release).");
        ImGui.TextDisabled(ai.HasConsent(id) ? "Privacy note: you said yes for this provider." : "Privacy note: not yet agreed. You are asked before the first picture goes anywhere.");
        if (ai.HasConsent(id) && ImGui.SmallButton("Withdraw my yes")) ai.WithdrawConsent(id);

        DrawConsent(studio, ai, id);
    }

    private void SyncInputs(AiService ai, string id)
    {
        if (lastShownProvider == id) return;
        lastShownProvider = id;
        var s = ai.Settings.For(id);
        modelInput = s.Model;
        baseUrlInput = s.BaseUrl;
    }

    private static void Persist(Studio studio, AiService ai)
    {
        studio.Config.SetSection(AiService.ConfigKey, ai.Settings);
        studio.Save();
    }

    private void RunTest(Studio studio, AiService ai)
    {
        busy = true;
        status = "Asking the provider...";
        statusOk = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await ai.TestKey().ConfigureAwait(false);
                status = result.Message;
                statusOk = result.Ok;
                var id = ai.ProviderId;
                var s = ai.Settings.For(id);
                if (result.Ok && s.Model.Length == 0 && id != GeminiProvider.ProviderId && result.ChosenModel != null) { s.Model = result.ChosenModel; modelInput = result.ChosenModel; }
                if (result.RemainingToday is int left) { s.DailyLimit = Math.Max(s.DailyLimit, ai.UsedToday(id) + left); }
                Persist(studio, ai);
            }
            catch (Exception ex)
            {
                status = AiMessages.For(AiError.Unknown, AiService.NameOf(ai.ProviderId));
                statusOk = false;
                ai.Log.Error("Test key threw", ex);
            }
            finally { busy = false; }
        });
    }

    private void RequestTestPicture(Studio studio, AiService ai)
    {
        if (!ai.HasConsent(ai.ProviderId)) { consentThenSend = true; openConsent = true; return; }
        SendTestPicture(studio, ai);
    }

    private void SendTestPicture(Studio studio, AiService ai)
    {
        busy = true;
        status = "Sending the test picture...";
        statusOk = true;
        _ = Task.Run(async () =>
        {
            try
            {
                var schema = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["left_colour"] = new JsonObject { ["type"] = "string" },
                        ["right_colour"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("left_colour", "right_colour"),
                };
                var result = await ai.AnalyseImage(Convert.FromBase64String(TestPictureBase64), "image/png", schema,
                    "Name the main colour of the left half and the right half of this picture in one word each.").ConfigureAwait(false);
                if (result.Ok)
                {
                    status = $"It worked. The AI saw: left {result.Value!["left_colour"]}, right {result.Value["right_colour"]}.";
                    statusOk = true;
                }
                else
                {
                    status = AiMessages.For(result.Failure!, AiService.NameOf(ai.ProviderId));
                    statusOk = false;
                }
                Persist(studio, ai);
            }
            catch (Exception ex)
            {
                status = AiMessages.For(AiError.Unknown, AiService.NameOf(ai.ProviderId));
                statusOk = false;
                ai.Log.Error("test picture threw", ex);
            }
            finally { busy = false; }
        });
    }

    private void DrawConsent(Studio studio, AiService ai, string id)
    {
        if (openConsent) { ImGui.OpenPopup("Before a picture goes online##consent"); openConsent = false; }
        var open = true;
        ImGui.SetNextWindowSize(new Vector2(520, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Before a picture goes online##consent", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.PushTextWrapPos(500);
        ImGui.TextUnformatted(ai.ConsentText(id));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button("Yes, that's fine"))
        {
            ai.GiveConsent(id);
            ImGui.CloseCurrentPopup();
            if (consentThenSend) { consentThenSend = false; SendTestPicture(studio, ai); }
        }
        ImGui.SameLine();
        if (ImGui.Button("No, send nothing"))
        {
            consentThenSend = false;
            status = "Nothing was sent.";
            statusOk = true;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}

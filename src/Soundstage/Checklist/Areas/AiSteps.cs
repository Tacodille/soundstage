// No Dalamud types here (see IChecklistArea.cs). Buttons for these steps are in Checklist/AreaUi/AiStepsUi.cs.
namespace Soundstage.Checklist;

/// <summary>Part 6 of the Soundstage plan: the in-game half of the AI layer's validation table.</summary>
public sealed class AiSteps : IChecklistArea
{
    public string Id => "ai";
    public string Name => "Part 6: optional AI layer";
    public int Order => 60;

    public IReadOnlyList<ChecklistStep> Steps { get; } = new ChecklistStep[]
    {
        new("1", "With no key, nothing changes",
            "Before you do anything on the AI tab, open the Lighting, Set and Poses tabs and apply a preset. PASS: they behave exactly as before and nothing about AI pops up. The AI tab itself shows 'No key saved for this provider' and 'not yet agreed'."),
        new("2", "Make a free Gemini key",
            "Press Get a key. Your browser opens Google AI Studio (aistudio.google.com/apikey). Sign in with a Google account, make a key, and copy it. PASS: you have a key on the clipboard without having to enter any card details."),
        new("3", "Paste the key and Test key",
            "On the AI tab pick Google Gemini, paste the key, press Save key, then Test key. PASS: a green message starting 'The key works', saying how many models Google lists and which one Soundstage will use. The Key box has gone blank after saving, and the key is shown as dots while you type."),
        new("4", "A wrong key gets the bad-key message",
            "Paste a made-up key (for example the real one with its last letter changed), Save key, Test key. PASS: a red message saying Google did not accept the key and what to do about it, with no HTTP numbers or code in it. Then put your real key back and Test key again (green)."),
        new("5", "Offline gets the internet message",
            "Turn Wi-Fi off (or unplug the network cable), press Test key. PASS: a red message saying it could not reach the internet. Turn the network back on and Test key again (green)."),
        new("6", "Privacy note before the first picture",
            "Press Send a test picture. PASS: a window called 'Before a picture goes online' appears first, names Google Gemini, says the picture leaves your computer, and mentions what Google's free tier may do with it. Nothing has been sent yet."),
        new("7", "Saying no sends nothing",
            "In that window press No, send nothing. PASS: the status line says 'Nothing was sent', and the 'Used today' count did not go up."),
        new("8", "Saying yes sends one picture",
            "Press Send a test picture again, then Yes, that's fine. PASS: a green 'It worked' message naming an orange-ish colour for the left and a blue-ish one for the right, and 'Used today' went up by one. Press Send a test picture a second time: no window this time, it just sends."),
        new("9", "Changing provider asks again",
            "Pick OpenRouter on the AI tab (you do not need a key), then look at the privacy line under the buttons. PASS: it says 'not yet agreed' for OpenRouter even though you said yes for Gemini. Switch back to Gemini and it says you said yes. Press Withdraw my yes, then Send a test picture: the window appears again."),
        new("10", "The key is not in the log",
            "Press the button on this step. PASS: it counts how many times your saved Gemini key appears in dalamud.log and the count is 0. (If you want to check by hand: search dalamud.log for the first ten characters of the key; there should be no hits. Lines starting [Soundstage/AI] are fine.)"),
        new("11", "The key survives a restart, encrypted",
            "Restart the game (or reload the plugin), open the AI tab. PASS: it still says a key is saved and Test key is green without pasting again. Search Soundstage.json in pluginConfigs for the key: it is not there in plain text."),
        new("12", "Optional: OpenRouter",
            "Only if you want to try it. Pick OpenRouter, Get a key, paste, Test key. PASS: a green message with how many free requests are left today and a suggested model ending :free; copy it into the Model box and Send a test picture. Skip this step if you do not want an OpenRouter account."),
    };
}

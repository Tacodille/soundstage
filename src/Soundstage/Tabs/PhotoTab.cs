using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Soundstage.Ai;
using Soundstage.Ai.PhotoToScene;

namespace Soundstage.Tabs;

/// <summary>Settings of the photo tab, kept in <see cref="Configuration.Sections"/> under <see cref="Key"/>.</summary>
public sealed class PhotoSettings
{
    public const string Key = "photo";

    /// <summary>The last path typed or picked, so the box is filled next time. Only the path is kept, never the picture.</summary>
    public string LastPath { get; set; } = "";
}

/// <summary>
/// Photo to Scene: pick a picture, press Analyse, read the card, then Apply, Save or Adjust. The picture is shrunk and
/// stripped of its metadata first, nothing is sent until the AI tab's privacy note has been agreed, and nothing is applied
/// without a click here. The original file is never changed or copied.
/// </summary>
public sealed class PhotoTab : ITab
{
    public string Name => "Photo";
    public int Order => 65;

    private static readonly Vector4 Green = new(0.5f, 1f, 0.6f, 1f);
    private static readonly Vector4 Red = new(1f, 0.5f, 0.4f, 1f);

    private readonly FileDialogManager dialogs = new();
    private PhotoRun? run;
    private string pathInput = "";
    private bool pathLoaded;
    private PreparedImage? image;
    private PhotoOutcome? outcome;
    private string status = "";
    private bool statusOk;
    private string saveName = "";
    private CancellationTokenSource? cts;
    private volatile bool busy;
    private volatile PhotoOutcome? finished;

    public void Draw(Studio studio)
    {
        run ??= new PhotoRun(studio.Ai, () => studio.Scenes);
        var settings = studio.Config.Section<PhotoSettings>(PhotoSettings.Key);
        if (!pathLoaded) { pathInput = settings.LastPath; pathLoaded = true; }
        if (finished != null) { outcome = finished; finished = null; if (!outcome.Ok) Say(outcome.Message, false); }
        dialogs.Draw();

        ImGui.TextWrapped("Pick a photo and Soundstage reads its light (where it comes from, how soft, what colour) and builds the closest Scene. Optional: it needs a key on the AI tab. Nothing is sent until you press Analyse, and nothing is applied until you press Apply.");
        ImGui.Separator();

        DrawPicker(studio, settings);
        DrawPreview();
        DrawSend(studio);
        if (!string.IsNullOrEmpty(status)) ImGui.TextColored(statusOk ? Green : Red, status);
        if (outcome is { Ok: true }) { ImGui.Separator(); DrawCard(studio, outcome); }
    }

    private void Say(string text, bool ok) { status = text; statusOk = ok; }

    // ---- picking and loading ----

    private void DrawPicker(Studio studio, PhotoSettings settings)
    {
        ImGui.SetNextItemWidth(420);
        ImGui.InputText("Photo file##photopath", ref pathInput, 500);
        ImGui.SameLine();
        if (ImGui.Button("Browse..."))
            dialogs.OpenFileDialog("Pick a photo", "Pictures{.jpg,.jpeg,.png,.bmp,.gif,.tga}", (ok, path) =>
            {
                if (ok && !string.IsNullOrEmpty(path)) { pathInput = path; }
            });
        ImGui.SameLine();
        if (ImGui.Button("Paste path"))
        {
            var clip = ImGui.GetClipboardText() ?? "";
            pathInput = clip.Trim().Trim('"');
        }
        if (ImGui.Button("Load photo##photoload"))
        {
            settings.LastPath = pathInput.Trim().Trim('"');
            studio.Config.SetSection(PhotoSettings.Key, settings);
            studio.Save();
            outcome = null;
            image = PhotoIntake.LoadFile(pathInput);
            Say(image.Ok ? "" : image.Message, false);
        }
        ImGui.SameLine();
        ImGui.TextDisabled("JPEG, PNG, BMP, GIF or TGA. Drag and drop is not available here. The file is only read, never changed or copied.");
    }

    private void DrawPreview()
    {
        if (image is not { Ok: true } img) return;
        ImGui.TextUnformatted($"Loaded {img.OriginalWidth} x {img.OriginalHeight}. What would be sent: {img.Width} x {img.Height} JPEG, {img.Jpeg.Length / 1024} KB, no camera or location data.");
        var cell = 4f;
        var origin = ImGui.GetCursorScreenPos();
        var list = ImGui.GetWindowDrawList();
        for (var y = 0; y < img.ThumbHeight; y++)
            for (var x = 0; x < img.ThumbWidth; x++)
            {
                var i = (y * img.ThumbWidth + x) * 3;
                var col = ImGui.ColorConvertFloat4ToU32(new Vector4(img.ThumbRgb[i] / 255f, img.ThumbRgb[i + 1] / 255f, img.ThumbRgb[i + 2] / 255f, 1f));
                var p = origin + new Vector2(x * cell, y * cell);
                list.AddRectFilled(p, p + new Vector2(cell, cell), col);
            }
        ImGui.Dummy(new Vector2(img.ThumbWidth * cell, img.ThumbHeight * cell));
    }

    // ---- consent and request ----

    private void DrawSend(Studio studio)
    {
        var r = run!;
        var ai = studio.Ai;
        if (image is not { Ok: true }) return;
        ImGui.Separator();
        switch (r.Gate())
        {
            case PhotoGate.NoKey:
                ImGui.TextWrapped("To read photos you need a key from a provider with a free tier. It takes about two minutes: open the AI tab, press Get a key, paste it in and press Test key.");
                if (ImGui.Button("Take me to the AI tab")) TabHost.RequestTab("AI");
                return;
            case PhotoGate.NeedsConsent:
                ImGui.TextWrapped("Before the first picture goes anywhere, read this:");
                ImGui.TextWrapped(ai.ConsentText(ai.ProviderId));
                if (ImGui.Button("Yes, that is fine for photos I choose")) ai.GiveConsent(ai.ProviderId);
                return;
        }

        if (busy)
        {
            ImGui.TextUnformatted("Reading the photo...");
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) cts?.Cancel();
            return;
        }
        if (ImGui.Button(r.ButtonLabel() + "##analyse")) Start(studio, image);
    }

    private void Start(Studio studio, PreparedImage img)
    {
        cts?.Dispose();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        var r = run!;
        busy = true;
        outcome = null;
        Say("", true);
        _ = Task.Run(async () =>
        {
            try { finished = await r.Analyse(img, token).ConfigureAwait(false); }
            catch (Exception ex) { finished = new PhotoOutcome { Message = "Something went wrong reading the photo. Check dalamud.log for a line starting [Soundstage/AI]." }; studio.Ai.Log.Info("photo run threw: " + ex.GetType().Name); }
            finally { busy = false; }
        });
    }

    // ---- result card ----

    private void DrawCard(Studio studio, PhotoOutcome o)
    {
        var a = o.Analysis!;
        var scene = o.Scene!;
        ImGui.TextUnformatted("Here is what I read");
        Row("Key light", $"from the {a.KeyDirection}, {a.KeyElevation} the eye line, {a.KeySoftness}", a.KeyColour);
        if (a.FillColour != null) Row("Fill", "colour", a.FillColour);
        Row("Rim light", a.HasRim switch { true => "yes", false => "none", _ => "not stated" }, a.RimColour);
        if (a.BackdropColour != null) Row("Backdrop", "colour", a.BackdropColour);
        if (a.Mood.Count > 0) Row("Mood", string.Join(", ", a.Mood), null);
        ImGui.TextUnformatted($"Closest Scene: {studio.Scenes.FirstOrDefault(s => s.Id == o.BaseSceneId)?.Name ?? o.BaseSceneId}.");

        var yaw = o.FaceHint?.HeadYaw is { Length: > 0 } fy ? fy : a.HeadYaw;
        var pitch = o.FaceHint?.HeadPitch is { Length: > 0 } fp ? fp : a.HeadPitch;
        var expr = o.FaceHint?.Expression is { Length: > 0 } fe ? fe : a.Expression;
        if (yaw.Length + pitch.Length + expr.Length > 0)
            ImGui.TextDisabled($"Suggestions for the pose (not applied): head {string.Join(", ", new[] { yaw, pitch }.Where(s => s.Length > 0))}{(expr.Length > 0 ? "; expression " + expr : "")}.");
        foreach (var n in o.Notes) ImGui.TextDisabled("- " + n);
        ImGui.TextDisabled("This is one reading of one picture. Check it against what you see.");

        ImGui.Spacing();
        if (ImGui.Button("Apply this Scene")) ApplyIt(studio, scene);
        ImGui.SameLine();
        if (ImGui.Button("Apply and adjust")) { ApplyIt(studio, scene); TabHost.RequestTab("Lighting"); }
        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Name##photosave", ref saveName, 60);
        ImGui.SameLine();
        if (ImGui.Button("Save as my Scene"))
        {
            if (saveName.Trim().Length == 0) Say("Type a name first.", false);
            else if (!ApplyIt(studio, scene, quiet: true) && studio.Plugin.Current == null) Say("Could not put the Scene on stage to save it.", false);
            else
            {
                var saved = studio.Plugin.SaveCurrentAs(saveName);
                Say(saved ? $"Saved '{saveName.Trim()}' to Mine (and it is on stage now)." : studio.Plugin.Message, saved);
            }
        }
    }

    private bool ApplyIt(Studio studio, Scene scene, bool quiet = false)
    {
        var ok = studio.Plugin.Apply(scene.Clone());
        if (!quiet) Say(ok ? "Applied." : studio.Plugin.Message, ok);
        return ok;
    }

    private static void Row(string label, string text, float[]? colour)
    {
        ImGui.TextUnformatted($"{label}: {text}");
        if (colour == null) return;
        ImGui.SameLine();
        ImGui.ColorButton("##sw" + label, new Vector4(colour[0], colour[1], colour[2], 1f), ImGuiColorEditFlags.NoTooltip, new Vector2(18, 18));
    }

    public void OnHidden(Studio studio) { }
}

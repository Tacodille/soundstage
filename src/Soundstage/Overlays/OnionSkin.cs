using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Soundstage.Workflow;

namespace Soundstage.Overlays;

/// <summary>
/// A reference image drawn over the game at a chosen opacity, behind the plugin windows (the background draw list). The path is remembered and the
/// image is read from where it is: nothing is copied or uploaded. The booth and the contact sheet hide it while they capture.
/// </summary>
public sealed class OnionSkin
{
    private readonly Func<OnionSettings> settings;
    private readonly Func<bool> captureHides;

    private IDalamudTextureWrap? wrap;
    private Task<IDalamudTextureWrap>? loading;
    private string loadedKey = "";

    public OnionSkin(Func<OnionSettings> settings, Func<bool> captureHides)
    {
        this.settings = settings;
        this.captureHides = captureHides;
    }

    public ITextureProvider? Textures { get; set; }

    /// <summary>Why nothing is showing, for the tab. Empty when it is drawing.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The image size in pixels once loaded.</summary>
    public Vector2 ImageSize { get; private set; }

    public void Draw()
    {
        var s = settings();
        if (!s.On || captureHides()) return;
        if (string.IsNullOrWhiteSpace(s.ImagePath)) { Status = "Pick an image first."; return; }
        if (Textures == null) { Status = "Textures are not ready yet."; return; }

        var key = s.ImagePath + "|" + s.EdgeOnly;
        if (key != loadedKey) StartLoad(s, key);
        if (loading != null && loading.IsCompleted)
        {
            if (loading.IsCompletedSuccessfully) { wrap?.Dispose(); wrap = loading.Result; ImageSize = new Vector2(wrap.Width, wrap.Height); Status = ""; }
            else Status = "Could not load the image: " + (loading.Exception?.GetBaseException().Message ?? "cancelled");
            loading = null;
        }
        if (wrap == null) return;

        var view = ImGui.GetMainViewport();
        var fit = MathF.Min(view.Size.X / wrap.Width, view.Size.Y / wrap.Height) * Math.Clamp(s.Scale, 0.1f, 4f);
        var size = new Vector2(wrap.Width * fit, wrap.Height * fit);
        var centre = view.Pos + view.Size / 2f + new Vector2(s.OffsetX * view.Size.X, s.OffsetY * view.Size.Y);
        var min = centre - size / 2f;
        var alpha = (uint)(Math.Clamp(s.Opacity, 0f, 1f) * 255f);
        var colour = (alpha << 24) | 0x00FFFFFFu;
        var (u0, u1) = s.FlipHorizontal ? (1f, 0f) : (0f, 1f);
        ImGui.GetBackgroundDrawList().AddImage(wrap.Handle, min, min + size, new Vector2(u0, 0f), new Vector2(u1, 1f), colour);
    }

    private void StartLoad(OnionSettings s, string key)
    {
        loadedKey = key;
        var textures = Textures!;
        var path = s.ImagePath;
        var edge = s.EdgeOnly;
        loading = Task.Run(async () =>
        {
            var bytes = edge ? EdgeFilter.EdgePng(path) : await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            return await textures.CreateFromImageAsync(bytes, "Soundstage onion skin").ConfigureAwait(false);
        });
    }

    /// <summary>Forget the loaded image so the next frame reads the file again (used after a path change from outside).</summary>
    public void Reload() => loadedKey = "";
}

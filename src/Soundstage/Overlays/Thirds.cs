using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Soundstage.CameraTools;

namespace Soundstage.Overlays;

/// <summary>
/// The composition guides: thirds, golden ratio, centre cross and a safe margin, drawn over the whole screen with the background draw list.
/// Dalamud draws it, so it shows in a screenshot unless the game UI is hidden. It hides with the game UI (Scroll Lock or GPose's hide-UI)
/// the same way the light markers do, so a shot taken with the UI hidden is clean.
/// </summary>
public sealed class Thirds
{
    private readonly IGameGui gameGui;
    private readonly Func<CameraSettings> settings;

    public Thirds(IGameGui gameGui, Func<CameraSettings> settings)
    {
        this.gameGui = gameGui;
        this.settings = settings;
    }

    /// <summary>Hooked to UiBuilder.Draw, so it runs with the main window closed.</summary>
    public void Draw()
    {
        var s = settings();
        if (!(s.Thirds || s.Golden || s.CentreCross || s.SafeMargin)) return;
        if (gameGui.GameUiHidden) return;
        try
        {
            var viewport = ImGui.GetMainViewport();
            var origin = viewport.Pos;
            var lines = ThirdsGeometry.Build(viewport.Size.X, viewport.Size.Y, s);
            if (lines.Count == 0) return;
            var dl = ImGui.GetBackgroundDrawList();
            foreach (var l in lines)
            {
                var (col, thick) = l.Kind switch
                {
                    OverlayKind.Thirds => (0xB0FFFFFFu, 1.5f),
                    OverlayKind.Golden => (0xB000C8FFu, 1.5f),     // ABGR: amber
                    OverlayKind.Cross => (0x90FFFFFFu, 1f),
                    _ => (0xB05050FFu, 1.5f),                       // ABGR: red-ish margin box
                };
                dl.AddLine(origin + new Vector2(l.X1, l.Y1), origin + new Vector2(l.X2, l.Y2), col, thick);
            }
        }
        catch (Exception ex)
        {
            // An overlay must never take the UI down; the tab shows nothing about it, so one quiet line is enough.
            if (!warned) { warned = true; System.Diagnostics.Debug.WriteLine("Soundstage thirds overlay: " + ex.Message); }
        }
    }

    private bool warned;
}

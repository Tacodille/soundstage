using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Soundstage.Tabs;

namespace Soundstage;

/// <summary>The main window: a Stagehand status block, then every tab TabHost found.</summary>
public sealed class MainWindow : Window
{
    private readonly Studio studio;
    private readonly TabHost tabs;

    public MainWindow(Studio studio, TabHost tabs) : base("Soundstage###SoundstageMain")
    {
        this.studio = studio;
        this.tabs = tabs;
        Size = new Vector2(460, 640);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    private StagehandBridge Bridge => studio.Bridge;

    /// <summary>Part 12: the photo booth and contact sheet hide the plugin's windows while they capture.</summary>
    public override bool DrawConditions() => !Soundstage.Workflow.WorkflowState.UiHidden;

    public override void Draw()
    {
        DrawStatus();
        ImGui.Separator();
        tabs.Draw(studio);
    }

    private void DrawStatus()
    {
        if (!Bridge.Available)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), $"Stagehand not available ({Bridge.Availability}).");
            ImGui.TextWrapped("Install and enable Stagehand 0.5.x, then press Recheck Stagehand.");
            if (ImGui.Button("Recheck Stagehand")) Bridge.Refresh();
        }
        else
        {
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.6f, 1f), $"Stagehand available, API revision {Bridge.Revision}");
        }

        ImGui.TextUnformatted($"Placed: preset {(Bridge.Placed ? "yes" : "no")}, smoke {(Bridge.SmokePlaced ? "yes" : "no")}, canvas {(Bridge.CanvasPlaced ? "yes" : "no")}");
        var result = Bridge.LastResult == null ? "n/a" : Bridge.LastResult.Value ? "true" : "false";
        ImGui.TextUnformatted($"Last call: {Bridge.LastCall} -> {result}");
        if (!string.IsNullOrEmpty(Bridge.LastError))
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), $"Reason / error: {Bridge.LastError}");
    }
}

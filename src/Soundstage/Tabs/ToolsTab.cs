using Dalamud.Bindings.ImGui;

namespace Soundstage.Tabs;

/// <summary>Dev tools: test checklist, light survey, smoke test. Shown only after /soundstage dev.</summary>
public sealed class ToolsTab : ITab
{
    public string Name => "Tools";
    public int Order => 90;
    public bool IsVisible(Studio studio) => studio.Config.DevMode;

    private Studio studio = null!;
    private Plugin plugin => studio.Plugin;
    private Configuration Config => studio.Config;
    private StagehandBridge Bridge => studio.Bridge;
    private string surveyLabel = "level 1";
    private float surveyRadius = 25f;

    public void Draw(Studio studio)
    {
        this.studio = studio;
        DrawTools();
    }

    private void DrawTools()
    {
        if (ImGui.Button("Test checklist")) plugin.OpenChecklist();
        ImGui.SameLine();
        if (ImGui.Button("Recheck Stagehand")) Bridge.Refresh();

        ImGui.Separator();
        ImGui.TextUnformatted("Light survey (reads the game's own lights, changes nothing):");
        ImGui.SetNextItemWidth(140);
        ImGui.InputText("Label##survey", ref surveyLabel, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.SliderFloat("Radius (m)##survey", ref surveyRadius, 5f, 100f);
        if (ImGui.Button("Survey lights")) plugin.RunSurvey(surveyLabel, surveyRadius);
        ImGui.TextDisabled("Set the label to the housing brightness level, press Survey, change the level, repeat.");
        ImGui.Separator();

        if (ImGui.Button("Six-light test Scene")) plugin.ApplySixLightTest();
        ImGui.SameLine();
        ImGui.TextDisabled("Applies a Scene with six lights (the cap). All six should show.");
        ImGui.Separator();

        if (ImGui.Button("Smoke test (red light)")) plugin.PlaceSmoke();
        ImGui.SameLine();
        if (ImGui.Button("Remove smoke")) plugin.RemoveSmoke();
    }
}

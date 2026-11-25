using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundstage.Look;

namespace Soundstage.Tabs;

/// <summary>Part 3's probe: talks to the ReShade add-on over its pipe. Dev mode only (/soundstage dev); Part 11 builds the real Look tab.</summary>
public sealed class ProbeReShadeTab : ITab
{
    public string Name => "ReShade probe";
    public int Order => 92;
    public bool IsVisible(Studio studio) => studio.Config.DevMode;

    private readonly ReShadeProbe probe = new();
    private string presetPath = @".\reshade-presets\";
    private string uniformName = "";
    private float uniformValue = 1f;
    private string shotPath = Path.Combine(Path.GetTempPath(), "soundstage-probe.png");
    private string lastCommand = "";
    private volatile string lastReply = "(nothing sent yet)";
    private volatile bool lastOk = true;
    private volatile bool busy;

    public void Draw(Studio studio)
    {
        ImGui.TextWrapped("Talks to the Soundstage ReShade add-on (soundstage.addon64, copied next to dxgi.dll by you). The Test checklist, ReShade area, says what to try.");
        ImGui.Separator();

        if (ImGui.Button("Ping")) Send(ReShadeProtocol.Ping(), studio);
        ImGui.SameLine();
        if (ImGui.Button("Current preset")) Send(ReShadeProtocol.Status(), studio);
        ImGui.SameLine();
        if (ImGui.Button("List uniforms")) Send(ReShadeProtocol.Uniforms(), studio);

        ImGui.SetNextItemWidth(420);
        ImGui.InputText("##preset", ref presetPath, 400);
        ImGui.SameLine();
        if (ImGui.Button("Switch preset")) TrySend(() => ReShadeProtocol.Preset(presetPath), studio);
        ImGui.TextDisabled("A path under the game folder, like .\\reshade-presets\\<pack>\\<name>.ini. A path that does not exist leaves the current preset alone.");

        ImGui.SetNextItemWidth(200);
        ImGui.InputText("Uniform##name", ref uniformName, 100);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.DragFloat("Value##uniform", ref uniformValue, 0.01f);
        ImGui.SameLine();
        if (ImGui.Button("Set uniform")) TrySend(() => ReShadeProtocol.Uniform(uniformName, uniformValue), studio);

        ImGui.SetNextItemWidth(420);
        ImGui.InputText("##shot", ref shotPath, 400);
        ImGui.SameLine();
        if (ImGui.Button("Take shot")) TrySend(() => ReShadeProtocol.Shot(shotPath), studio);

        ImGui.Separator();
        if (busy) ImGui.TextDisabled("Waiting for the add-on...");
        if (lastCommand.Length > 0) ImGui.TextDisabled("Sent: " + lastCommand);
        var colour = lastOk ? new Vector4(0.5f, 0.9f, 0.5f, 1f) : new Vector4(1f, 0.55f, 0.45f, 1f);
        ImGui.TextColored(colour, lastOk ? "OK: " + lastReply : "Failed: " + lastReply);
    }

    private void TrySend(Func<string> build, Studio studio)
    {
        string line;
        try { line = build(); }
        catch (ArgumentException ex) { lastOk = false; lastReply = ex.Message; lastCommand = ""; return; }
        Send(line, studio);
    }

    private void Send(string line, Studio studio)
    {
        if (busy) return;
        busy = true;
        lastCommand = line;
        _ = Task.Run(async () =>
        {
            var reply = await probe.SendAsync(line).ConfigureAwait(false);
            lastOk = reply.Ok;
            lastReply = reply.Text;
            busy = false;
            studio.Log.Information($"[Soundstage/RESHADE] {line} -> {(reply.Ok ? "OK" : "ERR")} {reply.Text}");
        });
    }
}

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundstage.Look;

namespace Soundstage.Tabs;

/// <summary>
/// The Look tab: which route is live, a ReShade preset for each look slot, a preview button per slot, and auto focus.
/// It reads ReShade.ini (to find the presets folder and the auto-save setting) and never writes it.
/// </summary>
public sealed class LookTab : ITab
{
    public string Name => "Look";
    public int Order => 50;

    private static readonly Vector4 Green = new(0.5f, 0.9f, 0.5f, 1f);
    private static readonly Vector4 Amber = new(1f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 Red = new(1f, 0.55f, 0.45f, 1f);

    /// <summary>Names some depth-of-field shaders use. Shader packs differ, so this is a nudge: "Settings from ReShade" lists the real ones.</summary>
    private static readonly string[] CommonFocusNames = { "ManualFocusPlane", "FocusPlane", "FocusDepth", "fDOF_FocusDepth", "DOF_FocusPlane" };

    private ReShadeIni? ini;
    private List<string> presets = new();
    private string presetFolder = "";
    private bool loaded;
    private string presetFilter = "";
    private string uniformFilter = "";

    public void Draw(Studio studio)
    {
        var look = studio.Look;
        var settings = look.Settings;
        if (!loaded) Reload(look, settings);

        DrawRoute(look, settings);
        ImGui.Separator();
        DrawSlots(studio, look, settings);
        ImGui.Separator();
        DrawAutoFocus(studio, look, settings);
    }

    private void Reload(LookService look, LookSettings settings)
    {
        ini = ReShadeIni.TryLoad(look.GameFolder);
        presetFolder = settings.PresetFolderOverride.Length > 0 ? settings.PresetFolderOverride : ini?.PresetRoot() ?? "";
        presets = presetFolder.Length > 0 ? Slots.ListPresets(look.GameFolder, presetFolder) : new();
        loaded = true;
    }

    private void DrawRoute(LookService look, LookSettings settings)
    {
        var (label, colour) = look.Route switch
        {
            LookRoute.Pipe => ("Switching presets through the Soundstage ReShade add-on", Green),
            LookRoute.Manual => ("Manual: Soundstage tells you which preset to pick", Amber),
            _ => ("Checking for the add-on...", Amber),
        };
        ImGui.TextColored(colour, label);
        ImGui.TextWrapped(look.RouteReason);
        if (look.AddonVersion is int v) ImGui.TextDisabled($"Add-on version {v} (this build speaks {ReShadeProtocol.PluginVersion}).");
        if (ImGui.Button("Check again")) { _ = look.ProbeAsync(); Reload(look, settings); }
        ImGui.SameLine();
        if (ImGui.Button("Copy game folder")) ImGui.SetClipboardText(look.GameFolder);
        ImGui.SameLine();
        ImGui.TextDisabled("Copy soundstage.addon64 here, next to dxgi.dll, then restart the game.");

        if (look.Route == LookRoute.Manual)
        {
            ImGui.TextWrapped("No add-on means Soundstage cannot change ReShade for you. Pick the preset by hand in ReShade's overlay, or bind a shortcut key to it there (Soundstage never edits ReShade.ini).");
            if (look.ManualPreset.Length > 0)
            {
                ImGui.TextUnformatted("Last preset a Scene asked for: " + look.ManualPreset);
                ImGui.SameLine();
                if (ImGui.SmallButton("Copy name")) ImGui.SetClipboardText(look.ManualPreset);
            }
        }

        if (ini?.AutoSavePreset == true)
            ImGui.TextColored(Amber, "ReShade is set to save your preset automatically (AutoSavePreset=1 in ReShade.ini), so a focus or other setting Soundstage changes can end up saved into your preset file. Soundstage puts them back when a Scene comes off, but if you want it surer, turn Auto-save off in ReShade's Settings.");
        else if (ini == null)
            ImGui.TextDisabled("ReShade.ini was not found in the game folder, so the preset folder is unknown. Check the game folder above.");

        if (look.Status.Length > 0) ImGui.TextColored(look.StatusOk ? Green : Red, look.Status);
        if (look.LookApplied)
        {
            ImGui.TextUnformatted("Your own preset before a Scene changed it: " + look.PreviousPreset);
            ImGui.SameLine();
            if (ImGui.SmallButton("Put it back")) look.RemoveLook();
        }
    }

    private void DrawSlots(Studio studio, LookService look, LookSettings settings)
    {
        ImGui.TextUnformatted("Look slots");
        ImGui.TextDisabled("A Scene names a mood, and you choose which of your presets is that mood. An unmapped slot does nothing.");
        ImGui.TextDisabled(presetFolder.Length > 0 ? $"Preset folder (from ReShade.ini): {presetFolder}, {presets.Count} presets." : "No preset folder found yet.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Rescan")) Reload(look, settings);

        var changed = false;
        foreach (var slot in Slots.All(studio.Presets))
        {
            ImGui.PushID("slot" + slot);
            var path = Slots.Resolve(slot, settings);
            ImGui.SetNextItemWidth(130);
            ImGui.TextUnformatted(slot);
            ImGui.SameLine(150);
            ImGui.SetNextItemWidth(360);
            var shown = path == null ? "(not mapped)" : path;
            if (ImGui.BeginCombo("##map", shown))
            {
                ImGui.SetNextItemWidth(300);
                ImGui.InputTextWithHint("##filter", "type to filter", ref presetFilter, 100);
                if (ImGui.Selectable("(not mapped)", path == null)) { Slots.Map(settings, slot, null); changed = true; }
                var shownCount = 0;
                foreach (var p in presets)
                {
                    if (presetFilter.Length > 0 && !p.Contains(presetFilter, StringComparison.OrdinalIgnoreCase)) continue;
                    if (++shownCount > 200) { ImGui.TextDisabled("More than 200 match: type more of the name."); break; }
                    if (ImGui.Selectable(p, p == path)) { Slots.Map(settings, slot, p); changed = true; }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (path == null) ImGui.BeginDisabled();
            if (ImGui.SmallButton("Preview")) look.PreviewSlot(slot);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy")) ImGui.SetClipboardText(path ?? "");
            if (path == null) ImGui.EndDisabled();
            if (path != null && !Slots.PresetExists(look.GameFolder, path))
            {
                ImGui.SameLine();
                ImGui.TextColored(Red, "missing");
            }
            ImGui.PopID();
        }
        if (changed) look.Commit();
    }

    private void DrawAutoFocus(Studio studio, LookService look, LookSettings settings)
    {
        ImGui.TextUnformatted("Auto focus");
        ImGui.TextWrapped("Sets a depth-of-field setting from the camera's distance to the head. It needs the add-on (version 2), a GPose camera, and the name of the focus setting your depth-of-field shader uses.");
        var changed = false;

        var name = settings.FocusUniform;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Focus setting##uniform", ref name, 100)) { settings.FocusUniform = name.Trim(); changed = true; }
        if (ImGui.BeginCombo("##pick", "choose a name..."))
        {
            ImGui.TextDisabled("Common names (shader packs differ):");
            foreach (var n in CommonFocusNames)
                if (ImGui.Selectable(n)) { settings.FocusUniform = n; changed = true; }
            ImGui.Separator();
            ImGui.TextDisabled($"From ReShade ({look.Uniforms.Length}):");
            ImGui.SetNextItemWidth(260);
            ImGui.InputTextWithHint("##ufilter", "type to filter", ref uniformFilter, 60);
            var count = 0;
            foreach (var n in look.Uniforms)
            {
                if (uniformFilter.Length > 0 && !n.Contains(uniformFilter, StringComparison.OrdinalIgnoreCase)) continue;
                if (++count > 150) { ImGui.TextDisabled("More match: type more."); break; }
                if (ImGui.Selectable(n)) { settings.FocusUniform = n; changed = true; }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Settings from ReShade")) look.RefreshUniforms();

        var scale = settings.FocusScale;
        var offset = settings.FocusOffset;
        var lowest = settings.FocusMin;
        var highest = settings.FocusMax;
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragFloat("Scale##fs", ref scale, 0.01f, -1000f, 1000f)) { settings.FocusScale = scale; changed = true; }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragFloat("Offset##fo", ref offset, 0.01f, -1000f, 1000f)) { settings.FocusOffset = offset; changed = true; }
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragFloat("Lowest##fmin", ref lowest, 0.01f, 0f, 1000f)) { settings.FocusMin = lowest; changed = true; }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        if (ImGui.DragFloat("Highest##fmax", ref highest, 0.1f, 0f, 100000f)) { settings.FocusMax = highest; changed = true; }
        ImGui.TextDisabled("The setting gets: distance in metres x Scale + Offset, kept between Lowest and Highest. The defaults pass metres straight through.");

        var preview = look.PreviewFocus;
        if (ImGui.Checkbox("Try auto focus now (this session only)", ref preview)) look.PreviewFocus = preview;

        var scene = studio.Current;
        if (scene != null)
        {
            var on = scene.Look?.AutoFocus == true;
            if (scene.Look == null) ImGui.BeginDisabled();
            if (ImGui.Checkbox("Auto focus for the applied Scene (" + scene.Name + ")", ref on) && scene.Look != null)
            {
                scene.Look.AutoFocus = on ? true : null;
                look.SetSceneAutoFocus(on);
            }
            if (scene.Look == null) ImGui.EndDisabled();
            if (scene.Look == null) ImGui.TextDisabled("This Scene has no look slot, so it has no auto focus switch. It is off by default for every Scene.");
        }

        if (look.AutoFocusActive && look.FocusSource?.Invoke() is AutoFocusInput point && AutoFocus.Distance(point) is float metres)
            ImGui.TextDisabled($"Camera to head: {metres:F2} m, focus setting {AutoFocus.Map(metres, settings):F2}.");
        else if (settings.FocusUniform.Length == 0)
            ImGui.TextDisabled("Name the focus setting above to switch it on.");

        if (changed) look.Commit();
    }
}

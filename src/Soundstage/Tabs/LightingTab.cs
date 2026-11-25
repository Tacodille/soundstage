using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Soundstage.Tabs;

/// <summary>
/// Scene picker, a block of controls for every light in the applied Scene (the prototype adjusted only the first), role colours,
/// the gel picker, colour harmony, animation controls, exposure badges and the marker toggle. Slider moves re-send the stage
/// live, as the prototype did.
/// </summary>
public sealed class LightingTab : ITab
{
    public string Name => "Lighting";
    public int Order => 10;

    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Amber = new(1.0f, 0.72f, 0.15f, 1f);
    private static readonly Vector4 Red = new(1.0f, 0.3f, 0.25f, 1f);

    private Studio studio = null!;
    private Plugin plugin => studio.Plugin;
    private Configuration Config => studio.Config;
    private StagehandBridge Bridge => studio.Bridge;
    private LightingService Lighting => plugin.Lighting;
    private string saveName = "";
    private int harmonyKind;
    private Vector3 harmonyBase = new(1f, 0.3f, 0.2f);
    private bool harmonyBaseSet;
    private string harmonyMessage = "";
    private string measureMessage = "";

    public void Draw(Studio studio)
    {
        this.studio = studio;
        // Part 15: with no Stagehand there is nothing for these controls to talk to, so grey them and say why.
        var greyed = !Bridge.Available;
        if (greyed)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.45f, 1f), "Stagehand did not answer, so the lights are switched off here.");
            ImGui.TextDisabled("Install and enable Stagehand 0.5.x, then press Recheck on the Status tab.");
            ImGui.BeginDisabled();
        }
        try
        {
            DrawPresetPicker();
            ImGui.Separator();
            DrawLights();
            ImGui.Separator();
            DrawHarmony();
            ImGui.Separator();
            DrawAnimation();
            ImGui.Separator();
            DrawExposureSettings();
            ImGui.Separator();
            DrawToggles();
            ImGui.Separator();
            DrawSave();
        }
        finally { if (greyed) ImGui.EndDisabled(); }
    }

    // ---- Scene picker (as moved from the prototype) ----

    private void DrawPresetPicker()
    {
        var selected = plugin.SelectedPreset();
        if (ImGui.BeginCombo("Scene", selected?.Name ?? "(none)"))
        {
            // Categories in order of first appearance; "Mine" last.
            var cats = plugin.Presets.Select(p => p.Category).Distinct().OrderBy(c => c == "Mine" ? 1 : 0).ToList();
            foreach (var cat in cats)
            {
                ImGui.TextDisabled(cat);
                foreach (var p in plugin.Presets.Where(p => p.Category == cat))
                {
                    if (ImGui.Selectable("   " + p.Name + "###" + p.Id, p.Id == Config.SelectedPresetId))
                        plugin.SelectScene(p.Id, null);
                }
            }
            ImGui.EndCombo();
        }

        if (selected != null && selected.Palettes.Count > 1)
        {
            var current = plugin.SelectedPalette;
            if (!selected.Palettes.ContainsKey(current)) current = selected.EffectiveDefaultPalette;
            if (ImGui.BeginCombo("Palette", current))
            {
                foreach (var name in selected.Palettes.Keys)
                    if (ImGui.Selectable(name, name == current)) plugin.SelectScene(selected.Id, name);
                ImGui.EndCombo();
            }
        }

        if (selected != null)
        {
            if (!string.IsNullOrWhiteSpace(selected.Description)) ImGui.TextWrapped(selected.Description);
            if (!string.IsNullOrWhiteSpace(selected.BestIn)) ImGui.TextWrapped("Best in: " + selected.BestIn);
            ImGui.TextDisabled($"{selected.Lights.Count} light(s){(selected.Backdrop != null ? ", with backdrop" : "")}");
        }

        if (ImGui.Button("Apply") && selected != null) plugin.Apply(selected, plugin.SelectedPalette);
        ImGui.SameLine();
        if (ImGui.Button("Remove all")) plugin.RemoveAll();
        ImGui.SameLine();
        if (ImGui.Button("Reload scenes")) plugin.ReloadPresets();

        if (!string.IsNullOrEmpty(plugin.Message)) ImGui.TextWrapped(plugin.Message);
    }

    // ---- Every light ----

    private void DrawLights()
    {
        var cur = plugin.Current;
        if (cur == null || cur.Lights.Count == 0)
        {
            ImGui.TextDisabled("Adjust the Scene: apply one first.");
            return;
        }

        ImGui.TextUnformatted($"Lights in {cur.Name}");
        ImGui.TextDisabled(Lighting.HeadStatus);

        var changed = false;
        for (var i = 0; i < cur.Lights.Count; i++)
        {
            ImGui.PushID(i);
            changed |= DrawLight(cur.Lights[i], i == 0 ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            ImGui.PopID();
        }
        if (cur.Backdrop != null)
        {
            ImGui.PushID(100);
            var col = new Vector3(cur.Backdrop.Color[0], cur.Backdrop.Color[1], cur.Backdrop.Color[2]);
            if (ImGui.ColorEdit3("Backdrop colour", ref col)) { cur.Backdrop.Color = new[] { col.X, col.Y, col.Z }; changed = true; }
            ImGui.PopID();
            if (cur.Backdrop.Light != null)
            {
                ImGui.PushID(101);
                changed |= DrawLight(cur.Backdrop.Light, ImGuiTreeNodeFlags.None);
                ImGui.PopID();
            }
        }

        // Same stage id, so this is the live-update test: only re-send once the lights exist.
        if (changed && Bridge.Placed) plugin.PlaceNow();
    }

    private bool DrawLight(SceneLight l, ImGuiTreeNodeFlags flags)
    {
        var changed = false;
        var (text, colour) = ExposureBadge(l);
        var header = $"{l.Role}: {l.Name}";
        var open = ImGui.CollapsingHeader(header + "###light", flags);
        ImGui.SameLine();
        ImGui.TextColored(colour, text);
        if (!open) return false;

        var rangeDirty = false;

        var intensity = l.Intensity;
        if (ImGui.SliderFloat("Intensity", ref intensity, 0f, 20f)) { l.Intensity = intensity; rangeDirty = true; }

        var c = new Vector3(l.Color[0], l.Color[1], l.Color[2]);
        if (ImGui.ColorEdit3("Colour (" + l.Role + ")", ref c)) { l.Color = new[] { c.X, c.Y, c.Z }; rangeDirty = true; }

        if (ImGui.BeginCombo("Gel", "pick a gel..."))
        {
            if (Lighting.Gels.Count == 0) ImGui.TextDisabled("gels.json is missing or empty.");
            foreach (var gel in Lighting.Gels)
            {
                var g = gel.Colour;
                ImGui.ColorButton("##sw" + gel.Name, new Vector4(g[0], g[1], g[2], 1f), ImGuiColorEditFlags.NoTooltip, new Vector2(14, 14));
                ImGui.SameLine();
                if (ImGui.Selectable(gel.Name + "###gel" + gel.Name)) { l.Color = (float[])g.Clone(); rangeDirty = true; }
                if (ImGui.IsItemHovered() && gel.Note.Length > 0) ImGui.SetTooltip(gel.Note);
            }
            ImGui.EndCombo();
        }

        var az = l.AzimuthDeg;
        if (ImGui.SliderFloat("Angle (+ = left)", ref az, -180f, 180f)) { l.AzimuthDeg = az; changed = true; }
        var dist = l.Distance;
        if (ImGui.SliderFloat("Distance (m)", ref dist, 0.5f, 5f)) { l.Distance = dist; changed = true; }
        var height = l.Height;
        if (ImGui.SliderFloat("Height (m)", ref height, 0f, 4f)) { l.Height = height; changed = true; }

        var face = AimSolver.IsFaceAimed(l);
        if (ImGui.Checkbox("Follow the face", ref face)) { l.AimAt = face ? AimSolver.Face : null; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Places this light relative to your head instead of a fixed height, so the shape of the light lands on the face.");
        ImGui.SameLine();
        ImGui.TextDisabled($"range {l.Range:F1} m (set by the brightness rule)");

        if (rangeDirty)
        {
            l.Range = SceneStore.GameRange(l);   // the brightest channel and the intensity set the range
            changed = true;
        }
        return changed;
    }

    private (string Text, Vector4 Colour) ExposureBadge(SceneLight l)
    {
        var settings = Lighting.Settings;
        var face = Lighting.LastFace ?? AimSolver.FallbackFace(l);
        var d = Exposure.SubjectDistance(l, face);
        var value = Exposure.Estimate(l, d);
        var level = Exposure.Level(value, settings);
        var colour = level switch { ExposureLevel.Clip => Red, ExposureLevel.Hot => Amber, _ => Green };
        return ($"[{Exposure.Describe(level)} {value:F2} at {d:F1} m]", colour);
    }

    // ---- Harmony ----

    private void DrawHarmony()
    {
        ImGui.TextUnformatted("Colour harmony");
        var cur = plugin.Current;
        if (cur == null) { ImGui.TextDisabled("Apply a Scene first."); return; }

        if (!harmonyBaseSet && cur.Lights.Count > 0)
        {
            harmonyBase = new Vector3(cur.Lights[0].Color[0], cur.Lights[0].Color[1], cur.Lights[0].Color[2]);
            harmonyBaseSet = true;
        }
        if (ImGui.BeginCombo("Harmony", Harmony.Kinds[harmonyKind]))
        {
            for (var i = 0; i < Harmony.Kinds.Length; i++)
                if (ImGui.Selectable(Harmony.Kinds[i], i == harmonyKind)) harmonyKind = i;
            ImGui.EndCombo();
        }
        ImGui.ColorEdit3("Base colour (the key)", ref harmonyBase);
        if (ImGui.Button("Make palette"))
        {
            var roles = cur.Lights.Select(l => l.Role).ToList();
            if (cur.Backdrop?.Light != null) roles.Add(cur.Backdrop.Light.Role);
            if (cur.Backdrop != null) roles.Add("backdrop");
            var map = Harmony.Palette(Harmony.Kinds[harmonyKind], new[] { harmonyBase.X, harmonyBase.Y, harmonyBase.Z }, roles);
            foreach (var l in cur.Lights.Concat(cur.Backdrop?.Light != null ? new[] { cur.Backdrop.Light } : Array.Empty<SceneLight>()))
                if (map.TryGetValue(l.Role, out var col)) { l.Color = col; l.Range = SceneStore.GameRange(l); }
            if (cur.Backdrop != null && map.TryGetValue("backdrop", out var bc)) cur.Backdrop.Color = bc;
            var name = "Harmony: " + Harmony.Kinds[harmonyKind];
            cur.Palettes[name] = map;
            harmonyMessage = $"Made the palette '{name}' on this Scene and applied it. Save current as... keeps it.";
            if (Bridge.Placed) plugin.PlaceNow();
        }
        if (harmonyMessage.Length > 0) ImGui.TextWrapped(harmonyMessage);
    }

    // ---- Animation ----

    private void DrawAnimation()
    {
        ImGui.TextUnformatted("Animation");
        var cur = plugin.Current;
        var settings = Lighting.Settings;
        if (cur?.Animation == null || cur.Animation.Effects.Count == 0)
        {
            ImGui.TextDisabled("This Scene has no animation. (Neon split, Club strobe, Campfire flicker and Neon sign flicker do.)");
            return;
        }

        var play = settings.PlayAnimation;
        if (ImGui.Checkbox("Play animation", ref play)) { settings.PlayAnimation = play; Lighting.SaveSettings(); if (Bridge.Placed) plugin.PlaceNow(); }

        var hz = settings.MaxAnimationHz;
        if (ImGui.SliderFloat("Most re-sends per second", ref hz, 2f, Animator.HardMaxHz)) { settings.MaxAnimationHz = hz; Lighting.SaveSettings(); }
        ImGui.TextDisabled($"Running at {Lighting.CurrentHz:F1} Hz. It slows itself down if a re-send costs more than {settings.FrameBudgetMs:F0} ms.");

        for (var i = 0; i < cur.Animation.Effects.Count; i++)
        {
            var e = cur.Animation.Effects[i];
            ImGui.PushID(200 + i);
            var on = e.Enabled;
            var label = e.Kind == "strobe" ? $"Strobe on {e.Target}" : $"{char.ToUpperInvariant(e.Kind[0])}{e.Kind[1..]} on {e.Target}";
            if (ImGui.Checkbox(label, ref on))
            {
                e.Enabled = on;
                if (Bridge.Placed) plugin.PlaceNow();
            }
            if (e.Enabled)
            {
                ImGui.SameLine();
                var amount = e.Amount;
                ImGui.SetNextItemWidth(120);
                if (ImGui.SliderFloat("amount", ref amount, 0f, 1f)) e.Amount = amount;
                ImGui.SameLine();
                var rate = e.RateHz;
                var max = e.Kind == "strobe" ? settings.StrobeMaxHz : Animator.HardMaxHz;
                ImGui.SetNextItemWidth(120);
                if (ImGui.SliderFloat("per second", ref rate, 0.05f, max)) e.RateHz = rate;
            }
            ImGui.PopID();
        }

        if (cur.Animation.Effects.Any(e => e.Kind == "strobe"))
            ImGui.TextColored(Amber, $"Flashing light can affect people with photosensitive epilepsy. The strobe is off until you tick it and never goes above {settings.StrobeMaxHz:F0} flashes a second.");

        if (ImGui.Button("Measure frame cost (10 s)")) measureMessage = Lighting.StartMeasure();
        if (Lighting.Measuring != LightingService.MeasurePhase.None) ImGui.TextDisabled("Measuring: animation " + (Lighting.Measuring == LightingService.MeasurePhase.Off ? "off" : "on") + "...");
        else if (Lighting.LastMeasure.Length > 0) ImGui.TextWrapped(Lighting.LastMeasure);
        if (measureMessage.Length > 0) ImGui.TextWrapped(measureMessage);
        ImGui.TextDisabled("Frame times also go to dalamud.log under [Soundstage/ANIM].");
    }

    // ---- Exposure ----

    private void DrawExposureSettings()
    {
        ImGui.TextUnformatted("Exposure warning");
        var s = Lighting.Settings;
        var amber = s.AmberThreshold;
        var red = s.RedThreshold;
        var changed = false;
        if (ImGui.SliderFloat("Amber from", ref amber, 0.2f, 10f)) { s.AmberThreshold = amber; changed = true; }
        if (ImGui.SliderFloat("Red from", ref red, 0.2f, 20f)) { s.RedThreshold = MathF.Max(red, s.AmberThreshold); changed = true; }
        if (ImGui.Button("Reset to the starting guess")) { s.AmberThreshold = LightingSettings.DefaultAmber; s.RedThreshold = LightingSettings.DefaultRed; changed = true; }
        if (changed) Lighting.SaveSettings();
        ImGui.TextDisabled("Each light's value at the face is max colour x intensity / (falloff x distance^n). " + LightingSettings.ThresholdNote);
    }

    // ---- Toggles and save ----

    private void DrawToggles()
    {
        var follow = Config.FollowPlayer;
        if (ImGui.Checkbox("Follow player", ref follow)) { Config.FollowPlayer = follow; plugin.Save(); }

        var markers = Config.ShowLightMarkers;
        if (ImGui.Checkbox("Show light markers", ref markers)) { Config.ShowLightMarkers = markers; plugin.Save(); }
    }

    private void DrawSave()
    {
        ImGui.InputText("Name##save", ref saveName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save current as...")) plugin.SaveCurrentAs(saveName);
        ImGui.TextDisabled("Writes the applied Scene, slider tweaks and any harmony palette included, to user-scenes.json (shown under Mine).");
    }
}

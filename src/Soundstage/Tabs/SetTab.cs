using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Soundstage.Tabs;

/// <summary>
/// The set the Scene stands in: the sweep (or the old panel), prop kits, atmosphere items and a picture. Everything
/// here edits the applied Scene's own blocks, so Save current as keeps it. Sliders re-place the stage when released, not
/// on every frame, because a sweep is several objects.
/// </summary>
public sealed class SetTab : ITab
{
    public string Name => "Set";
    public int Order => 20;

    private Studio studio = null!;
    private Plugin plugin => studio.Plugin;
    private Configuration Config => studio.Config;
    private StagehandBridge Bridge => studio.Bridge;

    private string picturePath = "";
    private string pictureStatus = "";
    private string lastScene = "";

    public void Draw(Studio studio)
    {
        this.studio = studio;
        var scene = plugin.Current;
        if (scene == null)
        {
            ImGui.TextWrapped("Apply a Scene on the Lighting tab first. The set is part of the Scene, so it needs one to edit.");
            ImGui.Separator();
            DrawCanvasTest();
            return;
        }
        if (lastScene != scene.Id) { lastScene = scene.Id; picturePath = scene.Set?.Picture?.Path ?? ""; pictureStatus = ""; }

        ImGui.TextUnformatted($"Scene: {scene.Name}");
        if (SetApply.LastSummary.Length > 0) ImGui.TextDisabled("Last build: " + SetApply.LastSummary);
        ImGui.Separator();

        if (ImGui.CollapsingHeader("Backdrop", ImGuiTreeNodeFlags.DefaultOpen)) DrawBackdrop(scene);
        if (ImGui.CollapsingHeader("Prop kits", ImGuiTreeNodeFlags.DefaultOpen)) DrawKits(scene);
        if (ImGui.CollapsingHeader("Atmosphere", ImGuiTreeNodeFlags.DefaultOpen)) DrawAtmosphere(scene);
        if (ImGui.CollapsingHeader("Picture")) DrawPicture(scene);
        if (ImGui.CollapsingHeader("Canvas test")) DrawCanvasTest();
    }

    /// <summary>Re-sends the stage if one is up, so the change shows now.</summary>
    private void Replace()
    {
        if (Bridge.Placed) plugin.PlaceNow();
    }

    // ---- Backdrop ----

    private void DrawBackdrop(Scene scene)
    {
        var set = scene.Set;
        var kind = set?.Kind ?? "none";
        if (kind == "none" && scene.Backdrop != null) kind = "partition";   // an old Scene with only the panel

        var show = Config.ShowBackdrop;
        if (ImGui.Checkbox("Show backdrop", ref show))
        {
            Config.ShowBackdrop = show;
            plugin.Save();
            plugin.Bridge.Info($"Show backdrop now {(show ? "on" : "off")}.");
            Replace();
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(off keeps the lights and drops the sweep and panel; saving then saves without them)");

        ImGui.TextUnformatted("Type:");
        foreach (var (id, label) in new[] { ("none", "None"), ("sweep", "Sweep"), ("partition", "Panel") })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(label + "##kind", kind == id))
            {
                scene.Set ??= new SceneSet();
                scene.Set.Kind = id;
                if (id == "sweep" && scene.Set.Color == null && string.IsNullOrEmpty(scene.Set.ColorRole))
                    scene.Set.ColorRole = scene.Palettes.Count > 0 ? "backdrop" : "";
                if (id == "partition" && scene.Backdrop == null) scene.Backdrop = new SceneBackdrop();
                Replace();
            }
        }
        set = scene.Set;
        if (set == null || set.Kind == "none") { ImGui.TextDisabled("No backdrop."); return; }

        if (set.Kind == "sweep") DrawSweep(scene, set);
        else DrawPanelFixes();
    }

    private void DrawSweep(Scene scene, SceneSet set)
    {
        var colour = SetApply.SweepColour(scene);
        var c3 = new Vector3(colour.X, colour.Y, colour.Z);
        if (ImGui.ColorEdit3("Sweep colour", ref c3))
        {
            set.Color = new[] { c3.X, c3.Y, c3.Z };
            set.ColorRole = "";
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Replace();
        if (!string.IsNullOrEmpty(set.ColorRole))
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"from the palette ({set.ColorRole})");
        }
        else if (scene.Palettes.Values.Any(p => p.ContainsKey("backdrop")))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("Use palette colour")) { set.ColorRole = "backdrop"; Replace(); }
        }

        var width = set.SweepWidth;
        if (ImGui.SliderFloat("Width (m)", ref width, SweepBuilder.MinWidth, SweepBuilder.MaxWidth)) set.SweepWidth = width;
        if (ImGui.IsItemDeactivatedAfterEdit()) Replace();

        var spec = SetApply.SweepSpecFor(scene);
        var distance = set.SweepDistance > 0f ? set.SweepDistance : spec.Distance;
        if (ImGui.SliderFloat("Distance behind you (m)", ref distance, SweepBuilder.MinDistance, SweepBuilder.MaxDistance)) set.SweepDistance = distance;
        if (ImGui.IsItemDeactivatedAfterEdit()) Replace();

        var cove = set.SweepCove;
        if (ImGui.SliderFloat("Cove size (m)", ref cove, SweepBuilder.MinCove, SweepBuilder.MaxCove)) set.SweepCove = cove;
        if (ImGui.IsItemDeactivatedAfterEdit()) Replace();

        var pieces = SweepBuilder.Layout(SetApply.SweepSpecFor(scene)).Count;
        var ext = SweepBuilder.FloorExtent(SetApply.SweepSpecFor(scene));
        ImGui.TextDisabled($"{pieces} pieces of the white partition (4 m x 3 m each). Floor from {-ext.MinZ:0.0} m behind you to {ext.MaxZ:0.0} m in front.");
    }

    /// <summary>The old panel's live fixes, kept for a Scene whose backdrop is the single panel.</summary>
    private void DrawPanelFixes()
    {
        if (plugin.Current?.Backdrop == null) { ImGui.TextDisabled("This Scene has no panel."); return; }
        var b = Config.Backdrop;
        var changed = false;

        var sx = b.ScaleX;
        if (ImGui.SliderFloat("Scale X (width)", ref sx, 0.1f, 6f)) { b.ScaleX = sx; changed = true; }
        var sy = b.ScaleY;
        if (ImGui.SliderFloat("Scale Y (height)", ref sy, 0.1f, 6f)) { b.ScaleY = sy; changed = true; }
        var d = b.DistanceOffset;
        if (ImGui.SliderFloat("Distance offset (m)", ref d, -2f, 6f)) { b.DistanceOffset = d; changed = true; }
        var h = b.HeightOffset;
        if (ImGui.SliderFloat("Height offset (m)", ref h, -3f, 4f)) { b.HeightOffset = h; changed = true; }

        ImGui.TextUnformatted("Yaw offset:");
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{yaw:0}##yaw{yaw}", Math.Abs(b.YawOffsetDeg - yaw) < 0.5f)) { b.YawOffsetDeg = yaw; changed = true; }
        }
        if (ImGui.Button("Reset backdrop fixes")) { Config.Backdrop = new BackdropAdjust(); changed = true; }
        ImGui.TextDisabled("The panel is 4 m wide and 3 m tall at scale 1, with its foot on the floor. Scale values multiply the Scene's own scale.");

        if (changed)
        {
            plugin.Save();
            plugin.Bridge.Info($"Backdrop adjust now: {Config.Backdrop}");
            Replace();
        }
    }

    // ---- Prop kits ----

    private void DrawKits(Scene scene)
    {
        var kits = SetCatalogue.Kits;
        if (kits.Count == 0) { ImGui.TextDisabled("No kits found beside the plugin (kits folder)."); return; }
        var set = scene.Set ??= new SceneSet();

        foreach (var kit in kits)
        {
            ImGui.PushID("kit|" + kit.Id);
            var on = set.Props.Contains(kit.Id, StringComparer.OrdinalIgnoreCase);
            if (ImGui.Checkbox($"{kit.Name} ({kit.Objects.Count})", ref on))
            {
                if (on) set.Props.Add(kit.Id);
                else set.Props.RemoveAll(p => string.Equals(p, kit.Id, StringComparison.OrdinalIgnoreCase));
                Replace();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(kit.Description);
            ImGui.SameLine();
            ImGui.TextDisabled(kit.Status);

            if (on)
            {
                if (!set.KitPlacements.TryGetValue(kit.Id, out var pl)) pl = new KitPlacement();
                var changed = false;
                var x = pl.X;
                var z = pl.Z;
                ImGui.SetNextItemWidth(160f);
                if (ImGui.SliderFloat("Across", ref x, -3f, 3f)) { pl.X = x; changed = true; }
                var edited = ImGui.IsItemDeactivatedAfterEdit();
                ImGui.SameLine();
                ImGui.SetNextItemWidth(160f);
                if (ImGui.SliderFloat("Back/forward", ref z, -3f, 3f)) { pl.Z = z; changed = true; }
                edited |= ImGui.IsItemDeactivatedAfterEdit();
                ImGui.SameLine();
                if (ImGui.SmallButton("Flip 180")) { pl.YawDeg = (pl.YawDeg + 180f) % 360f; changed = true; edited = true; }
                ImGui.SameLine();
                if (ImGui.SmallButton("Reset")) { pl = new KitPlacement(); changed = true; edited = true; }
                if (changed) set.KitPlacements[kit.Id] = pl;
                if (edited) Replace();
            }
            ImGui.PopID();
        }
        ImGui.TextDisabled($"Each kit holds up to {KitStore.MaxObjects} objects. 'Across' moves it to your left (+) or right (-); 'Flip 180' turns a kit whose furniture faces the wrong way.");
    }

    // ---- Atmosphere ----

    private void DrawAtmosphere(Scene scene)
    {
        var items = SetCatalogue.Atmosphere;
        if (items.Count == 0) { ImGui.TextDisabled("No atmosphere items found beside the plugin (atmosphere.json)."); return; }
        var atmos = scene.Atmosphere ??= new SceneAtmosphere();
        var effective = AtmosphereBuilder.Effective(atmos);

        foreach (var item in items)
        {
            ImGui.PushID("atmos|" + item.Id);
            var choice = effective.FirstOrDefault(c => string.Equals(c.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            var on = choice != null;
            if (ImGui.Checkbox($"{item.Name}  [{item.Kind}]", ref on))
            {
                SetChoice(atmos, item.Id, on, choice?.Amount ?? 1f);
                Replace();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(item.Note);
            ImGui.SameLine();
            ImGui.TextDisabled(item.Status);
            if (on && choice != null)
            {
                var amount = choice.Amount;
                ImGui.SameLine();
                ImGui.SetNextItemWidth(120f);
                if (ImGui.SliderFloat(item.Kind == "sound" ? "Volume" : "Size", ref amount, 0.2f, 2.5f)) SetChoice(atmos, item.Id, true, amount);
                if (ImGui.IsItemDeactivatedAfterEdit()) Replace();
            }
            ImGui.PopID();
        }
        ImGui.TextDisabled("An item that does not show or sound in game is marked in the checklist and dropped from atmosphere.json.");
    }

    /// <summary>Records an on/off choice in the Scene's own list. An entry there overrides the old haze, dust and sun flags for that item.</summary>
    private static void SetChoice(SceneAtmosphere atmos, string id, bool enabled, float amount)
    {
        var existing = atmos.Items.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing == null) atmos.Items.Add(existing = new AtmosphereChoice { Id = id });
        existing.Enabled = enabled;
        existing.Amount = amount;
    }

    // ---- Picture ----

    private void DrawPicture(Scene scene)
    {
        ImGui.TextWrapped("Hangs your own picture (PNG or JPG, shrunk to 1024 px on the long side) on the portrait canvas model. Needs the canvas test to have passed in game (checklist, Set, step 6).");
        ImGui.InputText("File##pic", ref picturePath, 500);
        if (ImGui.Button("Hang it"))
        {
            if (!File.Exists(picturePath)) pictureStatus = "That file is not there.";
            else
            {
                try
                {
                    var (_, w, h) = PictureProp.FromFile(picturePath);
                    var set = scene.Set ??= new SceneSet();
                    set.Picture = new ScenePicture { Path = picturePath };
                    pictureStatus = $"Converted: {w} x {h} px.";
                    Replace();
                }
                catch (Exception ex) { pictureStatus = "Could not read that image: " + ex.Message; }
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Take it down") && scene.Set != null) { scene.Set.Picture = null; pictureStatus = "Picture removed."; Replace(); }
        if (pictureStatus.Length > 0) ImGui.TextDisabled(pictureStatus);

        if (scene.Set?.Picture is { } pic)
        {
            var edited = false;
            var height = pic.Height; var px = pic.X; var pz = pic.Z; var ph = pic.CentreHeight; var yaw = pic.YawDeg;
            if (ImGui.SliderFloat("Picture height (m)", ref height, 0.3f, 3f)) pic.Height = height;
            edited |= ImGui.IsItemDeactivatedAfterEdit();
            if (ImGui.SliderFloat("Across (m)", ref px, -3f, 3f)) pic.X = px;
            edited |= ImGui.IsItemDeactivatedAfterEdit();
            if (ImGui.SliderFloat("Back/forward (m)", ref pz, -3f, 3f)) pic.Z = pz;
            edited |= ImGui.IsItemDeactivatedAfterEdit();
            if (ImGui.SliderFloat("Centre height (m)", ref ph, 0.3f, 3.5f)) pic.CentreHeight = ph;
            edited |= ImGui.IsItemDeactivatedAfterEdit();
            if (ImGui.SliderFloat("Extra turn (deg)", ref yaw, -180f, 180f)) pic.YawDeg = yaw;
            edited |= ImGui.IsItemDeactivatedAfterEdit();
            if (edited) Replace();
        }
    }

    // ---- Canvas test (the old Tools-tab test, kept as it was) ----

    private void DrawCanvasTest()
    {
        if (ImGui.Button("Canvas test (portrait)")) plugin.PlaceCanvas();
        ImGui.SameLine();
        if (ImGui.Button("Remove canvas")) plugin.RemoveCanvas();

        var cy = Config.CanvasYawOffsetDeg;
        var ch = Config.CanvasHeightOffset;
        ImGui.TextUnformatted("Canvas yaw offset:");
        var canvasChanged = false;
        foreach (var yaw in new[] { 0f, 90f, 180f, 270f })
        {
            ImGui.SameLine();
            if (ImGui.RadioButton($"{yaw:0}##cyaw{yaw}", Math.Abs(cy - yaw) < 0.5f)) { Config.CanvasYawOffsetDeg = yaw; canvasChanged = true; }
        }
        if (ImGui.SliderFloat("Canvas height offset (m)", ref ch, -1f, 1f)) { Config.CanvasHeightOffset = ch; canvasChanged = true; }
        if (canvasChanged)
        {
            plugin.Save();
            if (Bridge.CanvasPlaced) plugin.PlaceCanvas();
        }
    }
}

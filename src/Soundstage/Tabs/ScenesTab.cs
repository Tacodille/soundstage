using System.Numerics;
using Dalamud.Bindings.ImGui;
using Soundstage.Poses;

namespace Soundstage.Tabs;

/// <summary>
/// The front door (Part 7.1): browse Scenes by category, tag and search, pick a palette, Apply, Remove all, Undo, Snapshot,
/// save your own and swap share codes. The Lighting tab keeps the per-light sliders.
/// </summary>
public sealed class ScenesTab : ITab
{
    public string Name => "Scenes";
    public int Order => 5;

    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Warn = new(1f, 0.8f, 0.4f, 1f);

    private string search = "";
    private string category = "All";
    private string tag = "Any";
    private string saveName = "";
    private string renameTo = "";
    private string pasted = "";
    private string importName = "";
    private ShareImport? import;
    private Scene? pendingApply;       // a Scene waiting on the "unsaved snapshot" question
    private string? pendingApplyPalette;
    private string? deleteId;
    private string? renameId;
    private string? copiedFor;

    public void Draw(Studio studio)
    {
        var plugin = studio.Plugin;
        var desk = studio.Desk;
        var all = plugin.Scenes;
        var selected = plugin.SelectedScene();

        DrawFilters(all);
        DrawList(plugin, all, selected);
        ImGui.Separator();
        if (selected == null) ImGui.TextDisabled("No Scene selected.");
        else { DrawDetails(plugin, desk, selected); PoseFitSection.Draw(studio, selected); }   // part 14: Poses that fit
        ImGui.Separator();
        DrawSnapshot(plugin, desk);
        ImGui.Separator();
        DrawShare(plugin, desk, selected);
        DrawMessages(plugin, desk);
        DrawConfirmApply(plugin);
        DrawConfirmDelete(desk, all);
    }

    // ---- Browse ----

    private void DrawFilters(IReadOnlyList<Scene> all)
    {
        ImGui.SetNextItemWidth(180f);
        ImGui.InputTextWithHint("##scenesearch", "Search name, tags, rig", ref search, 64);
        ImGui.SameLine();
        var cats = new List<string> { "All" };
        cats.AddRange(all.Select(s => s.Category).Distinct().OrderBy(c => c == "Mine" ? 1 : 0));
        ImGui.SetNextItemWidth(120f);
        if (ImGui.BeginCombo("Category", category))
        {
            foreach (var c in cats) if (ImGui.Selectable(c, c == category)) category = c;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var tags = new List<string> { "Any" };
        tags.AddRange(all.SelectMany(s => s.Tags).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        ImGui.SetNextItemWidth(120f);
        if (ImGui.BeginCombo("Tag", tag))
        {
            foreach (var t in tags) if (ImGui.Selectable(t, t == tag)) tag = t;
            ImGui.EndCombo();
        }
    }

    private bool Matches(Scene s)
    {
        if (category != "All" && s.Category != category) return false;
        if (tag != "Any" && !s.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;
        if (search.Length == 0) return true;
        return s.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || s.Rig.Contains(search, StringComparison.OrdinalIgnoreCase)
            || s.Description.Contains(search, StringComparison.OrdinalIgnoreCase) || s.BestIn.Contains(search, StringComparison.OrdinalIgnoreCase)
            || s.Tags.Any(t => t.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawList(Plugin plugin, IReadOnlyList<Scene> all, Scene? selected)
    {
        if (!ImGui.BeginChild("##scenelist", new Vector2(0, 230), true)) { ImGui.EndChild(); return; }
        var shown = 0;
        foreach (var cat in all.Select(s => s.Category).Distinct().OrderBy(c => c == "Mine" ? 1 : 0))
        {
            var rows = all.Where(s => s.Category == cat && Matches(s)).ToList();
            if (rows.Count == 0) continue;
            ImGui.TextDisabled(cat);
            foreach (var s in rows)
            {
                shown++;
                var label = "   " + s.Name + (s.Rig.Length > 0 && !string.Equals(s.Rig, s.Name, StringComparison.OrdinalIgnoreCase) ? "  (" + s.Rig + ")" : "") + "###sc|" + s.Id;
                if (ImGui.Selectable(label, selected != null && s.Id == selected.Id)) plugin.SelectScene(s.Id, null);
            }
        }
        if (shown == 0) ImGui.TextDisabled("No Scene matches that.");
        ImGui.EndChild();
    }

    // ---- The selected Scene ----

    private void DrawDetails(Plugin plugin, SceneDesk desk, Scene s)
    {
        ImGui.TextUnformatted(s.Name);
        if (!string.IsNullOrWhiteSpace(s.Description)) ImGui.TextWrapped(s.Description);
        if (!string.IsNullOrWhiteSpace(s.BestIn)) ImGui.TextWrapped("Best in: " + s.BestIn);
        ImGui.TextColored(Dim, $"{s.Lights.Count} light(s){(s.Backdrop != null ? ", with backdrop" : "")}"
            + (s.Tags.Count > 0 ? "   tags: " + string.Join(", ", s.Tags) : "")
            + (s.BlocksPresent().Any() ? "   also carries: " + string.Join(", ", s.BlocksPresent()) : ""));

        var palette = plugin.SelectedPalette;
        if (!s.Palettes.ContainsKey(palette)) palette = s.EffectiveDefaultPalette;
        if (s.Palettes.Count > 1)
        {
            ImGui.SetNextItemWidth(220f);
            if (ImGui.BeginCombo("Palette", palette))
            {
                foreach (var name in s.Palettes.Keys)
                    if (ImGui.Selectable(name, name == palette)) plugin.SelectScene(s.Id, name);
                ImGui.EndCombo();
            }
        }

        DrawPoseBlock(desk, s);

        if (ImGui.Button("Apply")) RequestApply(plugin, desk, s, palette);
        ImGui.SameLine();
        if (ImGui.Button("Remove all")) plugin.RemoveAll();
        ImGui.SameLine();
        ImGui.BeginDisabled(!desk.Undo.CanUndo);
        if (ImGui.Button($"Undo ({Math.Max(0, desk.Undo.Count - 1)})")) desk.DoUndo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Reload Scenes")) plugin.ReloadScenes();

        if (s.Category == "Mine") DrawMineActions(desk, s);
    }

    private void RequestApply(Plugin plugin, SceneDesk desk, Scene s, string palette)
    {
        if (desk.PendingSnapshot != null)
        {
            pendingApply = s;
            pendingApplyPalette = palette;
            ImGui.OpenPopup("Unsaved snapshot##apply");
            return;
        }
        plugin.Apply(s, palette);
    }

    private void DrawPoseBlock(SceneDesk desk, Scene s)
    {
        if (s.Pose is not { HasPoseOrOutfit: true } p) return;
        ImGui.Spacing();
        ImGui.TextUnformatted("Pose and outfit");
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(p.PoseName)) parts.Add($"Brio pose '{p.PoseName}'{(string.IsNullOrWhiteSpace(p.PosePack) ? "" : " from " + p.PosePack)}" + (desk.Poses.Available ? "" : " (Brio not running)"));
        if (!string.IsNullOrWhiteSpace(p.GlamourerDesign)) parts.Add($"Glamourer design '{p.GlamourerDesign}'" + (desk.Glamourer.Available ? "" : " (Glamourer not running)"));
        if (p.PenumbraMods.Count > 0) parts.Add($"{p.PenumbraMods.Count} Penumbra mod setting(s), saved in your collection" + (desk.Penumbra.Available ? "" : " (Penumbra not running)"));
        foreach (var part in parts) ImGui.BulletText(part);
        var on = desk.WantsPoseOutfit(s);
        if (ImGui.Checkbox("Apply pose and outfit##poseoutfit", ref on)) desk.SetWantsPoseOutfit(s, on);
        if (!on) ImGui.TextColored(Dim, "Left alone until you tick this. Applying a Scene never changes your outfit or mods on its own.");
        ImGui.Spacing();
    }

    private void DrawMineActions(SceneDesk desk, Scene s)
    {
        if (renameId != s.Id) { renameId = s.Id; renameTo = s.Name; }
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("##renameto", ref renameTo, 64);
        ImGui.SameLine();
        if (ImGui.Button("Rename")) desk.RenameMine(s, renameTo);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) { deleteId = s.Id; ImGui.OpenPopup("Delete Scene##del"); }
    }

    // ---- Snapshot ----

    private void DrawSnapshot(Plugin plugin, SceneDesk desk)
    {
        ImGui.TextUnformatted("Snapshot");
        ImGui.SameLine();
        ImGui.TextColored(Dim, "Reads what is placed now, tweaks included, as a new Scene.");
        if (ImGui.Button("Snapshot now")) desk.TakeSnapshot();
        if (desk.PendingSnapshot is { } snap)
        {
            ImGui.SameLine();
            ImGui.TextColored(Warn, $"Unsaved: {snap.Name}");
            ImGui.SetNextItemWidth(220f);
            ImGui.InputText("Name##savename", ref saveName, 64);
            ImGui.SameLine();
            if (ImGui.Button("Save to Mine"))
            {
                if (desk.SaveSnapshotAs(saveName) != null) saveName = "";
            }
            ImGui.SameLine();
            if (ImGui.Button("Discard")) desk.DiscardSnapshot();
        }
    }

    // ---- Share codes ----

    private void DrawShare(Plugin plugin, SceneDesk desk, Scene? selected)
    {
        ImGui.TextUnformatted("Share codes");
        ImGui.BeginDisabled(selected == null);
        if (ImGui.Button("Copy share code") && selected != null)
        {
            var code = desk.MakeCode(selected, plugin.SelectedPalette);
            if (code != null) { ImGui.SetClipboardText(code); copiedFor = selected.Id; }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextColored(Dim, "A code holds the lights, palettes and notes of one Scene. Never a key or a file path.");

        ImGui.SetNextItemWidth(-130f);
        ImGui.InputTextWithHint("##pastecode", "Paste a share code (ss1:...)", ref pasted, 9000);
        ImGui.SameLine();
        if (ImGui.Button("Read code")) { import = desk.ReadCode(pasted); importName = import.Scene?.Name ?? ""; }

        if (import == null) return;
        if (import.Scene == null)
        {
            ImGui.TextColored(Warn, import.Error ?? "That code could not be read.");
            return;
        }
        ImGui.TextUnformatted("This code holds:");
        foreach (var line in ShareCode.Describe(import.Scene, SceneStore.AppliedBlocks)) ImGui.BulletText(line);
        foreach (var note in import.Notes) ImGui.TextColored(Dim, note);
        ImGui.SetNextItemWidth(220f);
        ImGui.InputText("Save as##importname", ref importName, 64);
        ImGui.SameLine();
        if (ImGui.Button("Save to Mine##import"))
        {
            if (desk.SaveImport(import.Scene, importName) != null) { import = null; pasted = ""; }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel##import")) import = null;
    }

    // ---- Messages and questions ----

    private void DrawMessages(Plugin plugin, SceneDesk desk)
    {
        if (!string.IsNullOrEmpty(plugin.Message)) ImGui.TextWrapped(plugin.Message);
        if (!string.IsNullOrEmpty(desk.Status)) ImGui.TextWrapped(desk.Status);
        foreach (var line in desk.BlockLines) ImGui.TextColored(Dim, line);
    }

    private void DrawConfirmApply(Plugin plugin)
    {
        if (!ImGui.BeginPopupModal("Unsaved snapshot##apply", ImGuiWindowFlags.AlwaysAutoResize)) return;
        ImGui.TextUnformatted("You have a snapshot that is not saved. Applying a Scene throws it away.");
        if (ImGui.Button("Apply anyway") && pendingApply != null)
        {
            plugin.Desk.DiscardSnapshot();
            plugin.Apply(pendingApply, pendingApplyPalette);
            pendingApply = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Keep the snapshot")) { pendingApply = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }

    private void DrawConfirmDelete(SceneDesk desk, IReadOnlyList<Scene> all)
    {
        if (!ImGui.BeginPopupModal("Delete Scene##del", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var s = all.FirstOrDefault(x => x.Id == deleteId);
        ImGui.TextUnformatted(s == null ? "That Scene is gone." : $"Delete '{s.Name}' from Mine? This cannot be undone.");
        if (s != null && ImGui.Button("Delete")) { desk.DeleteMine(s); deleteId = null; ImGui.CloseCurrentPopup(); }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) { deleteId = null; ImGui.CloseCurrentPopup(); }
        ImGui.EndPopup();
    }
}

using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using Soundstage.Poses;

namespace Soundstage.Tabs;

/// <summary>
/// The "Poses" shelf: a scanned list of .pose / .cmp files that loads through Brio's IPC. Draw and Tick run on the
/// framework thread, which is where Brio's IPC wants to be called from. The scan runs on a worker thread.
/// </summary>
public sealed class PoseShelf : IDisposable
{
    private const int HoverRestMs = 350;
    private const int DrawStaleMs = 400;   // the window stopped drawing (closed, collapsed): put the old pose back
    private const float ThumbWidth = 180f;

    private readonly Configuration config;
    private readonly Action save;
    private readonly BrioBridge brio;
    private readonly IClientState clientState;
    private readonly ITargetManager targets;
    private readonly IObjectTable objects;
    private readonly ITextureProvider textures;
    private readonly IPluginLog log;

    private volatile List<PoseEntry> entries = new();
    private volatile Dictionary<string, PoseEntry> byPath = new(StringComparer.OrdinalIgnoreCase);
    private int scanning;
    private string scanSummary = "Not scanned yet.";
    private readonly CancellationTokenSource cancel = new();

    private string search = "";
    private string newFolder = "";
    private string status = "";
    private string? clickedPath;
    private bool wasGPosing;

    // Hover preview: one snapshot of the actor's pose, taken before the first preview load.
    private string? hoverPath;
    private DateTime hoverSince;
    private Vector2 hoverMouse;
    private string? previewPath;
    private string? snapshotJson;
    private IGameObject? snapshotActor;
    private DateTime lastDrawUtc = DateTime.MinValue;

    private sealed class Thumb { public volatile bool Done; public IDalamudTextureWrap? Wrap; }
    private readonly Dictionary<string, Thumb> thumbs = new(StringComparer.OrdinalIgnoreCase);

    // Tag store (Poses/PoseTags.cs): attached on the first draw, when the tab can ask for the config folder.
    private volatile PoseTags? tags;
    private volatile Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);
    private string tagFilter = "";
    // Part 14 (thumbnails): the cache is made when the config folder is known; ExtraTick lets the thumbnail job run on the framework tick.
    public ThumbnailCache? Cache { get; private set; }
    public Action? ExtraTick { get; set; }
    public IReadOnlyList<PoseEntry> Entries => entries;
    private string newTag = "";
    private DateTime tagsDirtySince = DateTime.MinValue;

    // Read-outs for the test checklist (Checklist/AreaUi/PosesStepsUi.cs).
    public string ScanSummary => scanSummary;
    public string Status => status;
    public int EntryCount => entries.Count;
    public int PackCount => entries.Select(e => e.Pack).Distinct().Count();
    public bool InGpose => clientState.IsGPosing;

    public PoseShelf(Configuration config, Action save, BrioBridge brio, IClientState clientState, ITargetManager targets,
        IObjectTable objects, ITextureProvider textures, IPluginLog log)
    {
        this.config = config;
        this.save = save;
        this.brio = brio;
        this.clientState = clientState;
        this.targets = targets;
        this.objects = objects;
        this.textures = textures;
        this.log = log;

        if (!config.PoseFoldersInitialised)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            config.PoseFolders.Add(Path.Combine(docs, "FFXIV Mods"));
            config.PoseFoldersInitialised = true;
            save();
        }
        Rescan();
    }

    public void Dispose()
    {
        cancel.Cancel();
        try { if (snapshotJson != null) EndPreview(restore: true, "plugin unloading"); } catch { /* shutting down */ }
        foreach (var t in thumbs.Values) t.Wrap?.Dispose();
        thumbs.Clear();
    }

    // ---- Scan ----

    private List<string> FolderList()
    {
        var list = new List<string>(config.PoseFolders);
        if (config.IncludeBrioLibraryFolders)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            foreach (var sub in new[] { Path.Combine("Brio", "Poses"), Path.Combine("Anamnesis", "Poses") })
            {
                var p = Path.Combine(docs, sub);
                if (Directory.Exists(p)) list.Add(p);
            }
        }
        return list;
    }

    public void Rescan()
    {
        if (Interlocked.Exchange(ref scanning, 1) == 1) return;
        var folders = FolderList();
        scanSummary = "Scanning...";
        var token = cancel.Token;
        Task.Run(() =>
        {
            try
            {
                var list = PoseLibrary.Scan(folders, s => log.Information("{P} {S}", BrioBridge.LogPrefix, s), out var capped, token);
                var map = new Dictionary<string, PoseEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in list) map[e.Path] = e;
                byPath = map;
                entries = list;
                HashEntries(list);
                var cmp = list.Count(e => e.IsCmp);
                scanSummary = $"{list.Count} pose file(s) ({list.Count - cmp} .pose, {cmp} .cmp) in {folders.Count} folder(s)" + (capped ? $", capped at {PoseLibrary.MaxFiles}" : "") + ".";
                log.Information("{P} scan done: {S}", BrioBridge.LogPrefix, scanSummary);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                scanSummary = "Scan failed: " + ex.Message;
                log.Information("{P} scan -> EXCEPTION {Ex}", BrioBridge.LogPrefix, ex);
            }
            finally { Interlocked.Exchange(ref scanning, 0); }
        }, token);
    }

    // ---- Tags ----

    public PoseTags? Tags => tags;

    /// <summary>Loads <c>pose-tags.json</c> from the plugin's config folder (once) and hashes the scanned files.</summary>
    public void UseConfigDirectory(string dir)
    {
        if (tags != null || string.IsNullOrEmpty(dir)) return;
        var store = PoseTags.Load(Path.Combine(dir, PoseTags.FileName));
        Cache = new ThumbnailCache(Path.Combine(dir, ThumbnailCache.FolderName));
        tags = store;
        log.Information("{P} tag store: {Path} ({N} tagged pose(s)){Note}", BrioBridge.LogPrefix, store.StorePath, store.TaggedCount,
            store.LoadNote.Length > 0 ? " " + store.LoadNote : "");
        var list = entries;
        if (list.Count > 0) Task.Run(() => HashEntries(list));
    }

    /// <summary>Hashes every listed file (cached by size and write time) so tags follow the content, not the name.</summary>
    private void HashEntries(List<PoseEntry> list)
    {
        var store = tags;
        if (store == null) return;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in list)
        {
            var h = store.HashFor(e.Path);
            if (h != null) map[e.Path] = h;
        }
        hashes = map;
        log.Information("{P} tag store: hashed {N} of {T} pose file(s).", BrioBridge.LogPrefix, map.Count, list.Count);
        if (Cache is { } cache)
        {
            var hit = cache.CountHits(map.Values);
            log.Information("{P} thumbnail cache: {Hit} hit, {Miss} miss of {T} pose file(s).", BrioBridge.LogPrefix, hit, list.Count - hit, list.Count);
        }
    }

    private string? HashOf(PoseEntry e) => hashes.TryGetValue(e.Path, out var h) ? h : null;

    private IReadOnlyList<string> TagsOf(PoseEntry e) => tags?.TagsFor(HashOf(e)) ?? Array.Empty<string>();

    private void TagsChanged()
    {
        try { tags?.Save(); }
        catch (Exception ex) { status = "Could not save the tags: " + ex.Message; log.Information("{P} tag store save -> EXCEPTION {Ex}", BrioBridge.LogPrefix, ex.Message); }
    }

    // ---- Target and loading ----

    /// <summary>The actor to pose: the GPose target, else the local player's GPose clone, else the local player.</summary>
    private IGameObject? ResolveTarget(out string why)
    {
        why = "";
        if (!brio.Available) { why = "Brio is not available."; return null; }
        if (!clientState.IsGPosing) { why = "Not in GPose."; return null; }
        var t = targets.GPoseTarget;
        if (t != null) return t;
        var lp = objects.LocalPlayer;
        if (lp == null) { why = "No player to pose."; return null; }
        var name = lp.Name.TextValue;
        foreach (var o in objects)
            if (o.ObjectIndex >= 201 && o.Name.TextValue == name) return o;   // GPose actors sit at object index 201 and up
        return lp;
    }

    private static string ActorName(IGameObject o)
    {
        try { return o.Name.TextValue; } catch { return "actor"; }
    }

    private string Parts(PoseEntry e)
    {
        if (e.IsCmp) return "body";
        return (config.PoseBody, config.PoseExpression) switch { (true, true) => "body and expression", (true, false) => "body only", (false, true) => "expression only", _ => "nothing" };
    }

    /// <summary>Loads one pose on <paramref name="actor"/> with the ticked parts. Sets <see cref="status"/> on failure only.</summary>
    private bool Load(PoseEntry e, IGameObject actor)
    {
        var body = config.PoseBody;
        var expression = config.PoseExpression && !e.IsCmp;
        if (e.IsCmp)
        {
            if (!body) { status = "A .cmp pose has no expression part. Tick Body to load it."; return false; }
            return Finish(brio.LoadFromFile(actor, e.Path));
        }
        if (!body && !expression) { status = "Tick Body or Expression first."; return false; }
        if (body && expression) return Finish(brio.LoadFromFile(actor, e.Path));

        string text;
        try { text = File.ReadAllText(e.Path); }
        catch (Exception ex) { status = "Could not read the file: " + ex.Message; log.Information("{P} read '{Path}' -> EXCEPTION {Ex}", BrioBridge.LogPrefix, e.Path, ex.Message); return false; }

        var json = PoseFilter.Reduce(text, body, expression, out var err, out var kept, out var total);
        if (json == null) { status = err; log.Information("{P} reduce '{Path}' body={B} expression={X} -> {Err}", BrioBridge.LogPrefix, e.Path, body, expression, err); return false; }
        return Finish(brio.LoadFromJson(actor, json, $"'{e.Name}' body={body} expression={expression}, {kept}/{total} bones"));

        bool Finish(bool ok)
        {
            if (!ok) status = "Brio did not load it: " + brio.LastError;
            return ok;
        }
    }

    /// <summary>Part 7: a scanned pose by pack and name (case-insensitive), or null. Used when a Scene's pose block names one.</summary>
    public PoseEntry? FindPose(string? pack, string name) =>
        entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(pack) || string.Equals(e.Pack, pack, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Part 7: loads a pose on the GPose actor with the shelf's ticked parts, without touching the shelf's status line.</summary>
    public bool LoadForScene(PoseEntry e, out string message)
    {
        var actor = ResolveTarget(out var why);
        if (actor == null) { message = why.Length > 0 ? why : "No actor to pose."; return false; }
        var before = status;
        var ok = Load(e, actor);
        message = ok ? "" : status;
        status = before;
        return ok;
    }

    private void Click(PoseEntry e)
    {
        var why = "";
        var actor = snapshotActor ?? ResolveTarget(out why);
        if (actor == null)
        {
            status = why.Length > 0 ? why : "No actor to pose.";
            return;
        }
        DropPreview();
        clickedPath = e.Path;
        if (Load(e, actor))
        {
            status = $"Loaded '{e.Name}' ({Parts(e)}) on {ActorName(actor)}.";
            config.RecentPoses.RemoveAll(p => p.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
            config.RecentPoses.Insert(0, e.Path);
            if (config.RecentPoses.Count > Configuration.MaxRecentPoses) config.RecentPoses.RemoveRange(Configuration.MaxRecentPoses, config.RecentPoses.Count - Configuration.MaxRecentPoses);
            save();
        }
    }

    // ---- Hover preview ----

    private void BeginPreview(PoseEntry e)
    {
        previewPath = e.Path;   // set first, so a failure below is not retried every frame
        IGameObject? actor = snapshotActor;
        if (snapshotJson == null)
        {
            actor = ResolveTarget(out var why);
            if (actor == null) return;   // not in GPose: nothing to preview on, and the status line already says so
            var json = brio.GetPoseJson(actor);
            if (json == null) { status = "Hover preview skipped: Brio could not read the current pose."; return; }
            snapshotJson = json;
            snapshotActor = actor;
        }
        if (actor != null && Load(e, actor)) status = $"Previewing '{e.Name}' ({Parts(e)}). Click to keep it; leave the list to put the old pose back.";
    }

    /// <summary>Ends a preview. With <paramref name="restore"/> the snapshot is loaded back onto the actor first.</summary>
    private void EndPreview(bool restore, string why)
    {
        var json = snapshotJson;
        var actor = snapshotActor;
        DropPreview();
        if (!restore || json == null || actor == null) return;
        var ok = brio.LoadFromJson(actor, json, "restore snapshot (" + why + ")");
        status = ok ? "Put the previous pose back." : "Could not put the previous pose back: " + brio.LastError;
    }

    private void DropPreview()
    {
        snapshotJson = null;
        snapshotActor = null;
        previewPath = null;
        hoverPath = null;
    }

    /// <summary>Framework tick: safety nets for a preview the draw code can no longer end.</summary>
    public void Tick()
    {
        try { ExtraTick?.Invoke(); }
        catch (Exception ex) { log.Information("{P} extra tick -> EXCEPTION {Ex}", BrioBridge.LogPrefix, ex.Message); }
        var gpose = clientState.IsGPosing;
        if (gpose && !wasGPosing && !brio.Available) brio.Refresh();
        wasGPosing = gpose;

        if (snapshotJson == null) return;
        if (!gpose) { log.Information("{P} left GPose during a preview; snapshot dropped.", BrioBridge.LogPrefix); DropPreview(); return; }
        if ((DateTime.UtcNow - lastDrawUtc).TotalMilliseconds > DrawStaleMs) EndPreview(restore: true, "window closed or collapsed");
    }

    /// <summary>Called when the Poses section is not drawn this frame (header collapsed).</summary>
    public void SectionHidden()
    {
        if (snapshotJson != null) EndPreview(restore: true, "section collapsed");
    }

    private void UpdateHover(PoseEntry? hovered, bool listHovered)
    {
        if (!config.PoseHoverPreview)
        {
            if (snapshotJson != null) EndPreview(restore: true, "hover preview switched off");
            return;
        }
        var now = DateTime.UtcNow;
        if (hovered != null)
        {
            var mouse = ImGui.GetMousePos();
            if (hoverPath != hovered.Path || Vector2.Distance(mouse, hoverMouse) > 3f)
            {
                if (hoverPath != hovered.Path) hoverPath = hovered.Path;
                hoverSince = now;
                hoverMouse = mouse;
                return;
            }
            if ((now - hoverSince).TotalMilliseconds >= HoverRestMs && previewPath != hovered.Path) BeginPreview(hovered);
        }
        else if (!listHovered)
        {
            hoverPath = null;
            if (snapshotJson != null) EndPreview(restore: true, "mouse left the list");
        }
        else hoverPath = null;   // over a gap or a star: keep whatever is showing
    }

    // ---- Thumbnails ----

    private Thumb? ThumbFor(PoseEntry e)
    {
        if (e.IsCmp) return null;
        if (thumbs.TryGetValue(e.Path, out var t)) return t;
        t = new Thumb();
        thumbs[e.Path] = t;
        var path = e.Path;
        Task.Run(async () =>
        {
            try
            {
                var bytes = PoseLibrary.ExtractPreviewImage(path);
                if (bytes == null && Cache != null && hashes.TryGetValue(path, out var cachedHash)) bytes = Cache.Read(cachedHash);   // part 14: the rendered thumbnail
                if (bytes != null)
                {
                    t.Wrap = await textures.CreateFromImageAsync(bytes, "Soundstage pose preview");
                    log.Information("{P} preview image for '{Path}': {N} bytes", BrioBridge.LogPrefix, path, bytes.Length);
                }
            }
            catch (Exception ex) { log.Information("{P} preview image for '{Path}' -> EXCEPTION {Ex}", BrioBridge.LogPrefix, path, ex.Message); }
            finally { t.Done = true; }
        });
        return t;
    }

    // ---- Thumbnail run (part 14): the job in Poses/Thumbnails.cs asks for these ----

    private string? thumbSnapshotJson;
    private IGameObject? thumbActor;

    /// <summary>Remembers the actor's pose before the thumbnail job starts loading others. Null on success, otherwise one plain sentence.</summary>
    public string? BeginThumbRun()
    {
        if (snapshotJson != null) EndPreview(restore: true, "thumbnails started");
        var actor = ResolveTarget(out var why);
        if (actor == null) return why.Length > 0 ? why : "No actor to pose.";
        var json = brio.GetPoseJson(actor);
        if (json == null) return "Brio could not read the current pose, so it could not be put back afterwards.";
        thumbActor = actor;
        thumbSnapshotJson = json;
        return null;
    }

    /// <summary>Loads the whole pose file (body and expression, whatever the shelf ticks say) on the actor remembered by <see cref="BeginThumbRun"/>.</summary>
    public bool LoadForThumb(PoseEntry e) => thumbActor != null && brio.LoadFromFile(thumbActor, e.Path);

    /// <summary>Puts the remembered pose back and forgets the actor.</summary>
    public string? EndThumbRun()
    {
        var json = thumbSnapshotJson;
        var actor = thumbActor;
        thumbSnapshotJson = null;
        thumbActor = null;
        if (json == null || actor == null) return null;
        return brio.LoadFromJson(actor, json, "restore after thumbnails") ? null : "Could not put your pose back: " + brio.LastError;
    }

    /// <summary>New thumbnails exist: forget the "no picture" answers so the tooltips look again.</summary>
    public void ThumbnailsChanged()
    {
        foreach (var key in thumbs.Where(kv => kv.Value.Done && kv.Value.Wrap == null).Select(kv => kv.Key).ToList()) thumbs.Remove(key);
    }

    // ---- Drawing ----

    private static string Safe(string s) => s.Replace("##", "# #");

    private bool Matches(PoseEntry e)
        => (search.Length == 0
            || e.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || e.Pack.Contains(search, StringComparison.OrdinalIgnoreCase))
           && (tagFilter.Length == 0 || (tags?.Matches(HashOf(e), tagFilter) ?? false));

    public void Draw()
    {
        lastDrawUtc = DateTime.UtcNow;
        DrawStatus();

        var body = config.PoseBody;
        if (ImGui.Checkbox("Body", ref body)) { config.PoseBody = body; save(); }
        ImGui.SameLine();
        var expr = config.PoseExpression;
        if (ImGui.Checkbox("Expression", ref expr)) { config.PoseExpression = expr; save(); }
        ImGui.SameLine();
        var hover = config.PoseHoverPreview;
        if (ImGui.Checkbox("Hover preview", ref hover)) { config.PoseHoverPreview = hover; save(); }

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Search##poses", ref search, 64);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160);
        ImGui.InputTextWithHint("##tagfilter", "tag filter, e.g. sit", ref tagFilter, 64);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Only poses with every tag word you type. Right-click a pose to tag it.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(90);
        if (ImGui.BeginCombo("##tagpick", "tags"))
        {
            var all = tags?.AllTags() ?? Array.Empty<(string Tag, int Count)>();
            if (all.Count == 0) ImGui.TextDisabled("No tags yet.");
            foreach (var (tag, count) in all)
                if (ImGui.Selectable($"{tag} ({count})##pick|{tag}")) tagFilter = (tagFilter.Trim() + " " + tag).Trim();
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Rescan")) Rescan();
        ImGui.SameLine();
        ImGui.TextDisabled(scanSummary);

        DrawFolders();

        PoseEntry? hovered = null;
        bool listHovered;
        if (ImGui.BeginChild("##poselist", new Vector2(0, 280), true))
        {
            var all = entries;
            var map = byPath;
            var shown = all.Where(Matches).ToList();

            var favs = config.FavouritePoses.Select(p => map.TryGetValue(p, out var e) ? e : null).Where(e => e != null && Matches(e)).Cast<PoseEntry>().ToList();
            var recent = config.RecentPoses.Select(p => map.TryGetValue(p, out var e) ? e : null).Where(e => e != null && Matches(e)).Cast<PoseEntry>().ToList();

            DrawSection("Favourites", favs, ImGuiTreeNodeFlags.DefaultOpen, ref hovered);
            DrawSection("Recently used", recent, ImGuiTreeNodeFlags.DefaultOpen, ref hovered);
            ImGui.Separator();
            foreach (var pack in shown.GroupBy(e => e.Pack))
                DrawSection(pack.Key, pack.ToList(), ImGuiTreeNodeFlags.None, ref hovered);

            if (shown.Count == 0) ImGui.TextDisabled(all.Count == 0 ? "No pose files found. Check the folders above." : "Nothing matches the search.");
            listHovered = ImGui.IsWindowHovered();
        }
        else listHovered = false;
        ImGui.EndChild();

        UpdateHover(hovered, listHovered);
    }

    private void DrawStatus()
    {
        if (!brio.Available)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), $"Brio not available ({brio.LastError ?? "unknown"}).");
            ImGui.TextWrapped("Install and enable Brio 0.8.x with IPC switched on (Brio settings, IPC), then press Recheck Brio.");
            if (ImGui.Button("Recheck Brio")) brio.Refresh();
        }
        else if (!clientState.IsGPosing)
        {
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.4f, 1f), $"Brio ready (API {brio.Version}). Enter GPose to load poses.");
        }
        else
        {
            var actor = targets.GPoseTarget;
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.6f, 1f), $"In GPose. Poses go on: {(actor != null ? ActorName(actor) : "your character (no GPose target selected)")}.");
        }
        if (!string.IsNullOrEmpty(status)) ImGui.TextWrapped(status);
    }

    private void DrawFolders()
    {
        if (!ImGui.TreeNode($"Pose folders ({config.PoseFolders.Count})###poseFolders")) return;
        string? remove = null;
        foreach (var f in config.PoseFolders)
        {
            ImGui.PushID("folder|" + f);
            if (ImGui.SmallButton("x")) remove = f;
            ImGui.SameLine();
            ImGui.TextUnformatted(Safe(f));
            ImGui.PopID();
        }
        if (remove != null) { config.PoseFolders.Remove(remove); save(); Rescan(); }

        ImGui.SetNextItemWidth(320);
        ImGui.InputText("##newFolder", ref newFolder, 260);
        ImGui.SameLine();
        if (ImGui.Button("Add folder") && newFolder.Trim().Length > 0)
        {
            var f = newFolder.Trim().Trim('"');
            if (!config.PoseFolders.Contains(f, StringComparer.OrdinalIgnoreCase)) { config.PoseFolders.Add(f); save(); Rescan(); }
            newFolder = "";
        }
        var brioLib = config.IncludeBrioLibraryFolders;
        if (ImGui.Checkbox("Also scan Brio's library folders (Documents\\Brio\\Poses, Documents\\Anamnesis\\Poses) if they exist", ref brioLib))
        {
            config.IncludeBrioLibraryFolders = brioLib;
            save();
            Rescan();
        }
        ImGui.TreePop();
    }

    private void DrawSection(string title, List<PoseEntry> rows, ImGuiTreeNodeFlags flags, ref PoseEntry? hovered)
    {
        if (rows.Count == 0 && (title == "Favourites" || title == "Recently used")) return;
        if (search.Length > 0) ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        if (!ImGui.CollapsingHeader($"{Safe(title)} ({rows.Count})###sec|{title}", flags)) return;
        foreach (var e in rows) DrawRow(title, e, ref hovered);
    }

    private void DrawTagPopup(PoseEntry e, IReadOnlyList<string> own)
    {
        if (!ImGui.BeginPopupContextItem("##tags")) return;
        ImGui.TextUnformatted("Tags for " + Safe(e.Name));
        var hash = HashOf(e);
        if (tags == null || hash == null)
        {
            ImGui.TextDisabled(tags == null ? "The tag store is not ready yet." : "This file has not been read yet.");
        }
        else
        {
            foreach (var t in own)
                if (ImGui.SmallButton("x  " + t + "##rm|" + t)) { tags.Remove(hash, t); TagsChanged(); }
            ImGui.SetNextItemWidth(160);
            var enter = ImGui.InputTextWithHint("##newtag", "add a tag", ref newTag, 48, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button("Add") || enter) && newTag.Length > 0)
            {
                if (tags.Add(hash, newTag)) { TagsChanged(); newTag = ""; }
                else status = $"Not added: a tag is plain words, lower case, up to {PoseTags.MaxTagLength} characters, and not already on the pose.";
            }
        }
        ImGui.EndPopup();
    }

    private void DrawRow(string section, PoseEntry e, ref PoseEntry? hovered)
    {
        ImGui.PushID(section + "|" + e.Path);
        var fav = config.FavouritePoses.Contains(e.Path, StringComparer.OrdinalIgnoreCase);
        if (ImGui.SmallButton(fav ? "★" : "☆"))
        {
            if (fav) config.FavouritePoses.RemoveAll(p => p.Equals(e.Path, StringComparison.OrdinalIgnoreCase));
            else config.FavouritePoses.Add(e.Path);
            save();
        }
        ImGui.SameLine();

        if (e.IsCmp) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        var clicked = ImGui.Selectable(Safe(e.Name) + (e.IsCmp ? "  (.cmp)" : ""), clickedPath == e.Path);
        if (e.IsCmp) ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            hovered = e;
            var thumb = ThumbFor(e);
            var resting = hoverPath == e.Path && (DateTime.UtcNow - hoverSince).TotalMilliseconds >= HoverRestMs;
            if (thumb?.Wrap is { } wrap && resting)
            {
                var h = ThumbWidth * wrap.Height / Math.Max(1, wrap.Width);
                ImGui.BeginTooltip();
                ImGui.Image(wrap.Handle, new Vector2(ThumbWidth, h));
                ImGui.EndTooltip();
            }
            else if (e.IsCmp && resting)
            {
                ImGui.SetTooltip(".cmp: body only, there is no expression to load.");
            }
        }
        if (clicked) Click(e);

        var own = TagsOf(e);
        if (own.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("[" + string.Join(", ", own) + "]");
        }
        DrawTagPopup(e, own);
        ImGui.PopID();
    }
}

/// <summary>The Brio pose shelf tab. The shelf logic stays in <see cref="PoseShelf"/> above; the tab only hands it the window.</summary>
public sealed class PosesTab : ITab
{
    public string Name => "Poses";
    public int Order => 30;

    public void Draw(Studio studio)
    {
        studio.Poses.UseConfigDirectory(studio.Plugin.ConfigDirectory);
        studio.Poses.Draw();
        PoseTagHost.For(studio).DrawPanel(studio);   // part 14: Make thumbnails, Tag with AI
    }

    // The shelf puts a hover-previewed pose back when it is not on screen.
    public void OnHidden(Studio studio) => studio.Poses.SectionHidden();
}

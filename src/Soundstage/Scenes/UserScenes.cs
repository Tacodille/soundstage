// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>
/// Part 7.4: the user's own Scenes in <c>user-scenes.json</c>. Save, rename and delete. Every Scene in the file is "Mine";
/// the id is made from the name and kept unique among all loaded Scenes. Writes go through <see cref="SceneStore.SaveUser"/>
/// (temp file, then move), so a crash mid-save leaves the old file.
/// </summary>
public sealed class UserScenes
{
    private readonly string path;
    private readonly Action<string> log;

    public UserScenes(string path, Action<string> log)
    {
        this.path = path;
        this.log = log;
    }

    public string Path => path;

    public List<Scene> Load()
    {
        var list = SceneStore.LoadFile(path, log);
        foreach (var s in list) s.Category = "Mine";
        return list;
    }

    /// <summary>An id for <paramref name="name"/> that no id in <paramref name="taken"/> uses: "my-look", then "my-look-2" and so on.</summary>
    public static string UniqueId(string name, IEnumerable<string> taken)
    {
        var set = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var baseId = SceneStore.Slug(name);
        var id = baseId;
        for (var n = 2; set.Contains(id); n++) id = baseId + "-" + n;
        return id;
    }

    /// <summary>
    /// Saves a copy of <paramref name="scene"/> as a Mine Scene called <paramref name="name"/>. A Scene that is already Mine and
    /// keeps its name is replaced in place; anything else gets a fresh id. Returns the saved Scene, or null with the reason.
    /// </summary>
    public Scene? Save(Scene scene, string name, IEnumerable<string> allLoadedIds, out string? error)
    {
        error = null;
        name = name.Trim();
        if (name.Length == 0) { error = "Type a name first."; return null; }
        try
        {
            var mine = Load();
            var copy = scene.Clone();
            copy.Name = name;
            copy.Category = "Mine";
            copy.LegacyIds = new();
            copy.BakeDefaultPalette();
            var replace = mine.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (replace != null) { copy.Id = replace.Id; mine.Remove(replace); }
            else copy.Id = UniqueId(name, allLoadedIds.Concat(mine.Select(m => m.Id)));
            var why = SceneStore.Validate(copy);
            if (why != null) { error = "That Scene cannot be saved: " + why; return null; }
            mine.Add(copy);
            SceneStore.SaveUser(path, mine);
            log($"scenes: saved '{copy.Id}' to {path}.");
            return copy;
        }
        catch (Exception ex) { error = "Save failed: " + ex.Message; return null; }
    }

    /// <summary>A loaded Scene whose id clashed with a shipped one is shown as "user-" plus its file id, so both match.</summary>
    private static bool IsId(Scene m, string id) =>
        string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals("user-" + m.Id, id, StringComparison.OrdinalIgnoreCase);

    public bool Rename(string id, string newName, out string? error)
    {
        error = null;
        newName = newName.Trim();
        if (newName.Length == 0) { error = "Type a name first."; return false; }
        try
        {
            var mine = Load();
            var s = mine.FirstOrDefault(m => IsId(m, id));
            if (s == null) { error = "That Scene is not in Mine."; return false; }
            s.Name = newName;
            SceneStore.SaveUser(path, mine);
            return true;
        }
        catch (Exception ex) { error = "Rename failed: " + ex.Message; return false; }
    }

    public bool Delete(string id, out string? error)
    {
        error = null;
        try
        {
            var mine = Load();
            if (mine.RemoveAll(m => IsId(m, id)) == 0) { error = "That Scene is not in Mine."; return false; }
            SceneStore.SaveUser(path, mine);
            log($"scenes: deleted '{id}' from {path}.");
            return true;
        }
        catch (Exception ex) { error = "Delete failed: " + ex.Message; return false; }
    }
}

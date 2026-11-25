// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage;

/// <summary>One thing a Scene's pose block can reach for: a plugin that may be missing. Each port never throws.</summary>
public interface IScenePort
{
    /// <summary>The plugin's name as the user knows it ("Brio", "Glamourer", "Penumbra").</summary>
    string PluginName { get; }

    /// <summary>True when the plugin is running and its IPC answered.</summary>
    bool Available { get; }
}

public interface IPosePort : IScenePort
{
    /// <summary>Loads the named pose on the posed actor. False with a plain reason (not in GPose, pose not found).</summary>
    bool TryLoad(string? pack, string name, out string message);
}

public interface IOutfitPort : IScenePort
{
    bool TryApplyDesign(string designName, out string message);
}

public interface IModsPort : IScenePort
{
    bool TrySet(IReadOnlyList<PenumbraModChoice> mods, out string message);
}

/// <summary>
/// Part 7.5: applies a Scene's pose block through the three ports. Nothing happens unless the user ticked "Apply pose and
/// outfit" for that Scene. A missing plugin, pose, design or mod skips that part alone and says so in one line; the lines are
/// logged and shown to the user.
/// </summary>
public static class PoseBlockApplier
{
    public static List<string> Apply(ScenePose? block, bool optedIn, IPosePort? pose, IOutfitPort? outfit, IModsPort? mods)
    {
        var lines = new List<string>();
        if (block == null || !block.HasPoseOrOutfit) return lines;
        if (!optedIn)
        {
            lines.Add("This Scene has a pose and outfit block, which was left alone because \"Apply pose and outfit\" is not ticked for it.");
            return lines;
        }

        if (!string.IsNullOrWhiteSpace(block.PoseName))
        {
            if (pose is not { Available: true }) lines.Add($"Pose '{block.PoseName}' skipped: {(pose?.PluginName ?? "Brio")} is not running.");
            else lines.Add(pose.TryLoad(block.PosePack, block.PoseName!, out var m) ? $"Pose '{block.PoseName}' loaded." : $"Pose '{block.PoseName}' skipped: {m}");
        }
        else if (!string.IsNullOrWhiteSpace(block.PoseFile))
            lines.Add("Pose skipped: this Scene names a pose file by path, which only works on the machine that saved it.");

        if (!string.IsNullOrWhiteSpace(block.GlamourerDesign))
        {
            if (outfit is not { Available: true }) lines.Add($"Glamourer design '{block.GlamourerDesign}' skipped: {(outfit?.PluginName ?? "Glamourer")} is not running.");
            else lines.Add(outfit.TryApplyDesign(block.GlamourerDesign!, out var m) ? $"Glamourer design '{block.GlamourerDesign}' applied." : $"Glamourer design '{block.GlamourerDesign}' skipped: {m}");
        }

        if (block.PenumbraMods.Count > 0)
        {
            if (mods is not { Available: true }) lines.Add($"Penumbra mod settings ({block.PenumbraMods.Count}) skipped: {(mods?.PluginName ?? "Penumbra")} is not running.");
            else lines.Add(mods.TrySet(block.PenumbraMods, out var m) ? $"Penumbra mod settings applied: {m}" : $"Penumbra mod settings skipped: {m}");
        }
        return lines;
    }
}

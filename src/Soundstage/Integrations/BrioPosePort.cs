using Soundstage.Tabs;

namespace Soundstage;

/// <summary>Part 7.5: the pose shelf and Brio behind <see cref="IPosePort"/>, so a Scene's pose block loads a pose by pack and name.</summary>
public sealed class BrioPosePort : IPosePort
{
    private readonly PoseShelf shelf;
    private readonly BrioBridge brio;

    public BrioPosePort(PoseShelf shelf, BrioBridge brio)
    {
        this.shelf = shelf;
        this.brio = brio;
    }

    public string PluginName => "Brio";
    public bool Available => brio.Available;

    public bool TryLoad(string? pack, string name, out string message)
    {
        var entry = shelf.FindPose(pack, name);
        if (entry == null)
        {
            message = $"the pose shelf has no pose '{name}'" + (string.IsNullOrWhiteSpace(pack) ? "." : $" in the pack '{pack}'.");
            return false;
        }
        return shelf.LoadForScene(entry, out message);
    }
}

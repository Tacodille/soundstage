// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Text;

namespace Soundstage.Ai.PhotoToScene;

/// <summary>The instruction sent with the picture. The model chooses from the catalogue given here, so its answer maps onto a Scene that exists.</summary>
public static class PhotoPrompt
{
    /// <summary>The Scenes the model may name. The user's own Scenes ("Mine") are left out, so their names never leave the PC.</summary>
    public static IReadOnlyList<Scene> Catalogue(IEnumerable<Scene> all) =>
        all.Where(s => !string.Equals(s.Category, "Mine", StringComparison.OrdinalIgnoreCase)).ToList();

    public static string Build(IReadOnlyList<Scene> catalogue)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are reading the lighting in a portrait or character photo, so a lighting preset can be matched to it. Describe the light, not the person.");
        sb.AppendLine("Answer with one JSON object and nothing else. Use only these words.");
        sb.AppendLine();
        sb.AppendLine("- keyDirection: where the main light comes from, as seen in the photo, one of: " + string.Join(", ", PhotoWords.Directions) + ". Left means the left side of the picture.");
        sb.AppendLine("- keyElevation: one of: " + string.Join(", ", PhotoWords.Elevations) + " (above the eye line, level with it, or below it).");
        sb.AppendLine("- keySoftness: one of: " + string.Join(", ", PhotoWords.Softness) + " (hard means crisp shadow edges).");
        sb.AppendLine("- colours: the colour of each light itself, not of skin or clothes, as #RRGGBB: key, fill, rim. Use null for a light you cannot see.");
        sb.AppendLine("- backdropColour: the main colour of the wall or background as #RRGGBB, or null.");
        sb.AppendLine("- hasRim: true when a separate light edges the hair or shoulders from behind.");
        sb.AppendLine("- mood: up to three of: " + string.Join(", ", PhotoWords.Moods) + ".");
        sb.AppendLine("- headAngle: yaw one of: " + string.Join(", ", PhotoWords.YawBuckets) + "; pitch one of: " + string.Join(", ", PhotoWords.PitchBuckets) + ".");
        sb.AppendLine("- expression: one or two plain words, for example smiling or calm.");
        sb.AppendLine("- rig: the rig that fits best, copied exactly from the list below.");
        sb.AppendLine("- closestScene: the id of the closest Scene, copied exactly from the list below.");
        sb.AppendLine();
        sb.AppendLine("Scenes (id | rig | tags):");
        foreach (var s in catalogue)
            sb.AppendLine($"{s.Id} | {s.Rig} | {string.Join(", ", s.Tags)}");
        sb.AppendLine();
        sb.AppendLine("Do not name files, web addresses or anything outside this list. Do not describe the person.");
        return sb.ToString();
    }
}

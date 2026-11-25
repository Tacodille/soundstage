// No Dalamud types in this file or in Checklist/Areas/*.cs: the offline self-check compiles them as shared files, so an
// area file that reaches for a Dalamud type fails to build there. Buttons and live checks go in Checklist/AreaUi/.
using System.Reflection;

namespace Soundstage.Checklist;

/// <summary>One thing the tester does in game and then marks Pass or Fail. <see cref="Id"/> is unique inside its area.</summary>
public sealed record ChecklistStep(string Id, string Title, string Instructions);

/// <summary>A status the plugin worked out itself, so no Pass / Fail buttons are shown for the step.</summary>
public sealed record ChecklistResult(string Status, string Note);

/// <summary>
/// One area of the test checklist, usually one per plan part. <see cref="ChecklistRules.Discover"/> finds every
/// implementation in the assembly by reflection, so a part adds its steps with one new file in Checklist/Areas/ and
/// edits nothing shared. The class needs a parameterless constructor.
/// </summary>
public interface IChecklistArea
{
    /// <summary>Short and stable: it is the result key prefix and the area word in the log line (for example "core").</summary>
    string Id { get; }

    /// <summary>The heading shown in the window.</summary>
    string Name { get; }

    /// <summary>Top to bottom. Core is 10; use the part number times 10 for a part's area.</summary>
    int Order { get; }

    IReadOnlyList<ChecklistStep> Steps { get; }
}

public static class ChecklistRules
{
    public const string LogPrefix = "[Soundstage/TEST]";

    /// <summary>The config key for a result: "area/step", or "area/step:sub" for one item inside a step.</summary>
    public static string Key(string areaId, string stepId, string? sub = null) =>
        sub == null ? $"{areaId}/{stepId}" : $"{areaId}/{stepId}:{sub}";

    /// <summary>The line written to dalamud.log for one Pass / Fail click.</summary>
    public static string LogLine(string areaId, string stepId, string title, string status, string note) =>
        $"{LogPrefix} {areaId} {stepId} {title}: {status} note={note}";

    /// <summary>Every area type in the assembly, constructed, in Order. See <see cref="Discover(IEnumerable{Type}, Action{string})"/>.</summary>
    public static IReadOnlyList<IChecklistArea> Discover(Assembly assembly, Action<string> log) => Discover(assembly.GetTypes(), log);

    /// <summary>
    /// Every area type in the list, constructed, in Order. An area that cannot be built, repeats an area id, or repeats
    /// a step id inside itself is skipped with one log line, so one bad file cannot take the checklist down.
    /// </summary>
    public static IReadOnlyList<IChecklistArea> Discover(IEnumerable<Type> types, Action<string> log)
    {
        var found = new List<IChecklistArea>();
        foreach (var type in types)
        {
            if (type.IsAbstract || type.IsInterface || !typeof(IChecklistArea).IsAssignableFrom(type)) continue;
            if (type.GetConstructor(Type.EmptyTypes) == null) { log($"{type.Name} has no parameterless constructor, skipped."); continue; }
            IChecklistArea area;
            try { area = (IChecklistArea)Activator.CreateInstance(type)!; }
            catch (Exception ex) { log($"{type.Name} could not be created, skipped: {ex.Message}"); continue; }

            if (found.Any(a => a.Id == area.Id)) { log($"{type.Name} repeats the area id '{area.Id}', skipped."); continue; }
            if (area.Steps.Select(s => s.Id).Distinct().Count() != area.Steps.Count) { log($"{type.Name} repeats a step id, skipped."); continue; }
            found.Add(area);
        }
        return found.OrderBy(a => a.Order).ThenBy(a => a.Id, StringComparer.Ordinal).ToList();
    }
}

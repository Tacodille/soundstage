using Soundstage.Checklist;

namespace Soundstage.SelfCheck;

/// <summary>
/// The test checklist's discovery and its log format. Checklist/Areas/*.cs are compiled into this project as shared
/// files, so these checks see every area file in the plugin, and an area file that uses a Dalamud type breaks this build.
/// </summary>
public sealed class ChecklistChecks : CheckArea
{
    public override string Name => "Test checklist";
    public override int Order => 40;

    public override void Run()
    {
        var log = new List<string>();
        // Only the plugin's own area files: the fake areas at the bottom of this file live in this assembly too.
        var areas = ChecklistRules.Discover(typeof(ChecklistRules).Assembly.GetTypes().Where(t => t.Namespace == "Soundstage.Checklist"), log.Add);
        var core = areas.FirstOrDefault(a => a.Id == "core");

        Check("discovery finds the core area", core != null, string.Join(", ", areas.Select(a => a.Id)));
        Check("core area has the ten ported steps, ids 1 to 10 in order",
            core != null && core.Steps.Select(s => s.Id).SequenceEqual(Enumerable.Range(1, 10).Select(i => i.ToString())) && core.Order == 10);
        Check("every discovered area and step has a name and instructions, and discovery logged no skips",
            areas.All(a => a.Name.Length > 0 && a.Steps.Count > 0 && a.Steps.All(s => s.Title.Length > 0 && s.Instructions.Length > 0)) && log.Count == 0,
            string.Join(" | ", log));
        Check("areas come back sorted by Order", areas.Select(a => a.Order).SequenceEqual(areas.Select(a => a.Order).OrderBy(o => o)));

        Check("result key is area/step, or area/step:sub", ChecklistRules.Key("core", "4") == "core/4" && ChecklistRules.Key("core", "3", "neon-green-rim") == "core/3:neon-green-rim");
        Check("log line is '[Soundstage/TEST] <area> <step id> <title>: PASS|FAIL note=...'",
            ChecklistRules.LogLine("core", "4", "Live update in GPose", "PASS", "smooth") == "[Soundstage/TEST] core 4 Live update in GPose: PASS note=smooth");

        // Negative control: bad areas are skipped, each with one log line, and a good one beside them still loads.
        var badLog = new List<string>();
        var found = ChecklistRules.Discover(new[] { typeof(CoreSteps), typeof(DuplicateIdArea), typeof(RepeatedStepArea), typeof(NoDefaultCtorArea), typeof(GoodExtraArea) }, badLog.Add);
        Check("a repeated area id, a repeated step id and a missing constructor are skipped with one line each; a good extra area loads",
            found.Select(a => a.Id).SequenceEqual(new[] { "core", "extra" }) && badLog.Count == 3, $"{string.Join(",", found.Select(a => a.Id))}; {badLog.Count} lines");
    }

    private abstract class FakeArea : IChecklistArea
    {
        public abstract string Id { get; }
        public string Name => "Fake";
        public int Order => 999;
        public virtual IReadOnlyList<ChecklistStep> Steps { get; } = new[] { new ChecklistStep("1", "One", "Do one.") };
    }

    private sealed class DuplicateIdArea : FakeArea { public override string Id => "core"; }

    private sealed class RepeatedStepArea : FakeArea
    {
        public override string Id => "repeated";
        public override IReadOnlyList<ChecklistStep> Steps { get; } = new[] { new ChecklistStep("1", "One", "a"), new ChecklistStep("1", "Again", "b") };
    }

    private sealed class NoDefaultCtorArea : IChecklistArea
    {
        public NoDefaultCtorArea(int unused) { }
        public string Id => "noctor";
        public string Name => "No ctor";
        public int Order => 999;
        public IReadOnlyList<ChecklistStep> Steps => new[] { new ChecklistStep("1", "One", "a") };
    }

    private sealed class GoodExtraArea : FakeArea { public override string Id => "extra"; }
}

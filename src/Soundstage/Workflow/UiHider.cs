// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Workflow;

/// <summary>One thing that has to be out of the shot: how to hide it, how to put it back, and (optionally) how to tell it has gone.</summary>
public sealed record HideTarget(string Name, Action Hide, Action Restore, Func<bool>? Settled = null);

/// <summary>
/// Hides a set of targets for a capture and puts every one of them back. <see cref="Restore"/> runs each target's restore even if an
/// earlier one threw, and never throws itself, so a failed capture cannot leave the window or the markers hidden.
/// </summary>
public sealed class UiHider
{
    private readonly Func<IReadOnlyList<HideTarget>> targets;
    private readonly Action<string> log;
    private readonly List<HideTarget> hidden = new();

    public UiHider(Func<IReadOnlyList<HideTarget>> targets, Action<string> log)
    {
        this.targets = targets;
        this.log = log;
    }

    public bool IsHidden => hidden.Count > 0;

    /// <summary>The names of what could not be hidden or restored in the last call, for the status line.</summary>
    public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();

    /// <summary>Hides everything. A target whose hide throws is still put on the restore list, because it may have half-hidden.</summary>
    public void Hide()
    {
        if (IsHidden) return;
        var problems = new List<string>();
        foreach (var t in targets())
        {
            hidden.Add(t);
            try { t.Hide(); }
            catch (Exception ex) { problems.Add(t.Name); log($"[Soundstage/WORK] could not hide {t.Name}: {ex.Message}"); }
        }
        Problems = problems;
    }

    /// <summary>True when every target that can say so reports it is gone.</summary>
    public bool AllSettled()
    {
        foreach (var t in hidden)
        {
            try { if (t.Settled != null && !t.Settled()) return false; }
            catch (Exception) { /* a probe that throws counts as settled: waiting on it would never end */ }
        }
        return true;
    }

    /// <summary>Puts everything back, last hidden first. Safe to call any number of times.</summary>
    public void Restore()
    {
        if (!IsHidden) return;
        var problems = new List<string>();
        for (var i = hidden.Count - 1; i >= 0; i--)
        {
            var t = hidden[i];
            try { t.Restore(); }
            catch (Exception ex) { problems.Add(t.Name); log($"[Soundstage/WORK] could not restore {t.Name}: {ex.Message}"); }
        }
        hidden.Clear();
        Problems = problems;
    }
}

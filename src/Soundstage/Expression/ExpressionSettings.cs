// No Dalamud types in this file.
namespace Soundstage.Expression;

/// <summary>One rated match: the score the model gave and the player's own 1 to 5 rating of how recognisable the face is.</summary>
public sealed class ExpressionTrial
{
    public string Note { get; set; } = "";
    public float Score { get; set; }
    public int Rating { get; set; }
    public string Date { get; set; } = "";
}

/// <summary>Settings of the Expression tab, kept in <see cref="Configuration.Sections"/> under "expression".</summary>
public sealed class ExpressionSettings
{
    public const string Key = "expression";

    public string LastImage { get; set; } = "";

    /// <summary>A Camera tab framing id. The face is read from a screenshot, so it has to fill enough of it.</summary>
    public string Framing { get; set; } = "close-up";

    /// <summary>How far a bone moves for a position channel, in the pose file's own units. Tune against the first calibration.</summary>
    public float PositionStep { get; set; } = 0.004f;

    /// <summary>How far a bone turns for a rotation channel, in degrees.</summary>
    public float RotationStepDeg { get; set; } = 6f;

    /// <summary>The solver may go this many steps either way and no further. The safe range of a bone.</summary>
    public float SafeSteps { get; set; } = 2f;

    /// <summary>How many face bones get measured. Three axes and two directions each, so 13 bones is 78 captures plus the baseline.</summary>
    public int MaxBones { get; set; } = 13;

    /// <summary>Frames to wait after a pose load before the picture is taken.</summary>
    public int SettleFrames { get; set; } = 4;

    public int MaxIterations { get; set; } = 6;

    /// <summary>The loop stops when an iteration improves the error by less than this.</summary>
    public float Epsilon { get; set; } = 0.002f;

    public List<ExpressionTrial> Trials { get; set; } = new();

    /// <summary>When the median rating is under 3 the tab shows only its label until this is ticked.</summary>
    public bool ShowExperimental { get; set; }
}

/// <summary>The rating rule of part 17.8: five rated photos, and a median under "recognisable" (3) makes the feature experimental.</summary>
public static class ExpressionTrials
{
    public const int Needed = 5;
    public const int Recognisable = 3;

    public static float? Median(IEnumerable<int> ratings)
    {
        var r = ratings.Where(x => x is >= 1 and <= 5).OrderBy(x => x).ToList();
        if (r.Count == 0) return null;
        var mid = r.Count / 2;
        return r.Count % 2 == 1 ? r[mid] : (r[mid - 1] + r[mid]) / 2f;
    }

    public static float? Median(ExpressionSettings s) => Median(s.Trials.Select(t => t.Rating));

    /// <summary>True once there are five ratings and their median is under 3.</summary>
    public static bool IsExperimental(ExpressionSettings s)
        => s.Trials.Count(t => t.Rating is >= 1 and <= 5) >= Needed && Median(s) is { } m && m < Recognisable;

    public static string Describe(ExpressionSettings s)
    {
        var n = s.Trials.Count(t => t.Rating is >= 1 and <= 5);
        if (n == 0) return $"No rated matches yet. Rate {Needed} to find out whether this is good enough to rely on.";
        var m = Median(s)!.Value;
        if (n < Needed) return $"{n} of {Needed} matches rated so far (median {m:0.#} of 5).";
        return IsExperimental(s)
            ? $"Experimental: median rating {m:0.#} of 5 over {n} matches, under 'recognisable' (3)."
            : $"Median rating {m:0.#} of 5 over {n} matches.";
    }
}

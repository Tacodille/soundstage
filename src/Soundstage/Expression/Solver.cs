// No Dalamud types in this file.
namespace Soundstage.Expression;

/// <summary>
/// The table as dense arrays: one row per blendshape name, and for each channel the response per step at +Step (<see cref="Channel.Plus"/>) and
/// at -Step (<see cref="Channel.Minus"/>), both written as "change in the blendshapes for one step of that move".
/// </summary>
public sealed class ResponseModel
{
    public sealed record Channel(ChannelRecord Record, float[] Plus, float[] Minus, float MaxSteps);

    public ResponseModel(string[] names, float[] baseline, IReadOnlyList<Channel> channels)
    {
        Names = names;
        Baseline = baseline;
        Channels = channels;
    }

    public string[] Names { get; }
    public float[] Baseline { get; }
    public IReadOnlyList<Channel> Channels { get; }

    /// <summary>Builds the model from a table. Only measured channels count; a channel with no step or range is left out.</summary>
    public static ResponseModel From(ExpressionTable table)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var k in table.Baseline.Keys) names.Add(k);
        foreach (var c in table.Channels.Where(c => c.Measured))
        {
            foreach (var k in c.Plus!.Keys) names.Add(k);
            foreach (var k in c.Minus!.Keys) names.Add(k);
        }
        names.Remove(Weights.Neutral);
        var arr = names.ToArray();
        var baseline = arr.Select(n => table.Baseline.TryGetValue(n, out var v) ? v : 0f).ToArray();
        var list = new List<Channel>();
        foreach (var c in table.Channels.Where(c => c.Measured && c.Step > 0 && c.Max > 0))
            list.Add(new Channel(c, Column(arr, c.Plus!), Column(arr, c.Minus!), c.Max / c.Step));
        return new ResponseModel(arr, baseline, list);
    }

    private static float[] Column(string[] names, Dictionary<string, float> source)
    {
        var col = new float[names.Length];
        for (var i = 0; i < names.Length; i++) col[i] = source.TryGetValue(names[i], out var v) ? v : 0f;
        return col;
    }

    /// <summary>What the model says the blendshapes will be at the given offsets (in steps, one per channel).</summary>
    public float[] Predict(float[] steps)
    {
        var o = (float[])Baseline.Clone();
        for (var j = 0; j < Channels.Count && j < steps.Length; j++)
        {
            var v = steps[j];
            var col = v >= 0 ? Channels[j].Plus : Channels[j].Minus;
            var m = MathF.Abs(v);
            for (var i = 0; i < o.Length; i++) o[i] += m * col[i];
        }
        return o;
    }
}

/// <summary>The weights of the error: the blendshapes that show most on a face count more.</summary>
public static class Weights
{
    public const string Neutral = "_neutral";
    public const float VisibleWeight = 3f;

    /// <summary>The 12 blendshapes that read most clearly in a portrait: brows, eyelids, jaw and the smile.</summary>
    public static readonly string[] Visible =
    {
        "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
        "eyeBlinkLeft", "eyeBlinkRight", "eyeWideLeft", "eyeWideRight",
        "jawOpen", "mouthSmileLeft", "mouthSmileRight",
    };

    public static float[] For(string[] names)
    {
        var vis = new HashSet<string>(Visible, StringComparer.Ordinal);
        return names.Select(n => vis.Contains(n) ? VisibleWeight : 1f).ToArray();
    }

    /// <summary>Weighted mean absolute error between two blendshape vectors on the same names.</summary>
    public static float WeightedError(float[] a, float[] b, float[] w)
    {
        double sum = 0, wsum = 0;
        for (var i = 0; i < a.Length; i++) { sum += w[i] * Math.Abs(a[i] - b[i]); wsum += w[i]; }
        return wsum > 0 ? (float)(sum / wsum) : 0f;
    }

    /// <summary>100 x (1 - error / 0.5), clamped to 0 to 100. The model's own number and not a judgement of likeness.</summary>
    public static float Score(float weightedError) => Math.Clamp(100f * (1f - weightedError / 0.5f), 0f, 100f);
}

/// <summary>
/// Part 17.3. Finds how far to move each channel so the blendshapes land on a target. Pure maths: every channel becomes two non-negative
/// variables (a move one way, a move the other) and a bounded non-negative least-squares fit is run by cyclic coordinate descent, which for
/// this convex problem converges to the one answer. The bounds are each bone's safe range, so the answer can never distort the face.
/// </summary>
public static class Solver
{
    /// <summary>
    /// <paramref name="wanted"/> is the change wanted in each blendshape (target minus where the model says the face starts), on the model's names.
    /// Returns one value per channel, in steps (positive or negative), each within that channel's <see cref="ResponseModel.Channel.MaxSteps"/>.
    /// </summary>
    public static float[] Solve(ResponseModel model, float[] wanted, float[] weights, int sweeps = 400, float ridge = 1e-4f)
    {
        var J = model.Channels.Count;
        var N = model.Names.Length;
        var z = new double[2 * J];
        var result = new float[J];
        if (J == 0 || N == 0) return result;

        // residual r = wanted - A z
        var r = new double[N];
        for (var i = 0; i < N; i++) r[i] = wanted[i];

        // The ridge is relative to the size of the columns, so it breaks ties without pulling a small response towards zero.
        var raw = new double[2 * J];
        for (var k = 0; k < 2 * J; k++)
        {
            var col = Col(model, k);
            double s = 0;
            for (var i = 0; i < N; i++) s += weights[i] * col[i] * col[i];
            raw[k] = s;
        }
        var reg = Math.Max(raw.Average(), 1e-12) * ridge;
        var colNorm = raw.Select(x => x + reg).ToArray();

        for (var sweep = 0; sweep < sweeps; sweep++)
        {
            double biggest = 0;
            for (var k = 0; k < 2 * J; k++)
            {
                var col = Col(model, k);
                double g = 0;
                for (var i = 0; i < N; i++) g += weights[i] * col[i] * r[i];
                var old = z[k];
                var upper = model.Channels[k / 2].MaxSteps;
                var nz = Math.Clamp(old + (g - reg * old) / colNorm[k], 0, upper);
                var d = nz - old;
                if (d != 0)
                {
                    for (var i = 0; i < N; i++) r[i] -= col[i] * d;
                    z[k] = nz;
                    biggest = Math.Max(biggest, Math.Abs(d));
                }
            }
            if (biggest < 1e-7) break;
        }

        for (var j = 0; j < J; j++) result[j] = (float)(z[2 * j] - z[2 * j + 1]);
        return result;
    }

    private static float[] Col(ResponseModel m, int k) => (k & 1) == 0 ? m.Channels[k / 2].Plus : m.Channels[k / 2].Minus;

    /// <summary>Clamps a set of offsets (in steps) to each channel's safe range.</summary>
    public static float[] Clamp(ResponseModel model, float[] steps)
    {
        var o = new float[steps.Length];
        for (var j = 0; j < steps.Length; j++)
            o[j] = j < model.Channels.Count ? Math.Clamp(steps[j], -model.Channels[j].MaxSteps, model.Channels[j].MaxSteps) : steps[j];
        return o;
    }
}

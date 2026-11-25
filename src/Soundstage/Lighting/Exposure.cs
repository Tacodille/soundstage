// No Dalamud types in this file: the offline self-check compiles Lighting/*.cs as shared files.
using System.Numerics;

namespace Soundstage;

public enum ExposureLevel { Ok, Hot, Clip }

/// <summary>
/// The exposure warning. The estimate is the brightness rule (measured 18/11/2026 for where a light's range ends) read at
/// the subject's distance: max(colour) x intensity / (falloffFactor x d^n), n = 1, 2, 3 for Linear, Quadratic, Cubic.
/// At the range it equals <see cref="SceneStore.GameCutoff"/>. Where "too hot" starts is a calibration, in <see cref="LightingSettings"/>.
/// </summary>
public static class Exposure
{
    public static int Exponent(SceneLight l) => l.Falloff.Trim().ToLowerInvariant() switch { "linear" => 1, "cubic" => 3, _ => 2 };

    /// <summary>The value of the light at <paramref name="distance"/> metres. The exponent is a parameter only so the self-check can run a wrong-exponent control.</summary>
    public static double Estimate(SceneLight l, double distance, int? exponentOverride = null)
    {
        var n = exponentOverride ?? Exponent(l);
        var max = Math.Max(l.Color[0], Math.Max(l.Color[1], l.Color[2]));
        var d = Math.Max(distance, 0.05);   // inside 5 cm the formula is meaningless; this keeps it finite
        return max * l.Intensity / (l.FalloffFactor * Math.Pow(d, n));
    }

    /// <summary>Distance from the light (where it will be placed) to the face, both stage-local.</summary>
    public static float SubjectDistance(SceneLight l, Vector3 face)
    {
        var flagged = AimSolver.IsFaceAimed(l);
        var pos = flagged ? AimSolver.Position(l, face) : SceneConverter.LightPosition(l);
        var target = flagged ? face : new Vector3(0f, l.AimHeight ?? AimSolver.DefaultHeadHeight, 0f);
        return Vector3.Distance(pos, target);
    }

    public static ExposureLevel Level(double value, LightingSettings s) =>
        value >= s.RedThreshold ? ExposureLevel.Clip : value >= s.AmberThreshold ? ExposureLevel.Hot : ExposureLevel.Ok;

    public static string Describe(ExposureLevel level) => level switch
    {
        ExposureLevel.Clip => "will clip",
        ExposureLevel.Hot => "likely too hot",
        _ => "ok",
    };
}

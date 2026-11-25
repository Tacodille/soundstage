// No Dalamud types in this file: the offline self-check compiles Face/*.cs as source files.
namespace Soundstage.Face;

/// <summary>
/// What the face analyser says about one picture. Angles are in degrees with this convention (the same one the helper uses,
/// proved by tools/face/symmetry_test.py):
/// yaw &gt; 0 means the face turns toward the RIGHT of the picture, pitch &gt; 0 means the chin lifts (looking above the lens),
/// roll &gt; 0 means the top of the head tips toward the right of the picture.
/// </summary>
public sealed class FaceResult
{
    /// <summary>False when there is no answer (no helper, no face, a crash). <see cref="Reason"/> then says why, in a sentence for the tab.</summary>
    public bool Available { get; init; }

    public string Reason { get; init; } = "";

    public float YawDeg { get; init; }
    public float PitchDeg { get; init; }
    public float RollDeg { get; init; }

    /// <summary>0 to 1. A framing figure (how much of the picture the face fills): the model itself gives no score. See docs/face-model-decision.md.</summary>
    public float Confidence { get; init; }

    /// <summary>The model's blendshape scores by name, 0 to 1. The model returns 52 (ARKit's 51 plus "_neutral").</summary>
    public IReadOnlyDictionary<string, float> Blendshapes { get; init; } = new Dictionary<string, float>();

    /// <summary>478 landmarks as x, y, z triples; x and y are 0 to 1 across the picture (y down), z is the model's relative depth.</summary>
    public float[] Landmarks { get; init; } = Array.Empty<float>();

    public int LandmarkCount => Landmarks.Length / 3;

    public static FaceResult Unavailable(string reason) => new() { Available = false, Reason = reason };

    /// <summary>The x, y of landmark <paramref name="i"/> (0 to 1 across the picture).</summary>
    public (float X, float Y) Point(int i) => (Landmarks[i * 3], Landmarks[i * 3 + 1]);

    public float Blend(string name) => Blendshapes.TryGetValue(name, out var v) ? v : 0f;
}

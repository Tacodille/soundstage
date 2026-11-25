// No Dalamud types in this file.
using System.Text.Json;

namespace Soundstage.Face;

/// <summary>A person's outline cut to its own bounding box and sampled onto a square grid, with the box's width over height. See face_core.silhouette.</summary>
public sealed class SilhouetteGrid
{
    public int Size { get; init; }
    public bool[] Cells { get; init; } = Array.Empty<bool>();
    public float Aspect { get; init; }

    /// <summary>Share of the whole picture the person covers (framing, not shape).</summary>
    public float Cover { get; init; }

    public int Filled => Cells.Count(c => c);

    public static SilhouetteGrid? FromBase64(string bits, int size, float aspect, float cover)
    {
        try
        {
            var bytes = Convert.FromBase64String(bits);
            if (size <= 0 || bytes.Length * 8 < size * size) return null;
            var cells = new bool[size * size];
            for (var i = 0; i < cells.Length; i++) cells[i] = (bytes[i >> 3] & (0x80 >> (i & 7))) != 0;   // numpy.packbits is most significant bit first
            return new SilhouetteGrid { Size = size, Cells = cells, Aspect = aspect, Cover = cover };
        }
        catch (FormatException) { return null; }
    }

    /// <summary>Grid cells mirrored left to right (a person who is the photo's mirror image).</summary>
    public SilhouetteGrid Flipped()
    {
        var f = new bool[Cells.Length];
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++) f[y * Size + x] = Cells[y * Size + (Size - 1 - x)];
        return new SilhouetteGrid { Size = Size, Cells = f, Aspect = Aspect, Cover = Cover };
    }
}

/// <summary>The body pose model's answer for one picture: 33 landmarks in the picture and 33 in metres, and the person's outline. Never throws on a bad reply.</summary>
public sealed class BodyPoseResult
{
    public const int Count = 33;

    public bool Available { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>x, y (0 to 1 across the picture, y down), z (relative depth), visibility. 33 rows.</summary>
    public float[][] Image { get; init; } = Array.Empty<float[]>();

    /// <summary>x, y, z in metres with the origin at the hip centre, then visibility. MediaPipe's frame: x toward the picture's right, y DOWN, z away from the camera. 33 rows.</summary>
    public float[][] World { get; init; } = Array.Empty<float[]>();

    public SilhouetteGrid? Silhouette { get; init; }

    public static BodyPoseResult Unavailable(string reason) => new() { Available = false, Reason = reason };

    public static readonly string[] Names =
    {
        "nose", "left_eye_inner", "left_eye", "left_eye_outer", "right_eye_inner", "right_eye", "right_eye_outer", "left_ear", "right_ear",
        "mouth_left", "mouth_right", "left_shoulder", "right_shoulder", "left_elbow", "right_elbow", "left_wrist", "right_wrist",
        "left_pinky", "right_pinky", "left_index", "right_index", "left_thumb", "right_thumb", "left_hip", "right_hip", "left_knee",
        "right_knee", "left_ankle", "right_ankle", "left_heel", "right_heel", "left_foot_index", "right_foot_index",
    };

    public static int IndexOf(string name) => Array.IndexOf(Names, name);

    public static BodyPoseResult Parse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Unavailable("The face helper sent something unexpected.");
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var why = root.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                return Unavailable(string.IsNullOrWhiteSpace(why) ? "The helper found no body." : why!);
            }
            var img = Rows(root, "image");
            var world = Rows(root, "world");
            if (img == null || world == null) return Unavailable("The helper's body reply was missing its landmarks.");

            SilhouetteGrid? sil = null;
            if (root.TryGetProperty("silhouette", out var s) && s.ValueKind == JsonValueKind.Object
                && s.TryGetProperty("bits", out var b) && b.ValueKind == JsonValueKind.String
                && s.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Number)
            {
                var aspect = s.TryGetProperty("aspect", out var a) && a.ValueKind == JsonValueKind.Number ? (float)a.GetDouble() : 0f;
                var cover = s.TryGetProperty("cover", out var c) && c.ValueKind == JsonValueKind.Number ? (float)c.GetDouble() : 0f;
                sil = SilhouetteGrid.FromBase64(b.GetString() ?? "", sz.GetInt32(), aspect, cover);
            }
            return new BodyPoseResult { Available = true, Image = img, World = world, Silhouette = sil };
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return Unavailable("The helper's body reply could not be read.");
        }
    }

    private static float[][]? Rows(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() != Count) return null;
        var rows = new float[Count][];
        var i = 0;
        foreach (var p in arr.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() < 4) return null;
            rows[i++] = p.EnumerateArray().Take(4).Select(x => (float)x.GetDouble()).ToArray();
        }
        return rows;
    }
}

/// <summary>Compares two outlines: overlap of the filled cells, times how alike the boxes' proportions are. 0 to 100.</summary>
public static class SilhouetteScore
{
    public static float Iou(SilhouetteGrid a, SilhouetteGrid b)
    {
        if (a.Size != b.Size || a.Cells.Length != b.Cells.Length) return 0f;
        var inter = 0; var uni = 0;
        for (var i = 0; i < a.Cells.Length; i++)
        {
            if (a.Cells[i] && b.Cells[i]) inter++;
            if (a.Cells[i] || b.Cells[i]) uni++;
        }
        return uni == 0 ? 0f : inter / (float)uni;
    }

    public static float AspectAgreement(SilhouetteGrid a, SilhouetteGrid b)
    {
        if (a.Aspect <= 0f || b.Aspect <= 0f) return 0f;
        return MathF.Min(a.Aspect, b.Aspect) / MathF.Max(a.Aspect, b.Aspect);
    }

    /// <summary>100 for an identical outline. Both parts matter: two outlines can overlap well after the grids are stretched to fit while the real shapes are wide against tall.</summary>
    public static float Score(SilhouetteGrid photo, SilhouetteGrid game) => 100f * Iou(photo, game) * AspectAgreement(photo, game);
}

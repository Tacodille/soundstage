// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Numerics;

namespace Soundstage;

/// <summary>
/// The measured size of the dyeable white partition (fun_b0_m0766), read from the game files by tools/measure_mdl.py on
/// 21/11/2026 (docs/set-notes.md). The pivot is the middle of the bottom edge: x and z are centred, y runs 0 to 3.
/// </summary>
public static class PartitionSize
{
    public const float Width = 4.0f;
    public const float Height = 3.0f;
    public const float Thickness = 0.2258f;
}

/// <summary>One partition in the sweep: where its pivot goes, how it is turned and scaled. Rotation is (pitch, yaw, roll) degrees.</summary>
public sealed record SweepPiece(string Id, string Part, Vector3 Position, Vector3 PitchYawRollDeg, Vector3 Scale);

/// <summary>What the sweep should look like. Metres, in the stage frame (+Z forward, origin at the subject's feet).</summary>
public sealed record SweepSpec(float Width = 8f, float Distance = 2.5f, float Cove = 1f, float WallHeight = 3f, float FloorFront = 2.5f);

/// <summary>
/// A seamless sweep from partitions: a wall behind the subject, a floor piece laid flat in front of it, and a 45 degree
/// cove piece joining them. All of it is the same dyed partition, repeated across for width, so it needs no new model.
/// This is pure layout (no game types), so the maths is checked offline. Rotation facts (System.Numerics, right-handed):
/// a pitch of +90 turns a piece's local up (+Y) to +Z (forward) and its local +Z to down, and +135 turns local up to
/// forward-and-down, which is the direction the cove has to run from the wall's foot to the floor.
/// </summary>
public static class SweepBuilder
{
    public const int MaxPieces = 40;
    public const float MinWidth = 4f, MaxWidth = 16f;
    public const float MinDistance = 1f, MaxDistance = 8f;
    public const float MinCove = 0.3f, MaxCove = 2f;
    public const float MaxWallHeight = 6f;

    /// <summary>How much neighbouring pieces overlap, so no hairline shows between them.</summary>
    public const float Overlap = 0.01f;

    /// <summary>Scale of the thin axis. 0.25 of 22.6 cm is 5.6 cm, so a floor piece sits a few centimetres proud of the ground.</summary>
    public const float ThinScale = 0.25f;

    /// <summary>The floor's top surface sits this high above the ground, so it does not flicker against it.</summary>
    public const float FloorLift = 0.012f;

    public static SweepSpec Clamp(SweepSpec s) => new(
        Math.Clamp(s.Width, MinWidth, MaxWidth),
        Math.Clamp(s.Distance, MinDistance, MaxDistance),
        Math.Clamp(s.Cove, MinCove, MaxCove),
        Math.Clamp(s.WallHeight, PartitionSize.Height * 0.5f, MaxWallHeight),
        Math.Clamp(s.FloorFront, 1f, 6f));

    /// <summary>The pieces for a sweep. Never more than <see cref="MaxPieces"/>: if the request needs more, the floor's front edge pulls back.</summary>
    public static IReadOnlyList<SweepPiece> Layout(SweepSpec requested)
    {
        var s = Clamp(requested);
        var pieces = Build(s);
        while (pieces.Count > MaxPieces && s.FloorFront > 1f)
        {
            s = s with { FloorFront = MathF.Max(1f, s.FloorFront - 1f) };
            pieces = Build(s);
        }
        return pieces;
    }

    /// <summary>How many columns of pieces a width needs: whole partitions, each at most 4 m wide.</summary>
    public static int Columns(float width) => Math.Max(1, (int)MathF.Ceiling(width / PartitionSize.Width - 1e-4f));

    /// <summary>Rows needed to cover a length with pieces that are at most 3 m long.</summary>
    public static int Rows(float length) => Math.Max(1, (int)MathF.Ceiling(length / PartitionSize.Height - 1e-4f));

    private static List<SweepPiece> Build(SweepSpec s)
    {
        var list = new List<SweepPiece>();
        var cols = Columns(s.Width);
        var tileW = s.Width / cols;
        var sx = (tileW + Overlap) / PartitionSize.Width;
        float XOf(int i) => -s.Width / 2f + tileW * (i + 0.5f);

        var wallRows = Rows(s.WallHeight);
        var rowH = s.WallHeight / wallRows;
        var coveLength = s.Cove * MathF.Sqrt(2f);
        var floorStart = -s.Distance + s.Cove;
        var floorDepth = Math.Max(0.5f, s.FloorFront - floorStart);
        var floorRows = Rows(floorDepth);
        var rowD = floorDepth / floorRows;
        var floorY = FloorLift - PartitionSize.Thickness * ThinScale / 2f;

        for (var i = 0; i < cols; i++)
        {
            var x = XOf(i);
            for (var j = 0; j < wallRows; j++)
                list.Add(new SweepPiece($"sweep-wall-{i}-{j}", "wall",
                    new Vector3(x, s.Cove + j * rowH, -s.Distance), Vector3.Zero,
                    new Vector3(sx, (rowH + Overlap) / PartitionSize.Height, ThinScale)));

            list.Add(new SweepPiece($"sweep-cove-{i}", "cove",
                new Vector3(x, s.Cove + Overlap, -s.Distance), new Vector3(135f, 0f, 0f),
                new Vector3(sx, (coveLength + 2f * Overlap) / PartitionSize.Height, ThinScale)));

            for (var j = 0; j < floorRows; j++)
                list.Add(new SweepPiece($"sweep-floor-{i}-{j}", "floor",
                    new Vector3(x, floorY, floorStart - Overlap + j * rowD), new Vector3(90f, 0f, 0f),
                    new Vector3(sx, (rowD + 2f * Overlap) / PartitionSize.Height, ThinScale)));
        }
        return list;
    }

    /// <summary>The ground area the sweep's floor covers, as (minX, maxX, minZ, maxZ), for the self-check and the Set tab's read-out.</summary>
    public static (float MinX, float MaxX, float MinZ, float MaxZ) FloorExtent(SweepSpec requested)
    {
        var s = Clamp(requested);
        return (-s.Width / 2f, s.Width / 2f, -s.Distance + s.Cove, s.FloorFront);
    }
}

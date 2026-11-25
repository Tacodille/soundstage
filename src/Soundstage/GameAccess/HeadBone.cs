using System.Numerics;

namespace Soundstage.GameAccess;

/// <summary>
/// The local player's head position in the world, from the model's own skeleton (bone j_kao). Used by the lighting part to
/// aim the key at the face. Never throws: it returns false, and the caller falls back to the player position plus a body
/// height. Must run on the framework thread (an ImGui draw is on it).
/// </summary>
public static class HeadBone
{
    public static bool TryGet(out Vector3 position) => TryGet(out position, out _);

    public static bool TryGet(out Vector3 position, out string reason)
    {
        var r = GameAccessHost.Guard.ReadHead();
        position = r.Ok ? r.Value : default;
        reason = r.Reason;
        return r.Ok;
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using GameCamera = FFXIVClientStructs.FFXIV.Client.Game.Camera;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace Soundstage.GameAccess;

/// <summary>
/// The only place that touches game memory. It uses FFXIVClientStructs types as Dalamud ships them (no signatures, no hooks),
/// which is why it needs no Dalamud service. Every method here may throw or return a failure; <see cref="GuardedAccess"/>
/// is what the rest of the plugin calls. Written from the installed structs and the public description of how
/// "Lights, Camera, Action!" reaches the same camera (read only, nothing copied).
/// </summary>
public sealed unsafe class ClientStructsReader : IGameReader
{
    /// <summary>The GPose FOV slider, kept as a raw offset because the installed Camera struct does not name it.</summary>
    public const int AddedFovOffset = 0x13C;

    public const string HeadBoneName = "j_kao";

    public bool InGpose() => GameMain.IsInGPose();

    public Access<CameraState> ReadCamera()
    {
        var cam = ActiveCamera(out var why);
        if (cam == null) return Access<CameraState>.Fail(why);
        var scene = &cam->CameraBase.SceneCamera;
        return Access<CameraState>.Success(new CameraState(
            scene->Object.Position, scene->LookAtVector,
            cam->DirH, cam->DirV, cam->Distance, cam->FoV, *(float*)((byte*)cam + AddedFovOffset)));
    }

    public Access<bool> WriteCamera(CameraWrite w)
    {
        var cam = ActiveCamera(out var why);
        if (cam == null) return Access<bool>.Fail(why);
        if (w.Yaw is { } yaw) cam->DirH = OrbitMath.WrapPi(yaw);
        if (w.Pitch is { } pitch) cam->DirV = Math.Clamp(pitch, cam->DirVMin, cam->DirVMax);
        if (w.Distance is { } d) cam->Distance = Math.Clamp(d, cam->MinDistance, cam->MaxDistance);
        if (w.AddedFov is { } f) *(float*)((byte*)cam + AddedFovOffset) = OrbitMath.ClampAddedFov(f);
        if (w.Position is { } p) cam->CameraBase.SceneCamera.Object.Position = p;
        return Access<bool>.Success(true);
    }

    public Access<Vector3> ReadPlayerPosition()
    {
        var player = LocalPlayer(out var why);
        return player == null ? Access<Vector3>.Fail(why) : Access<Vector3>.Success(player->Position);
    }

    /// <summary>Route A: player, then draw object, then skeleton and pose, then the head bone's model-space position carried into the world.</summary>
    public Access<Vector3> ReadHeadPosition()
    {
        var player = LocalPlayer(out var why);
        if (player == null) return Access<Vector3>.Fail(why);
        var draw = player->DrawObject;
        if (draw == null) return Access<Vector3>.Fail("The player has no draw object yet.");
        if (draw->Object.GetObjectType() != ObjectType.CharacterBase) return Access<Vector3>.Fail("The player's draw object is not a character model.");
        var model = (CharacterBase*)draw;
        var skeleton = model->Skeleton;
        if (skeleton == null) return Access<Vector3>.Fail("The player's model has no skeleton yet.");

        for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
        {
            var pose = skeleton->PartialSkeletons[i].GetHavokPose(0);
            if (pose == null || pose->Skeleton == null) continue;
            var bones = pose->Skeleton->Bones;
            for (var b = 0; b < bones.Length && b < pose->ModelPose.Length; b++)
            {
                if (!NameIs(bones[b].Name.StringAndFlag, HeadBoneName)) continue;
                var t = pose->ModelPose[b].Translation;
                var sc = model->Scale;
                var local = new Vector3(t.X * sc.X, t.Y * sc.Y, t.Z * sc.Z);
                var rotated = Vector3.Transform(local, model->Rotation);
                var origin = model->Position;
                return Access<Vector3>.Success(new Vector3(origin.X + rotated.X, origin.Y + rotated.Y, origin.Z + rotated.Z));
            }
        }
        return Access<Vector3>.Fail($"No bone named {HeadBoneName} in any of the model's {skeleton->PartialSkeletonCount} partial skeletons.");
    }

    /// <summary>For the probe's type listing: the bone names the first pose that has any bones carries, joined with commas (capped).</summary>
    public string DescribeSkeleton()
    {
        var player = LocalPlayer(out var why);
        if (player == null) return why;
        var draw = player->DrawObject;
        if (draw == null) return "no draw object";
        var model = (CharacterBase*)draw;
        var skeleton = model->Skeleton;
        if (skeleton == null) return "no skeleton";
        var parts = new List<string>();
        for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
        {
            var pose = skeleton->PartialSkeletons[i].GetHavokPose(0);
            parts.Add(pose == null || pose->Skeleton == null ? $"#{i}: no pose" : $"#{i}: {pose->Skeleton->Bones.Length} bones, {pose->ModelPose.Length} model transforms");
        }
        return $"{skeleton->PartialSkeletonCount} partial skeletons ({string.Join("; ", parts)})";
    }

    private static GameCamera* ActiveCamera(out string why)
    {
        var mgr = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager.Instance();
        if (mgr == null) { why = "The game's camera manager is not up yet."; return null; }
        var cam = mgr->GetActiveCamera();
        if (cam == null) { why = "No active camera."; return null; }
        why = "";
        return cam;
    }

    private static BattleChara* LocalPlayer(out string why)
    {
        var control = Control.Instance();
        if (control == null) { why = "The game's control block is not up yet."; return null; }
        var p = control->LocalPlayer;
        if (p == null) { why = "No local player (not logged in, or loading)."; return null; }
        why = "";
        return p;
    }

    private static bool NameIs(byte* stringAndFlag, string name)
    {
        // hkStringPtr keeps a flag in the low bit of the pointer.
        var ptr = (byte*)((nuint)stringAndFlag & ~(nuint)1);
        if (ptr == null) return false;
        return string.Equals(Marshal.PtrToStringAnsi((nint)ptr), name, StringComparison.Ordinal);
    }
}

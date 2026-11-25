// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.IO.Compression;
using System.Numerics;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using Stagehand.Definitions.Objects;

namespace Soundstage;

// ---- Scene to Stagehand definition ----------------------------------------------------------------------------

public static class SceneConverter
{
    public const string BackdropModel = "bgcommon/hou/indoor/general/0766/bgparts/fun_b0_m0766.mdl";
    public const string CanvasModel = "bgcommon/hou/indoor/general/0204/bgparts/fun_b0_m0204.mdl";
    public const string CanvasDiffusePath = "bgcommon/hou/indoor/general/0204/texture/fun_b0_m0204_0b_d.tex";
    public const string CanvasModpackId = "soundstage-canvas";
    private const float Deg = MathF.PI / 180f;

    /// <summary>Stage-local position: (sin az * d, height, cos az * d). +Z is the player's forward, +X their left.</summary>
    public static Vector3 LightPosition(SceneLight l)
    {
        var a = l.AzimuthDeg * Deg;
        return new Vector3(MathF.Sin(a) * l.Distance, l.Height, MathF.Cos(a) * l.Distance);
    }

    /// <summary>
    /// (pitch, yaw, roll) in degrees that turn a light's +Z axis from <paramref name="from"/> towards <paramref name="target"/>.
    /// Same maths as the original key light: yaw faces the target on the ground plane, positive pitch tips it down.
    /// </summary>
    public static Vector3 AimPitchYawRoll(Vector3 from, Vector3 target)
    {
        var to = target - from;
        var yaw = MathF.Atan2(to.X, to.Z) / Deg;
        var horizontal = MathF.Sqrt(to.X * to.X + to.Z * to.Z);
        var pitch = MathF.Atan2(-to.Y, horizontal) / Deg;
        return new Vector3(pitch, yaw, 0f);
    }

    /// <param name="faceLocal">The face in stage-local space (part 8). A light flagged aimAt "face" is placed and aimed relative to it; others ignore it.</param>
    public static LightDefinition MakeLight(SceneLight l, string fallbackName, Vector3? faceLocal = null)
    {
        SceneStore.TryShape(l.Shape, out var shape);
        SceneStore.TryFalloff(l.Falloff, out var falloff);
        var faceAimed = faceLocal != null && AimSolver.IsFaceAimed(l);
        var pos = faceAimed ? AimSolver.Position(l, faceLocal!.Value) : LightPosition(l);
        var anyShadow = l.CharacterShadows || l.ObjectShadows;
        var def = new LightDefinition
        {
            DisplayName = string.IsNullOrWhiteSpace(l.Name) ? fallbackName : l.Name,
            Shape = shape,
            Color = new Vector3(l.Color[0], l.Color[1], l.Color[2]),
            Intensity = l.Intensity,
            Range = l.Range,
            FalloffFunction = falloff,
            FalloffFactor = l.FalloffFactor,
            SpotLightAngleDegrees = l.ConeDeg,
            AngularFalloffDegrees = l.AngularFalloffDeg,
            Position = pos,
            ShadowPlaneNear = 0.25f,
            ShadowPlaneFar = 100f,
            CharacterShadowRange = 50f,
            EnableSpecularHighlights = l.Specular,
            EnableCharacterShadows = l.CharacterShadows,
            EnableObjectShadows = l.ObjectShadows,
            EnableDynamicShadows = anyShadow,
        };
        if (shape is LightShape.Spot or LightShape.Flat)
            def.RotationPitchYawRollDegrees = AimPitchYawRoll(pos, faceAimed ? faceLocal!.Value : new Vector3(0f, l.AimHeight ?? pos.Y, 0f));
        return def;
    }

    public static StageDefinition Build(Scene p, BackdropAdjust? adjust = null, Vector3? faceLocal = null)
    {
        adjust ??= new BackdropAdjust();
        var stage = new StageDefinition { Info = new StageInfo { Name = "Soundstage scene " + p.Name, AuthorName = "Tacodile" } };
        for (var i = 0; i < p.Lights.Count; i++)
            stage.Objects["light" + i] = MakeLight(p.Lights[i], "Light " + (i + 1), faceLocal);

        // Part 9: a Scene whose set is a sweep replaces the old panel; the panel's light stays.
        if (p.Backdrop is { } b)
        {
            if (p.Set?.Kind != "sweep") stage.Objects["backdrop"] = new BgObjectDefinition
            {
                DisplayName = "Backdrop",
                ModelGamePath = BackdropModel,
                DyeColor = new Vector4(b.Color[0], b.Color[1], b.Color[2], 1f),
                Opacity = 1f,
                Scale = new Vector3(b.Scale[0] * adjust.ScaleX, b.Scale[1] * adjust.ScaleY, b.Scale[2]),
                // Azimuth 180: straight behind. Base at the feet (height 0) plus the live offset.
                Position = new Vector3(0f, adjust.HeightOffset, -(b.DistanceBehind + adjust.DistanceOffset)),
                // Rotation 0 is assumed to face +Z, which is towards the player from behind them. The offset fixes it live.
                RotationPitchYawRollDegrees = new Vector3(0f, adjust.YawOffsetDeg, 0f),
            };
            if (b.Light != null)
                stage.Objects["backdropLight"] = MakeLight(b.Light, "Backdrop light", faceLocal);
        }
        SetApply.AddTo(stage, p);   // part 9: sweep, prop kits, picture, atmosphere
        return stage;
    }

    /// <summary>Test-only: the portrait of the Admiral, 1.5 m front-left of the player at head height, facing them, with the diffuse texture replaced.</summary>
    public static StageDefinition BuildCanvasTest(byte[] diffuseTex, float yawOffsetDeg = 0f, float heightOffset = 0f)
    {
        var pos = new Vector3(MathF.Sin(45f * Deg) * 1.5f, 1.6f + heightOffset, MathF.Cos(45f * Deg) * 1.5f);
        // Assumed: model +Z is its front, so facing the player (at the stage origin) means +Z points along -pos.
        var yaw = MathF.Atan2(-pos.X, -pos.Z) / Deg + yawOffsetDeg;

        var stage = new StageDefinition { Info = new StageInfo { Name = "Soundstage canvas test", AuthorName = "Tacodile" } };
        stage.Objects["canvas"] = new BgObjectDefinition
        {
            DisplayName = "Canvas test",
            ModelGamePath = CanvasModel,
            Position = pos,
            RotationPitchYawRollDegrees = new Vector3(0f, yaw, 0f),
            Opacity = 1f,
            DyeColor = Vector4.One,
            ModpackId = CanvasModpackId,
        };
        var pack = new EmbeddedModpackDefinition { DisplayName = "Soundstage canvas test" };
        // Key = the exact game path the game asks for (lower case, forward slashes). Embedded data is the whole .tex file.
        pack.ModdedResources[CanvasDiffusePath] = new EmbeddedModResourceDefinition
        {
            CompressionScheme = ModCompressionScheme.Zlib,
            CompressedDataBytes = EmbeddedModResourceDefinition.CompressDataBytes(diffuseTex, ModCompressionScheme.Zlib),
        };
        stage.EmbeddedModpacks[CanvasModpackId] = pack;
        return stage;
    }
}

// Moved unchanged from the prototype's single Program.cs (sections 1 to 8).
using System.Numerics;
using Soundstage;
using Stagehand.Definitions;
using Stagehand.Definitions.ModResources;
using Stagehand.Definitions.Objects;

namespace Soundstage.SelfCheck;

public sealed class PresetChecks : CheckArea
{
    public override string Name => "Presets, converter and user presets";
    public override int Order => 10;

    public override void Run()
    {
        // ---- 1. Example parses; bad presets are skipped with a reason ----
        var logs = new List<string>();
        var examplePath = Path.Combine(root, "TestData", "presets.example.json");
        var presets = SceneStore.LoadFile(examplePath, logs.Add);
        Check("example file loads 2 good presets", presets.Count == 2, $"loaded {presets.Count}");
        Check("5 bad presets each skipped with a logged reason", logs.Count(l => l.Contains("skipped")) == 5, $"{logs.Count(l => l.Contains("skipped"))} skip lines");
        foreach (var l in logs) Console.WriteLine("      log: " + l);
        var neon = presets.First(p => p.Id == "neon-split-blue-red");
        var azs = presets.First(p => p.Id == "test-three-azimuths");

        // ---- 2. Light positions at az 0 / 90 / 180 (hand-computed) ----
        var s2 = SceneConverter.Build(azs);
        var front = (LightDefinition)s2.Objects["light0"];
        var left = (LightDefinition)s2.Objects["light1"];
        var back = (LightDefinition)s2.Objects["light2"];
        Check("az 0, d 2.0, h 1.0 -> (0, 1.0, 2.0)", NearV(front.Position, new Vector3(0f, 1f, 2f)), front.Position.ToString());
        Check("az 90, d 1.5, h 1.7 -> (1.5, 1.7, 0)  (+X is the player's left)", NearV(left.Position, new Vector3(1.5f, 1.7f, 0f)), left.Position.ToString());
        Check("az 180, d 3.0, h 2.0 -> (0, 2.0, -3.0)", NearV(back.Position, new Vector3(0f, 2f, -3f)), back.Position.ToString());

        // ---- 3. Spot at az 90 aims along -X with the right pitch ----
        {
            var rot = left.RotationPitchYawRollDegrees;
            // toTarget = (0,1.55,0) - (1.5,1.7,0) = (-1.5, -0.15, 0). yaw = atan2(-1.5, 0) = -90. pitch = atan2(0.15, 1.5) = 5.7106 (positive = down).
            var expectPitch = MathF.Atan2(0.15f, 1.5f) / Rad;
            Check("spot az 90: yaw -90", Near(rot.Y, -90f, 0.01f), $"yaw {rot.Y:F4}");
            Check("spot az 90: pitch = atan2(0.15, 1.5) = 5.7106 down", Near(rot.X, expectPitch, 0.01f), $"pitch {rot.X:F4}");
            // Rotate the light's +Z by the same quaternion Stagehand builds: it must equal the normalised direction to the aim point.
            var q = Quaternion.CreateFromYawPitchRoll(rot.Y * Rad, rot.X * Rad, rot.Z * Rad);
            var dir = Vector3.Transform(Vector3.UnitZ, q);
            var want = Vector3.Normalize(new Vector3(0f, 1.55f, 0f) - left.Position);
            Check("spot az 90: beam direction = (-0.995, -0.0995, 0)", NearV(dir, want, 1e-3f) && dir.X < -0.99f, dir.ToString());
            Check("spot az 90 beam points along -X", dir.X < 0 && MathF.Abs(dir.Z) < 1e-3f);
        }
        {
            // aimHeight null on a Flat at az 180: aims level at the axis, so pitch 0 and the beam points along +Z (towards the player).
            var r = back.RotationPitchYawRollDegrees;
            var dir = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(r.Y * Rad, r.X * Rad, 0f));
            Check("flat az 180, aimHeight null: level (pitch 0) and aimed at the axis (+Z)", Near(r.X, 0f, 0.01f) && NearV(dir, new Vector3(0, 0, 1), 1e-3f), $"pitch {r.X:F3}, dir {dir}");
            Check("point light gets no rotation", front.RotationPitchYawRollDegrees == Vector3.Zero);
        }

        // ---- 4. Field mapping ----
        {
            var key = (LightDefinition)SceneConverter.Build(neon).Objects["light0"];
            Check("shape Spot, intensity 6, range 12", key.Shape == LightShape.Spot && Near(key.Intensity, 6f) && Near(key.Range, 12f));
            Check("coneDeg 30 -> SpotLightAngleDegrees, angularFalloffDeg 10 -> AngularFalloffDegrees", Near(key.SpotLightAngleDegrees, 30f) && Near(key.AngularFalloffDegrees, 10f));
            Check("falloff Quadratic, factor 1", key.FalloffFunction == LightFalloffFunction.Quadratic && Near(key.FalloffFactor, 1f));
            Check("shadows: character on, object off, specular on", key.EnableCharacterShadows && !key.EnableObjectShadows && key.EnableSpecularHighlights);
            Check("colour (0.1, 0.25, 1.0)", NearV(key.Color, new Vector3(0.1f, 0.25f, 1f)));
            Check("lower-case 'cubic' and 'spot' parse (tolerant)", left.FalloffFunction == LightFalloffFunction.Cubic && left.Shape == LightShape.Spot && Near(left.SpotLightAngleDegrees, 40f));
        }

        // ---- 5. Backdrop ----
        {
            var stage = SceneConverter.Build(neon);
            var bd = (BgObjectDefinition)stage.Objects["backdrop"];
            Check("backdrop model is fun_b0_m0766", bd.ModelGamePath == "bgcommon/hou/indoor/general/0766/bgparts/fun_b0_m0766.mdl");
            Check("backdrop at z = -distanceBehind (-2.0), base at feet (y 0), x 0", NearV(bd.Position, new Vector3(0f, 0f, -2f)), bd.Position.ToString());
            Check("backdrop scale (3,3,1), dye (0.05,0.05,0.3,1)", NearV(bd.Scale, new Vector3(3, 3, 1)) && Near(bd.DyeColor.X, 0.05f) && Near(bd.DyeColor.Z, 0.3f) && Near(bd.DyeColor.W, 1f));
            Check("backdrop with null light adds no extra light", !stage.Objects.ContainsKey("backdropLight"));

            var adj = new BackdropAdjust { ScaleX = 2f, ScaleY = 0.5f, DistanceOffset = 1f, YawOffsetDeg = 180f, HeightOffset = 0.25f };
            var bd2 = (BgObjectDefinition)SceneConverter.Build(neon, adj).Objects["backdrop"];
            Check("live adjust: scale (6, 1.5, 1), z -3, y 0.25, yaw 180",
                NearV(bd2.Scale, new Vector3(6f, 1.5f, 1f)) && NearV(bd2.Position, new Vector3(0f, 0.25f, -3f)) && Near(bd2.RotationPitchYawRollDegrees.Y, 180f), $"{bd2.Scale} {bd2.Position} {bd2.RotationPitchYawRollDegrees}");

            var s2b = SceneConverter.Build(azs);
            var wash = s2b.Objects.TryGetValue("backdropLight", out var w) ? (LightDefinition)w : null;
            Check("backdrop.light becomes an extra light at az 180 d 1.5 h 1.0 -> (0,1,-1.5)", wash != null && NearV(wash.Position, new Vector3(0f, 1f, -1.5f)), wash?.Position.ToString() ?? "missing");
        }

        // ---- 6. Round trip with the embedded canvas texture ----
        {
            var texPath = Path.Combine(root, "TestData", "canvas_test_d.tex");
            var tex = File.ReadAllBytes(texPath);
            var stage = SceneConverter.BuildCanvasTest(tex);
            var str = stage.ToDefinitionString();
            var ok = StageDefinition.TryParseDefinitionString(str, out var back2);
            Check("canvas stage definition string parses", ok && back2 != null, $"{str.Length} chars");
            if (ok && back2 != null)
            {
                var obj = back2.Objects["canvas"] as BgObjectDefinition;
                Check("canvas object keeps model path and modpack id", obj != null && obj.ModelGamePath == SceneConverter.CanvasModel && obj.ModpackId == SceneConverter.CanvasModpackId);
                Check("canvas modpack present, one resource keyed by the _d.tex game path",
                    back2.EmbeddedModpacks.TryGetValue(SceneConverter.CanvasModpackId, out var pack) && pack.ModdedResources.Count == 1 && pack.ModdedResources.ContainsKey(SceneConverter.CanvasDiffusePath));
                var res = back2.EmbeddedModpacks[SceneConverter.CanvasModpackId].ModdedResources[SceneConverter.CanvasDiffusePath] as EmbeddedModResourceDefinition;
                var bytes = res == null ? Array.Empty<byte>() : EmbeddedModResourceDefinition.DecompressDataBytes(res.CompressedDataBytes, res.CompressionScheme);
                Check("embedded texture bytes survive the round trip exactly", res != null && res.CompressionScheme == ModCompressionScheme.Zlib && bytes.AsSpan().SequenceEqual(tex), $"{tex.Length} bytes in, {bytes.Length} out, {res?.CompressedDataBytes.Length} compressed");
                Check("texture is a .tex: flags 0x800000, format 0x1450, 512x512", BitConverter.ToUInt32(bytes, 0) == 0x800000 && BitConverter.ToUInt32(bytes, 4) == 0x1450 && BitConverter.ToUInt16(bytes, 8) == 512 && BitConverter.ToUInt16(bytes, 10) == 512);
                Check("canvas placed at (1.06, 1.6, 1.06), 1.5 m from the axis, facing it (yaw -135)",
                    obj != null && NearV(obj.Position, new Vector3(1.0607f, 1.6f, 1.0607f), 1e-3f) && Near(obj.RotationPitchYawRollDegrees.Y, -135f, 0.01f), obj?.Position + " " + obj?.RotationPitchYawRollDegrees);
            }

            // A preset stage round-trips too.
            var ps = SceneConverter.Build(azs).ToDefinitionString();
            var ok2 = StageDefinition.TryParseDefinitionString(ps, out var pback);
            Check("preset stage (3 lights + backdrop + backdrop light) round-trips", ok2 && pback != null && pback.Objects.Count == 5, $"{pback?.Objects.Count} objects");
        }

        // ---- 7. User presets: save then load, and Mine ----
        {
            var tmp = Path.Combine(Path.GetTempPath(), "soundstage-selfcheck-" + Guid.NewGuid().ToString("N"));
            var userPath = Path.Combine(tmp, "user-scenes.json");
            try
            {
                var mine = neon.Clone();
                mine.Name = "My look!";
                mine.Id = SceneStore.Slug(mine.Name);
                mine.Category = "Mine";
                mine.Lights[0].Intensity = 9.5f;
                SceneStore.SaveUser(userPath, new[] { mine });
                var all = SceneStore.LoadAll(Path.Combine(tmp, "none.json"), userPath, null, _ => { }, new[] { SceneStore.BuiltInWarmKeyCoolFill() });
                var loaded = all.FirstOrDefault(p => p.Id == "my-look");
                Check("slug of 'My look!' is my-look", SceneStore.Slug("My look!") == "my-look");
                Check("saved user preset reloads in 'Mine' with slider tweak, backdrop and references intact",
                    loaded != null && loaded.Category == "Mine" && Near(loaded.Lights[0].Intensity, 9.5f) && loaded.Backdrop != null && loaded.References != null, loaded == null ? "missing" : "");
                Check("built-in preset present when no shipped file", all.Any(p => p.Id == "builtin-warm-key-cool-fill"));
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---- 8. Read-only look at the real shipped presets.json, if there is one ----
        {
            var real = Path.Combine(root, "scenes.json");
            if (File.Exists(real))
            {
                var rl = new List<string>();
                var rp = SceneStore.LoadFile(real, rl.Add);
                var skipped = rl.Count(l => l.Contains("skipped"));
                Check("real scenes.json: loads, nothing skipped", rp.Count > 0 && skipped == 0, $"{rp.Count} loaded, {skipped} skipped, {rp.Count(p => p.Backdrop != null)} with backdrop");
                foreach (var l in rl.Where(l => l.Contains("skipped"))) Console.WriteLine("      log: " + l);
                foreach (var p in rp)
                {
                    var st = SceneConverter.Build(p);
                    var parsed = StageDefinition.TryParseDefinitionString(st.ToDefinitionString(), out _);
                    if (!parsed) Check($"real preset '{p.Id}' round-trips", false);
                }
            }
            else Console.WriteLine("      (no scenes.json beside the source yet, real-file check skipped)");
        }

    }
}

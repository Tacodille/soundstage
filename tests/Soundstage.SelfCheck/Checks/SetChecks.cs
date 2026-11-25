// Part 9: the sweep layout, prop kits, atmosphere catalogue, picture conversion and how they join the stage.
using System.Numerics;
using System.Text.Json;
using Soundstage;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage.SelfCheck;

public sealed class SetChecks : CheckArea
{
    public override string Name => "Set: sweep, kits, atmosphere, picture";
    public override int Order => 90;

    private static readonly string[] ShippedKits = { "crates", "fireside", "greenery", "interview", "library", "lounge", "throne-room" };

    public override void Run()
    {
        Sweep();
        KitChecks();
        AtmosphereChecks();
        Picture();
        Joining();
    }

    // ---- 9.2 sweep ----

    private static Quaternion Rot(Vector3 pyr) =>
        Quaternion.CreateFromYawPitchRoll(pyr.Y * Rad, pyr.X * Rad, pyr.Z * Rad);

    /// <summary>A piece's local point (in partition units: x -2..2, y 0..3, z -0.113..0.113) as a stage-frame point.</summary>
    private static Vector3 World(SweepPiece p, float lx, float ly, float lz) =>
        p.Position + Vector3.Transform(new Vector3(lx, ly, lz) * p.Scale, Rot(p.PitchYawRollDeg));

    private void Sweep()
    {
        var spec = new SweepSpec(8f, 2.5f, 1f, 3f, 2.5f);
        var pieces = SweepBuilder.Layout(spec);
        var wall = pieces.Where(p => p.Part == "wall").ToList();
        var cove = pieces.Where(p => p.Part == "cove").ToList();
        var floor = pieces.Where(p => p.Part == "floor").ToList();
        Check("sweep 8 m wide: 2 wall, 2 cove, 4 floor pieces", wall.Count == 2 && cove.Count == 2 && floor.Count == 4, $"{wall.Count}/{cove.Count}/{floor.Count}");
        Check("piece ids are unique", pieces.Select(p => p.Id).Distinct().Count() == pieces.Count);

        // Measured partition: 4 m wide, 3 m tall, foot at the pivot. A width of 8 needs two across, each a little over 4 m after the overlap.
        Check("measured partition size is 4 x 3 x 0.2258", PartitionSize.Width == 4f && PartitionSize.Height == 3f && Near(PartitionSize.Thickness, 0.2258f, 1e-4f));
        var xs = wall.Select(p => p.Position.X).OrderBy(x => x).ToList();
        Check("wall pieces centred at x = -2 and +2", Near(xs[0], -2f) && Near(xs[1], 2f), string.Join(",", xs));
        var wallLeft = wall.Min(p => p.Position.X - 2f * p.Scale.X);
        var wallRight = wall.Max(p => p.Position.X + 2f * p.Scale.X);
        Check("wall covers the whole 8 m with the overlap", wallLeft <= -4f && wallRight >= 4f, $"{wallLeft:0.000}..{wallRight:0.000}");
        Check("neighbouring wall pieces overlap, no gap", wall.OrderBy(p => p.Position.X).Zip(wall.OrderBy(p => p.Position.X).Skip(1), (a, b) => b.Position.X - 2f * b.Scale.X <= a.Position.X + 2f * a.Scale.X).All(x => x));
        Check("wall stands at z = -distance on the cove's top", wall.All(p => Near(p.Position.Z, -2.5f) && Near(p.Position.Y, 1f) && p.PitchYawRollDeg == Vector3.Zero));
        Check("wall top is cove + 3 m (plus the overlap)", wall.All(p => Near(World(p, 0, 3f, 0).Y, 1f + 3f + SweepBuilder.Overlap, 1e-3f)));

        // The cove must run from the wall's foot (y = cove, z = -distance) down to the floor's start (y ~ 0, z = -distance + cove).
        var c = cove[0];
        var top = World(c, 0, 0, 0);
        var bottom = World(c, 0, PartitionSize.Height, 0);
        Check("cove starts at the wall's foot", Near(top.Y, 1f + SweepBuilder.Overlap, 1e-3f) && Near(top.Z, -2.5f, 1e-3f), top.ToString());
        Check("cove ends on the floor at -distance + cove, at 45 degrees", Near(bottom.Y, 0f, 0.03f) && Near(bottom.Z, -1.5f, 0.03f), bottom.ToString());
        var run = bottom.Z - top.Z;
        var drop = top.Y - bottom.Y;
        Check("cove slope is 45 degrees (run equals drop)", Near(run, drop, 0.03f), $"run {run:0.000} drop {drop:0.000}");

        // Floor: first row starts where the cove ends, laid flat, top surface a hair above the ground.
        var f0 = floor.OrderBy(p => p.Position.Z).First();
        var start = World(f0, 0, 0, 0);
        var farEnd = World(f0, 0, PartitionSize.Height, 0);
        Check("floor starts at the cove's foot", Near(start.Z, -1.5f - SweepBuilder.Overlap, 0.02f), start.Z.ToString("0.000"));
        Check("floor runs forward (+Z) and stays level", farEnd.Z > start.Z + 1.5f && Near(farEnd.Y, start.Y, 1e-3f), $"{start.Z:0.00}->{farEnd.Z:0.00}, y {start.Y:0.000}/{farEnd.Y:0.000}");
        var ys = new List<float>();
        foreach (var f in floor)
            foreach (var lz in new[] { -PartitionSize.Thickness / 2f, PartitionSize.Thickness / 2f })
                ys.Add(World(f, 0, 0, lz).Y);
        Check("floor top is 1.2 cm above the ground", Near(ys.Max(), SweepBuilder.FloorLift, 1e-4f), ys.Max().ToString("0.0000"));
        Check("floor reaches 2.5 m in front", floor.Max(p => World(p, 0, PartitionSize.Height, 0).Z) >= 2.5f - 1e-3f);
        var rows = floor.Select(p => p.Position.Z).Distinct().OrderBy(z => z).ToList();
        Check("floor rows overlap, no gap along z", rows.Zip(rows.Skip(1), (a, b) => b <= World(floor.First(p => Near(p.Position.Z, a)), 0, PartitionSize.Height, 0).Z).All(x => x));

        // Limits.
        var huge = SweepBuilder.Layout(new SweepSpec(100f, 100f, 100f, 100f, 100f));
        Check("a huge request is clamped and stays within the piece cap", huge.Count <= SweepBuilder.MaxPieces && huge.Max(p => MathF.Abs(p.Position.X)) <= 8f + 1e-3f, $"{huge.Count} pieces");
        var tiny = SweepBuilder.Layout(new SweepSpec(0.1f, 0f, 0f, 0f, 0f));
        Check("a tiny request still builds a sweep", tiny.Count >= 3 && tiny.All(p => p.Scale.X > 0f && p.Scale.Y > 0f));
        Check("narrow sweeps use one column, wide ones more", SweepBuilder.Columns(4f) == 1 && SweepBuilder.Columns(4.1f) == 2 && SweepBuilder.Columns(16f) == 4);
        Check("every scale is at most 1.05 (pieces repeat, they are not stretched)", pieces.All(p => p.Scale.X <= 1.05f && p.Scale.Y <= 1.05f));
    }

    // ---- 9.3 kits ----

    private void KitChecks()
    {
        var logs = new List<string>();
        var kits = KitStore.LoadDirectory(Path.Combine(root, "kits"), logs.Add);
        Check("all seven shipped kits load with nothing skipped", kits.Select(k => k.Id).OrderBy(x => x).SequenceEqual(ShippedKits) && logs.Count == 0, string.Join("; ", logs));
        Check("at least four kits ship", kits.Count >= 4);
        Check("every kit has 1 to 12 objects", kits.All(k => k.Objects.Count is >= 1 and <= KitStore.MaxObjects), string.Join(",", kits.Select(k => k.Objects.Count)));
        Check("every model path is a housing furniture path in data", kits.SelectMany(k => k.Objects).All(o => o.Model.StartsWith("bgcommon/hou/indoor/general/") && o.Model.EndsWith(".mdl")));
        Check("kit files hold no model path in code: no kit path appears in Kits.cs", !File.ReadAllText(Path.Combine(root, "Set", "Kits.cs")).Contains("bgcommon/hou"));

        // Control: a kit with 13 objects is rejected (plan validation row).
        var twelve = Enumerable.Range(0, 12).Select(i => new KitObject { Name = "o" + i, Model = "bgcommon/hou/indoor/general/0308/bgparts/fun_b0_m0308.mdl" }).ToList();
        var ok12 = new Kit { Id = "twelve", Name = "Twelve", Objects = twelve };
        Check("control: a kit with 12 objects is accepted", KitStore.Validate(ok12) == null, KitStore.Validate(ok12) ?? "");
        var bad13 = new Kit { Id = "thirteen", Name = "Thirteen", Objects = twelve.Append(new KitObject { Model = twelve[0].Model }).ToList() };
        var why13 = KitStore.Validate(bad13);
        Check("a kit with 13 objects is rejected", why13 != null && why13.Contains("13"), why13 ?? "accepted");

        var json13 = JsonSerializer.Serialize(bad13, KitStore.Options);
        var parseLogs = new List<string>();
        Check("a 13-object kit file is skipped by the loader with a log line", KitStore.Parse(json13, parseLogs.Add, "thirteen.json") == null && parseLogs.Count == 1, string.Join("|", parseLogs));

        Kit Mutate(Action<Kit> f) { var k = JsonSerializer.Deserialize<Kit>(JsonSerializer.Serialize(ok12, KitStore.Options), KitStore.Options)!; f(k); return k; }
        Check("an upper-case model path is rejected", KitStore.Validate(Mutate(k => k.Objects[0].Model = "bgcommon/hou/indoor/General/0308/bgparts/fun_b0_m0308.mdl")) != null);
        Check("a backslash model path is rejected", KitStore.Validate(Mutate(k => k.Objects[0].Model = @"bgcommon\hou\indoor\general\0308\bgparts\fun_b0_m0308.mdl")) != null);
        Check("a model path that is not .mdl is rejected", KitStore.Validate(Mutate(k => k.Objects[0].Model = "bgcommon/hou/indoor/general/0308/texture/x.tex")) != null);
        Check("a bad id is rejected", KitStore.Validate(Mutate(k => k.Id = "Has Space")) != null);
        Check("scale 0 and scale 9 are rejected", KitStore.Validate(Mutate(k => k.Objects[0].Scale = 0f)) != null && KitStore.Validate(Mutate(k => k.Objects[0].Scale = 9f)) != null);
        Check("an offset past 15 m is rejected", KitStore.Validate(Mutate(k => k.Objects[0].Offset = new[] { 0f, 0f, 40f })) != null);
        Check("an empty kit is rejected", KitStore.Validate(Mutate(k => k.Objects.Clear())) != null);

        // Placement maths: a positive yaw turns +Z towards +X, so an offset of 1 m to +X, turned 90 degrees, lands at z = -1.
        var r = KitBuilder.Rotate(new Vector3(1f, 0f, 0f), 90f);
        Check("yaw 90 turns an offset on +X to -Z", NearV(r, new Vector3(0f, 0f, -1f)), r.ToString());
        var r2 = KitBuilder.Rotate(new Vector3(0f, 0f, 1f), 90f);
        Check("yaw 90 turns +Z to +X", NearV(r2, new Vector3(1f, 0f, 0f)), r2.ToString());

        var probe = new Kit
        {
            Id = "probe", Name = "Probe", Anchor = new KitPlacement { X = 2f, Z = -1f, YawDeg = 90f },
            Objects = new List<KitObject> { new() { Name = "a", Model = twelve[0].Model, Offset = new[] { 1f, 0.5f, 0f }, YawDeg = 10f, Scale = 2f } },
        };
        var placed = KitBuilder.Build(probe, new KitPlacement { X = 1f, Z = 1f, YawDeg = 0f });
        var obj = placed[0].Object;
        Check("kit object lands at anchor + placement + rotated offset", NearV(obj.Position, new Vector3(3f, 0.5f, -1f)), obj.Position.ToString());
        Check("kit object yaw is its own plus the anchor's", Near(obj.RotationPitchYawRollDegrees.Y, 100f), obj.RotationPitchYawRollDegrees.Y.ToString());
        Check("kit object scale is uniform", obj.Scale == new Vector3(2f));
        Check("kit object ids are per kit and per index", placed[0].Id == "kit-probe-0");
        var flipped = KitBuilder.Build(probe, new KitPlacement { YawDeg = 180f })[0].Object;
        Check("Flip 180 turns the object 180 degrees more", Near(flipped.RotationPitchYawRollDegrees.Y, 10f + 90f + 180f), flipped.RotationPitchYawRollDegrees.Y.ToString());
        Check("a shipped kit builds to as many objects as it lists", kits.All(k => KitBuilder.Build(k, null).Count == k.Objects.Count));
    }

    // ---- 9.4 atmosphere ----

    private void AtmosphereChecks()
    {
        var logs = new List<string>();
        var catalogue = AtmosphereStore.Load(Path.Combine(root, "atmosphere.json"), logs.Add);
        Check("atmosphere.json loads 9 items with nothing skipped", catalogue.Count == 9 && logs.Count == 0, $"{catalogue.Count}, {string.Join("; ", logs)}");
        Check("at least three atmosphere items", catalogue.Count >= 3);
        Check("both kinds are there: visual effects and sound", catalogue.Any(i => i.Kind == "vfx") && catalogue.Any(i => i.Kind == "sound"));

        AtmosphereItem Fresh(Action<AtmosphereItem> f) { var i = new AtmosphereItem { Id = "x", Name = "X", Kind = "vfx", Path = "vfx/common/eff/x.avfx" }; f(i); return i; }
        Check("control: a plain vfx item is accepted", AtmosphereStore.Validate(Fresh(_ => { })) == null);
        Check("a vfx path that is not .avfx is rejected", AtmosphereStore.Validate(Fresh(i => i.Path = "vfx/common/eff/x.tex")) != null);
        Check("a sound path that is not .scd is rejected", AtmosphereStore.Validate(Fresh(i => { i.Kind = "sound"; i.Path = "music/x.mdl"; })) != null);
        Check("an unknown kind is rejected", AtmosphereStore.Validate(Fresh(i => i.Kind = "smell")) != null);
        Check("a volume of 9 is rejected", AtmosphereStore.Validate(Fresh(i => { i.Kind = "sound"; i.Path = "music/ffxiv/x.scd"; i.Volume = 9f; })) != null);

        var atmos = new SceneAtmosphere { Haze = "light", Dust = true, Sun = true };
        var placed = AtmosphereBuilder.Build(atmos, catalogue);
        Check("the old flags (haze light, dust, sun) become three objects", placed.Count == 3 && placed.Select(p => p.Id).OrderBy(x => x).SequenceEqual(new[] { "atmos-dust", "atmos-haze", "atmos-sun" }), string.Join(",", placed.Select(p => p.Id)));
        var haze = placed.First(p => p.Id == "atmos-haze").Object as VfxObjectDefinition;
        Check("a vfx item becomes a VfxObjectDefinition with its path and offset", haze != null && haze.VfxGamePath.EndsWith(".avfx") && haze.Position.Z < 0f, haze?.VfxGamePath ?? "null");
        var heavy = AtmosphereBuilder.Build(new SceneAtmosphere { Haze = "heavy" }, catalogue).Single().Object as VfxObjectDefinition;
        Check("heavy haze is 1.8 times bigger than light haze", heavy != null && haze != null && Near(heavy.Scale.X / haze.Scale.X, 1.8f, 1e-3f));

        var sound = AtmosphereBuilder.Build(new SceneAtmosphere { Items = { new AtmosphereChoice { Id = "room-night", Amount = 0.5f } } }, catalogue).Single().Object as SoundObjectDefinition;
        Check("a sound item becomes a SoundObjectDefinition with index and scaled volume", sound != null && sound.SoundGamePath.EndsWith(".scd") && sound.SoundIndex == 0 && Near(sound.Volume, 0.3f, 1e-3f), sound?.Volume.ToString() ?? "null");

        var off = AtmosphereBuilder.Build(new SceneAtmosphere { Haze = "light", Items = { new AtmosphereChoice { Id = "haze", Enabled = false } } }, catalogue);
        Check("an explicit item switched off overrides the old flag", off.Count == 0);
        var missing = new List<string>();
        var none = AtmosphereBuilder.Build(new SceneAtmosphere { Items = { new AtmosphereChoice { Id = "no-such-thing" } } }, catalogue, missing);
        Check("an item the catalogue lacks is skipped and reported", none.Count == 0 && missing.SequenceEqual(new[] { "no-such-thing" }));
        Check("every shipped item builds", catalogue.All(i => AtmosphereBuilder.Build(new SceneAtmosphere { Items = { new AtmosphereChoice { Id = i.Id } } }, catalogue).Count == 1));
    }

    // ---- 9.6 picture ----

    private void Picture()
    {
        Check("FitSize keeps a small image as it is", PictureProp.FitSize(800, 600) == (800, 600));
        Check("FitSize shrinks the long side to 1024", PictureProp.FitSize(2048, 1024) == (1024, 512) && PictureProp.FitSize(3000, 4000) == (768, 1024));
        Check("FitSize never goes below 1 pixel", PictureProp.FitSize(1, 5000) == (1, 1024) && PictureProp.FitSize(1, 1) == (1, 1));

        var texPath = Path.Combine(root, "TestData", "canvas_test_d.tex");
        var original = File.ReadAllBytes(texPath);
        var read = PictureProp.ReadTex(original)!.Value;
        var made = PictureProp.ToTex(read.Rgba, read.W, read.H);
        Check("the test texture is 512 x 512", read.W == 512 && read.H == 512);
        Check("converted .tex has the same length as the Keylight beta's test texture", made.Length == original.Length, $"{made.Length} vs {original.Length}");
        Check("header (flags, format, size, mip count, mip offsets) is byte for byte the same", made.AsSpan(0, 80).SequenceEqual(original.AsSpan(0, 80)));
        Check("mip 0 pixels are byte for byte the same", made.AsSpan(80, 512 * 512 * 4).SequenceEqual(original.AsSpan(80, 512 * 512 * 4)));
        var worst = 0;
        for (var i = 80 + 512 * 512 * 4; i < made.Length; i++) worst = Math.Max(worst, Math.Abs(made[i] - original[i]));
        Check("smaller mips are within 2 levels of the original's (rounding only)", worst <= 2, $"largest difference {worst}");

        // Decode a real PNG: the preview of the same pattern must come out as the same pixels.
        var png = Path.Combine(root, "TestData", "canvas_test_d_preview.png");
        var (tex, w, h) = PictureProp.FromFile(png);
        var back = PictureProp.ReadTex(tex)!.Value;
        Check("PNG decodes to 512 x 512", w == 512 && h == 512);
        Check("PNG pixels equal the test texture's mip 0", back.Rgba.AsSpan().SequenceEqual(read.Rgba));

        // A big image is capped at 1024 and keeps its shape.
        var big = new byte[2048 * 1024 * 4];
        for (var i = 0; i < big.Length; i += 4) { big[i] = 200; big[i + 1] = 100; big[i + 2] = 50; big[i + 3] = 255; }
        var small = PictureProp.Resize(big, 2048, 1024, 1024, 512);
        Check("resize of a flat image keeps its colour exactly", small.Length == 1024 * 512 * 4 && small[0] == 200 && small[1] == 100 && small[2] == 50 && small[3] == 255 && small[^1] == 255);
        var bigTex = PictureProp.ToTex(small, 1024, 512);
        Check("a 1024 x 512 .tex has the full mip chain down to 1 x 1 (11 levels)", bigTex[14] == 11, bigTex[14].ToString());
        Check("a non-power-of-two image converts (300 x 200)", PictureProp.ToTex(new byte[300 * 200 * 4], 300, 200)[14] == 9);
        Check("a wrong-sized buffer is refused", Throws(() => PictureProp.ToTex(new byte[10], 4, 4)));
        Check("garbage bytes are not an image", Throws(() => PictureProp.FromImageBytes(new byte[] { 1, 2, 3, 4, 5 })));

        // The prop itself.
        var pic = new ScenePicture { Height = 1.2f, X = 1.5f, Z = 1f, CentreHeight = 1.5f };
        var (obj, pack) = PictureProp.Build(pic, tex, 512, 256);
        Check("a 2:1 picture is twice as wide as high at the quad", Near(obj.Scale.X * PictureProp.QuadWidth, 2f * obj.Scale.Y * PictureProp.QuadHeight, 1e-3f), obj.Scale.ToString());
        Check("quad height at scale is the requested height", Near(obj.Scale.Y * PictureProp.QuadHeight, 1.2f, 1e-3f));
        Check("the picture's centre is at the requested height", Near(obj.Position.Y + PictureProp.QuadCentreY * obj.Scale.Y, 1.5f, 1e-3f));
        Check("the picture faces the subject at the origin", NearV(new Vector3(MathF.Sin(obj.RotationPitchYawRollDegrees.Y * Rad), 0f, MathF.Cos(obj.RotationPitchYawRollDegrees.Y * Rad)), Vector3.Normalize(new Vector3(-obj.Position.X, 0f, -obj.Position.Z)), 1e-3f));
        Check("the modpack swaps the canvas's own diffuse texture", pack.ModdedResources.ContainsKey(PictureProp.DiffusePath) && obj.ModpackId == PictureProp.ModpackId);
        Check("the picture model and texture paths match the canvas test's", PictureProp.Model == SceneConverter.CanvasModel && PictureProp.DiffusePath == SceneConverter.CanvasDiffusePath);
    }

    private static bool Throws(Action a) { try { a(); return false; } catch { return true; } }

    // ---- 9.7 how it all joins the stage ----

    private void Joining()
    {
        Check("this build applies the set and atmosphere blocks", SceneStore.AppliedBlocks.Contains("set") && SceneStore.AppliedBlocks.Contains("atmosphere"), string.Join(",", SceneStore.AppliedBlocks));

        var kits = KitStore.LoadDirectory(Path.Combine(root, "kits"), _ => { });
        var catalogue = AtmosphereStore.Load(Path.Combine(root, "atmosphere.json"), _ => { });
        var texBytes = File.ReadAllBytes(Path.Combine(root, "TestData", "canvas_test_d.tex"));

        var scene = new Scene
        {
            Id = "t", Name = "T",
            Lights = { new SceneLight { Role = "key", Shape = "Point" } },
            Palettes = { ["Default"] = new() { ["key"] = new[] { 1f, 1f, 1f }, ["backdrop"] = new[] { 0.2f, 0.4f, 0.8f } } },
            DefaultPalette = "Default",
            Backdrop = new SceneBackdrop { Color = new[] { 1f, 0f, 0f }, DistanceBehind = 2f, Light = new SceneLight { Role = "bg", Shape = "Point" } },
            Set = new SceneSet { Kind = "sweep", ColorRole = "backdrop", Props = { "lounge", "crates" }, Picture = new ScenePicture { Path = "fake.png" } },
            Atmosphere = new SceneAtmosphere { Haze = "light", Items = { new AtmosphereChoice { Id = "room-night" } } },
        };

        var stage = new StageDefinition();
        SetApply.AddTo(stage, scene, kits, catalogue, p => p == "fake.png" ? (texBytes, 512, 512) : null);
        var sweep = stage.Objects.Where(o => o.Key.StartsWith("sweep-")).ToList();
        var expectSweep = SweepBuilder.Layout(SetApply.SweepSpecFor(scene)).Count;
        Check("the sweep, both kits, the picture and two atmosphere items are in the stage",
            sweep.Count == expectSweep && stage.Objects.Keys.Count(k => k.StartsWith("kit-lounge-")) == 4 && stage.Objects.Keys.Count(k => k.StartsWith("kit-crates-")) == 7
            && stage.Objects.ContainsKey("picture") && stage.Objects.ContainsKey("atmos-haze") && stage.Objects.ContainsKey("atmos-room-night"), $"{stage.Objects.Count} objects");
        Check("the sweep's distance comes from the Scene's old backdrop when the Set block has none", SetApply.SweepSpecFor(scene).Distance == 2f);
        Check("the sweep's colour is the Scene's palette backdrop colour", sweep.All(o => o.Value is BgObjectDefinition b && NearV(new Vector3(b.DyeColor.X, b.DyeColor.Y, b.DyeColor.Z), new Vector3(0.2f, 0.4f, 0.8f))));
        Check("the picture rides on its own modpack", stage.EmbeddedModpacks.ContainsKey(PictureProp.ModpackId));
        Check("a missing picture file adds no picture and no error", MissingPicture(scene, kits, catalogue));

        scene.Set.ColorRole = "";
        scene.Set.Color = new[] { 1f, 1f, 1f };
        Check("a fixed colour is used when there is no palette role", NearV(new Vector3(SetApply.SweepColour(scene).X, SetApply.SweepColour(scene).Y, SetApply.SweepColour(scene).Z), Vector3.One));
        scene.Set.ColorRole = "backdrop";

        // Through the real converter (which reads the loaded catalogue): the old panel gives way to the sweep, its light stays.
        SetCatalogue.Load(root, _ => { });
        var full = SceneConverter.Build(scene);
        Check("converter: sweep replaces the old panel", !full.Objects.ContainsKey("backdrop") && full.Objects.Keys.Any(k => k.StartsWith("sweep-")));
        Check("converter: the panel's own light stays", full.Objects.ContainsKey("backdropLight") && full.Objects.ContainsKey("light0"));
        scene.Set.Kind = "partition";
        Check("converter: kind partition keeps the old panel", SceneConverter.Build(scene).Objects.ContainsKey("backdrop") && !SceneConverter.Build(scene).Objects.Keys.Any(k => k.StartsWith("sweep-")));
        scene.Set.Kind = "sweep";

        // The whole thing survives the trip through the definition string Stagehand receives.
        var s = full.ToDefinitionString();
        var parsed = StageDefinition.TryParseDefinitionString(s, out var back);
        Check("the stage with a sweep, kits and atmosphere round-trips through the definition string", parsed && back.Objects.Count == full.Objects.Count, $"{full.Objects.Count} objects, {s.Length} chars");
        Check("vfx and sound objects survive the round trip", back.Objects.Values.OfType<VfxObjectDefinition>().Any() && back.Objects.Values.OfType<SoundObjectDefinition>().Any());

        // The Scene file keeps what the tab wrote.
        scene.Set.KitPlacements["lounge"] = new KitPlacement { X = 0.5f, Z = -0.5f, YawDeg = 180f };
        var json = JsonSerializer.Serialize(scene, SceneStore.WriteOptions);
        var again = JsonSerializer.Deserialize<Scene>(json, SceneStore.ReadOptions)!;
        Check("a Scene's set and atmosphere survive save and load", again.Set!.Props.SequenceEqual(new[] { "lounge", "crates" }) && again.Set.KitPlacements["lounge"].YawDeg == 180f
            && again.Set.Picture!.Path == "fake.png" && again.Atmosphere!.Items.Single().Id == "room-night" && again.Set.SweepWidth == 8f);

        // The shipped Scenes: those with a sweep gain one; the rest are unchanged.
        var shipped = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), _ => { });
        var sweepScenes = shipped.Where(x => x.Set?.Kind == "sweep").ToList();
        Check("five shipped Scenes ask for a sweep and each builds one", sweepScenes.Count == 5 && sweepScenes.All(x => SceneConverter.Build(SceneStore.ApplyPalette(x, null)).Objects.Keys.Any(k => k.StartsWith("sweep-"))), string.Join(",", sweepScenes.Select(x => x.Id)));
        Check("a shipped Scene without a set builds no sweep or kit", shipped.Where(x => x.Set == null).All(x => !SceneConverter.Build(x).Objects.Keys.Any(k => k.StartsWith("sweep-") || k.StartsWith("kit-"))));
        Check("no shipped Scene builds more than 40 set objects", sweepScenes.All(x => SceneConverter.Build(x).Objects.Keys.Count(k => k.StartsWith("sweep-")) <= SweepBuilder.MaxPieces));
        Check("the catalogue loaded for the converter has the shipped kits", SetCatalogue.Kits.Count == ShippedKits.Length && SetCatalogue.Atmosphere.Count == 9);
    }

    private static bool MissingPicture(Scene scene, IReadOnlyList<Kit> kits, IReadOnlyList<AtmosphereItem> catalogue)
    {
        var stage = new StageDefinition();
        SetApply.AddTo(stage, scene, kits, catalogue, _ => null);
        return !stage.Objects.ContainsKey("picture") && !stage.EmbeddedModpacks.ContainsKey(PictureProp.ModpackId);
    }
}

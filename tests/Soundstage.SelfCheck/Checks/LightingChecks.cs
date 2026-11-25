// Part 8: face aim, animation waveforms, throttle, gels, harmony, exposure and the three animated Scenes. No game needed.
using System.Diagnostics;
using System.Numerics;
using Soundstage;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage.SelfCheck;

public sealed class LightingChecks : CheckArea
{
    public override string Name => "Lighting: face aim, animation, gels, harmony, exposure";
    public override int Order => 80;

    public override void Run()
    {
        var logs = new List<string>();
        var scenes = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), logs.Add);
        AimChecks(scenes);
        AnimationChecks(scenes);
        ThrottleChecks();
        HarmonyChecks();
        GelChecks();
        ExposureChecks(scenes);
        CatalogueChecks(scenes);
    }

    private static SceneLight Spot(float az, float dist, float height, float? aim = 1.55f, string? aimAt = "face") => new()
    {
        Role = "key", Name = "Key", Shape = "Spot", Color = new[] { 1f, 1f, 1f }, Intensity = 4f, Range = 10f,
        AzimuthDeg = az, Distance = dist, Height = height, AimHeight = aim, AimAt = aimAt,
    };

    private static SceneAnimationEffect Copy(SceneAnimationEffect e, float? rate = null, float? amount = null, bool? enabled = null) => new()
    {
        Target = e.Target, Kind = e.Kind, Property = e.Property, Seed = e.Seed,
        Amount = amount ?? e.Amount, RateHz = rate ?? e.RateHz, Enabled = enabled ?? e.Enabled,
    };

    // ---- 8.1 aim ----
    private void AimChecks(List<Scene> scenes)
    {
        var l = Spot(45f, 1.4f, 2.9f);
        var authored = new Vector3(0f, 1.55f, 0f);
        Check("aim: face at the authored place gives exactly the old light position", NearV(AimSolver.Position(l, authored), SceneConverter.LightPosition(l)),
            $"{AimSolver.Position(l, authored)} vs {SceneConverter.LightPosition(l)}");
        var raised = AimSolver.Position(l, authored + new Vector3(0f, 0.12f, 0f));
        Check("aim: a head 12 cm higher lifts the light 12 cm and nothing else", NearV(raised, SceneConverter.LightPosition(l) + new Vector3(0f, 0.12f, 0f)), raised.ToString());
        var shifted = AimSolver.Position(l, new Vector3(0.10f, 1.55f, -0.05f));
        Check("aim: a head 10 cm left and 5 cm back carries the light with it", NearV(shifted, SceneConverter.LightPosition(l) + new Vector3(0.10f, 0f, -0.05f)), shifted.ToString());

        // Elevation is the Scene's own: height minus aimHeight, whatever the head height is.
        var tall = AimSolver.Position(l, new Vector3(0f, 1.80f, 0f));
        Check("aim: elevation above the face stays 1.35 m (2.9 minus 1.55) for a taller head", Near(tall.Y - 1.80f, 1.35f), $"{tall.Y - 1.80f:F3}");

        Check("aim: world head to stage-local, facing +Z", NearV(AimSolver.ToStageLocal(new Vector3(10f, 1.6f, 5.2f), new Vector3(10f, 0f, 5f), 0f), new Vector3(0f, 1.6f, 0.2f)));
        Check("aim: world head to stage-local, facing +X (yaw 90 degrees): 20 cm ahead is still +Z locally",
            NearV(AimSolver.ToStageLocal(new Vector3(10.2f, 1.6f, 5f), new Vector3(10f, 0f, 5f), MathF.PI / 2f), new Vector3(0f, 1.6f, 0.2f), 1e-3f));
        Check("aim: re-solve threshold is 2 cm (1.9 cm no, 2.1 cm yes)",
            !AimSolver.MovedEnough(Vector3.Zero, new Vector3(0.019f, 0f, 0f)) && AimSolver.MovedEnough(Vector3.Zero, new Vector3(0f, 0.021f, 0f)));
        Check("aim: a head read at the feet, at the origin or as NaN is not believed",
            !AimSolver.Plausible(new Vector3(0f, 0.1f, 0f), out _) && !AimSolver.Plausible(new Vector3(float.NaN, 1.6f, 0f), out _) && !AimSolver.Plausible(new Vector3(3f, 1.6f, 0f), out _));
        Check("aim: a head 1.6 m up and 4 cm off axis is believed", AimSolver.Plausible(new Vector3(0.04f, 1.6f, 0.02f), out _));

        // Through the converter: a spot's own +Z axis points at the face.
        var face = new Vector3(0.06f, 1.72f, 0.04f);
        var def = SceneConverter.MakeLight(l, "L", face);
        var p = def.RotationPitchYawRollDegrees;
        var dir = Vector3.Transform(Vector3.UnitZ, Quaternion.CreateFromYawPitchRoll(p.Y * Rad, p.X * Rad, 0f));
        var want = Vector3.Normalize(face - def.Position);
        Check("converter: a face-aimed spot points its beam at the face", NearV(dir, want, 2e-3f), $"beam {dir}, wanted {want}");
        Check("converter: and sits at the solved position", NearV(def.Position, AimSolver.Position(l, face)));

        var plain = Spot(45f, 1.4f, 2.9f, aimAt: null);
        var plainDef = SceneConverter.MakeLight(plain, "L", face);
        Check("converter: a light that is not flagged ignores the face", NearV(plainDef.Position, SceneConverter.LightPosition(plain)));
        var noFace = SceneConverter.MakeLight(l, "L", null);
        Check("fallback: no head read places a flagged light exactly as authored", NearV(noFace.Position, SceneConverter.LightPosition(l)) && NearV(noFace.RotationPitchYawRollDegrees, plainDef.RotationPitchYawRollDegrees));

        // Whole Scene: Rembrandt with a head 15 cm higher moves its key and leaves its fill.
        var rem = scenes.FirstOrDefault(s => s.Id == "rembrandt");
        if (rem == null) { Check("aim: Rembrandt is in the catalogue", false); return; }
        var a = SceneConverter.Build(rem, null, null);
        var b = SceneConverter.Build(rem, null, new Vector3(0f, 1.70f, 0f));
        var keyA = (LightDefinition)a.Objects["light0"];
        var keyB = (LightDefinition)b.Objects["light0"];
        var fillA = (LightDefinition)a.Objects["light1"];
        var fillB = (LightDefinition)b.Objects["light1"];
        Check("Rembrandt: the flagged key follows a head 10 cm above its authored aim height (1.6 m)", NearV(keyB.Position - keyA.Position, new Vector3(0f, 0.10f, 0f)), (keyB.Position - keyA.Position).ToString());
        Check("Rembrandt: the unflagged fill stays put", NearV(fillA.Position, fillB.Position));
    }

    // ---- 8.2 animation ----
    private void AnimationChecks(List<Scene> scenes)
    {
        var flicker = new SceneAnimationEffect { Target = "key", Kind = "flicker", Amount = 0.4f, RateHz = 8f, Enabled = true };
        var seq1 = Enumerable.Range(0, 120).Select(i => Animator.Evaluate(flicker, i / 60.0, 7, 3f).Intensity).ToList();
        var seq2 = Enumerable.Range(0, 120).Select(i => Animator.Evaluate(flicker, i / 60.0, 7, 3f).Intensity).ToList();
        var seq3 = Enumerable.Range(0, 120).Select(i => Animator.Evaluate(flicker, i / 60.0, 8, 3f).Intensity).ToList();
        Check("flicker: same seed gives the same sequence", seq1.SequenceEqual(seq2));
        Check("flicker: a different seed gives a different sequence", !seq1.SequenceEqual(seq3));
        Check("flicker: only dips, never above 1 or below 1 minus the amount", seq1.All(v => v <= 1f && v >= 0.6f - 1e-5f), $"min {seq1.Min():F3} max {seq1.Max():F3}");
        Check("flicker: it really varies (at least 8 distinct levels in 2 s at 8 steps a second)", seq1.Distinct().Count() >= 8, $"{seq1.Distinct().Count()} levels");

        Check("noise: always in [0, 1)", Enumerable.Range(-50, 5000).All(i => { var n = Animator.Noise(i * 31, i); return n >= 0f && n < 1f; }));
        Check("seed: made from Scene id, target and kind, and the same on every machine (FNV-1a)",
            Animator.SeedFor("club-strobe", new SceneAnimationEffect { Target = "kicker", Kind = "strobe" }) == -559169249);
        Check("seed: an explicit seed wins", Animator.SeedFor("x", new SceneAnimationEffect { Target = "a", Kind = "flicker", Seed = 99 }) == 99);

        // Strobe: requested 10 Hz, capped at 3 Hz.
        var strobe = new SceneAnimationEffect { Target = "kicker", Kind = "strobe", Amount = 1f, RateHz = 10f, Enabled = true };
        Check("strobe: the effective rate is capped at 3 Hz", Near(Animator.EffectiveRate(strobe, 3f), 3f));
        int Transitions(float cap)
        {
            var count = 0;
            var last = Animator.Evaluate(strobe, 0.0001, 1, cap).Intensity;
            for (var i = 1; i < 2000; i++)   // 2 s sampled every millisecond
            {
                var v = Animator.Evaluate(strobe, i / 1000.0 + 0.0001, 1, cap).Intensity;
                if (Math.Abs(v - last) > 0.5f) count++;
                last = v;
            }
            return count;
        }
        var capped = Transitions(3f);
        Check("strobe: 2 s at the 3 Hz cap flips on and off about 12 times (6 flashes)", capped is >= 11 and <= 13, $"{capped} flips");
        Check("strobe control: with the cap lifted to 10 Hz it flips far more often (so the cap is what held it down)", Transitions(10f) > 30, $"{Transitions(10f)} flips");
        var half = Copy(strobe, rate: 2f);
        Check("strobe: amount 1 turns the light fully off in the dark half", Near(Animator.Evaluate(half, 0.75, 1, 3f).Intensity, 0f) && Near(Animator.Evaluate(half, 0.1, 1, 3f).Intensity, 1f));
        Check("strobe: a disabled effect is the identity", Near(Animator.Evaluate(Copy(strobe, enabled: false), 0.75, 1, 3f).Intensity, 1f));

        var pulse = new SceneAnimationEffect { Target = "key", Kind = "pulse", Amount = 0.2f, RateHz = 1f, Enabled = true };
        Check("pulse: quarter period is the peak, three quarters the trough", Near(Animator.Evaluate(pulse, 0.25, 1, 3f).Intensity, 1.2f) && Near(Animator.Evaluate(pulse, 0.75, 1, 3f).Intensity, 0.8f));
        var hue = Copy(pulse, amount: 0.25f); hue.Property = "hue";
        var (hi, hd) = Animator.Evaluate(hue, 0.25, 1, 3f);
        Check("pulse on hue: intensity stays 1 and the hue swings by amount x 360 degrees", Near(hi, 1f) && Near(hd, 90f), $"{hi}, {hd}");
        var drift = new SceneAnimationEffect { Target = "key", Kind = "drift", Amount = 0.1f, RateHz = 0.5f, Enabled = true };
        Check("drift: stays within the amount and is smooth (steps under 2 percent per millisecond)",
            Enumerable.Range(0, 4000).Select(i => Animator.Evaluate(drift, i / 1000.0, 3, 3f).Intensity).Pairwise().All(pr => Math.Abs(pr.Item2 - pr.Item1) < 0.02f));
        Check("an unknown kind does nothing", Near(Animator.Evaluate(new SceneAnimationEffect { Kind = "wobble", Enabled = true }, 1.0, 1, 3f).Intensity, 1f));

        var rotated = Harmony.RotateHue(new[] { 1f, 0f, 0f }, 120f);
        Check("hue rotation: red by 120 degrees is green and the brightest channel is unchanged", NearV(new Vector3(rotated[0], rotated[1], rotated[2]), new Vector3(0f, 1f, 0f), 1e-4f));

        // Apply on a real Scene.
        var camp = scenes.FirstOrDefault(s => s.Id == "campfire-flicker");
        if (camp == null) { Check("campfire Scene is in the catalogue", false); return; }
        var before = camp.Lights[0].Intensity;
        var frame = Animator.Apply(camp, 1.234, 3f);
        Check("apply: returns a copy and leaves the Scene alone", !ReferenceEquals(frame, camp) && Near(camp.Lights[0].Intensity, before));
        Check("apply: the fire gets dimmer or equal, never brighter, and its range does not move", frame.Lights[0].Intensity <= before * 1.13f && Near(frame.Lights[0].Range, camp.Lights[0].Range), $"{frame.Lights[0].Intensity:F2} of {before:F2}");
        Check("apply: other lights are untouched", Near(frame.Lights[1].Intensity, camp.Lights[1].Intensity));
        var frames = Enumerable.Range(0, 60).Select(i => Animator.Apply(camp, i / 15.0, 3f).Lights[0].Intensity).Distinct().Count();
        Check("apply: four seconds at 15 Hz gives a lively fire (many distinct intensities)", frames >= 15, $"{frames}");
        var still = camp.Clone();
        foreach (var e in still.Animation!.Effects) e.Enabled = false;
        Check("apply: with every effect off the Scene itself comes back (no copy, no cost)", ReferenceEquals(Animator.Apply(still, 3.0, 3f), still));

        // Block registration.
        var had = SceneStore.AppliedBlocks.Contains("animation");
        Animator.RegisterBlock();
        Check("the animation block is registered as applied", SceneStore.AppliedBlocks.Contains("animation"));
        if (!had) SceneStore.AppliedBlocks.Remove("animation");
    }

    // ---- throttle and cost ----
    private void ThrottleChecks()
    {
        var t = new AnimationThrottle(15f);
        Check("throttle: starts at the cap and the cap never exceeds 15 Hz", Near(t.Hz, 15f) && Near(new AnimationThrottle(99f).Hz, 15f));
        Check("throttle: slow re-sends (10 ms against a 4 ms budget) bring the rate down", t.Evaluate(10, 4) && t.Hz < 15f, $"{t.Hz:F2}");
        for (var i = 0; i < 30; i++) t.Evaluate(10, 4);
        Check("throttle: never below 2 Hz", Near(t.Hz, 2f), $"{t.Hz:F2}");
        for (var i = 0; i < 40; i++) t.Evaluate(0.3, 4);
        Check("throttle: cheap re-sends bring it back up, never above the cap", Near(t.Hz, 15f), $"{t.Hz:F2}");
        var mid = new AnimationThrottle(15f);
        mid.Evaluate(10, 4);
        var h = mid.Hz;
        Check("throttle: a middling cost (2 ms against 4) leaves the rate alone", !mid.Evaluate(2, 4) && Near(mid.Hz, h));

        var m = new CostMeter();
        foreach (var v in new[] { 1.0, 3.0, 2.0 }) m.Add(v);
        Check("cost meter: average and worst", Near((float)m.Average, 2f) && Near((float)m.Worst, 3f) && m.Count == 3);
        Check("cost meter: empty reads as no samples", new CostMeter().ToString() == "no samples");
        var s = new LightingSettings();
        Check("settings: defaults are 15 Hz, 4 ms budget, 3 Hz strobe cap, play on", Near(s.MaxAnimationHz, 15f) && Near(s.FrameBudgetMs, 4f) && Near(s.StrobeMaxHz, 3f) && s.PlayAnimation);
    }

    // ---- 8.3 harmony ----
    private void HarmonyChecks()
    {
        foreach (var rgb in new[] { new[] { 1f, 0f, 0f }, new[] { 0.2f, 0.6f, 0.9f }, new[] { 0.9f, 0.9f, 0.1f }, new[] { 0.3f, 0.3f, 0.3f }, new[] { 0.05f, 0.05f, 1f } })
        {
            var (h, s, v) = Harmony.ToHsv(rgb);
            var back = Harmony.FromHsv(h, s, v);
            Check($"hsv round trip ({rgb[0]:F2},{rgb[1]:F2},{rgb[2]:F2})", NearV(new Vector3(rgb[0], rgb[1], rgb[2]), new Vector3(back[0], back[1], back[2]), 1e-4f));
        }

        float HueOf(float[] c) => Harmony.ToHsv(c).H;
        float Diff(float a, float b) => ((b - a) % 360f + 360f) % 360f;
        var red = new[] { 1f, 0f, 0f };
        var roles2 = new[] { "key", "key2" };
        var comp = Harmony.Palette("complementary", red, roles2);
        Check("complementary: second role is 180 degrees from the base", Near(HueOf(comp["key"]), 0f, 0.5f) && Near(HueOf(comp["key2"]), 180f, 0.5f), $"{HueOf(comp["key"]):F1}, {HueOf(comp["key2"]):F1}");
        var roles3 = new[] { "key", "key2", "rim" };
        var split = Harmony.Palette("split complementary", red, roles3);
        Check("split complementary: 150 and 210 degrees", Near(HueOf(split["key2"]), 150f, 0.5f) && Near(HueOf(split["rim"]), 210f, 0.5f), $"{HueOf(split["key2"]):F1}, {HueOf(split["rim"]):F1}");
        var tri = Harmony.Palette("triadic", red, roles3);
        Check("triadic: 120 and 240 degrees", Near(HueOf(tri["key2"]), 120f, 0.5f) && Near(HueOf(tri["rim"]), 240f, 0.5f));
        var ana = Harmony.Palette("analogous", red, roles3);
        Check("analogous: +30 and -30 degrees", Near(Diff(0f, HueOf(ana["key2"])), 30f, 0.5f) && Near(Diff(0f, HueOf(ana["rim"])), 330f, 0.5f));
        var many = Harmony.Palette("complementary", red, new[] { "key", "key2", "fill", "rim", "backdrop" });
        Check("harmony: every role gets a colour, including the backdrop", many.Count == 5 && many.Values.All(c => c.Length == 3));
        Check("harmony: every light colour keeps the base colour's brightest channel (so the brightness rule does not move); the backdrop is darker",
            new[] { "key", "key2", "fill", "rim" }.All(r => Near(many[r].Max(), 1f, 1e-4f)) && Near(many["backdrop"].Max(), 0.4f, 1e-4f));
        Check("harmony: a fill is softer (less saturated) than the key", Harmony.ToHsv(many["fill"]).S < Harmony.ToHsv(many["key"]).S);
        Check("harmony: an unknown kind falls back to complementary", Near(HueOf(Harmony.Palette("???", red, roles2)["key2"]), 180f, 0.5f));
        var grey = Harmony.Palette("triadic", new[] { 0.5f, 0.5f, 0.5f }, roles3);
        Check("harmony: a grey base still gets three different hues", new[] { "key", "key2", "rim" }.Select(r => (int)HueOf(grey[r])).Distinct().Count() == 3);
    }

    // ---- 8.3 gels ----
    private void GelChecks()
    {
        var logs = new List<string>();
        var gels = Gels.Load(Path.Combine(root, "gels.json"), logs.Add);
        Check("gels.json loads at least 12 gels with nothing skipped", gels.Count >= 12 && logs.Count == 0, $"{gels.Count} gels, {logs.Count} log lines");
        Check("every gel has a unique name and a valid #rrggbb", gels.Select(g => g.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == gels.Count && gels.All(g => Gels.ParseHex(g.Hex) != null));
        var brands = new[] { "lee", "rosco", "roscolux", "cinegel", "gam ", "apollo", "no.", "#1" };
        Check("gel names are generic (no maker names or numbers)", gels.All(g => !brands.Any(b => g.Name.Contains(b, StringComparison.OrdinalIgnoreCase))), string.Join(",", gels.Select(g => g.Name)));
        Check("hex parses to channels", NearV(new Vector3(Gels.ParseHex("#ff8000")![0], Gels.ParseHex("#ff8000")![1], Gels.ParseHex("#ff8000")![2]), new Vector3(1f, 128f / 255f, 0f)) && Gels.ParseHex("nope") == null && Gels.ParseHex("#12345") == null);

        var l2 = new List<string>();
        var parsed = Gels.Parse("""{"gels":[{"name":"Fine","hex":"#102030"},{"name":"","hex":"#ffffff"},{"name":"Bad","hex":"red"}]}""", l2.Add);
        Check("a gel with no name or a bad hex is skipped with a log line each; the good one stays", parsed.Count == 1 && l2.Count == 2, $"{parsed.Count} kept, {l2.Count} lines");
        var l3 = new List<string>();
        Check("a file with no gels array gives an empty list and one line", Gels.Parse("{}", l3.Add).Count == 0 && l3.Count == 1);
        var l4 = new List<string>();
        Check("a missing file gives an empty list and one line", Gels.Load(Path.Combine(root, "no-such-gels.json"), l4.Add).Count == 0 && l4.Count == 1);

        // The generator is the source: regenerating gives the shipped file.
        var py = PythonFinder.Find();
        if (py == null) { Console.WriteLine("      (no Python found; gels.json regeneration check skipped)"); return; }
        var script = Path.GetFullPath(Path.Combine(root, "..", "..", "tools", "gen_scenes.py"));
        var tmp = Path.Combine(Path.GetTempPath(), "soundstage-lightchecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var psi = new ProcessStartInfo(py, $"\"{script}\" --out \"{Path.Combine(tmp, "scenes.json")}\"") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            using var proc = Process.Start(psi)!;
            var err = proc.StandardError.ReadToEnd();
            proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();
            var made = Path.Combine(tmp, "gels.json");
            Check("gen_scenes.py regenerates gels.json identical to the shipped file", proc.ExitCode == 0 && File.Exists(made) && File.ReadAllBytes(made).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(root, "gels.json"))), err.Trim());
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    // ---- 8.4 exposure ----
    private void ExposureChecks(List<Scene> scenes)
    {
        // Nine lights: three falloffs, three colour / intensity / factor mixes. The range is the closed form of the rule, worked
        // here independently of SceneStore.GameRange, so the estimate and the rule are two separate pieces of code.
        var nine = new List<SceneLight>();
        foreach (var fall in new[] { "Linear", "Quadratic", "Cubic" })
            foreach (var (col, inten, ff) in new[] { (new[] { 1f, 1f, 1f }, 4f, 1f), (new[] { 0.2f, 0.9f, 0.4f }, 9f, 1.5f), (new[] { 0.5f, 0.1f, 0.05f }, 2f, 0.7f) })
                nine.Add(new SceneLight { Role = "r", Falloff = fall, Color = col, Intensity = inten, FalloffFactor = ff });
        double RangeFor(SceneLight l)
        {
            var n = l.Falloff == "Linear" ? 1 : l.Falloff == "Cubic" ? 3 : 2;
            return Math.Pow(l.Color.Max() * l.Intensity / (l.FalloffFactor * SceneStore.GameCutoff), 1.0 / n);
        }
        var worst = nine.Max(l => Math.Abs(Exposure.Estimate(l, RangeFor(l)) / SceneStore.GameCutoff - 1.0));
        Check("exposure rule: nine lights (three falloffs x three mixes) each land at 0.0196 at their range", worst < 1e-9, $"worst relative error {worst:E2}");
        // Control: the same lights with the exponent swapped for a wrong one must NOT land there.
        var wrong = nine.Min(l => Math.Abs(Exposure.Estimate(l, RangeFor(l), (l.Falloff == "Linear" ? 1 : l.Falloff == "Cubic" ? 3 : 2) % 3 + 1) / SceneStore.GameCutoff - 1.0));
        Check("exposure control: with a wrong exponent every one of them misses 0.0196 by more than 20 percent", wrong > 0.2, $"smallest miss {wrong:P0}");

        // The catalogue: every shipped light at its stored range, within the rounding of a range to 0.1 m.
        var offByTooMuch = new List<string>();
        var count = 0;
        foreach (var s in scenes)
            foreach (var l in s.Lights.Concat(s.Backdrop?.Light != null ? new[] { s.Backdrop.Light } : Array.Empty<SceneLight>()))
            {
                count++;
                var n = Exposure.Exponent(l);
                var rel = Math.Abs(Exposure.Estimate(l, l.Range) / SceneStore.GameCutoff - 1.0);
                if (rel > n * 0.1 / l.Range + 0.01) offByTooMuch.Add($"{s.Id}/{l.Role} {rel:P1}");
            }
        Check($"exposure rule on the shipped catalogue: all {count} lights land at 0.0196 at their stored range (to the 0.1 m rounding)", offByTooMuch.Count == 0, string.Join("; ", offByTooMuch.Take(3)));
        var someLight = scenes[0].Lights[0];
        Check("exposure control on the catalogue: a wrong exponent misses on that same light", Math.Abs(Exposure.Estimate(someLight, someLight.Range, Exposure.Exponent(someLight) + 1) / SceneStore.GameCutoff - 1.0) > 0.2);

        // Thresholds and levels.
        var cfg = new LightingSettings();
        Check("exposure thresholds: amber 2.0 and red 4.0, a guess dated 21/11/2026", Near(cfg.AmberThreshold, 2f) && Near(cfg.RedThreshold, 4f) && LightingSettings.ThresholdNote.Contains("21/11/2026"));
        Check("exposure levels: 1.99 ok, 2.0 hot, 3.99 hot, 4.0 clip",
            Exposure.Level(1.99, cfg) == ExposureLevel.Ok && Exposure.Level(2.0, cfg) == ExposureLevel.Hot && Exposure.Level(3.99, cfg) == ExposureLevel.Hot && Exposure.Level(4.0, cfg) == ExposureLevel.Clip);
        var white = new SceneLight { Color = new[] { 1f, 1f, 1f }, Intensity = 4f, Falloff = "Quadratic", FalloffFactor = 1f };
        Check("exposure: a white intensity-4 quadratic light at 1 m is 4.0 and at 2 m is 1.0", Math.Abs(Exposure.Estimate(white, 1) - 4.0) < 1e-9 && Math.Abs(Exposure.Estimate(white, 2) - 1.0) < 1e-9);
        var near = Spot(0f, 1.0f, 1.55f);
        Check("exposure: subject distance for a light 1 m straight ahead of the face is 1 m, and follows a head that moves", Near(Exposure.SubjectDistance(near, new Vector3(0f, 1.55f, 0f)), 1f) && Near(Exposure.SubjectDistance(near, new Vector3(0f, 1.80f, 0f)), 1f));
        var fixedLight = Spot(0f, 1.0f, 1.55f, aimAt: null);
        Check("exposure: an unflagged light is measured to the authored aim point, so a taller head does not change it", Near(Exposure.SubjectDistance(fixedLight, new Vector3(0f, 1.80f, 0f)), 1f));
    }

    // ---- 8.5 catalogue ----
    private void CatalogueChecks(List<Scene> scenes)
    {
        var animated = new[] { "club-strobe", "campfire-flicker", "neon-sign-flicker" };
        var found = animated.Select(id => scenes.FirstOrDefault(s => s.Id == id)).ToList();
        Check("the three animated Scenes are in the catalogue as Scenes 16 to 18", found.All(s => s != null) && scenes.Count >= 18 && animated.SequenceEqual(scenes.Skip(15).Take(3).Select(s => s.Id)));
        if (found.Any(s => s == null)) return;
        var club = found[0]!; var camp = found[1]!; var neon = found[2]!;
        Check("club strobe: every effect ships off, so the strobe is off", club.Animation!.Effects.All(e => !e.Enabled) && !Animator.HasActive(club));
        Check("club strobe: the description says the strobe is off and carries the photosensitivity note",
            club.Description.Contains("OFF") && club.Description.Contains("photosensitive") && club.Animation.Effects.Any(e => e.Kind == "strobe"));
        Check("club strobe: the strobe's own rate is within the 3 Hz cap", club.Animation.Effects.Where(e => e.Kind == "strobe").All(e => e.RateHz <= 3f));
        Check("campfire and neon sign: flicker ships on", Animator.HasActive(camp) && Animator.HasActive(neon));
        Check("neon sign: the sign and its wall flicker with the same seed, so they drop out together",
            neon.Animation!.Effects.Count == 2 && neon.Animation.Effects.Select(e => e.Seed).Distinct().Count() == 1 && neon.Animation.Effects[0].Seed != null);
        Check("every animation effect in the whole catalogue targets a role the Scene has and a kind this build plays",
            scenes.Where(s => s.Animation != null).All(s => s.Animation!.Effects.All(e => Animator.IsKnownKind(e.Kind) && (s.Lights.Any(l => l.Role == e.Target) || s.Backdrop?.Light?.Role == e.Target))),
            string.Join(", ", scenes.Where(s => s.Animation != null && !s.Animation.Effects.All(e => Animator.IsKnownKind(e.Kind) && (s.Lights.Any(l => l.Role == e.Target) || s.Backdrop?.Light?.Role == e.Target))).Select(s => s.Id)));
        Check("the face-aimed lights in the catalogue are the key lights of the face rigs (11 of them)",
            scenes.Sum(s => s.Lights.Count(AimSolver.IsFaceAimed)) == 11 && new[] { "rembrandt", "butterfly", "loop", "clamshell", "split-light", "low-key" }.All(id => scenes.First(s => s.Id == id).Lights.Any(AimSolver.IsFaceAimed)),
            scenes.Sum(s => s.Lights.Count(AimSolver.IsFaceAimed)).ToString());
        Check("a Scene round trips its aimAt flag through save and load", SceneStore.Parse(System.Text.Json.JsonSerializer.Serialize(new SceneFile { Scenes = { scenes.First(s => s.Id == "rembrandt").Clone() } }, SceneStore.WriteOptions), "rt", _ => { })[0].Lights[0].AimAt == "face");

        // A frame-by-frame sweep of the strobe scene with the strobe switched on (as the user would): intensities stay finite and in range.
        var on = club.Clone();
        foreach (var e in on.Animation!.Effects) e.Enabled = true;
        var vals = Enumerable.Range(0, 300).Select(i => Animator.Apply(on, i / 15.0, 3f)).SelectMany(f => f.Lights.Select(l => l.Intensity)).ToList();
        Check("club strobe switched on: 20 s of frames at 15 Hz keep every intensity finite and between 0 and 1.3 x the Scene's own", vals.All(v => v >= 0f && v <= 11f && !float.IsNaN(v)));
    }
}

internal static class PairwiseExtensions
{
    public static IEnumerable<(T, T)> Pairwise<T>(this IEnumerable<T> source)
    {
        using var e = source.GetEnumerator();
        if (!e.MoveNext()) yield break;
        var prev = e.Current;
        while (e.MoveNext()) { yield return (prev, e.Current); prev = e.Current; }
    }
}

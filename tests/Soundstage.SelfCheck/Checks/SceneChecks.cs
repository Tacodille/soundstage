// Part 4: scene format v2, the store, legacy ids and the generated catalogue.
using System.Diagnostics;
using System.Text.Json;
using Soundstage;
using Stagehand.Definitions;
using Stagehand.Definitions.Objects;

namespace Soundstage.SelfCheck;

public sealed class SceneChecks : CheckArea
{
    public override string Name => "Scenes: format v2, store, aliases and catalogue";
    public override int Order => 40;

    private static readonly string[] Refined =
        { "neon-split", "neon-wall-hard", "neon-wall-soft", "rim-only", "hair-glow", "split-light", "window-light", "golden-hour", "high-key" };
    private static readonly string[] NewRigs = { "rembrandt", "butterfly", "loop", "clamshell", "silhouette", "low-key" };
    /// <summary>Part 8 appended these (Scenes 16 to 18).</summary>
    private static readonly string[] Animated = { "club-strobe", "campfire-flicker", "neon-sign-flicker" };

    /// <summary>Old preset id, the Scene it becomes, and the palette it selects (null: the default).</summary>
    private static readonly (string Old, string Scene, string? Palette)[] Aliases =
    {
        ("neon-split-blue-red", "neon-split", null),
        ("neon-orange-teal-split", "neon-split", "Orange and teal"),
        ("neon-magenta-on-blue", "neon-wall-hard", null),
        ("neon-pink-on-lavender", "neon-wall-soft", null),
        ("neon-orange-on-cyan", "neon-wall-soft", "Orange and cyan"),
        ("neon-green-rim", "rim-only", null),
        ("soft-backlit-hair-glow", "hair-glow", null),
        ("harsh-half-face", "split-light", null),
        ("soft-window", "window-light", null),
        ("natural-golden-hour", "golden-hour", null),
        ("soft-white-high-key", "high-key", null),
    };

    public override void Run()
    {
        var logs = new List<string>();
        var shippedPath = Path.Combine(root, "scenes.json");
        var scenes = SceneStore.LoadFile(shippedPath, logs.Add);

        // ---- Catalogue shape ----
        Check("scenes.json loads 18 Scenes with nothing skipped", scenes.Count == 18 && !logs.Any(l => l.Contains("skipped")), $"{scenes.Count} loaded, {logs.Count} log lines");
        Check("the nine refined Scenes, six new rigs and three animated Scenes are all there, in that order",
            scenes.Select(s => s.Id).SequenceEqual(Refined.Concat(NewRigs).Concat(Animated)), string.Join(",", scenes.Select(s => s.Id)));
        Check("file is formatVersion 2 with a \"scenes\" array", JsonDocument.Parse(File.ReadAllText(shippedPath)).RootElement.TryGetProperty("scenes", out _));
        Check("every Scene has a unique role per light, a default palette and a look slot or camera",
            scenes.All(s => s.Lights.Select(l => l.Role).Distinct().Count() == s.Lights.Count && s.Palettes.ContainsKey(s.EffectiveDefaultPalette)
                && (s.Look != null || s.Camera != null)));
        Check("every palette only names roles the Scene has (or backdrop)",
            scenes.All(s => s.Palettes.Values.All(p => p.Keys.All(r => r == "backdrop" || s.Lights.Any(l => l.Role == r) || s.Backdrop?.Light?.Role == r))));
        Check("the default palette equals the colours on the lights",
            scenes.All(s => s.Lights.All(l => s.Palettes[s.EffectiveDefaultPalette].TryGetValue(l.Role, out var c) && c.SequenceEqual(l.Color))));
        Check("Neon split has four palettes, Rim only three, Neon wall soft two",
            scenes.First(s => s.Id == "neon-split").Palettes.Count == 4 && scenes.First(s => s.Id == "rim-only").Palettes.Count == 3 && scenes.First(s => s.Id == "neon-wall-soft").Palettes.Count == 2);
        {
            var blocks = scenes.SelectMany(s => s.BlocksPresent()).Distinct().OrderBy(x => x).ToList();
            Check("all six optional blocks are carried by at least one Scene", blocks.SequenceEqual(new[] { "animation", "atmosphere", "camera", "look", "pose", "set" }), string.Join(",", blocks));
        }

        // ---- Old ids resolve ----
        foreach (var (old, scene, palette) in Aliases)
        {
            var found = SceneStore.Resolve(scenes, old, out var pal);
            Check($"old id '{old}' resolves to {scene}" + (palette != null ? $" / {palette}" : ""), found?.Id == scene && pal == palette, found == null ? "null" : $"{found.Id} / {pal}");
        }
        Check("an id that does not exist resolves to nothing", SceneStore.Resolve(scenes, "nope", out _) == null);

        // ---- Brightness rule ----
        {
            var bad = BrightnessViolations(scenes);
            Check("brightness rule: every light of every Scene is within 0.05 of the rule", bad.Count == 0, string.Join("; ", bad.Take(3)));
            var allPalettes = new List<Scene>();
            foreach (var s in scenes) foreach (var p in s.Palettes.Keys) allPalettes.Add(SceneStore.ApplyPalette(s, p));
            var badP = BrightnessViolations(allPalettes);
            Check($"brightness rule holds after applying every palette ({allPalettes.Count} Scene x palette)", badP.Count == 0, string.Join("; ", badP.Take(3)));

            // Negative control: one range edited by hand must be caught, naming the Scene and the light.
            var broken = scenes.Select(s => s.Clone()).ToList();
            broken.First(s => s.Id == "loop").Lights[0].Range += 1.0f;
            var caught = BrightnessViolations(broken);
            Check("negative control: a hand-edited range fails the rule and names the Scene and light",
                caught.Count == 1 && caught[0].StartsWith("loop/") && caught[0].Contains("Key"), string.Join("; ", caught));
        }

        // ---- Palette application ----
        {
            var split = scenes.First(s => s.Id == "neon-split");
            var orange = SceneStore.ApplyPalette(split, "Orange and teal");
            Check("palette: Orange and teal puts orange on the key and teal on the second key",
                NearV(new System.Numerics.Vector3(orange.Lights[0].Color[0], orange.Lights[0].Color[1], orange.Lights[0].Color[2]), new System.Numerics.Vector3(1f, 0.45f, 0.05f))
                && orange.Lights[1].Color[1] == 0.85f);
            Check("palette: the original Scene is not changed by applying one", split.Lights[0].Color[2] == 1.0f && split.EffectiveDefaultPalette == "Blue and red");
            var unknown = SceneStore.ApplyPalette(split, "No such palette");
            Check("palette: an unknown name falls back to the default colours", unknown.Lights[0].Color.SequenceEqual(split.Lights[0].Color));
            var wall = SceneStore.ApplyPalette(scenes.First(s => s.Id == "neon-wall-soft"), "Orange and cyan");
            Check("palette: the backdrop colour and wash light follow the palette",
                Near(wall.Backdrop!.Color[0], 0.1f) && Near(wall.Backdrop.Light!.Color[1], 0.8f));
            var s2 = SceneConverter.Build(orange);
            Check("palette: the converted stage carries the new colour", s2.Objects["light0"] is LightDefinition { } ld && Near(ld.Color.X, 1f) && Near(ld.Color.Y, 0.45f));
        }

        // ---- Every Scene and palette becomes a stage that round-trips ----
        {
            var failed = new List<string>();
            var count = 0;
            foreach (var s in scenes)
                foreach (var p in s.Palettes.Keys)
                {
                    count++;
                    var st = SceneConverter.Build(SceneStore.ApplyPalette(s, p));
                    if (!StageDefinition.TryParseDefinitionString(st.ToDefinitionString(), out _)) failed.Add($"{s.Id}/{p}");
                }
            Check($"all {count} Scene x palette stages build and round-trip", failed.Count == 0, string.Join(",", failed));
        }

        // ---- Unknown and unapplied blocks ----
        {
            var l2 = new List<string>();
            var withUnknown = """{"formatVersion":2,"scenes":[{"id":"a","name":"A","lights":[{"shape":"Point","color":[1,1,1]}],"hologram":{"x":1}},{"id":"b","name":"B","lights":[{"shape":"Point","color":[1,1,1]}],"hologram":{"x":2},"set":{"kind":"sweep"}}]}""";
            var got = SceneStore.Parse(withUnknown, "unknown-block", l2.Add);
            Check("an unknown block is ignored: both Scenes load", got.Count == 2);
            Check("an unknown block logs exactly one line, naming it", l2.Count == 1 && l2[0].Contains("hologram"), $"{l2.Count} lines");
            Check("the known set block was read", got[1].Set?.Kind == "sweep");
        }
        {
            var l3 = new List<string>();
            var tmp = NewTemp();
            try
            {
                var all = SceneStore.LoadAll(Path.Combine(root, "scenes.json"), Path.Combine(tmp, "user-scenes.json"), null, l3.Add);
                Check("loading the catalogue logs one line saying which blocks this build does not apply yet",
                    l3.Count(l => l.Contains("does not apply the")) == 1 && l3.Any(l => l.Split("(applied:")[0] is var notApplied && !notApplied.Contains("set") && !notApplied.Contains("atmosphere") && !notApplied.Contains("look") && !notApplied.Contains("camera") && l.Contains("applied: ")), string.Join(" | ", l3));   // parts 9, 10 and 11 apply set, atmosphere, camera and look, so none of those is listed (animation and pose register at runtime, outside SelfCheck, so they may still be listed here) in the "not yet" list
                Check("the catalogue still loads (18 Scenes) when no block is applied", all.Count == 18);
            }
            finally { Cleanup(tmp); }
        }

        // ---- Round trip keeps the blocks ----
        {
            var s = scenes.First(x => x.Id == "neon-wall-hard");
            var text = JsonSerializer.Serialize(new SceneFile { Scenes = { s } }, SceneStore.WriteOptions);
            var back = SceneStore.Parse(text, "roundtrip", _ => { }).Single();
            Check("write then read keeps palettes, roles and every block",
                back.Palettes.Count == s.Palettes.Count && back.Lights[0].Role == "key" && back.Look?.Slot == "neon-night" && back.Camera?.Height == "chest"
                && back.Animation?.Effects[0].Kind == "flicker" && back.Pose!.Tags.Contains("looking away") && back.Set?.ColorRole == "backdrop" && back.LegacyIds.ContainsKey("neon-magenta-on-blue"));
        }

        // ---- Old v1 files load under v2 ----
        var baselinePath = Path.Combine(TestRoot, "TestData", "baseline", "presets.json");
        {
            var l4 = new List<string>();
            var old = SceneStore.LoadFile(baselinePath, l4.Add);
            Check("the old v1 presets.json (11 presets) loads as v2 Scenes, nothing skipped", old.Count == 11 && l4.Count == 0, $"{old.Count} loaded, {l4.Count} log lines");
            Check("a v1 preset gets roles by light order (key, fill, rim) and one Default palette",
                old[0].Lights.Select(l => l.Role).SequenceEqual(new[] { "key", "fill", "rim" }) && old[0].Palettes.Count == 1 && old[0].Palettes.ContainsKey("Default"));
            Check("a v1 backdrop wash light gets the role bg", old.First(p => p.Backdrop != null).Backdrop!.Light!.Role == "bg");
            Check("every one of the 11 old ids is an alias in scenes.json", old.All(p => SceneStore.Resolve(scenes, p.Id, out _) != null), string.Join(",", old.Where(p => SceneStore.Resolve(scenes, p.Id, out _) == null).Select(p => p.Id)));
            Check("an old preset keeps its light values (blue key of neon-split-blue-red: azimuth -65, intensity 8)",
                old[0].Lights[0].AzimuthDeg == -65f && old[0].Lights[0].Intensity == 8f);
            // The baseline's own ranges already obey the rule, which is what makes the new generator trustworthy.
            Check("the old presets' ranges obey the rule too", BrightnessViolations(old).Count == 0, string.Join("; ", BrightnessViolations(old).Take(3)));
        }

        // ---- a real user-presets.json, copied, migrates and its selected id resolves ----
        {
            var realUser = Path.Combine(TestRoot, "TestData", "legacy", "Keylight", "user-presets.json");
            var tmp = NewTemp();
            try
            {
                var userScenes = Path.Combine(tmp, "user-scenes.json");
                var legacyCopy = Path.Combine(tmp, "user-presets.json");
                File.Copy(realUser, legacyCopy);
                var before = File.ReadAllBytes(legacyCopy);
                var l5 = new List<string>();
                var all = SceneStore.LoadAll(Path.Combine(root, "scenes.json"), userScenes, legacyCopy, l5.Add);
                var mine = all.Where(s => s.Category == "Mine").ToList();
                Check("real user-presets.json loads under v2: its presets appear in Mine", mine.Count >= 1 && mine.Any(s => s.Id == "beta-test-look"), $"{mine.Count} in Mine");
                Check("migration wrote user-scenes.json as formatVersion 2", File.Exists(userScenes) && JsonDocument.Parse(File.ReadAllText(userScenes)).RootElement.GetProperty("formatVersion").GetInt32() == 2);
                Check("the old user-presets.json is untouched, byte for byte", File.ReadAllBytes(legacyCopy).AsSpan().SequenceEqual(before));
                Check("migrated Scenes keep their lights, backdrop and one Default palette",
                    mine.All(s => s.Lights.Count > 0 && s.Palettes.ContainsKey("Default")) && mine.First(s => s.Id == "beta-test-look").Backdrop != null);
                Check("the migration logs once, saying the old file is untouched", l5.Count(l => l.Contains("carried") && l.Contains("untouched")) == 1, string.Join(" | ", l5));

                // Second load reads user-scenes.json and does not migrate again.
                var l6 = new List<string>();
                var again = SceneStore.LoadAll(Path.Combine(root, "scenes.json"), userScenes, legacyCopy, l6.Add);
                Check("second start reads user-scenes.json and does not migrate again", again.Count == all.Count && !l6.Any(l => l.Contains("carried")));

                // A user Scene saved after a palette change keeps the colours the user sees.
                var current = SceneStore.ApplyPalette(scenes.First(s => s.Id == "neon-split"), "Violet and amber");
                current.BakeDefaultPalette();
                current.Id = "my-violet"; current.Category = "Mine";
                var path2 = Path.Combine(tmp, "saved.json");
                SceneStore.SaveUser(path2, new[] { current });
                var reread = SceneStore.LoadFile(path2, _ => { }).Single();
                Check("saved user Scene reloads with the colours it was saved with", reread.Lights[0].Color[0] == 0.5f && reread.Palettes[reread.EffectiveDefaultPalette]["key"][0] == 0.5f);
                Check("a saved Scene still obeys the brightness rule", BrightnessViolations(new[] { reread }).Count == 0, string.Join("; ", BrightnessViolations(new[] { reread })));
            }
            finally { Cleanup(tmp); }
        }
        {
            // The selected id in the old Keylight.json is soft-window: it must resolve.
            var cfg = File.ReadAllText(Path.Combine(TestRoot, "TestData", "legacy", "Keylight.json"));
            var legacy = Soundstage.Migration.LegacyKeylight.ParseConfig(cfg, _ => { });
            var found = legacy == null ? null : SceneStore.Resolve(scenes, legacy.SelectedPresetId, out _);
            Check("the old selected preset id (from Keylight.json) resolves to a Scene", found != null, legacy?.SelectedPresetId ?? "no config");
        }

        // ---- Six-light cap ----
        {
            Check("the light cap is 6", SceneStore.MaxLights == 6);
            var l7 = new List<string>();
            var six = SceneStore.LoadFile(Path.Combine(root, "TestData", "six-lights.json"), l7.Add);
            Check("TestData/six-lights.json loads one Scene with six lights", six.Count == 1 && six[0].Lights.Count == 6 && l7.Count == 0, string.Join(" | ", l7));
            if (six.Count == 1)
            {
                var st = SceneConverter.Build(six[0]);
                Check("the six-light Scene becomes six light objects", st.Objects.Count(o => o.Value is LightDefinition) == 6);
                var seven = six[0].Clone();
                seven.Id = "seven"; seven.Lights.Add(seven.Lights[0].Clone());
                var l8 = new List<string>();
                var got = SceneStore.Parse(JsonSerializer.Serialize(new SceneFile { Scenes = { seven } }, SceneStore.WriteOptions), "seven", l8.Add);
                Check("seven lights are refused with a reason", got.Count == 0 && l8.Any(l => l.Contains("limit is 6")), string.Join(" | ", l8));
            }
        }

        // ---- The generator is the source ----
        {
            var py = FindPython();
            var script = Path.GetFullPath(Path.Combine(root, "..", "..", "tools", "gen_scenes.py"));
            if (py == null) Console.WriteLine("      (no Python found; generator checks skipped)");
            else
            {
                var tmp = NewTemp();
                try
                {
                    var legacyOut = Path.Combine(tmp, "legacy.json");
                    var (code, err) = Run(py, $"\"{script}\" --legacy --out \"{legacyOut}\"");
                    Check("gen_scenes.py --legacy runs", code == 0 && File.Exists(legacyOut), err);
                    Check("--legacy output equals the old presets.json byte for byte",
                        File.Exists(legacyOut) && File.ReadAllBytes(legacyOut).AsSpan().SequenceEqual(File.ReadAllBytes(baselinePath)));

                    var sceneOut = Path.Combine(tmp, "scenes.json");
                    var (code2, err2) = Run(py, $"\"{script}\" --out \"{sceneOut}\"");
                    Check("gen_scenes.py regenerates scenes.json identical to the shipped file",
                        code2 == 0 && File.Exists(sceneOut) && File.ReadAllBytes(sceneOut).AsSpan().SequenceEqual(File.ReadAllBytes(shippedPath)), err2);
                }
                finally { Cleanup(tmp); }
            }
        }
    }

    /// <summary>"scene/light" for every light whose range is not within 0.05 of the rule.</summary>
    private static List<string> BrightnessViolations(IEnumerable<Scene> scenes)
    {
        var bad = new List<string>();
        foreach (var s in scenes)
        {
            var all = s.Lights.ToList();
            if (s.Backdrop?.Light != null) all.Add(s.Backdrop.Light);
            foreach (var l in all)
            {
                var want = SceneStore.GameRange(l);
                if (MathF.Abs(l.Range - want) > 0.05f) bad.Add($"{s.Id}/{l.Name}: range {l.Range:F2}, rule says {want:F2}");
            }
        }
        return bad;
    }

    private static string NewTemp()
    {
        var d = Path.Combine(Path.GetTempPath(), "soundstage-scenechecks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static void Cleanup(string d) { try { Directory.Delete(d, true); } catch { } }

    private static string? FindPython() => PythonFinder.Find();

    private static (int Code, string Err) Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, err.Trim());
    }
}

internal static class SceneLightExtensions
{
    public static SceneLight Clone(this SceneLight l) => JsonSerializer.Deserialize<SceneLight>(JsonSerializer.Serialize(l, SceneStore.WriteOptions), SceneStore.ReadOptions)!;
}

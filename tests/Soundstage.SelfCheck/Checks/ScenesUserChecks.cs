// Part 7: snapshot round trip, undo, user Scenes, share codes (round trip, size, hostile input, key control), pose block.
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundstage;

namespace Soundstage.SelfCheck;

public sealed class ScenesUserChecks : CheckArea
{
    public override string Name => "Scenes tab: snapshot, undo, user Scenes, share codes, pose block";
    public override int Order => 70;

    private const string Sentinel = "SENTINEL-KEY-7f3a91c2-do-not-leak";

    private void Ok(string name, bool ok, string? detail = null) => Check(name, ok, detail ?? "");

    private static string Compact(object s) => JsonSerializer.Serialize(s, SceneStore.WriteOptions);

    public override void Run()
    {
        var shipped = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), _ => { });
        Ok("scenes.json loads (18 Scenes) for the part 7 checks", shipped.Count == 18, shipped.Count.ToString());
        Snapshots(shipped);
        Undo(shipped);
        UserFile(shipped);
        Codes(shipped);
        Hostile(shipped);
        KeyControl(shipped);
        PoseBlock();
    }

    // ---- 7.2 snapshot ----

    private void Snapshots(List<Scene> shipped)
    {
        var worst = new List<string>();
        var combos = 0;
        foreach (var scene in shipped)
        {
            foreach (var palette in scene.Palettes.Keys.Prepend((string)null!).Take(scene.Palettes.Count + 1))
            {
                var applied = SceneStore.ApplyPalette(scene, palette);
                foreach (var adjust in new[] { new BackdropAdjust(), new BackdropAdjust { ScaleX = 1.3f, ScaleY = 0.8f, DistanceOffset = 0.4f, HeightOffset = 0.1f, YawOffsetDeg = 90f } })
                {
                    combos++;
                    var stage = SceneConverter.Build(applied, adjust);
                    var back = Snapshot.FromStage(stage, applied, adjust);
                    var diff = Snapshot.Differences(applied, back);
                    if (diff.Count > 0) worst.Add($"{scene.Id}/{palette}: {string.Join("; ", diff)}");
                }
            }
        }
        Ok($"snapshot round trip: apply then snapshot gives the same Scene ({combos} Scene, palette and backdrop-adjust combinations)", worst.Count == 0, string.Join(" | ", worst.Take(3)));

        // Control: the comparer must actually see a 3 cm move and a 2 degree aim change, or the check above proves nothing.
        var a = SceneStore.ApplyPalette(shipped.First(s => s.Lights.Any(l => l.Shape == "Spot")), null);
        var b = a.Clone();
        b.Lights[0].Distance += 0.03f;
        Ok("control: a light moved 3 cm is reported as a difference", Snapshot.Differences(a, b).Count > 0);
        var c = a.Clone();
        var spot = c.Lights.First(l => l.Shape == "Spot");
        spot.AimHeight = (spot.AimHeight ?? spot.Height) + 0.5f;
        Ok("control: a spot re-aimed by half a metre is reported as a difference", Snapshot.Differences(a, c).Count > 0);

        // The inverse itself, over a sweep, against the plan's tolerance.
        var worstCm = 0f; var worstDeg = 0f;
        foreach (var az in new[] { -179f, -135f, -90f, -45f, -10f, 0f, 10f, 45f, 90f, 135f, 179f })
            foreach (var d in new[] { 0.6f, 1.5f, 3f })
                foreach (var h in new[] { 0.2f, 1.6f, 3f })
                {
                    var l = new SceneLight { AzimuthDeg = az, Distance = d, Height = h };
                    var p = SceneConverter.LightPosition(l);
                    var (iaz, idist, ih) = Snapshot.InvertLightPosition(p);
                    var back = SceneConverter.LightPosition(new SceneLight { AzimuthDeg = iaz, Distance = idist, Height = ih });
                    worstCm = MathF.Max(worstCm, Vector3.Distance(p, back) * 100f);
                    var dd = MathF.Abs(iaz - az) % 360f; worstDeg = MathF.Max(worstDeg, dd > 180f ? 360f - dd : dd);
                }
        Ok("LightPosition inverse is within 1 cm and 0.5 degrees over a sweep", worstCm <= 1f && worstDeg <= 0.5f, $"{worstCm:F4} cm, {worstDeg:F4} deg");

        // A tweak made after apply (what the sliders do) comes back from the placed stage.
        var tweaked = SceneStore.ApplyPalette(shipped[0], null);
        tweaked.Lights[0].AzimuthDeg = 33f; tweaked.Lights[0].Distance = 2.2f; tweaked.Lights[0].Intensity = 7f; tweaked.Lights[0].Color = new[] { 0.2f, 0.4f, 0.9f };
        var snap = Snapshot.FromStage(SceneConverter.Build(tweaked), shipped[0]);
        var l0 = snap.Lights[0];
        Ok("a slider tweak survives the snapshot (angle, distance, intensity, colour)",
            Near(l0.AzimuthDeg, 33f, 0.05f) && Near(l0.Distance, 2.2f, 0.005f) && Near(l0.Intensity, 7f) && l0.Color.SequenceEqual(new[] { 0.2f, 0.4f, 0.9f }));
        Ok("a snapshot keeps the basis Scene's roles, tags and blocks", snap.Lights.Select(l => l.Role).SequenceEqual(shipped[0].Lights.Select(l => l.Role)) && snap.Tags.SequenceEqual(shipped[0].Tags));
    }

    // ---- 7.3 undo ----

    private void Undo(List<Scene> shipped)
    {
        var stack = new UndoStack();
        var sA = shipped[0].Clone(); var sB = shipped[1].Clone(); var sC = shipped[2].Clone();
        stack.Push(sA, "", "a"); stack.Push(sB, "", "b"); stack.Push(sC, "", "c");
        var u1 = stack.Undo(sC); var u2 = stack.Undo(sB); var u3 = stack.Undo(sA);
        Ok("undo returns the states in reverse order (C, then B, then A) and nothing after the first",
            u1?.Scene.Id == sB.Id && u2?.Scene.Id == sA.Id && u3 == null, $"{u1?.Scene.Id}, {u2?.Scene.Id}, {u3?.Scene.Id}");

        var big = new UndoStack();
        for (var i = 0; i < 25; i++) { var s = shipped[i % shipped.Count].Clone(); s.Id = "s" + i; big.Push(s, "", "p"); }
        Ok("the stack keeps the last 20 states", big.Count == UndoStack.Capacity && big.States[0].Scene.Id == "s5" && big.Top!.Scene.Id == "s24", $"{big.Count}, first {big.States[0].Scene.Id}");

        // Tweaks: not recorded until still, then one step.
        var t = new UndoStack();
        var working = shipped[0].Clone();
        t.Push(working, "", "apply");
        var t0 = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        working.Lights[0].Intensity += 2f;
        var early = t.Observe(working, "", t0);
        var still = t.Observe(working, "", t0.AddSeconds(0.3));
        var settled = t.Observe(working, "", t0.AddSeconds(0.8));
        Ok("a slider tweak is recorded once it has been still for 0.6 s, not before", !early && !still && settled && t.Count == 2);
        working.Lights[0].Intensity += 1f;
        t.Observe(working, "", t0.AddSeconds(1));
        var revert = t.Undo(working);
        Ok("undo during an unrecorded tweak goes back to the last recorded state without dropping it", revert != null && t.Count == 2 && Near(revert.Scene.Lights[0].Intensity, shipped[0].Lights[0].Intensity + 2f));
        var back = t.Undo(revert!.Scene);
        Ok("the next undo then reverts the recorded tweak", back != null && Near(back.Scene.Lights[0].Intensity, shipped[0].Lights[0].Intensity) && t.Count == 1);
        Ok("undo with nothing recorded returns null and does not throw", new UndoStack().Undo(null) == null);
    }

    // ---- 7.4 user Scenes ----

    private void UserFile(List<Scene> shipped)
    {
        var dir = Path.Combine(Path.GetTempPath(), "soundstage-part7-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var logs = new List<string>();
            var mine = new UserScenes(Path.Combine(dir, "user-scenes.json"), logs.Add);
            var ids = shipped.Select(s => s.Id).ToList();
            var first = mine.Save(shipped[0], "My look!", ids, out var e1);
            var again = mine.Save(shipped[1], "my look!", ids, out _);
            var second = mine.Save(shipped[0], "Other look", ids, out _);
            var loaded = mine.Load();
            Ok("save puts a Scene in user-scenes.json as Mine with an id made from its name",
                first != null && e1 == null && first.Id == "my-look" && first.Category == "Mine" && loaded.Any(s => s.Id == "my-look"), first?.Id);
            Ok("saving under an existing name replaces it (one entry, new content)", again != null && loaded.Count(s => s.Id == "my-look") == 1 && loaded.First(s => s.Id == "my-look").Lights.Count == shipped[1].Lights.Count);
            Ok("a name that would clash with a shipped id gets a different id", UserScenes.UniqueId("Neon split", ids) != "neon-split" && UserScenes.UniqueId("Neon split", ids).StartsWith("neon-split"));
            Ok("a blank name is refused with a reason", mine.Save(shipped[0], "   ", ids, out var e2) == null && !string.IsNullOrEmpty(e2));
            var bad = shipped[0].Clone(); bad.Lights.Clear();
            Ok("a Scene with no lights is refused, not written", mine.Save(bad, "Broken", ids, out var e3) == null && !string.IsNullOrEmpty(e3) && mine.Load().All(s => s.Name != "Broken"));
            Ok("rename changes the name and keeps the id", mine.Rename("my-look", "Renamed", out _) && mine.Load().First(s => s.Id == "my-look").Name == "Renamed");
            Ok("delete removes it and leaves the others", mine.Delete("my-look", out _) && mine.Load().Count == 1 && second != null && mine.Load()[0].Id == second.Id);
            Ok("rename or delete of a Scene that is not there fails with a reason", !mine.Delete("nope", out var e4) && !string.IsNullOrEmpty(e4) && !mine.Rename("nope", "x", out _));
            Ok("no .tmp file is left beside the Scenes file", !Directory.GetFiles(dir, "*.tmp").Any());
        }
        finally { try { Directory.Delete(dir, true); } catch { /* temp only */ } }
    }

    // ---- 7.4 share codes ----

    private void Codes(List<Scene> shipped)
    {
        ShareCode.SecretValues = () => new[] { Sentinel };
        try
        {
            var fails = new List<string>();
            var longest = 0;
            foreach (var s in shipped)
            {
                var shown = SceneStore.ApplyPalette(s, null);
                shown.BakeDefaultPalette();
                if (!ShareCode.TryEncode(shown, out var code, out var err)) { fails.Add($"{s.Id}: {err}"); continue; }
                longest = Math.Max(longest, code.Length);
                var r = ShareCode.Decode(code);
                if (r.Scene == null) { fails.Add($"{s.Id}: {r.Error}"); continue; }
                var expect = shown.Clone();
                expect.Category = "Mine"; expect.LegacyIds = new();
                if (Compact(expect) != Compact(r.Scene)) fails.Add($"{s.Id}: differs after the trip");
            }
            Ok($"every shipped Scene round-trips through a share code unchanged (longest code {longest} characters)", fails.Count == 0, string.Join(" | ", fails.Take(3)));
            Ok("every code is within the 8 KB cap", longest <= ShareCode.MaxCodeChars);

            var one = shipped[0];
            ShareCode.TryEncode(one, out var c1, out _);
            Ok("a code starts with ss1: and uses only URL-safe characters", c1.StartsWith("ss1:") && c1[4..].All(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'));
            Ok("a code pasted with line breaks and spaces still reads", ShareCode.Decode("  " + c1.Insert(20, "\r\n ").Insert(40, "\n") + "\n").Scene != null);

            // Oversize: text that does not compress.
            var rng = new Random(7);
            var noise = new string(Enumerable.Range(0, 20000).Select(_ => (char)('a' + rng.Next(26))).ToArray());
            var huge = one.Clone(); huge.Description = noise;
            Ok("oversize: a Scene whose code would pass 8 KB is refused at encoding, with the numbers", !ShareCode.TryEncode(huge, out var hc, out var herr) && hc == "" && herr.Contains("limit"), herr);
            Ok("control: the same Scene with a short description encodes", ShareCode.TryEncode(one, out _, out _));
            var longCode = ShareCode.Prefix + new string('A', ShareCode.MaxCodeChars);
            var longRead = ShareCode.Decode(longCode);
            Ok("oversize: a pasted code over 8 KB is refused without being unpacked", longRead.Scene == null && !string.IsNullOrEmpty(longRead.Error));

            // Import: unknown blocks ignored, and noted.
            var node = JsonNode.Parse(Compact(one))!.AsObject();
            node["hologram"] = new JsonObject { ["intensity"] = 3 };
            var withUnknown = Craft(WrapScene(node));
            var ru = ShareCode.Decode(withUnknown);
            Ok("a Scene with a block this build does not know is read, and the block is ignored", ru.Scene != null && ru.Notes.Any(n => n.Contains("hologram")), ru.Error);
            Ok("describe lists what a Scene holds and what this build will not apply", ShareCode.Describe(one, new HashSet<string>()).Any(l => l.Contains("light")) && ShareCode.Describe(shipped.First(s => s.Look != null), new HashSet<string>()).Any(l => l.Contains("does not apply")));
        }
        finally { ShareCode.SecretValues = () => Array.Empty<string>(); }
    }

    private void Hostile(List<Scene> shipped)
    {
        string Raw(string text) => ShareCode.Prefix + ShareCode.ToBase64Url(Deflate(Encoding.UTF8.GetBytes(text)));
        var one = shipped[0];
        var lights1000 = JsonNode.Parse(Compact(one))!.AsObject();
        var arr = new JsonArray();
        for (var i = 0; i < 1000; i++) arr.Add(JsonNode.Parse(Compact(one.Lights[0]))!.AsObject().DeepClone());
        lights1000["lights"] = arr;
        var lightsCode = Craft(WrapScene(lights1000));

        var deep = new string('[', 500) + new string(']', 500);
        var nested = JsonNode.Parse(Compact(one))!.AsObject();
        var longName = JsonNode.Parse(Compact(one))!.AsObject(); longName["name"] = new string('x', 5000);
        var manyTags = JsonNode.Parse(Compact(one))!.AsObject(); var tg = new JsonArray(); for (var i = 0; i < 500; i++) tg.Add("t" + i); manyTags["tags"] = tg;
        var twoScenes = "{\"formatVersion\":2,\"scenes\":[" + Compact(shipped[0]) + "," + Compact(shipped[1]) + "]}";
        var bomb = ShareCode.Prefix + ShareCode.ToBase64Url(Deflate(new byte[2_000_000]));

        var cases = new (string Name, string? Code)[]
        {
            ("1,000 lights", lightsCode),
            ("bad Base64 (characters)", "ss1:!!!!not*base64!!!!"),
            ("bad Base64 (length)", "ss1:A"),
            ("Base64 that is not deflate", ShareCode.Prefix + ShareCode.ToBase64Url(Encoding.UTF8.GetBytes("this is just text, not deflate data"))),
            ("deflate of text that is not JSON", Raw("hello there")),
            ("deflate of invalid UTF-8", ShareCode.Prefix + ShareCode.ToBase64Url(Deflate(new byte[] { 0xFF, 0xFE, 0xC3, 0x28, 0x7B }))),
            ("bad JSON (truncated)", Raw("{\"formatVersion\":2,\"scenes\":[{\"id\":\"x\",")),
            ("JSON that is an array", Raw("[1,2,3]")),
            ("JSON with no scenes", Raw("{\"hello\":1}")),
            ("JSON nested 500 deep", Raw("{\"formatVersion\":2,\"scenes\":" + deep + "}")),
            ("a decompression bomb (2 MB of zeros in a small code)", bomb),
            ("a name of 5,000 characters", Craft(WrapScene(longName))),
            ("500 tags", Craft(WrapScene(manyTags))),
            ("two Scenes in one code", Raw(twoScenes)),
            ("wrong prefix", "ss2:" + ShareCode.ToBase64Url(Deflate(Encoding.UTF8.GetBytes("{}")))),
            ("empty", ""),
            ("whitespace only", "  \r\n "),
            ("null", null),
            ("just the prefix", "ss1:"),
        };
        var failures = new List<string>();
        foreach (var (name, code) in cases)
        {
            try
            {
                var r = ShareCode.Decode(code);
                if (r.Scene != null || string.IsNullOrWhiteSpace(r.Error)) failures.Add($"{name}: accepted or no reason");
            }
            catch (Exception ex) { failures.Add($"{name}: threw {ex.GetType().Name}"); }
        }
        Ok($"hostile codes are refused, each with a reason and none throwing ({cases.Length} cases incl. 1,000 lights and bad Base64)", failures.Count == 0, string.Join(" | ", failures));
        var lightsRead = ShareCode.Decode(lightsCode);
        Ok("the 1,000-light code is refused (it also unpacks past the 256 KB limit)", lightsRead.Scene == null && !string.IsNullOrEmpty(lightsRead.Error), lightsRead.Error);
        var forty = JsonNode.Parse(Compact(one))!.AsObject();
        var arr40 = new JsonArray();
        for (var i = 0; i < 40; i++) { var l = JsonNode.Parse(Compact(one.Lights[0]))!.AsObject(); l["role"] = "r" + i; arr40.Add(l); }
        forty["lights"] = arr40;
        var fortyRead = ShareCode.Decode(Craft(WrapScene(forty)));
        Ok("a 40-light code is small enough to unpack and is refused by the light cap", fortyRead.Scene == null && (fortyRead.Error ?? "").Contains("light"), fortyRead.Error);
        Ok("control: the same code with 3 lights reads", ShareCode.Decode(Craft(WrapScene(JsonNode.Parse(Compact(one))!.AsObject()))).Scene != null);
    }

    // ---- The key control ----

    private void KeyControl(List<Scene> shipped)
    {
        ShareCode.SecretValues = () => new[] { Sentinel };
        try
        {
            var one = shipped[0];
            // Control, as the plan asks: a Scene deliberately given a key field must be rejected by the strip step.
            var withKey = one.Clone();
            withKey.References = JsonSerializer.Deserialize<JsonElement>("{\"notes\":\"ok\",\"nested\":{\"apiKey\":\"" + Sentinel + "\"},\"list\":[{\"Authorization\":\"Bearer abc\"}]}");
            var stripped = ShareCode.Strip(withKey, out var sensitive);
            Ok("control: the strip step finds and removes key fields at any depth (apiKey, Authorization)", sensitive.Count == 2 && !Compact(stripped).Contains(Sentinel), string.Join(",", sensitive));
            Ok("control: a Scene with a key field is rejected at encoding, not shipped with the field cut out", !ShareCode.TryEncode(withKey, out var code0, out var err0) && code0 == "" && err0.Contains("key"), err0);
            var clean = one.Clone();
            clean.References = JsonSerializer.Deserialize<JsonElement>("{\"notes\":\"ok\",\"keyLight\":\"fine\",\"monkey\":1}");
            Ok("control: the same Scene without the key field encodes (names like keyLight are not secrets)", ShareCode.TryEncode(clean, out _, out _));

            // The sentinel in any other field is caught by value.
            var inText = one.Clone(); inText.Description = "see " + Sentinel;
            Ok("a stored key value found in any text field refuses the code", !ShareCode.TryEncode(inText, out _, out var err1) && err1.Contains("stored key"), err1);
            var shaped = one.Clone(); shaped.BestIn = "AIzaSyD-" + new string('x', 33);
            Ok("text shaped like a Google key refuses the code", !ShareCode.TryEncode(shaped, out _, out _));
            var skShaped = one.Clone(); skShaped.Name = "sk-or-v1-" + new string('a', 40);
            Ok("text shaped like an sk- key refuses the code", !ShareCode.TryEncode(skShaped, out _, out _));

            // A code from outside that carries a key: stripped or refused on import, never kept.
            var node = JsonNode.Parse(Compact(one))!.AsObject();
            node["references"] = JsonNode.Parse("{\"api_key\":\"" + Sentinel + "\",\"fine\":1}");
            node["apiKey"] = Sentinel;           // a field that is not in the schema at all
            var r = ShareCode.Decode(Craft(WrapScene(node)));
            Ok("an incoming code with key fields is read with them removed, and says so", r.Scene != null && !Compact(r.Scene).Contains(Sentinel) && r.Notes.Any(n => n.Contains("key")), r.Error);
            var node2 = JsonNode.Parse(Compact(one))!.AsObject();
            node2["description"] = "my key is " + Sentinel;
            var r2 = ShareCode.Decode(Craft(WrapScene(node2)));
            Ok("an incoming code that contains the stored key value is refused", r2.Scene == null && (r2.Error ?? "").Contains("stored key"), r2.Error);

            // Across every shipped Scene and every palette: the sentinel is in no serialised Scene or code.
            var leaks = 0;
            foreach (var s in shipped)
                foreach (var pal in s.Palettes.Keys)
                {
                    var shown = SceneStore.ApplyPalette(s, pal); shown.BakeDefaultPalette();
                    if (Compact(shown).Contains(Sentinel)) leaks++;
                    if (ShareCode.TryEncode(shown, out var code, out _) && ShareCode.FindSecret(Encoding.UTF8.GetString(Inflate(ShareCode.FromBase64Url(code[4..])))) != null) leaks++;
                }
            Ok("the sentinel key appears in no serialised Scene or share code (all shipped Scenes, all palettes)", leaks == 0, leaks.ToString());

            // The path of a pose file never travels.
            var withPath = one.Clone();
            withPath.Pose = new ScenePose { PoseFile = @"D:\Poses\mine.pose", PosePack = "Pack", PoseName = "mine" };
            ShareCode.TryEncode(withPath, out var pc, out _);
            var inflated = Encoding.UTF8.GetString(Inflate(ShareCode.FromBase64Url(pc[4..])));
            var pr = ShareCode.Decode(pc);
            Ok("a pose file path is not in the code; the pack and pose name are", !inflated.Contains("mine.pose") && pr.Scene?.Pose?.PoseFile == null && pr.Scene?.Pose?.PoseName == "mine" && pr.Scene.Pose.PosePack == "Pack");
        }
        finally { ShareCode.SecretValues = () => Array.Empty<string>(); }
    }

    // ---- 7.5 pose block ----

    private sealed class FakePort : IPosePort, IOutfitPort, IModsPort
    {
        public string PluginName { get; init; } = "Fake";
        public bool Available { get; init; }
        public bool Succeeds { get; init; } = true;
        public int Calls;
        public bool TryLoad(string? pack, string name, out string message) { Calls++; message = Succeeds ? "" : "the pose shelf has no pose"; return Succeeds; }
        public bool TryApplyDesign(string designName, out string message) { Calls++; message = Succeeds ? "" : "no design called that"; return Succeeds; }
        public bool TrySet(IReadOnlyList<PenumbraModChoice> mods, out string message) { Calls++; message = Succeeds ? "1 of 1 mod(s) set" : "no collection"; return Succeeds; }
    }

    private void PoseBlock()
    {
        var block = new ScenePose
        {
            PosePack = "Pack", PoseName = "sit", GlamourerDesign = "Evening",
            PenumbraMods = { new PenumbraModChoice { ModDirectory = "mod", ModName = "Mod", Options = { ["Colour"] = new List<string> { "Red" } } } },
        };
        var up = new FakePort { Available = true };
        var none = PoseBlockApplier.Apply(block, optedIn: false, up, up, up);
        Ok("without the tick, the pose block touches nothing and says why", up.Calls == 0 && none.Count == 1 && none[0].Contains("not ticked"));
        var all = PoseBlockApplier.Apply(block, true, up, up, up);
        Ok("with the tick and all three plugins present, each part is applied once", up.Calls == 3 && all.Count == 3 && all.All(l => !l.Contains("skipped")), string.Join(" | ", all));
        var down = new FakePort { Available = false, PluginName = "Glamourer" };
        var skipped = PoseBlockApplier.Apply(block, true, new FakePort { Available = false, PluginName = "Brio" }, down, new FakePort { Available = false, PluginName = "Penumbra" });
        Ok("missing plugins are named plainly, one line each, and nothing throws", skipped.Count == 3 && skipped.Any(l => l.Contains("Brio is not running")) && skipped.Any(l => l.Contains("Glamourer is not running")) && skipped.Any(l => l.Contains("Penumbra is not running")), string.Join(" | ", skipped));
        var nulls = PoseBlockApplier.Apply(block, true, null, null, null);
        Ok("a build with no ports at all still answers in plain words", nulls.Count == 3 && nulls.All(l => l.Contains("not running")));
        var failing = new FakePort { Available = true, Succeeds = false };
        var fl = PoseBlockApplier.Apply(block, true, failing, failing, failing);
        Ok("a missing pose, design or collection skips that part with its reason", fl.Count == 3 && fl.All(l => l.Contains("skipped")) && fl[0].Contains("no pose") && fl[1].Contains("no design"), string.Join(" | ", fl));
        Ok("control: a Scene with no pose block, or one with tags only, produces no lines even with the tick",
            PoseBlockApplier.Apply(null, true, up, up, up).Count == 0 && PoseBlockApplier.Apply(new ScenePose { Tags = { "sitting" } }, true, up, up, up).Count == 0);
        var pathOnly = PoseBlockApplier.Apply(new ScenePose { PoseFile = @"C:\x\a.pose" }, true, up, up, up);
        Ok("a pose named only by file path is skipped with a reason", pathOnly.Count == 1 && pathOnly[0].Contains("path"));
        Ok("this build lists the pose block as applied", new SceneDeskBlocks().Contains("pose"));
    }

    /// <summary>The pose block is registered in the plugin's SceneDesk constructor (needs Dalamud); here the same registration is read from the source.</summary>
    private sealed class SceneDeskBlocks
    {
        public bool Contains(string block) => File.ReadAllText(Path.Combine(root, "SceneDesk.cs")).Contains($"AppliedBlocks.Add(\"{block}\")");
    }

    // ---- helpers ----

    private static JsonObject WrapScene(JsonObject scene) => new() { ["formatVersion"] = 2, ["scenes"] = new JsonArray(scene) };

    private static string Craft(JsonObject root) => ShareCode.Prefix + ShareCode.ToBase64Url(Deflate(Encoding.UTF8.GetBytes(root.ToJsonString())));

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        using var z = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var o = new MemoryStream();
        z.CopyTo(o);
        return o.ToArray();
    }
}

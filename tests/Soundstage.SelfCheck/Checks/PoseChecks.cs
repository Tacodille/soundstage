// Moved unchanged from the prototype's single Program.cs (sections 9 to 12).
using System.Numerics;
using Soundstage;
using Soundstage.Poses;

namespace Soundstage.SelfCheck;

public sealed class PoseChecks : CheckArea
{
    public override string Name => "Poses";
    public override int Order => 20;

    public override void Run()
    {
        // ---- 9. Poses: folder scan on a temp tree with brackets, a heart and an apostrophe in the names ----
        {
            var tmp = Path.Combine(Path.GetTempPath(), "soundstage-poses-" + Guid.NewGuid().ToString("N"));
            try
            {
                var heart = Path.Combine(tmp, "[♡CB♡ ] Noctem");
                var crow = Path.Combine(tmp, "Crow's first pose pack");
                var nested = Path.Combine(tmp, "Lean Pack", "extra");
                foreach (var d in new[] { heart, crow, nested }) Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(heart, "Sit [soft] ♡.pose"), "{}");
                File.WriteAllText(Path.Combine(heart, "Stand.POSE"), "{}");
                File.WriteAllText(Path.Combine(crow, "Crow 1.pose"), "{}");
                File.WriteAllText(Path.Combine(tmp, "Root pose.pose"), "{}");
                File.WriteAllText(Path.Combine(nested, "Old style.cmp"), "{}");
                File.WriteAllText(Path.Combine(heart, "notes.txt"), "x");
                File.WriteAllText(Path.Combine(heart, "almost.poses"), "x");

                var slog = new List<string>();
                var found = PoseLibrary.Scan(new[] { tmp, Path.Combine(tmp, "does-not-exist"), tmp }, slog.Add, out var capped);
                Check("scan finds 5 files (4 .pose incl. upper-case, 1 .cmp), ignores .txt and .poses, lists a doubled folder once",
                    found.Count == 5 && found.Count(e => e.IsCmp) == 1 && !capped, $"{found.Count} found");
                Check("pack names: heart folder kept as a plain string, root files under the root's name, nested as a relative path",
                    found.Any(e => e.Pack == "[♡CB♡ ] Noctem" && e.Name == "Sit [soft] ♡")
                    && found.Any(e => e.Pack == "Crow's first pose pack")
                    && found.Any(e => e.Pack == Path.GetFileName(tmp) && e.Name == "Root pose")
                    && found.Any(e => e.Pack == "Lean Pack/extra" && e.IsCmp),
                    string.Join(" | ", found.Select(e => e.Pack + "/" + e.Name)));
                Check("a missing folder is logged and skipped", slog.Any(l => l.Contains("not found")));
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---- 10. Poses: body / expression split by Brio's own bone-prefix rules ----
        {
            const string synthetic = """
                {"TypeName":"Brio Pose","Bones":{"j_kosi":{},"j_ude_a_l":{},"j_kao":{},"j_f_eye_l":{},"j_f_ago":{},"j_kami_a":{},"n_throw":{},"j_f_ulip_a":{},"mystery_bone":{}},
                 "MainHand":{"n_buki_l":{}},"OffHand":{},"Prop":{"p":{}},"Ornament":{}}
                """;
            var body = PoseFilter.Reduce(synthetic, true, false, out var e1, out var k1, out var t1);
            var expr = PoseFilter.Reduce(synthetic, false, true, out var e2, out var k2, out var t2);
            var bodyBones = System.Text.Json.Nodes.JsonNode.Parse(body!)!["Bones"]!.AsObject().Select(kv => kv.Key).ToHashSet();
            var exprNode = System.Text.Json.Nodes.JsonNode.Parse(expr!)!;
            var exprBones = exprNode["Bones"]!.AsObject().Select(kv => kv.Key).ToHashSet();
            Check("body keeps j_kosi, j_ude_a_l and an unknown bone; drops head, eyes, jaw, hair, legacy lip and n_throw",
                bodyBones.SetEquals(new[] { "j_kosi", "j_ude_a_l", "mystery_bone" }), string.Join(",", bodyBones));
            Check("expression keeps j_kao, j_f_eye_l, j_f_ago, j_kami_a only",
                exprBones.SetEquals(new[] { "j_kao", "j_f_eye_l", "j_f_ago", "j_kami_a" }), string.Join(",", exprBones));
            Check("expression-only empties MainHand and Prop; body keeps Prop but drops MainHand",
                exprNode["MainHand"]!.AsObject().Count == 0 && exprNode["Prop"]!.AsObject().Count == 0
                && System.Text.Json.Nodes.JsonNode.Parse(body!)!["Prop"]!.AsObject().Count == 1
                && System.Text.Json.Nodes.JsonNode.Parse(body!)!["MainHand"]!.AsObject().Count == 0);
            var none = PoseFilter.Reduce("""{"Bones":{"j_kosi":{}}}""", false, true, out var e3, out _, out _);
            Check("expression-only on a file with no face bones returns nothing, with a reason", none == null && e3.Length > 0, e3);
            Check("neither part ticked returns nothing, with a reason", PoseFilter.Reduce(synthetic, false, false, out var e4, out _, out _) == null && e4.Length > 0);
        }

        // ---- 11. Poses: the real folder (read-only) ----
        {
            var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FFXIV Mods");
            if (Directory.Exists(real))
            {
                var rlog = new List<string>();
                var rf = PoseLibrary.Scan(new[] { real }, rlog.Add, out var rcap);
                var nPose = rf.Count(e => !e.IsCmp);
                Console.WriteLine($"      scan of '{real}': {rf.Count} files, {nPose} .pose, {rf.Count - nPose} .cmp, {rf.Select(e => e.Pack).Distinct().Count()} packs");
                // Counted independently rather than pinned: the folder grows whenever a pose is added (it read 67, then 73).
                var onDisk = Directory.EnumerateFiles(real, "*.pose", SearchOption.AllDirectories).Count();
                Check("real folder scan finds every .pose file on disk", nPose == onDisk && !rcap, $"{nPose} of {onDisk} .pose");

                // Every real pose splits cleanly: the parts are disjoint, and body + expression never exceed the file's bones.
                var split = 0; var readable = 0; var withImage = 0;
                foreach (var e in rf.Where(e => !e.IsCmp))
                {
                    var text = File.ReadAllText(e.Path);
                    var b = PoseFilter.Reduce(text, true, false, out _, out var kb, out var tot);
                    var x = PoseFilter.Reduce(text, false, true, out _, out var kx, out _);
                    if (tot > 0) readable++;
                    if (kb + kx <= tot) split++;
                    if (PoseLibrary.ExtractPreviewImage(e.Path) != null) withImage++;
                }
                Check("every real .pose splits into body and expression without overlap", readable == nPose && split == nPose, $"{split}/{nPose}");
                Console.WriteLine($"      real .pose files carrying an embedded preview image: {withImage} of {nPose}");
            }
            else Console.WriteLine("      (no FFXIV Mods folder in Documents, real-folder check skipped)");
        }

        // ---- 12. Poses: preview image extraction ----
        {
            var tmp = Path.Combine(Path.GetTempPath(), "soundstage-poseimg-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                // A 1x1 PNG.
                var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
                var withImg = Path.Combine(tmp, "with image.pose");
                var without = Path.Combine(tmp, "without.pose");
                File.WriteAllText(withImg, "{\"TypeName\":\"Brio Pose\",\"Bones\":{},\"Base64Image\":\"" + Convert.ToBase64String(png) + "\"}");
                File.WriteAllText(without, "{\"TypeName\":\"Brio Pose\",\"Bones\":{},\"Base64Image\":null}");
                var got = PoseLibrary.ExtractPreviewImage(withImg);
                Check("embedded Base64Image comes back as the exact image bytes", got != null && got.AsSpan().SequenceEqual(png), got == null ? "null" : $"{got.Length} bytes");
                Check("null Base64Image, a missing file and a .cmp give no image", PoseLibrary.ExtractPreviewImage(without) == null
                    && PoseLibrary.ExtractPreviewImage(Path.Combine(tmp, "nope.pose")) == null && PoseLibrary.ExtractPreviewImage(Path.Combine(tmp, "x.cmp")) == null);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---- 13. Poses: tag store (part 5) ----
        {
            var tmp = Path.Combine(Path.GetTempPath(), "soundstage-tags-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tmp);
                var a = Path.Combine(tmp, "Sit down.pose");
                var b = Path.Combine(tmp, "Wave.pose");
                File.WriteAllText(a, "{\"TypeName\":\"Brio Pose\",\"Bones\":{\"j_kosi\":{}}}");
                File.WriteAllText(b, "{\"TypeName\":\"Brio Pose\",\"Bones\":{\"j_ude_a_l\":{}}}");
                var storePath = Path.Combine(tmp, "cfg", PoseTags.FileName);

                var tags = new PoseTags(storePath);
                var ha = tags.HashFor(a)!;
                var hb = tags.HashFor(b)!;
                Check("hash is 64 lower-case hex digits and differs between two files", ha.Length == 64 && ha.All(Uri.IsHexDigit) && ha == ha.ToLowerInvariant() && ha != hb, ha[..8] + " vs " + hb[..8]);

                Check("tags are normalised: lower case, plain words, 24 characters at most",
                    PoseTags.Normalise("  Sitting  ") == "sitting"
                    && PoseTags.Normalise("Cute!! Pose?") == "cute pose"
                    && PoseTags.Normalise("a-b") == "a-b"
                    && PoseTags.Normalise(new string('x', 40))!.Length == 24
                    && PoseTags.Normalise("???") == null && PoseTags.Normalise("") == null && PoseTags.Normalise(null) == null);

                Check("add, duplicate refused, empty refused", tags.Add(ha, "Sitting") && !tags.Add(ha, "sitting") && !tags.Add(ha, "!!!") && tags.Add(ha, "cosy") && tags.Add(hb, "waving"));
                tags.Save();
                var again = PoseTags.Load(storePath);
                Check("round trip: saved tags come back from disk, sorted", again.TagsFor(ha).SequenceEqual(new[] { "cosy", "sitting" }) && again.TagsFor(hb).SequenceEqual(new[] { "waving" }) && again.TaggedCount == 2, string.Join(",", again.TagsFor(ha)));
                Check("saving leaves no .tmp file behind", !File.Exists(storePath + ".tmp"));

                // Negative control: the file is renamed and moved on disk; the tags follow the content hash.
                var moved = Path.Combine(tmp, "renamed", "Completely different name.pose");
                Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
                File.Move(a, moved);
                var fresh = PoseTags.Load(storePath);
                Check("rename keeps tags: the moved file hashes the same and finds its tags", fresh.HashFor(moved) == ha && fresh.TagsFor(fresh.HashFor(moved)).Contains("sitting"));
                File.WriteAllText(moved, "{\"TypeName\":\"Brio Pose\",\"Bones\":{\"j_kosi\":{\"x\":1}}}");
                Check("control: an edited file hashes differently and has no tags", fresh.HashFor(moved) != ha && fresh.TagsFor(fresh.HashFor(moved)).Count == 0);
                Check("an unreadable path hashes to null", fresh.HashFor(Path.Combine(tmp, "nope.pose")) == null);

                Check("filter: every word must match a tag start; empty matches all",
                    again.Matches(ha, "sit") && again.Matches(ha, "sit cosy") && !again.Matches(ha, "sit wav") && again.Matches(ha, "") && !again.Matches(null, "sit") && again.Matches(null, ""));
                Check("remove drops the tag, and the pose when none are left", again.Remove(ha, "cosy") && again.Remove(ha, "sitting") && again.TaggedCount == 1 && !again.Remove(ha, "sitting"));
                Check("AllTags counts poses per tag", again.AllTags().Count == 1 && again.AllTags()[0] is { Tag: "waving", Count: 1 });

                // A damaged file starts empty, says so, and keeps a .bad copy.
                File.WriteAllText(storePath, "{ not json");
                var bad = PoseTags.Load(storePath);
                Check("a damaged tag file starts empty with a note and a .bad copy", bad.TaggedCount == 0 && bad.LoadNote.Length > 0 && File.Exists(storePath + ".bad"));
                Check("a missing tag file is an empty store with no note", PoseTags.Load(Path.Combine(tmp, "none.json")) is { TaggedCount: 0, LoadNote: "" });
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // ---- 14. Poses: the checklist area (part 5) ----
        {
            var area = new Soundstage.Checklist.PosesSteps();
            Check("the poses checklist area is order 50, has unique step ids, and every step says what PASS or what to do",
                area.Order == 50 && area.Id == "poses" && area.Steps.Select(s => s.Id).Distinct().Count() == area.Steps.Count && area.Steps.All(s => s.Instructions.Length > 40),
                area.Steps.Count + " steps");
        }

    }
}

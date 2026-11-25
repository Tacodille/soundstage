// Part 18: the body map, the solver on a synthetic stick figure, the outline score, and the matching loop against a fake game.
// No game, no Python, no network. Real helper replies for two public-domain photos are recorded in TestData/body-recorded.json.
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Soundstage.Expression;
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed partial class BodyChecks : CheckArea
{
    public override string Name => "Body retarget (Part 18)";
    public override int Order => 180;

    public override void Run()
    {
        Map();
        Solver();
        Loop();
        Pack();
        Wiring();
    }

    private static string MapPath => Path.Combine(root, "Expression", "body-map.json");

    private BodyMap LoadMap()
    {
        var m = BodyMap.TryParse(File.ReadAllText(MapPath), out var problem);
        if (m == null) throw new InvalidOperationException("body-map.json did not parse: " + problem);
        return m;
    }

    // ---- 18.3 the map ----

    private void Map()
    {
        var json = File.ReadAllText(MapPath);
        var map = BodyMap.TryParse(json, out var problem);
        Check("map: the shipped body-map.json parses", map != null, problem);
        if (map == null) return;
        Check("map: 16 joints, both sides of arms and legs", map.Joints.Count == 16 && map.Joints.Count(j => j.Id.EndsWith("L")) == 7 && map.Joints.Count(j => j.Id.EndsWith("R")) == 7, $"{map.Joints.Count} joints");
        Check("map: every joint's bone is in the bone list with a parent entry", map.Joints.All(j => map.ParentOf(j.Bone) != null));
        Check("map: every parent is itself in the list or the root", map.Bones.All(b => b.Parent.Length == 0 || map.Bones.Any(x => x.Bone == b.Parent)));
        Check("map: depth runs root outwards (hand deeper than forearm deeper than upper arm)", map.DepthOf("j_te_l") > map.DepthOf("j_ude_b_l") && map.DepthOf("j_ude_b_l") > map.DepthOf("j_ude_a_l") && map.DepthOf("j_ude_a_l") > map.DepthOf("j_sako_l"));
        Check("map: every landmark name is a pose landmark", map.Joints.SelectMany(j => j.From.Concat(j.To)).All(n => BodyPoseResult.IndexOf(n) >= 0));
        Check("map: left bones follow left landmarks and right follow right", map.Joints.All(j =>
            !(j.Bone.EndsWith("_l")) || (j.From.Concat(j.To).All(n => !n.StartsWith("right_") || j.Id.StartsWith("clavicle") || j.Id == "spine" || j.Id == "neck")))
            && map.Joints.Where(j => j.Bone.EndsWith("_r")).All(j => j.From.Concat(j.To).All(n => !n.StartsWith("left_") || j.Id.StartsWith("clavicle"))));
        Check("map: limits are real (swing 0 to 180, twist 0 to 180)", map.Joints.All(j => j.SwingLimitDeg is > 0 and <= 180 && j.TwistLimitDeg is >= 0 and <= 180));
        Check("map: it is data, the code names no bone", !File.ReadAllText(Path.Combine(root, "Expression", "BodyRetarget.cs")).Contains("j_ude") && !File.ReadAllText(Path.Combine(root, "Expression", "BodyRetarget.cs")).Contains("j_asi"));
        Check("map: it is embedded in the plugin", File.ReadAllText(Path.Combine(root, "Soundstage.csproj")).Contains("body-map.json"));
        Check("map: every j_ bone name falls under one of Brio's own bone categories", map.Bones.Where(b => b.Bone.StartsWith("j_")).All(b =>
            BrioBoneCategories.Filters.Any(f => f.Prefixes.Any(p => b.Bone.StartsWith(p, StringComparison.Ordinal)))), "every j_ bone matches a category prefix");

        // Frame: a person facing the camera with an arm out to the picture's right (their left), then raised, in MediaPipe's frame.
        var f = map.Frame;
        Check("frame: picture right is the model's +x", NearV(f.ToModel(new Vector3(1, 0, 0)), new Vector3(1, 0, 0)));
        Check("frame: picture down is the model's -y (up flips)", NearV(f.ToModel(new Vector3(0, 1, 0)), new Vector3(0, -1, 0)));
        Check("frame: away from the camera is the model's -z (the character faces the camera)", NearV(f.ToModel(new Vector3(0, 0, 1)), new Vector3(0, 0, -1)));
        Check("frame: a proper rotation, not a mirror", Math.Abs(Matrix3Det(f) - 1f) < 1e-4f, $"det {Matrix3Det(f):0.###}");

        // Bad maps are refused (controls for the parser).
        Check("map (control): an unknown landmark is refused", BodyMap.TryParse(json.Replace("\"left_elbow\"", "\"left_elbo\""), out _) == null);
        Check("map (control): a swing limit of 0 is refused", BodyMap.TryParse(json.Replace("\"swingLimitDeg\": 175", "\"swingLimitDeg\": 0"), out _) == null);
        Check("map (control): a bone not in the bone list is refused", BodyMap.TryParse(json.Replace("\"bone\": \"j_te_l\", \"child\"", "\"bone\": \"j_te_x\", \"child\""), out _) == null);
        Check("map (control): not JSON is refused", BodyMap.TryParse("{nope", out _) == null);
    }

    private static float Matrix3Det(BodyFrame f)
    {
        var a = f.X; var b = f.Y; var c = f.Z;
        return a[0] * (b[1] * c[2] - b[2] * c[1]) - a[1] * (b[0] * c[2] - b[2] * c[0]) + a[2] * (b[0] * c[1] - b[1] * c[0]);
    }

    // ---- the checklist, the tab slot and the docs ----

    private void Wiring()
    {
        var area = new Soundstage.Checklist.BodySteps();
        Check("checklist: the body area is order 180 (part 18)", area.Order == 180 && area.Id == "body");
        Check("checklist: step ids are unique", area.Steps.Select(x => x.Id).Distinct().Count() == area.Steps.Count);
        Check("checklist: every step says what PASS looks like", area.Steps.All(x => x.Instructions.Contains("PASS")));
        var all = string.Join(" ", area.Steps.Select(x => x.Instructions + x.Title));
        Check("checklist: it covers the download, one helper, the map check, a photo, a match, limits, the face left alone, undo, five ratings and the alt",
            new[] { "Download the body model", "One helper", "Check the body map", "full-length photo", "Match one photo", "Limits hold", "untouched", "Undo and cancel", "Five photos", "Midlander" }.All(k => all.Contains(k, StringComparison.OrdinalIgnoreCase)));
        Check("checklist: it names the log lines and the rating file the plan lists", all.Contains("[Soundstage/BODY] rest") && all.Contains("docs/expression-trials.md"));

        var tab = File.ReadAllText(Path.Combine(root, "Tabs", "ExpressionTab.cs"));
        Check("tab: the empty slot from part 17 now hands over to BodySection", tab.Contains("body.Draw(studio, face!"));
        Check("tab: a low face rating does not hide the body section", System.Text.RegularExpressions.Regex.IsMatch(tab, @"DrawBodySection\(studio\);[^\n]*\n\s*return;"));
        var bodyTab = File.ReadAllText(Path.Combine(root, "Tabs", "BodySection.cs"));
        Check("tab: the body section uses the shared helper it is given, not one of its own", !bodyTab.Contains("new FaceServices") && bodyTab.Contains("face.Analyser.AnalysePose"));
        Check("tab: the screenshots are removed after a run", bodyTab.Contains("rig!.Clean()"));
        var loop = File.ReadAllText(Path.Combine(root, "Expression", "BodyMatch.cs"));
        Check("loop: the only pose it loads is the body-only copy (BodyOnly) or the saved body-only original", loop.Contains("BodyRetarget.BodyOnly(solved.Json") && loop.Contains("rig.Load(originalBody") && !loop.Contains("rig.Load(solved"));

        var trials = File.ReadAllText(Path.Combine(root, "..", "..", "docs", "expression-trials.md"));
        Check("docs: the body trial table is in expression-trials.md with the rule", trials.Contains("Body pose trials") && trials.Contains("median of the five ratings is under 3"));
        var notes = File.ReadAllText(Path.Combine(root, "..", "..", "docs", "body-retarget-notes.md"));
        Check("docs: the pose source decision records its numbers and says what was not measured", notes.Contains("Choice: local") && notes.Contains("not measured"));
        var model = File.ReadAllText(Path.Combine(root, "..", "..", "docs", "face-model-decision.md"));
        var pack = PosePackManifest.TryParse(File.ReadAllText(Path.Combine(root, "Face", "pose-pack.json")), out var problem);
        Check("pose pack: the pinned list parses", pack != null, problem);
        Check("pose pack: name, version, URL, licence, size and hash are all in face-model-decision.md", pack != null && model.Contains(pack.Name) && model.Contains(pack.Version) && model.Contains(pack.Url) && model.Contains(pack.Sha256) && model.Contains(pack.Size.ToString("N0")) && model.Contains("Apache 2.0"));
        Check("pose pack (control): a pack from another host is refused", PosePackManifest.TryParse(File.ReadAllText(Path.Combine(root, "Face", "pose-pack.json")).Replace("storage.googleapis.com", "example.com"), out _) == null);
        Check("pose pack (control): a pack with a short hash is refused", PosePackManifest.TryParse(File.ReadAllText(Path.Combine(root, "Face", "pose-pack.json")).Replace("5134a3aad27a", "5134a3aad2"), out _) == null);
    }
}

using System.Numerics;
using System.Text.Json;
using Soundstage.Checklist;
using Soundstage.Look;

namespace Soundstage.SelfCheck;

/// <summary>
/// Part 11: the Look service against a fake add-on. The pipe protocol (v2 commands, version handshake, mismatch and timeout),
/// slot mapping, restore-on-removal, an unmapped slot, a malformed reply, and the auto focus rules. The game and ReShade
/// themselves are checklist steps (area "look").
/// </summary>
public sealed class LookChecks : CheckArea
{
    public override string Name => "Look: ReShade per Scene, auto focus";
    public override int Order => 110;

    public override void Run()
    {
        Protocol();
        PipeVersioning();
        SlotsAndIni();
        Service();
        Focus();
        Misc();
    }

    // ---- Protocol (pure) ----

    private void Protocol()
    {
        Check("v2 commands are single lines in the documented shape",
            ReShadeProtocol.Focus("ManualFocusPlane", 2.5f) == "focus ManualFocusPlane 2.5" && ReShadeProtocol.Restore() == "restore" && ReShadeProtocol.Unload() == "unload");
        Check("focus refuses a space in the name, a negative, NaN and infinity before anything is sent",
            Throws(() => ReShadeProtocol.Focus("a b", 1f)) && Throws(() => ReShadeProtocol.Focus("a", -1f)) &&
            Throws(() => ReShadeProtocol.Focus("a", float.NaN)) && Throws(() => ReShadeProtocol.Focus("a", float.PositiveInfinity)) && Throws(() => ReShadeProtocol.Focus("", 1f)));
        Check("add-on version parses from a ping reply",
            ReShadeProtocol.ParseAddonVersion(new ReShadeReply(true, "soundstage-addon 2")) == 2 && ReShadeProtocol.ParseAddonVersion(new ReShadeReply(true, "soundstage-addon 1")) == 1);
        Check("control: a failure, a different OK reply and a zero or non-number version are not a version",
            ReShadeProtocol.ParseAddonVersion(ReShadeReply.Fail("soundstage-addon 2")) == null && ReShadeProtocol.ParseAddonVersion(new ReShadeReply(true, "hello")) == null &&
            ReShadeProtocol.ParseAddonVersion(new ReShadeReply(true, "soundstage-addon 0")) == null && ReShadeProtocol.ParseAddonVersion(new ReShadeReply(true, "soundstage-addon x")) == null);
        Check("version 1 is too old for focus, restore and unload but not for preset, uniform or shot",
            ReShadeProtocol.VersionMismatch("focus", 1) != null && ReShadeProtocol.VersionMismatch("restore", 1) != null && ReShadeProtocol.VersionMismatch("unload", 1) != null &&
            ReShadeProtocol.VersionMismatch("preset", 1) == null && ReShadeProtocol.VersionMismatch("uniform", 1) == null && ReShadeProtocol.VersionMismatch("shot", 1) == null);
        Check("control: version 2 and an unknown version are not a mismatch", ReShadeProtocol.VersionMismatch("focus", 2) == null && ReShadeProtocol.VersionMismatch("focus", null) == null);
        var old = ReShadeProtocol.ExplainUnknownCommand("focus x 1", ReShadeReply.Fail("unknown command: focus"));
        Check("an older add-on's 'unknown command' becomes the version-mismatch sentence", !old.Ok && old.Text.Contains("version") && old.Text.Contains("soundstage.addon64"), old.Text);
        Check("control: other errors and successes pass through unchanged",
            ReShadeProtocol.ExplainUnknownCommand("preset a.ini", ReShadeReply.Fail("preset file not found: a.ini")).Text == "preset file not found: a.ini" &&
            ReShadeProtocol.ExplainUnknownCommand("ping", new ReShadeReply(true, "x")).Ok);
    }

    // ---- ReShadePipe with a fake link ----

    private void PipeVersioning()
    {
        var fake = new FakeAddon(version: 1);
        var pipe = new ReShadePipe(fake);
        var r = pipe.SendAsync(ReShadeProtocol.Focus("f", 1f)).GetAwaiter().GetResult();
        Check("a v1 add-on: focus is refused with the mismatch sentence and never reaches the pipe (only the ping did)",
            !r.Ok && r.Text.Contains("version 1") && fake.Sent.SequenceEqual(new[] { "ping" }), $"{r.Text} / sent: {string.Join(",", fake.Sent)}");
        var p = pipe.SendAsync(ReShadeProtocol.Preset(@".\a.ini")).GetAwaiter().GetResult();
        Check("control: a v1 add-on still takes a preset command", p.Ok && fake.Current == @".\a.ini", p.Text);

        var fake2 = new FakeAddon(version: 2);
        var pipe2 = new ReShadePipe(fake2);
        var f = pipe2.SendAsync(ReShadeProtocol.Focus("ManualFocusPlane", 3f)).GetAwaiter().GetResult();
        Check("a v2 add-on takes focus and restore", f.Ok && fake2.Values.GetValueOrDefault("ManualFocusPlane") == 3f && pipe2.SendAsync(ReShadeProtocol.Restore()).GetAwaiter().GetResult().Ok, f.Text);

        // A timeout is a plain failure and changes nothing the pipe believed.
        var silent = new FakeAddon(version: 2) { Silent = true };
        var pipe3 = new ReShadePipe(silent);
        var t = pipe3.SendAsync(ReShadeProtocol.Preset(@".\a.ini")).GetAwaiter().GetResult();
        Check("timeout: no reply gives a failure sentence, no version is believed", !t.Ok && pipe3.AddonVersion == null && silent.Current == "start.ini", t.Text);

        // Malformed reply: state must not change.
        var pipe4 = new ReShadePipe(fake2);
        pipe4.PingAsync().GetAwaiter().GetResult();
        fake2.Malformed = true;
        var m = pipe4.PingAsync().GetAwaiter().GetResult();
        Check("control: a malformed reply to a ping is a failure and leaves the known add-on version alone", !m.Ok && pipe4.AddonVersion == 2, m.Text);
    }

    // ---- Slots, ReShade.ini ----

    private void SlotsAndIni()
    {
        var ini = ReShadeIni.Parse("[GENERAL]\r\nPresetPath=.\\reshade-presets\\GShade\\My Look.ini\r\n[OVERLAY]\r\nAutoSavePreset=1\r\n");
        Check("ReShade.ini: preset path, auto-save and the preset folder (the top folder under the game folder)",
            ini.PresetPath == @".\reshade-presets\GShade\My Look.ini" && ini.AutoSavePreset && ini.PresetRoot() == @".\reshade-presets", ini.PresetRoot());
        var none = ReShadeIni.Parse("[GENERAL]\nStartupPresetPath=\n[OVERLAY]\nAutoSavePreset=0\n");
        Check("control: missing PresetPath is unknown (null), AutoSavePreset=0 is false", none.PresetPath == null && none.PresetRoot() == null && !none.AutoSavePreset);
        Check("an absolute PresetPath gives its own folder, and one straight in the game folder gives '.'",
            ReShadeIni.Parse("[GENERAL]\nPresetPath=D:\\presets\\x\\a.ini\n").PresetRoot() == @"D:\presets\x" && ReShadeIni.Parse("[GENERAL]\nPresetPath=.\\a.ini\n").PresetRoot() == ".");

        var s = new LookSettings();
        Slots.Map(s, "neon-night", @".\reshade-presets\A\n.ini");
        Check("a slot resolves to its mapped preset, case-insensitively, and an unmapped or empty one to null",
            Slots.Resolve("Neon-Night", s) == @".\reshade-presets\A\n.ini" && Slots.Resolve("dark-moody", s) == null && Slots.Resolve("", s) == null && Slots.Resolve(null, s) == null);
        Slots.Map(s, "neon-night", null);
        Check("mapping to nothing clears the slot", Slots.Resolve("neon-night", s) == null && s.Mapping.Count == 0);
        Check("Slots.All lists the five shipped slots and any other slot a Scene carries, once each",
            Slots.All(new[] { new Scene { Look = new SceneLook { Slot = "cool-teal" } }, new Scene { Look = new SceneLook { Slot = "NEON-NIGHT" } } }).SequenceEqual(Slots.Known.Append("cool-teal")));

        var tmp = Path.Combine(Path.GetTempPath(), "soundstage-look-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "reshade-presets", "Pack"));
            File.WriteAllText(Path.Combine(tmp, "reshade-presets", "Pack", "b.ini"), "x");
            File.WriteAllText(Path.Combine(tmp, "reshade-presets", "a.ini"), "x");
            File.WriteAllText(Path.Combine(tmp, "reshade-presets", "notes.txt"), "x");
            var list = Slots.ListPresets(tmp, @".\reshade-presets");
            Check("the preset folder lists .ini files only, as paths relative to the game folder", list.SequenceEqual(new[] { @".\reshade-presets\a.ini", @".\reshade-presets\Pack\b.ini" }), string.Join(",", list));
            Check("a relative path round-trips through ToFull and ToRelative; a missing file does not exist",
                Slots.ToRelative(tmp, Slots.ToFull(tmp, @".\reshade-presets\a.ini")) == @".\reshade-presets\a.ini" && Slots.PresetExists(tmp, @".\reshade-presets\a.ini") && !Slots.PresetExists(tmp, @".\reshade-presets\nope.ini"));
            Check("control: a missing folder lists nothing instead of throwing", Slots.ListPresets(tmp, @".\nowhere").Count == 0);
        }
        finally { try { Directory.Delete(tmp, true); } catch { /* best effort */ } }
    }

    // ---- LookService with a fake add-on ----

    private void Service()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "soundstage-look-svc-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(tmp, "reshade-presets"));
            foreach (var n in new[] { "neon", "moody", "mine" }) File.WriteAllText(Path.Combine(tmp, "reshade-presets", n + ".ini"), "x");
            const string mine = @".\reshade-presets\mine.ini";

            LookService Make(FakeAddon addon, LookSettings settings, List<string> log) =>
                new(new ReShadePipe(addon), tmp, () => settings, _ => { }, log.Add);
            Scene SceneWith(string slot, bool? focus = null) => new() { Id = slot, Look = new SceneLook { Slot = slot, AutoFocus = focus } };

            // Apply and restore.
            var addon = new FakeAddon(version: 2) { Current = mine };
            var settings = new LookSettings();
            Slots.Map(settings, "neon-night", @".\reshade-presets\neon.ini");
            Slots.Map(settings, "dark-moody", @".\reshade-presets\moody.ini");
            var log = new List<string>();
            var look = Make(addon, settings, log);
            look.ProbeAsync().GetAwaiter().GetResult();
            Check("route: the add-on answered, so the route is the pipe", look.Route == LookRoute.Pipe, look.RouteReason);

            look.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            Check("a Scene's look slot switches ReShade to the mapped preset and remembers the user's own", addon.Current == @".\reshade-presets\neon.ini" && settings.PreviousPreset == mine && look.StatusOk, look.Status);
            look.ApplyAsync(SceneWith("dark-moody")).GetAwaiter().GetResult();
            Check("a second Scene switches again but keeps the user's first preset as the one to restore", addon.Current == @".\reshade-presets\moody.ini" && settings.PreviousPreset == mine);
            addon.Values["Focus"] = 9f;   // pretend a uniform changed since
            look.RemoveAsync().GetAwaiter().GetResult();
            Check("removing the Scene restores the user's preset and clears the record", addon.Current == mine && settings.PreviousPreset == "" && look.StatusOk, look.Status);
            Check("the restore command goes before the preset switch back", IndexOf(addon.Sent, "restore") is int ri and >= 0 && ri < addon.Sent.LastIndexOf("preset " + mine), string.Join(" | ", addon.Sent));
            Check("log lines carry the [Soundstage/LOOK] prefix", log.Count > 0 && log.All(l => l.StartsWith("[Soundstage/LOOK]")), string.Join(" | ", log.Take(3)));

            // Unmapped slot is harmless, with a control that a mapped slot does send.
            var addon2 = new FakeAddon(version: 2) { Current = mine };
            var s2 = new LookSettings();
            var look2 = Make(addon2, s2, new());
            look2.ApplyAsync(SceneWith("soft-bloom")).GetAwaiter().GetResult();
            Check("an unmapped slot sends nothing, changes nothing and says so", addon2.Sent.Count == 0 && addon2.Current == mine && !look2.StatusOk && look2.Status.Contains("not mapped") && s2.PreviousPreset == "", look2.Status);
            Slots.Map(s2, "soft-bloom", @".\reshade-presets\neon.ini");
            look2.ApplyAsync(SceneWith("soft-bloom")).GetAwaiter().GetResult();
            Check("control: once mapped the same Scene does switch", addon2.Current == @".\reshade-presets\neon.ini");

            // Another machine's Scene: a slot this user has never heard of.
            look2.RemoveAsync().GetAwaiter().GetResult();
            look2.ApplyAsync(SceneWith("a-slot-from-another-machine")).GetAwaiter().GetResult();
            Check("a Scene from another machine with an unknown slot is harmless", addon2.Current == mine && s2.PreviousPreset == "");

            // A Scene with no look takes the previous look off.
            look2.ApplyAsync(SceneWith("soft-bloom")).GetAwaiter().GetResult();
            look2.ApplyAsync(new Scene { Id = "plain" }).GetAwaiter().GetResult();
            Check("a Scene with no look block puts the user's preset back", addon2.Current == mine && s2.PreviousPreset == "");

            // A mapped preset whose file is gone.
            var addon3 = new FakeAddon(version: 2) { Current = mine };
            var s3 = new LookSettings();
            Slots.Map(s3, "neon-night", @".\reshade-presets\deleted.ini");
            var look3 = Make(addon3, s3, new());
            look3.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            Check("a mapped preset missing from disk is not sent and the user's preset stays", !addon3.Sent.Any(x => x.StartsWith("preset")) && addon3.Current == mine && !look3.StatusOk, look3.Status);

            // Malformed reply to the status read: state must not change.
            var addon4 = new FakeAddon(version: 2) { Current = mine, MalformedFor = "status" };
            var s4 = new LookSettings();
            Slots.Map(s4, "neon-night", @".\reshade-presets\neon.ini");
            var look4 = Make(addon4, s4, new());
            look4.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            Check("control: a malformed reply while reading the user's preset changes nothing (no switch, nothing recorded)",
                addon4.Current == mine && s4.PreviousPreset == "" && !addon4.Sent.Any(x => x.StartsWith("preset")) && !look4.StatusOk, look4.Status);

            // A refused switch records nothing to restore.
            var addon5 = new FakeAddon(version: 2) { Current = mine, RefuseSwitch = true };
            var s5 = new LookSettings();
            Slots.Map(s5, "neon-night", @".\reshade-presets\neon.ini");
            var look5 = Make(addon5, s5, new());
            look5.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            Check("a refused preset switch leaves the preset as it was and nothing to restore", addon5.Current == mine && s5.PreviousPreset == "" && !look5.StatusOk, look5.Status);

            // Old add-on (v1): preset works and restore falls back to preset only.
            var addon6 = new FakeAddon(version: 1) { Current = mine };
            var s6 = new LookSettings();
            Slots.Map(s6, "neon-night", @".\reshade-presets\neon.ini");
            var look6 = Make(addon6, s6, new());
            look6.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            look6.RemoveAsync().GetAwaiter().GetResult();
            Check("an older add-on still switches and restores presets (restore of uniforms is skipped, never sent)",
                addon6.Current == mine && !addon6.Sent.Contains("restore") && s6.PreviousPreset == "");

            // No add-on: the manual route.
            var addon7 = new FakeAddon(version: 2) { Silent = true };
            var s7 = new LookSettings();
            Slots.Map(s7, "neon-night", @".\reshade-presets\neon.ini");
            var look7 = Make(addon7, s7, new());
            look7.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            Check("no add-on: manual route, the preset to pick is offered for copying, nothing is recorded",
                look7.Route == LookRoute.Manual && look7.ManualPreset == @".\reshade-presets\neon.ini" && s7.PreviousPreset == "" && look7.Status.Contains("Pick"), look7.Status);
            var s8 = new LookSettings { Verdict = "fail" };
            var addon8 = new FakeAddon(version: 2);
            var look8 = Make(addon8, s8, new());
            look8.ProbeAsync().GetAwaiter().GetResult();
            Check("a failed proof verdict pins the manual route without touching the pipe", look8.Route == LookRoute.Manual && addon8.Sent.Count == 0);

            // Dispose puts the preset back and unloads.
            var addon9 = new FakeAddon(version: 2) { Current = mine };
            var s9 = new LookSettings();
            Slots.Map(s9, "neon-night", @".\reshade-presets\neon.ini");
            var look9 = Make(addon9, s9, new());
            look9.ApplyAsync(SceneWith("neon-night")).GetAwaiter().GetResult();
            look9.Dispose();
            Check("plugin unload puts the user's preset back, then sends unload last", addon9.Current == mine && addon9.Sent.LastOrDefault() == "unload" && addon9.Unloaded, string.Join(" | ", addon9.Sent));
        }
        finally { try { Directory.Delete(tmp, true); } catch { /* best effort */ } }
    }

    // ---- Auto focus ----

    private void Focus()
    {
        var s = new LookSettings { FocusUniform = "ManualFocusPlane" };
        var cam = new Vector3(0, 1.5f, 3);
        var head = new Vector3(0, 1.5f, 0);
        Check("distance is camera to head", Near(AutoFocus.Distance(new AutoFocusInput(cam, head))!.Value, 3f));
        Check("control: a NaN or infinite point gives no distance", AutoFocus.Distance(new AutoFocusInput(new Vector3(float.NaN, 0, 0), head)) == null && AutoFocus.Distance(new AutoFocusInput(cam, new Vector3(0, float.PositiveInfinity, 0))) == null);
        Check("mapping is distance x scale + offset, kept between lowest and highest",
            Near(AutoFocus.Map(3f, new LookSettings { FocusScale = 2f, FocusOffset = 1f }), 7f) && Near(AutoFocus.Map(3f, new LookSettings { FocusMax = 2f }), 2f) &&
            Near(AutoFocus.Map(0.1f, new LookSettings { FocusMin = 0.5f }), 0.5f) && Near(AutoFocus.Map(3f, new LookSettings { FocusScale = -5f }), 0f));

        var af = new AutoFocus();
        var t0 = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var u1 = af.Next(new AutoFocusInput(cam, head), s, t0);
        Check("the first reading sends", u1 is { } a && Near(a.Value, 3f));
        af.Sent(u1!.Value, t0);
        Check("a still camera sends nothing more", af.Next(new AutoFocusInput(cam, head), s, t0.AddSeconds(1)) == null);
        var moved = new AutoFocusInput(new Vector3(0, 1.5f, 4), head);
        Check("control: a moved camera sends the new distance, but not within 100 ms of the last send",
            af.Next(moved, s, t0.AddMilliseconds(50)) == null && af.Next(moved, s, t0.AddMilliseconds(150)) is { } b && Near(b.Value, 4f));
        Check("a move smaller than the step sends nothing", af.Next(new AutoFocusInput(new Vector3(0, 1.5f, 3.005f), head), s, t0.AddSeconds(2)) == null);

        // The service: sends only when the Scene or the preview asks, with a uniform named.
        var addon = new FakeAddon(version: 2);
        var settings = new LookSettings { FocusUniform = "ManualFocusPlane" };
        var look = new LookService(new ReShadePipe(addon), Path.GetTempPath(), () => settings, _ => { }, _ => { }) { FocusSource = () => new AutoFocusInput(cam, head) };
        look.ProbeAsync().GetAwaiter().GetResult();
        look.Tick();
        Thread.Sleep(150);
        Check("control: auto focus off sends no focus command", !addon.Sent.Any(x => x.StartsWith("focus")));
        look.PreviewFocus = true;
        look.Tick();
        WaitFor(() => addon.Sent.Any(x => x.StartsWith("focus")));
        Check("auto focus on sends the focus uniform the user named", addon.Sent.Contains("focus ManualFocusPlane 3") && addon.Values.GetValueOrDefault("ManualFocusPlane") == 3f, string.Join(" | ", addon.Sent));
        var settings2 = new LookSettings();   // no uniform named
        var addon2 = new FakeAddon(version: 2);
        var look2 = new LookService(new ReShadePipe(addon2), Path.GetTempPath(), () => settings2, _ => { }, _ => { }) { FocusSource = () => new AutoFocusInput(cam, head), PreviewFocus = true };
        look2.ProbeAsync().GetAwaiter().GetResult();
        look2.Tick();
        Thread.Sleep(150);
        Check("control: with no focus uniform named nothing is sent", !addon2.Sent.Any(x => x.StartsWith("focus")));
        var addon3 = new FakeAddon(version: 1);
        var look3 = new LookService(new ReShadePipe(addon3), Path.GetTempPath(), () => new LookSettings { FocusUniform = "f" }, _ => { }, _ => { }) { FocusSource = () => new AutoFocusInput(cam, head), PreviewFocus = true };
        look3.ProbeAsync().GetAwaiter().GetResult();
        look3.Tick();
        WaitFor(() => !look3.StatusOk);
        Check("an older add-on: auto focus says it needs the new add-on and sends no focus line", !look3.StatusOk && look3.Status.Contains("version") && !addon3.Sent.Any(x => x.StartsWith("focus")), look3.Status);
    }

    private void Misc()
    {
        Check("SceneStore.AppliedBlocks includes look", SceneStore.AppliedBlocks.Contains("look"));
        var withFocus = new Scene { Id = "x", Name = "X", Lights = new(), Look = new SceneLook { Slot = "neon-night", AutoFocus = true } };
        var json = JsonSerializer.Serialize(withFocus, SceneStore.WriteOptions);
        var back = JsonSerializer.Deserialize<Scene>(json, SceneStore.ReadOptions);
        Check("SceneLook round-trips slot and auto focus", back?.Look is { Slot: "neon-night", AutoFocus: true });
        var plain = JsonSerializer.Serialize(new Scene { Id = "y", Look = new SceneLook { Slot = "dark-moody" } }, SceneStore.WriteOptions);
        Check("control: auto focus is left out of a file when it was never set (off by default)", !plain.Contains("autoFocus") && plain.Contains("dark-moody"));

        var shipped = SceneStore.LoadFile(Path.Combine(root, "scenes.json"), _ => { });
        var slots = shipped.Select(s => s.Look?.Slot).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        Check("every look slot in the shipped Scenes is one of the five known slots, and none has auto focus on", slots.All(x => Slots.Known.Contains(x!)) && shipped.All(s => s.Look?.AutoFocus != true), string.Join(",", slots));

        var areas = ChecklistRules.Discover(typeof(ChecklistRules).Assembly.GetTypes().Where(t => t.Namespace == "Soundstage.Checklist"), _ => { });
        var area = areas.FirstOrDefault(a => a.Id == "look");
        Check("checklist area LookSteps is found with order 110 and covers the in-game rows",
            area != null && area.Order == 110 && area.Steps.Count >= 8, area == null ? "missing" : $"{area.Steps.Count} steps");
    }

    // ---- Helpers ----

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static int IndexOf(List<string> list, string item) => list.IndexOf(item);

    private static void WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 60 && !done(); i++) Thread.Sleep(50);
    }

    /// <summary>Stands in for the ReShade add-on: holds the current preset and uniform values, and speaks the protocol at the version it is given.</summary>
    private sealed class FakeAddon : IReShadeLink
    {
        private readonly int version;
        private readonly object gate = new();

        public FakeAddon(int version) => this.version = version;

        public string Current { get; set; } = "start.ini";
        public Dictionary<string, float> Values { get; } = new();
        public List<string> Sent { get; } = new();
        public bool Silent { get; set; }
        public bool Malformed { get; set; }
        public string? MalformedFor { get; set; }
        public bool RefuseSwitch { get; set; }
        public bool Unloaded { get; private set; }

        public Task<ReShadeReply> SendAsync(string line, CancellationToken cancel = default)
        {
            lock (gate)
            {
                if (Silent) return Task.FromResult(ReShadeReply.Fail("The add-on did not reply in time."));
                Sent.Add(line);
                var verb = line.Split(' ', 2)[0];
                if (Malformed || verb == MalformedFor) return Task.FromResult(ReShadeProtocol.ParseReply("garbage with no OK"));
                var reply = verb switch
                {
                    "ping" => $"OK soundstage-addon {version}",
                    "status" => "OK preset=" + Current,
                    "preset" when RefuseSwitch => "ERR preset file not found: " + line[7..],
                    "preset" => Switch(line[7..]),
                    "focus" when version >= 2 => Focus(line.Split(' ')),
                    "restore" when version >= 2 => "OK restored=" + Values.Count,
                    "unload" when version >= 2 => Unload(),
                    "uniforms" => "OK " + string.Join(",", Values.Keys),
                    _ => "ERR unknown command: " + verb,
                };
                return Task.FromResult(ReShadeProtocol.ParseReply(reply));
            }
        }

        private string Switch(string path) { Current = path; return "OK preset=" + path; }

        private string Focus(string[] parts)
        {
            Values[parts[1]] = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
            return $"OK focus {parts[1]}={parts[2]}";
        }

        private string Unload() { Unloaded = true; return "OK unloaded restored=0"; }
    }
}

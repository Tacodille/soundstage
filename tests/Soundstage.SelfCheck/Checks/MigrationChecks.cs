// Written in step 1.3 of the Soundstage plan, moved here in 1.5.
using System.Numerics;
using Soundstage;

namespace Soundstage.SelfCheck;

public sealed class MigrationChecks : CheckArea
{
    public override string Name => "Migration from Keylight and config sections";
    public override int Order => 30;

    public override void Run()
    {
        // ---- 13. Migration of the old Keylight config and user presets (pure functions, no Dalamud) ----
        {
            var legacyDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "legacy"));
            string TreeHash(string dir) => string.Join("|", Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => Path.GetRelativePath(dir, f) + "=" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f)))));
            void CopyTree(string from, string to)
            {
                foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(to, Path.GetRelativePath(from, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest);
                }
            }

            var realText = File.ReadAllText(Path.Combine(legacyDir, "Keylight.json"));
            var mlog = new List<string>();
            var mapped = Soundstage.Migration.LegacyKeylight.ParseConfig(realText, mlog.Add);
            Check("real old Keylight.json parses with no log line", mapped != null && mlog.Count == 0, $"{mlog.Count} log lines");
            Check("every old field maps: flags, selected preset, backdrop, pose folder, canvas offsets",
                mapped != null && mapped.FollowPlayer && !mapped.ShowLightMarkers && mapped.SelectedPresetId == "soft-window" && mapped.ShowBackdrop
                && mapped.Backdrop.ScaleX == 1f && mapped.PoseFolders.Count == 1 && mapped.PoseFolders[0].EndsWith("FFXIV Mods") && mapped.PoseFoldersInitialised
                && mapped.IncludeBrioLibraryFolders && mapped.PoseBody && mapped.PoseExpression && mapped.PoseHoverPreview && mapped.Version == 3,
                mapped == null ? "null" : $"preset {mapped.SelectedPresetId}, folders {mapped.PoseFolders.Count}");
            Check("old test results map across (20 of them, '$type' skipped, a note intact)",
                mapped != null && mapped.TestResults.Count == 20 && !mapped.TestResults.ContainsKey("$type") && mapped.TestResults["6"].Note.StartsWith("Cant remove") && mapped.TestResults["3:neon-green-rim"].Status == "PASS",
                mapped == null ? "null" : mapped.TestResults.Count.ToString());

            var tmp = Path.Combine(Path.GetTempPath(), "soundstage-migrate-" + Guid.NewGuid().ToString("N"));
            try
            {
                // First run on a copy of the real files.
                var dirA = Path.Combine(tmp, "a");
                CopyTree(legacyDir, dirA);
                var before = TreeHash(dirA);
                var alog = new List<string>();
                var r1 = Soundstage.Migration.LegacyKeylight.Run(dirA, alog.Add);
                var srcAfter = TreeHash(dirA);
                var newPresets = Path.Combine(dirA, "Soundstage", "user-presets.json");
                Check("first run migrates and copies the user presets", r1.Outcome == Soundstage.Migration.MigrationOutcome.Migrated && r1.Settings != null && r1.PresetsCopied && File.Exists(newPresets) && alog.Count == 0);
                Check("the user-presets copy is byte-identical and the source files are untouched",
                    File.ReadAllBytes(newPresets).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dirA, "Keylight", "user-presets.json")))
                    && srcAfter.StartsWith(before) && File.ReadAllText(Path.Combine(dirA, "Keylight.json")) == realText);

                // Second run, once the caller has saved Soundstage.json: nothing happens.
                File.WriteAllText(Path.Combine(dirA, "Soundstage.json"), "{}");
                var afterFirst = TreeHash(dirA);
                var r2 = Soundstage.Migration.LegacyKeylight.Run(dirA, alog.Add);
                Check("second migration run changes nothing", r2.Outcome == Soundstage.Migration.MigrationOutcome.NotNeeded && r2.Settings == null && TreeHash(dirA) == afterFirst && alog.Count == 0);

                // An existing Soundstage presets file is never overwritten.
                var dirB = Path.Combine(tmp, "b");
                CopyTree(legacyDir, dirB);
                Directory.CreateDirectory(Path.Combine(dirB, "Soundstage"));
                File.WriteAllText(Path.Combine(dirB, "Soundstage", "user-presets.json"), "{\"formatVersion\":1,\"presets\":[]}");
                var r3 = Soundstage.Migration.LegacyKeylight.Run(dirB, _ => { });
                Check("an existing Soundstage user-presets.json is not overwritten", r3.Outcome == Soundstage.Migration.MigrationOutcome.Migrated && !r3.PresetsCopied
                    && File.ReadAllText(Path.Combine(dirB, "Soundstage", "user-presets.json")).Contains("\"presets\":[]"));

                // Truncated JSON: defaults and exactly one log line (the negative control).
                var dirC = Path.Combine(tmp, "c");
                Directory.CreateDirectory(dirC);
                File.WriteAllText(Path.Combine(dirC, "Keylight.json"), realText.Substring(0, realText.Length / 3));
                var clog = new List<string>();
                var r4 = Soundstage.Migration.LegacyKeylight.Run(dirC, clog.Add);
                Check("truncated legacy JSON gives defaults and one log line",
                    r4.Outcome == Soundstage.Migration.MigrationOutcome.DefaultsAfterReadFailure && r4.Settings != null && !r4.Settings.FollowPlayer && r4.Settings.SelectedPresetId == "" && r4.Settings.TestResults.Count == 0 && clog.Count == 1,
                    $"{clog.Count} log lines");
                Console.WriteLine("      log: " + string.Join(" / ", clog));

                // Nothing at all: a fresh install.
                var dirD = Path.Combine(tmp, "d");
                Directory.CreateDirectory(dirD);
                Check("no Keylight files means nothing to migrate", Soundstage.Migration.LegacyKeylight.Run(dirD, _ => { }).Outcome == Soundstage.Migration.MigrationOutcome.NothingToMigrate);

                // Wrong types keep the defaults instead of failing.
                var odd = Soundstage.Migration.LegacyKeylight.ParseConfig("{\"FollowPlayer\":\"yes\",\"PoseFolders\":5,\"Backdrop\":3,\"ShowBackdrop\":false}", _ => { });
                Check("wrongly typed fields keep their defaults, valid ones still map", odd != null && !odd.FollowPlayer && odd.PoseFolders.Count == 0 && odd.Backdrop.ScaleX == 1f && !odd.ShowBackdrop);
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            // Config sections: a feature's settings round-trip, and a missing or broken section gives a fresh default.
            var sections = new Dictionary<string, string>();
            Soundstage.ConfigSections.Set(sections, "demo", new DemoSection { Count = 7, Label = "x" });
            var back3 = Soundstage.ConfigSections.Get<DemoSection>(sections, "demo");
            sections["broken"] = "{not json";
            Check("config section round-trips; missing or broken sections give defaults",
                back3.Count == 7 && back3.Label == "x" && Soundstage.ConfigSections.Get<DemoSection>(sections, "nope").Count == 1 && Soundstage.ConfigSections.Get<DemoSection>(sections, "broken").Count == 1);
        }

    }
}

public sealed class DemoSection
{
    public int Count { get; set; } = 1;
    public string Label { get; set; } = "";
}

// Integration fix (parts 16+17): the Face and Expression tabs share one FaceServices.
using Soundstage.Face;

namespace Soundstage.SelfCheck;

public sealed class SharedFaceChecks : CheckArea
{
    public override string Name => "Shared face helper (integration fix 16+17)";
    public override int Order => 175;

    public override void Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ss-sharedface-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var shared = new SharedFace(() => new FaceServices(dir, new FaceSettings(), _ => { }));
            var fromFaceTab = shared.Get();
            var fromExpressionTab = shared.Get();
            var fromBody = shared.Get();
            Check("shared face: both tabs and the body code get the same instance", ReferenceEquals(fromFaceTab, fromExpressionTab) && ReferenceEquals(fromFaceTab, fromBody));
            Check("shared face: the factory ran once", shared.Created == 1, $"{shared.Created}");
            Check("shared face: one analyser, so one helper", ReferenceEquals(fromFaceTab.Analyser, fromExpressionTab.Analyser));

            // Control: two separate holders (what two private instances amounted to) give different instances.
            var other = new SharedFace(() => new FaceServices(dir, new FaceSettings(), _ => { }));
            var control = other.Get();
            Check("shared face (control): separate holders give different instances", !ReferenceEquals(fromFaceTab, control));
            Check("shared face (control): separate analysers", !ReferenceEquals(fromFaceTab.Analyser, control.Analyser));

            shared.Dispose();
            shared.Dispose();
            bool threw = false;
            try { shared.Get(); } catch (ObjectDisposedException) { threw = true; }
            Check("shared face: disposed once, safe to dispose twice, refuses use after", threw);
            other.Dispose();
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }

        // The tabs really use it: nothing outside Studio builds a FaceServices.
        var builders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"new\s+([A-Za-z]+\.)*FaceServices\(") && Path.GetFileName(f) != "Studio.cs").ToList();
        Check("shared face: only Studio constructs a FaceServices", builders.Count == 0, string.Join(", ", builders.Select(Path.GetFileName)));
        Check("shared face (control): the source scan can see Studio's construction",
            System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(root, "Studio.cs")), @"new\s+([A-Za-z]+\.)*FaceServices\("));
        Check("shared face: both tabs read studio.Face",
            File.ReadAllText(Path.Combine(root, "Tabs", "FaceTab.cs")).Contains("studio.Face") && File.ReadAllText(Path.Combine(root, "Tabs", "ExpressionTab.cs")).Contains("studio.Face"));
    }
}

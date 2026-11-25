using System.IO.Pipes;
using System.Text;
using Soundstage.Checklist;
using Soundstage.Look;

namespace Soundstage.SelfCheck;

/// <summary>
/// Part 3: the plugin side of the ReShade add-on route. The add-on itself only runs inside the game, so this checks the
/// protocol lines, the reply parser, and the pipe client against a fake server on a private pipe name (never the real one).
/// </summary>
public sealed class ReShadeChecks : CheckArea
{
    public override string Name => "ReShade add-on (plugin side)";
    public override int Order => 30;

    public override void Run()
    {
        Check("commands are single lines in the documented shape",
            ReShadeProtocol.Ping() == "ping" && ReShadeProtocol.Status() == "status" && ReShadeProtocol.Uniforms() == "uniforms"
            && ReShadeProtocol.Preset(@".\reshade-presets\A B\c.ini") == @"preset .\reshade-presets\A B\c.ini"
            && ReShadeProtocol.Shot(@"C:\x\y.png") == @"shot C:\x\y.png");
        Check("uniform values use invariant culture and round-trip", ReShadeProtocol.Uniform("Brightness", 0.5f, 1f) == "uniform Brightness 0.5 1");

        // Negative controls: bad input is refused before anything is sent.
        Check("empty preset path is refused", Throws(() => ReShadeProtocol.Preset("  ")));
        Check("line break in a path is refused (no second command can ride along)", Throws(() => ReShadeProtocol.Preset("a.ini\nshot C:\\x.png")));
        Check("screenshot path must be a .png", Throws(() => ReShadeProtocol.Shot(@"C:\x\y.jpg")));
        Check("uniform name with a space, no values, five values and NaN are refused",
            Throws(() => ReShadeProtocol.Uniform("a b", 1f)) && Throws(() => ReShadeProtocol.Uniform("a")) &&
            Throws(() => ReShadeProtocol.Uniform("a", 1, 2, 3, 4, 5)) && Throws(() => ReShadeProtocol.Uniform("a", float.NaN)));

        var ok = ReShadeProtocol.ParseReply("OK preset=.\\a.ini\r\n");
        var err = ReShadeProtocol.ParseReply("ERR preset file not found: x.ini");
        Check("OK and ERR replies parse", ok is { Ok: true, Text: "preset=.\\a.ini" } && err is { Ok: false, Text: "preset file not found: x.ini" });
        Check("empty and unknown replies are failures, not successes", !ReShadeProtocol.ParseReply("").Ok && !ReShadeProtocol.ParseReply(null).Ok && !ReShadeProtocol.ParseReply("hello").Ok);

        // The pipe client against a fake add-on.
        var pipe = "soundstage-selfcheck-" + Environment.ProcessId;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(() => FakeAddon(pipe, stop.Token));
        var probe = new ReShadeProbe(pipe);
        var ping = probe.SendAsync(ReShadeProtocol.Ping()).GetAwaiter().GetResult();
        Check("client gets an OK reply from a server", ping is { Ok: true, Text: "soundstage-addon 1" }, ping.Text);
        var bad = probe.SendAsync(ReShadeProtocol.Preset(@".\reshade-presets\does-not-exist.ini")).GetAwaiter().GetResult();
        Check("negative control: a nonexistent preset comes back as a plain error and the fake's current preset is unchanged",
            !bad.Ok && bad.Text.StartsWith("preset file not found") && FakeCurrent == "start.ini", $"{bad.Text} / current={FakeCurrent}");
        var good = probe.SendAsync(ReShadeProtocol.Preset(@".\reshade-presets\real.ini")).GetAwaiter().GetResult();
        Check("control: an existing preset switches it (so the negative control could have failed)", good.Ok && FakeCurrent == @".\reshade-presets\real.ini", good.Text);
        stop.Cancel();
        try { using var unblock = new NamedPipeClientStream(".", pipe, PipeDirection.InOut); unblock.Connect(300); } catch { /* server already gone */ }

        var started = DateTime.UtcNow;
        var none = new ReShadeProbe("soundstage-selfcheck-nobody-" + Environment.ProcessId).SendAsync(ReShadeProtocol.Ping()).GetAwaiter().GetResult();
        var waited = DateTime.UtcNow - started;
        Check("no add-on: a plain message within 2 seconds, no exception", !none.Ok && none.Text.Contains("soundstage.addon64") && waited < TimeSpan.FromSeconds(2), $"{waited.TotalMilliseconds:F0} ms");

        var areas = ChecklistRules.Discover(typeof(ChecklistRules).Assembly.GetTypes().Where(t => t.Namespace == "Soundstage.Checklist"), _ => { });
        var area = areas.FirstOrDefault(a => a.Id == "reshade");
        Check("checklist area ReShadeProofSteps is found, order 30, with a step for each proof row",
            area != null && area.Order == 30 && area.Steps.Count >= 7, area == null ? "missing" : $"{area.Steps.Count} steps");
    }

    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (ArgumentException) { return true; }
    }

    private static string FakeCurrent = "start.ini";

    // Stands in for the add-on: preset files "exist" only when the path ends in real.ini.
    private static void FakeAddon(string pipeName, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1);
            server.WaitForConnection();
            var buffer = new byte[1024];
            var read = server.Read(buffer, 0, buffer.Length);
            if (read <= 0 || stop.IsCancellationRequested) continue;
            var line = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\n');
            string reply;
            if (line == "ping") reply = "OK soundstage-addon 1";
            else if (line.StartsWith("preset ") && line.EndsWith("real.ini")) { FakeCurrent = line[7..]; reply = "OK preset=" + FakeCurrent; }
            else if (line.StartsWith("preset ")) reply = "ERR preset file not found: " + line[7..];
            else reply = "ERR unknown command";
            var bytes = Encoding.UTF8.GetBytes(reply + "\n");
            server.Write(bytes, 0, bytes.Length);
            server.Flush();
            try { server.WaitForPipeDrain(); } catch { /* client already gone */ }
        }
    }
}

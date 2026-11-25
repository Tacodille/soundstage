// No Dalamud types in this file: the offline self-check compiles it as a shared file.
using System.Globalization;

namespace Soundstage.Look;

/// <summary>The reply to one add-on command: <see cref="Ok"/> plus the text after "OK " or "ERR ", or a plain reason it never got an answer.</summary>
public sealed record ReShadeReply(bool Ok, string Text)
{
    public static ReShadeReply Fail(string text) => new(false, text);
}

/// <summary>
/// The line protocol the ReShade add-on speaks (see docs/proof-reshade.md, section 5). Building and reading lines is pure
/// so it can be checked offline; <see cref="ReShadeProbe"/> does the pipe.
/// </summary>
public static class ReShadeProtocol
{
    public const string PipeName = "soundstage-reshade";

    /// <summary>The protocol version this plugin build speaks. The add-on's ping reply carries its own; version 1 is part 3's proof add-on.</summary>
    public const int PluginVersion = 2;

    /// <summary>The add-on version that first understood a command (1 for everything part 3 shipped).</summary>
    public static int MinAddonVersion(string verb) => verb switch { "focus" or "restore" or "unload" => 2, _ => 1 };

    public static string Ping() => "ping";
    public static string Status() => "status";
    public static string Uniforms() => "uniforms";

    /// <summary>The add-on checks the file exists and leaves the current preset alone when it does not.</summary>
    public static string Preset(string path) => "preset " + Clean(path, "preset path");

    public static string Shot(string path)
    {
        var clean = Clean(path, "screenshot path");
        if (!clean.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The screenshot path must end in .png.");
        return "shot " + clean;
    }

    /// <summary>Writes the focus distance (metres, already mapped by the caller if the shader wants another unit) into one float uniform. Add-on version 2.</summary>
    public static string Focus(string uniform, float metres)
    {
        if (string.IsNullOrWhiteSpace(uniform) || uniform.Any(char.IsWhiteSpace)) throw new ArgumentException("A uniform name has no spaces.");
        if (!float.IsFinite(metres) || metres < 0f) throw new ArgumentException("A focus distance must be a finite number, not negative.");
        return "focus " + uniform + " " + metres.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Puts every uniform the add-on changed back to the value it had first. Add-on version 2.</summary>
    public static string Restore() => "restore";

    /// <summary>Restores uniforms, then drops the connection so the pipe is free for the next client. Add-on version 2.</summary>
    public static string Unload() => "unload";

    /// <summary>The add-on version in a ping reply ("soundstage-addon 2"), or null when the reply is not a ping reply.</summary>
    public static int? ParseAddonVersion(ReShadeReply reply)
    {
        if (!reply.Ok) return null;
        var parts = reply.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0] == "soundstage-addon" && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    /// <summary>A plain sentence when the add-on is too old for the command, or null when it is fine (or its version is not known yet).</summary>
    public static string? VersionMismatch(string verb, int? addonVersion)
    {
        if (addonVersion is not int v) return null;
        var need = MinAddonVersion(verb);
        return v >= need ? null : $"The ReShade add-on in your game folder is version {v}, and \"{verb}\" needs version {need}. Close the game, copy the new soundstage.addon64 over the old one and start the game again.";
    }

    /// <summary>"ERR unknown command: focus" is what an older add-on says; turn it into the version-mismatch sentence.</summary>
    public static ReShadeReply ExplainUnknownCommand(string line, ReShadeReply reply)
    {
        if (reply.Ok || !reply.Text.StartsWith("unknown command", StringComparison.Ordinal)) return reply;
        var verb = line.Split(' ', 2)[0];
        return ReShadeReply.Fail(VersionMismatch(verb, 1) ?? reply.Text);
    }

    public static string Uniform(string name, params float[] values)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace)) throw new ArgumentException("A uniform name has no spaces.");
        if (values.Length is < 1 or > 4) throw new ArgumentException("A uniform takes one to four values.");
        if (values.Any(v => !float.IsFinite(v))) throw new ArgumentException("A uniform value must be a finite number.");
        return "uniform " + name + " " + string.Join(" ", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    }

    /// <summary>"OK ..." is a success, "ERR ..." a refusal the add-on explained; anything else is not a reply from the add-on.</summary>
    public static ReShadeReply ParseReply(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return ReShadeReply.Fail("The add-on sent no reply.");
        line = line.TrimEnd('\r', '\n');
        if (line == "OK") return new ReShadeReply(true, "");
        if (line.StartsWith("OK ", StringComparison.Ordinal)) return new ReShadeReply(true, line[3..]);
        if (line.StartsWith("ERR ", StringComparison.Ordinal)) return ReShadeReply.Fail(line[4..]);
        return ReShadeReply.Fail("Unexpected reply: " + line);
    }

    private static string Clean(string value, string what)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"The {what} is empty.");
        if (value.Contains('\n') || value.Contains('\r')) throw new ArgumentException($"The {what} has a line break in it.");
        return value.Trim();
    }
}

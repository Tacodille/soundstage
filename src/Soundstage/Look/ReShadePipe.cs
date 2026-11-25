// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Look;

/// <summary>Anything that can carry one add-on command line and bring back a reply. <see cref="ReShadeProbe"/> is the real one; the self-check uses a fake.</summary>
public interface IReShadeLink
{
    Task<ReShadeReply> SendAsync(string line, CancellationToken cancel = default);
}

/// <summary>
/// The version-aware layer over the pipe: it learns the add-on's version from a ping, refuses a command the add-on is
/// too old for with a plain sentence, and turns an older add-on's "unknown command" into the same sentence. A reply that
/// does not parse never changes what this object believes.
/// </summary>
public sealed class ReShadePipe
{
    private readonly IReShadeLink link;

    public ReShadePipe(IReShadeLink link) => this.link = link;

    /// <summary>Null until a ping has been answered by an add-on.</summary>
    public int? AddonVersion { get; private set; }

    public async Task<ReShadeReply> PingAsync(CancellationToken cancel = default)
    {
        var reply = await link.SendAsync(ReShadeProtocol.Ping(), cancel).ConfigureAwait(false);
        var version = ReShadeProtocol.ParseAddonVersion(reply);
        if (version != null) AddonVersion = version;
        return reply;
    }

    /// <summary>Sends a command. The first command after a start or an unanswered ping pings first, so the version is known.</summary>
    public async Task<ReShadeReply> SendAsync(string line, CancellationToken cancel = default)
    {
        if (AddonVersion == null)
        {
            var ping = await PingAsync(cancel).ConfigureAwait(false);
            if (!ping.Ok) return ping;
        }
        var verb = line.Split(' ', 2)[0];
        var mismatch = ReShadeProtocol.VersionMismatch(verb, AddonVersion);
        if (mismatch != null) return ReShadeReply.Fail(mismatch);
        var reply = await link.SendAsync(line, cancel).ConfigureAwait(false);
        return ReShadeProtocol.ExplainUnknownCommand(line, reply);
    }
}

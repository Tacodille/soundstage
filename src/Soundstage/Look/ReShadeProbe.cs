using System.IO.Pipes;
using System.Text;

namespace Soundstage.Look;

/// <summary>
/// Pipe client for the Soundstage ReShade add-on. Short timeouts, no retry loops, and every failure comes back as a
/// plain sentence in a <see cref="ReShadeReply"/>: it never throws at the caller. Call it off the game thread.
/// </summary>
public sealed class ReShadeProbe : IReShadeLink
{
    public const int ConnectTimeoutMs = 600;
    public const int ReplyTimeoutMs = 4000;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string pipeName;

    /// <param name="pipeName">Only the self-check passes one, so a test server never meets the real add-on.</param>
    public ReShadeProbe(string pipeName = ReShadeProtocol.PipeName) => this.pipeName = pipeName;

    public async Task<ReShadeReply> SendAsync(string line, CancellationToken cancel = default)
    {
        if (!await gate.WaitAsync(ReplyTimeoutMs, cancel).ConfigureAwait(false)) return ReShadeReply.Fail("Another ReShade command is still running.");
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(ConnectTimeoutMs, cancel).ConfigureAwait(false); }
            catch (TimeoutException) { return ReShadeReply.Fail("The ReShade add-on did not answer. Is soundstage.addon64 in the game folder, and has the game been restarted since?"); }
            catch (IOException ex) { return ReShadeReply.Fail("Could not open the ReShade add-on's pipe: " + ex.Message); }

            var bytes = Encoding.UTF8.GetBytes(line + "\n");
            await pipe.WriteAsync(bytes, cancel).ConfigureAwait(false);
            await pipe.FlushAsync(cancel).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(ReplyTimeoutMs);
            var reply = new StringBuilder();
            var buffer = new byte[1024];
            try
            {
                while (!reply.ToString().Contains('\n'))
                {
                    var read = await pipe.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                    if (read == 0) break;
                    reply.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested) { return ReShadeReply.Fail("The add-on did not reply in time."); }
            return ReShadeProtocol.ParseReply(reply.ToString().Split('\n')[0]);
        }
        catch (OperationCanceledException) { return ReShadeReply.Fail("Cancelled."); }
        catch (Exception ex) { return ReShadeReply.Fail("ReShade add-on error: " + ex.Message); }
        finally { gate.Release(); }
    }
}

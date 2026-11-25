// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Capture;

/// <summary>One way of getting the game's picture into a PNG file. A route says why it failed in a plain sentence and never throws.</summary>
public interface ICaptureRoute
{
    /// <summary>Short stable id: "addon", "key" or "window".</summary>
    string Id { get; }

    /// <summary>For the tab and the log.</summary>
    string Label { get; }

    /// <summary>Writes a PNG to <paramref name="path"/> (a temporary name the service renames afterwards).</summary>
    Task<RouteResult> CaptureAsync(string path, CancellationToken cancel);
}

public sealed record RouteResult(bool Ok, string Message)
{
    public static RouteResult Success(string message = "") => new(true, message);
    public static RouteResult Fail(string message) => new(false, message);
}

/// <summary>What <see cref="CaptureService.CaptureAsync"/> returns. <see cref="Path"/> is set only when the file is complete.</summary>
public sealed record CaptureResult(bool Ok, string Path, string Route, long Milliseconds, string Message);

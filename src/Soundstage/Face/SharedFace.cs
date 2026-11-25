// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>
/// Holds the one <see cref="FaceServices"/> the Face and Expression tabs (and the body retarget) share, built on first use. Two instances would
/// mean two helper processes. Studio owns one of these; the plugin unload stops the helper through FaceServices' own load-context hook.
/// </summary>
public sealed class SharedFace : IDisposable
{
    private readonly Func<FaceServices> factory;
    private readonly object gate = new();
    private FaceServices? value;
    private bool disposed;

    public SharedFace(Func<FaceServices> factory) => this.factory = factory;

    /// <summary>How many times the factory has run. One, however many tabs ask.</summary>
    public int Created { get; private set; }

    public FaceServices Get()
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SharedFace));
            if (value == null) { value = factory(); Created++; }
            return value;
        }
    }

    public void Dispose()
    {
        FaceServices? v;
        lock (gate) { if (disposed) return; disposed = true; v = value; value = null; }
        v?.Dispose();
    }
}

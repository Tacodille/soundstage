// No Dalamud types in this file.
namespace Soundstage.Face;

/// <summary>
/// The one door to face analysis. Whatever sits behind it (today a helper process, see docs/face-model-decision.md), a caller
/// only ever sees a <see cref="FaceResult"/>. An implementation never throws: every failure comes back as
/// <see cref="FaceResult.Unavailable"/> with a plain sentence.
/// </summary>
public interface IFaceAnalyser
{
    /// <summary>True when a call can be tried now (the helper is installed). It does not promise the picture has a face.</summary>
    bool CanTry { get; }

    /// <summary>Where things stand, as one line for the tab ("Face helper not downloaded yet", "ready", "failed to start: ...").</summary>
    string Status { get; }

    /// <summary>Analyses the picture file. Blocks until the answer or a timeout, so call it off the draw thread.</summary>
    FaceResult Analyse(string imagePath);
}

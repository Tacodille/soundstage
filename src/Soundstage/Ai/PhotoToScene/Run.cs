// No Dalamud types in this file: the offline self-check compiles it as a shared file.
namespace Soundstage.Ai.PhotoToScene;

/// <summary>Where the photo run stands before anything is sent.</summary>
public enum PhotoGate { Ready, NoKey, NeedsConsent }

/// <summary>Optional input from the face model (part 16). Words only, so the card can show a measured head angle in place of the AI's guess.</summary>
public interface IPhotoFaceHints
{
    /// <summary>Null when no face model is installed or it could not read the photo. Never sends anything anywhere.</summary>
    PhotoFaceHint? Read(byte[] jpeg);
}

public sealed record PhotoFaceHint(string HeadYaw, string HeadPitch, string Expression);

/// <summary>The result of one run: a card to show, or a plain message and the typed failure behind it.</summary>
public sealed class PhotoOutcome
{
    public PhotoAnalysis? Analysis { get; init; }
    public Scene? Scene { get; init; }
    public string BaseSceneId { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
    public AiFailure? Failure { get; init; }
    public bool Cancelled { get; init; }
    public string Message { get; init; } = "";
    public PhotoFaceHint? FaceHint { get; init; }
    public bool Ok => Scene != null && Analysis != null;
}

/// <summary>
/// One photo analysis: checks for a key and the part 6 consent first (and sends nothing if either is missing), sends the
/// prepared picture through <see cref="AiService"/>, then parses and maps the answer. The request can be cancelled.
/// </summary>
public sealed class PhotoRun
{
    private readonly AiService ai;
    private readonly Func<IEnumerable<Scene>> scenes;

    public PhotoRun(AiService ai, Func<IEnumerable<Scene>> scenes)
    {
        this.ai = ai;
        this.scenes = scenes;
    }

    public IPhotoFaceHints? FaceHints { get; set; }

    public string ProviderName => AiService.NameOf(ai.ProviderId);

    /// <summary>The text on the Analyse button, with the provider and model named so nothing is a surprise.</summary>
    public string ButtonLabel()
    {
        var model = ai.Settings.For(ai.ProviderId).Model;
        return $"Analyse with {ProviderName} ({(string.IsNullOrWhiteSpace(model) ? "model picked for your key" : model)})";
    }

    public PhotoGate Gate()
    {
        var id = ai.ProviderId;
        if (!ai.Keys.Has(id)) return PhotoGate.NoKey;
        if (!ai.HasConsent(id)) return PhotoGate.NeedsConsent;
        return PhotoGate.Ready;
    }

    public async Task<PhotoOutcome> Analyse(PreparedImage image, CancellationToken ct = default)
    {
        if (!image.Ok) return new PhotoOutcome { Message = image.Message };
        var gate = Gate();
        if (gate == PhotoGate.NoKey) return Refused(AiError.NoKey);
        if (gate == PhotoGate.NeedsConsent) return Refused(AiError.ConsentNeeded);

        var catalogue = PhotoPrompt.Catalogue(scenes());
        var prompt = PhotoPrompt.Build(catalogue);
        AiResult<System.Text.Json.Nodes.JsonObject> result;
        try
        {
            result = await ai.AnalyseImage(image.Jpeg, "image/jpeg", PhotoSchema.Build(), prompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new PhotoOutcome { Cancelled = true, Message = "Cancelled. Nothing was changed." };
        }
        if (ct.IsCancellationRequested) return new PhotoOutcome { Cancelled = true, Message = "Cancelled. Nothing was changed." };
        if (!result.Ok) return Refused(result.Failure!);

        var (analysis, error) = PhotoParser.Parse(result.Value, catalogue);
        if (analysis == null)
        {
            ai.Log.Info("photo reply was not usable: " + error);
            return Refused(new AiFailure(AiError.NotJson, error ?? ""));
        }

        var hint = FaceHints?.Read(image.Jpeg);
        var mapped = PhotoMapper.Map(analysis, catalogue);
        if (mapped.Scene == null)
        {
            ai.Log.Info("photo mapping failed: " + mapped.Error);
            return new PhotoOutcome { Analysis = analysis, Notes = mapped.Notes, Message = mapped.Error ?? "The Scene could not be made.", FaceHint = hint };
        }
        return new PhotoOutcome { Analysis = analysis, Scene = mapped.Scene, BaseSceneId = mapped.BaseSceneId, Notes = mapped.Notes, FaceHint = hint };
    }

    private PhotoOutcome Refused(AiError error) => Refused(new AiFailure(error));

    private PhotoOutcome Refused(AiFailure failure) =>
        new() { Failure = failure, Message = AiMessages.For(failure, ProviderName) };
}

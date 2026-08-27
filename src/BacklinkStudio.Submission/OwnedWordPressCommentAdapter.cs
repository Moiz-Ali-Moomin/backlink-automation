using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class OwnedWordPressCommentAdapter(
    IWordPressSubmissionGateway standard,
    IOwnedWordPressFallbackCommentAdapter fallback,
    IControlledBrowserCommentAdapter browser,
    IWordPressSiteProfileRepository profiles) : IOwnedWordPressCommentAdapter
{
    public string Name => nameof(OwnedWordPressCommentAdapter);

    public async Task<OwnedWordPressSubmissionResult> SubmitAsync(OwnedWordPressSubmissionRequest request, CancellationToken cancellationToken)
    {
        var source = request.Source;
        var profile = await profiles.FindForSourceAsync(source.OwnedNetworkProfileId, source.Host, cancellationToken);
        if (profile?.SubmissionMode is WordPressSubmissionMode.DirectApi or WordPressSubmissionMode.AuthenticatedIntegration)
            return await standard.SubmitAsync(request, cancellationToken);

        if (source.TechnicalCompatibility == TechnicalCompatibility.Compatible && !source.RequiresBrowser)
        {
            var result = await standard.SubmitAsync(request, cancellationToken);
            if (IsDefinitive(result)) return result;
        }

        if ((source.TechnicalCompatibility is TechnicalCompatibility.Compatible or
                TechnicalCompatibility.FallbackCandidate) && source.PostId is not null)
        {
            var result = await fallback.SubmitAsync(request, cancellationToken);
            if (IsDefinitive(result)) return result;
        }

        var browserCandidate = source.RequiresBrowser ||
            source.TechnicalCompatibility is TechnicalCompatibility.Compatible or TechnicalCompatibility.FallbackCandidate;
        if (browserCandidate && (profile is null || profile.SubmissionMode is
                WordPressSubmissionMode.ControlledBrowser or WordPressSubmissionMode.StandardComment))
            return await browser.SubmitAsync(request, profile, cancellationToken);

        return new(SubmissionStatus.ManualActionRequired, ModerationStatus.Unknown,
            WordPressSubmissionMode.ManualActionRequired, null, null, SubmissionFailureKind.BrowserRequired,
              "No governed WordPress submission strategy is available for this source.");
    }

    private static bool IsDefinitive(OwnedWordPressSubmissionResult result) =>
        result.MayHaveCreatedBacklink || result.FailureKind is not (
            SubmissionFailureKind.BrowserRequired or
            SubmissionFailureKind.UnsupportedForm or
            SubmissionFailureKind.EndpointNotFound or
            SubmissionFailureKind.ValidationFailed);
}

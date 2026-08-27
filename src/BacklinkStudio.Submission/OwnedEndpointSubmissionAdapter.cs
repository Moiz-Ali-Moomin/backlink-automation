using System.Net.Http.Json;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using Microsoft.Extensions.Options;

namespace BacklinkStudio.Submission;

public sealed class OwnedEndpointSubmissionAdapter(HttpClient client, IOptions<SubmissionOptions> options, ISubmissionAuthorizationResolver authorizations) : ISubmissionAdapter
{
    public string Name => "OwnedEndpoint";
    public bool CanHandle(string authorizationProfileKey, OpportunityType opportunityType)
    {
        try { return authorizations.Resolve(authorizationProfileKey).AllowedTypes.Contains(opportunityType); }
        catch (UnauthorizedAccessException) { return false; }
    }

    public async Task<SubmissionResult> SubmitAsync(SubmissionContext context, CancellationToken cancellationToken)
    {
        if (!options.Value.Profiles.TryGetValue(context.AuthorizationProfileKey, out var profile))
            throw new UnauthorizedAccessException("The named submission authorization profile is not configured.");
        using var request = new HttpRequestMessage(HttpMethod.Post, profile.Endpoint)
        {
            Content = JsonContent.Create(new { context.SourceUrl, context.TargetUrl, context.AnchorText, context.CampaignId })
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", context.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(profile.BearerToken)) request.Headers.Authorization = new("Bearer", profile.BearerToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var reference = response.Headers.TryGetValues("X-Submission-Reference", out var values) ? values.FirstOrDefault() : null;
        if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("The authorized submission endpoint is temporarily unavailable.", null, response.StatusCode);
        if (!response.IsSuccessStatusCode)
            return new(false, false, (int)response.StatusCode, reference, "The authorized endpoint rejected the submission.");
        return new(true, response.StatusCode == System.Net.HttpStatusCode.Accepted, (int)response.StatusCode, reference, null);
    }
}

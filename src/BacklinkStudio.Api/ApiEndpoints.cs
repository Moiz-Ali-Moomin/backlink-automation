using BacklinkStudio.Api.Contracts;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Api;

public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapBacklinkStudioApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1").WithTags("BacklinkStudio");

        api.MapPost("/backlink-workflows", async (StartBacklinkWorkflowRequest request, HttpContext context,
            IBacklinkWorkflowService service, CancellationToken cancellationToken) =>
        {
            var headerKey = HttpActorContext.IdempotencyKey(context);
            var key = !string.IsNullOrWhiteSpace(request.ClientRequestKey) ? request.ClientRequestKey :
                !string.IsNullOrWhiteSpace(headerKey) ? headerKey : context.TraceIdentifier;
            var result = await service.StartAsync(new(request.ProjectId, request.SourceUrls, request.SourceImportId,
                request.Identities?.Select(value => new BacklinkWorkflowIdentityInput(value.Name, value.Email)).ToArray(),
                request.IdentityPoolId, request.Comments, request.TemplatePoolId, request.TargetUrl,
                request.GlobalConcurrency, request.PerDomainConcurrency, request.PerDomainDelayMilliseconds,
                request.MaximumAttempts, request.VerificationDelaySeconds, key), HttpActorContext.From(context),
                cancellationToken);
            return Results.Accepted($"/api/v1/backlink-workflows/{result.WorkflowId}", result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionsExecute);

        api.MapGet("/backlink-workflows/{id:guid}", async (Guid id, int? limit, string? cursor,
            IBacklinkWorkflowService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, new PageRequest(limit ?? 50, cursor), cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionsRead);

        api.MapPost("/owned-networks", async (CreateOwnedNetworkRequest request, HttpContext context, IOwnedNetworkService service, CancellationToken cancellationToken) =>
        {
            var command = new CreateOwnedNetworkCommand(request.ProjectId, request.Name, request.Description, request.OwnershipStatus,
                request.AutomationPermitted, request.Domains.Select(x => new OwnedNetworkDomainInput(x.Domain, x.MatchType, x.Enabled)).ToArray(),
                request.OptionalNetworkTag, request.DefaultIdentityPoolId, request.DefaultTemplatePoolId, request.MaxConcurrency,
                request.PerDomainConcurrency, request.PerDomainDelayMilliseconds, request.Enabled, HttpActorContext.IdempotencyKey(context));
            var result = await service.CreateAsync(command, HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/owned-networks/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.OwnedNetworksWrite);

        api.MapGet("/owned-networks/{id:guid}", async (Guid id, IOwnedNetworkService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.OwnedNetworksRead);

        api.MapGet("/owned-networks", async (Guid projectId, int? limit, string? cursor, IOwnedNetworkService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.OwnedNetworksRead);

        api.MapPut("/owned-networks/{id:guid}", async (Guid id, UpdateOwnedNetworkRequest request, HttpContext context, IOwnedNetworkService service, CancellationToken cancellationToken) =>
        {
            var command = new UpdateOwnedNetworkCommand(id, request.Name, request.Description, request.OwnershipStatus,
                request.AutomationPermitted, request.Domains.Select(x => new OwnedNetworkDomainInput(x.Domain, x.MatchType, x.Enabled)).ToArray(),
                request.OptionalNetworkTag, request.DefaultIdentityPoolId, request.DefaultTemplatePoolId, request.MaxConcurrency,
                request.PerDomainConcurrency, request.PerDomainDelayMilliseconds, request.Enabled, HttpActorContext.IdempotencyKey(context));
            return Results.Ok(await service.UpdateAsync(command, HttpActorContext.From(context), cancellationToken));
        }).RequireAuthorization(AuthorizationScopes.OwnedNetworksWrite);

        api.MapPost("/submission-sources/import", async (Guid projectId, Guid networkId, SubmissionSourceImportFormat format,
            string fileName, string? tag, HttpContext context, ISubmissionSourceService service, CancellationToken cancellationToken) =>
        {
            var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = 1L * 1_024 * 1_024 * 1_024;
            var result = await service.ImportAsync(new ImportSubmissionSourcesCommand(projectId, networkId, format, fileName, tag,
                HttpActorContext.IdempotencyKey(context)), context.Request.Body, HttpActorContext.From(context), cancellationToken);
            return Results.Accepted($"/api/v1/submission-source-imports/{result.ImportId}", result);
        }).DisableAntiforgery().RequireAuthorization(AuthorizationScopes.SubmissionSourcesWrite);

        api.MapGet("/submission-source-imports/{id:guid}", async (Guid id, ISubmissionSourceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetImportAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionSourcesRead);

        api.MapPost("/submission-sources/validate", async (ValidateSubmissionSourcesRequest request, HttpContext context, ISubmissionSourceService service, CancellationToken cancellationToken) =>
            Results.Accepted(value: await service.ValidateAsync(new ValidateSubmissionSourcesCommand(request.ProjectId,
                request.OwnedNetworkProfileId, request.MaximumSources, HttpActorContext.IdempotencyKey(context)),
                HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionSourcesWrite);

        api.MapGet("/submission-sources/{id:guid}", async (Guid id, ISubmissionSourceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionSourcesRead);

        api.MapGet("/submission-sources", async (Guid projectId, Guid? networkId, string? domain, SourcePlatform? platform,
            CmsType? cms, string? adapter, OwnershipStatus? ownershipStatus, bool? automationPermitted,
            TechnicalCompatibility? compatibility, SubmissionSourceValidationStatus? validationStatus, bool? enabled,
            string? tag, SubmissionStatus? previousSubmissionStatus, BacklinkStatus? previousVerificationStatus,
            int? limit, string? cursor, ISubmissionSourceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId,
                new SubmissionSourceFilter(networkId, domain, platform, cms, adapter, ownershipStatus, automationPermitted,
                    compatibility, validationStatus, enabled, tag, previousSubmissionStatus, previousVerificationStatus),
                new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionSourcesRead);

        api.MapPost("/identity-pools", async (CreateIdentityPoolRequest request, HttpContext context, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreatePoolAsync(new(request.ProjectId, request.Name, request.SelectionStrategy,
                request.EmailStrategy, request.EmailBaseAddress, request.CatchAllDomain, request.Enabled,
                HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/identity-pools/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesWrite);
        api.MapGet("/identity-pools/{id:guid}", async (Guid id, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
            await service.GetPoolAsync(id, cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound())
            .RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesRead);
        api.MapGet("/identity-pools", async (Guid projectId, int? limit, string? cursor, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPoolsAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesRead);
        api.MapPut("/identity-pools/{id:guid}", async (Guid id, UpdateIdentityPoolRequest request, HttpContext context, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdatePoolAsync(new(id, request.Name, request.SelectionStrategy, request.EmailStrategy,
                request.EmailBaseAddress, request.CatchAllDomain, request.Enabled, HttpActorContext.IdempotencyKey(context)),
                HttpActorContext.From(context), cancellationToken))).RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesWrite);
        api.MapPost("/identity-pools/{poolId:guid}/identities", async (Guid poolId, CreateIdentityRequest request, HttpContext context, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateIdentityAsync(new(poolId, request.DisplayName, request.Email, request.Website,
                request.Organization, request.Enabled, request.Weight, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/identities/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesWrite);
        api.MapGet("/identity-pools/{poolId:guid}/identities", async (Guid poolId, int? limit, string? cursor, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListIdentitiesAsync(poolId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesRead);
        api.MapPut("/identities/{id:guid}", async (Guid id, UpdateIdentityRequest request, HttpContext context, ISubmissionIdentityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateIdentityAsync(new(id, request.DisplayName, request.Email, request.Website,
                request.Organization, request.Enabled, request.Weight, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionIdentitiesWrite);

        api.MapPost("/submission-templates/pools", async (CreateTemplatePoolRequest request, HttpContext context, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreatePoolAsync(new(request.ProjectId, request.Name, request.TemplateType,
                request.SelectionStrategy, request.PlacementMethod, request.Enabled, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/submission-templates/pools/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionTemplatesWrite);
        api.MapGet("/submission-templates/pools/{id:guid}", async (Guid id, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
            await service.GetPoolAsync(id, cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound())
            .RequireAuthorization(AuthorizationScopes.SubmissionTemplatesRead);
        api.MapGet("/submission-templates/pools", async (Guid projectId, int? limit, string? cursor, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListPoolsAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionTemplatesRead);
        api.MapPut("/submission-templates/pools/{id:guid}", async (Guid id, UpdateTemplatePoolRequest request, HttpContext context, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdatePoolAsync(new(id, request.Name, request.TemplateType, request.SelectionStrategy,
                request.PlacementMethod, request.Enabled, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionTemplatesWrite);
        api.MapPost("/submission-templates/pools/{poolId:guid}/templates", async (Guid poolId, CreateSubmissionTemplateRequest request, HttpContext context, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateTemplateAsync(new(poolId, request.Name, request.Body, request.PrefixVariants,
                request.SuffixVariants, request.AnchorVariants, request.TargetUrlVariants, request.Enabled, request.Weight,
                HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/submission-templates/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.SubmissionTemplatesWrite);
        api.MapGet("/submission-templates/pools/{poolId:guid}/templates", async (Guid poolId, int? limit, string? cursor, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTemplatesAsync(poolId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionTemplatesRead);
        api.MapPut("/submission-templates/{id:guid}", async (Guid id, UpdateSubmissionTemplateRequest request, HttpContext context, ISubmissionTemplateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateTemplateAsync(new(id, request.Name, request.Body, request.PrefixVariants,
                request.SuffixVariants, request.AnchorVariants, request.TargetUrlVariants, request.Enabled, request.Weight,
                HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionTemplatesWrite);

        api.MapPost("/submissions/preview", async (SubmissionPreviewRequest request, ISubmissionPreviewService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PreviewAsync(new(request.ProjectId, request.SubmissionSourceId, request.CampaignId,
                request.IdentityPoolId, request.TemplatePoolId, request.TargetUrl, request.AttemptNumber), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionsRead);

        api.MapPost("/wordpress-site-profiles", async (CreateWordPressSiteProfileRequest request, HttpContext context, IWordPressSiteProfileService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(new(request.OwnedNetworkProfileId, request.Domain, request.ApiBaseUrl,
                request.CredentialReference, request.SubmissionMode, request.Enabled, HttpActorContext.IdempotencyKey(context)),
                HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/wordpress-site-profiles/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.Admin);
        api.MapGet("/wordpress-site-profiles/{id:guid}", async (Guid id, IWordPressSiteProfileService service, CancellationToken cancellationToken) =>
            await service.GetAsync(id, cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound())
            .RequireAuthorization(AuthorizationScopes.Admin);
        api.MapGet("/wordpress-site-profiles", async (Guid ownedNetworkProfileId, int? limit, string? cursor, IWordPressSiteProfileService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(ownedNetworkProfileId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.Admin);
        api.MapPut("/wordpress-site-profiles/{id:guid}", async (Guid id, UpdateWordPressSiteProfileRequest request,
            HttpContext context, IWordPressSiteProfileService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(new(id, request.ApiBaseUrl, request.CredentialReference,
                request.SubmissionMode, request.Enabled, HttpActorContext.IdempotencyKey(context)),
                HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.Admin);

        api.MapPost("/agent-credentials", async (CreateAgentCredentialRequest request, HttpContext context, IAgentCredentialService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(new CreateAgentCredentialCommand(request.Name, request.Scopes, request.ExpiresAt, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/agent-credentials/{result.Credential.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.Admin);

        api.MapGet("/agent-credentials/{id:guid}", async (Guid id, IAgentCredentialService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.Admin);

        api.MapGet("/agent-credentials", async (int? limit, string? cursor, IAgentCredentialService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.Admin);

        api.MapPost("/agent-credentials/{id:guid}/rotate", async (Guid id, RotateAgentCredentialRequest request, HttpContext context, IAgentCredentialService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RotateAsync(new RotateAgentCredentialCommand(id, request.ExpiresAt, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.Admin);

        api.MapPost("/agent-credentials/{id:guid}/revoke", async (Guid id, HttpContext context, IAgentCredentialService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.RevokeAsync(new RevokeAgentCredentialCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.Admin);

        api.MapPost("/projects", async (CreateProjectRequest request, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(new CreateProjectCommand(request.Name, request.PrimaryDomain, request.Description, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/projects/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/projects", async (int? limit, string? cursor, IProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapGet("/projects/{id:guid}", async (Guid id, IProjectService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapPost("/projects/{projectId:guid}/targets", async (Guid projectId, AddTargetRequest request, HttpContext context, IProjectService service, CancellationToken cancellationToken) =>
        {
            var result = await service.AddTargetAsync(new AddTargetCommand(projectId, request.Url, request.Label, request.Keywords, request.PreferredAnchor, request.Category, request.Priority, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/projects/{projectId}/targets/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/projects/{projectId:guid}/targets", async (Guid projectId, int? limit, string? cursor, IProjectService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListTargetsAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapGet("/projects/{projectId:guid}/policy", async (Guid projectId, IPolicyService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.GetAsync(projectId, cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapPut("/projects/{projectId:guid}/policy", async (Guid projectId, UpdatePolicyRequest request, HttpContext context, IPolicyService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(new UpdatePolicyCommand(projectId, request.AutomationEnabled, request.MinimumQualityScore, request.MaximumRiskScore, request.ManualReviewRequired, request.HourlyActionLimit, request.DailyActionLimit, request.PerDomainActionLimit, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/projects/{projectId:guid}/blocklist", async (Guid projectId, int? limit, string? cursor, IPolicyService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListBlocklistAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapPost("/projects/{projectId:guid}/blocklist", async (Guid projectId, AddBlocklistEntryRequest request, HttpContext context, IPolicyService service, CancellationToken cancellationToken) =>
        {
            var result = await service.AddBlocklistAsync(new AddBlocklistEntryCommand(projectId, request.MatchType, request.Value, request.Reason, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/projects/{projectId}/blocklist/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapPost("/projects/{projectId:guid}/candidates/import", async (Guid projectId, ImportCandidatesRequest request, HttpContext context, ICandidateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ImportAsync(new ImportCandidatesCommand(projectId, request.Urls, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/projects/{projectId:guid}/candidates", async (Guid projectId, int? limit, string? cursor, ICandidateService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CandidatesRead);

        api.MapGet("/candidates/{id:guid}", async (Guid id, ICandidateService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.CandidatesRead);

        api.MapPost("/discovery/jobs", async (StartDiscoveryRequest request, HttpContext context, IDiscoveryService service, CancellationToken cancellationToken) =>
        {
            var result = await service.StartAsync(new StartDiscoveryCommand(request.ProjectId, request.Provider, request.Query, request.Content, request.Urls, request.MaximumResults, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Accepted($"/api/v1/jobs/{result.JobId}", result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/discovery/runs/{id:guid}", async (Guid id, IDiscoveryService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetRunAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapGet("/discovery/runs", async (Guid projectId, int? limit, string? cursor, IDiscoveryService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListRunsAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapPost("/projects/{projectId:guid}/analysis-jobs", async (Guid projectId, HttpContext context, IOpportunityService service, CancellationToken cancellationToken) =>
        {
            var result = await service.StartAnalysisAsync(new StartAnalysisCommand(projectId, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Accepted($"/api/v1/jobs/{result.JobId}", result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/jobs/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapGet("/jobs", async (Guid projectId, int? limit, string? cursor, IJobService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsRead);

        api.MapPost("/jobs/{id:guid}/pause", async (Guid id, HttpContext context, IJobService service, CancellationToken cancellationToken) =>
            Results.Accepted($"/api/v1/jobs/{id}", await service.PauseAsync(new ChangeJobStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapPost("/jobs/{id:guid}/resume", async (Guid id, HttpContext context, IJobService service, CancellationToken cancellationToken) =>
            Results.Accepted($"/api/v1/jobs/{id}", await service.ResumeAsync(new ChangeJobStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapPost("/jobs/{id:guid}/redrive", async (Guid id, HttpContext context, IJobService service, CancellationToken cancellationToken) =>
            Results.Accepted($"/api/v1/jobs/{id}", await service.RedriveAsync(new ChangeJobStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ProjectsWrite);

        api.MapGet("/opportunities/{id:guid}", async (Guid id, IOpportunityService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.OpportunitiesRead);

        api.MapGet("/opportunities", async (Guid projectId, int? minimumQuality, int? maximumRisk, int? limit, string? cursor, IOpportunityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, minimumQuality, maximumRisk, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.OpportunitiesRead);

        api.MapGet("/opportunities/summary", async (Guid projectId, IOpportunityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.SummaryAsync(projectId, cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.OpportunitiesRead);

        api.MapPost("/opportunities/approve", async (ApproveOpportunitiesRequest request, HttpContext context, IOpportunityService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ApproveAsync(new ApproveOpportunitiesCommand(request.ProjectId, request.OpportunityIds, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.OpportunitiesApprove);

        api.MapPost("/campaigns", async (CreateCampaignRequest request, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(new CreateCampaignCommand(request.ProjectId, request.Name, request.ApprovalMode, request.AuthorizationProfileKey, request.AuthorizationReference, request.DailyActionLimit, request.TargetId, request.OpportunityIds, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/campaigns/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.CampaignsWrite);

        api.MapPost("/campaigns/owned-network", async (CreateOwnedNetworkCampaignRequest request, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateOwnedNetworkAsync(new(request.ProjectId, request.Name, request.OwnedNetworkProfileId,
                request.TargetUrl, request.IdentityPoolId, request.TemplatePoolId, request.GlobalConcurrency,
                request.PerDomainConcurrency, request.PerDomainDelayMilliseconds, request.MaximumAttempts,
                request.VerificationDelaySeconds, request.Mode, request.Domain, request.Platform, request.CmsType,
                request.TechnicalCompatibility, request.ValidationStatus, request.Tag, request.DailyActionLimit,
                HttpActorContext.IdempotencyKey(context), request.PreviousSubmissionStatus,
                request.PreviousVerificationStatus), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/campaigns/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.CampaignsWrite);

        api.MapGet("/campaigns/{id:guid}", async (Guid id, ICampaignService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.CampaignsRead);

        api.MapGet("/campaigns", async (Guid projectId, int? limit, string? cursor, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CampaignsRead);

        api.MapPut("/campaigns/{id:guid}", async (Guid id, UpdateCampaignRequest request, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(new UpdateCampaignCommand(id, request.Name, request.ApprovalMode, request.DailyActionLimit, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CampaignsWrite);

        api.MapPost("/campaigns/{id:guid}/start", async (Guid id, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Accepted($"/api/v1/campaigns/{id}", await service.StartAsync(new StartCampaignCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionsExecute);

        api.MapPost("/campaigns/{id:guid}/pause", async (Guid id, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PauseAsync(new ChangeCampaignStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CampaignsExecute);

        api.MapPost("/campaigns/{id:guid}/resume", async (Guid id, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ResumeAsync(new ChangeCampaignStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CampaignsExecute);

        api.MapPost("/campaigns/{id:guid}/stop", async (Guid id, HttpContext context, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.StopAsync(new ChangeCampaignStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.CampaignsExecute);

        api.MapGet("/campaigns/{id:guid}/submissions", async (Guid id, int? limit, string? cursor, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListSubmissionsAsync(id, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionsRead);

        api.MapGet("/submissions/{id:guid}", async (Guid id, ICampaignService service, CancellationToken cancellationToken) =>
            await service.GetSubmissionAsync(id, cancellationToken) is { } result ? Results.Ok(result) : Results.NotFound())
            .RequireAuthorization(AuthorizationScopes.SubmissionsRead);

        api.MapGet("/submissions/{id:guid}/attempts", async (Guid id, ICampaignService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAttemptsAsync(id, cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SubmissionsRead);

        api.MapPost("/verification/jobs", async (StartVerificationRequest request, HttpContext context, IVerificationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.StartAsync(new StartVerificationCommand(request.BacklinkId, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Accepted($"/api/v1/jobs/{result.JobId}", result);
        }).RequireAuthorization(AuthorizationScopes.VerificationExecute);

        api.MapGet("/backlinks/{id:guid}", async (Guid id, IVerificationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.BacklinksRead);

        api.MapGet("/backlinks", async (Guid projectId, BacklinkStudio.Domain.BacklinkStatus? status, int? limit, string? cursor, IVerificationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, status, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.BacklinksRead);

        api.MapGet("/backlinks/{id:guid}/verification-history", async (Guid id, int? limit, string? cursor, IVerificationService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.HistoryAsync(id, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.BacklinksRead);

        api.MapPost("/schedules", async (CreateScheduleRequest request, HttpContext context, IScheduleService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(new CreateScheduleCommand(request.ProjectId, request.Name, request.Action, request.Timing, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Created($"/api/v1/schedules/{result.Id}", result);
        }).RequireAuthorization(AuthorizationScopes.SchedulesWrite);

        api.MapGet("/schedules/{id:guid}", async (Guid id, IScheduleService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.SchedulesRead);

        api.MapGet("/schedules", async (Guid projectId, int? limit, string? cursor, IScheduleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SchedulesRead);

        api.MapPut("/schedules/{id:guid}", async (Guid id, UpdateScheduleRequest request, HttpContext context, IScheduleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.UpdateAsync(new UpdateScheduleCommand(id, request.Name, request.Action, request.Timing, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SchedulesWrite);

        api.MapPost("/schedules/{id:guid}/pause", async (Guid id, HttpContext context, IScheduleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.PauseAsync(new ChangeScheduleStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SchedulesWrite);

        api.MapPost("/schedules/{id:guid}/resume", async (Guid id, HttpContext context, IScheduleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ResumeAsync(new ChangeScheduleStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SchedulesWrite);

        api.MapDelete("/schedules/{id:guid}", async (Guid id, HttpContext context, IScheduleService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.DeleteAsync(new ChangeScheduleStateCommand(id, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.SchedulesWrite);

        api.MapPost("/reports", async (GenerateReportRequest request, HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GenerateAsync(new GenerateReportCommand(request.ProjectId, request.CampaignId, request.Kind, request.Format, HttpActorContext.IdempotencyKey(context)), HttpActorContext.From(context), cancellationToken);
            return Results.Accepted($"/api/v1/jobs/{result.JobId}", result);
        }).RequireAuthorization(AuthorizationScopes.ReportsWrite);

        api.MapGet("/reports/{id:guid}", async (Guid id, IReportService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return result is null ? Results.NotFound() : Results.Ok(result);
        }).RequireAuthorization(AuthorizationScopes.ReportsRead);

        api.MapGet("/reports", async (Guid projectId, int? limit, string? cursor, IReportService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(projectId, new PageRequest(limit ?? 50, cursor), cancellationToken)))
            .RequireAuthorization(AuthorizationScopes.ReportsRead);

        api.MapGet("/reports/{id:guid}/download", async (Guid id, HttpContext context, IReportService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DownloadAsync(id, cancellationToken);
            if (result is null) return Results.NotFound();
            context.Response.Headers.ETag = $"\"{result.Sha256}\"";
            context.Response.Headers.ContentLength = result.Length;
            return Results.File(result.Content, result.ContentType, result.FileName, enableRangeProcessing: true);
        }).RequireAuthorization(AuthorizationScopes.ReportsRead);

        return endpoints;
    }
}

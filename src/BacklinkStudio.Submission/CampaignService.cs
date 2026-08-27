using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Submission;

public sealed class CampaignService(
    IProjectRepository projects,
    IOpportunityRepository opportunities,
    ISubmissionRepository submissions,
    IOwnedNetworkCampaignRepository ownedCampaigns,
    IOwnedNetworkRepository ownedNetworks,
    ISubmissionContentRepository submissionContent,
    IPolicyRepository policies,
    ISubmissionAuthorizationResolver authorizations,
    IOwnedNetworkExecutionAuthorizer ownedNetworkAuthorizer,
    IPolicyEvaluator policyEvaluator,
    IJobQueue jobs,
    IIdempotencyStore idempotency,
    IAuditSink audit,
    IStudioUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICampaignService
{
    public async Task<CampaignDto> CreateAsync(CreateCampaignCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "campaign_create");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<CampaignDto>(existing, hash);
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var target = await projects.GetTargetAsync(command.TargetId, cancellationToken) ?? throw new ResourceNotFoundException("Target", command.TargetId);
        if (target.ProjectId != command.ProjectId || !target.Enabled) throw new ValidationException("The enabled target must belong to the campaign project.");
        var ids = command.OpportunityIds.Distinct().ToArray();
        if (ids.Length is < 1 or > 100) throw new ValidationException("Between 1 and 100 opportunity IDs are required.");
        var selected = await opportunities.GetTrackedAsync(command.ProjectId, ids, cancellationToken);
        if (selected.Count != ids.Length) throw new ValidationException("Every opportunity must belong to the campaign project.");
        var authorization = authorizations.Resolve(command.AuthorizationProfileKey);
        if (selected.Any(x => !string.Equals(x.Domain, authorization.SourceDomain, StringComparison.OrdinalIgnoreCase) || !authorization.AllowedTypes.Contains(x.Type)))
            throw new UnauthorizedAccessException("Every opportunity must match the named profile's source domain and allowed types.");
        if (command.ApprovalMode == CampaignApprovalMode.Manual && selected.Any(x => x.AutomationStatus != AutomationStatus.Approved))
            throw new ValidationException("Manual campaigns may contain only explicitly approved opportunities.");
        await RequireAutomaticPolicyAsync(command.ProjectId, command.ApprovalMode, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var campaign = new Campaign(command.ProjectId, command.Name, command.ApprovalMode, authorization.ProfileKey, command.AuthorizationReference, command.DailyActionLimit, now);
        submissions.AddCampaign(campaign);
        submissions.AddCampaignTarget(new CampaignTarget(campaign.Id, target.Id, now));
        foreach (var item in selected) submissions.AddCampaignOpportunity(new CampaignOpportunity(campaign.Id, item.Id, item.AutomationStatus == AutomationStatus.Approved, now));
        var result = campaign.ToDto();
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "campaign.create", command.ProjectId, campaign.Id, null, actor.RequestId, $"campaignId={campaign.Id};opportunities={selected.Count};profile={authorization.ProfileKey};mode={campaign.ApprovalMode}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<CampaignDto> CreateOwnedNetworkAsync(CreateOwnedNetworkCampaignCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "campaign_create_owned_network");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<CampaignDto>(existing, hash);
        _ = await projects.GetAsync(command.ProjectId, cancellationToken) ?? throw new ResourceNotFoundException("Project", command.ProjectId);
        var network = await ownedNetworks.GetAsync(command.OwnedNetworkProfileId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", command.OwnedNetworkProfileId);
        if (network.ProjectId != command.ProjectId) throw new ValidationException("The owned network must belong to the campaign project.");
        if (command.Mode == OwnedNetworkCampaignMode.AutomaticOwnedNetwork)
        {
            var authorization = await ownedNetworkAuthorizer.AuthorizeProfileAsync(command.ProjectId, network.Id,
                cancellationToken);
            if (!authorization.Allowed)
                throw new UnauthorizedAccessException("Automatic campaigns require an authorized owned network.");
        }
        var identityPoolId = command.IdentityPoolId ?? network.DefaultIdentityPoolId
            ?? throw new ValidationException("The campaign requires an identity pool or an owned-network default.");
        var templatePoolId = command.TemplatePoolId ?? network.DefaultTemplatePoolId
            ?? throw new ValidationException("The campaign requires a template pool or an owned-network default.");
        var identityPool = await submissionContent.GetIdentityPoolAsync(identityPoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", identityPoolId);
        var templatePool = await submissionContent.GetTemplatePoolAsync(templatePoolId, false, cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", templatePoolId);
        if (identityPool.ProjectId != command.ProjectId || templatePool.ProjectId != command.ProjectId || !identityPool.Enabled || !templatePool.Enabled)
            throw new ValidationException("Enabled identity and template pools must belong to the campaign project.");
        if (command.GlobalConcurrency > network.MaxConcurrency || command.PerDomainConcurrency > network.PerDomainConcurrency ||
            command.PerDomainDelayMilliseconds < network.PerDomainDelayMilliseconds)
            throw new ValidationException("Campaign rate settings must not exceed the owned network's concurrency or minimum-delay policy.");
        var now = timeProvider.GetUtcNow();
        var approvalMode = command.Mode == OwnedNetworkCampaignMode.AutomaticOwnedNetwork ? CampaignApprovalMode.Automatic : CampaignApprovalMode.Manual;
        var campaign = new Campaign(command.ProjectId, command.Name, approvalMode, "owned-network", network.Id.ToString("N"), command.DailyActionLimit, now);
        var configuration = new OwnedNetworkCampaignConfiguration(campaign.Id, command.ProjectId, network.Id, command.TargetUrl,
            identityPoolId, templatePoolId, command.GlobalConcurrency, command.PerDomainConcurrency, command.PerDomainDelayMilliseconds,
            command.MaximumAttempts, command.VerificationDelaySeconds, command.Mode, command.Domain, command.Platform, command.CmsType,
            command.TechnicalCompatibility, command.ValidationStatus, command.Tag, now,
            command.PreviousSubmissionStatus, command.PreviousVerificationStatus);
        submissions.AddCampaign(campaign);
        ownedCampaigns.Add(configuration);
        var result = campaign.ToDto() with { OwnedNetwork = configuration.ToDto() };
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "campaign.create_owned_network", command.ProjectId,
            campaign.Id, null, actor.RequestId, $"campaignId={campaign.Id};ownedNetworkId={network.Id};mode={configuration.Mode}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<CampaignDto?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var campaign = await submissions.GetCampaignAsync(id, false, cancellationToken);
        if (campaign is null) return null;
        var configuration = await ownedCampaigns.GetAsync(id, false, cancellationToken);
        return campaign.ToDto() with { OwnedNetwork = configuration?.ToDto() };
    }

    public async Task<PageResult<CampaignDto>> ListAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await submissions.ListCampaignsAsync(projectId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id)) : null;
        var mapped = new List<CampaignDto>(values.Length);
        foreach (var value in values)
        {
            var configuration = await ownedCampaigns.GetAsync(value.Id, false, cancellationToken);
            mapped.Add(value.ToDto() with { OwnedNetwork = configuration?.ToDto() });
        }
        return new(mapped, next);
    }

    public async Task<CampaignDto> UpdateAsync(UpdateCampaignCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "campaign_update");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<CampaignDto>(existing, hash);
        var campaign = await submissions.GetCampaignAsync(command.CampaignId, true, cancellationToken) ?? throw new ResourceNotFoundException("Campaign", command.CampaignId);
        await RequireAutomaticPolicyAsync(campaign.ProjectId, command.ApprovalMode, cancellationToken);
        var now = timeProvider.GetUtcNow();
        campaign.Update(command.Name, command.ApprovalMode, command.DailyActionLimit, now);
        var result = campaign.ToDto();
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "campaign.update", campaign.ProjectId, campaign.Id, null, actor.RequestId, $"campaignId={campaign.Id};mode={campaign.ApprovalMode};dailyLimit={campaign.DailyActionLimit}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<StartCampaignResult> StartAsync(StartCampaignCommand command, ActorContext actor, CancellationToken cancellationToken)
    {
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, "campaign_start");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<StartCampaignResult>(existing, hash);
        var campaign = await submissions.GetCampaignAsync(command.CampaignId, true, cancellationToken) ?? throw new ResourceNotFoundException("Campaign", command.CampaignId);
        if (campaign.Status is not (CampaignStatus.Draft or CampaignStatus.Running))
        {
            throw new ConflictException($"Campaign in state {campaign.Status} cannot be started.");
        }

        var ownedConfiguration = await ownedCampaigns.GetAsync(campaign.Id, true, cancellationToken);
        if (ownedConfiguration is not null)
            return await StartOwnedNetworkAsync(campaign, ownedConfiguration, actor, scope, key, hash, cancellationToken);

        var authorization = authorizations.Resolve(campaign.AuthorizationProfileKey);
        var policy = await policies.GetAsync(campaign.ProjectId, false, cancellationToken) ?? throw new ValidationException("Project policy is missing.");
        if (campaign.ApprovalMode == CampaignApprovalMode.Automatic && (!policy.AutomationEnabled || policy.ManualReviewRequired))
            throw new ValidationException("Automatic campaigns require project automation to be enabled with manual review disabled.");
        var blocklist = await policies.ListEnabledBlocklistAsync(campaign.ProjectId, cancellationToken);
        var selections = await submissions.ListCampaignOpportunitiesAsync(campaign.Id, true, cancellationToken);
        var opportunityRows = await opportunities.GetTrackedAsync(campaign.ProjectId, selections.Select(x => x.OpportunityId).ToArray(), cancellationToken);
        var byId = opportunityRows.ToDictionary(x => x.Id);
        var now = timeProvider.GetUtcNow();
        var hourly = await submissions.CountSuccessfulActionsAsync(campaign.ProjectId, null, null, now.AddHours(-1), cancellationToken);
        var daily = await submissions.CountSuccessfulActionsAsync(campaign.ProjectId, null, null, now.AddDays(-1), cancellationToken);
        var campaignDaily = await submissions.CountSuccessfulActionsAsync(campaign.ProjectId, campaign.Id, null, now.AddDays(-1), cancellationToken);
        var domainCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var jobIds = new List<Guid>();
        var approvalRequired = 0;
        var rejected = 0;
        var manualActionRequired = 0;
        foreach (var selection in selections)
        {
            if (await submissions.SubmissionExistsAsync(selection.Id, cancellationToken)) continue;
            var opportunity = byId[selection.OpportunityId];
            if (!selection.ExplicitlyApproved && opportunity.AutomationStatus == AutomationStatus.Approved) selection.Approve();
            if (!domainCounts.TryGetValue(opportunity.Domain, out var domainActions))
            {
                domainActions = await submissions.CountSuccessfulActionsAsync(campaign.ProjectId, null, opportunity.Domain, now.AddDays(-1), cancellationToken);
                domainCounts.Add(opportunity.Domain, domainActions);
            }
            var sourceAuthorized = string.Equals(authorization.SourceDomain, opportunity.Domain, StringComparison.OrdinalIgnoreCase) && authorization.AllowedTypes.Contains(opportunity.Type);
            var duplicate = await submissions.HasSuccessfulSubmissionAsync(campaign.ProjectId, opportunity.Id, Guid.Empty, cancellationToken);
            var decision = campaignDaily + jobIds.Count >= campaign.DailyActionLimit
                ? new PolicyEvaluationResult(PolicyDecision.Rejected, ["The campaign daily action limit has been reached."])
                : policyEvaluator.Evaluate(policy, blocklist, new PolicyEvaluationContext(opportunity.Type, opportunity.SourceUrl, opportunity.Domain, opportunity.QualityScore, opportunity.RiskScore, campaign.ApprovalMode, sourceAuthorized, selection.ExplicitlyApproved, duplicate, hourly + jobIds.Count, daily + jobIds.Count, domainActions));
            if (decision.Decision != PolicyDecision.Allowed)
            {
                if (decision.Decision == PolicyDecision.ApprovalRequired) approvalRequired++;
                else if (decision.Decision == PolicyDecision.ManualActionRequired) manualActionRequired++;
                else rejected++;
                continue;
            }
            var persistent = new PersistentJob(JobType.Submission, campaign.ProjectId, campaign.Id, JsonSerializer.Serialize(new SubmissionJobPayload(1)), 0, now, 3, actor.RequestId, $"campaign:{campaign.Id}:opportunity:{opportunity.Id}", now, opportunity.Domain);
            await jobs.EnqueueAsync(persistent, cancellationToken);
            submissions.AddSubmission(new SubmissionJob(campaign.ProjectId, campaign.Id, selection.Id, persistent.Id, now));
            jobIds.Add(persistent.Id);
            domainCounts[opportunity.Domain] = domainActions + 1;
        }
        if (jobIds.Count > 0) campaign.Start(now);
        var status = jobIds.Count > 0 ? "queued" : approvalRequired + manualActionRequired > 0 ? "approvalRequired" : "rejected";
        var result = new StartCampaignResult(campaign.Id, jobIds.Count, jobIds, approvalRequired, rejected, manualActionRequired, status);
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "campaign.start", campaign.ProjectId, campaign.Id, null, actor.RequestId, $"campaignId={campaign.Id};jobsQueued={jobIds.Count};approvalRequired={approvalRequired};rejected={rejected};manualActionRequired={manualActionRequired}", jobIds.Count > 0 ? "succeeded" : status, actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    private async Task<StartCampaignResult> StartOwnedNetworkAsync(Campaign campaign, OwnedNetworkCampaignConfiguration configuration,
        ActorContext actor, string scope, string key, string hash, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (configuration.Mode != OwnedNetworkCampaignMode.AutomaticOwnedNetwork)
        {
            var pending = new StartCampaignResult(campaign.Id, 0, [], 1, 0, 0,
                configuration.Mode == OwnedNetworkCampaignMode.Preview ? "preview" : "approvalRequired");
            idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(pending), now));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return pending;
        }
        var authorization = await ownedNetworkAuthorizer.AuthorizeProfileAsync(campaign.ProjectId,
            configuration.OwnedNetworkProfileId, cancellationToken);
        if (!authorization.Allowed)
            throw new UnauthorizedAccessException("The owned network is no longer eligible for automatic execution.");
        var existingExpansion = await jobs.FindByIdempotencyAsync(campaign.ProjectId, JobType.OwnedNetworkCampaignExpansion,
            $"campaign:{campaign.Id}:expand", cancellationToken);
        var expansion = existingExpansion ?? new PersistentJob(JobType.OwnedNetworkCampaignExpansion, campaign.ProjectId, campaign.Id,
            JsonSerializer.Serialize(new OwnedNetworkCampaignExpansionPayload(1, campaign.Id)), 10, now, 5, actor.RequestId,
            $"campaign:{campaign.Id}:expand", now);
        if (existingExpansion is null) await jobs.EnqueueAsync(expansion, cancellationToken);
        if (campaign.Status == CampaignStatus.Draft) campaign.Start(now);
        var result = new StartCampaignResult(campaign.Id, 1, [expansion.Id], 0, 0, 0, "expansionQueued");
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, "campaign.start_owned_network", campaign.ProjectId,
            campaign.Id, expansion.Id, actor.RequestId, $"campaignId={campaign.Id};ownedNetworkId={configuration.OwnedNetworkProfileId}",
            "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public Task<CampaignDto> PauseAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, CampaignTransition.Pause, cancellationToken);

    public Task<CampaignDto> ResumeAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, CampaignTransition.Resume, cancellationToken);

    public Task<CampaignDto> StopAsync(ChangeCampaignStateCommand command, ActorContext actor, CancellationToken cancellationToken) =>
        ChangeStateAsync(command, actor, CampaignTransition.Stop, cancellationToken);

    private async Task<CampaignDto> ChangeStateAsync(ChangeCampaignStateCommand command, ActorContext actor, CampaignTransition transition, CancellationToken cancellationToken)
    {
        var operation = transition.ToString().ToLowerInvariant();
        var key = Idempotency.RequireKey(command.IdempotencyKey);
        var scope = Idempotency.Scope(actor, $"campaign_{operation}");
        var hash = Idempotency.HashRequest(command with { IdempotencyKey = string.Empty });
        var existing = await idempotency.FindAsync(scope, key, cancellationToken);
        if (existing is not null) return Idempotency.ReadExisting<CampaignDto>(existing, hash);
        var campaign = await submissions.GetCampaignAsync(command.CampaignId, true, cancellationToken) ?? throw new ResourceNotFoundException("Campaign", command.CampaignId);
        var now = timeProvider.GetUtcNow();
        int affectedJobs;
        if (transition == CampaignTransition.Pause)
        {
            campaign.Pause(now);
            affectedJobs = await submissions.PauseCampaignJobsAsync(campaign.Id, now, cancellationToken);
        }
        else if (transition == CampaignTransition.Resume)
        {
            campaign.Resume(now);
            affectedJobs = await submissions.ResumeCampaignJobsAsync(campaign.Id, now, cancellationToken);
        }
        else
        {
            campaign.Stop(now);
            affectedJobs = await submissions.StopCampaignJobsAsync(campaign.Id, now, cancellationToken);
        }
        var result = campaign.ToDto();
        idempotency.Add(new IdempotencyRecord(scope, key, hash, "campaign", campaign.Id, JsonSerializer.Serialize(result), now));
        audit.Append(new AuditEvent(actor.ActorType, actor.ActorId, actor.CredentialId, $"campaign.{operation}", campaign.ProjectId, campaign.Id, null, actor.RequestId, $"campaignId={campaign.Id};jobs={affectedJobs}", "succeeded", actor.SourceAddress, now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<SubmissionJobDto?> GetSubmissionAsync(Guid submissionJobId, CancellationToken cancellationToken) =>
        (await submissions.GetSubmissionAsync(submissionJobId, cancellationToken))?.ToDto();

    public async Task<PageResult<SubmissionJobDto>> ListSubmissionsAsync(Guid campaignId, PageRequest page, CancellationToken cancellationToken)
    {
        var rows = await submissions.ListSubmissionsAsync(campaignId, CursorCodec.Decode(page.Cursor), page.BoundedLimit + 1, cancellationToken);
        var values = rows.Take(page.BoundedLimit).ToArray();
        var next = rows.Count > page.BoundedLimit && values.Length > 0 ? CursorCodec.Encode(new PageCursor(values[^1].CreatedAt, values[^1].Id)) : null;
        return new(values.Select(x => x.ToDto()).ToArray(), next);
    }

    public async Task<IReadOnlyList<SubmissionAttemptDto>> ListAttemptsAsync(Guid submissionJobId, CancellationToken cancellationToken) =>
        (await submissions.ListAttemptsAsync(submissionJobId, 100, cancellationToken)).Select(x => x.ToDto()).ToArray();

    private async Task RequireAutomaticPolicyAsync(Guid projectId, CampaignApprovalMode mode, CancellationToken cancellationToken)
    {
        if (mode != CampaignApprovalMode.Automatic) return;
        var policy = await policies.GetAsync(projectId, false, cancellationToken) ?? throw new ValidationException("Project policy is missing.");
        if (!policy.AutomationEnabled || policy.ManualReviewRequired)
            throw new ValidationException("Automatic campaigns require project automation to be enabled with manual review disabled.");
    }

    private enum CampaignTransition
    {
        Pause,
        Resume,
        Stop
    }
}

public sealed record SubmissionJobPayload(int Version);
public sealed record OwnedNetworkCampaignExpansionPayload(int Version, Guid CampaignId);
public sealed record OwnedNetworkSubmissionJobPayload(int Version, Guid CampaignId, Guid SubmissionSourceId,
    Guid IdentityPoolId, Guid TemplatePoolId, string TargetUrl, BacklinkPlacementMethod PlacementMethod,
    int VerificationDelaySeconds);

internal static class OwnedNetworkCampaignMappings
{
    public static OwnedNetworkCampaignConfigurationDto ToDto(this OwnedNetworkCampaignConfiguration value) =>
        new(value.CampaignId, value.OwnedNetworkProfileId, value.TargetUrl, value.IdentityPoolId, value.TemplatePoolId,
            value.GlobalConcurrency, value.PerDomainConcurrency, value.PerDomainDelayMilliseconds, value.MaximumAttempts,
            value.VerificationDelaySeconds, value.Mode, value.Domain, value.Platform, value.CmsType,
            value.TechnicalCompatibility, value.ValidationStatus, value.Tag, value.SourcesQueued, value.ExpansionCompleted,
            value.PreviousSubmissionStatus, value.PreviousVerificationStatus);
}

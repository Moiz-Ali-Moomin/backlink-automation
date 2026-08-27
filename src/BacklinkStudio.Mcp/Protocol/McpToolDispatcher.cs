using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;

namespace BacklinkStudio.Mcp.Protocol;

public sealed class McpToolDispatcher(
    ISystemHealthService health,
    IProjectService projects,
    ICandidateService candidates,
    IOpportunityService opportunities,
    IJobService jobs,
    IPolicyService policies,
    IDiscoveryService discovery,
    ICampaignService campaigns,
    IVerificationService verification,
    IScheduleService schedules,
    IReportService reports,
    IAgentCredentialService agentCredentials,
    IOwnedNetworkService? ownedNetworks = null,
    ISubmissionSourceService? submissionSources = null,
    ISubmissionIdentityService? submissionIdentities = null,
    ISubmissionTemplateService? submissionTemplates = null,
    ISubmissionPreviewService? submissionPreview = null,
    IWordPressSiteProfileService? wordpressSiteProfiles = null,
    IBacklinkWorkflowService? backlinkWorkflows = null)
{
    private const int MaximumInlineImportBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private static readonly string[] BlocklistMatchTypes = ["domain", "host", "url", "urlPrefix", "submissionSource", "ownedNetworkProfile", "campaign"];
    private static readonly string[] DiscoveryProviders = ["manualUrl", "txtImport", "csvImport", "sitemap", "serper", "competitorBacklinkImport"];
    private static readonly string[] CampaignApprovalModes = ["manual", "semiAutomatic", "automatic"];
    private static readonly string[] BacklinkStatuses = ["pendingVerification", "verified", "missing", "lost", "error"];
    private static readonly string[] SubmissionStatuses = ["queued", "claimed", "processing", "running", "submitted",
        "pendingModeration", "approved", "rejected", "duplicate", "failed", "reconciliationRequired", "cancelled",
        "manualActionRequired"];
    private static readonly string[] ScheduleActions = ["discovery", "analysis", "verification"];
    private static readonly string[] ScheduleRecurrences = ["oneTime", "interval", "daily", "weekly", "monthly", "cron"];
    private static readonly string[] DaysOfWeek = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
    private static readonly string[] ReportKinds = ["campaignPerformance", "backlinkInventory"];
    private static readonly string[] ReportFormats = ["json", "csv", "xlsx", "html"];
    private static readonly string[] OwnershipStatuses = ["unverified", "owned", "controlled", "partnerControlled", "explicitPermission"];
    private static readonly string[] OwnedNetworkMatchTypes = ["exactHost", "exactDomain", "subdomainOf"];
    private static readonly string[] SourceImportFormats = ["txt", "csv"];
    private static readonly string[] SourcePlatforms = ["unknown", "wordPress", "ownedProperty", "genericWeb"];
    private static readonly string[] CmsTypes = ["unknown", "wordPress", "other"];
    private static readonly string[] TechnicalCompatibilities = ["unknown", "compatible", "fallbackCandidate", "incompatible", "manualActionRequired"];
    private static readonly string[] SourceValidationStatuses = ["pending", "queued", "running", "valid", "invalid", "error"];
    private static readonly string[] PoolSelectionStrategies = ["roundRobin", "deterministicRandom", "weightedDeterministicRandom"];
    private static readonly string[] IdentityEmailStrategies = ["fixed", "aliasPool", "plusAddressing", "catchAll", "preCreatedSynthetic"];
    private static readonly string[] TemplateTypes = ["wordPressComment", "ownedProperty", "genericOwnedNetwork"];
    private static readonly string[] PlacementMethods = ["websiteField", "commentBody"];
    private static readonly string[] OwnedCampaignModes = ["preview", "manualApproval", "automaticOwnedNetwork"];
    private static readonly string[] WordPressSubmissionModes = ["directApi", "authenticatedIntegration", "standardComment", "fallbackComment", "controlledBrowser", "manualActionRequired"];
    private static readonly string[] WorkflowIdentityRequiredFields = ["name", "email"];
    public static IReadOnlyList<McpTool> Tools { get; } = BuildTools();

    public async Task<McpResponse?> DispatchAsync(McpRequest request, ActorContext actor, IReadOnlySet<string> scopes, CancellationToken cancellationToken)
    {
        if (request.JsonRpc != "2.0" || string.IsNullOrWhiteSpace(request.Method))
        {
            return McpResponse.Failure(request.Id, -32600, "Invalid Request");
        }

        if (request.Method == "notifications/initialized")
        {
            return null;
        }

        if (request.Method == "initialize")
        {
            return McpResponse.Success(request.Id, new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { tools = new { listChanged = false } },
                serverInfo = new { name = "BacklinkStudio Engine", version = "0.1.0" }
            });
        }

        if (request.Method == "ping")
        {
            return McpResponse.Success(request.Id, new { });
        }

        if (request.Method == "tools/list")
        {
            var visible = Tools.Where(tool => HasScope(scopes, tool.RequiredScope)).Select(tool => new { tool.Name, tool.Description, tool.InputSchema });
            return McpResponse.Success(request.Id, new { tools = visible });
        }

        if (request.Method != "tools/call")
        {
            return McpResponse.Failure(request.Id, -32601, "Method not found");
        }

        try
        {
            var parameters = request.Params ?? throw new ValidationException("Tool call parameters are required.");
            var name = RequiredString(parameters, "name");
            var arguments = parameters.TryGetProperty("arguments", out var value) ? value : EmptyObject();
            var tool = Tools.SingleOrDefault(x => x.Name == name);
            if (tool is null)
            {
                return McpResponse.Failure(request.Id, -32602, "Unknown tool.");
            }
            if (!HasScope(scopes, tool.RequiredScope))
            {
                return ToolError(request.Id, "forbidden", "The credential lacks the required scope.");
            }

            var result = await CallToolAsync(name, arguments, actor, cancellationToken);
            return McpResponse.Success(request.Id, new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(result, JsonOptions) } }, isError = false });
        }
        catch (Exception exception) when (exception is BacklinkStudioException or DomainRuleException or JsonException or FormatException or UnauthorizedAccessException)
        {
            return ToolError(request.Id, "invalid_request", exception.Message);
        }
    }

    private async Task<object> CallToolAsync(string name, JsonElement args, ActorContext actor, CancellationToken cancellationToken) => name switch
    {
        "system_health" => await health.CheckAsync(cancellationToken),
        "backlink_workflow_start" => await BacklinkWorkflows.StartAsync(new(
            RequiredGuid(args, "projectId"), StringArray(args, "sourceUrls"), OptionalGuid(args, "sourceImportId"),
            OptionalArray<BacklinkWorkflowIdentityInput>(args, "identities"), OptionalGuid(args, "identityPoolId"),
            StringArray(args, "comments"), OptionalGuid(args, "templatePoolId"), RequiredString(args, "targetUrl"),
            OptionalInt(args, "globalConcurrency"), OptionalInt(args, "perDomainConcurrency"),
            OptionalInt(args, "perDomainDelayMilliseconds"), OptionalInt(args, "maximumAttempts"),
            OptionalInt(args, "verificationDelaySeconds"), OptionalString(args, "clientRequestKey") ?? actor.RequestId), actor,
            cancellationToken),
        "backlink_workflow_get" => await BacklinkWorkflows.GetAsync(RequiredGuid(args, "workflowId"), Page(args),
            cancellationToken) ?? throw new ResourceNotFoundException("BacklinkWorkflow", RequiredGuid(args, "workflowId")),
        "agent_credentials_list" => await agentCredentials.ListAsync(Page(args), cancellationToken),
        "agent_credentials_get" => await agentCredentials.GetAsync(RequiredGuid(args, "credentialId"), cancellationToken) ?? throw new ResourceNotFoundException("AgentCredential", RequiredGuid(args, "credentialId")),
        "agent_credential_create" => await agentCredentials.CreateAsync(new CreateAgentCredentialCommand(RequiredString(args, "name"), RequiredStringArray(args, "scopes"), OptionalDateTimeOffset(args, "expiresAt"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "agent_credential_rotate" => await agentCredentials.RotateAsync(new RotateAgentCredentialCommand(RequiredGuid(args, "credentialId"), OptionalDateTimeOffset(args, "expiresAt"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "agent_credential_revoke" => await agentCredentials.RevokeAsync(new RevokeAgentCredentialCommand(RequiredGuid(args, "credentialId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "projects_list" => await projects.ListAsync(Page(args), cancellationToken),
        "projects_get" => await projects.GetAsync(RequiredGuid(args, "projectId"), cancellationToken) ?? throw new ResourceNotFoundException("Project", RequiredGuid(args, "projectId")),
        "project_create" => await projects.CreateAsync(new CreateProjectCommand(RequiredString(args, "name"), RequiredString(args, "primaryDomain"), OptionalString(args, "description"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "targets_list" => await projects.ListTargetsAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "target_add" => await projects.AddTargetAsync(new AddTargetCommand(RequiredGuid(args, "projectId"), RequiredString(args, "url"), OptionalString(args, "label"), StringArray(args, "keywords"), OptionalString(args, "preferredAnchor"), OptionalString(args, "category"), OptionalInt(args, "priority") ?? 50, RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "owned_network_create" => await OwnedNetworks.CreateAsync(new CreateOwnedNetworkCommand(
            RequiredGuid(args, "projectId"), RequiredString(args, "name"), OptionalString(args, "description"),
            RequiredEnum<OwnershipStatus>(args, "ownershipStatus"), RequiredBool(args, "automationPermitted"),
            RequiredArray<OwnedNetworkDomainInput>(args, "domains"), OptionalString(args, "optionalNetworkTag"),
            OptionalGuid(args, "defaultIdentityPoolId"), OptionalGuid(args, "defaultTemplatePoolId"),
            OptionalInt(args, "maxConcurrency") ?? 100, OptionalInt(args, "perDomainConcurrency") ?? 2,
            OptionalInt(args, "perDomainDelayMilliseconds") ?? 1_000, OptionalBool(args, "enabled") ?? true,
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "owned_network_get" => await OwnedNetworks.GetAsync(RequiredGuid(args, "ownedNetworkId"), cancellationToken)
            ?? throw new ResourceNotFoundException("OwnedNetworkProfile", RequiredGuid(args, "ownedNetworkId")),
        "owned_network_list" => await OwnedNetworks.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "owned_network_update" => await OwnedNetworks.UpdateAsync(new UpdateOwnedNetworkCommand(
            RequiredGuid(args, "ownedNetworkId"), RequiredString(args, "name"), OptionalString(args, "description"),
            RequiredEnum<OwnershipStatus>(args, "ownershipStatus"), RequiredBool(args, "automationPermitted"),
            RequiredArray<OwnedNetworkDomainInput>(args, "domains"), OptionalString(args, "optionalNetworkTag"),
            OptionalGuid(args, "defaultIdentityPoolId"), OptionalGuid(args, "defaultTemplatePoolId"),
            RequiredInt(args, "maxConcurrency"), RequiredInt(args, "perDomainConcurrency"),
            RequiredInt(args, "perDomainDelayMilliseconds"), RequiredBool(args, "enabled"),
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "submission_sources_import" => await ImportSourcesAsync(args, actor, cancellationToken),
        "submission_sources_validate" => await SubmissionSources.ValidateAsync(new ValidateSubmissionSourcesCommand(
            RequiredGuid(args, "projectId"), OptionalGuid(args, "ownedNetworkId"), OptionalInt(args, "maximumSources") ?? 100_000,
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "submission_source_import_get" => await SubmissionSources.GetImportAsync(RequiredGuid(args, "importId"), cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionSourceImport", RequiredGuid(args, "importId")),
        "submission_sources_get" => await SubmissionSources.GetAsync(RequiredGuid(args, "submissionSourceId"), cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionSource", RequiredGuid(args, "submissionSourceId")),
        "submission_sources_list" => await SubmissionSources.ListAsync(RequiredGuid(args, "projectId"), new SubmissionSourceFilter(
            OptionalGuid(args, "ownedNetworkId"), OptionalString(args, "domain"), OptionalEnum<SourcePlatform>(args, "platform"),
            OptionalEnum<CmsType>(args, "cms"), OptionalString(args, "adapter"), OptionalEnum<OwnershipStatus>(args, "ownershipStatus"),
            OptionalBool(args, "automationPermitted"), OptionalEnum<TechnicalCompatibility>(args, "technicalCompatibility"),
            OptionalEnum<SubmissionSourceValidationStatus>(args, "validationStatus"), OptionalBool(args, "enabled"), OptionalString(args, "tag"),
            OptionalEnum<SubmissionStatus>(args, "previousSubmissionStatus"), OptionalEnum<BacklinkStatus>(args, "previousVerificationStatus")),
            Page(args), cancellationToken),
        "identity_pool_create" => await SubmissionIdentities.CreatePoolAsync(new(RequiredGuid(args, "projectId"),
            RequiredString(args, "name"), RequiredEnum<PoolSelectionStrategy>(args, "selectionStrategy"),
            RequiredEnum<IdentityEmailStrategy>(args, "emailStrategy"), OptionalString(args, "emailBaseAddress"),
            OptionalString(args, "catchAllDomain"), OptionalBool(args, "enabled") ?? true,
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "identity_pool_get" => await SubmissionIdentities.GetPoolAsync(RequiredGuid(args, "identityPoolId"), cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", RequiredGuid(args, "identityPoolId")),
        "identity_pool_list" => await SubmissionIdentities.ListPoolsAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "identity_pool_update" => await SubmissionIdentities.UpdatePoolAsync(new(RequiredGuid(args, "identityPoolId"),
            RequiredString(args, "name"), RequiredEnum<PoolSelectionStrategy>(args, "selectionStrategy"),
            RequiredEnum<IdentityEmailStrategy>(args, "emailStrategy"), OptionalString(args, "emailBaseAddress"),
            OptionalString(args, "catchAllDomain"), RequiredBool(args, "enabled"),
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "identity_create" => await SubmissionIdentities.CreateIdentityAsync(new(RequiredGuid(args, "identityPoolId"),
            RequiredString(args, "displayName"), RequiredString(args, "email"), OptionalString(args, "website"),
            OptionalString(args, "organization"), OptionalBool(args, "enabled") ?? true, OptionalInt(args, "weight") ?? 1,
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "identity_update" => await SubmissionIdentities.UpdateIdentityAsync(new(RequiredGuid(args, "identityId"),
            RequiredString(args, "displayName"), RequiredString(args, "email"), OptionalString(args, "website"),
            OptionalString(args, "organization"), RequiredBool(args, "enabled"), RequiredInt(args, "weight"),
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "template_pool_create" => await SubmissionTemplates.CreatePoolAsync(new(RequiredGuid(args, "projectId"),
            RequiredString(args, "name"), RequiredEnum<SubmissionTemplateType>(args, "templateType"),
            RequiredEnum<PoolSelectionStrategy>(args, "selectionStrategy"), RequiredEnum<BacklinkPlacementMethod>(args, "placementMethod"),
            OptionalBool(args, "enabled") ?? true, RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "template_pool_get" => await SubmissionTemplates.GetPoolAsync(RequiredGuid(args, "templatePoolId"), cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", RequiredGuid(args, "templatePoolId")),
        "template_pool_list" => await SubmissionTemplates.ListPoolsAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "template_pool_update" => await SubmissionTemplates.UpdatePoolAsync(new(RequiredGuid(args, "templatePoolId"),
            RequiredString(args, "name"), RequiredEnum<SubmissionTemplateType>(args, "templateType"),
            RequiredEnum<PoolSelectionStrategy>(args, "selectionStrategy"), RequiredEnum<BacklinkPlacementMethod>(args, "placementMethod"),
            RequiredBool(args, "enabled"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "template_create" => await SubmissionTemplates.CreateTemplateAsync(new(RequiredGuid(args, "templatePoolId"),
            RequiredString(args, "name"), RequiredString(args, "body"), StringArray(args, "prefixVariants"),
            StringArray(args, "suffixVariants"), StringArray(args, "anchorVariants"), StringArray(args, "targetUrlVariants"),
            OptionalBool(args, "enabled") ?? true, OptionalInt(args, "weight") ?? 1, RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "template_update" => await SubmissionTemplates.UpdateTemplateAsync(new(RequiredGuid(args, "templateId"),
            RequiredString(args, "name"), RequiredString(args, "body"), StringArray(args, "prefixVariants"),
            StringArray(args, "suffixVariants"), StringArray(args, "anchorVariants"), StringArray(args, "targetUrlVariants"),
            RequiredBool(args, "enabled"), RequiredInt(args, "weight"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "submission_preview" => await SubmissionPreview.PreviewAsync(new(RequiredGuid(args, "projectId"),
            RequiredGuid(args, "submissionSourceId"), OptionalGuid(args, "campaignId"), OptionalGuid(args, "identityPoolId"),
            OptionalGuid(args, "templatePoolId"), RequiredString(args, "targetUrl"), OptionalInt(args, "attemptNumber") ?? 1), cancellationToken),
        "wordpress_site_profile_create" => await WordPressSiteProfiles.CreateAsync(new(
            RequiredGuid(args, "ownedNetworkId"), RequiredString(args, "domain"), RequiredString(args, "apiBaseUrl"),
            OptionalString(args, "credentialReference"), RequiredEnum<WordPressSubmissionMode>(args, "submissionMode"),
            OptionalBool(args, "enabled") ?? true, RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "wordpress_site_profile_list" => await WordPressSiteProfiles.ListAsync(RequiredGuid(args, "ownedNetworkId"), Page(args), cancellationToken),
        "wordpress_site_profile_update" => await WordPressSiteProfiles.UpdateAsync(new(RequiredGuid(args, "wordpressSiteProfileId"),
            RequiredString(args, "apiBaseUrl"), OptionalString(args, "credentialReference"),
            RequiredEnum<WordPressSubmissionMode>(args, "submissionMode"), RequiredBool(args, "enabled"),
            RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "policy_get" => await policies.GetAsync(RequiredGuid(args, "projectId"), cancellationToken),
        "policy_update" => await policies.UpdateAsync(new UpdatePolicyCommand(RequiredGuid(args, "projectId"), RequiredBool(args, "automationEnabled"), RequiredInt(args, "minimumQualityScore"), RequiredInt(args, "maximumRiskScore"), RequiredBool(args, "manualReviewRequired"), RequiredInt(args, "hourlyActionLimit"), RequiredInt(args, "dailyActionLimit"), RequiredInt(args, "perDomainActionLimit"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "blocklist_list" => await policies.ListBlocklistAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "blocklist_add" => await policies.AddBlocklistAsync(new AddBlocklistEntryCommand(RequiredGuid(args, "projectId"), RequiredEnum<BlocklistMatchType>(args, "matchType"), RequiredString(args, "value"), RequiredString(args, "reason"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "candidates_list" => await candidates.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "candidates_get" => await candidates.GetAsync(RequiredGuid(args, "candidateId"), cancellationToken) ?? throw new ResourceNotFoundException("Candidate", RequiredGuid(args, "candidateId")),
        "candidates_import" => await candidates.ImportAsync(new ImportCandidatesCommand(RequiredGuid(args, "projectId"), StringArray(args, "urls") ?? [], RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "discovery_start" => await discovery.StartAsync(new StartDiscoveryCommand(RequiredGuid(args, "projectId"), RequiredEnum<DiscoveryProviderKind>(args, "provider"), OptionalString(args, "query"), OptionalString(args, "content"), StringArray(args, "urls"), OptionalInt(args, "maximumResults") ?? 1_000, RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "discovery_runs_list" => await discovery.ListRunsAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "discovery_run_get" => await discovery.GetRunAsync(RequiredGuid(args, "discoveryRunId"), cancellationToken) ?? throw new ResourceNotFoundException("DiscoveryRun", RequiredGuid(args, "discoveryRunId")),
        "opportunities_get" => await opportunities.GetAsync(RequiredGuid(args, "opportunityId"), cancellationToken) ?? throw new ResourceNotFoundException("Opportunity", RequiredGuid(args, "opportunityId")),
        "opportunities_list" => await opportunities.ListAsync(RequiredGuid(args, "projectId"), OptionalInt(args, "minimumQuality"), OptionalInt(args, "maximumRisk"), Page(args), cancellationToken),
        "opportunities_summary" => await opportunities.SummaryAsync(RequiredGuid(args, "projectId"), cancellationToken),
        "opportunities_approve" => await opportunities.ApproveAsync(new ApproveOpportunitiesCommand(RequiredGuid(args, "projectId"), GuidArray(args, "opportunityIds"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "campaign_create" => await CreateCampaignAsync(args, actor, cancellationToken),
        "campaign_get" => await campaigns.GetAsync(RequiredGuid(args, "campaignId"), cancellationToken) ?? throw new ResourceNotFoundException("Campaign", RequiredGuid(args, "campaignId")),
        "campaigns_get" => await campaigns.GetAsync(RequiredGuid(args, "campaignId"), cancellationToken) ?? throw new ResourceNotFoundException("Campaign", RequiredGuid(args, "campaignId")),
        "campaigns_status" => await campaigns.GetAsync(RequiredGuid(args, "campaignId"), cancellationToken) ?? throw new ResourceNotFoundException("Campaign", RequiredGuid(args, "campaignId")),
        "campaigns_list" => await campaigns.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "campaign_update" => await campaigns.UpdateAsync(new UpdateCampaignCommand(RequiredGuid(args, "campaignId"), RequiredString(args, "name"), RequiredEnum<CampaignApprovalMode>(args, "approvalMode"), RequiredInt(args, "dailyActionLimit"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "campaign_start" => await campaigns.StartAsync(new StartCampaignCommand(RequiredGuid(args, "campaignId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "campaign_pause" => await campaigns.PauseAsync(new ChangeCampaignStateCommand(RequiredGuid(args, "campaignId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "campaign_resume" => await campaigns.ResumeAsync(new ChangeCampaignStateCommand(RequiredGuid(args, "campaignId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "campaign_stop" => await campaigns.StopAsync(new ChangeCampaignStateCommand(RequiredGuid(args, "campaignId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "submissions_list" => await campaigns.ListSubmissionsAsync(RequiredGuid(args, "campaignId"), Page(args), cancellationToken),
        "submission_get" => await campaigns.GetSubmissionAsync(RequiredGuid(args, "submissionJobId"), cancellationToken)
            ?? throw new ResourceNotFoundException("SubmissionJob", RequiredGuid(args, "submissionJobId")),
        "submission_attempts" => await campaigns.ListAttemptsAsync(RequiredGuid(args, "submissionJobId"), cancellationToken),
        "verification_start" => await verification.StartAsync(new StartVerificationCommand(RequiredGuid(args, "backlinkId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "backlinks_get" => await verification.GetAsync(RequiredGuid(args, "backlinkId"), cancellationToken) ?? throw new ResourceNotFoundException("Backlink", RequiredGuid(args, "backlinkId")),
        "backlinks_list" => await verification.ListAsync(RequiredGuid(args, "projectId"), OptionalEnum<BacklinkStatus>(args, "status"), Page(args), cancellationToken),
        "verification_history" => await verification.HistoryAsync(RequiredGuid(args, "backlinkId"), Page(args), cancellationToken),
        "schedules_get" => await schedules.GetAsync(RequiredGuid(args, "scheduleId"), cancellationToken) ?? throw new ResourceNotFoundException("Schedule", RequiredGuid(args, "scheduleId")),
        "schedules_list" => await schedules.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "schedule_create" => await schedules.CreateAsync(new CreateScheduleCommand(RequiredGuid(args, "projectId"), RequiredString(args, "name"), RequiredObject<ScheduleActionConfiguration>(args, "action"), RequiredObject<ScheduleTiming>(args, "timing"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "schedule_update" => await schedules.UpdateAsync(new UpdateScheduleCommand(RequiredGuid(args, "scheduleId"), RequiredString(args, "name"), RequiredObject<ScheduleActionConfiguration>(args, "action"), RequiredObject<ScheduleTiming>(args, "timing"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "schedule_pause" => await schedules.PauseAsync(new ChangeScheduleStateCommand(RequiredGuid(args, "scheduleId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "schedule_resume" => await schedules.ResumeAsync(new ChangeScheduleStateCommand(RequiredGuid(args, "scheduleId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "schedule_delete" => await schedules.DeleteAsync(new ChangeScheduleStateCommand(RequiredGuid(args, "scheduleId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "reports_get" => await reports.GetAsync(RequiredGuid(args, "reportId"), cancellationToken) ?? throw new ResourceNotFoundException("Report", RequiredGuid(args, "reportId")),
        "reports_list" => await reports.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "report_generate" => await reports.GenerateAsync(new GenerateReportCommand(RequiredGuid(args, "projectId"), OptionalGuid(args, "campaignId"), RequiredEnum<ReportKind>(args, "kind"), RequiredEnum<ReportFormat>(args, "format"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "reports_generate" => await reports.GenerateAsync(new GenerateReportCommand(RequiredGuid(args, "projectId"), OptionalGuid(args, "campaignId"), RequiredEnum<ReportKind>(args, "kind"), RequiredEnum<ReportFormat>(args, "format"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "analysis_start" => await opportunities.StartAnalysisAsync(new StartAnalysisCommand(RequiredGuid(args, "projectId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "jobs_get" => await jobs.GetAsync(RequiredGuid(args, "jobId"), cancellationToken) ?? throw new ResourceNotFoundException("Job", RequiredGuid(args, "jobId")),
        "jobs_list" => await jobs.ListAsync(RequiredGuid(args, "projectId"), Page(args), cancellationToken),
        "jobs_pause" => await jobs.PauseAsync(new ChangeJobStateCommand(RequiredGuid(args, "jobId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "jobs_resume" => await jobs.ResumeAsync(new ChangeJobStateCommand(RequiredGuid(args, "jobId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        "jobs_redrive" => await jobs.RedriveAsync(new ChangeJobStateCommand(RequiredGuid(args, "jobId"), RequiredString(args, "clientRequestKey")), actor, cancellationToken),
        _ => throw new ValidationException("Unknown tool.")
    };

    private IOwnedNetworkService OwnedNetworks => ownedNetworks ?? throw new InvalidOperationException("Owned network service is unavailable.");
    private ISubmissionSourceService SubmissionSources => submissionSources ?? throw new InvalidOperationException("Submission source service is unavailable.");
    private ISubmissionIdentityService SubmissionIdentities => submissionIdentities ?? throw new InvalidOperationException("Submission identity service is unavailable.");
    private ISubmissionTemplateService SubmissionTemplates => submissionTemplates ?? throw new InvalidOperationException("Submission template service is unavailable.");
    private ISubmissionPreviewService SubmissionPreview => submissionPreview ?? throw new InvalidOperationException("Submission preview service is unavailable.");
    private IWordPressSiteProfileService WordPressSiteProfiles => wordpressSiteProfiles ?? throw new InvalidOperationException("WordPress site profile service is unavailable.");
    private IBacklinkWorkflowService BacklinkWorkflows => backlinkWorkflows ?? throw new InvalidOperationException("Backlink workflow service is unavailable.");

    private async Task<SubmissionSourceImportAcceptedDto> ImportSourcesAsync(JsonElement args, ActorContext actor, CancellationToken cancellationToken)
    {
        var inlineContent = RequiredString(args, "content");
        if (Encoding.UTF8.GetByteCount(inlineContent) > MaximumInlineImportBytes)
        {
            throw new ValidationException("Inline MCP imports must not exceed 8 MiB; use the streaming REST or CLI import for larger files.");
        }

        var content = Encoding.UTF8.GetBytes(inlineContent);
        await using var stream = new MemoryStream(content, writable: false);
        return await SubmissionSources.ImportAsync(new ImportSubmissionSourcesCommand(
            RequiredGuid(args, "projectId"), RequiredGuid(args, "ownedNetworkId"),
            RequiredEnum<SubmissionSourceImportFormat>(args, "format"), RequiredString(args, "fileName"),
            OptionalString(args, "tag"), RequiredString(args, "clientRequestKey")), stream, actor, cancellationToken);
    }

    private Task<CampaignDto> CreateCampaignAsync(JsonElement args, ActorContext actor, CancellationToken cancellationToken)
    {
        if (OptionalGuid(args, "ownedNetworkId") is { } ownedNetworkId)
            return campaigns.CreateOwnedNetworkAsync(new(RequiredGuid(args, "projectId"), RequiredString(args, "name"),
                ownedNetworkId, RequiredString(args, "targetUrl"), OptionalGuid(args, "identityPoolId"),
                OptionalGuid(args, "templatePoolId"), OptionalInt(args, "globalConcurrency") ?? 100,
                OptionalInt(args, "perDomainConcurrency") ?? 2, OptionalInt(args, "perDomainDelayMilliseconds") ?? 1_000,
                OptionalInt(args, "maximumAttempts") ?? 3, OptionalInt(args, "verificationDelaySeconds") ?? 3_600,
                OptionalEnum<OwnedNetworkCampaignMode>(args, "mode") ?? OwnedNetworkCampaignMode.AutomaticOwnedNetwork,
                OptionalString(args, "domain"), OptionalEnum<SourcePlatform>(args, "platform"), OptionalEnum<CmsType>(args, "cms"),
                OptionalEnum<TechnicalCompatibility>(args, "technicalCompatibility") ?? TechnicalCompatibility.Compatible,
                OptionalEnum<SubmissionSourceValidationStatus>(args, "validationStatus") ?? SubmissionSourceValidationStatus.Valid,
                OptionalString(args, "tag"), OptionalInt(args, "dailyActionLimit") ?? 100_000,
                RequiredString(args, "clientRequestKey"), OptionalEnum<SubmissionStatus>(args, "previousSubmissionStatus"),
                OptionalEnum<BacklinkStatus>(args, "previousVerificationStatus")), actor, cancellationToken);
        return campaigns.CreateAsync(new(RequiredGuid(args, "projectId"), RequiredString(args, "name"),
            RequiredEnum<CampaignApprovalMode>(args, "approvalMode"), RequiredString(args, "authorizationProfileKey"),
            RequiredString(args, "authorizationReference"), RequiredInt(args, "dailyActionLimit"), RequiredGuid(args, "targetId"),
            GuidArray(args, "opportunityIds"), RequiredString(args, "clientRequestKey")), actor, cancellationToken);
    }

    private static McpResponse ToolError(JsonElement? id, string code, string message) =>
        McpResponse.Success(id, new { content = new[] { new { type = "text", text = JsonSerializer.Serialize(new { code, message }, JsonOptions) } }, isError = true });

    private static bool HasScope(IReadOnlySet<string> scopes, string required) => required.Length == 0 || scopes.Contains(AuthorizationScopes.Admin) || scopes.Contains(required);
    private static PageRequest Page(JsonElement args) => new(OptionalInt(args, "limit") ?? 50, OptionalString(args, "cursor"));
    private static Guid RequiredGuid(JsonElement args, string name) => Guid.TryParse(RequiredString(args, name), out var value) ? value : throw new ValidationException($"{name} must be a UUID.");
    private static Guid? OptionalGuid(JsonElement args, string name) => OptionalString(args, name) is { } value ? Guid.TryParse(value, out var parsed) ? parsed : throw new ValidationException($"{name} must be a UUID.") : null;
    private static string RequiredString(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! : throw new ValidationException($"{name} is required.");
    private static string? OptionalString(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? OptionalInt(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
    private static int RequiredInt(JsonElement args, string name) => OptionalInt(args, name) ?? throw new ValidationException($"{name} is required and must be an integer.");
    private static bool RequiredBool(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ValidationException($"{name} is required and must be a boolean.");
    private static bool? OptionalBool(JsonElement args, string name) => args.TryGetProperty(name, out var value)
        ? value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : throw new ValidationException($"{name} must be a boolean.")
        : null;
    private static T RequiredEnum<T>(JsonElement args, string name) where T : struct, Enum => Enum.TryParse<T>(RequiredString(args, name), true, out var value) ? value : throw new ValidationException($"{name} is invalid.");
    private static T? OptionalEnum<T>(JsonElement args, string name) where T : struct, Enum => OptionalString(args, name) is { } value ? Enum.TryParse<T>(value, true, out var parsed) ? parsed : throw new ValidationException($"{name} is invalid.") : null;
    private static string[]? StringArray(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(x => x.GetString() ?? throw new ValidationException($"{name} values must be strings.")).ToArray() : null;
    private static string[] RequiredStringArray(JsonElement args, string name) => StringArray(args, name) ?? throw new ValidationException($"{name} is required.");
    private static Guid[] GuidArray(JsonElement args, string name) => StringArray(args, name)?.Select(x => Guid.TryParse(x, out var value) ? value : throw new ValidationException($"{name} values must be UUIDs.")).ToArray() ?? throw new ValidationException($"{name} is required.");
    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement args, string name) => OptionalString(args, name) is { } value
        ? DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : throw new ValidationException($"{name} must be an ISO-8601 timestamp.")
        : null;
    private static T RequiredObject<T>(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
        ? JsonSerializer.Deserialize<T>(value.GetRawText(), JsonOptions) ?? throw new ValidationException($"{name} is invalid.")
        : throw new ValidationException($"{name} is required and must be an object.");
    private static T[] RequiredArray<T>(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
        ? JsonSerializer.Deserialize<T[]>(value.GetRawText(), JsonOptions) ?? throw new ValidationException($"{name} is invalid.")
        : throw new ValidationException($"{name} is required and must be an array.");
    private static T[]? OptionalArray<T>(JsonElement args, string name) => args.TryGetProperty(name, out var value)
        ? value.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<T[]>(value.GetRawText(), JsonOptions) ?? throw new ValidationException($"{name} is invalid.")
            : throw new ValidationException($"{name} must be an array.")
        : null;
    private static JsonElement EmptyObject()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static IReadOnlyList<McpTool> BuildTools()
    {
        static object Schema(object properties, params string[] required) => new { type = "object", properties, required, additionalProperties = false };
        // MCP clients validate inputSchema before loading a tool, and JSON Schema requires
        // "description" to be a string. Omit the keyword entirely when no text is supplied
        // rather than serializing "description": null, which makes clients drop the tool.
        static object String(string? description = null) => description is null
            ? new Dictionary<string, object> { ["type"] = "string" }
            : new Dictionary<string, object> { ["type"] = "string", ["description"] = description };
        static object Integer(int minimum = 1, int maximum = 100) => new { type = "integer", minimum, maximum };
        static object Boolean() => new { type = "boolean" };
        var ownedNetworkDomain = new
        {
            type = "object",
            properties = new { domain = String("DNS host name"), matchType = new { type = "string", @enum = OwnedNetworkMatchTypes }, enabled = Boolean() },
            required = new[] { "domain", "matchType" },
            additionalProperties = false
        };
        var scheduleAction = new
        {
            type = "object",
            properties = new
            {
                actionType = new { type = "string", @enum = ScheduleActions },
                discovery = new
                {
                    type = "object",
                    properties = new { provider = new { type = "string", @enum = DiscoveryProviders }, query = String(), content = String(), urls = new { type = "array", maxItems = 5000, items = String() }, maximumResults = Integer(1, 5000) },
                    additionalProperties = false
                },
                verification = new
                {
                    type = "object",
                    properties = new { backlinkId = String("UUID"), status = new { type = "string", @enum = BacklinkStatuses }, maximumBacklinks = Integer(1, 500) },
                    additionalProperties = false
                }
            },
            required = new[] { "actionType" },
            additionalProperties = false
        };
        var scheduleTiming = new
        {
            type = "object",
            properties = new
            {
                recurrenceType = new { type = "string", @enum = ScheduleRecurrences },
                startsAt = String("ISO-8601 UTC timestamp"),
                oneTimeAt = String("ISO-8601 UTC timestamp"),
                intervalMinutes = Integer(1, 525600),
                timeOfDayUtc = String("HH:mm:ss"),
                dayOfWeek = new { type = "string", @enum = DaysOfWeek },
                dayOfMonth = Integer(1, 31),
                cronExpression = String("Standard five-field cron expression evaluated in UTC")
            },
            required = new[] { "recurrenceType", "startsAt" },
            additionalProperties = false
        };
        return
        [
            new("system_health", "Check BacklinkStudio and PostgreSQL readiness.", Schema(new { }), string.Empty),
            new("backlink_workflow_start", "Start the durable one-click backlink workflow. Provide sources, identities, comments, and one target URL; authorization, validation, adapter selection, submission, and independent verification remain internal.",
                Schema(new
                {
                    projectId = String("UUID"),
                    sourceUrls = new { type = "array", minItems = 1, maxItems = 10_000, items = String() },
                    sourceImportId = String("Optional UUID instead of sourceUrls"),
                    identities = new { type = "array", minItems = 1, maxItems = 1_000, items = new { type = "object", properties = new { name = String(), email = String() }, required = WorkflowIdentityRequiredFields, additionalProperties = false } },
                    identityPoolId = String("Optional UUID instead of identities"),
                    comments = new { type = "array", minItems = 1, maxItems = 1_000, items = String() },
                    templatePoolId = String("Optional UUID instead of comments"),
                    targetUrl = String(), globalConcurrency = Integer(1, 10_000),
                    perDomainConcurrency = Integer(1, 1_000),
                    perDomainDelayMilliseconds = Integer(0, 86_400_000), maximumAttempts = Integer(1, 20),
                    verificationDelaySeconds = Integer(0, 2_592_000), clientRequestKey = String()
                }, "projectId", "targetUrl"), AuthorizationScopes.SubmissionsExecute),
            new("backlink_workflow_get", "Get simple aggregate workflow status and bounded keyset-paginated per-source results.",
                Schema(new { workflowId = String("UUID"), limit = Integer(), cursor = String() }, "workflowId"),
                AuthorizationScopes.SubmissionsRead),
            new("agent_credentials_list", "List redacted agent credentials with bounded pagination. API keys are never returned.", Schema(new { limit = Integer(), cursor = String() }), AuthorizationScopes.Admin),
            new("agent_credentials_get", "Get redacted metadata for one agent credential.", Schema(new { credentialId = String("UUID") }, "credentialId"), AuthorizationScopes.Admin),
            new("agent_credential_create", "Create a scoped agent credential. The API key is displayed only in this first response.", Schema(new { name = String(), scopes = new { type = "array", minItems = 1, maxItems = 50, items = String() }, expiresAt = String("Optional ISO-8601 timestamp, no more than five years away"), clientRequestKey = String() }, "name", "scopes", "clientRequestKey"), AuthorizationScopes.Admin),
            new("agent_credential_rotate", "Revoke and replace an agent credential. The replacement API key is displayed only once.", Schema(new { credentialId = String("UUID"), expiresAt = String("Optional replacement expiry"), clientRequestKey = String() }, "credentialId", "clientRequestKey"), AuthorizationScopes.Admin),
            new("agent_credential_revoke", "Revoke an agent credential immediately.", Schema(new { credentialId = String("UUID"), clientRequestKey = String() }, "credentialId", "clientRequestKey"), AuthorizationScopes.Admin),
            new("projects_list", "List projects with bounded pagination.", Schema(new { limit = Integer(), cursor = String() }), AuthorizationScopes.ProjectsRead),
            new("projects_get", "Get one project.", Schema(new { projectId = String("UUID") }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("project_create", "Create a project idempotently.", Schema(new { name = String(), primaryDomain = String(), description = String(), clientRequestKey = String() }, "name", "primaryDomain", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("targets_list", "List project targets.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("target_add", "Add a project target idempotently.", Schema(new { projectId = String("UUID"), url = String(), label = String(), keywords = new { type = "array", items = String() }, preferredAnchor = String(), category = String(), priority = Integer(0), clientRequestKey = String() }, "projectId", "url", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("owned_network_create", "Register a bounded owned or controlled network and deterministic domain rules.", Schema(new { projectId = String("UUID"), name = String(), description = String(), ownershipStatus = new { type = "string", @enum = OwnershipStatuses }, automationPermitted = Boolean(), domains = new { type = "array", minItems = 1, maxItems = 1000, items = ownedNetworkDomain }, optionalNetworkTag = String(), defaultIdentityPoolId = String("Optional UUID"), defaultTemplatePoolId = String("Optional UUID"), maxConcurrency = Integer(1, 10_000), perDomainConcurrency = Integer(1, 1_000), perDomainDelayMilliseconds = Integer(0, 86_400_000), enabled = Boolean(), clientRequestKey = String() }, "projectId", "name", "ownershipStatus", "automationPermitted", "domains", "clientRequestKey"), AuthorizationScopes.OwnedNetworksWrite),
            new("owned_network_get", "Get one owned network and its normalized domain rules.", Schema(new { ownedNetworkId = String("UUID") }, "ownedNetworkId"), AuthorizationScopes.OwnedNetworksRead),
            new("owned_network_list", "List owned networks with bounded keyset pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.OwnedNetworksRead),
            new("owned_network_update", "Replace an owned network definition and domain rules idempotently.", Schema(new { ownedNetworkId = String("UUID"), name = String(), description = String(), ownershipStatus = new { type = "string", @enum = OwnershipStatuses }, automationPermitted = Boolean(), domains = new { type = "array", minItems = 1, maxItems = 1000, items = ownedNetworkDomain }, optionalNetworkTag = String(), defaultIdentityPoolId = String("Optional UUID"), defaultTemplatePoolId = String("Optional UUID"), maxConcurrency = Integer(1, 10_000), perDomainConcurrency = Integer(1, 1_000), perDomainDelayMilliseconds = Integer(0, 86_400_000), enabled = Boolean(), clientRequestKey = String() }, "ownedNetworkId", "name", "ownershipStatus", "automationPermitted", "domains", "maxConcurrency", "perDomainConcurrency", "perDomainDelayMilliseconds", "enabled", "clientRequestKey"), AuthorizationScopes.OwnedNetworksWrite),
            new("submission_sources_import", "Stage UTF-8 TXT or CSV source content in bounded PostgreSQL chunks and queue durable import processing.", Schema(new { projectId = String("UUID"), ownedNetworkId = String("UUID"), format = new { type = "string", @enum = SourceImportFormats }, fileName = String(), tag = String(), content = new { type = "string", maxLength = MaximumInlineImportBytes, description = "UTF-8 TXT or CSV content up to 8 MiB; use the streaming REST or CLI path for larger files" }, clientRequestKey = String() }, "projectId", "ownedNetworkId", "format", "fileName", "content", "clientRequestKey"), AuthorizationScopes.SubmissionSourcesWrite),
            new("submission_sources_validate", "Queue bounded durable validation jobs for cataloged sources.", Schema(new { projectId = String("UUID"), ownedNetworkId = String("Optional UUID"), maximumSources = Integer(1, 1_000_000), clientRequestKey = String() }, "projectId", "clientRequestKey"), AuthorizationScopes.SubmissionSourcesWrite),
            new("submission_source_import_get", "Get durable source-import status and counters.", Schema(new { importId = String("UUID") }, "importId"), AuthorizationScopes.SubmissionSourcesRead),
            new("submission_sources_get", "Get one cataloged submission source.", Schema(new { submissionSourceId = String("UUID") }, "submissionSourceId"), AuthorizationScopes.SubmissionSourcesRead),
            new("submission_sources_list", "List the source catalog with bounded filters and keyset pagination.", Schema(new { projectId = String("UUID"), ownedNetworkId = String("Optional UUID"), domain = String(), platform = new { type = "string", @enum = SourcePlatforms }, cms = new { type = "string", @enum = CmsTypes }, adapter = String(), ownershipStatus = new { type = "string", @enum = OwnershipStatuses }, automationPermitted = Boolean(), technicalCompatibility = new { type = "string", @enum = TechnicalCompatibilities }, validationStatus = new { type = "string", @enum = SourceValidationStatuses }, enabled = Boolean(), tag = String(), previousSubmissionStatus = new { type = "string", @enum = SubmissionStatuses }, previousVerificationStatus = new { type = "string", @enum = BacklinkStatuses }, limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.SubmissionSourcesRead),
            new("identity_pool_create", "Create an idempotent deterministic identity pool without sending email or creating external accounts.", Schema(new { projectId = String("UUID"), name = String(), selectionStrategy = new { type = "string", @enum = PoolSelectionStrategies }, emailStrategy = new { type = "string", @enum = IdentityEmailStrategies }, emailBaseAddress = String(), catchAllDomain = String(), enabled = Boolean(), clientRequestKey = String() }, "projectId", "name", "selectionStrategy", "emailStrategy", "clientRequestKey"), AuthorizationScopes.SubmissionIdentitiesWrite),
            new("identity_pool_get", "Get one submission identity pool.", Schema(new { identityPoolId = String("UUID") }, "identityPoolId"), AuthorizationScopes.SubmissionIdentitiesRead),
            new("identity_pool_list", "List identity pools with bounded keyset pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.SubmissionIdentitiesRead),
            new("identity_pool_update", "Update deterministic identity-pool selection and server-side email strategy settings idempotently.", Schema(new { identityPoolId = String("UUID"), name = String(), selectionStrategy = new { type = "string", @enum = PoolSelectionStrategies }, emailStrategy = new { type = "string", @enum = IdentityEmailStrategies }, emailBaseAddress = String(), catchAllDomain = String(), enabled = Boolean(), clientRequestKey = String() }, "identityPoolId", "name", "selectionStrategy", "emailStrategy", "enabled", "clientRequestKey"), AuthorizationScopes.SubmissionIdentitiesWrite),
            new("identity_create", "Add an identity to a pool idempotently.", Schema(new { identityPoolId = String("UUID"), displayName = String(), email = String(), website = String(), organization = String(), enabled = Boolean(), weight = Integer(1, 10_000), clientRequestKey = String() }, "identityPoolId", "displayName", "email", "clientRequestKey"), AuthorizationScopes.SubmissionIdentitiesWrite),
            new("identity_update", "Update a stored identity idempotently.", Schema(new { identityId = String("UUID"), displayName = String(), email = String(), website = String(), organization = String(), enabled = Boolean(), weight = Integer(1, 10_000), clientRequestKey = String() }, "identityId", "displayName", "email", "enabled", "weight", "clientRequestKey"), AuthorizationScopes.SubmissionIdentitiesWrite),
            new("template_pool_create", "Create an idempotent deterministic submission template pool.", Schema(new { projectId = String("UUID"), name = String(), templateType = new { type = "string", @enum = TemplateTypes }, selectionStrategy = new { type = "string", @enum = PoolSelectionStrategies }, placementMethod = new { type = "string", @enum = PlacementMethods }, enabled = Boolean(), clientRequestKey = String() }, "projectId", "name", "templateType", "selectionStrategy", "placementMethod", "clientRequestKey"), AuthorizationScopes.SubmissionTemplatesWrite),
            new("template_pool_get", "Get one submission template pool.", Schema(new { templatePoolId = String("UUID") }, "templatePoolId"), AuthorizationScopes.SubmissionTemplatesRead),
            new("template_pool_list", "List submission template pools with bounded keyset pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.SubmissionTemplatesRead),
            new("template_pool_update", "Update deterministic template-pool selection and placement configuration idempotently.", Schema(new { templatePoolId = String("UUID"), name = String(), templateType = new { type = "string", @enum = TemplateTypes }, selectionStrategy = new { type = "string", @enum = PoolSelectionStrategies }, placementMethod = new { type = "string", @enum = PlacementMethods }, enabled = Boolean(), clientRequestKey = String() }, "templatePoolId", "name", "templateType", "selectionStrategy", "placementMethod", "enabled", "clientRequestKey"), AuthorizationScopes.SubmissionTemplatesWrite),
            new("template_create", "Add a bounded template and controlled variants idempotently.", Schema(new { templatePoolId = String("UUID"), name = String(), body = String(), prefixVariants = new { type = "array", maxItems = 100, items = String() }, suffixVariants = new { type = "array", maxItems = 100, items = String() }, anchorVariants = new { type = "array", maxItems = 100, items = String() }, targetUrlVariants = new { type = "array", maxItems = 100, items = String() }, enabled = Boolean(), weight = Integer(1, 10_000), clientRequestKey = String() }, "templatePoolId", "name", "body", "clientRequestKey"), AuthorizationScopes.SubmissionTemplatesWrite),
            new("template_update", "Update a bounded template and its controlled variants idempotently.", Schema(new { templateId = String("UUID"), name = String(), body = String(), prefixVariants = new { type = "array", maxItems = 100, items = String() }, suffixVariants = new { type = "array", maxItems = 100, items = String() }, anchorVariants = new { type = "array", maxItems = 100, items = String() }, targetUrlVariants = new { type = "array", maxItems = 100, items = String() }, enabled = Boolean(), weight = Integer(1, 10_000), clientRequestKey = String() }, "templateId", "name", "body", "enabled", "weight", "clientRequestKey"), AuthorizationScopes.SubmissionTemplatesWrite),
            new("submission_preview", "Resolve ownership, adapter, identity, template, variants, and placement without network submission.", Schema(new { projectId = String("UUID"), submissionSourceId = String("UUID"), campaignId = String("Optional UUID"), identityPoolId = String("Optional UUID"), templatePoolId = String("Optional UUID"), targetUrl = String(), attemptNumber = Integer(1, 1_000) }, "projectId", "submissionSourceId", "targetUrl"), AuthorizationScopes.SubmissionsRead),
            new("wordpress_site_profile_create", "Map an owned-network domain to an optional named server-side credential. Credentials are required only for direct API and authenticated integration modes.", Schema(new { ownedNetworkId = String("UUID"), domain = String(), apiBaseUrl = String(), credentialReference = String("Optional server-side name for public comment modes"), submissionMode = new { type = "string", @enum = WordPressSubmissionModes }, enabled = Boolean(), clientRequestKey = String() }, "ownedNetworkId", "domain", "apiBaseUrl", "submissionMode", "clientRequestKey"), AuthorizationScopes.Admin),
            new("wordpress_site_profile_list", "List WordPress site profile metadata; secret values are never returned.", Schema(new { ownedNetworkId = String("UUID"), limit = Integer(), cursor = String() }, "ownedNetworkId"), AuthorizationScopes.Admin),
            new("wordpress_site_profile_update", "Change strategy or optional named credentials for an owned WordPress profile. Raw credentials remain server-side.", Schema(new { wordpressSiteProfileId = String("UUID"), apiBaseUrl = String(), credentialReference = String("Optional server-side name for public comment modes"), submissionMode = new { type = "string", @enum = WordPressSubmissionModes }, enabled = Boolean(), clientRequestKey = String() }, "wordpressSiteProfileId", "apiBaseUrl", "submissionMode", "enabled", "clientRequestKey"), AuthorizationScopes.Admin),
            new("policy_get", "Get the project's conservative automation policy.", Schema(new { projectId = String("UUID") }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("policy_update", "Update project score thresholds, review requirements, and action limits idempotently.", Schema(new { projectId = String("UUID"), automationEnabled = new { type = "boolean" }, minimumQualityScore = Integer(0), maximumRiskScore = Integer(0), manualReviewRequired = new { type = "boolean" }, hourlyActionLimit = Integer(0, 10_000), dailyActionLimit = Integer(0, 100_000), perDomainActionLimit = Integer(0, 10_000), clientRequestKey = String() }, "projectId", "automationEnabled", "minimumQualityScore", "maximumRiskScore", "manualReviewRequired", "hourlyActionLimit", "dailyActionLimit", "perDomainActionLimit", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("blocklist_list", "List enabled and disabled project blocklist entries with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("blocklist_add", "Add a normalized domain, host, URL, URL-prefix, source, network, or campaign exclusion idempotently.", Schema(new { projectId = String("UUID"), matchType = new { type = "string", @enum = BlocklistMatchTypes }, value = String(), reason = String(), clientRequestKey = String() }, "projectId", "matchType", "value", "reason", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("candidates_list", "List imported candidate pages.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.CandidatesRead),
            new("candidates_get", "Get one candidate page and its latest analysis state.", Schema(new { candidateId = String("UUID") }, "candidateId"), AuthorizationScopes.CandidatesRead),
            new("candidates_import", "Import up to 1000 candidate HTTP(S) URLs without fetching them.", Schema(new { projectId = String("UUID"), urls = new { type = "array", maxItems = 1000, items = String() }, clientRequestKey = String() }, "projectId", "urls", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("discovery_start", "Queue bounded discovery through a named provider and return a persistent job ID.", Schema(new { projectId = String("UUID"), provider = new { type = "string", @enum = DiscoveryProviders }, query = String(), content = String(), urls = new { type = "array", maxItems = 5000, items = String() }, maximumResults = Integer(1, 5_000), clientRequestKey = String() }, "projectId", "provider", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("discovery_runs_list", "List auditable discovery runs with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("discovery_run_get", "Get one auditable discovery run and its counters.", Schema(new { discoveryRunId = String("UUID") }, "discoveryRunId"), AuthorizationScopes.ProjectsRead),
            new("opportunities_get", "Get one opportunity.", Schema(new { opportunityId = String("UUID") }, "opportunityId"), AuthorizationScopes.OpportunitiesRead),
            new("opportunities_list", "List opportunities with filters and bounded pagination.", Schema(new { projectId = String("UUID"), minimumQuality = Integer(0), maximumRisk = Integer(0), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.OpportunitiesRead),
            new("opportunities_summary", "Summarize project opportunities.", Schema(new { projectId = String("UUID") }, "projectId"), AuthorizationScopes.OpportunitiesRead),
            new("opportunities_approve", "Explicitly approve bounded opportunities for a controlled campaign.", Schema(new { projectId = String("UUID"), opportunityIds = new { type = "array", minItems = 1, maxItems = 100, items = String("UUID") }, clientRequestKey = String() }, "projectId", "opportunityIds", "clientRequestKey"), AuthorizationScopes.OpportunitiesApprove),
            new("campaign_create", "Create either a legacy controlled campaign or a source-catalog owned-network campaign. Owned campaigns use ownedNetworkId and targetUrl; credentials remain server-side.", Schema(new { projectId = String("UUID"), name = String(), approvalMode = new { type = "string", @enum = CampaignApprovalModes }, authorizationProfileKey = String(), authorizationReference = String(), targetId = String("UUID"), opportunityIds = new { type = "array", minItems = 1, maxItems = 100, items = String("UUID") }, ownedNetworkId = String("UUID for owned-network mode"), targetUrl = String(), identityPoolId = String("Optional UUID"), templatePoolId = String("Optional UUID"), globalConcurrency = Integer(1, 10_000), perDomainConcurrency = Integer(1, 10_000), perDomainDelayMilliseconds = Integer(0, 86_400_000), maximumAttempts = Integer(1, 20), verificationDelaySeconds = Integer(0, 2_592_000), mode = new { type = "string", @enum = OwnedCampaignModes }, domain = String(), platform = new { type = "string", @enum = SourcePlatforms }, cms = new { type = "string", @enum = CmsTypes }, technicalCompatibility = new { type = "string", @enum = TechnicalCompatibilities }, validationStatus = new { type = "string", @enum = SourceValidationStatuses }, tag = String(), previousSubmissionStatus = new { type = "string", @enum = SubmissionStatuses }, previousVerificationStatus = new { type = "string", @enum = BacklinkStatuses }, dailyActionLimit = Integer(1, 100_000), clientRequestKey = String() }, "projectId", "name", "clientRequestKey"), AuthorizationScopes.CampaignsWrite),
            new("campaign_get", "Get one campaign and its durable owned-network expansion progress.", Schema(new { campaignId = String("UUID") }, "campaignId"), AuthorizationScopes.CampaignsRead),
            new("campaigns_get", "Get one campaign.", Schema(new { campaignId = String("UUID") }, "campaignId"), AuthorizationScopes.CampaignsRead),
            new("campaigns_status", "Get a campaign's durable lifecycle status.", Schema(new { campaignId = String("UUID") }, "campaignId"), AuthorizationScopes.CampaignsRead),
            new("campaigns_list", "List campaigns with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.CampaignsRead),
            new("campaign_update", "Update draft campaign metadata, approval mode, and daily limit.", Schema(new { campaignId = String("UUID"), name = String(), approvalMode = new { type = "string", @enum = CampaignApprovalModes }, dailyActionLimit = Integer(1, 100_000), clientRequestKey = String() }, "campaignId", "name", "approvalMode", "dailyActionLimit", "clientRequestKey"), AuthorizationScopes.CampaignsWrite),
            new("campaign_start", "Queue durable permitted submission jobs for a campaign.", Schema(new { campaignId = String("UUID"), clientRequestKey = String() }, "campaignId", "clientRequestKey"), AuthorizationScopes.SubmissionsExecute),
            new("campaign_pause", "Pause a running campaign and its non-terminal jobs.", Schema(new { campaignId = String("UUID"), clientRequestKey = String() }, "campaignId", "clientRequestKey"), AuthorizationScopes.CampaignsExecute),
            new("campaign_resume", "Resume a paused campaign and its paused jobs.", Schema(new { campaignId = String("UUID"), clientRequestKey = String() }, "campaignId", "clientRequestKey"), AuthorizationScopes.CampaignsExecute),
            new("campaign_stop", "Permanently stop a campaign and cancel work that has not started.", Schema(new { campaignId = String("UUID"), clientRequestKey = String() }, "campaignId", "clientRequestKey"), AuthorizationScopes.CampaignsExecute),
            new("submissions_list", "List campaign submission states; these are not backlink verification states.", Schema(new { campaignId = String("UUID"), limit = Integer(), cursor = String() }, "campaignId"), AuthorizationScopes.SubmissionsRead),
            new("submission_get", "Get one durable submission state; backlink verification remains separate.", Schema(new { submissionJobId = String("UUID") }, "submissionJobId"), AuthorizationScopes.SubmissionsRead),
            new("submission_attempts", "List immutable submission attempt history.", Schema(new { submissionJobId = String("UUID") }, "submissionJobId"), AuthorizationScopes.SubmissionsRead),
            new("verification_start", "Queue durable verification for a stored backlink candidate.", Schema(new { backlinkId = String("UUID"), clientRequestKey = String() }, "backlinkId", "clientRequestKey"), AuthorizationScopes.VerificationExecute),
            new("backlinks_get", "Get one backlink and its current verification-derived state.", Schema(new { backlinkId = String("UUID") }, "backlinkId"), AuthorizationScopes.BacklinksRead),
            new("backlinks_list", "List stored backlinks with bounded pagination and optional status filtering.", Schema(new { projectId = String("UUID"), status = new { type = "string", @enum = BacklinkStatuses }, limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.BacklinksRead),
            new("verification_history", "List immutable verification checks for one backlink with bounded pagination.", Schema(new { backlinkId = String("UUID"), limit = Integer(), cursor = String() }, "backlinkId"), AuthorizationScopes.BacklinksRead),
            new("schedules_get", "Get one persistent server-side schedule.", Schema(new { scheduleId = String("UUID") }, "scheduleId"), AuthorizationScopes.SchedulesRead),
            new("schedules_list", "List persistent schedules with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.SchedulesRead),
            new("schedule_create", "Create a persistent UTC schedule for discovery, analysis, or verification.", Schema(new { projectId = String("UUID"), name = String(), action = scheduleAction, timing = scheduleTiming, clientRequestKey = String() }, "projectId", "name", "action", "timing", "clientRequestKey"), AuthorizationScopes.SchedulesWrite),
            new("schedule_update", "Replace a persistent schedule definition and reactivate it.", Schema(new { scheduleId = String("UUID"), name = String(), action = scheduleAction, timing = scheduleTiming, clientRequestKey = String() }, "scheduleId", "name", "action", "timing", "clientRequestKey"), AuthorizationScopes.SchedulesWrite),
            new("schedule_pause", "Pause a persistent schedule.", Schema(new { scheduleId = String("UUID"), clientRequestKey = String() }, "scheduleId", "clientRequestKey"), AuthorizationScopes.SchedulesWrite),
            new("schedule_resume", "Resume a paused persistent schedule from its next UTC occurrence.", Schema(new { scheduleId = String("UUID"), clientRequestKey = String() }, "scheduleId", "clientRequestKey"), AuthorizationScopes.SchedulesWrite),
            new("schedule_delete", "Soft-delete a persistent schedule while retaining audit history.", Schema(new { scheduleId = String("UUID"), clientRequestKey = String() }, "scheduleId", "clientRequestKey"), AuthorizationScopes.SchedulesWrite),
            new("reports_get", "Get report lifecycle and artifact metadata. Binary artifacts are downloaded through the scoped REST endpoint.", Schema(new { reportId = String("UUID") }, "reportId"), AuthorizationScopes.ReportsRead),
            new("reports_list", "List persistent reports with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.ReportsRead),
            new("report_generate", "Queue an idempotent campaign-performance or backlink-inventory report in JSON, CSV, XLSX, or HTML and return persistent report and job IDs.", Schema(new { projectId = String("UUID"), campaignId = String("Optional UUID; required for campaignPerformance"), kind = new { type = "string", @enum = ReportKinds }, format = new { type = "string", @enum = ReportFormats }, clientRequestKey = String() }, "projectId", "kind", "format", "clientRequestKey"), AuthorizationScopes.ReportsWrite),
            new("reports_generate", "Queue an idempotent campaign-performance or backlink-inventory report in JSON, CSV, XLSX, or HTML and return persistent report and job IDs.", Schema(new { projectId = String("UUID"), campaignId = String("Optional UUID; required for campaignPerformance"), kind = new { type = "string", @enum = ReportKinds }, format = new { type = "string", @enum = ReportFormats }, clientRequestKey = String() }, "projectId", "kind", "format", "clientRequestKey"), AuthorizationScopes.ReportsWrite),
            new("analysis_start", "Queue deterministic candidate analysis and return a persistent job ID.", Schema(new { projectId = String("UUID"), clientRequestKey = String() }, "projectId", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("jobs_get", "Get persistent job status.", Schema(new { jobId = String("UUID") }, "jobId"), AuthorizationScopes.ProjectsRead),
            new("jobs_list", "List project jobs with bounded pagination.", Schema(new { projectId = String("UUID"), limit = Integer(), cursor = String() }, "projectId"), AuthorizationScopes.ProjectsRead),
            new("jobs_pause", "Request a durable job pause. Active work stops when its worker observes the request.", Schema(new { jobId = String("UUID"), clientRequestKey = String() }, "jobId", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("jobs_resume", "Resume a paused job or withdraw an active pause request.", Schema(new { jobId = String("UUID"), clientRequestKey = String() }, "jobId", "clientRequestKey"), AuthorizationScopes.ProjectsWrite),
            new("jobs_redrive", "Explicitly requeue a failed or dead-letter job with a reset attempt budget.", Schema(new { jobId = String("UUID"), clientRequestKey = String() }, "jobId", "clientRequestKey"), AuthorizationScopes.ProjectsWrite)
        ];
    }
}

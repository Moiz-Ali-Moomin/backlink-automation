using BacklinkStudio.Domain;
using Microsoft.EntityFrameworkCore;

namespace BacklinkStudio.Infrastructure.Persistence;

public sealed class BacklinkStudioDbContext(DbContextOptions<BacklinkStudioDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AgentCredential> AgentCredentials => Set<AgentCredential>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectTarget> ProjectTargets => Set<ProjectTarget>();
    public DbSet<OwnedNetworkProfile> OwnedNetworkProfiles => Set<OwnedNetworkProfile>();
    public DbSet<OwnedNetworkDomain> OwnedNetworkDomains => Set<OwnedNetworkDomain>();
    public DbSet<SubmissionSource> SubmissionSources => Set<SubmissionSource>();
    public DbSet<SubmissionSourceImport> SubmissionSourceImports => Set<SubmissionSourceImport>();
    public DbSet<SubmissionSourceImportChunk> SubmissionSourceImportChunks => Set<SubmissionSourceImportChunk>();
    public DbSet<SubmissionIdentityPool> SubmissionIdentityPools => Set<SubmissionIdentityPool>();
    public DbSet<SubmissionIdentity> SubmissionIdentities => Set<SubmissionIdentity>();
    public DbSet<SubmissionTemplatePool> SubmissionTemplatePools => Set<SubmissionTemplatePool>();
    public DbSet<SubmissionTemplate> SubmissionTemplates => Set<SubmissionTemplate>();
    public DbSet<WordPressSiteProfile> WordPressSiteProfiles => Set<WordPressSiteProfile>();
    public DbSet<CandidateSite> CandidateSites => Set<CandidateSite>();
    public DbSet<CandidatePage> CandidatePages => Set<CandidatePage>();
    public DbSet<Opportunity> Opportunities => Set<Opportunity>();
    public DbSet<OpportunityScoreReason> OpportunityScoreReasons => Set<OpportunityScoreReason>();
    public DbSet<BlocklistEntry> BlocklistEntries => Set<BlocklistEntry>();
    public DbSet<PolicyDefinition> PolicyDefinitions => Set<PolicyDefinition>();
    public DbSet<PersistentJob> Jobs => Set<PersistentJob>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<DiscoveryQuery> DiscoveryQueries => Set<DiscoveryQuery>();
    public DbSet<DiscoveryRun> DiscoveryRuns => Set<DiscoveryRun>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<OwnedNetworkCampaignConfiguration> OwnedNetworkCampaignConfigurations => Set<OwnedNetworkCampaignConfiguration>();
    public DbSet<CampaignTarget> CampaignTargets => Set<CampaignTarget>();
    public DbSet<CampaignOpportunity> CampaignOpportunities => Set<CampaignOpportunity>();
    public DbSet<SubmissionJob> SubmissionJobs => Set<SubmissionJob>();
    public DbSet<SubmissionAttempt> SubmissionAttempts => Set<SubmissionAttempt>();
    public DbSet<Backlink> Backlinks => Set<Backlink>();
    public DbSet<VerificationCheck> VerificationChecks => Set<VerificationCheck>();
    public DbSet<BacklinkWorkflow> BacklinkWorkflows => Set<BacklinkWorkflow>();
    public DbSet<BacklinkWorkflowSource> BacklinkWorkflowSources => Set<BacklinkWorkflowSource>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<Report> Reports => Set<Report>();
    internal DbSet<WorkerHeartbeatRow> WorkerHeartbeats => Set<WorkerHeartbeatRow>();
    internal DbSet<DomainRateLimitRow> DomainRateLimits => Set<DomainRateLimitRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureSecurity(modelBuilder);
        ConfigureProjects(modelBuilder);
        ConfigureOwnedNetworks(modelBuilder);
        ConfigureSubmissionContent(modelBuilder);
        ConfigureCandidates(modelBuilder);
        ConfigureDiscovery(modelBuilder);
        ConfigureSubmissions(modelBuilder);
        ConfigureVerification(modelBuilder);
        ConfigureBacklinkWorkflows(modelBuilder);
        ConfigureSchedules(modelBuilder);
        ConfigureReports(modelBuilder);
        ConfigureJobs(modelBuilder);
        ConfigureWorkerPlatform(modelBuilder);
    }

    private static void ConfigureBacklinkWorkflows(ModelBuilder modelBuilder)
    {
        var workflow = modelBuilder.Entity<BacklinkWorkflow>();
        workflow.ToTable("backlink_workflows", table =>
        {
            table.HasCheckConstraint("ck_backlink_workflow_concurrency",
                "global_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND global_concurrency");
            table.HasCheckConstraint("ck_backlink_workflow_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
            table.HasCheckConstraint("ck_backlink_workflow_attempts", "maximum_attempts BETWEEN 1 AND 20");
            table.HasCheckConstraint("ck_backlink_workflow_verification_delay", "verification_delay_seconds BETWEEN 0 AND 2592000");
            table.HasCheckConstraint("ck_backlink_workflow_campaign_limit", "cardinality(campaign_ids) <= 1000");
        }).HasKey(x => x.Id);
        workflow.Property(x => x.Id).HasColumnName("id");
        workflow.Property(x => x.ProjectId).HasColumnName("project_id");
        workflow.Property(x => x.SourceImportId).HasColumnName("source_import_id");
        workflow.Property(x => x.IdentityPoolId).HasColumnName("identity_pool_id");
        workflow.Property(x => x.TemplatePoolId).HasColumnName("template_pool_id");
        workflow.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2_048);
        workflow.Property(x => x.GlobalConcurrency).HasColumnName("global_concurrency");
        workflow.Property(x => x.PerDomainConcurrency).HasColumnName("per_domain_concurrency");
        workflow.Property(x => x.PerDomainDelayMilliseconds).HasColumnName("per_domain_delay_milliseconds");
        workflow.Property(x => x.MaximumAttempts).HasColumnName("maximum_attempts");
        workflow.Property(x => x.VerificationDelaySeconds).HasColumnName("verification_delay_seconds");
        workflow.Property(x => x.CampaignIds).HasColumnName("campaign_ids").HasColumnType("uuid[]");
        workflow.Property(x => x.OrchestrationJobId).HasColumnName("orchestration_job_id");
        workflow.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        workflow.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(2_000);
        workflow.Property(x => x.CreatedAt).HasColumnName("created_at");
        workflow.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        workflow.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        workflow.HasOne<SubmissionSourceImport>().WithMany().HasForeignKey(x => x.SourceImportId).OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<SubmissionIdentityPool>().WithMany().HasForeignKey(x => x.IdentityPoolId).OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<SubmissionTemplatePool>().WithMany().HasForeignKey(x => x.TemplatePoolId).OnDelete(DeleteBehavior.Restrict);
        workflow.HasOne<PersistentJob>().WithOne().HasForeignKey<BacklinkWorkflow>(x => x.OrchestrationJobId).OnDelete(DeleteBehavior.Restrict);
        workflow.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        workflow.HasIndex(x => x.OrchestrationJobId).IsUnique();

        var source = modelBuilder.Entity<BacklinkWorkflowSource>();
        source.ToTable("backlink_workflow_sources").HasKey(x => x.Id);
        source.Property(x => x.Id).HasColumnName("id");
        source.Property(x => x.WorkflowId).HasColumnName("workflow_id");
        source.Property(x => x.SubmissionSourceId).HasColumnName("submission_source_id");
        source.Property(x => x.CampaignId).HasColumnName("campaign_id");
        source.Property(x => x.OriginalUrl).HasColumnName("original_url").HasMaxLength(2_048);
        source.Property(x => x.NormalizedUrl).HasColumnName("normalized_url").HasMaxLength(2_048);
        source.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        source.Property(x => x.Host).HasColumnName("host").HasMaxLength(253);
        source.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(40);
        source.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(2_000);
        source.Property(x => x.CreatedAt).HasColumnName("created_at");
        source.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        source.HasOne<BacklinkWorkflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        source.HasOne<SubmissionSource>().WithMany().HasForeignKey(x => x.SubmissionSourceId).OnDelete(DeleteBehavior.Restrict);
        source.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        source.HasIndex(x => new { x.WorkflowId, x.NormalizedUrl }).IsUnique();
        source.HasIndex(x => new { x.WorkflowId, x.CreatedAt, x.Id });
        source.HasIndex(x => x.SubmissionSourceId);
    }

    private static void ConfigureSubmissionContent(ModelBuilder modelBuilder)
    {
        var identityPool = modelBuilder.Entity<SubmissionIdentityPool>();
        identityPool.ToTable("submission_identity_pools").HasKey(x => x.Id);
        identityPool.Property(x => x.Id).HasColumnName("id");
        identityPool.Property(x => x.ProjectId).HasColumnName("project_id");
        identityPool.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        identityPool.Property(x => x.SelectionStrategy).HasColumnName("selection_strategy").HasConversion<string>().HasMaxLength(40);
        identityPool.Property(x => x.EmailStrategy).HasColumnName("email_strategy").HasConversion<string>().HasMaxLength(40);
        identityPool.Property(x => x.EmailBaseAddress).HasColumnName("email_base_address").HasMaxLength(320);
        identityPool.Property(x => x.CatchAllDomain).HasColumnName("catch_all_domain").HasMaxLength(253);
        identityPool.Property(x => x.Enabled).HasColumnName("enabled");
        identityPool.Property(x => x.CreatedAt).HasColumnName("created_at");
        identityPool.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        identityPool.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        identityPool.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
        identityPool.HasIndex(x => new { x.ProjectId, x.Enabled, x.CreatedAt, x.Id });

        var identity = modelBuilder.Entity<SubmissionIdentity>();
        identity.ToTable("submission_identities", table =>
        {
            table.HasCheckConstraint("ck_submission_identities_weight", "weight BETWEEN 1 AND 10000");
            table.HasCheckConstraint("ck_submission_identities_usage_count", "usage_count >= 0");
        }).HasKey(x => x.Id);
        identity.Property(x => x.Id).HasColumnName("id");
        identity.Property(x => x.PoolId).HasColumnName("pool_id");
        identity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        identity.Property(x => x.Email).HasColumnName("email").HasMaxLength(320);
        identity.Property(x => x.Website).HasColumnName("website").HasMaxLength(2_048);
        identity.Property(x => x.Organization).HasColumnName("organization").HasMaxLength(200);
        identity.Property(x => x.Enabled).HasColumnName("enabled");
        identity.Property(x => x.Weight).HasColumnName("weight");
        identity.Property(x => x.UsageCount).HasColumnName("usage_count");
        identity.Property(x => x.CreatedAt).HasColumnName("created_at");
        identity.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        identity.HasOne<SubmissionIdentityPool>().WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.Cascade);
        identity.HasIndex(x => new { x.PoolId, x.Email }).IsUnique();
        identity.HasIndex(x => new { x.PoolId, x.Enabled, x.CreatedAt, x.Id });

        var templatePool = modelBuilder.Entity<SubmissionTemplatePool>();
        templatePool.ToTable("submission_template_pools").HasKey(x => x.Id);
        templatePool.Property(x => x.Id).HasColumnName("id");
        templatePool.Property(x => x.ProjectId).HasColumnName("project_id");
        templatePool.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        templatePool.Property(x => x.TemplateType).HasColumnName("template_type").HasConversion<string>().HasMaxLength(40);
        templatePool.Property(x => x.SelectionStrategy).HasColumnName("selection_strategy").HasConversion<string>().HasMaxLength(40);
        templatePool.Property(x => x.PlacementMethod).HasColumnName("placement_method").HasConversion<string>().HasMaxLength(40);
        templatePool.Property(x => x.Enabled).HasColumnName("enabled");
        templatePool.Property(x => x.CreatedAt).HasColumnName("created_at");
        templatePool.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        templatePool.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        templatePool.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
        templatePool.HasIndex(x => new { x.ProjectId, x.Enabled, x.CreatedAt, x.Id });

        var template = modelBuilder.Entity<SubmissionTemplate>();
        template.ToTable("submission_templates", table =>
        {
            table.HasCheckConstraint("ck_submission_templates_weight", "weight BETWEEN 1 AND 10000");
            table.HasCheckConstraint("ck_submission_templates_usage_count", "usage_count >= 0");
            table.HasCheckConstraint("ck_submission_templates_variant_limits",
                "cardinality(prefix_variants) <= 100 AND cardinality(suffix_variants) <= 100 AND cardinality(anchor_variants) <= 100 AND cardinality(target_url_variants) <= 100");
        }).HasKey(x => x.Id);
        template.Property(x => x.Id).HasColumnName("id");
        template.Property(x => x.PoolId).HasColumnName("pool_id");
        template.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        template.Property(x => x.Body).HasColumnName("body").HasMaxLength(10_000);
        template.Property(x => x.PrefixVariants).HasColumnName("prefix_variants").HasColumnType("text[]");
        template.Property(x => x.SuffixVariants).HasColumnName("suffix_variants").HasColumnType("text[]");
        template.Property(x => x.AnchorVariants).HasColumnName("anchor_variants").HasColumnType("text[]");
        template.Property(x => x.TargetUrlVariants).HasColumnName("target_url_variants").HasColumnType("text[]");
        template.Property(x => x.Enabled).HasColumnName("enabled");
        template.Property(x => x.Weight).HasColumnName("weight");
        template.Property(x => x.UsageCount).HasColumnName("usage_count");
        template.Property(x => x.CreatedAt).HasColumnName("created_at");
        template.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        template.HasOne<SubmissionTemplatePool>().WithMany().HasForeignKey(x => x.PoolId).OnDelete(DeleteBehavior.Cascade);
        template.HasIndex(x => new { x.PoolId, x.Name }).IsUnique();
        template.HasIndex(x => new { x.PoolId, x.Enabled, x.CreatedAt, x.Id });

        var wordpress = modelBuilder.Entity<WordPressSiteProfile>();
        wordpress.ToTable("wordpress_site_profiles").HasKey(x => x.Id);
        wordpress.Property(x => x.Id).HasColumnName("id");
        wordpress.Property(x => x.OwnedNetworkProfileId).HasColumnName("owned_network_profile_id");
        wordpress.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        wordpress.Property(x => x.ApiBaseUrl).HasColumnName("api_base_url").HasMaxLength(2_048);
        wordpress.Property(x => x.CredentialReference).HasColumnName("credential_reference").HasMaxLength(200);
        wordpress.Property(x => x.SubmissionMode).HasColumnName("submission_mode").HasConversion<string>().HasMaxLength(40);
        wordpress.Property(x => x.Enabled).HasColumnName("enabled");
        wordpress.Property(x => x.CreatedAt).HasColumnName("created_at");
        wordpress.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        wordpress.HasOne<OwnedNetworkProfile>().WithMany().HasForeignKey(x => x.OwnedNetworkProfileId).OnDelete(DeleteBehavior.Cascade);
        wordpress.HasIndex(x => new { x.OwnedNetworkProfileId, x.Domain }).IsUnique();
        wordpress.HasIndex(x => new { x.Domain, x.Enabled });
    }

    private static void ConfigureOwnedNetworks(ModelBuilder modelBuilder)
    {
        var network = modelBuilder.Entity<OwnedNetworkProfile>();
        network.ToTable("owned_network_profiles", table =>
        {
            table.HasCheckConstraint("ck_owned_network_profiles_automation_ownership", "NOT automation_permitted OR ownership_status <> 'Unverified'");
            table.HasCheckConstraint("ck_owned_network_profiles_concurrency", "max_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND max_concurrency");
            table.HasCheckConstraint("ck_owned_network_profiles_domain_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
        }).HasKey(x => x.Id);
        network.Property(x => x.Id).HasColumnName("id");
        network.Property(x => x.ProjectId).HasColumnName("project_id");
        network.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        network.Property(x => x.Description).HasColumnName("description").HasMaxLength(2_000);
        network.Property(x => x.OwnershipStatus).HasColumnName("ownership_status").HasConversion<string>().HasMaxLength(40);
        network.Property(x => x.AutomationPermitted).HasColumnName("automation_permitted");
        network.Property(x => x.OptionalNetworkTag).HasColumnName("optional_network_tag").HasMaxLength(100);
        network.Property(x => x.DefaultIdentityPoolId).HasColumnName("default_identity_pool_id");
        network.Property(x => x.DefaultTemplatePoolId).HasColumnName("default_template_pool_id");
        network.Property(x => x.MaxConcurrency).HasColumnName("max_concurrency");
        network.Property(x => x.PerDomainConcurrency).HasColumnName("per_domain_concurrency");
        network.Property(x => x.PerDomainDelayMilliseconds).HasColumnName("per_domain_delay_milliseconds");
        network.Property(x => x.Enabled).HasColumnName("enabled");
        network.Property(x => x.CreatedAt).HasColumnName("created_at");
        network.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        network.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        network.HasIndex(x => new { x.ProjectId, x.Name }).IsUnique();
        network.HasIndex(x => new { x.ProjectId, x.Enabled, x.CreatedAt, x.Id });
        network.HasIndex(x => new { x.OwnershipStatus, x.AutomationPermitted, x.Enabled });

        var networkDomain = modelBuilder.Entity<OwnedNetworkDomain>();
        networkDomain.ToTable("owned_network_domains").HasKey(x => x.Id);
        networkDomain.Property(x => x.Id).HasColumnName("id");
        networkDomain.Property(x => x.OwnedNetworkProfileId).HasColumnName("owned_network_profile_id");
        networkDomain.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        networkDomain.Property(x => x.MatchType).HasColumnName("match_type").HasConversion<string>().HasMaxLength(30);
        networkDomain.Property(x => x.Enabled).HasColumnName("enabled");
        networkDomain.Property(x => x.CreatedAt).HasColumnName("created_at");
        networkDomain.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        networkDomain.HasOne<OwnedNetworkProfile>().WithMany().HasForeignKey(x => x.OwnedNetworkProfileId).OnDelete(DeleteBehavior.Cascade);
        networkDomain.HasIndex(x => new { x.OwnedNetworkProfileId, x.Domain, x.MatchType }).IsUnique();
        networkDomain.HasIndex(x => new { x.Domain, x.Enabled });

        var source = modelBuilder.Entity<SubmissionSource>();
        source.ToTable("submission_sources", table =>
        {
            table.HasCheckConstraint("ck_submission_sources_automation_ownership", "NOT automation_permitted OR ownership_status <> 'Unverified'");
            table.HasCheckConstraint("ck_submission_sources_counters", "success_count >= 0 AND failure_count >= 0 AND pending_moderation_count >= 0 AND verified_count >= 0 AND lost_count >= 0");
            table.HasCheckConstraint("ck_submission_sources_content_length", "last_content_length IS NULL OR last_content_length >= 0");
        }).HasKey(x => x.Id);
        source.Property(x => x.Id).HasColumnName("id");
        source.Property(x => x.ProjectId).HasColumnName("project_id");
        source.Property(x => x.OwnedNetworkProfileId).HasColumnName("owned_network_profile_id");
        source.Property(x => x.SourceImportId).HasColumnName("source_import_id");
        source.Property(x => x.OriginalUrl).HasColumnName("original_url").HasMaxLength(2_048);
        source.Property(x => x.NormalizedUrl).HasColumnName("normalized_url").HasMaxLength(2_048);
        source.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        source.Property(x => x.Host).HasColumnName("host").HasMaxLength(253);
        source.Property(x => x.Platform).HasColumnName("platform").HasConversion<string>().HasMaxLength(40);
        source.Property(x => x.CmsType).HasColumnName("cms_type").HasConversion<string>().HasMaxLength(40);
        source.Property(x => x.OpportunityType).HasColumnName("opportunity_type").HasConversion<string>().HasMaxLength(50);
        source.Property(x => x.AdapterName).HasColumnName("adapter_name").HasMaxLength(100);
        source.Property(x => x.OwnershipStatus).HasColumnName("ownership_status").HasConversion<string>().HasMaxLength(40);
        source.Property(x => x.AutomationPermitted).HasColumnName("automation_permitted");
        source.Property(x => x.TechnicalCompatibility).HasColumnName("technical_compatibility").HasConversion<string>().HasMaxLength(40);
        source.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasConversion<string>().HasMaxLength(30);
        source.Property(x => x.ValidationReason).HasColumnName("validation_reason").HasMaxLength(2_000);
        source.Property(x => x.DetectionReason).HasColumnName("detection_reason").HasMaxLength(2_000);
        source.Property(x => x.RequiresBrowser).HasColumnName("requires_browser");
        source.Property(x => x.RequiresAuthentication).HasColumnName("requires_authentication");
        source.Property(x => x.RequiresManualAction).HasColumnName("requires_manual_action");
        source.Property(x => x.SupportsWordPressComment).HasColumnName("supports_wordpress_comment");
        source.Property(x => x.SupportsOwnedWordPressApi).HasColumnName("supports_owned_wordpress_api");
        source.Property(x => x.SupportsOwnedPropertyPlacement).HasColumnName("supports_owned_property_placement");
        source.Property(x => x.PostId).HasColumnName("post_id");
        source.Property(x => x.CommentEndpoint).HasColumnName("comment_endpoint").HasMaxLength(2_048);
        source.Property(x => x.DetectedFormAction).HasColumnName("detected_form_action").HasMaxLength(2_048);
        source.Property(x => x.PageTitle).HasColumnName("page_title").HasMaxLength(500);
        source.Property(x => x.FinalUrl).HasColumnName("final_url").HasMaxLength(2_048);
        source.Property(x => x.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2_048);
        source.Property(x => x.LastHttpStatus).HasColumnName("last_http_status");
        source.Property(x => x.LastContentType).HasColumnName("last_content_type").HasMaxLength(200);
        source.Property(x => x.LastContentLength).HasColumnName("last_content_length");
        source.Property(x => x.RedirectChain).HasColumnName("redirect_chain").HasColumnType("text[]");
        source.Property(x => x.CommentsEnabled).HasColumnName("comments_enabled");
        source.Property(x => x.CommentAuthorField).HasColumnName("comment_author_field").HasMaxLength(100);
        source.Property(x => x.CommentEmailField).HasColumnName("comment_email_field").HasMaxLength(100);
        source.Property(x => x.CommentWebsiteField).HasColumnName("comment_website_field").HasMaxLength(100);
        source.Property(x => x.CommentContentField).HasColumnName("comment_content_field").HasMaxLength(100);
        source.Property(x => x.CommentPostIdField).HasColumnName("comment_post_id_field").HasMaxLength(100);
        source.Property(x => x.AdditionalRequiredFields).HasColumnName("additional_required_fields").HasColumnType("text[]");
        source.Property(x => x.RequiresCookies).HasColumnName("requires_cookies");
        source.Property(x => x.RequiresNonce).HasColumnName("requires_nonce");
        source.Property(x => x.ModerationSignal).HasColumnName("moderation_signal").HasMaxLength(500);
        source.Property(x => x.Tag).HasColumnName("tag").HasMaxLength(100);
        source.Property(x => x.LastValidatedAt).HasColumnName("last_validated_at");
        source.Property(x => x.LastSubmissionAt).HasColumnName("last_submission_at");
        source.Property(x => x.LastSuccessfulSubmissionAt).HasColumnName("last_successful_submission_at");
        source.Property(x => x.SuccessCount).HasColumnName("success_count");
        source.Property(x => x.FailureCount).HasColumnName("failure_count");
        source.Property(x => x.PendingModerationCount).HasColumnName("pending_moderation_count");
        source.Property(x => x.VerifiedCount).HasColumnName("verified_count");
        source.Property(x => x.LostCount).HasColumnName("lost_count");
        source.Property(x => x.Enabled).HasColumnName("enabled");
        source.Property(x => x.CreatedAt).HasColumnName("created_at");
        source.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        source.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        source.HasOne<OwnedNetworkProfile>().WithMany().HasForeignKey(x => x.OwnedNetworkProfileId).OnDelete(DeleteBehavior.Restrict);
        source.HasIndex(x => new { x.ProjectId, x.NormalizedUrl }).IsUnique();
        source.HasIndex(x => x.SourceImportId);
        source.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.OwnedNetworkProfileId, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.Domain, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.Host, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.Platform, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.CmsType, x.CreatedAt, x.Id });
        source.HasIndex(x => new { x.AdapterName, x.CreatedAt, x.Id });
        source.HasIndex(nameof(SubmissionSource.OwnershipStatus), nameof(SubmissionSource.AutomationPermitted), nameof(SubmissionSource.Enabled), nameof(SubmissionSource.CreatedAt), nameof(SubmissionSource.Id));
        source.HasIndex(x => new { x.TechnicalCompatibility, x.ValidationStatus, x.Enabled, x.CreatedAt, x.Id });
        source.HasIndex(x => new
        {
            x.ProjectId,
            x.OwnedNetworkProfileId,
            x.TechnicalCompatibility,
            x.ValidationStatus,
            x.CreatedAt,
            x.Id
        }).HasDatabaseName("ix_submission_sources_campaign_selection")
            .HasFilter("automation_permitted AND enabled");
        source.HasIndex(x => x.LastValidatedAt);

        var sourceImport = modelBuilder.Entity<SubmissionSourceImport>();
        sourceImport.ToTable("submission_source_imports", table =>
        {
            table.HasCheckConstraint("ck_submission_source_imports_byte_length", "byte_length >= 0");
            table.HasCheckConstraint("ck_submission_source_imports_counters", "total_lines >= 0 AND accepted >= 0 AND duplicates >= 0 AND invalid >= 0 AND errors >= 0");
        }).HasKey(x => x.Id);
        sourceImport.Property(x => x.Id).HasColumnName("id");
        sourceImport.Property(x => x.ProjectId).HasColumnName("project_id");
        sourceImport.Property(x => x.OwnedNetworkProfileId).HasColumnName("owned_network_profile_id");
        sourceImport.Property(x => x.JobId).HasColumnName("job_id");
        sourceImport.Property(x => x.Format).HasColumnName("format").HasConversion<string>().HasMaxLength(20);
        sourceImport.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        sourceImport.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(255);
        sourceImport.Property(x => x.Tag).HasColumnName("tag").HasMaxLength(100);
        sourceImport.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        sourceImport.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
        sourceImport.Property(x => x.ByteLength).HasColumnName("byte_length");
        sourceImport.Property(x => x.TotalLines).HasColumnName("total_lines");
        sourceImport.Property(x => x.Accepted).HasColumnName("accepted");
        sourceImport.Property(x => x.Duplicates).HasColumnName("duplicates");
        sourceImport.Property(x => x.Invalid).HasColumnName("invalid");
        sourceImport.Property(x => x.Errors).HasColumnName("errors");
        sourceImport.Property(x => x.SafeError).HasColumnName("safe_error").HasMaxLength(2_000);
        sourceImport.Property(x => x.CreatedAt).HasColumnName("created_at");
        sourceImport.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        sourceImport.Property(x => x.CompletedAt).HasColumnName("completed_at");
        sourceImport.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        sourceImport.HasOne<OwnedNetworkProfile>().WithMany().HasForeignKey(x => x.OwnedNetworkProfileId).OnDelete(DeleteBehavior.Restrict);
        sourceImport.HasOne<PersistentJob>().WithMany().HasForeignKey(x => x.JobId).OnDelete(DeleteBehavior.Restrict);
        sourceImport.HasIndex(x => new { x.ProjectId, x.IdempotencyKey }).IsUnique();
        sourceImport.HasIndex(x => x.JobId).IsUnique();
        sourceImport.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        sourceImport.HasIndex(x => new { x.Status, x.CreatedAt });

        var sourceImportChunk = modelBuilder.Entity<SubmissionSourceImportChunk>();
        sourceImportChunk.ToTable("submission_source_import_chunks", table =>
        {
            table.HasCheckConstraint("ck_submission_source_import_chunks_sequence", "sequence >= 0");
            table.HasCheckConstraint("ck_submission_source_import_chunks_size", "octet_length(content) BETWEEN 1 AND 65536");
        }).HasKey(x => x.Id);
        sourceImportChunk.Property(x => x.Id).HasColumnName("id");
        sourceImportChunk.Property(x => x.ImportId).HasColumnName("import_id");
        sourceImportChunk.Property(x => x.Sequence).HasColumnName("sequence");
        sourceImportChunk.Property(x => x.Content).HasColumnName("content").HasColumnType("bytea");
        sourceImportChunk.Property(x => x.CreatedAt).HasColumnName("created_at");
        sourceImportChunk.HasOne<SubmissionSourceImport>().WithMany().HasForeignKey(x => x.ImportId).OnDelete(DeleteBehavior.Cascade);
        sourceImportChunk.HasIndex(x => new { x.ImportId, x.Sequence }).IsUnique();

        source.HasOne<SubmissionSourceImport>().WithMany().HasForeignKey(x => x.SourceImportId).OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureReports(ModelBuilder modelBuilder)
    {
        var report = modelBuilder.Entity<Report>();
        report.ToTable("reports", table =>
        {
            table.HasCheckConstraint("ck_reports_byte_length", "byte_length IS NULL OR byte_length >= 0");
            table.HasCheckConstraint("ck_reports_row_count", "row_count IS NULL OR row_count >= 0");
        }).HasKey(x => x.Id);
        report.Property(x => x.Id).HasColumnName("id");
        report.Property(x => x.ProjectId).HasColumnName("project_id");
        report.Property(x => x.CampaignId).HasColumnName("campaign_id");
        report.Property(x => x.JobId).HasColumnName("job_id");
        report.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(40);
        report.Property(x => x.Format).HasColumnName("format").HasConversion<string>().HasMaxLength(20);
        report.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        report.Property(x => x.ArtifactName).HasColumnName("artifact_name").HasMaxLength(255);
        report.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200);
        report.Property(x => x.ByteLength).HasColumnName("byte_length");
        report.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
        report.Property(x => x.RowCount).HasColumnName("row_count");
        report.Property(x => x.Error).HasColumnName("error").HasMaxLength(2_000);
        report.Property(x => x.CreatedAt).HasColumnName("created_at");
        report.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        report.Property(x => x.StartedAt).HasColumnName("started_at");
        report.Property(x => x.CompletedAt).HasColumnName("completed_at");
        report.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        report.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.SetNull);
        report.HasOne<PersistentJob>().WithOne().HasForeignKey<Report>(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
        report.HasIndex(x => x.JobId).IsUnique();
        report.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        report.HasIndex(x => new { x.CampaignId, x.CreatedAt, x.Id });
        report.HasIndex(x => new { x.Status, x.CreatedAt });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAppendOnlyRecords();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAppendOnlyRecords();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceAppendOnlyRecords()
    {
        if (ChangeTracker.Entries<AuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit events are append-only.");
        }
        if (ChangeTracker.Entries<VerificationCheck>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Verification checks are append-only.");
        }
    }

    private static void ConfigureVerification(ModelBuilder modelBuilder)
    {
        var backlink = modelBuilder.Entity<Backlink>();
        backlink.ToTable("backlinks").HasKey(x => x.Id);
        backlink.Property(x => x.Id).HasColumnName("id");
        backlink.Property(x => x.ProjectId).HasColumnName("project_id");
        backlink.Property(x => x.CampaignId).HasColumnName("campaign_id");
        backlink.Property(x => x.SubmissionJobId).HasColumnName("submission_job_id");
        backlink.Property(x => x.SubmissionSourceId).HasColumnName("submission_source_id");
        backlink.Property(x => x.SubmissionAttemptId).HasColumnName("submission_attempt_id");
        backlink.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(2_048);
        backlink.Property(x => x.NormalizedSourceUrl).HasColumnName("normalized_source_url").HasMaxLength(2_048);
        backlink.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2_048);
        backlink.Property(x => x.NormalizedTargetUrl).HasColumnName("normalized_target_url").HasMaxLength(2_048);
        backlink.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        backlink.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        backlink.Property(x => x.AnchorText).HasColumnName("anchor_text").HasMaxLength(500);
        backlink.Property(x => x.Rel).HasColumnName("rel").HasColumnType("text[]");
        backlink.Property(x => x.Nofollow).HasColumnName("nofollow");
        backlink.Property(x => x.Ugc).HasColumnName("ugc");
        backlink.Property(x => x.Sponsored).HasColumnName("sponsored");
        backlink.Property(x => x.HttpStatus).HasColumnName("http_status");
        backlink.Property(x => x.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2_048);
        backlink.Property(x => x.FirstSeenAt).HasColumnName("first_seen_at");
        backlink.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
        backlink.Property(x => x.LastCheckedAt).HasColumnName("last_checked_at");
        backlink.Property(x => x.CreatedAt).HasColumnName("created_at");
        backlink.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        backlink.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        backlink.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.SetNull);
        backlink.HasOne<SubmissionJob>().WithOne().HasForeignKey<Backlink>(x => x.SubmissionJobId).OnDelete(DeleteBehavior.SetNull);
        backlink.HasOne<SubmissionSource>().WithMany().HasForeignKey(x => x.SubmissionSourceId).OnDelete(DeleteBehavior.SetNull);
        backlink.HasOne<SubmissionAttempt>().WithOne().HasForeignKey<Backlink>(x => x.SubmissionAttemptId).OnDelete(DeleteBehavior.SetNull);
        backlink.HasIndex(x => x.SubmissionJobId).IsUnique();
        backlink.HasIndex(x => new { x.SubmissionSourceId, x.Status });
        backlink.HasIndex(x => x.SubmissionAttemptId).IsUnique().HasFilter("submission_attempt_id IS NOT NULL");
        backlink.HasIndex(x => new { x.ProjectId, x.NormalizedSourceUrl, x.NormalizedTargetUrl }).IsUnique();
        backlink.HasIndex(x => new { x.ProjectId, x.Status, x.CreatedAt, x.Id });
        backlink.HasIndex(x => new { x.Domain, x.Status });
        backlink.HasIndex(x => x.LastCheckedAt);

        var check = modelBuilder.Entity<VerificationCheck>();
        check.ToTable("verification_checks", table => table.HasCheckConstraint("ck_verification_duration", "duration_milliseconds >= 0")).HasKey(x => x.Id);
        check.Property(x => x.Id).HasColumnName("id");
        check.Property(x => x.BacklinkId).HasColumnName("backlink_id");
        check.Property(x => x.CheckedAt).HasColumnName("checked_at");
        check.Property(x => x.Found).HasColumnName("found");
        check.Property(x => x.HttpStatus).HasColumnName("http_status");
        check.Property(x => x.Anchor).HasColumnName("anchor").HasMaxLength(500);
        check.Property(x => x.Rel).HasColumnName("rel").HasColumnType("text[]");
        check.Property(x => x.Error).HasColumnName("error").HasMaxLength(2_000);
        check.Property(x => x.DurationMilliseconds).HasColumnName("duration_milliseconds");
        check.HasOne<Backlink>().WithMany().HasForeignKey(x => x.BacklinkId).OnDelete(DeleteBehavior.Restrict);
        check.HasIndex(x => new { x.BacklinkId, x.CheckedAt, x.Id });
    }

    private static void ConfigureSchedules(ModelBuilder modelBuilder)
    {
        var schedule = modelBuilder.Entity<Schedule>();
        schedule.ToTable("schedules", table =>
        {
            table.HasCheckConstraint("ck_schedule_interval", "interval_minutes IS NULL OR interval_minutes BETWEEN 1 AND 525600");
            table.HasCheckConstraint("ck_schedule_day_of_month", "day_of_month IS NULL OR day_of_month BETWEEN 1 AND 31");
            table.HasCheckConstraint("ck_schedule_last_job_count", "last_job_count >= 0");
            table.HasCheckConstraint("ck_schedule_failures", "consecutive_failures >= 0");
            table.HasCheckConstraint("ck_schedule_recoveries", "recovery_count >= 0");
            table.HasCheckConstraint("ck_schedule_timing", "(recurrence_type = 'OneTime' AND one_time_at IS NOT NULL AND interval_minutes IS NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Interval' AND one_time_at IS NULL AND interval_minutes IS NOT NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Daily' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Weekly' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NOT NULL AND day_of_month IS NULL AND cron_expression IS NULL) OR (recurrence_type = 'Monthly' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NOT NULL AND day_of_week IS NULL AND day_of_month IS NOT NULL AND cron_expression IS NULL) OR (recurrence_type = 'Cron' AND one_time_at IS NULL AND interval_minutes IS NULL AND time_of_day_utc IS NULL AND day_of_week IS NULL AND day_of_month IS NULL AND cron_expression IS NOT NULL)");
        }).HasKey(x => x.Id);
        schedule.Property(x => x.Id).HasColumnName("id");
        schedule.Property(x => x.ProjectId).HasColumnName("project_id");
        schedule.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        schedule.Property(x => x.ActionType).HasColumnName("action_type").HasConversion<string>().HasMaxLength(30);
        schedule.Property(x => x.ActionPayload).HasColumnName("action_payload").HasColumnType("jsonb");
        schedule.Property(x => x.RecurrenceType).HasColumnName("recurrence_type").HasConversion<string>().HasMaxLength(30);
        schedule.Property(x => x.StartsAt).HasColumnName("starts_at");
        schedule.Property(x => x.OneTimeAt).HasColumnName("one_time_at");
        schedule.Property(x => x.IntervalMinutes).HasColumnName("interval_minutes");
        schedule.Property(x => x.TimeOfDayUtc).HasColumnName("time_of_day_utc");
        schedule.Property(x => x.DayOfWeek).HasColumnName("day_of_week").HasConversion<string>().HasMaxLength(20);
        schedule.Property(x => x.DayOfMonth).HasColumnName("day_of_month");
        schedule.Property(x => x.CronExpression).HasColumnName("cron_expression").HasMaxLength(200);
        schedule.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        schedule.Property(x => x.NextRunAt).HasColumnName("next_run_at");
        schedule.Property(x => x.AvailableAt).HasColumnName("available_at");
        schedule.Property(x => x.LastScheduledFor).HasColumnName("last_scheduled_for");
        schedule.Property(x => x.LastRunAt).HasColumnName("last_run_at");
        schedule.Property(x => x.LastJobCount).HasColumnName("last_job_count");
        schedule.Property(x => x.ConsecutiveFailures).HasColumnName("consecutive_failures");
        schedule.Property(x => x.RecoveryCount).HasColumnName("recovery_count");
        schedule.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(2_000);
        schedule.Property(x => x.ClaimedBy).HasColumnName("claimed_by").HasMaxLength(200);
        schedule.Property(x => x.ClaimedAt).HasColumnName("claimed_at");
        schedule.Property(x => x.ClaimExpiresAt).HasColumnName("claim_expires_at");
        schedule.Property(x => x.CreatedAt).HasColumnName("created_at");
        schedule.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        schedule.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        schedule.HasIndex(x => new { x.Status, x.NextRunAt, x.AvailableAt });
        schedule.HasIndex(x => x.ClaimExpiresAt);
        schedule.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
    }

    private static void ConfigureSecurity(ModelBuilder modelBuilder)
    {
        var user = modelBuilder.Entity<User>();
        user.ToTable("users").HasKey(x => x.Id);
        user.Property(x => x.Id).HasColumnName("id");
        user.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200);
        user.Property(x => x.Enabled).HasColumnName("enabled");
        user.Property(x => x.CreatedAt).HasColumnName("created_at");

        var credential = modelBuilder.Entity<AgentCredential>();
        credential.ToTable("agent_credentials").HasKey(x => x.Id);
        credential.Property(x => x.Id).HasColumnName("id");
        credential.Property(x => x.UserId).HasColumnName("user_id");
        credential.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        credential.Property(x => x.LookupHash).HasColumnName("lookup_hash").HasMaxLength(64);
        credential.Property(x => x.KeyHash).HasColumnName("key_hash");
        credential.Property(x => x.KeySalt).HasColumnName("key_salt");
        credential.Property(x => x.Scopes).HasColumnName("scopes").HasColumnType("text[]");
        credential.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        credential.Property(x => x.RevokedAt).HasColumnName("revoked_at").IsConcurrencyToken();
        credential.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        credential.Property(x => x.CreatedAt).HasColumnName("created_at");
        credential.HasIndex(x => x.LookupHash).IsUnique();
        credential.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);

        var audit = modelBuilder.Entity<AuditEvent>();
        audit.ToTable("audit_events", table => table.HasCheckConstraint("ck_audit_input_summary_length", "length(input_summary) <= 2000"));
        audit.HasKey(x => x.Id);
        audit.Property(x => x.Id).HasColumnName("id");
        audit.Property(x => x.Timestamp).HasColumnName("timestamp");
        audit.Property(x => x.ActorType).HasColumnName("actor_type").HasConversion<string>().HasMaxLength(30);
        audit.Property(x => x.ActorId).HasColumnName("actor_id").HasMaxLength(200);
        audit.Property(x => x.CredentialId).HasColumnName("credential_id");
        audit.Property(x => x.Operation).HasColumnName("operation").HasMaxLength(200);
        audit.Property(x => x.ProjectId).HasColumnName("project_id");
        audit.Property(x => x.CampaignId).HasColumnName("campaign_id");
        audit.Property(x => x.JobId).HasColumnName("job_id");
        audit.Property(x => x.RequestId).HasColumnName("request_id").HasMaxLength(100);
        audit.Property(x => x.InputSummary).HasColumnName("input_summary").HasMaxLength(2_000);
        audit.Property(x => x.Result).HasColumnName("result").HasMaxLength(100);
        audit.Property(x => x.SourceAddress).HasColumnName("source_address").HasMaxLength(100);
        audit.HasIndex(x => new { x.ProjectId, x.Timestamp });
        audit.HasIndex(x => x.JobId);

        var idem = modelBuilder.Entity<IdempotencyRecord>();
        idem.ToTable("idempotency_records").HasKey(x => x.Id);
        idem.Property(x => x.Id).HasColumnName("id");
        idem.Property(x => x.Scope).HasColumnName("scope").HasMaxLength(300);
        idem.Property(x => x.Key).HasColumnName("key").HasMaxLength(200);
        idem.Property(x => x.RequestHash).HasColumnName("request_hash").HasMaxLength(64);
        idem.Property(x => x.ResourceType).HasColumnName("resource_type").HasMaxLength(100);
        idem.Property(x => x.ResourceId).HasColumnName("resource_id");
        idem.Property(x => x.ResponseJson).HasColumnName("response_json").HasColumnType("jsonb");
        idem.Property(x => x.CreatedAt).HasColumnName("created_at");
        idem.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        idem.HasIndex(x => new { x.Scope, x.Key }).IsUnique();
        idem.HasIndex(x => x.ExpiresAt);
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder)
    {
        var project = modelBuilder.Entity<Project>();
        project.ToTable("projects").HasKey(x => x.Id);
        project.Property(x => x.Id).HasColumnName("id");
        project.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        project.Property(x => x.PrimaryDomain).HasColumnName("primary_domain").HasMaxLength(253);
        project.Property(x => x.Description).HasColumnName("description").HasMaxLength(2_000);
        project.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        project.Property(x => x.CreatedAt).HasColumnName("created_at");
        project.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        project.HasIndex(x => x.PrimaryDomain);

        var target = modelBuilder.Entity<ProjectTarget>();
        target.ToTable("project_targets").HasKey(x => x.Id);
        target.Property(x => x.Id).HasColumnName("id");
        target.Property(x => x.ProjectId).HasColumnName("project_id");
        target.Property(x => x.Url).HasColumnName("url").HasMaxLength(2_048);
        target.Property(x => x.NormalizedUrl).HasColumnName("normalized_url").HasMaxLength(2_048);
        target.Property(x => x.Label).HasColumnName("label").HasMaxLength(200);
        target.Property(x => x.Keywords).HasColumnName("keywords").HasColumnType("text[]");
        target.Property(x => x.PreferredAnchor).HasColumnName("preferred_anchor").HasMaxLength(500);
        target.Property(x => x.Category).HasColumnName("category").HasMaxLength(100);
        target.Property(x => x.Priority).HasColumnName("priority");
        target.Property(x => x.Enabled).HasColumnName("enabled");
        target.Property(x => x.CreatedAt).HasColumnName("created_at");
        target.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        target.HasIndex(x => new { x.ProjectId, x.NormalizedUrl }).IsUnique();
        target.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
    }

    private static void ConfigureCandidates(ModelBuilder modelBuilder)
    {
        var site = modelBuilder.Entity<CandidateSite>();
        site.ToTable("candidate_sites").HasKey(x => x.Id);
        site.Property(x => x.Id).HasColumnName("id");
        site.Property(x => x.ProjectId).HasColumnName("project_id");
        site.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        site.Property(x => x.CreatedAt).HasColumnName("created_at");
        site.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        site.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        site.HasIndex(x => new { x.ProjectId, x.Domain }).IsUnique();

        var page = modelBuilder.Entity<CandidatePage>();
        page.ToTable("candidate_pages").HasKey(x => x.Id);
        page.Property(x => x.Id).HasColumnName("id");
        page.Property(x => x.ProjectId).HasColumnName("project_id");
        page.Property(x => x.CandidateSiteId).HasColumnName("candidate_site_id");
        page.Property(x => x.Url).HasColumnName("url").HasMaxLength(2_048);
        page.Property(x => x.NormalizedUrl).HasColumnName("normalized_url").HasMaxLength(2_048);
        page.Property(x => x.FinalUrl).HasColumnName("final_url").HasMaxLength(2_048);
        page.Property(x => x.HttpStatus).HasColumnName("http_status");
        page.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(200);
        page.Property(x => x.Title).HasColumnName("title").HasMaxLength(500);
        page.Property(x => x.CanonicalUrl).HasColumnName("canonical_url").HasMaxLength(2_048);
        page.Property(x => x.Cms).HasColumnName("cms").HasMaxLength(100);
        page.Property(x => x.RobotsDirectives).HasColumnName("robots_directives").HasMaxLength(500);
        page.Property(x => x.ExistingTargetLink).HasColumnName("existing_target_link");
        page.Property(x => x.EligibleSignals).HasColumnName("eligible_signals").HasColumnType("text[]");
        page.Property(x => x.RequiresJavaScript).HasColumnName("requires_javascript");
        page.Property(x => x.AnalysisError).HasColumnName("analysis_error").HasMaxLength(2_000);
        page.Property(x => x.AnalysisStatus).HasColumnName("analysis_status").HasConversion<string>().HasMaxLength(30);
        page.Property(x => x.LastAnalyzedAt).HasColumnName("last_analyzed_at");
        page.Property(x => x.CreatedAt).HasColumnName("created_at");
        page.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        page.HasOne<CandidateSite>().WithMany().HasForeignKey(x => x.CandidateSiteId).OnDelete(DeleteBehavior.Cascade);
        page.HasIndex(x => new { x.ProjectId, x.NormalizedUrl }).IsUnique();
        page.HasIndex(x => new { x.ProjectId, x.AnalysisStatus });
        page.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });

        var opportunity = modelBuilder.Entity<Opportunity>();
        opportunity.ToTable("opportunities", table =>
        {
            table.HasCheckConstraint("ck_opportunities_quality_score", "quality_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_opportunities_risk_score", "risk_score BETWEEN 0 AND 100");
        }).HasKey(x => x.Id);
        opportunity.Property(x => x.Id).HasColumnName("id");
        opportunity.Property(x => x.ProjectId).HasColumnName("project_id");
        opportunity.Property(x => x.CandidatePageId).HasColumnName("candidate_page_id");
        opportunity.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(40);
        opportunity.Property(x => x.SourceUrl).HasColumnName("source_url").HasMaxLength(2_048);
        opportunity.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        opportunity.Property(x => x.QualityScore).HasColumnName("quality_score");
        opportunity.Property(x => x.RiskScore).HasColumnName("risk_score");
        opportunity.Property(x => x.AutomationStatus).HasColumnName("automation_status").HasConversion<string>().HasMaxLength(40);
        opportunity.Property(x => x.AnalysisReason).HasColumnName("analysis_reason").HasMaxLength(2_000);
        opportunity.Property(x => x.DetectedAt).HasColumnName("detected_at");
        opportunity.Property(x => x.LastAnalyzedAt).HasColumnName("last_analyzed_at");
        opportunity.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        opportunity.Property(x => x.ApprovedBy).HasColumnName("approved_by").HasMaxLength(200);
        opportunity.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        opportunity.HasOne<CandidatePage>().WithMany().HasForeignKey(x => x.CandidatePageId).OnDelete(DeleteBehavior.Cascade);
        opportunity.HasIndex(x => x.CandidatePageId).IsUnique();
        opportunity.HasIndex(x => new { x.ProjectId, x.QualityScore, x.RiskScore });
        opportunity.HasIndex(x => new { x.ProjectId, x.DetectedAt, x.Id });
        opportunity.HasIndex(x => x.Domain);

        var scoreReason = modelBuilder.Entity<OpportunityScoreReason>();
        scoreReason.ToTable("opportunity_score_reasons", table => table.HasCheckConstraint("ck_opportunity_score_reason_points", "points BETWEEN -100 AND 100"));
        scoreReason.HasKey(x => x.Id);
        scoreReason.Property(x => x.Id).HasColumnName("id");
        scoreReason.Property(x => x.OpportunityId).HasColumnName("opportunity_id");
        scoreReason.Property(x => x.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(20);
        scoreReason.Property(x => x.Code).HasColumnName("code").HasMaxLength(100);
        scoreReason.Property(x => x.Points).HasColumnName("points");
        scoreReason.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(500);
        scoreReason.Property(x => x.CreatedAt).HasColumnName("created_at");
        scoreReason.HasOne<Opportunity>().WithMany(x => x.ScoreReasons).HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Cascade);
        opportunity.Navigation(x => x.ScoreReasons).UsePropertyAccessMode(PropertyAccessMode.Field);
        scoreReason.HasIndex(x => new { x.OpportunityId, x.Kind });

        var blocklist = modelBuilder.Entity<BlocklistEntry>();
        blocklist.ToTable("blocklist_entries");
        blocklist.HasKey(x => x.Id);
        blocklist.Property(x => x.Id).HasColumnName("id");
        blocklist.Property(x => x.ProjectId).HasColumnName("project_id");
        blocklist.Property(x => x.MatchType).HasColumnName("match_type").HasConversion<string>().HasMaxLength(30);
        blocklist.Property(x => x.Value).HasColumnName("value").HasMaxLength(2_048);
        blocklist.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        blocklist.Property(x => x.Enabled).HasColumnName("enabled");
        blocklist.Property(x => x.CreatedAt).HasColumnName("created_at");
        blocklist.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        blocklist.HasIndex(x => new { x.ProjectId, x.MatchType, x.Value }).IsUnique();
        blocklist.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });

        var policy = modelBuilder.Entity<PolicyDefinition>();
        policy.ToTable("policy_definitions", table =>
        {
            table.HasCheckConstraint("ck_policy_quality_score", "minimum_quality_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_policy_risk_score", "maximum_risk_score BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_policy_hourly_limit", "hourly_action_limit BETWEEN 0 AND 10000");
            table.HasCheckConstraint("ck_policy_daily_limit", "daily_action_limit BETWEEN 0 AND 100000");
            table.HasCheckConstraint("ck_policy_domain_limit", "per_domain_action_limit BETWEEN 0 AND 10000");
        });
        policy.HasKey(x => x.Id);
        policy.Property(x => x.Id).HasColumnName("id");
        policy.Property(x => x.ProjectId).HasColumnName("project_id");
        policy.Property(x => x.AutomationEnabled).HasColumnName("automation_enabled");
        policy.Property(x => x.MinimumQualityScore).HasColumnName("minimum_quality_score");
        policy.Property(x => x.MaximumRiskScore).HasColumnName("maximum_risk_score");
        policy.Property(x => x.ManualReviewRequired).HasColumnName("manual_review_required");
        policy.Property(x => x.HourlyActionLimit).HasColumnName("hourly_action_limit");
        policy.Property(x => x.DailyActionLimit).HasColumnName("daily_action_limit");
        policy.Property(x => x.PerDomainActionLimit).HasColumnName("per_domain_action_limit");
        policy.Property(x => x.CreatedAt).HasColumnName("created_at");
        policy.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        policy.HasOne<Project>().WithOne().HasForeignKey<PolicyDefinition>(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        policy.HasIndex(x => x.ProjectId).IsUnique();
    }

    private static void ConfigureJobs(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<PersistentJob>();
        job.ToTable("jobs", table =>
        {
            table.HasCheckConstraint("ck_jobs_recovery_count", "recovery_count >= 0");
            table.HasCheckConstraint("ck_jobs_domain", "domain IS NULL OR (char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain))");
        }).HasKey(x => x.Id);
        job.Property(x => x.Id).HasColumnName("id");
        job.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(30);
        job.Property(x => x.ProjectId).HasColumnName("project_id");
        job.Property(x => x.CampaignId).HasColumnName("campaign_id");
        job.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        job.Property(x => x.Priority).HasColumnName("priority");
        job.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb");
        job.Property(x => x.CreatedAt).HasColumnName("created_at");
        job.Property(x => x.AvailableAt).HasColumnName("available_at");
        job.Property(x => x.ClaimedAt).HasColumnName("claimed_at");
        job.Property(x => x.ClaimExpiresAt).HasColumnName("claim_expires_at");
        job.Property(x => x.StartedAt).HasColumnName("started_at");
        job.Property(x => x.CompletedAt).HasColumnName("completed_at");
        job.Property(x => x.WorkerId).HasColumnName("worker_id").HasMaxLength(200);
        job.Property(x => x.AttemptCount).HasColumnName("attempt_count");
        job.Property(x => x.MaxAttempts).HasColumnName("max_attempts");
        job.Property(x => x.LastError).HasColumnName("last_error").HasMaxLength(4_000);
        job.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(100);
        job.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200);
        job.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        job.Property(x => x.LastHeartbeatAt).HasColumnName("last_heartbeat_at");
        job.Property(x => x.PauseRequestedAt).HasColumnName("pause_requested_at");
        job.Property(x => x.PausedAt).HasColumnName("paused_at");
        job.Property(x => x.LastFailureKind).HasColumnName("last_failure_kind").HasConversion<string>().HasMaxLength(40);
        job.Property(x => x.RecoveryCount).HasColumnName("recovery_count");
        job.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        job.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.SetNull);
        job.HasIndex(x => new { x.ProjectId, x.Type, x.IdempotencyKey }).IsUnique();
        job.HasIndex(x => new { x.Status, x.AvailableAt, x.Priority, x.CreatedAt });
        job.HasIndex(x => x.ClaimExpiresAt);
        job.HasIndex(x => new { x.Domain, x.Status, x.ClaimExpiresAt });
        job.HasIndex(x => new { x.CampaignId, x.Status, x.ClaimExpiresAt });
        job.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        job.HasIndex(x => new { x.Priority, x.AvailableAt, x.CreatedAt })
            .IsDescending(true, false, false)
            .HasFilter("status IN ('Queued', 'RetryScheduled') AND pause_requested_at IS NULL")
            .HasDatabaseName("ix_jobs_claim_ready");
        job.HasIndex(x => x.ClaimExpiresAt)
            .HasFilter("status IN ('Claimed', 'Running')")
            .HasDatabaseName("ix_jobs_expired_claims");
        job.HasIndex(x => new { x.ProjectId, x.ClaimExpiresAt })
            .HasFilter("status IN ('Claimed', 'Running')")
            .HasDatabaseName("ix_jobs_active_project");
        job.HasIndex(x => new { x.ProjectId, x.CampaignId, x.CompletedAt })
            .HasFilter("type = 'Submission' AND status = 'Succeeded'")
            .HasDatabaseName("ix_jobs_submission_project_campaign_completed");
        job.HasIndex(x => new { x.ProjectId, x.Domain, x.CompletedAt })
            .HasFilter("type = 'Submission' AND status = 'Succeeded'")
            .HasDatabaseName("ix_jobs_submission_project_domain_completed");
        job.HasIndex(x => new { x.CampaignId, x.CompletedAt })
            .HasFilter("type = 'Submission' AND status = 'Succeeded'")
            .HasDatabaseName("ix_jobs_submission_campaign_completed");
    }

    private static void ConfigureSubmissions(ModelBuilder modelBuilder)
    {
        var campaign = modelBuilder.Entity<Campaign>();
        campaign.ToTable("campaigns", table => table.HasCheckConstraint("ck_campaign_daily_limit", "daily_action_limit BETWEEN 1 AND 100000")).HasKey(x => x.Id);
        campaign.Property(x => x.Id).HasColumnName("id");
        campaign.Property(x => x.ProjectId).HasColumnName("project_id");
        campaign.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        campaign.Property(x => x.ApprovalMode).HasColumnName("approval_mode").HasConversion<string>().HasMaxLength(30);
        campaign.Property(x => x.AuthorizationProfileKey).HasColumnName("authorization_profile_key").HasMaxLength(100);
        campaign.Property(x => x.AuthorizationReference).HasColumnName("authorization_reference").HasMaxLength(500);
        campaign.Property(x => x.DailyActionLimit).HasColumnName("daily_action_limit");
        campaign.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        campaign.Property(x => x.CreatedAt).HasColumnName("created_at");
        campaign.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        campaign.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        campaign.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });

        var ownedCampaign = modelBuilder.Entity<OwnedNetworkCampaignConfiguration>();
        ownedCampaign.ToTable("owned_network_campaign_configurations", table =>
        {
            table.HasCheckConstraint("ck_owned_campaign_concurrency", "global_concurrency BETWEEN 1 AND 10000 AND per_domain_concurrency BETWEEN 1 AND global_concurrency");
            table.HasCheckConstraint("ck_owned_campaign_domain_delay", "per_domain_delay_milliseconds BETWEEN 0 AND 86400000");
            table.HasCheckConstraint("ck_owned_campaign_attempts", "maximum_attempts BETWEEN 1 AND 20");
            table.HasCheckConstraint("ck_owned_campaign_verification_delay", "verification_delay_seconds BETWEEN 0 AND 2592000");
            table.HasCheckConstraint("ck_owned_campaign_sources_queued", "sources_queued >= 0");
        }).HasKey(x => x.CampaignId);
        ownedCampaign.Property(x => x.CampaignId).HasColumnName("campaign_id");
        ownedCampaign.Property(x => x.ProjectId).HasColumnName("project_id");
        ownedCampaign.Property(x => x.OwnedNetworkProfileId).HasColumnName("owned_network_profile_id");
        ownedCampaign.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2_048);
        ownedCampaign.Property(x => x.IdentityPoolId).HasColumnName("identity_pool_id");
        ownedCampaign.Property(x => x.TemplatePoolId).HasColumnName("template_pool_id");
        ownedCampaign.Property(x => x.GlobalConcurrency).HasColumnName("global_concurrency");
        ownedCampaign.Property(x => x.PerDomainConcurrency).HasColumnName("per_domain_concurrency");
        ownedCampaign.Property(x => x.PerDomainDelayMilliseconds).HasColumnName("per_domain_delay_milliseconds");
        ownedCampaign.Property(x => x.MaximumAttempts).HasColumnName("maximum_attempts");
        ownedCampaign.Property(x => x.VerificationDelaySeconds).HasColumnName("verification_delay_seconds");
        ownedCampaign.Property(x => x.Mode).HasColumnName("mode").HasConversion<string>().HasMaxLength(40);
        ownedCampaign.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        ownedCampaign.Property(x => x.Platform).HasColumnName("platform").HasConversion<string>().HasMaxLength(40);
        ownedCampaign.Property(x => x.CmsType).HasColumnName("cms_type").HasConversion<string>().HasMaxLength(40);
        ownedCampaign.Property(x => x.TechnicalCompatibility).HasColumnName("technical_compatibility").HasConversion<string>().HasMaxLength(40);
        ownedCampaign.Property(x => x.ValidationStatus).HasColumnName("validation_status").HasConversion<string>().HasMaxLength(30);
        ownedCampaign.Property(x => x.Tag).HasColumnName("tag").HasMaxLength(100);
        ownedCampaign.Property(x => x.PreviousSubmissionStatus).HasColumnName("previous_submission_status").HasConversion<string>().HasMaxLength(30);
        ownedCampaign.Property(x => x.PreviousVerificationStatus).HasColumnName("previous_verification_status").HasConversion<string>().HasMaxLength(30);
        ownedCampaign.Property(x => x.LastSourceCreatedAt).HasColumnName("last_source_created_at");
        ownedCampaign.Property(x => x.LastSourceId).HasColumnName("last_source_id");
        ownedCampaign.Property(x => x.SourcesQueued).HasColumnName("sources_queued");
        ownedCampaign.Property(x => x.ExpansionCompleted).HasColumnName("expansion_completed");
        ownedCampaign.Property(x => x.CreatedAt).HasColumnName("created_at");
        ownedCampaign.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        ownedCampaign.HasOne<Campaign>().WithOne().HasForeignKey<OwnedNetworkCampaignConfiguration>(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        ownedCampaign.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        ownedCampaign.HasOne<OwnedNetworkProfile>().WithMany().HasForeignKey(x => x.OwnedNetworkProfileId).OnDelete(DeleteBehavior.Restrict);
        ownedCampaign.HasOne<SubmissionIdentityPool>().WithMany().HasForeignKey(x => x.IdentityPoolId).OnDelete(DeleteBehavior.Restrict);
        ownedCampaign.HasOne<SubmissionTemplatePool>().WithMany().HasForeignKey(x => x.TemplatePoolId).OnDelete(DeleteBehavior.Restrict);
        ownedCampaign.HasIndex(x => new { x.OwnedNetworkProfileId, x.ExpansionCompleted, x.CreatedAt });

        var target = modelBuilder.Entity<CampaignTarget>();
        target.ToTable("campaign_targets").HasKey(x => x.Id);
        target.Property(x => x.Id).HasColumnName("id");
        target.Property(x => x.CampaignId).HasColumnName("campaign_id");
        target.Property(x => x.ProjectTargetId).HasColumnName("project_target_id");
        target.Property(x => x.CreatedAt).HasColumnName("created_at");
        target.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        target.HasOne<ProjectTarget>().WithMany().HasForeignKey(x => x.ProjectTargetId).OnDelete(DeleteBehavior.Restrict);
        target.HasIndex(x => new { x.CampaignId, x.ProjectTargetId }).IsUnique();

        var opportunity = modelBuilder.Entity<CampaignOpportunity>();
        opportunity.ToTable("campaign_opportunities").HasKey(x => x.Id);
        opportunity.Property(x => x.Id).HasColumnName("id");
        opportunity.Property(x => x.CampaignId).HasColumnName("campaign_id");
        opportunity.Property(x => x.OpportunityId).HasColumnName("opportunity_id");
        opportunity.Property(x => x.ExplicitlyApproved).HasColumnName("explicitly_approved");
        opportunity.Property(x => x.CreatedAt).HasColumnName("created_at");
        opportunity.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        opportunity.HasOne<Opportunity>().WithMany().HasForeignKey(x => x.OpportunityId).OnDelete(DeleteBehavior.Restrict);
        opportunity.HasIndex(x => new { x.CampaignId, x.OpportunityId }).IsUnique();

        var submission = modelBuilder.Entity<SubmissionJob>();
        submission.ToTable("submission_jobs").HasKey(x => x.Id);
        submission.Property(x => x.Id).HasColumnName("id");
        submission.Property(x => x.ProjectId).HasColumnName("project_id");
        submission.Property(x => x.CampaignId).HasColumnName("campaign_id");
        submission.Property(x => x.CampaignOpportunityId).HasColumnName("campaign_opportunity_id");
        submission.Property(x => x.SubmissionSourceId).HasColumnName("submission_source_id");
        submission.Property(x => x.PersistentJobId).HasColumnName("persistent_job_id");
        submission.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2_048);
        submission.Property(x => x.PlacementType).HasColumnName("placement_type").HasConversion<string>().HasMaxLength(40);
        submission.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        submission.Property(x => x.CreatedAt).HasColumnName("created_at");
        submission.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        submission.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        submission.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
        submission.HasOne<CampaignOpportunity>().WithOne().HasForeignKey<SubmissionJob>(x => x.CampaignOpportunityId).OnDelete(DeleteBehavior.Restrict);
        submission.HasOne<SubmissionSource>().WithMany().HasForeignKey(x => x.SubmissionSourceId).OnDelete(DeleteBehavior.Restrict);
        submission.HasOne<PersistentJob>().WithOne().HasForeignKey<SubmissionJob>(x => x.PersistentJobId).OnDelete(DeleteBehavior.Restrict);
        submission.HasIndex(x => x.CampaignOpportunityId).IsUnique().HasFilter("campaign_opportunity_id IS NOT NULL");
        submission.HasIndex(x => x.PersistentJobId).IsUnique();
        submission.HasIndex(x => new { x.CampaignId, x.CreatedAt, x.Id });
        submission.HasIndex(x => new { x.SubmissionSourceId, x.Status });
        submission.HasIndex(x => new { x.CampaignId, x.SubmissionSourceId, x.TargetUrl, x.PlacementType }).IsUnique()
            .HasFilter("submission_source_id IS NOT NULL");

        var attempt = modelBuilder.Entity<SubmissionAttempt>();
        attempt.ToTable("submission_attempts", table => table.HasCheckConstraint("ck_submission_attempt_number", "attempt_number > 0")).HasKey(x => x.Id);
        attempt.Property(x => x.Id).HasColumnName("id");
        attempt.Property(x => x.SubmissionJobId).HasColumnName("submission_job_id");
        attempt.Property(x => x.PersistentJobId).HasColumnName("persistent_job_id");
        attempt.Property(x => x.ProjectId).HasColumnName("project_id");
        attempt.Property(x => x.CampaignId).HasColumnName("campaign_id");
        attempt.Property(x => x.SubmissionSourceId).HasColumnName("submission_source_id");
        attempt.Property(x => x.TargetUrl).HasColumnName("target_url").HasMaxLength(2_048);
        attempt.Property(x => x.AttemptNumber).HasColumnName("attempt_number");
        attempt.Property(x => x.Adapter).HasColumnName("adapter").HasMaxLength(100);
        attempt.Property(x => x.Strategy).HasColumnName("strategy").HasMaxLength(100);
        attempt.Property(x => x.IdentityId).HasColumnName("identity_id");
        attempt.Property(x => x.TemplateId).HasColumnName("template_id");
        attempt.Property(x => x.ResolvedDisplayName).HasColumnName("resolved_display_name").HasMaxLength(200);
        attempt.Property(x => x.ResolvedEmail).HasColumnName("resolved_email").HasMaxLength(320);
        attempt.Property(x => x.ResolvedWebsite).HasColumnName("resolved_website").HasMaxLength(2_048);
        attempt.Property(x => x.StartedAt).HasColumnName("started_at");
        attempt.Property(x => x.FinishedAt).HasColumnName("finished_at");
        attempt.Property(x => x.Result).HasColumnName("result").HasConversion<string>().HasMaxLength(30);
        attempt.Property(x => x.HttpStatus).HasColumnName("http_status");
        attempt.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(500);
        attempt.Property(x => x.Error).HasColumnName("error").HasMaxLength(2_000);
        attempt.Property(x => x.Endpoint).HasColumnName("endpoint").HasMaxLength(2_048);
        attempt.Property(x => x.RedirectDestination).HasColumnName("redirect_destination").HasMaxLength(2_048);
        attempt.Property(x => x.ModerationStatus).HasColumnName("moderation_status").HasConversion<string>().HasMaxLength(30);
        attempt.Property(x => x.FailureKind).HasColumnName("failure_kind").HasConversion<string>().HasMaxLength(40);
        attempt.HasOne<SubmissionJob>().WithMany().HasForeignKey(x => x.SubmissionJobId).OnDelete(DeleteBehavior.Cascade);
        attempt.HasOne<PersistentJob>().WithMany().HasForeignKey(x => x.PersistentJobId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<SubmissionSource>().WithMany().HasForeignKey(x => x.SubmissionSourceId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<SubmissionIdentity>().WithMany().HasForeignKey(x => x.IdentityId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasOne<SubmissionTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasIndex(x => new { x.SubmissionJobId, x.AttemptNumber }).IsUnique();
        attempt.HasIndex(x => new { x.ProjectId, x.CampaignId, x.StartedAt });
    }

    private static void ConfigureWorkerPlatform(ModelBuilder modelBuilder)
    {
        var heartbeat = modelBuilder.Entity<WorkerHeartbeatRow>();
        heartbeat.ToTable("worker_heartbeats", table =>
        {
            table.HasCheckConstraint("ck_worker_heartbeat_concurrency", "concurrency BETWEEN 1 AND 32");
            table.HasCheckConstraint("ck_worker_heartbeat_buffer", "buffer_size BETWEEN 1 AND 1000");
            table.HasCheckConstraint("ck_worker_heartbeat_active", "active_jobs BETWEEN 0 AND concurrency");
        }).HasKey(x => x.WorkerId);
        heartbeat.Property(x => x.WorkerId).HasColumnName("worker_id").HasMaxLength(200);
        heartbeat.Property(x => x.MachineName).HasColumnName("machine_name").HasMaxLength(200);
        heartbeat.Property(x => x.ProcessId).HasColumnName("process_id");
        heartbeat.Property(x => x.Concurrency).HasColumnName("concurrency");
        heartbeat.Property(x => x.BufferSize).HasColumnName("buffer_size");
        heartbeat.Property(x => x.ActiveJobs).HasColumnName("active_jobs");
        heartbeat.Property(x => x.StartedAt).HasColumnName("started_at");
        heartbeat.Property(x => x.LastHeartbeatAt).HasColumnName("last_heartbeat_at");
        heartbeat.Property(x => x.StoppedAt).HasColumnName("stopped_at");
        heartbeat.HasIndex(x => new { x.StoppedAt, x.LastHeartbeatAt });

        var rateLimit = modelBuilder.Entity<DomainRateLimitRow>();
        rateLimit.ToTable("domain_rate_limits", table => table.HasCheckConstraint("ck_domain_rate_limit_domain", "char_length(domain) BETWEEN 1 AND 253 AND domain = lower(domain)")).HasKey(x => new { x.ScopeKey, x.Domain });
        rateLimit.Property(x => x.ScopeKey).HasColumnName("scope_key").HasMaxLength(100);
        rateLimit.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(253);
        rateLimit.Property(x => x.ProjectId).HasColumnName("project_id");
        rateLimit.Property(x => x.CampaignId).HasColumnName("campaign_id");
        rateLimit.Property(x => x.NextAllowedAt).HasColumnName("next_allowed_at");
        rateLimit.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        rateLimit.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        rateLimit.HasIndex(x => x.NextAllowedAt);
    }

    private static void ConfigureDiscovery(ModelBuilder modelBuilder)
    {
        var query = modelBuilder.Entity<DiscoveryQuery>();
        query.ToTable("discovery_queries", table => table.HasCheckConstraint("ck_discovery_query_maximum_results", "maximum_results BETWEEN 1 AND 5000"));
        query.HasKey(x => x.Id);
        query.Property(x => x.Id).HasColumnName("id");
        query.Property(x => x.ProjectId).HasColumnName("project_id");
        query.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(50);
        query.Property(x => x.QueryText).HasColumnName("query_text").HasMaxLength(2_000);
        query.Property(x => x.InputJson).HasColumnName("input_json").HasColumnType("jsonb");
        query.Property(x => x.MaximumResults).HasColumnName("maximum_results");
        query.Property(x => x.CreatedAt).HasColumnName("created_at");
        query.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        query.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });

        var run = modelBuilder.Entity<DiscoveryRun>();
        run.ToTable("discovery_runs", table =>
        {
            table.HasCheckConstraint("ck_discovery_run_discovered", "urls_discovered >= 0");
            table.HasCheckConstraint("ck_discovery_run_accepted", "urls_accepted >= 0");
            table.HasCheckConstraint("ck_discovery_run_duplicates", "duplicate_count >= 0");
            table.HasCheckConstraint("ck_discovery_run_blocked", "blocked_count >= 0");
            table.HasCheckConstraint("ck_discovery_run_invalid", "invalid_count >= 0");
            table.HasCheckConstraint("ck_discovery_run_errors", "error_count >= 0");
        });
        run.HasKey(x => x.Id);
        run.Property(x => x.Id).HasColumnName("id");
        run.Property(x => x.ProjectId).HasColumnName("project_id");
        run.Property(x => x.DiscoveryQueryId).HasColumnName("discovery_query_id");
        run.Property(x => x.JobId).HasColumnName("job_id");
        run.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(30);
        run.Property(x => x.CreatedAt).HasColumnName("created_at");
        run.Property(x => x.StartedAt).HasColumnName("started_at");
        run.Property(x => x.FinishedAt).HasColumnName("finished_at");
        run.Property(x => x.UrlsDiscovered).HasColumnName("urls_discovered");
        run.Property(x => x.UrlsAccepted).HasColumnName("urls_accepted");
        run.Property(x => x.DuplicateCount).HasColumnName("duplicate_count");
        run.Property(x => x.BlockedCount).HasColumnName("blocked_count");
        run.Property(x => x.InvalidCount).HasColumnName("invalid_count");
        run.Property(x => x.ErrorCount).HasColumnName("error_count");
        run.Property(x => x.Errors).HasColumnName("errors").HasColumnType("text[]");
        run.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        run.HasOne<DiscoveryQuery>().WithMany().HasForeignKey(x => x.DiscoveryQueryId).OnDelete(DeleteBehavior.Cascade);
        run.HasOne<PersistentJob>().WithOne().HasForeignKey<DiscoveryRun>(x => x.JobId).OnDelete(DeleteBehavior.SetNull);
        run.HasIndex(x => x.DiscoveryQueryId).IsUnique();
        run.HasIndex(x => x.JobId).IsUnique();
        run.HasIndex(x => new { x.ProjectId, x.CreatedAt, x.Id });
        run.HasIndex(x => new { x.ProjectId, x.Status });
    }
}

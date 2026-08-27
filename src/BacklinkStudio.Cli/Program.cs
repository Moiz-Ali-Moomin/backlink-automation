using System.Text;
using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Discovery;
using BacklinkStudio.Domain;
using BacklinkStudio.Infrastructure;
using BacklinkStudio.Opportunities;
using BacklinkStudio.Reporting;
using BacklinkStudio.Submission;
using BacklinkStudio.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddBacklinkStudioInfrastructure(builder.Configuration);
builder.Services.AddBacklinkStudioDiscovery();
builder.Services.AddScoped<IOpportunityService, OpportunityService>();
builder.Services.AddBacklinkStudioReporting();
builder.Services.AddBacklinkStudioSubmission(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddBacklinkStudioVerification();
using var host = builder.Build();

if (args.Length < 2)
{
    PrintHelp();
    return 2;
}

var actor = new ActorContext(ActorType.Agent, Environment.UserName, null, Guid.CreateVersion7().ToString("D"), "local");
try
{
    await using var scope = host.Services.CreateAsyncScope();
    object result = (args[0].ToLowerInvariant(), args[1].ToLowerInvariant()) switch
    {
        ("project", "list") => await scope.ServiceProvider.GetRequiredService<IProjectService>().ListAsync(new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("project", "create") => await scope.ServiceProvider.GetRequiredService<IProjectService>().CreateAsync(new CreateProjectCommand(RequiredOption(args, "--name"), RequiredOption(args, "--domain"), StringOption(args, "--description"), RequestKey(args)), actor, CancellationToken.None),
        ("target", "add") => await scope.ServiceProvider.GetRequiredService<IProjectService>().AddTargetAsync(new AddTargetCommand(GuidOption(args, "--project"), RequiredOption(args, "--url"), StringOption(args, "--label"), null, null, null, 50, RequestKey(args)), actor, CancellationToken.None),
        ("network", "create") => await CreateNetworkAsync(scope.ServiceProvider, args, actor),
        ("network", "list") => await scope.ServiceProvider.GetRequiredService<IOwnedNetworkService>().ListAsync(GuidOption(args, "--project"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("network", "get") => await scope.ServiceProvider.GetRequiredService<IOwnedNetworkService>().GetAsync(GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("OwnedNetworkProfile", GuidOption(args, "--id")),
        ("network", "update") => await UpdateNetworkAsync(scope.ServiceProvider, args, actor),
        ("sources", "import") => await ImportSourcesAsync(scope.ServiceProvider, args, actor),
        ("sources", "validate") => await scope.ServiceProvider.GetRequiredService<ISubmissionSourceService>().ValidateAsync(new ValidateSubmissionSourcesCommand(GuidOption(args, "--project"), OptionalGuidOption(args, "--network"), IntOption(args, "--maximum-sources") ?? 100_000, RequestKey(args)), actor, CancellationToken.None),
        ("sources", "list") => await scope.ServiceProvider.GetRequiredService<ISubmissionSourceService>().ListAsync(GuidOption(args, "--project"), new SubmissionSourceFilter(OwnedNetworkProfileId: OptionalGuidOption(args, "--network"), Domain: StringOption(args, "--domain"), Platform: OptionalEnumOption<SourcePlatform>(args, "--platform"), CmsType: OptionalEnumOption<CmsType>(args, "--cms"), OwnershipStatus: OptionalEnumOption<OwnershipStatus>(args, "--ownership"), AutomationPermitted: BoolOption(args, "--automation-permitted"), TechnicalCompatibility: OptionalEnumOption<TechnicalCompatibility>(args, "--compatibility"), ValidationStatus: OptionalEnumOption<SubmissionSourceValidationStatus>(args, "--validation-status"), Enabled: BoolOption(args, "--enabled"), Tag: StringOption(args, "--tag"), PreviousSubmissionStatus: OptionalEnumOption<SubmissionStatus>(args, "--previous-submission-status"), PreviousVerificationStatus: OptionalEnumOption<BacklinkStatus>(args, "--previous-verification-status")), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("sources", "get") => await scope.ServiceProvider.GetRequiredService<ISubmissionSourceService>().GetAsync(GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("SubmissionSource", GuidOption(args, "--id")),
        ("sources", "import-status") => await scope.ServiceProvider.GetRequiredService<ISubmissionSourceService>().GetImportAsync(GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("SubmissionSourceImport", GuidOption(args, "--id")),
        ("identity-pool", "create") => await scope.ServiceProvider.GetRequiredService<ISubmissionIdentityService>().CreatePoolAsync(new(
            GuidOption(args, "--project"), RequiredOption(args, "--name"), EnumOption<PoolSelectionStrategy>(args, "--selection"),
            EnumOption<IdentityEmailStrategy>(args, "--email-strategy"), StringOption(args, "--email-base"), StringOption(args, "--catch-all-domain"),
            BoolOption(args, "--enabled") ?? true, RequestKey(args)), actor, CancellationToken.None),
        ("identity-pool", "list") => await scope.ServiceProvider.GetRequiredService<ISubmissionIdentityService>().ListPoolsAsync(
            GuidOption(args, "--project"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("identity-pool", "get") => await scope.ServiceProvider.GetRequiredService<ISubmissionIdentityService>().GetPoolAsync(GuidOption(args, "--id"), CancellationToken.None)
            ?? throw new ResourceNotFoundException("SubmissionIdentityPool", GuidOption(args, "--id")),
        ("identity-pool", "update") => await UpdateIdentityPoolAsync(scope.ServiceProvider, args, actor),
        ("identity", "create") => await scope.ServiceProvider.GetRequiredService<ISubmissionIdentityService>().CreateIdentityAsync(new(
            GuidOption(args, "--pool"), RequiredOption(args, "--display-name"), RequiredOption(args, "--email"), StringOption(args, "--website"),
            StringOption(args, "--organization"), BoolOption(args, "--enabled") ?? true, IntOption(args, "--weight") ?? 1, RequestKey(args)), actor, CancellationToken.None),
        ("identity", "update") => await UpdateIdentityAsync(scope.ServiceProvider, args, actor),
        ("template-pool", "create") => await scope.ServiceProvider.GetRequiredService<ISubmissionTemplateService>().CreatePoolAsync(new(
            GuidOption(args, "--project"), RequiredOption(args, "--name"), EnumOption<SubmissionTemplateType>(args, "--type"),
            EnumOption<PoolSelectionStrategy>(args, "--selection"), EnumOption<BacklinkPlacementMethod>(args, "--placement"),
            BoolOption(args, "--enabled") ?? true, RequestKey(args)), actor, CancellationToken.None),
        ("template-pool", "list") => await scope.ServiceProvider.GetRequiredService<ISubmissionTemplateService>().ListPoolsAsync(
            GuidOption(args, "--project"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("template-pool", "get") => await scope.ServiceProvider.GetRequiredService<ISubmissionTemplateService>().GetPoolAsync(GuidOption(args, "--id"), CancellationToken.None)
            ?? throw new ResourceNotFoundException("SubmissionTemplatePool", GuidOption(args, "--id")),
        ("template-pool", "update") => await UpdateTemplatePoolAsync(scope.ServiceProvider, args, actor),
        ("template", "create") => await scope.ServiceProvider.GetRequiredService<ISubmissionTemplateService>().CreateTemplateAsync(new(
            GuidOption(args, "--pool"), RequiredOption(args, "--name"), RequiredOption(args, "--body"), MultiOption(args, "--prefix"),
            MultiOption(args, "--suffix"), MultiOption(args, "--anchor"), MultiOption(args, "--target-variant"),
            BoolOption(args, "--enabled") ?? true, IntOption(args, "--weight") ?? 1, RequestKey(args)), actor, CancellationToken.None),
        ("template", "update") => await UpdateTemplateAsync(scope.ServiceProvider, args, actor),
        ("submission", "preview") => await scope.ServiceProvider.GetRequiredService<ISubmissionPreviewService>().PreviewAsync(new(
            GuidOption(args, "--project"), GuidOption(args, "--source"), OptionalGuidOption(args, "--campaign"),
            OptionalGuidOption(args, "--identity-pool"), OptionalGuidOption(args, "--template-pool"), RequiredOption(args, "--target"),
            IntOption(args, "--attempt") ?? 1), CancellationToken.None),
        ("campaign", "create") => await CreateOwnedCampaignAsync(scope.ServiceProvider, args, actor),
        ("campaign", "start") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().StartAsync(new(
            GuidOption(args, "--id"), RequestKey(args)), actor, CancellationToken.None),
        ("campaign", "status") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().GetAsync(
            GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("Campaign", GuidOption(args, "--id")),
        ("campaign", "pause") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().PauseAsync(new(
            GuidOption(args, "--id"), RequestKey(args)), actor, CancellationToken.None),
        ("campaign", "resume") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().ResumeAsync(new(
            GuidOption(args, "--id"), RequestKey(args)), actor, CancellationToken.None),
        ("campaign", "stop") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().StopAsync(new(
            GuidOption(args, "--id"), RequestKey(args)), actor, CancellationToken.None),
        ("submissions", "list") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().ListSubmissionsAsync(
            GuidOption(args, "--campaign"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("submissions", "get") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().GetSubmissionAsync(
            GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("SubmissionJob", GuidOption(args, "--id")),
        ("submissions", "attempts") => await scope.ServiceProvider.GetRequiredService<ICampaignService>().ListAttemptsAsync(
            GuidOption(args, "--id"), CancellationToken.None),
        ("backlinks", "list") => await scope.ServiceProvider.GetRequiredService<IVerificationService>().ListAsync(
            GuidOption(args, "--project"), OptionalEnumOption<BacklinkStatus>(args, "--status"),
            new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("backlinks", "get") => await scope.ServiceProvider.GetRequiredService<IVerificationService>().GetAsync(
            GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("Backlink", GuidOption(args, "--id")),
        ("backlinks", "start") => await StartSimpleWorkflowAsync(scope.ServiceProvider, args, actor),
        ("backlinks", "status") => await scope.ServiceProvider.GetRequiredService<IBacklinkWorkflowService>().GetAsync(
            GuidOption(args, "--id"), new PageRequest(IntOption(args, "--limit") ?? 50,
                StringOption(args, "--cursor")), CancellationToken.None)
            ?? throw new ResourceNotFoundException("BacklinkWorkflow", GuidOption(args, "--id")),
        ("verification", "start") => await scope.ServiceProvider.GetRequiredService<IVerificationService>().StartAsync(new(
            GuidOption(args, "--backlink"), RequestKey(args)), actor, CancellationToken.None),
        ("backlinks", "run") => await RunOwnedNetworkWorkflowAsync(scope.ServiceProvider, args, actor),
        ("candidate", "import") => await ImportAsync(scope.ServiceProvider, args, actor),
        ("candidate", "list") => await scope.ServiceProvider.GetRequiredService<ICandidateService>().ListAsync(GuidOption(args, "--project"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("analysis", "start") => await scope.ServiceProvider.GetRequiredService<IOpportunityService>().StartAnalysisAsync(new StartAnalysisCommand(GuidOption(args, "--project"), RequestKey(args)), actor, CancellationToken.None),
        ("opportunity", "list") => await scope.ServiceProvider.GetRequiredService<IOpportunityService>().ListAsync(GuidOption(args, "--project"), IntOption(args, "--minimum-quality"), IntOption(args, "--maximum-risk"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("job", "get") => await scope.ServiceProvider.GetRequiredService<IJobService>().GetAsync(GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("Job", GuidOption(args, "--id")),
        ("report", "generate") => await scope.ServiceProvider.GetRequiredService<IReportService>().GenerateAsync(new GenerateReportCommand(GuidOption(args, "--project"), OptionalGuidOption(args, "--campaign"), EnumOption<ReportKind>(args, "--kind"), EnumOption<ReportFormat>(args, "--format"), RequestKey(args)), actor, CancellationToken.None),
        ("report", "list") => await scope.ServiceProvider.GetRequiredService<IReportService>().ListAsync(GuidOption(args, "--project"), new PageRequest(IntOption(args, "--limit") ?? 50, StringOption(args, "--cursor")), CancellationToken.None),
        ("report", "get") => await scope.ServiceProvider.GetRequiredService<IReportService>().GetAsync(GuidOption(args, "--id"), CancellationToken.None) ?? throw new ResourceNotFoundException("Report", GuidOption(args, "--id")),
        _ => throw new ValidationException("Unknown command.")
    };

    Console.WriteLine(JsonSerializer.Serialize(result, CliJson.Options));
    return 0;
}

catch (BacklinkStudioException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task<ImportCandidatesResult> ImportAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var separator = Array.IndexOf(args, "--urls");
    if (separator < 0 || separator == args.Length - 1)
    {
        throw new ValidationException("candidate import requires --urls followed by one or more URLs.");
    }

    return await services.GetRequiredService<ICandidateService>().ImportAsync(
        new ImportCandidatesCommand(GuidOption(args, "--project"), args[(separator + 1)..], RequestKey(args)),
        actor,
        CancellationToken.None);
}

static Task<OwnedNetworkProfileDto> CreateNetworkAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var domains = MultiOption(args, "--domain");
    if (domains.Length == 0) throw new ValidationException("network create requires at least one --domain value.");
    var matchType = OptionalEnumOption<OwnedNetworkDomainMatchType>(args, "--match-type") ?? OwnedNetworkDomainMatchType.ExactDomain;
    return services.GetRequiredService<IOwnedNetworkService>().CreateAsync(new CreateOwnedNetworkCommand(
        GuidOption(args, "--project"), RequiredOption(args, "--name"), StringOption(args, "--description"),
        EnumOption<OwnershipStatus>(args, "--ownership"), RequiredBoolOption(args, "--automation-permitted"),
        domains.Select(x => new OwnedNetworkDomainInput(x, matchType)).ToArray(), StringOption(args, "--tag"),
        OptionalGuidOption(args, "--identity-pool"), OptionalGuidOption(args, "--template-pool"),
        IntOption(args, "--concurrency") ?? 100, IntOption(args, "--per-domain-concurrency") ?? 2,
        IntOption(args, "--per-domain-delay-ms") ?? 1_000, BoolOption(args, "--enabled") ?? true, RequestKey(args)),
        actor, CancellationToken.None);
}

static async Task<OwnedNetworkProfileDto> UpdateNetworkAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var service = services.GetRequiredService<IOwnedNetworkService>();
    var id = GuidOption(args, "--id");
    var current = await service.GetAsync(id, CancellationToken.None) ?? throw new ResourceNotFoundException("OwnedNetworkProfile", id);
    var domains = MultiOption(args, "--domain");
    var matchType = OptionalEnumOption<OwnedNetworkDomainMatchType>(args, "--match-type") ?? OwnedNetworkDomainMatchType.ExactDomain;
    var rules = domains.Length == 0
        ? current.Domains.Select(x => new OwnedNetworkDomainInput(x.Domain, x.MatchType, x.Enabled)).ToArray()
        : domains.Select(x => new OwnedNetworkDomainInput(x, matchType)).ToArray();
    return await service.UpdateAsync(new UpdateOwnedNetworkCommand(id, StringOption(args, "--name") ?? current.Name,
        StringOption(args, "--description") ?? current.Description,
        OptionalEnumOption<OwnershipStatus>(args, "--ownership") ?? current.OwnershipStatus,
        BoolOption(args, "--automation-permitted") ?? current.AutomationPermitted, rules,
        StringOption(args, "--tag") ?? current.OptionalNetworkTag, OptionalGuidOption(args, "--identity-pool") ?? current.DefaultIdentityPoolId,
        OptionalGuidOption(args, "--template-pool") ?? current.DefaultTemplatePoolId,
        IntOption(args, "--concurrency") ?? current.MaxConcurrency,
        IntOption(args, "--per-domain-concurrency") ?? current.PerDomainConcurrency,
        IntOption(args, "--per-domain-delay-ms") ?? current.PerDomainDelayMilliseconds,
        BoolOption(args, "--enabled") ?? current.Enabled, RequestKey(args)), actor, CancellationToken.None);
}

static async Task<SubmissionSourceImportAcceptedDto> ImportSourcesAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    if (args.Length < 3) throw new ValidationException("sources import requires a TXT or CSV file path.");
    var path = Path.GetFullPath(args[2]);
    if (!File.Exists(path)) throw new ValidationException("Source import file was not found.");
    var format = OptionalEnumOption<SubmissionSourceImportFormat>(args, "--format")
        ?? (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? SubmissionSourceImportFormat.Csv : SubmissionSourceImportFormat.Txt);
    await using var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1_024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    return await services.GetRequiredService<ISubmissionSourceService>().ImportAsync(new ImportSubmissionSourcesCommand(
        GuidOption(args, "--project"), null, format, Path.GetFileName(path), StringOption(args, "--tag"), RequestKey(args)),
        content, actor, CancellationToken.None);
}

static async Task<OwnedNetworkWorkflowAcceptedDto> RunOwnedNetworkWorkflowAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var projectId = GuidOption(args, "--project");
    var path = Path.GetFullPath(RequiredOption(args, "--sources"));
    if (!File.Exists(path)) throw new ValidationException("Owned-network source file was not found.");
    var format = OptionalEnumOption<SubmissionSourceImportFormat>(args, "--format")
        ?? (Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ? SubmissionSourceImportFormat.Csv : SubmissionSourceImportFormat.Txt);
    var networkId = await ResolveNetworkIdAsync(services, projectId, RequiredOption(args, "--network"));
    var identityPoolId = await ResolveIdentityPoolIdAsync(services, projectId, RequiredOption(args, "--identity-pool"));
    var templatePoolId = await ResolveTemplatePoolIdAsync(services, projectId, RequiredOption(args, "--template-pool"));
    await using var content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1_024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);
    return await services.GetRequiredService<IOwnedNetworkWorkflowService>().StartAsync(new(projectId, networkId,
        format, Path.GetFileName(path), StringOption(args, "--tag"), StringOption(args, "--name") ?? "Owned network backlinks",
        RequiredOption(args, "--target"), identityPoolId, templatePoolId, IntOption(args, "--concurrency") ?? 100,
        IntOption(args, "--per-domain-concurrency") ?? 2, IntOption(args, "--per-domain-delay-ms") ?? 1_000,
        IntOption(args, "--maximum-attempts") ?? 3, IntOption(args, "--verification-delay-seconds") ?? 3_600,
        IntOption(args, "--maximum-sources") ?? 1_000_000, StringOption(args, "--domain"),
        OptionalEnumOption<SourcePlatform>(args, "--platform"), OptionalEnumOption<CmsType>(args, "--cms"),
        StringOption(args, "--tag"), IntOption(args, "--daily-limit") ?? 100_000, RequestKey(args)), content,
        actor, CancellationToken.None);
}

static async Task<BacklinkWorkflowAcceptedDto> StartSimpleWorkflowAsync(IServiceProvider services, string[] args,
    ActorContext actor)
{
    var projectId = await ResolveSimpleProjectIdAsync(services, args);
    var sourcesPath = Path.GetFullPath(RequiredOption(args, "--sources"));
    var identitiesPath = Path.GetFullPath(RequiredOption(args, "--identities"));
    var commentsPath = Path.GetFullPath(RequiredOption(args, "--comments"));
    if (!File.Exists(sourcesPath) || !File.Exists(identitiesPath) || !File.Exists(commentsPath))
        throw new ValidationException("Sources, identities, and comments files must exist.");
    var sourceUrls = await ReadSourceUrlsAsync(sourcesPath);
    var identities = (await File.ReadAllLinesAsync(identitiesPath)).Select((value, index) =>
    {
        var separator = value.IndexOf(',');
        if (separator <= 0 || separator == value.Length - 1)
            throw new ValidationException($"Identity line {index + 1} must contain name,email.");
        return new BacklinkWorkflowIdentityInput(value[..separator].Trim(), value[(separator + 1)..].Trim());
    }).ToArray();
    var comments = (await File.ReadAllLinesAsync(commentsPath)).Select(value => value.Trim())
        .Where(value => value.Length > 0).ToArray();
    return await services.GetRequiredService<IBacklinkWorkflowService>().StartAsync(new(projectId, sourceUrls,
        null, identities, null, comments, null, RequiredOption(args, "--target"),
        IntOption(args, "--concurrency"), IntOption(args, "--per-domain-concurrency"),
        IntOption(args, "--delay-ms") ?? IntOption(args, "--per-domain-delay-ms"), IntOption(args, "--maximum-attempts"),
        IntOption(args, "--verification-delay-seconds"), RequestKey(args)), actor, CancellationToken.None);
}

static async Task<string[]> ReadSourceUrlsAsync(string path)
{
    var lines = await File.ReadAllLinesAsync(path);
    if (!Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase))
        return lines.Select(value => value.Trim())
            .Where(value => value.Length > 0 && !value.StartsWith('#')).ToArray();

    var rows = lines.Where(value => !string.IsNullOrWhiteSpace(value)).Select(ParseCsvLine).ToArray();
    if (rows.Length == 0) return [];
    var urlIndex = Array.FindIndex(rows[0], value => value.Trim().Equals("url", StringComparison.OrdinalIgnoreCase));
    if (urlIndex < 0) throw new ValidationException("CSV sources require a url header.");
    return rows.Skip(1).Where(row => row.Length > urlIndex)
        .Select(row => row[urlIndex].Trim()).Where(value => value.Length > 0).ToArray();
}

static string[] ParseCsvLine(string line)
{
    var fields = new List<string>();
    var field = new StringBuilder();
    var quoted = false;
    for (var index = 0; index < line.Length; index++)
    {
        var character = line[index];
        if (character == '"')
        {
            if (quoted && index + 1 < line.Length && line[index + 1] == '"')
            {
                field.Append('"');
                index++;
            }
            else quoted = !quoted;
        }
        else if (character == ',' && !quoted)
        {
            fields.Add(field.ToString());
            field.Clear();
        }
        else field.Append(character);
    }
    if (quoted) throw new ValidationException("CSV sources contain an unterminated quoted field.");
    fields.Add(field.ToString());
    return fields.ToArray();
}

static async Task<Guid> ResolveSimpleProjectIdAsync(IServiceProvider services, string[] args)
{
    if (OptionalGuidOption(args, "--project") is { } explicitId) return explicitId;
    var page = await services.GetRequiredService<IProjectService>().ListAsync(new PageRequest(2, null),
        CancellationToken.None);
    if (page.Items.Count != 1)
        throw new ValidationException("Specify --project when BacklinkStudio contains zero or multiple projects.");
    return page.Items[0].Id;
}

static async Task<Guid> ResolveNetworkIdAsync(IServiceProvider services, Guid projectId, string value)
{
    if (Guid.TryParse(value, out var id)) return id;
    string? cursor = null;
    do
    {
        var page = await services.GetRequiredService<IOwnedNetworkService>().ListAsync(projectId, new(100, cursor), CancellationToken.None);
        var match = page.Items.SingleOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;
        cursor = page.NextCursor;
    } while (cursor is not null);
    throw new ValidationException($"Owned network '{value}' was not found in the project.");
}

static async Task<Guid> ResolveIdentityPoolIdAsync(IServiceProvider services, Guid projectId, string value)
{
    if (Guid.TryParse(value, out var id)) return id;
    string? cursor = null;
    do
    {
        var page = await services.GetRequiredService<ISubmissionIdentityService>().ListPoolsAsync(projectId, new(100, cursor), CancellationToken.None);
        var match = page.Items.SingleOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;
        cursor = page.NextCursor;
    } while (cursor is not null);
    throw new ValidationException($"Identity pool '{value}' was not found in the project.");
}

static async Task<Guid> ResolveTemplatePoolIdAsync(IServiceProvider services, Guid projectId, string value)
{
    if (Guid.TryParse(value, out var id)) return id;
    string? cursor = null;
    do
    {
        var page = await services.GetRequiredService<ISubmissionTemplateService>().ListPoolsAsync(projectId, new(100, cursor), CancellationToken.None);
        var match = page.Items.SingleOrDefault(x => x.Name.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (match is not null) return match.Id;
        cursor = page.NextCursor;
    } while (cursor is not null);
    throw new ValidationException($"Template pool '{value}' was not found in the project.");
}

static async Task<SubmissionIdentityDto> UpdateIdentityAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var service = services.GetRequiredService<ISubmissionIdentityService>();
    var id = GuidOption(args, "--id");
    var current = await service.GetIdentityAsync(id, CancellationToken.None) ?? throw new ResourceNotFoundException("SubmissionIdentity", id);
    return await service.UpdateIdentityAsync(new(id, StringOption(args, "--display-name") ?? current.DisplayName,
        StringOption(args, "--email") ?? current.Email, StringOption(args, "--website") ?? current.Website,
        StringOption(args, "--organization") ?? current.Organization, BoolOption(args, "--enabled") ?? current.Enabled,
        IntOption(args, "--weight") ?? current.Weight, RequestKey(args)), actor, CancellationToken.None);
}

static async Task<IdentityPoolDto> UpdateIdentityPoolAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var service = services.GetRequiredService<ISubmissionIdentityService>();
    var id = GuidOption(args, "--id");
    var current = await service.GetPoolAsync(id, CancellationToken.None)
        ?? throw new ResourceNotFoundException("SubmissionIdentityPool", id);
    return await service.UpdatePoolAsync(new(id, StringOption(args, "--name") ?? current.Name,
        OptionalEnumOption<PoolSelectionStrategy>(args, "--selection") ?? current.SelectionStrategy,
        OptionalEnumOption<IdentityEmailStrategy>(args, "--email-strategy") ?? current.EmailStrategy,
        StringOption(args, "--email-base") ?? current.EmailBaseAddress,
        StringOption(args, "--catch-all-domain") ?? current.CatchAllDomain,
        BoolOption(args, "--enabled") ?? current.Enabled, RequestKey(args)), actor, CancellationToken.None);
}

static async Task<TemplatePoolDto> UpdateTemplatePoolAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var service = services.GetRequiredService<ISubmissionTemplateService>();
    var id = GuidOption(args, "--id");
    var current = await service.GetPoolAsync(id, CancellationToken.None)
        ?? throw new ResourceNotFoundException("SubmissionTemplatePool", id);
    return await service.UpdatePoolAsync(new(id, StringOption(args, "--name") ?? current.Name,
        OptionalEnumOption<SubmissionTemplateType>(args, "--type") ?? current.TemplateType,
        OptionalEnumOption<PoolSelectionStrategy>(args, "--selection") ?? current.SelectionStrategy,
        OptionalEnumOption<BacklinkPlacementMethod>(args, "--placement") ?? current.PlacementMethod,
        BoolOption(args, "--enabled") ?? current.Enabled, RequestKey(args)), actor, CancellationToken.None);
}

static async Task<SubmissionTemplateDto> UpdateTemplateAsync(IServiceProvider services, string[] args, ActorContext actor)
{
    var service = services.GetRequiredService<ISubmissionTemplateService>();
    var id = GuidOption(args, "--id");
    var current = await service.GetTemplateAsync(id, CancellationToken.None) ?? throw new ResourceNotFoundException("SubmissionTemplate", id);
    var prefixes = MultiOption(args, "--prefix");
    var suffixes = MultiOption(args, "--suffix");
    var anchors = MultiOption(args, "--anchor");
    var targets = MultiOption(args, "--target-variant");
    return await service.UpdateTemplateAsync(new(id, StringOption(args, "--name") ?? current.Name,
        StringOption(args, "--body") ?? current.Body, prefixes.Length == 0 ? current.PrefixVariants : prefixes,
        suffixes.Length == 0 ? current.SuffixVariants : suffixes, anchors.Length == 0 ? current.AnchorVariants : anchors,
        targets.Length == 0 ? current.TargetUrlVariants : targets, BoolOption(args, "--enabled") ?? current.Enabled,
        IntOption(args, "--weight") ?? current.Weight, RequestKey(args)), actor, CancellationToken.None);
}

static Task<CampaignDto> CreateOwnedCampaignAsync(IServiceProvider services, string[] args, ActorContext actor) =>
    services.GetRequiredService<ICampaignService>().CreateOwnedNetworkAsync(new(
        GuidOption(args, "--project"), RequiredOption(args, "--name"), GuidOption(args, "--network"),
        RequiredOption(args, "--target"), OptionalGuidOption(args, "--identity-pool"), OptionalGuidOption(args, "--template-pool"),
        IntOption(args, "--concurrency") ?? 100, IntOption(args, "--per-domain-concurrency") ?? 2,
        IntOption(args, "--per-domain-delay-ms") ?? 1_000, IntOption(args, "--maximum-attempts") ?? 3,
        IntOption(args, "--verification-delay-seconds") ?? 3_600,
        OptionalEnumOption<OwnedNetworkCampaignMode>(args, "--mode") ?? OwnedNetworkCampaignMode.AutomaticOwnedNetwork,
        StringOption(args, "--domain"), OptionalEnumOption<SourcePlatform>(args, "--platform"),
        OptionalEnumOption<CmsType>(args, "--cms"), OptionalEnumOption<TechnicalCompatibility>(args, "--compatibility") ?? TechnicalCompatibility.Compatible,
        OptionalEnumOption<SubmissionSourceValidationStatus>(args, "--validation-status") ?? SubmissionSourceValidationStatus.Valid,
        StringOption(args, "--tag"), IntOption(args, "--daily-limit") ?? 100_000, RequestKey(args),
        OptionalEnumOption<SubmissionStatus>(args, "--previous-submission-status"),
        OptionalEnumOption<BacklinkStatus>(args, "--previous-verification-status")), actor, CancellationToken.None);

static string RequestKey(string[] values) => StringOption(values, "--request-key") ?? Guid.CreateVersion7().ToString("D");

static Guid GuidOption(string[] values, string name) => Guid.TryParse(RequiredOption(values, name), out var value)
    ? value
    : throw new ValidationException($"{name} must be a UUID.");

static Guid? OptionalGuidOption(string[] values, string name) => StringOption(values, name) is { } raw
    ? Guid.TryParse(raw, out var value) ? value : throw new ValidationException($"{name} must be a UUID.")
    : null;

static T EnumOption<T>(string[] values, string name) where T : struct, Enum =>
    Enum.TryParse<T>(RequiredOption(values, name), true, out var value) ? value : throw new ValidationException($"{name} is invalid.");

static T? OptionalEnumOption<T>(string[] values, string name) where T : struct, Enum =>
    StringOption(values, name) is { } raw ? Enum.TryParse<T>(raw, true, out var value) ? value : throw new ValidationException($"{name} is invalid.") : null;

static bool RequiredBoolOption(string[] values, string name) => BoolOption(values, name) ?? throw new ValidationException($"{name} is required and must be true or false.");

static bool? BoolOption(string[] values, string name) => StringOption(values, name) is { } raw
    ? bool.TryParse(raw, out var value) ? value : throw new ValidationException($"{name} must be true or false.")
    : null;

static int? IntOption(string[] values, string name)
{
    var value = StringOption(values, name);
    return value is null ? null : int.TryParse(value, out var number) ? number : throw new ValidationException($"{name} must be an integer.");
}

static string RequiredOption(string[] values, string name) => StringOption(values, name) ?? throw new ValidationException($"{name} is required.");

static string? StringOption(string[] values, string name)
{
    var index = Array.IndexOf(values, name);
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static string[] MultiOption(string[] values, string name)
{
    var result = new List<string>();
    for (var index = 0; index < values.Length - 1; index++)
        if (values[index].Equals(name, StringComparison.OrdinalIgnoreCase)) result.Add(values[index + 1]);
    return result.ToArray();
}

static void PrintHelp()
{
    Console.WriteLine("""
        BacklinkStudio Engine CLI
          backlinkstudio project list [--limit 50] [--cursor value]
          backlinkstudio project create --name value --domain example.com [--description value] [--request-key value]
          backlinkstudio target add --project UUID --url URL [--label value] [--request-key value]
          backlinkstudio network create --project UUID --name value --ownership Owned|Controlled|PartnerControlled|ExplicitPermission --automation-permitted true --domain example.com [--domain blog.example.com] [--match-type ExactHost|ExactDomain|SubdomainOf]
          backlinkstudio network list --project UUID
          backlinkstudio network get --id UUID
          backlinkstudio network update --id UUID [network options]
          backlinkstudio sources import FILE --project UUID --network UUID [--format Txt|Csv] [--tag value] [--request-key value]
          backlinkstudio sources import-status --id UUID
          backlinkstudio sources validate --project UUID [--network UUID] [--maximum-sources 100000] [--request-key value]
          backlinkstudio sources list --project UUID [--network UUID] [--domain value] [--previous-submission-status Submitted] [--previous-verification-status Verified] [--limit 50] [--cursor value]
          backlinkstudio sources get --id UUID
          backlinkstudio identity-pool create --project UUID --name value --selection RoundRobin|DeterministicRandom|WeightedDeterministicRandom --email-strategy Fixed|AliasPool|PlusAddressing|CatchAll|PreCreatedSynthetic
          backlinkstudio identity-pool list --project UUID
          backlinkstudio identity-pool get|update --id UUID [pool options]
          backlinkstudio identity create --pool UUID --display-name value --email address [--weight 1]
          backlinkstudio identity update --id UUID [identity options]
          backlinkstudio template-pool create --project UUID --name value --type WordPressComment|OwnedProperty|GenericOwnedNetwork --selection RoundRobin|DeterministicRandom|WeightedDeterministicRandom --placement WebsiteField|CommentBody
          backlinkstudio template-pool list --project UUID
          backlinkstudio template-pool get|update --id UUID [pool options]
          backlinkstudio template create --pool UUID --name value --body value [--prefix value] [--suffix value] [--anchor value] [--target-variant URL]
          backlinkstudio template update --id UUID [template options]
          backlinkstudio submission preview --project UUID --source UUID --target URL [--identity-pool UUID] [--template-pool UUID] [--campaign UUID]
          backlinkstudio campaign create --project UUID --name value --network UUID --target URL [--identity-pool UUID] [--template-pool UUID] [--previous-submission-status Submitted] [--previous-verification-status Verified] [--concurrency 100]
          backlinkstudio campaign start --id UUID
          backlinkstudio campaign status --id UUID
          backlinkstudio campaign pause|resume|stop --id UUID
          backlinkstudio submissions list --campaign UUID [--limit 50] [--cursor value]
          backlinkstudio submissions get|attempts --id UUID
          backlinkstudio backlinks list --project UUID [--status PendingVerification|Verified|Missing|Lost|Error]
          backlinkstudio backlinks get --id UUID
          backlinkstudio backlinks start --sources urls.txt --identities identities.csv --comments comments.txt --target URL [--project UUID] [--concurrency 8] [--delay-ms 1000]
          backlinkstudio backlinks status --id WORKFLOW_UUID [--limit 50] [--cursor value]
          backlinkstudio verification start --backlink UUID [--request-key value]
          backlinkstudio backlinks run --sources FILE --project UUID --network UUID-or-name --target URL --identity-pool UUID-or-name --template-pool UUID-or-name [--concurrency 100]
          backlinkstudio candidate import --project UUID [--request-key value] --urls URL [URL...]
          backlinkstudio candidate list --project UUID
          backlinkstudio analysis start --project UUID [--request-key value]
          backlinkstudio opportunity list --project UUID
          backlinkstudio job get --id UUID
          backlinkstudio report generate --project UUID [--campaign UUID] --kind CampaignPerformance|BacklinkInventory --format Json|Csv|Xlsx|Html [--request-key value]
          backlinkstudio report list --project UUID [--limit 50] [--cursor value]
          backlinkstudio report get --id UUID
        """);
}

internal static class CliJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

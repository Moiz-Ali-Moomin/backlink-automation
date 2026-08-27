using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Mcp;
using BacklinkStudio.Mcp.Protocol;

namespace BacklinkStudio.ContractTests;

/// <summary>
/// Regression coverage for the STDIO privilege-escalation defect: a locally attached STDIO client
/// must not implicitly receive administrator authority.
/// </summary>
public sealed class StdioScopeTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] CredentialTools = ["agent_credential_create", "agent_credential_rotate", "agent_credential_revoke", "agent_credentials_list", "agent_credentials_get"];

    [Fact]
    public void DefaultStdioScopes_AreReadOnlyAndExcludeCredentialManagement()
    {
        var scopes = new StdioMcpOptions().Resolve();

        Assert.NotEmpty(scopes);
        Assert.DoesNotContain(AuthorizationScopes.Admin, scopes);
        Assert.All(scopes, scope => Assert.True(AuthorizationScopes.IsKnown(scope), $"'{scope}' is not a known scope."));
        Assert.All(scopes, scope => Assert.EndsWith(":read", scope, StringComparison.Ordinal));
    }

    [Fact]
    public void ElevatedStdioScopes_RequireExplicitCredentialManagementOptIn()
    {
        var withoutOptIn = new StdioMcpOptions { Scopes = [AuthorizationScopes.Admin] };
        var exception = Assert.Throws<InvalidOperationException>(() => withoutOptIn.Resolve());
        Assert.Contains("AllowCredentialManagement", exception.Message, StringComparison.Ordinal);

        var withOptIn = new StdioMcpOptions { Scopes = [AuthorizationScopes.Admin], AllowCredentialManagement = true };
        Assert.Contains(AuthorizationScopes.Admin, withOptIn.Resolve());
    }

    [Fact]
    public void UnknownStdioScope_IsRejected()
    {
        var options = new StdioMcpOptions { Scopes = ["projects:read", "definitely:not:a:scope"] };
        var exception = Assert.Throws<InvalidOperationException>(() => options.Resolve());
        Assert.Contains("definitely:not:a:scope", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("agent_credential_create")]
    [InlineData("agent_credential_rotate")]
    [InlineData("agent_credential_revoke")]
    public async Task DefaultStdio_CannotReachCredentialManagementTools(string tool)
    {
        var response = await CallAsync(new StdioMcpOptions().Resolve(), tool);
        AssertForbidden(response);
    }

    [Theory]
    [InlineData("project_create")]
    [InlineData("target_add")]
    [InlineData("policy_update")]
    [InlineData("blocklist_add")]
    [InlineData("candidates_import")]
    [InlineData("discovery_start")]
    [InlineData("analysis_start")]
    [InlineData("opportunities_approve")]
    [InlineData("campaign_create")]
    [InlineData("campaign_start")]
    [InlineData("campaign_stop")]
    [InlineData("verification_start")]
    [InlineData("schedule_create")]
    [InlineData("report_generate")]
    [InlineData("jobs_redrive")]
    public async Task DefaultStdio_CannotReachToolsOutsideItsScopes(string tool)
    {
        var response = await CallAsync(new StdioMcpOptions().Resolve(), tool);
        AssertForbidden(response);
    }

    [Fact]
    public async Task DefaultStdio_ReachesReadOnlyToolsWithinItsScopes()
    {
        // A read tool inside the default scope set must pass the gate and reach the Application service.
        await Assert.ThrowsAsync<ScopeGatePassedException>(() => CallAsync(new StdioMcpOptions().Resolve(), "projects_list"));
    }

    [Fact]
    public async Task IntentionallyElevatedStdio_ReachesCredentialManagementTools()
    {
        var scopes = new StdioMcpOptions { Scopes = [AuthorizationScopes.Admin], AllowCredentialManagement = true }.Resolve();
        var exception = await Assert.ThrowsAsync<ScopeGatePassedException>(() => CallAsync(scopes, "agent_credentials_list"));
        Assert.Contains("IAgentCredentialService", exception.Member, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IntentionallyElevatedStdio_HonoursNarrowNonAdminScopes()
    {
        var scopes = new StdioMcpOptions { Scopes = [AuthorizationScopes.SubmissionsExecute] }.Resolve();

        AssertForbidden(await CallAsync(scopes, "agent_credential_create"));
        AssertForbidden(await CallAsync(scopes, "project_create"));

        // Authorized: the call clears the scope gate and fails later on argument validation instead.
        AssertErrorCode(await CallAsync(scopes, "campaign_start"), "invalid_request");
        AssertForbidden(await CallAsync(scopes, "campaign_pause"));
    }

    [Fact]
    public async Task CampaignControlScope_DoesNotGrantSubmissionExecution()
    {
        var scopes = new StdioMcpOptions { Scopes = [AuthorizationScopes.CampaignsExecute] }.Resolve();

        AssertForbidden(await CallAsync(scopes, "campaign_start"));
        AssertErrorCode(await CallAsync(scopes, "campaign_pause"), "invalid_request");
    }

    [Fact]
    public async Task InlineSourceImport_IsBoundedBeforeApplicationDispatch()
    {
        using var parameters = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            name = "submission_sources_import",
            arguments = new
            {
                projectId = Guid.CreateVersion7().ToString(),
                ownedNetworkId = Guid.CreateVersion7().ToString(),
                format = "txt",
                fileName = "owned-sites.txt",
                content = new string('x', (8 * 1024 * 1024) + 1),
                clientRequestKey = "oversized-inline-import"
            }
        }, SerializerOptions));
        var request = new McpRequest("2.0", null, "tools/call", parameters.RootElement.Clone());
        var scopes = new StdioMcpOptions { Scopes = [AuthorizationScopes.SubmissionSourcesWrite] }.Resolve();

        var response = await CreateDispatcher().DispatchAsync(request, StdioActor(), scopes, CancellationToken.None);

        AssertErrorCode(response, "invalid_request");
    }

    [Fact]
    public async Task ToolDiscovery_ReflectsConfiguredStdioScopes()
    {
        var defaultTools = await ListToolsAsync(new StdioMcpOptions().Resolve());
        Assert.Contains("projects_list", defaultTools);
        Assert.Contains("system_health", defaultTools);
        Assert.All(CredentialTools, name => Assert.DoesNotContain(name, defaultTools));
        Assert.DoesNotContain("campaign_start", defaultTools);
        Assert.True(defaultTools.Count < McpToolDispatcher.Tools.Count);

        var elevatedTools = await ListToolsAsync(new StdioMcpOptions { Scopes = [AuthorizationScopes.Admin], AllowCredentialManagement = true }.Resolve());
        Assert.All(CredentialTools, name => Assert.Contains(name, elevatedTools));
        Assert.Equal(McpToolDispatcher.Tools.Count, elevatedTools.Count);

        var narrowTools = await ListToolsAsync(new StdioMcpOptions { Scopes = [AuthorizationScopes.CampaignsRead] }.Resolve());
        Assert.Contains("campaigns_list", narrowTools);
        Assert.DoesNotContain("projects_list", narrowTools);
        Assert.All(CredentialTools, name => Assert.DoesNotContain(name, narrowTools));
    }

    private static McpToolDispatcher CreateDispatcher() => new(
        ThrowingServiceProxy.Create<ISystemHealthService>(),
        ThrowingServiceProxy.Create<IProjectService>(),
        ThrowingServiceProxy.Create<ICandidateService>(),
        ThrowingServiceProxy.Create<IOpportunityService>(),
        ThrowingServiceProxy.Create<IJobService>(),
        ThrowingServiceProxy.Create<IPolicyService>(),
        ThrowingServiceProxy.Create<IDiscoveryService>(),
        ThrowingServiceProxy.Create<ICampaignService>(),
        ThrowingServiceProxy.Create<IVerificationService>(),
        ThrowingServiceProxy.Create<IScheduleService>(),
        ThrowingServiceProxy.Create<IReportService>(),
        ThrowingServiceProxy.Create<IAgentCredentialService>());

    private static Task<McpResponse?> CallAsync(IReadOnlySet<string> scopes, string tool)
    {
        var request = Request($"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\",\"arguments\":{{}}}}}}");
        return CreateDispatcher().DispatchAsync(request, StdioActor(), scopes, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<string>> ListToolsAsync(IReadOnlySet<string> scopes)
    {
        var request = Request("""{"jsonrpc":"2.0","id":1,"method":"tools/list"}""");
        var response = await CreateDispatcher().DispatchAsync(request, StdioActor(), scopes, CancellationToken.None);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, SerializerOptions));
        return document.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(x => x.GetProperty("name").GetString()!)
            .ToArray();
    }

    private static ActorContext StdioActor() => new(BacklinkStudio.Domain.ActorType.Agent, "local-stdio", null, "test", null);

    private static McpRequest Request(string json) => JsonSerializer.Deserialize<McpRequest>(json, SerializerOptions)!;

    private static void AssertForbidden(McpResponse? response) => AssertErrorCode(response, "forbidden");

    private static void AssertErrorCode(McpResponse? response, string expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, SerializerOptions));
        var result = document.RootElement.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        using var payload = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
        Assert.Equal(expected, payload.RootElement.GetProperty("code").GetString());
    }
}

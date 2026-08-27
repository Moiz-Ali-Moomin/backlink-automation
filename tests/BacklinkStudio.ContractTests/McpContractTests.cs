using System.Text.Json;
using BacklinkStudio.Application;
using BacklinkStudio.Domain;
using BacklinkStudio.Mcp.Protocol;

namespace BacklinkStudio.ContractTests;

public sealed class McpContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ToolCatalog_ContainsMilestoneEightBusinessToolsOnly()
    {
        var names = McpToolDispatcher.Tools.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var required = new[] { "system_health", "agent_credentials_list", "agent_credentials_get", "agent_credential_create", "agent_credential_rotate", "agent_credential_revoke", "projects_list", "projects_get", "project_create", "targets_list", "target_add", "policy_get", "policy_update", "blocklist_list", "blocklist_add", "candidates_list", "candidates_get", "candidates_import", "discovery_start", "discovery_runs_list", "discovery_run_get", "opportunities_list", "opportunities_get", "opportunities_summary", "opportunities_approve", "campaign_create", "campaigns_list", "campaigns_get", "campaigns_status", "campaign_update", "campaign_start", "campaign_pause", "campaign_resume", "campaign_stop", "submissions_list", "submission_attempts", "verification_start", "backlinks_list", "backlinks_get", "verification_history", "schedules_get", "schedules_list", "schedule_create", "schedule_update", "schedule_pause", "schedule_resume", "schedule_delete", "reports_get", "reports_list", "report_generate", "analysis_start", "jobs_get", "jobs_list", "jobs_pause", "jobs_resume", "jobs_redrive" };
        Assert.All(required, name => Assert.Contains(name, names));
        var ownedNetworkRequired = new[]
        {
            "owned_network_create", "owned_network_list", "owned_network_get", "owned_network_update",
            "submission_sources_import", "submission_sources_list", "submission_sources_get", "submission_sources_validate",
            "identity_pool_create", "identity_pool_list", "identity_pool_get", "identity_pool_update", "identity_create", "identity_update",
            "template_pool_create", "template_pool_list", "template_pool_get", "template_pool_update", "template_create", "template_update",
            "submission_preview", "campaign_get", "submission_get", "reports_generate"
        };
        Assert.All(ownedNetworkRequired, name => Assert.Contains(name, names));
        Assert.Contains("backlink_workflow_start", names);
        Assert.Contains("backlink_workflow_get", names);
        Assert.Equal(AuthorizationScopes.SubmissionsExecute,
            McpToolDispatcher.Tools.Single(x => x.Name == "backlink_workflow_start").RequiredScope);
        Assert.Equal(AuthorizationScopes.SubmissionsRead,
            McpToolDispatcher.Tools.Single(x => x.Name == "backlink_workflow_get").RequiredScope);
        var forbiddenPrimitives = new[]
        {
            "http_request_any_url", "submit_arbitrary_form", "playwright_run_script", "execute_javascript",
            "execute_shell", "execute_sql", "raw_network_request"
        };
        Assert.All(forbiddenPrimitives, name => Assert.DoesNotContain(name, names));
        Assert.DoesNotContain(names, name => name.Contains("shell", StringComparison.OrdinalIgnoreCase) || name.Contains("sql", StringComparison.OrdinalIgnoreCase) || name.Contains("arbitrary", StringComparison.OrdinalIgnoreCase) || name.Contains("playwright", StringComparison.OrdinalIgnoreCase));
        Assert.All(McpToolDispatcher.Tools.Where(x => x.Name.StartsWith("agent_credential", StringComparison.Ordinal)), tool => Assert.Equal("admin", tool.RequiredScope));
        Assert.Equal(AuthorizationScopes.ReportsWrite, McpToolDispatcher.Tools.Single(x => x.Name == "report_generate").RequiredScope);
        Assert.All(McpToolDispatcher.Tools.Where(x => x.Name is "reports_get" or "reports_list"), tool => Assert.Equal(AuthorizationScopes.ReportsRead, tool.RequiredScope));
        Assert.Equal(AuthorizationScopes.SubmissionsExecute, McpToolDispatcher.Tools.Single(x => x.Name == "campaign_start").RequiredScope);
        Assert.All(McpToolDispatcher.Tools.Where(x => x.Name is "submissions_list" or "submission_get" or "submission_attempts"), tool => Assert.Equal(AuthorizationScopes.SubmissionsRead, tool.RequiredScope));
    }

    [Fact]
    public void JsonRpcResponse_HasMcpShape()
    {
        using var idDocument = JsonDocument.Parse("1");
        var response = McpResponse.Success(idDocument.RootElement.Clone(), new { status = "queued" });
        var json = JsonSerializer.Serialize(response, SerializerOptions);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
        Assert.True(document.RootElement.TryGetProperty("result", out _));
        Assert.False(document.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public void ToolPayloads_SerializeEnumsAsCamelCaseStrings()
    {
        var optionsField = typeof(McpToolDispatcher).GetField("JsonOptions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var options = Assert.IsType<JsonSerializerOptions>(optionsField?.GetValue(null));

        Assert.Equal("\"queued\"", JsonSerializer.Serialize(JobStatus.Queued, options));
        Assert.Equal("\"paused\"", JsonSerializer.Serialize(JobStatus.Paused, options));
        Assert.Equal("\"active\"", JsonSerializer.Serialize(ScheduleStatus.Active, options));
    }
}

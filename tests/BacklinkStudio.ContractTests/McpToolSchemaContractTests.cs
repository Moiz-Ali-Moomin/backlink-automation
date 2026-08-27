using System.Text.Json;
using BacklinkStudio.Mcp.Protocol;

namespace BacklinkStudio.ContractTests;

/// <summary>
/// Guards the JSON Schema wire contract of every registered MCP tool. MCP clients validate
/// <c>inputSchema</c> before a tool is loaded, and a single malformed keyword makes the client
/// silently drop the tool, so these assertions run across the whole registry rather than a sample.
/// </summary>
public sealed class McpToolSchemaContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Keywords whose value is a map of arbitrary names to subschemas.</summary>
    private static readonly string[] SubschemaMapKeywords = ["properties", "patternProperties", "definitions", "$defs", "dependentSchemas"];

    /// <summary>Keywords whose value is a single subschema, or a tuple of subschemas.</summary>
    private static readonly string[] SubschemaKeywords = ["items", "additionalItems", "additionalProperties", "unevaluatedItems", "unevaluatedProperties", "contains", "propertyNames", "not", "if", "then", "else"];

    /// <summary>Keywords whose value is an array of subschemas.</summary>
    private static readonly string[] SubschemaListKeywords = ["oneOf", "anyOf", "allOf", "prefixItems"];

    [Fact]
    public void EveryToolSchema_OmitsDescriptionOrEmitsAString()
    {
        var violations = new List<string>();
        foreach (var tool in McpToolDispatcher.Tools)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
            CollectDescriptionViolations(document.RootElement, $"{tool.Name}/schema", violations);
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void EveryTool_HasSerializableWellFormedSchema()
    {
        Assert.NotEmpty(McpToolDispatcher.Tools);
        foreach (var tool in McpToolDispatcher.Tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), "A registered tool has an empty name.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Name} has an empty description.");
            Assert.NotNull(tool.InputSchema);

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
            var schema = document.RootElement;
            Assert.Equal(JsonValueKind.Object, schema.ValueKind);
            Assert.Equal("object", schema.GetProperty("type").GetString());

            if (schema.TryGetProperty("properties", out var properties))
            {
                Assert.Equal(JsonValueKind.Object, properties.ValueKind);
            }

            if (schema.TryGetProperty("required", out var required))
            {
                Assert.Equal(JsonValueKind.Array, required.ValueKind);
                Assert.All(required.EnumerateArray(), entry => Assert.Equal(JsonValueKind.String, entry.ValueKind));
            }
        }
    }

    [Fact]
    public void ToolNames_AreUnique()
    {
        var duplicates = McpToolDispatcher.Tools
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Tools_DoNotExposeOwnershipOverrideControls()
    {
        foreach (var tool in McpToolDispatcher.Tools)
        {
            var contract = $"{tool.Name} {JsonSerializer.Serialize(tool.InputSchema, SerializerOptions)}";
            Assert.DoesNotContain("ownership_override", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ownershipOverride", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("bypass_ownership", contract, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("force_submit", contract, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Tools_DoNotExposeGenericBrowserOrArbitraryEndpointControls()
    {
        var forbidden = new[]
        {
            "playwright_run_script", "browser_eval", "execute_javascript", "browser_click_anything",
            "browser_submit_arbitrary_form", "browser_navigate_any_url", "submit_arbitrary_form",
            "arbitraryEndpoint", "endpointUrl"
        };
        foreach (var tool in McpToolDispatcher.Tools)
        {
            var contract = $"{tool.Name} {JsonSerializer.Serialize(tool.InputSchema, SerializerOptions)}";
            Assert.All(forbidden, value => Assert.DoesNotContain(value, contract, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void SourceCompatibilitySchemas_ExposeFallbackCandidateAsAStateNotAnOverride()
    {
        foreach (var toolName in new[] { "submission_sources_list", "campaign_create" })
        {
            var tool = Assert.Single(McpToolDispatcher.Tools, x => x.Name == toolName);
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
            var values = document.RootElement.GetProperty("properties").GetProperty("technicalCompatibility")
                .GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Contains("fallbackCandidate", values);
        }
    }

    [Fact]
    public void SimpleWorkflowSchema_ContainsOnlyHighLevelInputs()
    {
        var tool = Assert.Single(McpToolDispatcher.Tools, x => x.Name == "backlink_workflow_start");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
        var properties = document.RootElement.GetProperty("properties");
        var names = properties.EnumerateObject().Select(value => value.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("sourceUrls", names);
        Assert.Contains("identities", names);
        Assert.Contains("comments", names);
        Assert.Contains("targetUrl", names);
        var required = document.RootElement.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("clientRequestKey", required);
        Assert.DoesNotContain("ownershipStatus", names);
        Assert.DoesNotContain("automationPermitted", names);
        Assert.DoesNotContain("ownedNetworkProfileId", names);
        Assert.DoesNotContain("wordPressSiteProfileId", names);
        Assert.DoesNotContain("credentialReference", names);
        Assert.DoesNotContain("adapterName", names);
        Assert.DoesNotContain("browser", names);
    }

    [Fact]
    public void SourceImportSchema_DoesNotRequireOrExposeOwnedNetworkProfile()
    {
        var tool = Assert.Single(McpToolDispatcher.Tools, x => x.Name == "submission_sources_import");
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
        var properties = document.RootElement.GetProperty("properties");
        var names = properties.EnumerateObject().Select(value => value.Name).ToHashSet(StringComparer.Ordinal);
        var required = document.RootElement.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString()).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ownedNetworkId", names);
        Assert.DoesNotContain("ownedNetworkProfileId", names);
        Assert.DoesNotContain("ownedNetworkId", required);
    }

    /// <summary>
    /// Explicit coverage for the tools an MCP client previously rejected because a no-argument
    /// string property serialized <c>"description": null</c>.
    /// </summary>
    [Theory]
    [InlineData("projects_list", "cursor")]
    [InlineData("owned_network_list", "cursor")]
    [InlineData("submission_sources_import", "fileName")]
    [InlineData("submission_sources_validate", "clientRequestKey")]
    [InlineData("submission_preview", "targetUrl")]
    public void PreviouslyRejectedTool_EmitsNoNullDescription(string toolName, string propertyName)
    {
        var tool = Assert.Single(McpToolDispatcher.Tools, x => x.Name == toolName);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(tool.InputSchema, SerializerOptions));
        var property = document.RootElement.GetProperty("properties").GetProperty(propertyName);

        Assert.Equal("string", property.GetProperty("type").GetString());
        if (property.TryGetProperty("description", out var description))
        {
            Assert.Equal(JsonValueKind.String, description.ValueKind);
        }
    }

    /// <summary>
    /// Walks a subschema, treating <c>description</c> as a keyword only where it is one. Inside
    /// <c>properties</c> and friends, "description" is a caller-supplied property name whose value
    /// is itself a subschema, so it must be recursed into rather than type-checked.
    /// </summary>
    private static void CollectDescriptionViolations(JsonElement schema, string path, List<string> violations)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var member in schema.EnumerateObject())
        {
            var memberPath = $"{path}/{member.Name}";
            if (member.Name == "description")
            {
                if (member.Value.ValueKind != JsonValueKind.String)
                {
                    violations.Add($"{memberPath} is {member.Value.ValueKind}, expected String or omission. Schema: {schema.GetRawText()}");
                }

                continue;
            }

            if (SubschemaMapKeywords.Contains(member.Name, StringComparer.Ordinal) && member.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var subschema in member.Value.EnumerateObject())
                {
                    CollectDescriptionViolations(subschema.Value, $"{memberPath}/{subschema.Name}", violations);
                }

                continue;
            }

            if (SubschemaKeywords.Contains(member.Name, StringComparer.Ordinal) || SubschemaListKeywords.Contains(member.Name, StringComparer.Ordinal))
            {
                if (member.Value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var subschema in member.Value.EnumerateArray())
                    {
                        CollectDescriptionViolations(subschema, $"{memberPath}/{index++}", violations);
                    }
                }
                else
                {
                    CollectDescriptionViolations(member.Value, memberPath, violations);
                }
            }
        }
    }
}

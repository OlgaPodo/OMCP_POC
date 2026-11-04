using System.Text.Json;
using System.Text.Json.Nodes;
using GraphMcpServer.Services;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public sealed class CsdlMcpProjection(GraphClientService graphService, CopilotSearchService copilotSearch)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Validate()
    {
        UserCsdlProvider.ValidateDependencies();
    }

    public static ListToolsResult ListTools() => new()
    {
        Tools =
        [
            new Tool
            {
                Name = "search",
                Title = "Semantic Search API Catalog",
                Description = "Search the Microsoft Graph API catalog of prompts, entity sets, and example OData queries using Copilot AI semantic retrieval. Matches natural language utterances against curated prompt workflows and returns candidate API templates with their entity set, recommended OData path, and parameter extraction guidance.",
                InputSchema = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Natural language request, question, or intent to search for in the API catalog (e.g., 'Who is Olga\'s manager?', 'Find security groups')."
                        },
                        ["top"] = new JsonObject
                        {
                            ["type"] = "integer",
                            ["description"] = "Maximum number of candidate API templates to return (default: 3)."
                        }
                    },
                    ["required"] = new JsonArray { "query" },
                    ["additionalProperties"] = false
                })
            },
            new Tool
            {
                Name = "query",
                Title = "Execute Graph OData Query",
                Description = "Execute an OData HTTP GET request against Microsoft Graph to retrieve entity or collection data. Supports relative paths such as 'me', 'users', 'users/{id}', 'users/{id}/manager', 'users/{id}/directReports', 'groups', 'groups/{id}/members', 'groups/{id}/owners', as well as standard OData query parameters ($filter, $select, etc.).",
                InputSchema = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["path"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Relative Microsoft Graph OData request path (e.g., 'me', 'users/olga@contoso.com/manager', 'users?$filter=startswith(displayName, \'David\')', 'groups/11111111-2222-3333-4444-555555555555/members')."
                        }
                    },
                    ["required"] = new JsonArray { "path" },
                    ["additionalProperties"] = false
                })
            }
        ]
    };

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.Name, "search", StringComparison.OrdinalIgnoreCase))
        {
            string query = "";
            int top = 3;
            if (request.Arguments != null)
            {
                if (request.Arguments.TryGetValue("query", out var qVal) && qVal.ValueKind == JsonValueKind.String)
                    query = qVal.GetString() ?? "";
                if (request.Arguments.TryGetValue("top", out var tVal) && tVal.TryGetInt32(out var tInt))
                    top = tInt;
            }

            var searchResult = await copilotSearch.SearchAsync(query, top, cancellationToken);
            return new CallToolResult
            {
                IsError = false,
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(searchResult, JsonOptions) }]
            };
        }

        if (string.Equals(request.Name, "query", StringComparison.OrdinalIgnoreCase))
        {
            string path = "";
            if (request.Arguments != null && request.Arguments.TryGetValue("path", out var pVal) && pVal.ValueKind == JsonValueKind.String)
                path = pVal.GetString() ?? "";

            var data = await graphService.ExecuteODataQueryAsync(path, cancellationToken);
            return new CallToolResult
            {
                IsError = data == null,
                Content = [new TextContentBlock { Text = data == null
                    ? JsonSerializer.Serialize(new { error = "The requested directory object or OData path was not found." })
                    : JsonSerializer.Serialize(data, JsonOptions) }]
            };
        }

        throw new ArgumentException($"Undeclared tool: {request.Name}. The server provides only two tools: 'search' and 'query'.");
    }

    public static ListResourcesResult ListResources() => new()
    {
        Resources = UserCsdlProvider.GetProjectedResources()
            .Where(definition => !definition["UriTemplate"]!.GetValue<string>().Contains('{'))
            .Select(definition => new Resource
            {
                Uri = definition["UriTemplate"]!.GetValue<string>(),
                Name = definition["Name"]!.GetValue<string>(),
                Title = definition["Title"]!.GetValue<string>(),
                Description = definition["Description"]!.GetValue<string>(),
                MimeType = definition["MimeType"]!.GetValue<string>()
            }).ToList()
    };

    public static ListResourceTemplatesResult ListResourceTemplates() => new()
    {
        ResourceTemplates = UserCsdlProvider.GetProjectedResources()
            .Where(definition => definition["UriTemplate"]!.GetValue<string>().Contains('{'))
            .Select(definition => new ResourceTemplate
            {
                UriTemplate = definition["UriTemplate"]!.GetValue<string>(),
                Name = definition["Name"]!.GetValue<string>(),
                Title = definition["Title"]!.GetValue<string>(),
                Description = definition["Description"]!.GetValue<string>(),
                MimeType = definition["MimeType"]!.GetValue<string>()
            }).ToList()
    };

    public async ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken cancellationToken)
    {
        var data = await graphService.ExecuteODataQueryAsync(request.Uri, cancellationToken);
        return new ReadResourceResult
        {
            Contents = [new TextResourceContents
            {
                Uri = request.Uri,
                MimeType = "application/json",
                Text = JsonSerializer.Serialize(data, JsonOptions)
            }]
        };
    }
}

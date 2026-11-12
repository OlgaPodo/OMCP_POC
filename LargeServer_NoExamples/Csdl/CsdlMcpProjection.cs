using System.Text.Json;
using System.Text.RegularExpressions;
using GraphMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public sealed class CsdlMcpProjection(IODataClientService client, ILogger<CsdlMcpProjection> logger)
{
    private sealed record ODataExecution(HttpMethod Method, string ODataQuery, JsonElement Data);

    public static ListToolsResult ListTools() => new()
    {
        Tools = CsdlProvider.GetProjectedTools().Select(t => new Tool
        {
            Name = t.Name, Title = t.Title, Description = t.Description, InputSchema = EdmJsonSchema.ForArguments(t)
        }).ToList()
    };
    public static ListResourcesResult ListResources() => new()
    {
        Resources = CsdlProvider.GetProjectedResources().Where(r => !r.UriTemplate.Contains('{'))
            .Select(r => new Resource { Name = r.Name, Title = r.Title, Description = r.Description,
                Uri = r.UriTemplate, MimeType = "application/json" }).ToList()
    };
    public static ListResourceTemplatesResult ListResourceTemplates() => new()
    {
        ResourceTemplates = CsdlProvider.GetProjectedResources().Where(r => r.UriTemplate.Contains('{'))
            .Select(r => new ResourceTemplate { Name = r.Name, Title = r.Title, Description = r.Description,
                UriTemplate = r.UriTemplate, MimeType = "application/json" }).ToList()
    };

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        try
        {
            var tool = CsdlProvider.GetProjectedTools().SingleOrDefault(t => t.Name == request.Name)
                ?? throw new ArgumentException($"Unknown CSDL tool: {request.Name}");
            var execution = await ExecuteAsync(tool, request.Arguments ?? new Dictionary<string, JsonElement>(), cancellationToken);
            return new() { Content = [new TextContentBlock { Text = SerializeResponse(tool, execution) }] };
        }
        catch (ArgumentException exception)
        {
            logger.LogWarning(exception, "Invalid arguments for CSDL tool {Tool}", request.Name);
            return new() { IsError = true, Content = [new TextContentBlock { Text = exception.Message }] };
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "OData request failed for CSDL tool {Tool}", request.Name);
            return new() { IsError = true, Content = [new TextContentBlock { Text = $"OData request failed ({exception.StatusCode?.ToString() ?? "transport error"})." }] };
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "OData configuration is invalid for CSDL tool {Tool}", request.Name);
            return new() { IsError = true, Content = [new TextContentBlock { Text = "Configure a valid OData:BaseUrl before executing this tool." }] };
        }
        catch (Azure.Identity.AuthenticationFailedException exception)
        {
            logger.LogError(exception, "OData authentication failed for CSDL tool {Tool}", request.Name);
            return new() { IsError = true, Content = [new TextContentBlock { Text = "OData authentication failed. Check Azure CLI sign-in and OData:Scope." }] };
        }
    }

    public async ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken cancellationToken)
    {
        foreach (var resource in CsdlProvider.GetProjectedResources())
        {
            var match = resource.MatchPattern.Match(request.Uri);
            if (!match.Success) continue;
            var arguments = new Dictionary<string, JsonElement>();
            foreach (var key in resource.Tool.Arguments.Where(a => a.IsKey))
            {
                var value = Uri.UnescapeDataString(match.Groups[key.Name].Value);
                arguments[key.Name] = EdmJsonSchema.ParseKey(key.Type, value);
            }
            var execution = await ExecuteAsync(resource.Tool, arguments, cancellationToken);
            return new() { Contents = [new TextResourceContents
            {
                Uri = request.Uri,
                MimeType = "application/json",
                Text = SerializeResponse(resource.Tool, execution)
            }] };
        }
        throw new ArgumentException($"No CSDL resource matches URI: {request.Uri}");
    }

    private static string SerializeResponse(ToolBinding tool, ODataExecution execution)
    {
        var examples = CsdlProvider.GetExamplesFor(tool).Select(example => new
        {
            targetKind = example.TargetKind,
            targetName = example.TargetName,
            entitySet = example.EntitySet,
            utterance = example.Utterance,
            request = example.Request,
            description = example.Description
        });
        return JsonSerializer.Serialize(new
        {
            method = execution.Method.Method,
            odataQuery = execution.ODataQuery,
            entitySet = tool.EntitySet,
            data = execution.Data,
            examples
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<ODataExecution> ExecuteAsync(ToolBinding tool, IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var known = tool.Arguments.Select(a => a.Name).Concat(tool.QueryOptions ? EdmJsonSchema.QueryOptions.Keys : []);
        if (arguments.Keys.Any(k => !known.Contains(k, StringComparer.Ordinal))) throw new ArgumentException("Unknown tool argument.");
        foreach (var argument in tool.Arguments)
        {
            if (!arguments.TryGetValue(argument.Name, out var value))
            {
                if (argument.Required) throw new ArgumentException($"Missing required tool argument: {argument.Name}");
            }
            else EdmJsonSchema.Validate(argument.Type, value, argument.Name);
        }
        var path = tool.Path;
        var keys = tool.Arguments.Where(a => a.IsKey).ToList();
        if (keys.Count > 0)
        {
            var predicate = string.Join(",", keys.Select(k =>
                (keys.Count == 1 ? "" : k.Name[4..] + "=") + Uri.EscapeDataString(EdmJsonSchema.Literal(k.Type, arguments[k.Name]))));
            var separator = path.IndexOf('/');
            path = separator < 0 ? $"{path}({predicate})" : path.Insert(separator, $"({predicate})");
        }
        var query = new List<string>();
        var parameters = tool.Arguments.Where(a => !a.IsKey && arguments.ContainsKey(a.Name)).ToList();
        if (tool.OperationKind == OperationKind.Function)
        {
            path += "(" + string.Join(",", parameters.Select(p => p.Name + "=@" + p.Name)) + ")";
            query.AddRange(parameters.Select(p => "@" + p.Name + "=" + Uri.EscapeDataString(EdmJsonSchema.Literal(p.Type, arguments[p.Name]))));
        }
        if (tool.QueryOptions)
        {
            foreach (var (name, odataName) in EdmJsonSchema.QueryOptions)
            {
                if (!arguments.TryGetValue(name, out var value)) continue;
                if (name is "top" or "skip")
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var count) || count < 0)
                        throw new ArgumentException($"{name} must be a nonnegative Int32.");
                }
                else if (name == "count")
                {
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("count must be a Boolean.");
                }
                else if (value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} must be a string.");
                query.Add(odataName + "=" + Uri.EscapeDataString(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()));
            }
        }
        if (query.Count > 0) path += "?" + string.Join("&", query);
        var body = tool.OperationKind == OperationKind.Action
            ? JsonSerializer.SerializeToElement(parameters.ToDictionary(p => p.Name, p => arguments[p.Name]))
            : (JsonElement?)null;
        var method = tool.OperationKind == OperationKind.Action ? HttpMethod.Post : HttpMethod.Get;
        var data = await client.SendAsync(method, path, body, cancellationToken);
        return new(method, path, data);
    }
}

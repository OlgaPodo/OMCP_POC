using System.Text.Json;
using System.Text.RegularExpressions;
using GraphMcpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

/// <summary>Handles MCP discovery, tool calls, and resource reads using generated metadata and shared OData execution.</summary>
public sealed class CsdlMcpProjection(IODataClientService client, ILogger<CsdlMcpProjection> logger)
{
    /// <summary>Carries an executed request's method, OData path, and JSON payload for response serialization.</summary>
    private sealed record ODataExecution(HttpMethod Method, string ODataQuery, JsonElement Data);

    public static ListToolsResult ListTools() => new() { Tools = GeneratedModel.McpTools };
    public static ListResourcesResult ListResources() => new() { Resources = GeneratedModel.McpResources };
    public static ListResourceTemplatesResult ListResourceTemplates() => new() { ResourceTemplates = GeneratedModel.McpResourceTemplates };

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        try
        {
            var binding = CsdlProvider.GetRequestBindings().SingleOrDefault(binding => binding.Name == request.Name)
                ?? throw new ArgumentException($"Unknown CSDL tool: {request.Name}");
            var execution = await ExecuteAsync(binding, request.Arguments ?? new Dictionary<string, JsonElement>(), cancellationToken);
            return new() { Content = [new TextContentBlock { Text = SerializeResponse(binding, execution) }] };
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
            logger.LogError(exception, "Graph execution configuration is invalid for CSDL tool {Tool}", request.Name);
            return new() { IsError = true, Content = [new TextContentBlock { Text = exception.Message }] };
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
            foreach (var key in resource.RequestBinding.Arguments.Where(a => a.IsKey))
            {
                var value = Uri.UnescapeDataString(match.Groups[key.Name].Value);
                arguments[key.Name] = EdmJsonSchema.ParseKey(key.Type, value);
            }
            var execution = await ExecuteAsync(resource.RequestBinding, arguments, cancellationToken);
            return new() { Contents = [new TextResourceContents
            {
                Uri = request.Uri,
                MimeType = "application/json",
                Text = SerializeResponse(resource.RequestBinding, execution)
            }] };
        }
        throw new ArgumentException($"No CSDL resource matches URI: {request.Uri}");
    }

    private static string SerializeResponse(ODataRequestBinding binding, ODataExecution execution)
    {
        var examples = CsdlProvider.GetExamplesFor(binding).Select(example => new
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
            entitySet = binding.EntitySet,
            data = execution.Data,
            examples
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private async Task<ODataExecution> ExecuteAsync(ODataRequestBinding binding, IDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var known = binding.Arguments.Select(a => a.Name).Concat(binding.QueryOptions ? EdmJsonSchema.QueryOptions.Keys : []);
        if (arguments.Keys.Any(k => !known.Contains(k, StringComparer.Ordinal))) throw new ArgumentException("Unknown OData request argument.");
        foreach (var argument in binding.Arguments)
        {
            if (!arguments.TryGetValue(argument.Name, out var value))
            {
                if (argument.Required) throw new ArgumentException($"Missing required OData request argument: {argument.Name}");
            }
            else EdmJsonSchema.Validate(argument.Type, value, argument.Name);
        }
        var path = binding.Path;
        var keys = binding.Arguments.Where(a => a.IsKey).ToList();
        if (keys.Count > 0)
        {
            var predicate = string.Join(",", keys.Select(k =>
                (keys.Count == 1 ? "" : k.Name[4..] + "=") + Uri.EscapeDataString(EdmJsonSchema.Literal(k.Type, arguments[k.Name]))));
            var separator = path.IndexOf('/');
            path = separator < 0 ? $"{path}({predicate})" : path.Insert(separator, $"({predicate})");
        }
        var query = new List<string>();
        var parameters = binding.Arguments.Where(a => !a.IsKey && arguments.ContainsKey(a.Name)).ToList();
        if (binding.OperationKind == OperationKind.Function)
        {
            path += "(" + string.Join(",", parameters.Select(p => p.Name + "=@" + p.Name)) + ")";
            query.AddRange(parameters.Select(p => "@" + p.Name + "=" + Uri.EscapeDataString(EdmJsonSchema.Literal(p.Type, arguments[p.Name]))));
        }
        if (binding.QueryOptions)
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
        var body = binding.OperationKind == OperationKind.Action
            ? JsonSerializer.SerializeToElement(parameters.ToDictionary(p => p.Name, p => arguments[p.Name]))
            : (JsonElement?)null;
        var method = binding.OperationKind == OperationKind.Action ? HttpMethod.Post : HttpMethod.Get;
        var data = await client.SendAsync(
            binding,
            new Dictionary<string, JsonElement>(arguments),
            method,
            path,
            body,
            cancellationToken);
        return new(method, path, data);
    }
}

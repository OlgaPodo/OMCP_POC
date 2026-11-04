using System.Text.Json;
using System.Text.Json.Nodes;
using GraphMcpServer.Services;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public sealed class CsdlMcpProjection(GraphClientService graphService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> Operations =
        ["FindUsers", "GetUserProfile", "FindGroups", "GetGroupDetails", "GetMe", "GetAllGroups", "ReadResource"];

    public static void Validate()
    {
        UserCsdlProvider.ValidateDependencies();
        foreach (var definition in UserCsdlProvider.GetProjectedTools().Concat(UserCsdlProvider.GetProjectedResources()))
            if (!Operations.Contains(definition["Operation"]!.GetValue<string>()))
                throw new InvalidOperationException($"Unsupported CSDL operation: {definition["Operation"]}");
    }

    public static ListToolsResult ListTools() => new()
    {
        Tools = UserCsdlProvider.GetProjectedTools().Select(definition => new Tool
        {
            Name = definition["Name"]!.GetValue<string>(),
            Title = definition["Title"]!.GetValue<string>(),
            Description = definition["Description"]!.GetValue<string>(),
            InputSchema = CreateInputSchema(definition)
        }).ToList()
    };

    private static JsonElement CreateInputSchema(JsonObject definition)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var argument in definition["Arguments"]!.AsArray())
        {
            var name = argument!["Name"]!.GetValue<string>();
            var property = new JsonObject { ["type"] = "string", ["description"] = argument["Description"]!.DeepClone() };
            if (argument["Required"]!.GetValue<bool>()) required.Add(name);
            else property["default"] = argument["Default"]!.DeepClone();
            properties[name] = property;
        }
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["properties"] = properties, ["required"] = required, ["additionalProperties"] = false
        });
    }

    private static Dictionary<string, string> BindArguments(JsonObject definition, IDictionary<string, JsonElement>? supplied)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in definition["Arguments"]!.AsArray())
        {
            var name = argument!["Name"]!.GetValue<string>();
            if (supplied != null && supplied.TryGetValue(name, out var value))
            {
                if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                    throw new ArgumentException($"Tool argument {name} must be a nonempty string.");
                result[name] = value.GetString()!;
            }
            else if (argument["Required"]!.GetValue<bool>())
                throw new ArgumentException($"Missing required tool argument: {name}");
            else result[name] = argument["Default"]!.GetValue<string>();
        }
        if (supplied != null && supplied.Keys.Any(name => !result.ContainsKey(name)))
            throw new ArgumentException("The tool contains an undeclared argument.");
        return result;
    }

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        var definition = UserCsdlProvider.GetProjectedTools().SingleOrDefault(d => d["Name"]!.GetValue<string>() == request.Name)
            ?? throw new ArgumentException($"Undeclared CSDL tool: {request.Name}");
        var arguments = BindArguments(definition, request.Arguments);
        object? data;
        if (definition["Operation"]!.GetValue<string>() == "ReadResource")
        {
            var (resource, value) = await ReadDataAsync(arguments["uri"], cancellationToken);
            data = new { uri = arguments["uri"], entitySet = resource["entitySet"]!.GetValue<string>(), data = value };
        }
        else data = await ExecuteAsync(definition["Operation"]!.GetValue<string>(), arguments.Values.SingleOrDefault(), cancellationToken);
        return new CallToolResult
        {
            IsError = data == null,
            Content = [new TextContentBlock { Text = data == null
                ? JsonSerializer.Serialize(new { error = "The requested directory object was not found." })
                : JsonSerializer.Serialize(data, JsonOptions) }]
        };
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
        var (definition, data) = await ReadDataAsync(request.Uri, cancellationToken);
        return new ReadResourceResult
        {
            Contents = [new TextResourceContents
            {
                Uri = request.Uri, MimeType = definition["MimeType"]!.GetValue<string>(),
                Text = JsonSerializer.Serialize(data, JsonOptions)
            }]
        };
    }

    private async Task<(JsonObject Definition, object Data)> ReadDataAsync(string uri, CancellationToken cancellationToken)
    {
        foreach (var definition in UserCsdlProvider.GetProjectedResources()
                     .OrderBy(d => d["UriTemplate"]!.GetValue<string>().Contains('{')))
        {
            var template = definition["UriTemplate"]!.GetValue<string>();
            var parameter = definition["Parameter"]!.GetValue<string>();
            string? argument = null;
            if (parameter.Length == 0)
            {
                if (uri != template) continue;
            }
            else
            {
                var parts = template.Split("{" + parameter + "}", StringSplitOptions.None);
                if (parts.Length != 2 || !uri.StartsWith(parts[0], StringComparison.Ordinal)
                    || !uri.EndsWith(parts[1], StringComparison.Ordinal) || uri.Length <= parts[0].Length + parts[1].Length)
                    continue;
                argument = Uri.UnescapeDataString(uri.Substring(parts[0].Length, uri.Length - parts[0].Length - parts[1].Length));
            }
            var data = await ExecuteAsync(definition["Operation"]!.GetValue<string>(), argument, cancellationToken)
                ?? throw new KeyNotFoundException($"Resource not found: {uri}");
            return (definition, data);
        }
        throw new ArgumentException($"No CSDL resource binding matches URI: {uri}");
    }

    private async Task<object?> ExecuteAsync(string operation, string? argument, CancellationToken cancellationToken)
    {
        switch (operation)
        {
            case "FindUsers":
                var users = await graphService.FindUsersAsync(argument!, cancellationToken);
                return new { query = argument, total = users.Count, users };
            case "GetUserProfile":
                return await graphService.GetUserProfileAsync(argument!, cancellationToken);
            case "FindGroups":
                var groups = await graphService.FindGroupsAsync(argument!, cancellationToken);
                return new { query = argument, total = groups.Count, groups };
            case "GetGroupDetails":
                return await graphService.GetGroupDetailsAsync(argument!, cancellationToken);
            case "GetMe":
                return await graphService.GetMeAsync(cancellationToken);
            case "GetAllGroups":
                return await graphService.GetAllGroupsAsync(cancellationToken);
            default:
                throw new InvalidOperationException($"Unsupported CSDL resource operation: {operation}");
        }
    }
}

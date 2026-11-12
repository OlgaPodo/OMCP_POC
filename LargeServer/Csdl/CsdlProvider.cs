using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public enum StaticTypeKind
{
    String, Boolean, Integer, Number, Guid, Date, DateTimeOffset, TimeOfDay,
    Duration, Binary, Enum, Object, Array
}

public enum OperationKind { Read, Function, Action }

public sealed record StaticProperty(string Name, StaticTypeDescriptor Type);
public sealed record StaticTypeDescriptor(
    string FullName,
    StaticTypeKind Kind,
    bool Nullable = false,
    long? Minimum = null,
    long? Maximum = null,
    IReadOnlyList<string>? EnumValues = null,
    IReadOnlyList<StaticProperty>? Properties = null,
    bool IsOpen = false,
    StaticTypeDescriptor? ElementType = null);
public sealed record ParameterBinding(
    string Name, StaticTypeDescriptor Type, bool Required, bool IsKey = false, string? Description = null);
public sealed record ToolBinding(
    string Name, string Title, string Description, string Path, OperationKind OperationKind,
    IReadOnlyList<ParameterBinding> Arguments, string AnnotationTarget, string? EntitySet,
    string? EntityType, bool QueryOptions, string InputSchemaJson);
public sealed record ResourceDefinition(
    string Name, string Title, string Description, string UriTemplate, string ToolName);
public sealed record ResourceBinding(
    string Name, string Title, string Description, string UriTemplate, ToolBinding Tool, Regex MatchPattern);
public sealed record PromptArgumentDefinition(string Name, string? Description, bool Required, string? Default);
public sealed record PromptMessageDefinition(Role Role, string Text);
public sealed record PromptDefinition(
    string Name, string Title, string Description,
    IReadOnlyList<PromptArgumentDefinition> Arguments, IReadOnlyList<PromptMessageDefinition> Messages);
public sealed record ExampleDefinition(
    string TargetKind, string TargetName, string? EntitySet,
    string Utterance, string Request, string Description);

public static class CsdlProvider
{
    private static readonly IReadOnlyDictionary<string, ToolBinding> ToolsByName =
        GeneratedModel.Tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

    private static readonly IReadOnlyList<ResourceBinding> Resources =
        GeneratedModel.Resources.Select(resource =>
        {
            var tool = ToolsByName[resource.ToolName];
            var pattern = Regex.Escape(resource.UriTemplate);
            foreach (var key in tool.Arguments.Where(argument => argument.IsKey))
                pattern = pattern.Replace(Regex.Escape("{" + key.Name + "}"), $"(?<{key.Name}>[^/]+)");
            return new ResourceBinding(
                resource.Name, resource.Title, resource.Description, resource.UriTemplate,
                tool, new Regex("^" + pattern + "$", RegexOptions.CultureInvariant | RegexOptions.Compiled));
        }).ToList();

    public static string CsdlFilePath => GeneratedModel.SourceCsdl;
    public static string GetCsdl() => GetCsdlXml();
    public static string GetCsdlXml() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(GeneratedModel.CsdlXmlBase64));
    public static string GetCsdlJson() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(GeneratedModel.CsdlJsonBase64));
    public static IReadOnlyList<ToolBinding> GetProjectedTools() => GeneratedModel.Tools;
    public static IReadOnlyList<ResourceBinding> GetProjectedResources() => Resources;
    public static IReadOnlyList<ExampleDefinition> GetExampleQueries() => GeneratedModel.Examples;

    public static IReadOnlyList<ExampleDefinition> GetExamplesFor(ToolBinding tool) =>
        GeneratedModel.Examples.Where(example =>
            example.TargetName == tool.AnnotationTarget ||
            tool.OperationKind != OperationKind.Read && example.TargetName == tool.AnnotationTarget ||
            tool.EntitySet != null && example.EntitySet == tool.EntitySet ||
            tool.EntityType != null && example.TargetName == tool.EntityType)
            .Distinct()
            .ToList();

    public static ListPromptsResult ListPrompts() => new()
    {
        Prompts = GeneratedModel.Prompts.Select(prompt => new Prompt
        {
            Name = prompt.Name,
            Title = prompt.Title,
            Description = prompt.Description,
            Arguments = prompt.Arguments.Select(argument => new PromptArgument
            {
                Name = argument.Name,
                Description = argument.Description,
                Required = argument.Required
            }).ToList()
        }).ToList()
    };

    public static GetPromptResult GetPrompt(GetPromptRequestParams request)
    {
        var prompt = GeneratedModel.Prompts.SingleOrDefault(prompt => prompt.Name == request.Name)
            ?? throw new ArgumentException($"Unknown prompt: {request.Name}");
        if (request.Arguments?.Keys.Any(key => !prompt.Arguments.Any(argument => argument.Name == key)) == true)
            throw new ArgumentException("Unknown prompt argument.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in prompt.Arguments)
        {
            var value = argument.Default;
            if (request.Arguments != null && request.Arguments.TryGetValue(argument.Name, out var supplied))
            {
                if (supplied.ValueKind != JsonValueKind.String)
                    throw new ArgumentException($"Prompt argument {argument.Name} must be a string.");
                value = supplied.GetString();
            }
            if (value == null && argument.Required)
                throw new ArgumentException($"Missing required prompt argument: {argument.Name}");
            values[argument.Name] = value ?? "";
        }
        return new GetPromptResult
        {
            Description = prompt.Description,
            Messages = prompt.Messages.Select(message => new PromptMessage
            {
                Role = message.Role,
                Content = new TextContentBlock
                {
                    Text = Regex.Replace(message.Text, @"\{([A-Za-z0-9_]+)\}", match =>
                        values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value)
                }
            }).ToList()
        };
    }

    public static IReadOnlyList<string> ExtractODataQueries(string text) =>
        Regex.Matches(text, @"(?:GET|POST)\s+([^\r\n]+?)(?=\s+(?:and\s+)?(?:GET|POST)\s+|[.]\s|\r?\n|$)")
            .Select(match => match.Groups[1].Value.TrimEnd('.', ',', ';'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static object GetCatalog() => GeneratedModel.Prompts.Select(prompt =>
    {
        var queries = prompt.Messages.SelectMany(message => ExtractODataQueries(message.Text)).Distinct().ToList();
        var sources = GeneratedModel.EntitySets
            .Where(source => queries.Any(query => Regex.IsMatch(query, $@"^{Regex.Escape(source)}(?:[/('?$]|$)")))
            .ToList();
        var bindings = GeneratedModel.Tools
            .Where(tool => tool.EntitySet != null && sources.Contains(tool.EntitySet))
            .Select(tool => tool.Name)
            .ToList();
        return new
        {
            name = prompt.Name,
            title = prompt.Title,
            description = prompt.Description,
            odataQueries = queries,
            toolBinding = string.Join(", ", bindings),
            toolBindings = bindings,
            resourceBindings = Resources
                .Where(resource => resource.Tool.EntitySet != null && sources.Contains(resource.Tool.EntitySet))
                .Select(resource => resource.UriTemplate),
            entitySets = sources,
            template = prompt.Messages.FirstOrDefault()?.Text ?? "",
            arguments = prompt.Arguments.Select(argument => new
            {
                name = argument.Name,
                description = argument.Description,
                required = argument.Required,
                @default = argument.Default
            })
        };
    }).ToList();

    public static object GetProjection() => new
    {
        csdlPath = GeneratedModel.SourceCsdl,
        projectionMode = "static-generated-csharp",
        tools = GeneratedModel.Tools.Select(tool => new
        {
            tool.Name,
            tool.Title,
            tool.Description,
            Operation = tool.OperationKind.ToString(),
            ODataQuery = $"{(tool.OperationKind == OperationKind.Action ? "POST" : "GET")} {tool.Path}",
            annotationTarget = tool.AnnotationTarget,
            entitySet = tool.EntitySet,
            Arguments = tool.Arguments.Select(argument => new
            {
                argument.Name,
                Type = argument.Type.FullName,
                argument.Required
            })
        }),
        resources = Resources.Select(resource => new
        {
            resource.Name,
            resource.Title,
            resource.Description,
            resource.UriTemplate,
            MimeType = "application/json",
            entitySet = resource.Tool.EntitySet
        }),
        prompts = GetCatalog(),
        exampleQueries = GeneratedModel.Examples,
        mcpTools = CsdlMcpProjection.ListTools().Tools,
        mcpResources = CsdlMcpProjection.ListResources().Resources,
        mcpResourceTemplates = CsdlMcpProjection.ListResourceTemplates().ResourceTemplates
    };

    public static object GetPromptArchitecturalAnalysis() => new
    {
        modelParser = "No runtime CSDL parser. MCP bindings are generated as static C# source.",
        entityContainer = GeneratedModel.EntityContainer,
        entityTypes = GeneratedModel.EntityTypesCount,
        operations = GeneratedModel.OperationsCount,
        prompts = GeneratedModel.Prompts.Count,
        examples = GeneratedModel.Examples.Count
    };
}

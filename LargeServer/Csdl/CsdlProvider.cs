using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

/// <summary>Identifies EDM value categories used by runtime input validation and literal formatting.</summary>
public enum StaticTypeKind
{
    String, Boolean, Integer, Number, Guid, Date, DateTimeOffset, TimeOfDay,
    Duration, Binary, Enum, Object, Array
}

/// <summary>Distinguishes OData reads, functions, and actions when constructing runtime requests.</summary>
public enum OperationKind { Read, Function, Action }
/// <summary>Selects direct or workload-specific Graph execution for a generated request binding.</summary>
public enum GraphExecutionKind
{
    Direct, Drive, DriveSearch, DriveItem, Teams, Channel, Chats, Todo, ChannelSend, TodoComplete
}

/// <summary>Describes a structured input property's name and runtime type.</summary>
public sealed record StaticProperty(string Name, StaticTypeDescriptor Type);
/// <summary>Supplies generated type constraints for validating runtime values and formatting OData literals.</summary>
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
/// <summary>Describes a request parameter's type, requiredness, key status, and documentation.</summary>
public sealed record ParameterBinding(
    string Name, StaticTypeDescriptor Type, bool Required, bool IsKey = false, string? Description = null);
/// <summary>Carries generated OData execution metadata and linked examples shared by MCP tools and resources; is not an MCP tool.</summary>
public sealed record ODataRequestBinding(
    string Name, string Title, string Description, string Path, OperationKind OperationKind,
    IReadOnlyList<ParameterBinding> Arguments, string AnnotationTarget, string? EntitySet,
    string? EntityType, bool QueryOptions, string InputSchemaJson, GraphExecutionKind ExecutionKind,
    IReadOnlyList<ExampleDefinition> Examples);
/// <summary>Links an MCP resource URI and generated match pattern to its OData request binding.</summary>
public sealed record ResourceBinding(
    string Name, string Title, string Description, string UriTemplate, ODataRequestBinding RequestBinding, Regex MatchPattern);
/// <summary>Describes a prompt argument's requiredness, documentation, and default substitution value.</summary>
public sealed record PromptArgumentDefinition(string Name, string? Description, bool Required, string? Default);
/// <summary>Stores a prompt message's role and text template for runtime substitution.</summary>
public sealed record PromptMessageDefinition(Role Role, string Text);
/// <summary>Stores generated prompt metadata, argument definitions, and message templates.</summary>
public sealed record PromptDefinition(
    string Name, string Title, string Description,
    IReadOnlyList<PromptArgumentDefinition> Arguments, IReadOnlyList<PromptMessageDefinition> Messages);
/// <summary>Carries a CSDL-annotated utterance and OData request associated with an EDM target.</summary>
public sealed record ExampleDefinition(
    string TargetKind, string TargetName, string? EntitySet,
    string Utterance, string Request, string Description);

/// <summary>Exposes transformer-generated metadata and renders prompts from runtime arguments without schema generation.</summary>
public static class CsdlProvider
{
    public static string CsdlFilePath => GeneratedModel.SourceCsdl;
    public static string GetCsdl() => GetCsdlXml();
    public static string GetCsdlXml() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(GeneratedModel.CsdlXmlBase64));
    public static string GetCsdlJson() =>
        Encoding.UTF8.GetString(Convert.FromBase64String(GeneratedModel.CsdlJsonBase64));
    public static IReadOnlyList<ODataRequestBinding> GetRequestBindings() => GeneratedModel.RequestBindings;
    public static IReadOnlyList<ResourceBinding> GetProjectedResources() => GeneratedModel.Resources;
    public static IReadOnlyList<ExampleDefinition> GetExampleQueries() => GeneratedModel.Examples;

    public static IReadOnlyList<ExampleDefinition> GetExamplesFor(ODataRequestBinding binding) => binding.Examples;

    public static ListPromptsResult ListPrompts() => new() { Prompts = GeneratedModel.McpPrompts };

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

    public static object GetCatalog() => GeneratedModel.Catalog;
    public static object GetProjection() => GeneratedModel.Projection;
    public static object GetPromptArchitecturalAnalysis() => GeneratedModel.Analysis;
}

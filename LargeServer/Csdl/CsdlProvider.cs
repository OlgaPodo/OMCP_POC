using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Configuration;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Microsoft.OData.Edm.Vocabularies;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public sealed record ParameterBinding(string Name, IEdmTypeReference Type, bool Required, bool IsKey = false, string? Description = null);
public sealed record ToolBinding(string Name, string Title, string Description, string Path,
    IEdmOperation? Operation, IReadOnlyList<ParameterBinding> Arguments, string AnnotationTarget,
    string? EntitySet, bool QueryOptions = false);
public sealed record ResourceBinding(string Name, string Title, string Description, string UriTemplate, ToolBinding Tool, Regex MatchPattern);
public sealed record PromptArgumentDefinition(string Name, string? Description, bool Required, string? Default);
public sealed record PromptMessageDefinition(Role Role, string Text);
public sealed record PromptDefinition(string Name, string Title, string Description,
    IReadOnlyList<PromptArgumentDefinition> Arguments, IReadOnlyList<PromptMessageDefinition> Messages);
public sealed record ExampleDefinition(string TargetKind, string TargetName, string? EntitySet,
    string Utterance, string Request, string Description);

public static class CsdlProvider
{
    private sealed record CsdlState(string FilePath, string Text, IEdmModel Model,
        IReadOnlyList<ToolBinding> Tools, IReadOnlyList<ResourceBinding> Resources,
        IReadOnlyList<PromptDefinition> Prompts, IReadOnlyList<ExampleDefinition> Examples);

    private static CsdlState? _state;
    private static CsdlState State => _state ?? throw new InvalidOperationException("Initialize the CSDL provider before use.");
    public static string CsdlFilePath => State.FilePath;
    public static IEdmModel EdmModel => State.Model;

    public static string ResolveCsdlPath(IConfiguration config)
    {
        var path = config["Csdl:FilePath"] ?? config["Csdl:Path"]
            ?? Environment.GetEnvironmentVariable("CSDL_FILEPATH")
            ?? Environment.GetEnvironmentVariable("CSDL_PATH");
        if (!string.IsNullOrWhiteSpace(path)) return Path.GetFullPath(path);
        var directory = config["Csdl:Directory"] ?? Environment.GetEnvironmentVariable("CSDL_DIRECTORY")
            ?? AppContext.BaseDirectory;
        var name = config["Csdl:FileName"] ?? Environment.GetEnvironmentVariable("CSDL_FILENAME") ?? "metadata.csdl";
        return Path.GetFullPath(Path.Combine(directory, name));
    }

    public static void Initialize(IConfiguration config) => Initialize(ResolveCsdlPath(config));

    public static void Initialize(string path)
    {
        var text = File.ReadAllText(path);
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        if (!CsdlReader.TryParse(reader, out var model, out var errors))
            throw new InvalidOperationException($"CSDL parse failed: {string.Join("; ", errors.Select(e => e.ErrorMessage))}");
        if (!model.Validate(out errors))
            throw new InvalidOperationException($"CSDL validation failed: {string.Join("; ", errors.Select(e => e.ErrorMessage))}");
        var container = model.EntityContainer ?? throw new InvalidOperationException("CSDL must declare an EntityContainer.");
        var tools = new List<ToolBinding>();
        var resources = new List<ResourceBinding>();
        var sources = container.Elements.OfType<IEdmNavigationSource>().ToList();

        foreach (var source in sources)
        {
            if (source is not IEdmVocabularyAnnotatable annotatable)
                throw new NotSupportedException($"Navigation source {source.Name} does not support vocabulary annotations.");
            var target = $"{container.FullName()}/{source.Name}";
            var keys = source is IEdmEntitySet ? KeyArguments(model, source.EntityType) : [];
            var description = Description(model, annotatable, $"Read {source.Name}.");
            var read = new ToolBinding($"get_{source.Name}", source.Name, description, source.Name,
                null, keys, target, source.Name, true);
            if (source is IEdmEntitySet)
            {
                var list = read with { Name = $"list_{source.Name}", Arguments = [] };
                tools.Add(list);
                var listUri = $"odata://{target}";
                resources.Add(new(source.Name, source.Name, description, listUri, list, CreateResourcePattern(listUri, list.Arguments)));
            }
            tools.Add(read);
            var suffix = string.Concat(keys.Select(k => "/{" + k.Name + "}"));
            var readUri = $"odata://{target}{suffix}";
            resources.Add(new($"get_{source.Name}", source.Name, description, readUri, read, CreateResourcePattern(readUri, read.Arguments)));

            foreach (var navigation in source.EntityType.NavigationProperties())
            {
                tools.Add(read with
                {
                    Name = $"get_{source.Name}_{navigation.Name}",
                    Title = $"{source.Name}/{navigation.Name}",
                    Description = Description(model, navigation, $"Read {navigation.Name} from {source.Name}."),
                    Path = $"{source.Name}/{navigation.Name}",
                    AnnotationTarget = $"{source.EntityType.FullName()}/{navigation.Name}"
                });
            }
        }

        foreach (var operation in model.SchemaElements.OfType<IEdmOperation>().Where(o => o.IsBound))
        {
            var bindingType = operation.Parameters.First().Type;
            var collection = bindingType.IsCollection();
            var type = collection ? bindingType.AsCollection().ElementType().Definition : bindingType.Definition;
            if (type is not IEdmEntityType entityType)
                throw new NotSupportedException($"Bound operation {operation.FullName()} must bind to an entity or entity collection.");
            foreach (var source in sources.Where(s => (!collection || s is IEdmEntitySet) &&
                         (IsOrDerivesFrom(s.EntityType, entityType) || IsOrDerivesFrom(entityType, s.EntityType))))
            {
                var keys = !collection && source is IEdmEntitySet ? KeyArguments(model, source.EntityType) : [];
                var cast = IsOrDerivesFrom(source.EntityType, entityType) ? "" : "/" + entityType.FullName();
                tools.Add(OperationBinding(model, operation, $"{source.Name}{cast}/{operation.FullName()}",
                    $"{source.Name}_{operation.FullName()}", keys, source.Name));
            }
        }
        foreach (var import in container.Elements.OfType<IEdmOperationImport>())
            tools.Add(OperationBinding(model, import.Operation, import.Name, import.Name, [], null));

        // Overloads retain distinct, stable names without depending on schema declaration order.
        foreach (var group in tools.GroupBy(t => SafeName(t.Name)).ToList())
        {
            foreach (var tool in group)
            {
                var index = tools.IndexOf(tool);
                var signature = tool.Path + "|" + tool.Operation?.SchemaElementKind + "|" +
                    string.Join("|", tool.Arguments.Select(a => $"{a.Name}:{a.Type.FullName()}"));
                var suffix = group.Count() > 1
                    ? "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..12].ToLowerInvariant()
                    : "";
                var name = SafeName(tool.Name) + suffix;
                if (name.Length > 128)
                    name = name[..115] + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12].ToLowerInvariant();
                tools[index] = tool with { Name = name };
                for (var i = 0; i < resources.Count; i++)
                    if (ReferenceEquals(resources[i].Tool, tool)) resources[i] = resources[i] with { Tool = tools[index] };
            }
        }
        if (tools.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != tools.Count)
            throw new InvalidOperationException("CSDL produces duplicate tool bindings.");
        resources = resources.DistinctBy(r => r.UriTemplate).ToList();
        var prompts = Records(model, container, "Agent.Prompts").Select(ReadPrompt).ToList();
        if (prompts.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != prompts.Count)
            throw new InvalidOperationException("CSDL prompt names must be unique.");
        var examples = new List<ExampleDefinition>();
        foreach (var annotation in model.VocabularyAnnotations.Where(a => a.Term.FullName() is "Agent.Examples" or "Agent.ExampleQueries"))
        {
            foreach (var record in Collection(annotation.Value).Select(Record))
            {
                if (record.Properties.Any(p => p.Name == "Prompt"))
                    throw new InvalidOperationException("Examples must not reference prompts.");
                var target = annotation.Target switch
                {
                    IEdmEntityContainerElement e => $"{e.Container.FullName()}/{e.Name}",
                    IEdmSchemaElement e => e.FullName(),
                    _ => throw new NotSupportedException($"Unsupported example target: {annotation.Target.GetType().Name}")
                };
                examples.Add(new(annotation.Target is IEdmSchemaElement element ? element.SchemaElementKind.ToString() :
                    annotation.Target is IEdmEntitySet ? "EntitySet" : "Singleton", target,
                    annotation.Target is IEdmNavigationSource s ? s.Name : null,
                    String(record, "Utterance"), String(record, "Request"), String(record, "Description", false)));
            }
        }
        foreach (var tool in tools) _ = EdmJsonSchema.ForArguments(tool);
        _state = new(Path.GetFullPath(path), text, model, tools, resources, prompts, examples);
    }

    private static string SafeName(string name) => Regex.Replace(name, "[^A-Za-z0-9_-]", "_");
    private static bool IsOrDerivesFrom(IEdmEntityType type, IEdmEntityType expected)
    {
        for (IEdmStructuredType? current = type; current != null; current = current.BaseType)
            if (current == expected) return true;
        return false;
    }

    private static List<ParameterBinding> KeyArguments(IEdmModel model, IEdmEntityType type) =>
        type.Key().Select(k => new ParameterBinding("key_" + k.Name, k.Type, true, true,
            Description(model, k, $"Entity key property '{k.Name}'."))).ToList();

    private static Regex CreateResourcePattern(string uriTemplate, IReadOnlyList<ParameterBinding> keys)
    {
        var pattern = Regex.Escape(uriTemplate);
        foreach (var key in keys.Where(a => a.IsKey))
            pattern = pattern.Replace(Regex.Escape("{" + key.Name + "}"), $"(?<{key.Name}>[^/]+)");
        return new Regex("^" + pattern + "$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    }

    private static ToolBinding OperationBinding(IEdmModel model, IEdmOperation operation, string path,
        string name, IReadOnlyList<ParameterBinding> keys, string? entitySet)
    {
        var arguments = keys.Concat(operation.Parameters.Skip(operation.IsBound ? 1 : 0)
            .Select(p => new ParameterBinding(p.Name, p.Type, p is not IEdmOptionalParameter, false,
                Description(model, p, $"Parameter '{p.Name}'.")))).ToList();
        if (arguments.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != arguments.Count)
            throw new InvalidOperationException($"Operation {operation.FullName()} parameter conflicts with a generated key argument.");
        return new(name, operation.Name, Description(model, operation, $"Invoke {operation.FullName()}."),
            path, operation, arguments, operation.FullName(), entitySet);
    }

    private static string Description(IEdmModel model, IEdmVocabularyAnnotatable target, string fallback) =>
        model.FindVocabularyAnnotations(target).FirstOrDefault(a => a.Term.FullName() == "Org.OData.Core.V1.Description")
            ?.Value is IEdmStringConstantExpression s ? s.Value : fallback;

    private static IEnumerable<IEdmRecordExpression> Records(IEdmModel model, IEdmVocabularyAnnotatable target, string term) =>
        model.FindVocabularyAnnotations(target).Where(a => a.Term.FullName() == term)
            .SelectMany(a => Collection(a.Value)).Select(Record);
    private static IEnumerable<IEdmExpression> Collection(IEdmExpression expression) =>
        expression is IEdmCollectionExpression c ? c.Elements : throw new InvalidOperationException("Expected an annotation collection.");
    private static IEdmRecordExpression Record(IEdmExpression expression) =>
        expression as IEdmRecordExpression ?? throw new InvalidOperationException("Expected an annotation record.");
    private static IEdmExpression? Property(IEdmRecordExpression record, string name) =>
        record.Properties.SingleOrDefault(p => p.Name == name)?.Value;
    private static string String(IEdmRecordExpression record, string name, bool required = true) =>
        Property(record, name) is IEdmStringConstantExpression s ? s.Value :
        !required && Property(record, name) == null ? "" : throw new InvalidOperationException($"Expected annotation string {name}.");
    private static bool Boolean(IEdmRecordExpression record, string name) =>
        Property(record, name) is IEdmBooleanConstantExpression b ? b.Value : throw new InvalidOperationException($"Expected annotation Boolean {name}.");

    private static PromptDefinition ReadPrompt(IEdmRecordExpression record)
    {
        var arguments = Collection(Property(record, "Arguments") ?? throw new InvalidOperationException("Missing prompt Arguments."))
            .Select(Record).Select(a => new PromptArgumentDefinition(String(a, "Name"), String(a, "Description", false),
                Boolean(a, "Required"), Property(a, "Default") == null ? null : String(a, "Default"))).ToList();
        if (arguments.Select(a => a.Name).Distinct().Count() != arguments.Count)
            throw new InvalidOperationException("Prompt argument names must be unique.");
        var messages = Collection(Property(record, "Messages") ?? throw new InvalidOperationException("Missing prompt Messages."))
            .Select(Record).Select(m =>
            {
                var content = Record(Property(m, "Content") ?? throw new InvalidOperationException("Missing prompt Content."));
                if (String(content, "Type") != "text") throw new NotSupportedException("Only text prompt content is supported.");
                var role = String(m, "Role") switch
                {
                    "user" => Role.User,
                    "assistant" => Role.Assistant,
                    var value => throw new InvalidOperationException($"Unsupported prompt role: {value}")
                };
                return new PromptMessageDefinition(role, String(content, "Text"));
            }).ToList();
        return new(String(record, "Name"), String(record, "Title", false), String(record, "Description", false), arguments, messages);
    }

    public static string GetCsdl() => State.Text;
    public static string GetCsdlXml() => State.Text;
    public static string GetCsdlJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            if (!CsdlWriter.TryWriteCsdl(EdmModel, writer, out var errors))
                throw new InvalidOperationException($"CSDL JSON export failed: {string.Join("; ", errors.Select(e => e.ErrorMessage))}");
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    public static IReadOnlyList<ToolBinding> GetProjectedTools() => State.Tools;
    public static IReadOnlyList<ResourceBinding> GetProjectedResources() => State.Resources;
    public static IReadOnlyList<ExampleDefinition> GetExampleQueries() => State.Examples;

    public static ListPromptsResult ListPrompts() => new()
    {
        Prompts = State.Prompts.Select(p => new Prompt
        {
            Name = p.Name, Title = p.Title, Description = p.Description,
            Arguments = p.Arguments.Select(a => new PromptArgument { Name = a.Name, Description = a.Description, Required = a.Required }).ToList()
        }).ToList()
    };

    public static GetPromptResult GetPrompt(GetPromptRequestParams request)
    {
        var prompt = State.Prompts.SingleOrDefault(p => p.Name == request.Name) ?? throw new ArgumentException($"Unknown prompt: {request.Name}");
        if (request.Arguments?.Keys.Any(k => !prompt.Arguments.Any(a => a.Name == k)) == true)
            throw new ArgumentException("Unknown prompt argument.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in prompt.Arguments)
        {
            var value = argument.Default;
            if (request.Arguments != null && request.Arguments.TryGetValue(argument.Name, out var supplied))
            {
                if (supplied.ValueKind != JsonValueKind.String) throw new ArgumentException($"Prompt argument {argument.Name} must be a string.");
                value = supplied.GetString();
            }
            if (value == null && argument.Required) throw new ArgumentException($"Missing required prompt argument: {argument.Name}");
            values[argument.Name] = value ?? "";
        }
        return new()
        {
            Description = prompt.Description,
            Messages = prompt.Messages.Select(m => new PromptMessage
            {
                Role = m.Role, Content = new TextContentBlock
                {
                    Text = Regex.Replace(m.Text, @"\{([A-Za-z0-9_]+)\}", match =>
                        values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value)
                }
            }).ToList()
        };
    }

    public static IReadOnlyList<string> ExtractODataQueries(string text) =>
        Regex.Matches(text, @"(?:GET|POST)\s+([^\r\n]+?)(?=\s+(?:and\s+)?(?:GET|POST)\s+|[.]\s|\r?\n|$)")
            .Select(m => m.Groups[1].Value.TrimEnd('.', ',', ';')).Distinct(StringComparer.Ordinal).ToList();

    public static object GetCatalog() => State.Prompts.Select(p =>
    {
        var queries = p.Messages.SelectMany(m => ExtractODataQueries(m.Text)).Distinct().ToList();
        var sources = EdmModel.EntityContainer.Elements.OfType<IEdmNavigationSource>()
            .Where(s => queries.Any(q => Regex.IsMatch(q, $@"^{Regex.Escape(s.Name)}(?:[/('?$]|$)"))).Select(s => s.Name).ToList();
        var bindings = State.Tools.Where(t => t.EntitySet != null && sources.Contains(t.EntitySet)).Select(t => t.Name).ToList();
        return new
        {
            name = p.Name, title = p.Title, description = p.Description, odataQueries = queries,
            toolBinding = string.Join(", ", bindings), toolBindings = bindings,
            resourceBindings = State.Resources.Where(r => r.Tool.EntitySet != null && sources.Contains(r.Tool.EntitySet)).Select(r => r.UriTemplate),
            entitySets = sources, template = p.Messages.FirstOrDefault()?.Text ?? "",
            arguments = p.Arguments.Select(a => new { name = a.Name, description = a.Description, required = a.Required, @default = a.Default })
        };
    }).ToList();

    public static object GetProjection() => new
    {
        csdlPath = CsdlFilePath,
        tools = State.Tools.Select(t => new { t.Name, t.Title, t.Description, Operation = t.Operation?.FullName() ?? "Read",
            ODataQuery = $"{(t.Operation is IEdmAction ? "POST" : "GET")} {t.Path}", annotationTarget = t.AnnotationTarget,
            entitySet = t.EntitySet, Arguments = t.Arguments.Select(a => new { a.Name, Type = a.Type.FullName(), a.Required }) }),
        resources = State.Resources.Select(r => new { r.Name, r.Title, r.Description, r.UriTemplate, MimeType = "application/json", entitySet = r.Tool.EntitySet }),
        prompts = GetCatalog(), exampleQueries = State.Examples,
        mcpTools = CsdlMcpProjection.ListTools().Tools, mcpResources = CsdlMcpProjection.ListResources().Resources,
        mcpResourceTemplates = CsdlMcpProjection.ListResourceTemplates().ResourceTemplates
    };
    public static object GetPromptArchitecturalAnalysis() => new
    {
        modelParser = "CSDL XML -> validated IEdmModel -> typed MCP bindings. JSON is serialized only at output boundaries.",
        entityContainer = EdmModel.EntityContainer.FullName(), entityTypes = EdmModel.SchemaElements.OfType<IEdmEntityType>().Count(),
        operations = EdmModel.SchemaElements.OfType<IEdmOperation>().Count(), prompts = State.Prompts.Count, examples = State.Examples.Count
    };
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Vocabularies;

namespace CsdlToMcpTransformer;

/// <summary>Transforms a validated EDM model into standalone runtime bindings, discovery metadata, and diagnostics.</summary>
internal static class StaticModelGenerator
{
    /// <summary>Retains an EDM parameter's generation-time type, constraints, and documentation.</summary>
    private sealed record Parameter(
        string Name, IEdmTypeReference Type, bool Required, bool IsKey, string Description);
    /// <summary>Represents a generation-time OData request plan before runtime source is emitted.</summary>
    private sealed record ODataRequestBinding(
        string Name, string Title, string Description, string Path, string OperationKind,
        IReadOnlyList<Parameter> Arguments, string AnnotationTarget, string? EntitySet,
        string? EntityType, bool QueryOptions, string ExecutionKind = "Direct");
    /// <summary>Associates a generation-time resource URI with the request plan it will execute.</summary>
    private sealed record Resource(
        string Name, string Title, string Description, string UriTemplate, ODataRequestBinding RequestBinding);
    /// <summary>Retains prompt argument annotations for runtime metadata emission.</summary>
    private sealed record PromptArgument(string Name, string Description, bool Required, string? Default);
    /// <summary>Retains an annotated prompt message's role and unrendered template.</summary>
    private sealed record PromptMessage(string Role, string Text);
    /// <summary>Collects an annotated prompt's metadata, arguments, and messages during generation.</summary>
    private sealed record Prompt(
        string Name, string Title, string Description,
        IReadOnlyList<PromptArgument> Arguments, IReadOnlyList<PromptMessage> Messages);
    /// <summary>Retains an example's EDM target and request text for generation-time association.</summary>
    private sealed record Example(
        string TargetKind, string TargetName, string? EntitySet,
        string Utterance, string Request, string Description);

    /// <summary>Reports emitted source and projection counts to transformer callers.</summary>
    internal sealed record Result(
        string Source,
        int ToolCount,
        int ResourceCount,
        int PromptCount,
        int ExampleCount);

    public static Result Generate(
        IEdmModel model,
        string csdlPath,
        string csdlXml,
        string outputPath)
    {
        var container = model.EntityContainer
            ?? throw new InvalidOperationException("CSDL must declare an EntityContainer.");
        var sources = container.Elements.OfType<IEdmNavigationSource>().ToList();
        var bindings = new List<ODataRequestBinding>();
        var resources = new List<Resource>();

        foreach (var source in sources)
        {
            if (source is not IEdmVocabularyAnnotatable annotatable)
                throw new NotSupportedException($"Navigation source {source.Name} does not support annotations.");
            var target = $"{container.FullName()}/{source.Name}";
            var keys = source is IEdmEntitySet ? KeyArguments(model, source.EntityType) : [];
            var description = Description(model, annotatable, $"Read {source.Name}.");
            var read = new ODataRequestBinding(
                $"get_{source.Name}", source.Name, description, source.Name, "Read", keys,
                target, source.Name, source.EntityType.FullName(), true);
            if (source is IEdmEntitySet)
            {
                var list = read with { Name = $"list_{source.Name}", Arguments = [] };
                bindings.Add(list);
                resources.Add(new Resource(
                    source.Name, source.Name, description, $"odata://{target}", list));
            }
            bindings.Add(read);
            var suffix = string.Concat(keys.Select(key => "/{" + key.Name + "}"));
            resources.Add(new Resource(
                $"get_{source.Name}", source.Name, description, $"odata://{target}{suffix}", read));

            foreach (var navigation in source.EntityType.NavigationProperties())
            {
                bindings.Add(read with
                {
                    Name = $"get_{source.Name}_{navigation.Name}",
                    Title = $"{source.Name}/{navigation.Name}",
                    Description = Description(model, navigation, $"Read {navigation.Name} from {source.Name}."),
                    Path = $"{source.Name}/{navigation.Name}",
                    AnnotationTarget = $"{source.EntityType.FullName()}/{navigation.Name}"
                });
            }
        }

        foreach (var operation in model.SchemaElements.OfType<IEdmOperation>().Where(operation => operation.IsBound))
        {
            var bindingType = operation.Parameters.First().Type;
            var collection = bindingType.IsCollection();
            var type = collection ? bindingType.AsCollection().ElementType().Definition : bindingType.Definition;
            if (type is not IEdmEntityType entityType)
                throw new NotSupportedException($"Bound operation {operation.FullName()} must bind to an entity.");
            foreach (var source in sources.Where(source =>
                         (!collection || source is IEdmEntitySet) &&
                         (IsOrDerivesFrom(source.EntityType, entityType) ||
                          IsOrDerivesFrom(entityType, source.EntityType))))
            {
                var keys = !collection && source is IEdmEntitySet
                    ? KeyArguments(model, source.EntityType)
                    : [];
                var cast = IsOrDerivesFrom(source.EntityType, entityType) ? "" : "/" + entityType.FullName();
                bindings.Add(OperationBinding(
                    model, operation, $"{source.Name}{cast}/{operation.FullName()}",
                    $"{source.Name}_{operation.FullName()}", keys, source.Name, source.EntityType.FullName()));
            }
        }

        foreach (var import in container.Elements.OfType<IEdmOperationImport>())
            bindings.Add(OperationBinding(model, import.Operation, import.Name, import.Name, [], null, null));

        foreach (var group in bindings.GroupBy(binding => SafeName(binding.Name)).ToList())
        {
            foreach (var binding in group)
            {
                var index = bindings.IndexOf(binding);
                var signature = binding.Path + "|" + binding.OperationKind + "|" +
                    string.Join("|", binding.Arguments.Select(argument =>
                        $"{argument.Name}:{argument.Type.FullName()}"));
                var suffix = group.Count() > 1
                    ? "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)))[..12].ToLowerInvariant()
                    : "";
                var name = SafeName(binding.Name) + suffix;
                if (name.Length > 128)
                    name = name[..115] + "_" +
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12].ToLowerInvariant();
                var renamed = binding with { Name = name, ExecutionKind = GraphExecutionKind(binding) };
                bindings[index] = renamed;
                for (var resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
                    if (ReferenceEquals(resources[resourceIndex].RequestBinding, binding))
                        resources[resourceIndex] = resources[resourceIndex] with { RequestBinding = renamed };
            }
        }

        if (bindings.Select(binding => binding.Name).Distinct(StringComparer.Ordinal).Count() != bindings.Count)
            throw new InvalidOperationException("CSDL produces duplicate OData request bindings.");
        resources = resources.DistinctBy(resource => resource.UriTemplate).ToList();

        var prompts = Records(model, container, "Agent.Prompts").Select(ReadPrompt).ToList();
        var examples = new List<Example>();
        foreach (var annotation in model.VocabularyAnnotations.Where(annotation =>
                     annotation.Term.FullName() is "Agent.Examples" or "Agent.ExampleQueries"))
        {
            foreach (var record in Collection(annotation.Value).Select(Record))
            {
                var target = annotation.Target switch
                {
                    IEdmEntityContainerElement element => $"{element.Container.FullName()}/{element.Name}",
                    IEdmSchemaElement element => element.FullName(),
                    _ => throw new NotSupportedException(
                        $"Unsupported example target: {annotation.Target.GetType().Name}")
                };
                examples.Add(new Example(
                    annotation.Target is IEdmSchemaElement schemaElement
                        ? schemaElement.SchemaElementKind.ToString()
                        : annotation.Target is IEdmEntitySet ? "EntitySet" : "Singleton",
                    target,
                    annotation.Target is IEdmNavigationSource source ? source.Name : null,
                    String(record, "Utterance"),
                    String(record, "Request"),
                    String(record, "Description", false)));
            }
        }

        var csdlJson = WriteCsdlJson(model);
        var generatedSource = EmitSource(
            csdlPath, csdlXml, csdlJson, container, model, bindings, resources, prompts, examples);
        File.WriteAllText(outputPath, generatedSource);
        return new Result(generatedSource, bindings.Count, resources.Count, prompts.Count, examples.Count);
    }

    private static string EmitSource(
        string csdlPath,
        string csdlXml,
        string csdlJson,
        IEdmEntityContainer container,
        IEdmModel model,
        IReadOnlyList<ODataRequestBinding> bindings,
        IReadOnlyList<Resource> resources,
        IReadOnlyList<Prompt> prompts,
        IReadOnlyList<Example> examples)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Text.Json;");
        builder.AppendLine("using System.Text.RegularExpressions;");
        builder.AppendLine("using ModelContextProtocol.Protocol;");
        builder.AppendLine();
        builder.AppendLine("namespace GraphMcpServer.Csdl;");
        builder.AppendLine();
        builder.AppendLine("/// <summary>Contains transformer-emitted bindings, discovery metadata, prompts, examples, and diagnostics consumed at runtime.</summary>");
        builder.AppendLine("public static class GeneratedModel");
        builder.AppendLine("{");
        builder.AppendLine($"    public const string SourceCsdl = {Quote(Path.GetFileName(csdlPath))};");
        builder.AppendLine($"    public const string EntityContainer = {Quote(container.FullName())};");
        builder.AppendLine($"    public const int EntityTypesCount = {model.SchemaElements.OfType<IEdmEntityType>().Count()};");
        builder.AppendLine($"    public const int OperationsCount = {model.SchemaElements.OfType<IEdmOperation>().Count()};");
        builder.AppendLine($"    public const int SchemaElementsCount = {model.SchemaElements.Count()};");
        builder.AppendLine($"    public const string CsdlXmlBase64 = {Quote(Convert.ToBase64String(Encoding.UTF8.GetBytes(csdlXml)))};");
        builder.AppendLine($"    public const string CsdlJsonBase64 = {Quote(Convert.ToBase64String(Encoding.UTF8.GetBytes(csdlJson)))};");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyList<ExampleDefinition> Examples = new ExampleDefinition[]");
        builder.AppendLine("    {");
        foreach (var example in examples)
            builder.AppendLine($"        new({Quote(example.TargetKind)}, {Quote(example.TargetName)}, {Quote(example.EntitySet)}, {Quote(example.Utterance)}, {Quote(example.Request)}, {Quote(example.Description)}),");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyList<ODataRequestBinding> RequestBindings = new ODataRequestBinding[]");
        builder.AppendLine("    {");
        foreach (var binding in bindings)
        {
            builder.AppendLine("        new(");
            builder.AppendLine($"            {Quote(binding.Name)}, {Quote(binding.Title)}, {Quote(binding.Description)}, {Quote(binding.Path)},");
            builder.AppendLine($"            OperationKind.{binding.OperationKind},");
            builder.AppendLine("            new ParameterBinding[]");
            builder.AppendLine("            {");
            foreach (var argument in binding.Arguments)
                builder.AppendLine($"                new({Quote(argument.Name)}, {TypeExpression(argument.Type)}, {Bool(argument.Required)}, {Bool(argument.IsKey)}, {Quote(argument.Description)}),");
            builder.AppendLine("            },");
            builder.AppendLine($"            {Quote(binding.AnnotationTarget)}, {Quote(binding.EntitySet)}, {Quote(binding.EntityType)}, {Bool(binding.QueryOptions)},");
            builder.AppendLine($"            {Quote(InputSchemaJson(binding))}, GraphExecutionKind.{binding.ExecutionKind},");
            builder.AppendLine($"            new ExampleDefinition[] {{ {string.Join(", ", ExampleIndices(binding, examples).Select(index => $"Examples[{index}]"))} }}),");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly List<Tool> McpTools = new()");
        builder.AppendLine("    {");
        for (var index = 0; index < bindings.Count; index++)
        {
            builder.AppendLine("        new()");
            builder.AppendLine("        {");
            builder.AppendLine($"            Name = RequestBindings[{index}].Name, Title = RequestBindings[{index}].Title,");
            builder.AppendLine($"            Description = RequestBindings[{index}].Description,");
            builder.AppendLine($"            InputSchema = JsonSerializer.Deserialize<JsonElement>(RequestBindings[{index}].InputSchemaJson)");
            builder.AppendLine("        },");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        var bindingIndices = bindings.Select((binding, index) => (binding.Name, Index: index))
            .ToDictionary(binding => binding.Name, binding => binding.Index, StringComparer.Ordinal);
        builder.AppendLine("    public static readonly IReadOnlyList<ResourceBinding> Resources = new ResourceBinding[]");
        builder.AppendLine("    {");
        foreach (var resource in resources)
            builder.AppendLine($"        new({Quote(resource.Name)}, {Quote(resource.Title)}, {Quote(resource.Description)}, {Quote(resource.UriTemplate)}, RequestBindings[{bindingIndices[resource.RequestBinding.Name]}], new Regex({Quote(ResourceMatchPattern(resource))}, RegexOptions.CultureInvariant | RegexOptions.Compiled)),");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly List<Resource> McpResources = new()");
        builder.AppendLine("    {");
        foreach (var resource in resources.Where(resource => !resource.UriTemplate.Contains('{')))
            builder.AppendLine($"        new() {{ Name = {Quote(resource.Name)}, Title = {Quote(resource.Title)}, Description = {Quote(resource.Description)}, Uri = {Quote(resource.UriTemplate)}, MimeType = \"application/json\" }},");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly List<ResourceTemplate> McpResourceTemplates = new()");
        builder.AppendLine("    {");
        foreach (var resource in resources.Where(resource => resource.UriTemplate.Contains('{')))
            builder.AppendLine($"        new() {{ Name = {Quote(resource.Name)}, Title = {Quote(resource.Title)}, Description = {Quote(resource.Description)}, UriTemplate = {Quote(resource.UriTemplate)}, MimeType = \"application/json\" }},");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly IReadOnlyList<PromptDefinition> Prompts = new PromptDefinition[]");
        builder.AppendLine("    {");
        foreach (var prompt in prompts)
        {
            builder.AppendLine("        new(");
            builder.AppendLine($"            {Quote(prompt.Name)}, {Quote(prompt.Title)}, {Quote(prompt.Description)},");
            builder.AppendLine("            new PromptArgumentDefinition[]");
            builder.AppendLine("            {");
            foreach (var argument in prompt.Arguments)
                builder.AppendLine($"                new({Quote(argument.Name)}, {Quote(argument.Description)}, {Bool(argument.Required)}, {Quote(argument.Default)}),");
            builder.AppendLine("            },");
            builder.AppendLine("            new PromptMessageDefinition[]");
            builder.AppendLine("            {");
            foreach (var message in prompt.Messages)
                builder.AppendLine($"                new(Role.{message.Role}, {Quote(message.Text)}),");
            builder.AppendLine("            }),");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly List<Prompt> McpPrompts = new()");
        builder.AppendLine("    {");
        foreach (var prompt in prompts)
        {
            builder.AppendLine("        new()");
            builder.AppendLine("        {");
            builder.AppendLine($"            Name = {Quote(prompt.Name)}, Title = {Quote(prompt.Title)}, Description = {Quote(prompt.Description)},");
            builder.AppendLine("            Arguments = new List<PromptArgument>");
            builder.AppendLine("            {");
            foreach (var argument in prompt.Arguments)
                builder.AppendLine($"                new() {{ Name = {Quote(argument.Name)}, Description = {Quote(argument.Description)}, Required = {Bool(argument.Required)} }},");
            builder.AppendLine("            }");
            builder.AppendLine("        },");
        }
        builder.AppendLine("    };");
        builder.AppendLine();
        var catalog = PromptCatalog(prompts, container.EntitySets().Select(set => set.Name).ToList(), bindings, resources);
        builder.AppendLine($"    public static readonly JsonElement Catalog = JsonSerializer.Deserialize<JsonElement>({Quote(JsonSerializer.Serialize(catalog))});");
        var projectedBindings = bindings.Select(binding => new
        {
            binding.Name,
            binding.Title,
            binding.Description,
            Operation = binding.OperationKind,
            ExecutionKind = binding.ExecutionKind,
            ODataQuery = $"{(binding.OperationKind == "Action" ? "POST" : "GET")} {binding.Path}",
            annotationTarget = binding.AnnotationTarget,
            entitySet = binding.EntitySet,
            Arguments = binding.Arguments.Select(argument => new
            {
                argument.Name,
                Type = argument.Type.FullName(),
                argument.Required
            })
        });
        var projectedResources = resources.Select(resource => new
        {
            resource.Name,
            resource.Title,
            resource.Description,
            resource.UriTemplate,
            MimeType = "application/json",
            entitySet = resource.RequestBinding.EntitySet
        });
        builder.AppendLine();
        builder.AppendLine("    public static readonly object Projection = new");
        builder.AppendLine("    {");
        builder.AppendLine("        csdlPath = SourceCsdl,");
        builder.AppendLine("        projectionMode = \"static-generated-csharp\",");
        builder.AppendLine($"        tools = JsonSerializer.Deserialize<JsonElement>({Quote(JsonSerializer.Serialize(projectedBindings))}),");
        builder.AppendLine($"        resources = JsonSerializer.Deserialize<JsonElement>({Quote(JsonSerializer.Serialize(projectedResources))}),");
        builder.AppendLine("        prompts = Catalog,");
        builder.AppendLine("        exampleQueries = Examples,");
        builder.AppendLine("        mcpTools = McpTools,");
        builder.AppendLine("        mcpResources = McpResources,");
        builder.AppendLine("        mcpResourceTemplates = McpResourceTemplates");
        builder.AppendLine("    };");
        builder.AppendLine();
        builder.AppendLine("    public static readonly object Analysis = new");
        builder.AppendLine("    {");
        builder.AppendLine("        modelParser = \"No runtime CSDL parser. MCP bindings are generated as static C# source.\",");
        builder.AppendLine("        entityContainer = EntityContainer,");
        builder.AppendLine("        entityTypes = EntityTypesCount,");
        builder.AppendLine("        operations = OperationsCount,");
        builder.AppendLine("        prompts = Prompts.Count,");
        builder.AppendLine("        examples = Examples.Count");
        builder.AppendLine("    };");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static IEnumerable<int> ExampleIndices(ODataRequestBinding binding, IReadOnlyList<Example> examples) =>
        examples.Select((example, index) => (Example: example, Index: index))
            .Where(item =>
                item.Example.TargetName == binding.AnnotationTarget ||
                binding.EntitySet != null && item.Example.EntitySet == binding.EntitySet ||
                binding.EntityType != null && item.Example.TargetName == binding.EntityType)
            .DistinctBy(item => item.Example)
            .Select(item => item.Index);

    private static string ResourceMatchPattern(Resource resource)
    {
        var pattern = Regex.Escape(resource.UriTemplate);
        foreach (var key in resource.RequestBinding.Arguments.Where(argument => argument.IsKey))
            pattern = pattern.Replace(Regex.Escape("{" + key.Name + "}"), $"(?<{key.Name}>[^/]+)");
        return "^" + pattern + "$";
    }

    private static object PromptCatalog(
        IReadOnlyList<Prompt> prompts,
        IReadOnlyList<string> entitySets,
        IReadOnlyList<ODataRequestBinding> bindings,
        IReadOnlyList<Resource> resources) => prompts.Select(prompt =>
    {
        var queries = prompt.Messages.SelectMany(message => ExtractODataQueries(message.Text)).Distinct().ToList();
        var sources = entitySets
            .Where(source => queries.Any(query => Regex.IsMatch(query, $@"^{Regex.Escape(source)}(?:[/('?$]|$)")))
            .ToList();
        var toolNames = bindings
            .Where(binding => binding.EntitySet != null && sources.Contains(binding.EntitySet))
            .Select(binding => binding.Name)
            .ToList();
        return new
        {
            name = prompt.Name,
            title = prompt.Title,
            description = prompt.Description,
            odataQueries = queries,
            toolBinding = string.Join(", ", toolNames),
            toolBindings = toolNames,
            resourceBindings = resources
                .Where(resource => resource.RequestBinding.EntitySet != null && sources.Contains(resource.RequestBinding.EntitySet))
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

    private static IReadOnlyList<string> ExtractODataQueries(string text) =>
        Regex.Matches(text, @"(?:GET|POST)\s+([^\r\n]+?)(?=\s+(?:and\s+)?(?:GET|POST)\s+|[.]\s|\r?\n|$)")
            .Select(match => match.Groups[1].Value.TrimEnd('.', ',', ';'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string InputSchemaJson(ODataRequestBinding binding)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var definitions = new JsonObject();
        foreach (var argument in binding.Arguments)
        {
            var property = Schema(argument.Type, definitions, []);
            if (!string.IsNullOrWhiteSpace(argument.Description))
                property["description"] = argument.Description;
            properties[argument.Name] = property;
            if (argument.Required) required.Add(argument.Name);
        }
        if (binding.QueryOptions)
        {
            foreach (var (name, description) in QueryOptionDescriptions)
            {
                var property = name is "top" or "skip"
                    ? new JsonObject { ["type"] = "integer", ["minimum"] = 0 }
                    : new JsonObject { ["type"] = name == "count" ? "boolean" : "string" };
                property["description"] = description;
                properties[name] = property;
            }
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false,
            ["$defs"] = definitions
        }.ToJsonString();
    }

    private static readonly IReadOnlyDictionary<string, string> QueryOptionDescriptions =
        new Dictionary<string, string>
        {
            ["filter"] = "OData $filter expression",
            ["select"] = "Comma-separated list of properties to return ($select)",
            ["expand"] = "Comma-separated list of navigation properties to expand ($expand)",
            ["orderby"] = "OData $orderby sort expression",
            ["top"] = "Maximum number of items to return ($top)",
            ["skip"] = "Number of items to skip ($skip)",
            ["count"] = "Include total count of items in response ($count)",
            ["search"] = "Free-text search query ($search)"
        };

    private static JsonObject Schema(
        IEdmTypeReference type,
        JsonObject definitions,
        HashSet<string> visiting)
    {
        JsonObject schema;
        if (type.IsCollection())
        {
            schema = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Schema(type.AsCollection().ElementType(), definitions, visiting)
            };
        }
        else if (type.Definition is IEdmStructuredType structured)
        {
            var name = ((IEdmSchemaType)structured).FullName();
            if (!definitions.ContainsKey(name))
            {
                definitions[name] = new JsonObject();
                if (visiting.Add(name))
                {
                    var properties = new JsonObject();
                    var required = new JsonArray();
                    foreach (var property in structured.StructuralProperties())
                    {
                        properties[property.Name] = Schema(property.Type, definitions, visiting);
                        if (!property.Type.IsNullable) required.Add(property.Name);
                    }
                    definitions[name] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = properties,
                        ["required"] = required,
                        ["additionalProperties"] = structured.IsOpen
                    };
                    visiting.Remove(name);
                }
            }
            schema = new JsonObject { ["$ref"] = "#/$defs/" + name };
        }
        else if (type.Definition is IEdmEnumType enumeration)
        {
            schema = new JsonObject { ["type"] = "string" };
            if (!enumeration.IsFlags)
                schema["enum"] = new JsonArray(enumeration.Members
                    .Select(member => (JsonNode?)JsonValue.Create(member.Name)).ToArray());
        }
        else
        {
            var kind = PrimitiveKind(type);
            schema = kind switch
            {
                EdmPrimitiveTypeKind.Boolean => new JsonObject { ["type"] = "boolean" },
                EdmPrimitiveTypeKind.Byte or EdmPrimitiveTypeKind.SByte or EdmPrimitiveTypeKind.Int16 or
                    EdmPrimitiveTypeKind.Int32 or EdmPrimitiveTypeKind.Int64 =>
                    new JsonObject { ["type"] = "integer" },
                EdmPrimitiveTypeKind.Decimal or EdmPrimitiveTypeKind.Double or EdmPrimitiveTypeKind.Single =>
                    new JsonObject { ["type"] = "number" },
                _ => new JsonObject { ["type"] = "string" }
            };
            var range = IntegerRange(kind);
            if (range != null)
            {
                schema["minimum"] = range.Value.Min;
                schema["maximum"] = range.Value.Max;
            }
        }
        return type.IsNullable
            ? new JsonObject
            {
                ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" })
            }
            : schema;
    }

    private static string TypeExpression(IEdmTypeReference type, HashSet<string>? visiting = null)
    {
        visiting ??= [];
        if (type.IsCollection())
            return $"new StaticTypeDescriptor({Quote(type.FullName())}, StaticTypeKind.Array, {Bool(type.IsNullable)}, ElementType: {TypeExpression(type.AsCollection().ElementType(), visiting)})";
        if (type.Definition is IEdmStructuredType structured)
        {
            var name = ((IEdmSchemaType)structured).FullName();
            if (!visiting.Add(name))
                return $"new StaticTypeDescriptor({Quote(name)}, StaticTypeKind.Object, {Bool(type.IsNullable)}, IsOpen: {Bool(structured.IsOpen)})";
            var properties = string.Join(", ", structured.StructuralProperties().Select(property =>
                $"new StaticProperty({Quote(property.Name)}, {TypeExpression(property.Type, visiting)})"));
            visiting.Remove(name);
            return $"new StaticTypeDescriptor({Quote(name)}, StaticTypeKind.Object, {Bool(type.IsNullable)}, Properties: new StaticProperty[] {{ {properties} }}, IsOpen: {Bool(structured.IsOpen)})";
        }
        if (type.Definition is IEdmEnumType enumeration)
        {
            var values = string.Join(", ", enumeration.Members.Select(member => Quote(member.Name)));
            return $"new StaticTypeDescriptor({Quote(type.FullName())}, StaticTypeKind.Enum, {Bool(type.IsNullable)}, EnumValues: new string[] {{ {values} }})";
        }
        var primitive = PrimitiveKind(type);
        var kind = primitive switch
        {
            EdmPrimitiveTypeKind.Boolean => "Boolean",
            EdmPrimitiveTypeKind.Byte or EdmPrimitiveTypeKind.SByte or EdmPrimitiveTypeKind.Int16 or
                EdmPrimitiveTypeKind.Int32 or EdmPrimitiveTypeKind.Int64 => "Integer",
            EdmPrimitiveTypeKind.Decimal or EdmPrimitiveTypeKind.Double or EdmPrimitiveTypeKind.Single => "Number",
            EdmPrimitiveTypeKind.Guid => "Guid",
            EdmPrimitiveTypeKind.Date => "Date",
            EdmPrimitiveTypeKind.DateTimeOffset => "DateTimeOffset",
            EdmPrimitiveTypeKind.TimeOfDay => "TimeOfDay",
            EdmPrimitiveTypeKind.Duration => "Duration",
            EdmPrimitiveTypeKind.Binary => "Binary",
            _ => "String"
        };
        var range = IntegerRange(primitive);
        var rangeText = range == null ? "" : $", Minimum: {range.Value.Min}L, Maximum: {range.Value.Max}L";
        return $"new StaticTypeDescriptor({Quote(type.FullName())}, StaticTypeKind.{kind}, {Bool(type.IsNullable)}{rangeText})";
    }

    private static ODataRequestBinding OperationBinding(
        IEdmModel model,
        IEdmOperation operation,
        string path,
        string name,
        IReadOnlyList<Parameter> keys,
        string? entitySet,
        string? entityType)
    {
        var arguments = keys.Concat(operation.Parameters.Skip(operation.IsBound ? 1 : 0)
            .Select(parameter => new Parameter(
                parameter.Name,
                parameter.Type,
                parameter is not IEdmOptionalParameter,
                false,
                Description(model, parameter, $"Parameter '{parameter.Name}'."))))
            .ToList();
        return new ODataRequestBinding(
            name,
            operation.Name,
            Description(model, operation, $"Invoke {operation.FullName()}."),
            path,
            operation is IEdmAction ? "Action" : "Function",
            arguments,
            operation.FullName(),
            entitySet,
            entityType,
            false);
    }

    private static List<Parameter> KeyArguments(IEdmModel model, IEdmEntityType type) =>
        type.Key().Select(key => new Parameter(
            "key_" + key.Name,
            key.Type,
            true,
            true,
            Description(model, key, $"Entity key property '{key.Name}'."))).ToList();

    private static bool IsOrDerivesFrom(IEdmEntityType type, IEdmEntityType expected)
    {
        for (IEdmStructuredType? current = type; current != null; current = current.BaseType)
            if (current == expected) return true;
        return false;
    }

    private static string SafeName(string name) => Regex.Replace(name, "[^A-Za-z0-9_-]", "_");

    private static string GraphExecutionKind(ODataRequestBinding binding)
    {
        if (binding.Path.Contains("searchDriveItems", StringComparison.Ordinal))
            return "DriveSearch";
        if (binding.Path.Contains("recentDriveItems", StringComparison.Ordinal))
            return "Drive";
        if (binding.Path.Contains("deltaChannelMessages", StringComparison.Ordinal))
            return "Channel";
        if (binding.Path.Contains("sendChannelMessage", StringComparison.Ordinal))
            return "ChannelSend";
        if (binding.Path.Contains("completeTodoTask", StringComparison.Ordinal))
            return "TodoComplete";
        if (binding.Path.Contains("/drive", StringComparison.Ordinal))
            return "Drive";
        if (binding.Path.Contains("joinedTeams", StringComparison.Ordinal))
            return "Teams";
        if (binding.Path.Contains("/chats", StringComparison.Ordinal))
            return "Chats";
        if (binding.Path.Contains("/todoLists", StringComparison.Ordinal))
            return "Todo";
        return binding.EntitySet switch
        {
            "drives" => "Drive",
            "driveItems" => binding.Arguments.Any(argument => argument.IsKey) ? "DriveItem" : "Drive",
            "teams" => "Teams",
            "channels" or "chatMessages" => "Channel",
            "chats" => "Chats",
            "todoLists" or "todoTasks" => "Todo",
            _ => "Direct"
        };
    }
    private static string Description(
        IEdmModel model,
        IEdmVocabularyAnnotatable target,
        string fallback) =>
        model.FindVocabularyAnnotations(target)
            .FirstOrDefault(annotation => annotation.Term.FullName() == "Org.OData.Core.V1.Description")
            ?.Value is IEdmStringConstantExpression value ? value.Value : fallback;

    private static Prompt ReadPrompt(IEdmRecordExpression record)
    {
        var arguments = Collection(Property(record, "Arguments")
                ?? throw new InvalidOperationException("Missing prompt Arguments."))
            .Select(Record)
            .Select(argument => new PromptArgument(
                String(argument, "Name"),
                String(argument, "Description", false),
                Boolean(argument, "Required"),
                Property(argument, "Default") == null ? null : String(argument, "Default")))
            .ToList();
        var messages = Collection(Property(record, "Messages")
                ?? throw new InvalidOperationException("Missing prompt Messages."))
            .Select(Record)
            .Select(message =>
            {
                var content = Record(Property(message, "Content")
                    ?? throw new InvalidOperationException("Missing prompt Content."));
                var role = String(message, "Role") switch
                {
                    "user" => "User",
                    "assistant" => "Assistant",
                    var value => throw new InvalidOperationException($"Unsupported prompt role: {value}")
                };
                return new PromptMessage(role, String(content, "Text"));
            }).ToList();
        return new Prompt(
            String(record, "Name"),
            String(record, "Title", false),
            String(record, "Description", false),
            arguments,
            messages);
    }

    private static IEnumerable<IEdmRecordExpression> Records(
        IEdmModel model,
        IEdmVocabularyAnnotatable target,
        string term) =>
        model.FindVocabularyAnnotations(target)
            .Where(annotation => annotation.Term.FullName() == term)
            .SelectMany(annotation => Collection(annotation.Value))
            .Select(Record);
    private static IEnumerable<IEdmExpression> Collection(IEdmExpression expression) =>
        expression is IEdmCollectionExpression collection
            ? collection.Elements
            : throw new InvalidOperationException("Expected an annotation collection.");
    private static IEdmRecordExpression Record(IEdmExpression expression) =>
        expression as IEdmRecordExpression
        ?? throw new InvalidOperationException("Expected an annotation record.");
    private static IEdmExpression? Property(IEdmRecordExpression record, string name) =>
        record.Properties.SingleOrDefault(property => property.Name == name)?.Value;
    private static string String(IEdmRecordExpression record, string name, bool required = true) =>
        Property(record, name) is IEdmStringConstantExpression value
            ? value.Value
            : !required && Property(record, name) == null
                ? ""
                : throw new InvalidOperationException($"Expected annotation string {name}.");
    private static bool Boolean(IEdmRecordExpression record, string name) =>
        Property(record, name) is IEdmBooleanConstantExpression value
            ? value.Value
            : throw new InvalidOperationException($"Expected annotation Boolean {name}.");

    private static string WriteCsdlJson(IEdmModel model)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            if (!CsdlWriter.TryWriteCsdl(model, writer, out var errors))
                throw new InvalidOperationException(
                    $"CSDL JSON export failed: {string.Join("; ", errors.Select(error => error.ErrorMessage))}");
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static EdmPrimitiveTypeKind PrimitiveKind(IEdmTypeReference type) =>
        type.Definition is IEdmTypeDefinition definition
            ? definition.UnderlyingType.PrimitiveKind
            : type.PrimitiveKind();

    private static (long Min, long Max)? IntegerRange(EdmPrimitiveTypeKind kind) => kind switch
    {
        EdmPrimitiveTypeKind.Byte => (byte.MinValue, byte.MaxValue),
        EdmPrimitiveTypeKind.SByte => (sbyte.MinValue, sbyte.MaxValue),
        EdmPrimitiveTypeKind.Int16 => (short.MinValue, short.MaxValue),
        EdmPrimitiveTypeKind.Int32 => (int.MinValue, int.MaxValue),
        EdmPrimitiveTypeKind.Int64 => (long.MinValue, long.MaxValue),
        _ => null
    };

    private static string Quote(string? value) =>
        value == null ? "null" : JsonSerializer.Serialize(value);
    private static string Bool(bool value) => value ? "true" : "false";
}

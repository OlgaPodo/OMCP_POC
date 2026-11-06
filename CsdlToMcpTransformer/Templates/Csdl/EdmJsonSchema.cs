using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Microsoft.OData.Edm;

namespace GraphMcpServer.Csdl;

public static class EdmJsonSchema
{
    public static readonly IReadOnlyDictionary<string, string> QueryOptions = new Dictionary<string, string>
    {
        ["filter"] = "$filter", ["select"] = "$select", ["expand"] = "$expand", ["orderby"] = "$orderby",
        ["top"] = "$top", ["skip"] = "$skip", ["count"] = "$count", ["search"] = "$search"
    };

    public static readonly IReadOnlyDictionary<string, string> QueryOptionDescriptions = new Dictionary<string, string>
    {
        ["filter"] = "OData $filter expression (e.g. \"Price gt 20 and Name eq 'Widget'\")",
        ["select"] = "Comma-separated list of properties to return ($select)",
        ["expand"] = "Comma-separated list of navigation properties to expand ($expand)",
        ["orderby"] = "OData $orderby sort expression (e.g. 'Name asc')",
        ["top"] = "Maximum number of items to return ($top)",
        ["skip"] = "Number of items to skip ($skip)",
        ["count"] = "Include total count of items in response ($count)",
        ["search"] = "Free-text search query ($search)"
    };

    public static JsonElement ForArguments(ToolBinding tool)
    {
        var definitions = new JsonObject();
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var argument in tool.Arguments)
        {
            var prop = Schema(argument.Type, definitions);
            if (!string.IsNullOrWhiteSpace(argument.Description))
            {
                prop["description"] = argument.Description;
            }
            properties[argument.Name] = prop;
            if (argument.Required) required.Add(argument.Name);
        }
        if (tool.QueryOptions)
            foreach (var (option, desc) in QueryOptionDescriptions)
            {
                var optProp = option is "top" or "skip" ? new JsonObject { ["type"] = "integer", ["minimum"] = 0 } :
                    new JsonObject { ["type"] = option == "count" ? "boolean" : "string" };
                optProp["description"] = desc;
                properties[option] = optProp;
            }
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["properties"] = properties, ["required"] = required,
            ["additionalProperties"] = false, ["$defs"] = definitions
        });
    }

    private static JsonObject Schema(IEdmTypeReference type, JsonObject definitions)
    {
        JsonObject schema;
        if (type.IsCollection())
            schema = new() { ["type"] = "array", ["items"] = Schema(type.AsCollection().ElementType(), definitions) };
        else if (type.Definition is IEdmStructuredType structured)
        {
            var name = ((IEdmSchemaType)structured).FullName();
            if (!definitions.ContainsKey(name))
            {
                definitions[name] = new JsonObject();
                var properties = new JsonObject();
                var required = new JsonArray();
                foreach (var property in structured.StructuralProperties())
                {
                    properties[property.Name] = Schema(property.Type, definitions);
                    if (!property.Type.IsNullable) required.Add(property.Name);
                }
                definitions[name] = new JsonObject { ["type"] = "object", ["properties"] = properties,
                    ["required"] = required, ["additionalProperties"] = structured.IsOpen };
            }
            schema = new() { ["$ref"] = "#/$defs/" + name };
        }
        else if (type.Definition is IEdmEnumType enumeration)
        {
            schema = new() { ["type"] = "string" };
            if (!enumeration.IsFlags)
                schema["enum"] = new JsonArray(enumeration.Members.Select(m => (JsonNode?)JsonValue.Create(m.Name)).ToArray());
        }
        else
        {
            var kind = Kind(type);
            schema = kind switch
            {
                EdmPrimitiveTypeKind.Boolean => new() { ["type"] = "boolean" },
                EdmPrimitiveTypeKind.Byte or EdmPrimitiveTypeKind.SByte or EdmPrimitiveTypeKind.Int16 or
                    EdmPrimitiveTypeKind.Int32 or EdmPrimitiveTypeKind.Int64 => new() { ["type"] = "integer" },
                EdmPrimitiveTypeKind.Decimal or EdmPrimitiveTypeKind.Double or EdmPrimitiveTypeKind.Single => new() { ["type"] = "number" },
                EdmPrimitiveTypeKind.String or EdmPrimitiveTypeKind.Guid or EdmPrimitiveTypeKind.Date or
                    EdmPrimitiveTypeKind.DateTimeOffset or EdmPrimitiveTypeKind.TimeOfDay or EdmPrimitiveTypeKind.Duration or
                    EdmPrimitiveTypeKind.Binary => new() { ["type"] = "string" },
                _ => throw new NotSupportedException($"Unsupported MCP input type: {type.FullName()}")
            };
            if (kind is EdmPrimitiveTypeKind.Guid or EdmPrimitiveTypeKind.Date or EdmPrimitiveTypeKind.DateTimeOffset)
                schema["format"] = kind == EdmPrimitiveTypeKind.Guid ? "uuid" : kind == EdmPrimitiveTypeKind.Date ? "date" : "date-time";
            var range = IntegerRange(kind);
            if (range != null) { schema["minimum"] = range.Value.Min; schema["maximum"] = range.Value.Max; }
        }
        return type.IsNullable ? new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) } : schema;
    }

    private static EdmPrimitiveTypeKind Kind(IEdmTypeReference type) => type.Definition is IEdmTypeDefinition definition
        ? definition.UnderlyingType.PrimitiveKind : type.PrimitiveKind();

    private static (long Min, long Max)? IntegerRange(EdmPrimitiveTypeKind kind) => kind switch
    {
        EdmPrimitiveTypeKind.Byte => (byte.MinValue, byte.MaxValue),
        EdmPrimitiveTypeKind.SByte => (sbyte.MinValue, sbyte.MaxValue),
        EdmPrimitiveTypeKind.Int16 => (short.MinValue, short.MaxValue),
        EdmPrimitiveTypeKind.Int32 => (int.MinValue, int.MaxValue),
        EdmPrimitiveTypeKind.Int64 => (long.MinValue, long.MaxValue),
        _ => null
    };

    public static void Validate(IEdmTypeReference type, JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Null && type.IsNullable) return;
        if (type.IsCollection())
        {
            if (value.ValueKind != JsonValueKind.Array) throw Invalid(name, type);
            foreach (var item in value.EnumerateArray()) Validate(type.AsCollection().ElementType(), item, name);
            return;
        }
        if (type.Definition is IEdmStructuredType structured)
        {
            if (value.ValueKind != JsonValueKind.Object) throw Invalid(name, type);
            foreach (var property in structured.StructuralProperties())
            {
                if (value.TryGetProperty(property.Name, out var supplied)) Validate(property.Type, supplied, $"{name}.{property.Name}");
                else if (!property.Type.IsNullable) throw new ArgumentException($"Missing required property: {name}.{property.Name}");
            }
            if (!structured.IsOpen && value.EnumerateObject().Any(p => !structured.StructuralProperties().Any(s => s.Name == p.Name)))
                throw new ArgumentException($"Unknown property in {name}.");
            return;
        }
        if (type.Definition is IEdmEnumType enumeration)
        {
            if (value.ValueKind != JsonValueKind.String) throw Invalid(name, type);
            var members = value.GetString()!.Split(',', StringSplitOptions.TrimEntries);
            if ((!enumeration.IsFlags && members.Length != 1) || members.Any(m => !enumeration.Members.Any(e => e.Name == m)))
                throw Invalid(name, type);
            return;
        }
        var kind = Kind(type);
        var range = IntegerRange(kind);
        if (range != null)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) || number < range.Value.Min || number > range.Value.Max)
                throw Invalid(name, type);
            return;
        }
        if (kind == EdmPrimitiveTypeKind.Boolean)
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(name, type);
            return;
        }
        if (kind is EdmPrimitiveTypeKind.Decimal or EdmPrimitiveTypeKind.Single or EdmPrimitiveTypeKind.Double)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number) ||
                (kind == EdmPrimitiveTypeKind.Decimal && !value.TryGetDecimal(out _)) ||
                (kind == EdmPrimitiveTypeKind.Single && Math.Abs(number) > float.MaxValue)) throw Invalid(name, type);
            return;
        }
        if (value.ValueKind != JsonValueKind.String) throw Invalid(name, type);
        var text = value.GetString()!;
        var valid = kind switch
        {
            EdmPrimitiveTypeKind.String => true,
            EdmPrimitiveTypeKind.Guid => Guid.TryParse(text, out _),
            EdmPrimitiveTypeKind.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            EdmPrimitiveTypeKind.DateTimeOffset => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                System.Text.RegularExpressions.Regex.IsMatch(text, @"T.*(?:Z|[+-]\d{2}:\d{2})$"),
            EdmPrimitiveTypeKind.TimeOfDay => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            EdmPrimitiveTypeKind.Duration => ValidDuration(text),
            EdmPrimitiveTypeKind.Binary => Convert.TryFromBase64String(text, new byte[text.Length], out _),
            _ => throw new NotSupportedException($"Unsupported MCP input type: {type.FullName()}")
        };
        if (!valid) throw Invalid(name, type);
    }
    private static bool ValidDuration(string value)
    {
        try { _ = XmlConvert.ToTimeSpan(value); return true; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }
    private static ArgumentException Invalid(string name, IEdmTypeReference type) =>
        new($"Argument {name} must conform to {type.FullName()} (nullable: {type.IsNullable}).");

    public static string Literal(IEdmTypeReference type, JsonElement value)
    {
        Validate(type, value, "value");
        if (value.ValueKind == JsonValueKind.Null) return "null";
        if (type.IsCollection() || type.Definition is IEdmStructuredType) return value.GetRawText();
        if (type.Definition is IEdmEnumType enumeration) return $"{enumeration.FullName()}'{value.GetString()}'";
        return Kind(type) switch
        {
            EdmPrimitiveTypeKind.String => "'" + value.GetString()!.Replace("'", "''") + "'",
            EdmPrimitiveTypeKind.Binary => "binary'" + value.GetString() + "'",
            EdmPrimitiveTypeKind.Duration => "duration'" + value.GetString() + "'",
            _ => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
        };
    }

    public static JsonElement ParseKey(IEdmTypeReference type, string text)
    {
        var kind = Kind(type);
        if (IntegerRange(kind) != null || kind is EdmPrimitiveTypeKind.Boolean or EdmPrimitiveTypeKind.Decimal or
            EdmPrimitiveTypeKind.Double or EdmPrimitiveTypeKind.Single)
        {
            try
            {
                var value = JsonSerializer.Deserialize<JsonElement>(text);
                Validate(type, value, "key");
                return value;
            }
            catch (JsonException exception) { throw new ArgumentException("Invalid resource key.", exception); }
        }
        var result = JsonSerializer.SerializeToElement(text);
        Validate(type, result, "key");
        return result;
    }
}

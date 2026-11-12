using System.Globalization;
using System.Text.Json;
using System.Xml;

namespace GraphMcpServer.Csdl;

public static class EdmJsonSchema
{
    public static readonly IReadOnlyDictionary<string, string> QueryOptions = new Dictionary<string, string>
    {
        ["filter"] = "$filter", ["select"] = "$select", ["expand"] = "$expand", ["orderby"] = "$orderby",
        ["top"] = "$top", ["skip"] = "$skip", ["count"] = "$count", ["search"] = "$search"
    };

    public static JsonElement ForArguments(ToolBinding tool) =>
        JsonDocument.Parse(tool.InputSchemaJson).RootElement.Clone();

    public static void Validate(StaticTypeDescriptor type, JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Null && type.Nullable) return;
        if (type.Kind == StaticTypeKind.Array)
        {
            if (value.ValueKind != JsonValueKind.Array) throw Invalid(name, type);
            foreach (var item in value.EnumerateArray()) Validate(type.ElementType!, item, name);
            return;
        }
        if (type.Kind == StaticTypeKind.Object)
        {
            if (value.ValueKind != JsonValueKind.Object) throw Invalid(name, type);
            foreach (var property in type.Properties ?? [])
            {
                if (value.TryGetProperty(property.Name, out var supplied))
                    Validate(property.Type, supplied, $"{name}.{property.Name}");
                else if (!property.Type.Nullable)
                    throw new ArgumentException($"Missing required property: {name}.{property.Name}");
            }
            if (!type.IsOpen && value.EnumerateObject().Any(item =>
                    !(type.Properties ?? []).Any(property => property.Name == item.Name)))
                throw new ArgumentException($"Unknown property in {name}.");
            return;
        }
        if (type.Kind == StaticTypeKind.Enum)
        {
            if (value.ValueKind != JsonValueKind.String ||
                !(type.EnumValues ?? []).Contains(value.GetString(), StringComparer.Ordinal))
                throw Invalid(name, type);
            return;
        }
        if (type.Kind == StaticTypeKind.Integer)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number) ||
                type.Minimum.HasValue && number < type.Minimum ||
                type.Maximum.HasValue && number > type.Maximum)
                throw Invalid(name, type);
            return;
        }
        if (type.Kind == StaticTypeKind.Number)
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
                throw Invalid(name, type);
            return;
        }
        if (type.Kind == StaticTypeKind.Boolean)
        {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw Invalid(name, type);
            return;
        }
        if (value.ValueKind != JsonValueKind.String) throw Invalid(name, type);
        var text = value.GetString()!;
        var valid = type.Kind switch
        {
            StaticTypeKind.String => true,
            StaticTypeKind.Guid => Guid.TryParse(text, out _),
            StaticTypeKind.Date => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            StaticTypeKind.DateTimeOffset => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                System.Text.RegularExpressions.Regex.IsMatch(text, @"T.*(?:Z|[+-]\d{2}:\d{2})$"),
            StaticTypeKind.TimeOfDay => TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            StaticTypeKind.Duration => ValidDuration(text),
            StaticTypeKind.Binary => Convert.TryFromBase64String(text, new byte[text.Length], out _),
            _ => false
        };
        if (!valid) throw Invalid(name, type);
    }

    public static string Literal(StaticTypeDescriptor type, JsonElement value)
    {
        Validate(type, value, "value");
        if (value.ValueKind == JsonValueKind.Null) return "null";
        if (type.Kind is StaticTypeKind.Array or StaticTypeKind.Object) return value.GetRawText();
        if (type.Kind == StaticTypeKind.Enum) return $"{type.FullName}'{value.GetString()}'";
        return type.Kind switch
        {
            StaticTypeKind.String => "'" + value.GetString()!.Replace("'", "''") + "'",
            StaticTypeKind.Binary => "binary'" + value.GetString() + "'",
            StaticTypeKind.Duration => "duration'" + value.GetString() + "'",
            _ => value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()
        };
    }

    public static JsonElement ParseKey(StaticTypeDescriptor type, string text)
    {
        if (type.Kind is StaticTypeKind.Integer or StaticTypeKind.Number or StaticTypeKind.Boolean)
        {
            try
            {
                var value = JsonSerializer.Deserialize<JsonElement>(text);
                Validate(type, value, "key");
                return value;
            }
            catch (JsonException exception)
            {
                throw new ArgumentException("Invalid resource key.", exception);
            }
        }
        var result = JsonSerializer.SerializeToElement(text);
        Validate(type, result, "key");
        return result;
    }

    private static bool ValidDuration(string value)
    {
        try { _ = XmlConvert.ToTimeSpan(value); return true; }
        catch (FormatException) { return false; }
        catch (OverflowException) { return false; }
    }

    private static ArgumentException Invalid(string name, StaticTypeDescriptor type) =>
        new($"Argument {name} must conform to {type.FullName} (nullable: {type.Nullable}).");
}

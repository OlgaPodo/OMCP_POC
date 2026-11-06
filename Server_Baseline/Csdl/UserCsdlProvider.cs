using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public static class UserCsdlProvider
{
    private static readonly XNamespace Edm = "http://docs.oasis-open.org/odata/ns/edm";

    private sealed class CsdlState
    {
        public string FilePath { get; }
        public string Text { get; }
        public XDocument Document { get; }
        public IEdmModel EdmModel { get; }
        public JsonObject Model { get; }
        public JsonObject Container { get; }
        public JsonArray Prompts { get; }

        public CsdlState(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("CSDL file path must be specified.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"CSDL file not found at: {filePath}", filePath);

            FilePath = Path.GetFullPath(filePath);
            Text = File.ReadAllText(FilePath);
            Document = XDocument.Parse(Text);
            EdmModel = ParseEdmModel(Text);
            Model = CreateModel(Document);
            Container = Model["Microsoft.Graph"]!["GraphService"]!.AsObject();
            Prompts = Container["@Agent.Prompts"] as JsonArray ?? [];
        }
    }

    private static CsdlState? _state;
    private static readonly object SyncRoot = new();

    private static CsdlState State
    {
        get
        {
            if (_state != null) return _state;
            lock (SyncRoot)
            {
                _state ??= new CsdlState(ResolveCsdlPath());
                return _state;
            }
        }
    }

    public static void Initialize(IConfiguration configuration)
    {
        var path = ResolveCsdlPath(configuration);
        Initialize(path);
    }

    public static void Initialize(string filePath)
    {
        lock (SyncRoot)
        {
            _state = new CsdlState(filePath);
        }
    }

    public static void Initialize(string directory, string fileName)
    {
        Initialize(Path.Combine(directory, fileName));
    }

    public static string ResolveCsdlPath(IConfiguration? config = null)
    {
        config ??= GetDefaultConfiguration();

        // 1. Direct FilePath from configuration or environment
        var filePath = config["Csdl:FilePath"]
            ?? config["Csdl:Path"]
            ?? config["CsdlPath"]
            ?? Environment.GetEnvironmentVariable("CSDL_FILEPATH")
            ?? Environment.GetEnvironmentVariable("CSDL_PATH")
            ?? Environment.GetEnvironmentVariable("CsdlPath")
            ?? Environment.GetEnvironmentVariable("Csdl__FilePath");

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            return Path.GetFullPath(filePath);
        }

        // 2. Directory and FileName from configuration or environment
        var directory = config["Csdl:Directory"]
            ?? config["Csdl:FolderPath"]
            ?? config["Csdl:Dir"]
            ?? Environment.GetEnvironmentVariable("CSDL_DIRECTORY")
            ?? Environment.GetEnvironmentVariable("Csdl__Directory");

        var fileName = config["Csdl:FileName"]
            ?? config["Csdl:File"]
            ?? Environment.GetEnvironmentVariable("CSDL_FILENAME")
            ?? Environment.GetEnvironmentVariable("Csdl__FileName")
            ?? "Server_Baseline.csdl";

        if (!string.IsNullOrWhiteSpace(directory))
        {
            return Path.GetFullPath(Path.Combine(directory, fileName));
        }

        // 3. Fallback default locations
        var defaultDirs = new[]
        {
            @"C:\MCP\Csdl",
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Csdl")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Csdl"))
        };

        foreach (var dir in defaultDirs)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(@"C:\MCP\Csdl", fileName);
    }

    private static IConfiguration GetDefaultConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string CsdlText => State.Text;
    private static XDocument Document => State.Document;
    private static IEdmModel EdmModelInstance => State.EdmModel;
    private static JsonObject Model => State.Model;
    private static JsonObject Container => State.Container;
    private static JsonArray Prompts => State.Prompts;

    public static string CsdlFilePath => State.FilePath;
    public static string GetCsdlFilePath() => State.FilePath;

    public static IEdmModel EdmModel => EdmModelInstance;

    public static string GetCsdlXml() => CsdlText;
    public static string GetCsdl() => CsdlText;

    public static string GetCsdlJson() => Model.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static IEdmModel ParseEdmModel(string csdl)
    {
        using var reader = XmlReader.Create(new StringReader(csdl));
        if (!CsdlReader.TryParse(reader, out var model, out var errors))
        {
            var msg = string.Join("; ", errors?.Select(e => $"{e.ErrorCode}: {e.ErrorMessage}") ?? []);
            throw new InvalidOperationException($"Failed to parse CSDL into IEdmModel: {msg}");
        }
        return model;
    }

    private static JsonObject CreateModel(XDocument document)
    {
        var result = new JsonObject
        {
            ["$Version"] = "4.01",
            ["$EntityContainer"] = "Microsoft.Graph.GraphService"
        };
        foreach (var schema in document.Descendants(Edm + "Schema"))
        {
            var members = new JsonObject();
            foreach (var member in schema.Elements())
                members[RequiredAttribute(member, "Name")] = ConvertDefinition(member);
            result[RequiredAttribute(schema, "Namespace")] = members;
        }
        return result;
    }

    private static string RequiredAttribute(XElement element, string name) =>
        (string?)element.Attribute(name) ?? throw new InvalidOperationException($"Missing {name} on {element.Name}.");

    private static JsonObject ConvertDefinition(XElement element)
    {
        var kind = element.Name.LocalName;
        var result = new JsonObject();
        if (kind is not ("Property" or "EntitySet" or "Singleton"))
            result["$Kind"] = kind;
        var type = (string?)element.Attribute("Type") ?? (string?)element.Attribute("EntityType");
        if (type != null)
        {
            var collection = type.StartsWith("Collection(", StringComparison.Ordinal);
            result["$Type"] = collection ? type[11..^1] : type;
            if (collection || kind == "EntitySet") result["$Collection"] = true;
        }
        if (element.Attribute("Nullable") is { } nullable)
            result["$Nullable"] = bool.Parse(nullable.Value);
        if (element.Attribute("AppliesTo") is { } appliesTo)
            result["$AppliesTo"] = appliesTo.Value;
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Key":
                    result["$Key"] = new JsonArray(child.Elements().Select(p =>
                        (JsonNode?)JsonValue.Create(RequiredAttribute(p, "Name"))).ToArray());
                    break;
                case "Annotation":
                    result["@" + RequiredAttribute(child, "Term")] = ReadValue(child);
                    break;
                case "NavigationPropertyBinding":
                    var bindings = result["$NavigationPropertyBinding"] as JsonObject ?? new JsonObject();
                    if (result["$NavigationPropertyBinding"] == null) result["$NavigationPropertyBinding"] = bindings;
                    bindings[RequiredAttribute(child, "Path")] = RequiredAttribute(child, "Target");
                    break;
                default:
                    result[RequiredAttribute(child, "Name")] = ConvertDefinition(child);
                    break;
            }
        }
        return result;
    }

    private static JsonNode ReadValue(XElement element)
    {
        if (element.Attribute("String") is { } text) return JsonValue.Create(text.Value)!;
        if (element.Attribute("Bool") is { } boolean) return JsonValue.Create(bool.Parse(boolean.Value))!;
        var expression = element.Elements().Single();
        if (expression.Name.LocalName == "Collection")
            return new JsonArray(expression.Elements().Select(item => item.Name.LocalName == "String"
                ? (JsonNode?)JsonValue.Create(item.Value) : ReadRecord(item)).ToArray());
        return ReadRecord(expression);
    }

    public static IReadOnlyList<JsonObject> GetBindings(string term)
    {
        var result = new List<JsonObject>();
        void Add(JsonArray bindings, string target, string? entitySet)
        {
            foreach (var binding in bindings)
            {
                var definition = binding!.DeepClone().AsObject();
                definition["annotationTarget"] = target;
                definition["entitySet"] = entitySet;
                result.Add(definition);
            }
        }
        if (Container["@" + term] is JsonArray containerBindings)
            Add(containerBindings, "Microsoft.Graph.GraphService", null);
        foreach (var (name, resource) in Container)
        {
            if (resource is not JsonObject definition || definition["@" + term] is not JsonArray bindings) continue;
            var entitySet = definition["$Collection"]?.GetValue<bool>() == true ? name
                : Container.FirstOrDefault(entry => entry.Value is JsonObject candidate
                    && candidate["$Collection"]?.GetValue<bool>() == true
                    && candidate["$Type"]?.GetValue<string>() == definition["$Type"]?.GetValue<string>()).Key;
            Add(bindings, $"Microsoft.Graph.GraphService/{name}", entitySet);
        }
        return result;
    }

    public static string[] StringValues(JsonNode? node) =>
        node?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];

    public record CatalogItem(
        string PromptName,
        string Title,
        string Description,
        string? EntitySet,
        string Utterance,
        string ODataRequest,
        IReadOnlyList<string> ArgumentNames);

    public static IReadOnlyList<CatalogItem> GetCatalogItems()
    {
        var items = new List<CatalogItem>();
        var promptMap = Prompts.Select(p => p!.AsObject()).ToDictionary(p => p["Name"]!.GetValue<string>(), StringComparer.Ordinal);

        foreach (var (name, promptObj) in promptMap)
        {
            if (items.Any(i => i.PromptName == name)) continue;
            var title = promptObj["Title"]?.GetValue<string>() ?? name;
            var desc = promptObj["Description"]?.GetValue<string>() ?? "";
            var args = promptObj["Arguments"]?.AsArray().Select(a => a!["Name"]!.GetValue<string>()).ToList() ?? [];
            var request = "";
            var msg = promptObj["Messages"]?[0]?["Content"]?["Text"]?.GetValue<string>() ?? "";
            var m = Regex.Match(msg, @"GET\s+([A-Za-z0-9_\$'\(\)\{\}\/\?=&,-]+)");
            if (m.Success) request = m.Groups[1].Value;

            items.Add(new CatalogItem(
                PromptName: name,
                Title: title,
                Description: desc,
                EntitySet: null,
                Utterance: title,
                ODataRequest: request,
                ArgumentNames: args));
        }

        return items;
    }

    public static IReadOnlyList<JsonObject> GetProjectedTools()
    {
        var tools = new List<JsonObject>();
        if (Container["users"] is JsonObject)
        {
            tools.Add(new JsonObject
            {
                ["Name"] = "find_user",
                ["Title"] = "Find users",
                ["Description"] = "Search users by name, mail or userPrincipalName prefix.",
                ["Operation"] = "FindUsers",
                ["ODataQuery"] = "GET users?$filter=startswith(displayName,'{query}') or startswith(userPrincipalName,'{query}')",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/users",
                ["entitySet"] = "users",
                ["Arguments"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "query",
                    ["Description"] = "User name, mail or UPN prefix",
                    ["Required"] = true,
                    ["Default"] = ""
                })
            });
            tools.Add(new JsonObject
            {
                ["Name"] = "user_profile",
                ["Title"] = "User organization profile",
                ["Description"] = "Read a user profile with manager, peers and direct reports.",
                ["Operation"] = "GetUserProfile",
                ["ODataQuery"] = "GET users('{userId}')?$expand=manager,directReports",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/users",
                ["entitySet"] = "users",
                ["Arguments"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "userId",
                    ["Description"] = "User ID, UPN, display name, or me",
                    ["Required"] = false,
                    ["Default"] = "me"
                })
            });
        }
        if (Container["groups"] is JsonObject)
        {
            tools.Add(new JsonObject
            {
                ["Name"] = "find_group",
                ["Title"] = "Find groups",
                ["Description"] = "Search Microsoft 365 and security groups by name or mail prefix.",
                ["Operation"] = "FindGroups",
                ["ODataQuery"] = "GET groups?$filter=startswith(displayName,'{query}') or startswith(mailNickname,'{query}')",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/groups",
                ["entitySet"] = "groups",
                ["Arguments"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "query",
                    ["Description"] = "Group display name or prefix",
                    ["Required"] = true,
                    ["Default"] = ""
                })
            });
            tools.Add(new JsonObject
            {
                ["Name"] = "group_details",
                ["Title"] = "Group details",
                ["Description"] = "Read a group with members and owners; member profiles do not include their reporting relationships.",
                ["Operation"] = "GetGroupDetails",
                ["ODataQuery"] = "GET groups('{groupId}')?$expand=members,owners",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/groups",
                ["entitySet"] = "groups",
                ["Arguments"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "groupId",
                    ["Description"] = "Group ID, display name or mail nickname",
                    ["Required"] = true,
                    ["Default"] = ""
                })
            });
        }
        tools.Add(new JsonObject
        {
            ["Name"] = "read_resource",
            ["Title"] = "Read a projected resource",
            ["Description"] = "Read one CSDL-projected user or group resource by URI. This tool is an adapter to the same dispatcher as MCP resources/read; it returns uri and data.",
            ["Operation"] = "ReadResource",
            ["ODataQuery"] = "GET me | GET users('{userId}') | GET groups | GET groups('{groupId}')",
            ["annotationTarget"] = "Microsoft.Graph.GraphService",
            ["entitySet"] = null,
            ["Arguments"] = new JsonArray(new JsonObject
            {
                ["Name"] = "uri",
                ["Description"] = "Projected resource URI, such as users://me or groups://{groupId}",
                ["Required"] = true,
                ["Default"] = ""
            })
        });
        return tools;
    }

    public static IReadOnlyList<JsonObject> GetProjectedResources()
    {
        var resources = new List<JsonObject>();
        if (Container["users"] is JsonObject)
        {
            resources.Add(new JsonObject
            {
                ["Name"] = "user",
                ["Title"] = "User profile resource",
                ["Description"] = "User organization profile by user ID or UPN.",
                ["UriTemplate"] = "users://{userId}",
                ["MimeType"] = "application/json",
                ["Operation"] = "GetUserProfile",
                ["ODataQuery"] = "GET users('{userId}')?$expand=manager,directReports",
                ["Parameter"] = "userId",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/users",
                ["entitySet"] = "users"
            });
        }
        if (Container["me"] is JsonObject)
        {
            resources.Add(new JsonObject
            {
                ["Name"] = "me",
                ["Title"] = "Current user",
                ["Description"] = "The currently authenticated user.",
                ["UriTemplate"] = "users://me",
                ["MimeType"] = "application/json",
                ["Operation"] = "GetMe",
                ["ODataQuery"] = "GET me?$expand=manager,directReports",
                ["Parameter"] = "",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/me",
                ["entitySet"] = "users"
            });
        }
        if (Container["groups"] is JsonObject)
        {
            resources.Add(new JsonObject
            {
                ["Name"] = "groups",
                ["Title"] = "Group catalog",
                ["Description"] = "Microsoft 365 and security group catalog.",
                ["UriTemplate"] = "groups://all",
                ["MimeType"] = "application/json",
                ["Operation"] = "GetAllGroups",
                ["ODataQuery"] = "GET groups?$top=100",
                ["Parameter"] = "",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/groups",
                ["entitySet"] = "groups"
            });
            resources.Add(new JsonObject
            {
                ["Name"] = "group",
                ["Title"] = "Group members and owners",
                ["Description"] = "Group profile, members and owners by group ID.",
                ["UriTemplate"] = "groups://{groupId}",
                ["MimeType"] = "application/json",
                ["Operation"] = "GetGroupDetails",
                ["ODataQuery"] = "GET groups('{groupId}')?$expand=members,owners",
                ["Parameter"] = "groupId",
                ["annotationTarget"] = "Microsoft.Graph.GraphService/groups",
                ["entitySet"] = "groups"
            });
        }
        return resources;
    }

    public static IReadOnlyList<string> ExtractODataQueries(string messageText)
    {
        var matches = Regex.Matches(
            messageText,
            @"GET\s+((?:users|groups|me)(?:\([^)]*\))?(?:/[A-Za-z0-9_]+)*(?:\?[^\s.]+(?:\s+(?:or|and|eq)\s+[^\s.]+)*)?)",
            RegexOptions.CultureInvariant);
        return matches.Select(m => m.Groups[1].Value.TrimEnd('.', ',', ';')).Distinct(StringComparer.Ordinal).ToList();
    }

    private static (string[] EntitySets, string[] ToolBindings, string[] ResourceBindings, IReadOnlyList<string> Queries) InferPromptBindings(JsonObject prompt)
    {
        var text = prompt["Messages"]![0]!["Content"]!["Text"]!.GetValue<string>();
        var queries = ExtractODataQueries(text);
        var entitySets = new List<string>();
        bool touchesUsers = queries.Any(q => q.StartsWith("users", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me", StringComparison.OrdinalIgnoreCase));
        bool touchesGroups = queries.Any(q => q.StartsWith("groups", StringComparison.OrdinalIgnoreCase));
        if (touchesUsers) entitySets.Add("users");
        if (touchesGroups) entitySets.Add("groups");

        var tools = new List<string>();
        var resources = new List<string>();
        if (touchesUsers && touchesGroups)
        {
            tools.AddRange(["user_profile", "find_group", "read_resource"]);
            resources.AddRange(["users://{userId}", "groups://{groupId}"]);
        }
        else if (touchesUsers)
        {
            bool onlyDeptFilter = queries.All(q => q.Contains("department", StringComparison.OrdinalIgnoreCase));
            tools.Add(onlyDeptFilter ? "find_user" : "user_profile");
            resources.Add("users://{userId}");
            if (queries.Any(q => q.StartsWith("me", StringComparison.OrdinalIgnoreCase)))
                resources.Add("users://me");
        }
        else if (touchesGroups)
        {
            bool hasGroupLookup = queries.Any(q => q.StartsWith("groups(", StringComparison.OrdinalIgnoreCase) || q.StartsWith("groups?$top", StringComparison.OrdinalIgnoreCase));
            tools.Add(hasGroupLookup ? "group_details" : "find_group");
            resources.Add("groups://{groupId}");
            if (queries.Any(q => q.StartsWith("groups?$top", StringComparison.OrdinalIgnoreCase)))
                resources.Add("groups://all");
        }
        return (entitySets.ToArray(), tools.ToArray(), resources.ToArray(), queries);
    }

    public static void ValidateDependencies()
    {
        // 1. OData EDM Standard Model Validation
        if (!EdmModelInstance.Validate(out var edmErrors))
        {
            var errorList = edmErrors.ToList();
            if (errorList.Count > 0)
            {
                var messages = string.Join(Environment.NewLine, errorList.Select(e => $"[{e.ErrorCode}] {e.ErrorMessage}"));
                throw new InvalidOperationException($"OData EDM model validation failed:{Environment.NewLine}{messages}");
            }
        }

        // 2. Ensure CSDL has no Agent.Tools, Agent.Resources, or Agent.ExampleQueries annotations
        if (GetBindings("Agent.Tools").Count > 0 || GetBindings("Agent.Resources").Count > 0 || GetBindings("Agent.ExampleQueries").Count > 0)
            throw new InvalidOperationException("CSDL must not contain Agent.Tools, Agent.Resources, or Agent.ExampleQueries annotations.");
        if (Model["Agent"] is JsonObject agentSchema &&
            (agentSchema.ContainsKey("ToolDefinition") || agentSchema.ContainsKey("ResourceDefinition") ||
             agentSchema.ContainsKey("Tools") || agentSchema.ContainsKey("Resources") ||
             agentSchema.ContainsKey("ExampleQuery") || agentSchema.ContainsKey("ExampleQueries")))
            throw new InvalidOperationException("CSDL Agent schema must only define Prompts vocabulary elements.");

        // 3. Vocabulary & Dependency Validation (Prompts and Examples)
        var names = Prompts.Select(p => p!["Name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (names.Count != Prompts.Count)
            throw new InvalidOperationException("CSDL prompt names must be unique.");

        foreach (var prompt in Prompts)
        {
            var obj = prompt!.AsObject();
            if (obj.Any(property => property.Key.Equals("examples", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Examples must be entity-set annotations, not prompt properties.");
            if (obj.ContainsKey("Tools") || obj.ContainsKey("Resources"))
                throw new InvalidOperationException($"Prompt {obj["Name"]} must use OData queries in Messages instead of Tools/Resources properties.");
            var text = obj["Messages"]![0]!["Content"]!["Text"]!.GetValue<string>();
            if (text.Contains("{tool:", StringComparison.OrdinalIgnoreCase) || text.Contains("{resource:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Prompt {obj["Name"]} must use OData queries instead of tool or resource references.");
            if (ExtractODataQueries(text).Count == 0)
                throw new InvalidOperationException($"Prompt {obj["Name"]} must include at least one OData query.");
        }

        foreach (var (_, resource) in Container)
        {
            if (resource is not JsonObject definition || definition["@Agent.ExampleQueries"] is not JsonArray examples) continue;
            if (definition["$Collection"]?.GetValue<bool>() != true)
                throw new InvalidOperationException("Example queries must annotate entity sets.");
            if (examples.Any(example => example is JsonObject exObj && exObj.ContainsKey("Prompt")))
                throw new InvalidOperationException("Example queries must not reference prompts.");
        }
    }

    private static JsonObject ReadRecord(XElement record)
    {
        var result = new JsonObject();
        foreach (var property in record.Elements(Edm + "PropertyValue"))
            result[RequiredAttribute(property, "Property")] = ReadValue(property);
        return result;
    }

    public static ListPromptsResult ListPrompts() => new()
    {
        Prompts = Prompts.Select(p => new Prompt
        {
            Name = p!["Name"]!.GetValue<string>(),
            Title = p["Title"]!.GetValue<string>(),
            Description = p["Description"]!.GetValue<string>(),
            Arguments = p["Arguments"]!.AsArray().Select(a => new PromptArgument
            {
                Name = a!["Name"]!.GetValue<string>(),
                Description = a["Description"]?.GetValue<string>(),
                Required = a["Required"]!.GetValue<bool>()
            }).ToList()
        }).ToList()
    };

    public static GetPromptResult GetPrompt(GetPromptRequestParams request)
    {
        var prompt = Prompts.SingleOrDefault(p => p!["Name"]!.GetValue<string>() == request.Name)
            ?? throw new ArgumentException($"Unknown prompt: {request.Name}");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var argument in prompt["Arguments"]!.AsArray())
        {
            var name = argument!["Name"]!.GetValue<string>();
            string? value = argument["Default"]?.GetValue<string>();
            if (request.Arguments != null && request.Arguments.TryGetValue(name, out var supplied))
            {
                if (supplied.ValueKind != JsonValueKind.String)
                    throw new ArgumentException($"Prompt argument {name} must be a string.");
                value = supplied.GetString();
            }
            if (value == null && argument["Required"]!.GetValue<bool>())
                throw new ArgumentException($"Missing required prompt argument: {name}");
            values[name] = value ?? "";
        }
        if (request.Arguments != null && request.Arguments.Keys.Any(k => !values.ContainsKey(k)))
            throw new ArgumentException("The prompt contains an unknown argument.");
        return new GetPromptResult
        {
            Description = prompt["Description"]!.GetValue<string>(),
            Messages = prompt["Messages"]!.AsArray().Select(message => new PromptMessage
            {
                Role = message!["Role"]!.GetValue<string>() == "user" ? Role.User : Role.Assistant,
                Content = new TextContentBlock
                {
                    Text = Regex.Replace(message["Content"]!["Text"]!.GetValue<string>(), @"\{([A-Za-z0-9_]+)\}",
                        match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value)
                }
            }).ToList()
        };
    }

    public static JsonArray GetExampleQueries()
    {
        var examples = new JsonArray();
        foreach (var (name, resource) in Container)
        {
            if (resource is not JsonObject entitySet || entitySet["$Collection"]?.GetValue<bool>() != true) continue;
            if (entitySet["@Agent.ExampleQueries"] is not JsonArray queries) continue;
            foreach (var query in queries)
                examples.Add(new JsonObject
                {
                    ["utterance"] = query!["Utterance"]!.GetValue<string>(),
                    ["request"] = query["Request"]!.GetValue<string>(),
                    ["entitySet"] = name
                });
        }
        return examples;
    }

    public static JsonArray GetCatalog() => new(Prompts.Select(p =>
    {
        var promptObj = p!.AsObject();
        var (entitySets, toolBindings, resourceBindings, queries) = InferPromptBindings(promptObj);
        return (JsonNode?)new JsonObject
        {
            ["name"] = promptObj["Name"]!.DeepClone(),
            ["title"] = promptObj["Title"]!.DeepClone(),
            ["description"] = promptObj["Description"]!.DeepClone(),
            ["odataQueries"] = new JsonArray(queries.Select(q => (JsonNode?)JsonValue.Create(q)).ToArray()),
            ["toolBinding"] = string.Join(", ", toolBindings),
            ["toolBindings"] = new JsonArray(toolBindings.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["resourceBindings"] = new JsonArray(resourceBindings.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray()),
            ["entitySets"] = new JsonArray(entitySets.Select(name => (JsonNode?)JsonValue.Create(name)).ToArray()),
            ["template"] = promptObj["Messages"]![0]!["Content"]!["Text"]!.DeepClone(),
            ["arguments"] = new JsonArray(promptObj["Arguments"]!.AsArray().Select(a => (JsonNode?)new JsonObject
            {
                ["name"] = a!["Name"]!.DeepClone(),
                ["description"] = a["Description"]?.DeepClone(),
                ["required"] = a["Required"]!.DeepClone(),
                ["default"] = a["Default"]?.DeepClone()
            }).ToArray())
        };
    }).ToArray());

    public static object GetProjection() => new
    {
        tools = GetProjectedTools(),
        resources = GetProjectedResources(),
        prompts = GetCatalog(),
        exampleQueries = GetExampleQueries(),
        mcpTools = CsdlMcpProjection.ListTools().Tools,
        mcpResources = CsdlMcpProjection.ListResources().Resources,
        mcpResourceTemplates = CsdlMcpProjection.ListResourceTemplates().ResourceTemplates
    };

    public static object GetPromptArchitecturalAnalysis() => new
    {
        modelParser = "Validated using Microsoft.OData.Edm (IEdmModel) with CsdlReader.",
        promptProjection = "Container Agent.Prompts use standard OData queries in Messages and are projected to MCP names, arguments and messages without AI rewriting.",
        examples = "Entity and operation examples are independent metadata and do not reference prompts.",
        promptResponses = "Prompts contain rendered messages with OData query steps, never examples. Entity-set query annotations are independent metadata.",
        vocabulary = "CSDL contains no Agent.Tools or Agent.Resources annotations; tools and resources are projected deterministically from standard OData EntitySets and Singletons.",
        toolExecution = "Standard OData EntitySets and Singletons project to MCP discovery and dispatch through declared, supported C# operations.",
        composition = "Container prompts express multi-step OData queries across users and groups; joins are client-orchestrated, not bound to one entity set."
    };
}

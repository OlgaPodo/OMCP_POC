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
        public JsonObject GraphSchema { get; }
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
            GraphSchema = Model["Microsoft.Graph"]!.AsObject();
            Container = GraphSchema["GraphService"]!.AsObject();
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
        Initialize(ResolveCsdlPath(configuration));
    }

    public static void Initialize(string filePath)
    {
        lock (SyncRoot)
        {
            _state = new CsdlState(filePath);
        }
    }

    public static string ResolveCsdlPath(IConfiguration? config = null)
    {
        config ??= new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var filePath = config["Csdl:FilePath"]
            ?? config["Csdl:Path"]
            ?? Environment.GetEnvironmentVariable("CSDL_FILEPATH")
            ?? Environment.GetEnvironmentVariable("CSDL_PATH");

        if (!string.IsNullOrWhiteSpace(filePath))
            return Path.GetFullPath(filePath);

        var directory = config["Csdl:Directory"]
            ?? Environment.GetEnvironmentVariable("CSDL_DIRECTORY");

        var fileName = config["Csdl:FileName"]
            ?? Environment.GetEnvironmentVariable("CSDL_FILENAME")
            ?? "Server_WithPrompts.csdl";

        if (!string.IsNullOrWhiteSpace(directory))
            return Path.GetFullPath(Path.Combine(directory, fileName));

        var defaultDirs = new[]
        {
            @"C:\MCP\Csdl",
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Csdl")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Csdl"))
        };

        foreach (var dir in defaultDirs)
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate)) return candidate;
        }

        return Path.Combine(@"C:\MCP\Csdl", fileName);
    }

    private static string CsdlText => State.Text;
    private static XDocument Document => State.Document;
    private static IEdmModel EdmModelInstance => State.EdmModel;
    private static JsonObject Model => State.Model;
    private static JsonObject GraphSchema => State.GraphSchema;
    private static JsonObject Container => State.Container;
    private static JsonArray Prompts => State.Prompts;

    public static string CsdlFilePath => State.FilePath;
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
        if (element.Attribute("IsBound") is { } isBound)
            result["$IsBound"] = bool.Parse(isBound.Value);
        if (element.Attribute("Nullable") is { } nullable)
            result["$Nullable"] = bool.Parse(nullable.Value);
        if (element.Attribute("AppliesTo") is { } appliesTo)
            result["$AppliesTo"] = appliesTo.Value;

        var parameters = new JsonArray();
        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Key":
                    result["$Key"] = new JsonArray(child.Elements().Select(p =>
                        (JsonNode?)JsonValue.Create(RequiredAttribute(p, "Name"))).ToArray());
                    break;
                case "Parameter":
                    parameters.Add(new JsonObject
                    {
                        ["Name"] = RequiredAttribute(child, "Name"),
                        ["Type"] = RequiredAttribute(child, "Type"),
                        ["Nullable"] = child.Attribute("Nullable") is { } n ? bool.Parse(n.Value) : true
                    });
                    break;
                case "ReturnType":
                    result["$ReturnType"] = RequiredAttribute(child, "Type");
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
        if (parameters.Count > 0) result["$Parameters"] = parameters;
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

    private static JsonObject ReadRecord(XElement record)
    {
        var result = new JsonObject();
        foreach (var property in record.Elements(Edm + "PropertyValue"))
            result[RequiredAttribute(property, "Property")] = ReadValue(property);
        return result;
    }

    public static IReadOnlyList<JsonObject> GetProjectedTools()
    {
        var tools = new List<JsonObject>();

        if (Container["users"] is JsonObject)
        {
            tools.Add(CreateTool("find_user", "Find users", "Search users by name, mail or userPrincipalName prefix.", "FindUsers",
                "GET users?$filter=startswith(displayName,'{query}') or startswith(userPrincipalName,'{query}')",
                "Microsoft.Graph.GraphService/users", "users",
                ("query", "User name, mail or UPN prefix", true, "")));
            tools.Add(CreateTool("user_profile", "User organization profile", "Read a user profile with manager, peers and direct reports.", "GetUserProfile",
                "GET users('{userId}')?$expand=manager,directReports",
                "Microsoft.Graph.GraphService/users", "users",
                ("userId", "User ID, UPN, display name, or me", false, "me")));
        }

        if (Container["groups"] is JsonObject)
        {
            tools.Add(CreateTool("find_group", "Find groups", "Search Microsoft 365 and security groups by name or mail prefix.", "FindGroups",
                "GET groups?$filter=startswith(displayName,'{query}') or startswith(mailNickname,'{query}')",
                "Microsoft.Graph.GraphService/groups", "groups",
                ("query", "Group display name or prefix", true, "")));
            tools.Add(CreateTool("group_details", "Group details", "Read a group with members and owners; member profiles do not include their reporting relationships.", "GetGroupDetails",
                "GET groups('{groupId}')?$expand=members,owners",
                "Microsoft.Graph.GraphService/groups", "groups",
                ("groupId", "Group ID, display name or mail nickname", true, "")));
        }

        if (Container["drives"] is JsonObject && Container["driveItems"] is JsonObject)
        {
            if (GraphSchema["recentDriveItems"] is JsonObject)
            {
                tools.Add(CreateTool("list_my_files", "List OneDrive files (recentDriveItems)", "List files and folders in OneDrive root or recent items.", "ListMyFiles",
                    "GET me/drive/root/children?$select=id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName",
                    "Microsoft.Graph.recentDriveItems", "driveItems",
                    ("userId", "User ID, UPN, or 'me'", false, "me")));
            }
            if (GraphSchema["searchDriveItems"] is JsonObject)
            {
                tools.Add(CreateTool("search_files", "Search OneDrive & SharePoint files (searchDriveItems)", "Invoke bound OData function Microsoft.Graph.searchDriveItems(q='{query}') across OneDrive.", "SearchFiles",
                    "GET drives('drive-olga')/Microsoft.Graph.searchDriveItems(q='{query}')",
                    "Microsoft.Graph.searchDriveItems", "driveItems",
                    ("query", "File name or keyword (e.g. 'CSDL', 'MCP', 'Proposal')", true, "CSDL")));
            }
            tools.Add(CreateTool("file_last_modified_by", "Last user to modify file (lastModifiedByUser)", "Navigate from a DriveItem to its lastModifiedByUser profile.", "GetFileModifier",
                "GET driveItems('{fileName}')/lastModifiedByUser?$select=id,displayName,userPrincipalName,jobTitle,department",
                "Microsoft.Graph.GraphService/driveItems", "driveItems",
                ("fileName", "File ID or name (e.g. 'Script_MCP_POC.docx')", false, "Script_MCP_POC.docx")));
        }

        if (Container["teams"] is JsonObject && Container["channels"] is JsonObject)
        {
            tools.Add(CreateTool("list_teams", "List joined Microsoft Teams & channels", "List Microsoft Teams workspaces and expand their channels.", "ListTeams",
                "GET me/joinedTeams?$select=id,displayName,description,visibility,webUrl&$expand=channels",
                "Microsoft.Graph.GraphService/teams", "teams",
                ("filter", "Optional Team name filter or 'all'", false, "all")));
            if (GraphSchema["deltaChannelMessages"] is JsonObject)
            {
                tools.Add(CreateTool("channel_messages", "Read channel messages (deltaChannelMessages)", "Invoke bound OData function Microsoft.Graph.deltaChannelMessages() on a Teams channel.", "GetChannelMessages",
                    "GET channels('{channelId}')/Microsoft.Graph.deltaChannelMessages()?$top=20",
                    "Microsoft.Graph.deltaChannelMessages", "channels",
                    ("channelId", "Channel ID or display name (e.g. 'CSDL & TypeSpec', 'MCP 2.0 Integration')", false, "CSDL & TypeSpec")));
            }
            if (GraphSchema["sendChannelMessage"] is JsonObject)
            {
                tools.Add(CreateTool("send_channel_message", "Post Teams channel message (sendChannelMessage)", "Invoke bound OData action Microsoft.Graph.sendChannelMessage on a Teams channel.", "SendChannelMessage",
                    "POST channels('{channelId}')/Microsoft.Graph.sendChannelMessage",
                    "Microsoft.Graph.sendChannelMessage", "channels",
                    ("channelId", "Target channel ID or name", false, "MCP 2.0 Integration"),
                    ("subject", "Message subject header", false, "MCP 2.0 LargeServer Update"),
                    ("bodyContent", "Message text body", true, "Verified all 10 CSDL entities and 5 bound operations on port 3003.")));
            }
        }

        if (Container["chats"] is JsonObject)
        {
            tools.Add(CreateTool("list_chats", "List 1:1 and group chats", "Read recent Microsoft Teams 1:1 and group chats with members and messages.", "ListChats",
                "GET me/chats?$select=id,topic,chatType,lastUpdatedDateTime,webUrl&$expand=members,messages($top=5)",
                "Microsoft.Graph.GraphService/chats", "chats",
                ("filter", "Chat type ('oneOnOne', 'group', 'all') or participant name", false, "all")));
        }

        if (Container["todoLists"] is JsonObject && Container["todoTasks"] is JsonObject)
        {
            tools.Add(CreateTool("list_todo_tasks", "List Microsoft To Do task lists & tasks", "List Microsoft To Do task lists and filter tasks by status, importance, or keyword.", "ListTodoTasks",
                "GET me/todoLists?$select=id,displayName,wellknownListName&$expand=tasks($filter=status ne 'completed')",
                "Microsoft.Graph.GraphService/todoTasks", "todoTasks",
                ("filter", "Optional filter ('high', 'inProgress', 'notStarted', 'all', or keyword)", false, "all")));
            if (GraphSchema["completeTodoTask"] is JsonObject)
            {
                tools.Add(CreateTool("complete_todo_task", "Complete a To Do task (completeTodoTask)", "Invoke bound OData action Microsoft.Graph.completeTodoTask on a TodoTask.", "CompleteTodoTask",
                    "POST todoTasks('{taskId}')/Microsoft.Graph.completeTodoTask",
                    "Microsoft.Graph.completeTodoTask", "todoTasks",
                    ("taskId", "Task ID or title substring (e.g. 'task-101')", false, "task-101")));
            }
        }

        bool isMultiWorkload = Container["drives"] is JsonObject || Container["teams"] is JsonObject || Container["chats"] is JsonObject || Container["todoTasks"] is JsonObject;
        tools.Add(CreateTool("read_resource",
            isMultiWorkload ? "Read any CSDL-projected M365 resource" : "Read a projected resource",
            isMultiWorkload
                ? "Read any CSDL-projected user, group, drive, file, team, channel, chat, or To Do resource by URI."
                : "Read one CSDL-projected user or group resource by URI. This tool is an adapter to the same dispatcher as MCP resources/read; it returns uri and data.",
            "ReadResource",
            isMultiWorkload
                ? "GET me | GET users('{id}') | GET groups('{id}') | GET me/drive/root/children | GET teams | GET chats | GET me/todoLists"
                : "GET me | GET users('{userId}') | GET groups | GET groups('{groupId}')",
            "Microsoft.Graph.GraphService", null,
            ("uri",
                isMultiWorkload
                    ? "Projected resource URI (e.g. users://me, files://recent, teams://all, chats://all, todo://lists)"
                    : "Projected resource URI, such as users://me or groups://{groupId}",
                true,
                isMultiWorkload ? "files://recent" : "")));

        return tools;
    }

    private static JsonObject CreateTool(string name, string title, string description, string operation, string odataQuery, string target, string? entitySet, params (string Name, string Description, bool Required, string Default)[] args)
    {
        return new JsonObject
        {
            ["Name"] = name,
            ["Title"] = title,
            ["Description"] = description,
            ["Operation"] = operation,
            ["ODataQuery"] = odataQuery,
            ["annotationTarget"] = target,
            ["entitySet"] = entitySet,
            ["Arguments"] = new JsonArray(args.Select(a => (JsonNode?)new JsonObject
            {
                ["Name"] = a.Name,
                ["Description"] = a.Description,
                ["Required"] = a.Required,
                ["Default"] = a.Default
            }).ToArray())
        };
    }

    public static IReadOnlyList<JsonObject> GetProjectedResources()
    {
        var resources = new List<JsonObject>();
        bool isMultiWorkload = Container["drives"] is JsonObject || Container["teams"] is JsonObject || Container["chats"] is JsonObject || Container["todoTasks"] is JsonObject;

        if (isMultiWorkload)
        {
            if (Container["me"] is JsonObject)
                resources.Add(CreateResource("me", "Current user profile", "The signed-in user profile and reporting hierarchy.", "users://me", "GetMe", "GET me?$expand=manager,directReports", "", "Microsoft.Graph.GraphService/me", "users"));
            if (Container["users"] is JsonObject)
                resources.Add(CreateResource("user", "User profile by ID/UPN", "User organization profile by user ID or UPN.", "users://{userId}", "GetUserProfile", "GET users('{userId}')?$expand=manager,directReports", "userId", "Microsoft.Graph.GraphService/users", "users"));
            if (Container["groups"] is JsonObject)
            {
                resources.Add(CreateResource("groups", "Organization groups catalog", "Microsoft 365 and security group catalog.", "groups://all", "GetAllGroups", "GET groups?$top=50", "", "Microsoft.Graph.GraphService/groups", "groups"));
                resources.Add(CreateResource("group", "Group details, members & owners", "Group profile, members and owners by group ID.", "groups://{groupId}", "GetGroupDetails", "GET groups('{groupId}')?$expand=members,owners", "groupId", "Microsoft.Graph.GraphService/groups", "groups"));
            }
            if (Container["drives"] is JsonObject)
                resources.Add(CreateResource("my_drive", "My OneDrive & root children", "Signed-in user's OneDrive container and root files.", "drives://me", "ListMyFiles", "GET me/drive/root/children", "", "Microsoft.Graph.GraphService/drives", "drives"));
            if (Container["driveItems"] is JsonObject)
            {
                resources.Add(CreateResource("recent_files", "Recent OneDrive files", "Recently modified files in OneDrive.", "files://recent", "SearchFiles", "GET drives('drive-olga')/Microsoft.Graph.recentDriveItems()", "", "Microsoft.Graph.GraphService/driveItems", "driveItems"));
                resources.Add(CreateResource("file_item", "DriveItem & lastModifiedByUser", "Single DriveItem metadata and lastModifiedByUser relationship.", "files://{itemId}", "GetFileModifier", "GET driveItems('{itemId}')?$expand=lastModifiedByUser", "itemId", "Microsoft.Graph.GraphService/driveItems", "driveItems"));
            }
            if (Container["teams"] is JsonObject)
            {
                resources.Add(CreateResource("teams", "Joined Microsoft Teams & channels", "All joined Microsoft Teams workspaces with channels expanded.", "teams://all", "ListTeams", "GET me/joinedTeams?$expand=channels", "", "Microsoft.Graph.GraphService/teams", "teams"));
                resources.Add(CreateResource("team_by_id", "Team details & channels", "Single Microsoft Team workspace and its channels.", "teams://{teamId}", "ListTeams", "GET teams('{teamId}')?$expand=channels", "teamId", "Microsoft.Graph.GraphService/teams", "teams"));
            }
            if (Container["channels"] is JsonObject)
                resources.Add(CreateResource("channel_messages", "Teams channel messages", "Messages in a specific Microsoft Teams channel.", "channels://{channelId}/messages", "GetChannelMessages", "GET channels('{channelId}')/messages?$top=20", "channelId", "Microsoft.Graph.GraphService/channels", "channels"));
            if (Container["chats"] is JsonObject)
            {
                resources.Add(CreateResource("chats", "Recent 1:1 & group chats", "All recent 1:1 and group chats with members and messages.", "chats://all", "ListChats", "GET me/chats?$expand=members,messages($top=5)", "", "Microsoft.Graph.GraphService/chats", "chats"));
                resources.Add(CreateResource("chat_by_id", "Chat thread by ID", "Single 1:1 or group chat thread with messages.", "chats://{chatId}", "ListChats", "GET chats('{chatId}')?$expand=members,messages", "chatId", "Microsoft.Graph.GraphService/chats", "chats"));
            }
            if (Container["todoLists"] is JsonObject)
                resources.Add(CreateResource("todo_lists", "Microsoft To Do lists & tasks", "All Microsoft To Do task lists and their tasks.", "todo://lists", "ListTodoTasks", "GET me/todoLists?$expand=tasks", "", "Microsoft.Graph.GraphService/todoLists", "todoLists"));
            if (Container["todoTasks"] is JsonObject)
                resources.Add(CreateResource("todo_tasks", "Pending To Do tasks", "Open and high-importance tasks across Microsoft To Do.", "todo://tasks", "ListTodoTasks", "GET todoTasks?$filter=status ne 'completed'", "", "Microsoft.Graph.GraphService/todoTasks", "todoTasks"));
        }
        else
        {
            if (Container["users"] is JsonObject)
                resources.Add(CreateResource("user", "User profile resource", "User organization profile by user ID or UPN.", "users://{userId}", "GetUserProfile", "GET users('{userId}')?$expand=manager,directReports", "userId", "Microsoft.Graph.GraphService/users", "users"));
            if (Container["me"] is JsonObject)
                resources.Add(CreateResource("me", "Current user", "The currently authenticated user.", "users://me", "GetMe", "GET me?$expand=manager,directReports", "", "Microsoft.Graph.GraphService/me", "users"));
            if (Container["groups"] is JsonObject)
            {
                resources.Add(CreateResource("groups", "Group catalog", "Microsoft 365 and security group catalog.", "groups://all", "GetAllGroups", "GET groups?$top=100", "", "Microsoft.Graph.GraphService/groups", "groups"));
                resources.Add(CreateResource("group", "Group members and owners", "Group profile, members and owners by group ID.", "groups://{groupId}", "GetGroupDetails", "GET groups('{groupId}')?$expand=members,owners", "groupId", "Microsoft.Graph.GraphService/groups", "groups"));
            }
        }
        return resources;
    }

    private static JsonObject CreateResource(string name, string title, string description, string uriTemplate, string operation, string odataQuery, string parameter, string target, string entitySet)
    {
        return new JsonObject
        {
            ["Name"] = name,
            ["Title"] = title,
            ["Description"] = description,
            ["UriTemplate"] = uriTemplate,
            ["MimeType"] = "application/json",
            ["Operation"] = operation,
            ["ODataQuery"] = odataQuery,
            ["Parameter"] = parameter,
            ["annotationTarget"] = target,
            ["entitySet"] = entitySet
        };
    }

    public static JsonArray GetExampleQueries()
    {
        var examples = new JsonArray();

        void CollectFrom(JsonObject holder, string targetKind, string targetName, string? entitySet)
        {
            foreach (var term in new[] { "@Agent.Examples", "@Agent.ExampleQueries" })
            {
                if (holder[term] is not JsonArray arr) continue;
                foreach (var item in arr)
                {
                    if (item is not JsonObject rec) continue;
                    examples.Add(new JsonObject
                    {
                        ["targetKind"] = targetKind,
                        ["targetName"] = targetName,
                        ["entitySet"] = entitySet,
                        ["utterance"] = rec["Utterance"]?.GetValue<string>() ?? "",
                        ["request"] = rec["Request"]?.GetValue<string>() ?? "",
                        ["description"] = rec["Description"]?.GetValue<string>() ?? ""
                    });
                }
            }
        }

        // 1. Collect from EntityTypes, Functions, and Actions in Microsoft.Graph schema
        foreach (var (name, node) in GraphSchema)
        {
            if (name == "GraphService" || node is not JsonObject def) continue;
            var kind = def["$Kind"]?.GetValue<string>() ?? "SchemaElement";
            CollectFrom(def, kind, $"Microsoft.Graph.{name}", null);
        }

        // 2. Collect from EntitySets and Singletons in GraphService EntityContainer
        foreach (var (name, node) in Container)
        {
            if (name.StartsWith('@') || node is not JsonObject def) continue;
            var isCollection = def["$Collection"]?.GetValue<bool>() == true;
            CollectFrom(def, isCollection ? "EntitySet" : "Singleton", $"Microsoft.Graph.GraphService/{name}", name);
        }

        return examples;
    }

    public static IReadOnlyList<string> ExtractODataQueries(string messageText)
    {
        var matches = Regex.Matches(
            messageText,
            @"(?:GET|POST)\s+((?:users|groups|me|drives|driveItems|teams|channels|chats|chatMessages|todoLists|todoTasks)(?:\([^)]*\))?(?:/[A-Za-z0-9_.\(\)'=$-]+)*(?:\?[^\s.]+(?:\s+(?:or|and|eq|ne)\s+[^\s.]+)*)?)",
            RegexOptions.CultureInvariant);
        return matches.Select(m => m.Groups[1].Value.TrimEnd('.', ',', ';')).Distinct(StringComparer.Ordinal).ToList();
    }

    private static (string[] EntitySets, string[] ToolBindings, string[] ResourceBindings, IReadOnlyList<string> Queries) InferPromptBindings(JsonObject prompt)
    {
        var text = prompt["Messages"]![0]!["Content"]!["Text"]!.GetValue<string>();
        var queries = ExtractODataQueries(text);
        var entitySets = new List<string>();
        bool touchesUsers = queries.Any(q => q.StartsWith("users", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me", StringComparison.OrdinalIgnoreCase))
            && !queries.All(q => q.StartsWith("me/drive", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me/joinedTeams", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me/chats", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me/todoLists", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me/memberOf", StringComparison.OrdinalIgnoreCase));
        bool touchesGroups = queries.Any(q => q.StartsWith("groups", StringComparison.OrdinalIgnoreCase) || q.StartsWith("me/memberOf", StringComparison.OrdinalIgnoreCase));
        bool touchesDrives = queries.Any(q => q.StartsWith("drives", StringComparison.OrdinalIgnoreCase) || q.StartsWith("driveItems", StringComparison.OrdinalIgnoreCase) || q.Contains("/drive", StringComparison.OrdinalIgnoreCase));
        bool touchesTeams = queries.Any(q => q.StartsWith("teams", StringComparison.OrdinalIgnoreCase) || q.Contains("joinedTeams", StringComparison.OrdinalIgnoreCase));
        bool touchesChannels = queries.Any(q => q.StartsWith("channels", StringComparison.OrdinalIgnoreCase) || q.Contains("/channels", StringComparison.OrdinalIgnoreCase));
        bool touchesChats = queries.Any(q => q.StartsWith("chats", StringComparison.OrdinalIgnoreCase) || q.Contains("/chats", StringComparison.OrdinalIgnoreCase));
        bool touchesTodo = queries.Any(q => q.StartsWith("todo", StringComparison.OrdinalIgnoreCase) || q.Contains("/todo", StringComparison.OrdinalIgnoreCase));

        if (touchesUsers) entitySets.Add("users");
        if (touchesGroups) entitySets.Add("groups");
        if (touchesDrives) entitySets.Add("driveItems");
        if (touchesTeams) entitySets.Add("teams");
        if (touchesChannels) entitySets.Add("channels");
        if (touchesChats) entitySets.Add("chats");
        if (touchesTodo) entitySets.Add("todoTasks");

        var tools = new List<string>();
        var resources = new List<string>();

        if (!touchesDrives && !touchesTeams && !touchesChannels && !touchesChats && !touchesTodo)
        {
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
        }
        else
        {
            if (touchesUsers) { tools.Add("user_profile"); resources.Add("users://{userId}"); }
            if (touchesGroups) { tools.Add("find_group"); resources.Add("groups://{groupId}"); }
            if (touchesDrives)
            {
                tools.Add(queries.Any(q => q.Contains("searchDriveItems", StringComparison.OrdinalIgnoreCase)) ? "search_files" :
                          queries.Any(q => q.Contains("lastModifiedByUser", StringComparison.OrdinalIgnoreCase)) ? "file_last_modified_by" : "list_my_files");
                resources.Add("files://recent");
            }
            if (touchesTeams) { tools.Add("list_teams"); resources.Add("teams://all"); }
            if (touchesChannels) { tools.Add("channel_messages"); resources.Add("channels://{channelId}/messages"); }
            if (touchesChats) { tools.Add("list_chats"); resources.Add("chats://all"); }
            if (touchesTodo) { tools.Add("list_todo_tasks"); resources.Add("todo://lists"); }
            if (entitySets.Count > 1) tools.Add("read_resource");
        }

        return (entitySets.ToArray(), tools.Distinct().ToArray(), resources.Distinct().ToArray(), queries);
    }

    public static void ValidateDependencies()
    {
        if (!EdmModelInstance.Validate(out var edmErrors))
        {
            var errorList = edmErrors.ToList();
            if (errorList.Count > 0)
            {
                var messages = string.Join(Environment.NewLine, errorList.Select(e => $"[{e.ErrorCode}] {e.ErrorMessage}"));
                throw new InvalidOperationException($"OData EDM model validation failed:{Environment.NewLine}{messages}");
            }
        }

        var names = Prompts.Select(p => p!["Name"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (names.Count != Prompts.Count)
            throw new InvalidOperationException("CSDL prompt names must be unique.");

        if (Model["Agent"] is JsonObject agentSchema &&
            agentSchema["ExampleQuery"] is JsonObject exampleQueryType &&
            exampleQueryType.ContainsKey("Prompt"))
        {
            throw new InvalidOperationException("ExampleQuery complex type must not contain a Prompt reference property.");
        }

        foreach (var node in GraphSchema.Select(kv => kv.Value).Concat(Container.Select(kv => kv.Value)))
        {
            if (node is not JsonObject holder) continue;
            if (holder["@Agent.Examples"] is not JsonArray exArr) continue;
            foreach (var item in exArr)
            {
                if (item is JsonObject rec && rec.ContainsKey("Prompt"))
                    throw new InvalidOperationException("Entity and operation examples must not reference prompts.");
            }
        }
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
        csdlPath = CsdlFilePath,
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
        modelParser = "Validated using Microsoft.OData.Edm (IEdmModel) with CsdlReader across 10 EntityTypes and 5 bound Operations.",
        promptProjection = "22 container @Agent.Prompts covering Users, Groups, OneDrive Files, Teams Channels, Chats, and To Do Tasks.",
        examples = "28 @Agent.Examples annotations attached directly to EntityTypes, EntitySets, Singletons, Functions, and Actions.",
        vocabulary = "Tools and resources are projected deterministically from native OData EntitySets, Singletons, Functions, and Actions."
    };
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GraphMcpServer.Services;
using Microsoft.OData.Edm;
using ModelContextProtocol.Protocol;

namespace GraphMcpServer.Csdl;

public sealed class CsdlMcpProjection(GraphClientService graphService)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["manager"] = ["manager", "reports", "report", "boss", "supervisor", "reporting", "approves", "director"],
        ["peers"] = ["peers", "peer", "teammates", "colleagues", "same", "team", "alongside", "level"],
        ["directreports"] = ["directreports", "direct", "reports", "subordinates", "managed", "supervised", "staff", "under"],
        ["department"] = ["department", "title", "jobtitle", "architect", "architects", "office", "location", "prague", "idm", "idna", "identity"],
        ["group"] = ["group", "groups", "members", "owners", "owner", "member", "security", "unified", "memberof"],
        ["file"] = ["file", "files", "onedrive", "drive", "driveitems", "document", "documents", "modify", "modified", "modifier", "lastmodifiedbyuser", "searchdriveitems", "recentdriveitems", "docx", "pptx", "xlsx", "pdf"],
        ["team"] = ["team", "teams", "joinedteams", "channel", "channels", "deltachannelmessages", "sendchannelmessage", "typespec", "post"],
        ["chat"] = ["chat", "chats", "oneonone", "1:1", "conversation", "thread", "messages"],
        ["todo"] = ["todo", "todolists", "todotasks", "task", "tasks", "pending", "open", "complete", "completetodotask", "action", "importance"]
    };

    public static void Validate()
    {
        UserCsdlProvider.ValidateDependencies();
    }

    public static ListToolsResult ListTools() => new()
    {
        Tools =
        [
            new Tool
            {
                Name = "search_intent",
                Title = "Search CSDL Intents, Prompts, Examples & Schema",
                Description = "Step 1: Search across the CSDL @Agent.Prompts, @Agent.Examples, EntityTypes, EntitySets, and bound OData Functions/Actions to identify the user's intent, default parameters, and canonical OData v4.01 query template before calling execute_odata_query.",
                InputSchema = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["query"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Natural-language user question, intent keywords, or target entity/operation (e.g. 'Miriam manager', 'last user to modify Script_MCP_POC.docx', 'CSDL channel messages', 'open high importance todo tasks')"
                        }
                    },
                    ["required"] = new JsonArray("query"),
                    ["additionalProperties"] = false
                })
            },
            new Tool
            {
                Name = "execute_odata_query",
                Title = "Execute Constructed OData v4.01 Query",
                Description = "Step 2: Execute a canonical OData v4.01 query constructed from the CSDL prompt/example templates returned by search_intent (e.g. \"GET users('miriamg@contoso.com')/manager\", \"GET drives('drive-miriam')/Microsoft.Graph.searchDriveItems(q='CSDL')\", \"GET channels('ch-csdl-typespec')/messages?$top=20\", \"GET me/todoLists?$expand=tasks\").",
                InputSchema = JsonSerializer.SerializeToElement(new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["odataQuery"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "Canonical OData v4.01 request path (with optional GET/POST prefix), e.g. \"GET users('miriamg@contoso.com')/manager\" or \"GET me/drive/root/children\""
                        }
                    },
                    ["required"] = new JsonArray("odataQuery"),
                    ["additionalProperties"] = false
                })
            }
        ]
    };

    public static ListResourcesResult ListResources() => new() { Resources = [] };
    public static ListResourceTemplatesResult ListResourceTemplates() => new() { ResourceTemplates = [] };
    public static ListPromptsResult ListPrompts() => new() { Prompts = [] };

    public ValueTask<ReadResourceResult> ReadResourceAsync(ReadResourceRequestParams request, CancellationToken cancellationToken)
        => throw new ArgumentException($"Direct resource reads are disabled in Intent+Query mode. Use search_intent + execute_odata_query instead ({request.Uri}).");

    public async ValueTask<CallToolResult> CallToolAsync(CallToolRequestParams request, CancellationToken cancellationToken)
    {
        if (request.Name == "search_intent")
        {
            var query = GetRequiredString(request.Arguments, "query");
            var searchResult = SearchIntentInCsdl(query);
            return new CallToolResult
            {
                IsError = false,
                Content = [new TextContentBlock { Text = JsonSerializer.Serialize(searchResult, JsonOptions) }]
            };
        }

        if (request.Name == "execute_odata_query")
        {
            var odataQuery = GetRequiredString(request.Arguments, "odataQuery");
            var executionResult = await ExecuteODataQueryAsync(odataQuery, cancellationToken);
            return new CallToolResult
            {
                IsError = executionResult == null,
                Content = [new TextContentBlock
                {
                    Text = executionResult == null
                        ? JsonSerializer.Serialize(new { error = $"Could not execute OData query: {odataQuery}" }, JsonOptions)
                        : JsonSerializer.Serialize(executionResult, JsonOptions)
                }]
            };
        }

        throw new ArgumentException($"Unknown tool '{request.Name}'. Available tools: search_intent, execute_odata_query.");
    }

    private static string GetRequiredString(IDictionary<string, JsonElement>? args, string key)
    {
        if (args != null && args.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(val.GetString()))
            return val.GetString()!.Trim();
        throw new ArgumentException($"Missing required string argument: {key}");
    }

    private static HashSet<string> ExpandTokens(string text)
    {
        var raw = Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9_@.-]+")
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && w is not ("the" or "and" or "for" or "with" or "from" or "that" or "this" or "what" or "who" or "whom" or "are" or "is" or "in" or "of" or "to" or "all" or "get" or "show" or "list" or "find"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expanded = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
        foreach (var (_, group) in Synonyms)
        {
            if (group.Any(g => raw.Contains(g)))
            {
                foreach (var g in group) expanded.Add(g);
            }
        }
        return expanded;
    }

    private static int ScoreText(HashSet<string> queryTokens, string candidateText)
    {
        var lower = candidateText.ToLowerInvariant();
        int score = 0;
        foreach (var token in queryTokens)
        {
            if (lower.Contains(token, StringComparison.OrdinalIgnoreCase))
                score += token.Length >= 5 ? 3 : 2;
        }
        return score;
    }

    public static object SearchIntentInCsdl(string query)
    {
        var tokens = ExpandTokens(query);
        var catalog = UserCsdlProvider.GetCatalog();
        var examples = UserCsdlProvider.GetExampleQueries();

        var scoredPrompts = catalog
            .Select(node => node!.AsObject())
            .Select(p =>
            {
                var blob = $"{p["name"]} {p["title"]} {p["description"]} {p["template"]}";
                return (Prompt: p, Score: ScoreText(tokens, blob));
            })
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => new
            {
                intent = x.Prompt["name"]?.GetValue<string>(),
                title = x.Prompt["title"]?.GetValue<string>(),
                description = x.Prompt["description"]?.GetValue<string>(),
                score = x.Score,
                arguments = x.Prompt["arguments"],
                odataQueries = x.Prompt["odataQueries"],
                workflowTemplate = x.Prompt["template"]?.GetValue<string>()
            })
            .ToList();

        var scoredExamples = examples
            .Select(node => node!.AsObject())
            .Select(ex =>
            {
                var blob = $"{ex["targetKind"]} {ex["targetName"]} {ex["entitySet"]} {ex["utterance"]} {ex["request"]} {ex["description"]}";
                return (Example: ex, Score: ScoreText(tokens, blob));
            })
            .OrderByDescending(x => x.Score)
            .Take(6)
            .Select(x => new
            {
                targetKind = x.Example["targetKind"]?.GetValue<string>(),
                targetName = x.Example["targetName"]?.GetValue<string>(),
                entitySet = x.Example["entitySet"]?.GetValue<string>(),
                utterance = x.Example["utterance"]?.GetValue<string>(),
                canonicalODataRequest = x.Example["request"]?.GetValue<string>(),
                description = x.Example["description"]?.GetValue<string>(),
                score = x.Score
            })
            .ToList();

        var schemaEntities = UserCsdlProvider.EdmModel.SchemaElements
            .OfType<IEdmEntityType>()
            .Select(e => new
            {
                entityType = e.FullName(),
                key = e.Key().Select(k => k.Name).ToArray(),
                properties = e.StructuralProperties().Select(p => p.Name).ToArray(),
                navigationProperties = e.NavigationProperties().Select(n => n.Name).ToArray()
            })
            .ToList();

        var boundOperations = UserCsdlProvider.EdmModel.SchemaElements
            .OfType<IEdmOperation>()
            .Select(op => new
            {
                kind = op.SchemaElementKind.ToString(),
                name = op.FullName(),
                parameters = op.Parameters.Select(p => new { p.Name, type = p.Type.FullName() }).ToArray(),
                returnType = op.ReturnType?.FullName()
            })
            .ToList();

        return new
        {
            query,
            architecture = "Two-Step Intent Identification + CSDL OData v4.01 Query Construction",
            matchedPrompts = scoredPrompts,
            matchedExamples = scoredExamples,
            knownDirectoryDefaults = new
            {
                olgaUpn = "miriamg@contoso.com",
                alexUpn = "alexw@contoso.com",
                meganUpn = "meganb@contoso.com",
                adeleUpn = "adelev@contoso.com",
                diegosUpn = "diegos@contoso.com",
                pattiUpn = "pattif@contoso.com",
                lynneUpn = "lynner@contoso.com",
                janUpn = "jonis@contoso.com",
                mikeUpn = "henriettam@contoso.com",
                identityArchitectureGroupId = "11111111-2222-3333-4444-555555555555",
                securityReviewersGroupId = "22222222-3333-4444-5555-666666666666",
                graphPartnersGroupId = "33333333-4444-5555-6666-777777777777",
                odataTcGroupId = "44444444-5555-6666-7777-888888888888",
                defaultDriveId = "drive-miriam"
            },
            schemaEntities,
            boundOperations
        };
    }

    public async Task<object?> ExecuteODataQueryAsync(string rawQuery, CancellationToken cancellationToken)
    {
        var q = rawQuery.Trim();
        var method = "GET";
        if (q.StartsWith("POST ", StringComparison.OrdinalIgnoreCase))
        {
            method = "POST";
            q = q[5..].Trim();
        }
        else if (q.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
        {
            method = "GET";
            q = q[4..].Trim();
        }

        q = Regex.Replace(q, @"^https?://[^/]+/(?:v1\.0|beta|odata)/", "", RegexOptions.IgnoreCase).TrimStart('/');
        q = Uri.UnescapeDataString(q);
        // Resolve OData parameter aliases such as (q=@q)?@q='CSDL'
        foreach (Match pm in Regex.Matches(q, @"@([A-Za-z0-9_]+)=('[^']*'|[^&]+)"))
        {
            q = q.Replace("@" + pm.Groups[1].Value, pm.Groups[2].Value);
        }

        // 1. Bound Actions / POST operations
        if (q.Contains("completeTodoTask", StringComparison.OrdinalIgnoreCase))
        {
            var taskIdMatch = Regex.Match(q, @"todoTasks\('([^']+)'\)", RegexOptions.IgnoreCase);
            var taskId = taskIdMatch.Success ? taskIdMatch.Groups[1].Value : "task-101";
            var task = await graphService.CompleteTodoTaskAsync(taskId, cancellationToken);
            return new { method = "POST", odataQuery = q, entitySet = "todoTasks", data = task };
        }

        if (q.Contains("sendChannelMessage", StringComparison.OrdinalIgnoreCase))
        {
            var chMatch = Regex.Match(q, @"channels\('([^']+)'\)", RegexOptions.IgnoreCase);
            var channelId = chMatch.Success ? chMatch.Groups[1].Value : "ch-mcp-20";
            var msg = await graphService.SendChannelMessageAsync(channelId, "MCP 2.0 OData Action", "Executed via execute_odata_query on LargeServer_SearchQuery (:3005).", cancellationToken);
            return new { method = "POST", odataQuery = q, entitySet = "channels", data = msg };
        }

        // 2. Drive & DriveItems queries
        if (q.Contains("searchDriveItems", StringComparison.OrdinalIgnoreCase))
        {
            var kwMatch = Regex.Match(q, @"searchDriveItems\(\s*q\s*=\s*'([^']*)'\s*\)", RegexOptions.IgnoreCase);
            var kw = kwMatch.Success ? kwMatch.Groups[1].Value : "CSDL";
            var items = await graphService.SearchDriveItemsAsync(kw, cancellationToken);
            return new { method, odataQuery = q, entitySet = "driveItems", data = new { query = kw, total = items.Count, items } };
        }

        if (q.Contains("lastModifiedByUser", StringComparison.OrdinalIgnoreCase) || q.StartsWith("driveItems(", StringComparison.OrdinalIgnoreCase))
        {
            var itemMatch = Regex.Match(q, @"driveItems\('([^']+)'\)", RegexOptions.IgnoreCase);
            var itemId = itemMatch.Success ? itemMatch.Groups[1].Value : "Script_MCP_POC.docx";
            var item = await graphService.GetDriveItemWithModifierAsync(itemId, cancellationToken);
            return new { method, odataQuery = q, entitySet = "driveItems", data = item };
        }

        if (q.Contains("recentDriveItems", StringComparison.OrdinalIgnoreCase) || q.Contains("/drive", StringComparison.OrdinalIgnoreCase) || q.StartsWith("drives", StringComparison.OrdinalIgnoreCase) || q.StartsWith("driveItems", StringComparison.OrdinalIgnoreCase))
        {
            var modMatch = Regex.Match(q, @"startswith\(lastModifiedByDisplayName\s*,\s*'([^']+)'\)", RegexOptions.IgnoreCase);
            if (modMatch.Success)
            {
                var items = await graphService.SearchDriveItemsAsync(modMatch.Groups[1].Value, cancellationToken);
                return new { method, odataQuery = q, entitySet = "driveItems", data = new { query = modMatch.Groups[1].Value, total = items.Count, items } };
            }
            var drive = await graphService.GetDriveAsync("me", cancellationToken);
            return new { method, odataQuery = q, entitySet = "drives", data = drive };
        }

        // 3. Teams & Channels queries
        if (q.StartsWith("channels", StringComparison.OrdinalIgnoreCase) || q.Contains("/channels", StringComparison.OrdinalIgnoreCase) && q.Contains("/messages", StringComparison.OrdinalIgnoreCase) || q.Contains("deltaChannelMessages", StringComparison.OrdinalIgnoreCase) || q.StartsWith("chatMessages", StringComparison.OrdinalIgnoreCase))
        {
            var chMatch = Regex.Match(q, @"channels\('([^']+)'\)", RegexOptions.IgnoreCase);
            var chFilterMatch = Regex.Match(q, @"startswith\(displayName\s*,\s*'([^']+)'\)", RegexOptions.IgnoreCase);
            var chId = chMatch.Success ? chMatch.Groups[1].Value : chFilterMatch.Success ? chFilterMatch.Groups[1].Value : "CSDL & TypeSpec";
            var channel = await graphService.GetChannelMessagesAsync(chId, cancellationToken);
            return new { method, odataQuery = q, entitySet = "channels", data = channel };
        }

        if (q.StartsWith("teams", StringComparison.OrdinalIgnoreCase) || q.Contains("joinedTeams", StringComparison.OrdinalIgnoreCase))
        {
            var filterMatch = Regex.Match(q, @"startswith\(displayName\s*,\s*'([^']+)'\)", RegexOptions.IgnoreCase);
            var teamIdMatch = Regex.Match(q, @"teams\('([^']+)'\)", RegexOptions.IgnoreCase);
            var filter = teamIdMatch.Success ? teamIdMatch.Groups[1].Value : filterMatch.Success ? filterMatch.Groups[1].Value : "all";
            var teams = await graphService.GetTeamsAsync(filter, cancellationToken);
            return new { method, odataQuery = q, entitySet = "teams", data = new { filter, total = teams.Count, teams } };
        }

        // 4. Chats queries
        if (q.StartsWith("chats", StringComparison.OrdinalIgnoreCase) || q.Contains("/chats", StringComparison.OrdinalIgnoreCase))
        {
            var chatIdMatch = Regex.Match(q, @"chats\('([^']+)'\)", RegexOptions.IgnoreCase);
            var typeMatch = Regex.Match(q, @"chatType\s+eq\s+'([^']+)'", RegexOptions.IgnoreCase);
            var filter = chatIdMatch.Success ? chatIdMatch.Groups[1].Value : typeMatch.Success ? typeMatch.Groups[1].Value : "all";
            var chats = await graphService.GetChatsAsync(filter, cancellationToken);
            return new { method, odataQuery = q, entitySet = "chats", data = new { filter, total = chats.Count, chats } };
        }

        // 5. To Do Tasks & Lists queries
        if (q.StartsWith("todo", StringComparison.OrdinalIgnoreCase) || q.Contains("/todo", StringComparison.OrdinalIgnoreCase))
        {
            var impMatch = Regex.Match(q, @"importance\s+eq\s+'([^']+)'", RegexOptions.IgnoreCase);
            var filter = impMatch.Success ? impMatch.Groups[1].Value : "all";
            var lists = await graphService.GetTodoTaskListsAsync(filter, cancellationToken);
            return new { method, odataQuery = q, entitySet = "todoTasks", data = new { filter, totalLists = lists.Count, todoLists = lists } };
        }

        // 6. Groups queries
        if (q.StartsWith("groups", StringComparison.OrdinalIgnoreCase) || q.Contains("memberOf", StringComparison.OrdinalIgnoreCase))
        {
            var grpKeyMatch = Regex.Match(q, @"groups\('([^']+)'\)", RegexOptions.IgnoreCase);
            var grpSlashMatch = Regex.Match(q, @"^groups/([^/?]+)", RegexOptions.IgnoreCase);
            if (grpKeyMatch.Success || grpSlashMatch.Success)
            {
                var gid = grpKeyMatch.Success ? grpKeyMatch.Groups[1].Value : grpSlashMatch.Groups[1].Value;
                var detail = await graphService.GetGroupDetailsAsync(gid, cancellationToken);
                return new { method, odataQuery = q, entitySet = "groups", data = detail };
            }
            var filterMatch = Regex.Match(q, @"'(?<val>[^']+)'", RegexOptions.IgnoreCase);
            var kw = filterMatch.Success && q.Contains("$filter", StringComparison.OrdinalIgnoreCase) ? filterMatch.Groups["val"].Value : "";
            var groups = string.IsNullOrEmpty(kw)
                ? await graphService.GetAllGroupsAsync(cancellationToken)
                : await graphService.FindGroupsAsync(kw, cancellationToken);
            return new { method, odataQuery = q, entitySet = "groups", data = new { query = kw, total = groups.Count, groups } };
        }

        // 7. Users & me queries
        if (q.StartsWith("me", StringComparison.OrdinalIgnoreCase))
        {
            var profile = await graphService.GetUserProfileAsync("me", cancellationToken);
            return new { method, odataQuery = q, entitySet = "users", data = profile };
        }

        if (q.StartsWith("users", StringComparison.OrdinalIgnoreCase))
        {
            var usrKeyMatch = Regex.Match(q, @"users\('([^']+)'\)", RegexOptions.IgnoreCase);
            var usrSlashMatch = Regex.Match(q, @"^users/([^/?]+)", RegexOptions.IgnoreCase);
            if (usrKeyMatch.Success || usrSlashMatch.Success)
            {
                var uid = usrKeyMatch.Success ? usrKeyMatch.Groups[1].Value : usrSlashMatch.Groups[1].Value;
                var profile = await graphService.GetUserProfileAsync(uid, cancellationToken);
                return new { method, odataQuery = q, entitySet = "users", data = profile };
            }

            // Extract filter value from $filter
            var strMatches = Regex.Matches(q, @"'([^']+)'").Select(m => m.Groups[1].Value).ToList();
            var searchKw = strMatches.LastOrDefault() ?? "";
            var users = await graphService.FindUsersAsync(searchKw, cancellationToken);
            return new { method, odataQuery = q, entitySet = "users", data = new { query = searchKw, total = users.Count, users } };
        }

        return null;
    }
}

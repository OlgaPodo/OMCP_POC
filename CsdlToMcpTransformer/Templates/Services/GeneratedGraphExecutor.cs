using System.Text.Json;
using GraphMcpServer.Csdl;

namespace GraphMcpServer.Services;

/// <summary>Dispatches generated request bindings to direct or workload-specific Graph client operations.</summary>
public sealed class GeneratedGraphExecutor(GraphClientService graphService)
{
    public async Task<object?> ExecuteAsync(
        ODataRequestBinding binding,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpMethod method,
        string path,
        JsonElement? body,
        CancellationToken cancellationToken)
    {
        var decodedPath = Uri.UnescapeDataString(path);
        switch (binding.ExecutionKind)
        {
            case GraphExecutionKind.Direct:
                return await graphService.ExecuteODataQueryAsync(
                    decodedPath,
                    cancellationToken);

            case GraphExecutionKind.Drive:
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = binding.EntitySet,
                    data = await graphService.GetDriveAsync("me", cancellationToken)
                };

            case GraphExecutionKind.DriveSearch:
                var query = Argument(arguments, "q", Argument(arguments, "query", "CSDL"));
                var items = await graphService.SearchDriveItemsAsync(query, cancellationToken);
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "driveItems",
                    data = new { query, total = items.Count, items }
                };

            case GraphExecutionKind.DriveItem:
                var itemId = Key(arguments, "Script_MCP_POC.docx");
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "driveItems",
                    data = await graphService.GetDriveItemWithModifierAsync(itemId, cancellationToken)
                };

            case GraphExecutionKind.Teams:
                var teamFilter = Key(arguments, Argument(arguments, "filter", "all"));
                var teams = await graphService.GetTeamsAsync(teamFilter, cancellationToken);
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "teams",
                    data = new { filter = teamFilter, total = teams.Count, teams }
                };

            case GraphExecutionKind.Channel:
                var channelId = Key(arguments, "CSDL & TypeSpec");
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "channels",
                    data = await graphService.GetChannelMessagesAsync(channelId, cancellationToken)
                };

            case GraphExecutionKind.Chats:
                var chatFilter = Key(arguments, Argument(arguments, "filter", "all"));
                var chats = await graphService.GetChatsAsync(chatFilter, cancellationToken);
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "chats",
                    data = new { filter = chatFilter, total = chats.Count, chats }
                };

            case GraphExecutionKind.Todo:
                var todoFilter = Argument(arguments, "filter", "all");
                var lists = await graphService.GetTodoTaskListsAsync(todoFilter, cancellationToken);
                return new
                {
                    method = method.Method,
                    odataQuery = decodedPath,
                    entitySet = "todoTasks",
                    data = new { filter = todoFilter, totalLists = lists.Count, todoLists = lists }
                };

            case GraphExecutionKind.ChannelSend:
                var targetChannel = Key(arguments, "CSDL & TypeSpec");
                var subject = BodyString(body, "subject", "MCP 2.0 OData Action");
                var bodyContent = BodyString(
                    body,
                    "bodyContent",
                    "Executed by the generated static MCP server.");
                return new
                {
                    method = "POST",
                    odataQuery = decodedPath,
                    entitySet = "channels",
                    data = await graphService.SendChannelMessageAsync(
                        targetChannel,
                        subject,
                        bodyContent,
                        cancellationToken)
                };

            case GraphExecutionKind.TodoComplete:
                var taskId = Key(arguments, "task-101");
                return new
                {
                    method = "POST",
                    odataQuery = decodedPath,
                    entitySet = "todoTasks",
                    data = await graphService.CompleteTodoTaskAsync(taskId, cancellationToken)
                };

            default:
                throw new InvalidOperationException(
                    $"Unsupported generated Graph execution kind: {binding.ExecutionKind}.");
        }
    }

    private static string Key(IReadOnlyDictionary<string, JsonElement> arguments, string fallback)
    {
        var key = arguments.FirstOrDefault(argument =>
            argument.Key.StartsWith("key_", StringComparison.Ordinal));
        return key.Key == null ? fallback : ElementString(key.Value, fallback);
    }

    private static string Argument(
        IReadOnlyDictionary<string, JsonElement> arguments,
        string name,
        string fallback)
        => arguments.TryGetValue(name, out var value) ? ElementString(value, fallback) : fallback;

    private static string ElementString(JsonElement value, string fallback)
        => value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!
            : fallback;

    private static string BodyString(JsonElement? body, string name, string fallback)
        => body is JsonElement element &&
           element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value)
            ? ElementString(value, fallback)
            : fallback;
}

using System.ComponentModel;
using System.Text.Json;
using GraphMcpServer.Services;
using ModelContextProtocol.Server;

namespace GraphMcpServer.Resources;

public class GraphResources
{
    private readonly GraphClientService _graphService;

    public GraphResources(GraphClientService graphService)
    {
        _graphService = graphService;
    }

    [McpServerResource(
        UriTemplate = "users://me",
        Name = "me",
        Title = "Current Authenticated User Profile",
        MimeType = "application/json")]
    [Description("Provides the profile and details of the current authenticated user (/me).")]
    public async Task<string> GetMe(CancellationToken cancellationToken = default)
    {
        var me = await _graphService.GetMeAsync(cancellationToken);
        return JsonSerializer.Serialize(me, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerResource(
        UriTemplate = "users://{userId}",
        Name = "user",
        Title = "User Profile Resource",
        MimeType = "application/json")]
    [Description("Provides a user profile representation by user ID or userPrincipalName.")]
    public async Task<string> GetUser(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _graphService.GetUserProfileAsync(userId, cancellationToken);
        return JsonSerializer.Serialize(user, new JsonSerializerOptions { WriteIndented = true });
    }
}

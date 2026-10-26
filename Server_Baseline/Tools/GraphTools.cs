using System.ComponentModel;
using System.Text.Json;
using GraphMcpServer.Models;
using GraphMcpServer.Services;
using ModelContextProtocol.Server;

namespace GraphMcpServer.Tools;

public class GraphTools
{
    private readonly GraphClientService _graphService;

    public GraphTools(GraphClientService graphService)
    {
        _graphService = graphService;
    }

    [McpServerTool(Name = "find_user")]
    [Description("Search and filter Microsoft 365 / Entra ID users by name, email, or userPrincipalName via Microsoft Graph API.")]
    public async Task<string> FindUser(
        [Description("Name prefix, display name, email, or keyword to query (e.g., 'Olga', 'Vojtech')")] string query,
        CancellationToken cancellationToken = default)
    {
        var users = await _graphService.FindUsersAsync(query, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            query,
            total = users.Count,
            users = users.Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.UserPrincipalName,
                u.Mail,
                u.JobTitle,
                u.Department,
                u.OfficeLocation
            })
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [McpServerTool(Name = "user_profile")]
    [Description("Retrieve detailed profile for a user by id, userPrincipalName, or 'me', including organizational manager, direct reports, and peer colleagues.")]
    public async Task<string> UserProfile(
        [Description("User ID, userPrincipalName (e.g. 'olga@contoso.com'), or 'me'")] string userId = "me",
        CancellationToken cancellationToken = default)
    {
        var profile = await _graphService.GetUserProfileAsync(userId, cancellationToken);
        if (profile == null)
        {
            return JsonSerializer.Serialize(new { error = $"User not found for identifier '{userId}'." });
        }

        return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
    }
}

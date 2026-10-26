using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GraphMcpServer.Prompts;

public class GraphPrompts
{
    [McpServerPrompt(Name = "whom_olga_reports_to", Title = "Whom Olga reports to?")]
    [Description("Find the manager and organizational reporting line for Olga Podolyako (or any specified user).")]
    public GetPromptResult WhomOlgaReportsTo(
        [Description("User's full name, UPN, or 'me'")] string userName = "Olga Podolyako")
    {
        return new GetPromptResult
        {
            Description = $"Resolve manager and organizational hierarchy for {userName}.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"Whom does {userName} report to?"
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "who_are_olgas_peers", Title = "Who are Olga's peers?")]
    [Description("Identify peer colleagues who report to the same manager as Olga Podolyako.")]
    public GetPromptResult WhoAreOlgasPeers(
        [Description("User's full name, UPN, or 'me'")] string userName = "Olga Podolyako")
    {
        return new GetPromptResult
        {
            Description = $"Discover peers of {userName} under their common manager.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"Who are {userName}'s peers?"
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "direct_reports_of_manager", Title = "Direct reports of a manager")]
    [Description("Find all colleagues and direct reports reporting to a given manager.")]
    public GetPromptResult DirectReportsOfManager(
        [Description("Manager's name, UPN, or ID")] string managerNameOrId = "Alex Wilbur")
    {
        return new GetPromptResult
        {
            Description = $"List direct reports for manager {managerNameOrId}.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"List all direct reports for manager '{managerNameOrId}'."
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "search_users_by_dept_title", Title = "Search users by department and title")]
    [Description("Search for organizational users filtered by department and job title.")]
    public GetPromptResult SearchUsersByDeptTitle(
        [Description("Department name")] string department = "Identity",
        [Description("Job title keywords")] string jobTitle = "Architect")
    {
        return new GetPromptResult
        {
            Description = $"Search organizational users in department '{department}' with job title '{jobTitle}'.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"Find all employees in the '{department}' department holding a '{jobTitle}' title."
                    }
                }
            ]
        };
    }

    [McpServerPrompt(Name = "user_quick_lookup", Title = "User Quick Lookup and Profile Card")]
    [Description("Fetch a user's full contact card.")]
    public GetPromptResult UserQuickLookup(
        [Description("User ID, UPN, or 'me'")] string identifier = "me")
    {
        return new GetPromptResult
        {
            Description = $"Quick lookup for user '{identifier}'.",
            Messages =
            [
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $"Display the complete profile card for '{identifier}'."
                    }
                }
            ]
        };
    }
}

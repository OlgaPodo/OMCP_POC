using System.Text.Json.Serialization;

namespace GraphMcpServer.Models;

public class GraphUser
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("userPrincipalName")]
    public string UserPrincipalName { get; set; } = string.Empty;

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("jobTitle")]
    public string? JobTitle { get; set; }

    [JsonPropertyName("department")]
    public string? Department { get; set; }

    [JsonPropertyName("officeLocation")]
    public string? OfficeLocation { get; set; }

    [JsonPropertyName("mobilePhone")]
    public string? MobilePhone { get; set; }

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }
}

public class UserProfileDetail : GraphUser
{
    [JsonPropertyName("manager")]
    public GraphUser? Manager { get; set; }

    [JsonPropertyName("peers")]
    public List<GraphUser> Peers { get; set; } = [];

    [JsonPropertyName("directReports")]
    public List<GraphUser> DirectReports { get; set; } = [];

    [JsonPropertyName("source")]
    public string Source { get; set; } = "Microsoft Graph v1.0";
}

public class GraphGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("mail")]
    public string? Mail { get; set; }

    [JsonPropertyName("mailNickname")]
    public string? MailNickname { get; set; }

    [JsonPropertyName("groupTypes")]
    public List<string> GroupTypes { get; set; } = [];

    [JsonPropertyName("mailEnabled")]
    public bool MailEnabled { get; set; }

    [JsonPropertyName("securityEnabled")]
    public bool SecurityEnabled { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }
}

public class GroupDetail : GraphGroup
{
    [JsonPropertyName("owners")]
    public List<GraphUser> Owners { get; set; } = [];

    [JsonPropertyName("members")]
    public List<GraphUser> Members { get; set; } = [];

    [JsonPropertyName("source")]
    public string Source { get; set; } = "Microsoft Graph v1.0";
}

public class GraphDrive
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("driveType")]
    public string DriveType { get; set; } = "business";

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("ownerDisplayName")]
    public string? OwnerDisplayName { get; set; }

    [JsonPropertyName("quotaTotal")]
    public long QuotaTotal { get; set; }

    [JsonPropertyName("quotaUsed")]
    public long QuotaUsed { get; set; }

    [JsonPropertyName("items")]
    public List<GraphDriveItem> Items { get; set; } = [];
}

public class GraphDriveItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("fileMimeType")]
    public string? FileMimeType { get; set; }

    [JsonPropertyName("isFolder")]
    public bool IsFolder { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastModifiedDateTime")]
    public DateTimeOffset? LastModifiedDateTime { get; set; }

    [JsonPropertyName("lastModifiedByDisplayName")]
    public string? LastModifiedByDisplayName { get; set; }

    [JsonPropertyName("lastModifiedByUpn")]
    public string? LastModifiedByUpn { get; set; }

    [JsonPropertyName("lastModifiedByUser")]
    public GraphUser? LastModifiedByUser { get; set; }
}

public class GraphTeam
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("visibility")]
    public string Visibility { get; set; } = "Public";

    [JsonPropertyName("isArchived")]
    public bool IsArchived { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("channels")]
    public List<GraphChannel> Channels { get; set; } = [];
}

public class GraphChannel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("teamId")]
    public string TeamId { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("membershipType")]
    public string MembershipType { get; set; } = "standard";

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("messages")]
    public List<GraphChatMessage> Messages { get; set; } = [];
}

public class GraphChat
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("topic")]
    public string? Topic { get; set; }

    [JsonPropertyName("chatType")]
    public string ChatType { get; set; } = "oneOnOne";

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("lastUpdatedDateTime")]
    public DateTimeOffset? LastUpdatedDateTime { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }

    [JsonPropertyName("members")]
    public List<GraphUser> Members { get; set; } = [];

    [JsonPropertyName("messages")]
    public List<GraphChatMessage> Messages { get; set; } = [];
}

public class GraphChatMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("subject")]
    public string? Subject { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("bodyContent")]
    public string BodyContent { get; set; } = string.Empty;

    [JsonPropertyName("fromDisplayName")]
    public string FromDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("fromUpn")]
    public string FromUpn { get; set; } = string.Empty;

    [JsonPropertyName("importance")]
    public string Importance { get; set; } = "normal";

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("webUrl")]
    public string? WebUrl { get; set; }
}

public class GraphTodoTaskList
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("isOwner")]
    public bool IsOwner { get; set; } = true;

    [JsonPropertyName("isShared")]
    public bool IsShared { get; set; }

    [JsonPropertyName("wellknownListName")]
    public string WellknownListName { get; set; } = "defaultList";

    [JsonPropertyName("tasks")]
    public List<GraphTodoTask> Tasks { get; set; } = [];
}

public class GraphTodoTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("listId")]
    public string ListId { get; set; } = string.Empty;

    [JsonPropertyName("listDisplayName")]
    public string ListDisplayName { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("bodyPreview")]
    public string? BodyPreview { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "notStarted";

    [JsonPropertyName("importance")]
    public string Importance { get; set; } = "normal";

    [JsonPropertyName("isReminderOn")]
    public bool IsReminderOn { get; set; }

    [JsonPropertyName("createdDateTime")]
    public DateTimeOffset? CreatedDateTime { get; set; }

    [JsonPropertyName("dueDateTime")]
    public DateTimeOffset? DueDateTime { get; set; }

    [JsonPropertyName("completedDateTime")]
    public DateTimeOffset? CompletedDateTime { get; set; }

    [JsonPropertyName("assignedToUpn")]
    public string? AssignedToUpn { get; set; }
}

public class GraphListResponse<T>
{
    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];
}

public class GraphTraceRecord
{
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? RequestHeaders { get; set; }
    public string? ResponseSummary { get; set; }
    public bool IsLive { get; set; }
}

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

public class GraphListResponse<T>
{
    [JsonPropertyName("value")]
    public List<T> Value { get; set; } = [];

    [JsonPropertyName("@odata.context")]
    public string? ODataContext { get; set; }

    [JsonPropertyName("@odata.nextLink")]
    public string? ODataNextLink { get; set; }
}

public class GraphTraceRecord
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 200;
    public string? RequestHeaders { get; set; }
    public string? ResponseSummary { get; set; }
    public bool IsLive { get; set; } = true;
}

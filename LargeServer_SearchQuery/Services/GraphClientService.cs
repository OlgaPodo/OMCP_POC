using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using GraphMcpServer.Models;

namespace GraphMcpServer.Services;

public class GraphClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphClientService> _logger;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresOn = DateTimeOffset.MinValue;
    private readonly List<GraphTraceRecord> _traces = [];
    private readonly object _traceLock = new();

    private static readonly List<GraphChatMessage> PostedChannelMessages = [];
    private static readonly HashSet<string> CompletedTaskIds = new(StringComparer.OrdinalIgnoreCase);

    public GraphClientService(HttpClient httpClient, ILogger<GraphClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://graph.contoso.com/v1.0/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public List<GraphTraceRecord> GetRecentTraces()
    {
        lock (_traceLock)
        {
            return _traces.TakeLast(30).ToList();
        }
    }

    private void AddTrace(GraphTraceRecord trace)
    {
        lock (_traceLock)
        {
            _traces.Add(trace);
            if (_traces.Count > 100) _traces.RemoveAt(0);
        }
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_cachedToken) && DateTimeOffset.UtcNow < _tokenExpiresOn.AddMinutes(-5))
        {
            return _cachedToken;
        }

        try
        {
            var credential = new AzureCliCredential();
            var tokenResult = await credential.GetTokenAsync(
                new TokenRequestContext(["https://graph.contoso.com/.default"]),
                cancellationToken);

            _cachedToken = tokenResult.Token;
            _tokenExpiresOn = tokenResult.ExpiresOn;
            _logger.LogInformation("Successfully acquired Microsoft Graph access token via Azure CLI. Expires: {ExpiresOn}", _tokenExpiresOn);
            return _cachedToken;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AzureCliCredential failed. Attempting direct 'az account get-access-token' CLI execution.");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "az",
                    Arguments = "account get-access-token --resource-type ms-graph",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process != null)
                {
                    string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                    {
                        using var doc = JsonDocument.Parse(output);
                        if (doc.RootElement.TryGetProperty("accessToken", out var tokenProp))
                        {
                            _cachedToken = tokenProp.GetString();
                            _tokenExpiresOn = DateTimeOffset.UtcNow.AddMinutes(50);
                            _logger.LogInformation("Successfully acquired token from az CLI process directly.");
                            return _cachedToken;
                        }
                    }
                }
            }
            catch (Exception pEx)
            {
                _logger.LogWarning(pEx, "Direct az process token retrieval failed.");
            }
        }

        return null;
    }

    public async Task<List<GraphUser>> FindUsersAsync(string query, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                string safeQuery = Uri.EscapeDataString(query.Trim());
                // Use Graph filter on displayName, mail, or userPrincipalName
                string requestUrl = $"users?$filter=startswith(displayName,'{safeQuery}') or startswith(userPrincipalName,'{safeQuery}') or startswith(mail,'{safeQuery}')&$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation&$top=20";

                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: Bearer [REDACTED]\nAccept: application/json",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;

                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);

                    var list = JsonSerializer.Deserialize<GraphListResponse<GraphUser>>(content);
                    if (list?.Value != null && list.Value.Count > 0)
                    {
                        return list.Value;
                    }
                }
                else
                {
                    trace.ResponseSummary = await res.Content.ReadAsStringAsync(cancellationToken);
                    AddTrace(trace);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Graph call for FindUsers failed. Falling back to high-fidelity mock data.");
                AddTrace(new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/users?$filter=... [FALLBACK]",
                    StatusCode = 500,
                    ResponseSummary = ex.Message,
                    IsLive = false
                });
            }
        }

        // Offline / fallback data
        return GetMockUsers()
            .Where(u => u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        u.UserPrincipalName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (u.Mail != null && u.Mail.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (u.JobTitle != null && u.JobTitle.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (u.Department != null && u.Department.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<UserProfileDetail?> GetUserProfileAsync(string userIdOrUpn, CancellationToken cancellationToken = default)
    {
        string target = userIdOrUpn.Trim();
        bool isMe = string.Equals(target, "me", StringComparison.OrdinalIgnoreCase);

        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                string userEndpoint = isMe ? "me" : $"users/{Uri.EscapeDataString(target)}";
                string requestUrl = $"{userEndpoint}?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation,mobilePhone,preferredLanguage";

                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: Bearer [REDACTED]\nAccept: application/json",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;

                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);

                    var user = JsonSerializer.Deserialize<GraphUser>(content);
                    if (user != null)
                    {
                        var profile = new UserProfileDetail
                        {
                            Id = user.Id,
                            DisplayName = user.DisplayName,
                            UserPrincipalName = user.UserPrincipalName,
                            Mail = user.Mail,
                            JobTitle = user.JobTitle,
                            Department = user.Department,
                            OfficeLocation = user.OfficeLocation,
                            MobilePhone = user.MobilePhone,
                            PreferredLanguage = user.PreferredLanguage,
                            Source = "Microsoft Graph v1.0 (Live)"
                        };

                        // Fetch manager
                        profile.Manager = await GetManagerInternalAsync(user.Id, token, cancellationToken);

                        // Fetch direct reports
                        profile.DirectReports = await GetDirectReportsInternalAsync(user.Id, token, cancellationToken);

                        // If manager exists, fetch manager's direct reports to get peers
                        if (profile.Manager != null)
                        {
                            var mgrReports = await GetDirectReportsInternalAsync(profile.Manager.Id, token, cancellationToken);
                            profile.Peers = mgrReports.Where(p => !string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                        }

                        return profile;
                    }
                }
                else
                {
                    trace.ResponseSummary = await res.Content.ReadAsStringAsync(cancellationToken);
                    AddTrace(trace);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Graph call for GetUserProfile failed. Falling back to high-fidelity mock data.");
            }
        }

        // Mock fallback
        var mock = GetMockUserProfile(target);
        if (mock != null)
        {
            mock.Source = "Microsoft Graph v1.0 (High-Fidelity Mock Fallback)";
        }
        return mock;
    }

    public async Task<GraphUser?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var profile = await GetUserProfileAsync("me", cancellationToken);
        return profile;
    }

    public async Task<GraphUser?> GetUserManagerAsync(string userIdOrUpn, CancellationToken cancellationToken = default)
    {
        var profile = await GetUserProfileAsync(userIdOrUpn, cancellationToken);
        return profile?.Manager;
    }

    public async Task<List<GraphUser>> GetUserPeersAsync(string userIdOrUpn, CancellationToken cancellationToken = default)
    {
        var profile = await GetUserProfileAsync(userIdOrUpn, cancellationToken);
        return profile?.Peers ?? [];
    }

    public async Task<List<GraphUser>> GetDirectReportsAsync(string managerIdOrUpn, CancellationToken cancellationToken = default)
    {
        var profile = await GetUserProfileAsync(managerIdOrUpn, cancellationToken);
        return profile?.DirectReports ?? [];
    }

    private async Task<GraphUser?> GetManagerInternalAsync(string userId, string token, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"users/{Uri.EscapeDataString(userId)}/manager?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var trace = new GraphTraceRecord
            {
                Method = "GET",
                Url = $"https://graph.contoso.com/v1.0/{url}",
                RequestHeaders = "Authorization: Bearer [REDACTED]",
                IsLive = true
            };

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            trace.StatusCode = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync(cancellationToken);
                trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                AddTrace(trace);
                return JsonSerializer.Deserialize<GraphUser>(content);
            }
            AddTrace(trace);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get manager for {UserId}", userId);
        }
        return null;
    }

    private async Task<List<GraphUser>> GetDirectReportsInternalAsync(string userId, string token, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"users/{Uri.EscapeDataString(userId)}/directReports?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var trace = new GraphTraceRecord
            {
                Method = "GET",
                Url = $"https://graph.contoso.com/v1.0/{url}",
                RequestHeaders = "Authorization: Bearer [REDACTED]",
                IsLive = true
            };

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            trace.StatusCode = (int)res.StatusCode;
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync(cancellationToken);
                trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                AddTrace(trace);
                var list = JsonSerializer.Deserialize<GraphListResponse<GraphUser>>(content);
                return list?.Value ?? [];
            }
            AddTrace(trace);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get direct reports for {UserId}", userId);
        }
        return [];
    }


    public async Task<List<GraphGroup>> FindGroupsAsync(string query, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                string safeQuery = Uri.EscapeDataString(query.Trim());
                string requestUrl = $"groups?$filter=startswith(displayName,'{safeQuery}') or startswith(mailNickname,'{safeQuery}') or startswith(mail,'{safeQuery}')&$select=id,displayName,description,mail,mailNickname,groupTypes,mailEnabled,securityEnabled,visibility,createdDateTime&$top=20";

                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;

                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);

                    var list = JsonSerializer.Deserialize<GraphListResponse<GraphGroup>>(content);
                    if (list?.Value != null && list.Value.Count > 0)
                    {
                        return list.Value;
                    }
                }
                else
                {
                    trace.ResponseSummary = await res.Content.ReadAsStringAsync(cancellationToken);
                    AddTrace(trace);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Graph call for FindGroups failed. Falling back to mock groups.");
                AddTrace(new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/groups?$filter=... [FALLBACK]",
                    StatusCode = 500,
                    ResponseSummary = ex.Message,
                    IsLive = false
                });
            }
        }

        return GetMockGroups()
            .Where(g => g.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        (g.MailNickname != null && g.MailNickname.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (g.Mail != null && g.Mail.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (g.Description != null && g.Description.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    public async Task<List<GraphGroup>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                string requestUrl = "groups?$select=id,displayName,description,mail,mailNickname,groupTypes,mailEnabled,securityEnabled,visibility,createdDateTime&$top=50";
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);

                    var list = JsonSerializer.Deserialize<GraphListResponse<GraphGroup>>(content);
                    if (list?.Value != null && list.Value.Count > 0) return list.Value;
                }
                AddTrace(trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Graph call for GetAllGroups failed. Using mock groups.");
            }
        }
        return GetMockGroups();
    }

    public async Task<GroupDetail?> GetGroupDetailsAsync(string groupIdOrName, CancellationToken cancellationToken = default)
    {
        string target = groupIdOrName.Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                string requestUrl = $"groups/{Uri.EscapeDataString(target)}?$select=id,displayName,description,mail,mailNickname,groupTypes,mailEnabled,securityEnabled,visibility,createdDateTime";
                using var req = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);

                    var group = JsonSerializer.Deserialize<GraphGroup>(content);
                    if (group != null)
                    {
                        var owners = await GetGroupMembersOrOwnersInternalAsync(group.Id, "owners", token, cancellationToken);
                        var members = await GetGroupMembersOrOwnersInternalAsync(group.Id, "members", token, cancellationToken);

                        return new GroupDetail
                        {
                            Id = group.Id,
                            DisplayName = group.DisplayName,
                            Description = group.Description,
                            Mail = group.Mail,
                            MailNickname = group.MailNickname,
                            GroupTypes = group.GroupTypes,
                            MailEnabled = group.MailEnabled,
                            SecurityEnabled = group.SecurityEnabled,
                            Visibility = group.Visibility,
                            CreatedDateTime = group.CreatedDateTime,
                            Owners = owners,
                            Members = members,
                            Source = "Microsoft Graph v1.0 (Live)"
                        };
                    }
                }
                AddTrace(trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get live group details for {Group}", groupIdOrName);
            }
        }

        return GetMockGroupDetails(target);
    }

    private async Task<List<GraphUser>> GetGroupMembersOrOwnersInternalAsync(string groupId, string segment, string token, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"groups/{Uri.EscapeDataString(groupId)}/{segment}?$select=id,displayName,userPrincipalName,mail,jobTitle,department";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync(cancellationToken);
                var list = JsonSerializer.Deserialize<GraphListResponse<GraphUser>>(content);
                return list?.Value ?? [];
            }
        }
        catch
        {
        }
        return [];
    }

    public async Task<object?> ExecuteODataQueryAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string cleanPath = path.Trim().TrimStart('/');
        if (cleanPath.StartsWith("https://graph.contoso.com/v1.0/", StringComparison.OrdinalIgnoreCase))
            cleanPath = cleanPath["https://graph.contoso.com/v1.0/".Length..];
        else if (cleanPath.StartsWith("v1.0/", StringComparison.OrdinalIgnoreCase))
            cleanPath = cleanPath["v1.0/".Length..];
        else if (cleanPath.StartsWith("users://", StringComparison.OrdinalIgnoreCase))
            cleanPath = "users/" + cleanPath["users://".Length..];
        else if (cleanPath.StartsWith("groups://", StringComparison.OrdinalIgnoreCase))
            cleanPath = "groups/" + cleanPath["groups://".Length..];

        // Normalize OData parentheses key syntax, e.g. users('xyz') -> users/xyz
        cleanPath = System.Text.RegularExpressions.Regex.Replace(cleanPath, @"users\('([^']+)'\)", "users/$1");
        cleanPath = System.Text.RegularExpressions.Regex.Replace(cleanPath, @"groups\('([^']+)'\)", "groups/$1");

        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, cleanPath);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.contoso.com/v1.0/{cleanPath}",
                    RequestHeaders = "Authorization: ****** application/json",
                    IsLive = true
                };
                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    var content = await res.Content.ReadAsStringAsync(cancellationToken);
                    trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
                    AddTrace(trace);
                    return JsonDocument.Parse(content).RootElement.Clone();
                }
                trace.ResponseSummary = await res.Content.ReadAsStringAsync(cancellationToken);
                AddTrace(trace);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live Graph call failed for {Path}. Falling back to mock dataset.", cleanPath);
            }
        }

        // Offline / fallback dispatcher
        return await ExecuteMockODataQueryAsync(cleanPath, cancellationToken);
    }

    private async Task<object?> ExecuteMockODataQueryAsync(string path, CancellationToken cancellationToken)
    {
        string rawPath = path;
        string queryString = "";
        int qIdx = path.IndexOf('?');
        if (qIdx >= 0)
        {
            rawPath = path[..qIdx];
            queryString = path[(qIdx + 1)..];
        }

        string[] parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        // /me endpoint
        if (string.Equals(parts[0], "me", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1) return await GetMeAsync(cancellationToken);
            if (parts.Length >= 2)
            {
                if (string.Equals(parts[1], "manager", StringComparison.OrdinalIgnoreCase))
                    return await GetUserManagerAsync("me", cancellationToken);
                if (string.Equals(parts[1], "directReports", StringComparison.OrdinalIgnoreCase))
                    return await GetDirectReportsAsync("me", cancellationToken);
                if (string.Equals(parts[1], "peers", StringComparison.OrdinalIgnoreCase))
                    return await GetUserPeersAsync("me", cancellationToken);
            }
        }

        // /users endpoint
        if (string.Equals(parts[0], "users", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1)
            {
                if (!string.IsNullOrEmpty(queryString))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(queryString, @"(?:eq|startswith\([^,]+,)\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return await FindUsersAsync(match.Groups[1].Value, cancellationToken);
                    }
                }
                return GetMockUsers();
            }

            string userId = parts[1];
            if (parts.Length == 2)
            {
                return await GetUserProfileAsync(userId, cancellationToken);
            }

            if (parts.Length >= 3)
            {
                if (string.Equals(parts[2], "manager", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length >= 4 && string.Equals(parts[3], "directReports", StringComparison.OrdinalIgnoreCase))
                        return await GetUserPeersAsync(userId, cancellationToken);
                    return await GetUserManagerAsync(userId, cancellationToken);
                }
                if (string.Equals(parts[2], "directReports", StringComparison.OrdinalIgnoreCase))
                    return await GetDirectReportsAsync(userId, cancellationToken);
                if (string.Equals(parts[2], "peers", StringComparison.OrdinalIgnoreCase))
                    return await GetUserPeersAsync(userId, cancellationToken);
            }
        }

        // /groups endpoint
        if (string.Equals(parts[0], "groups", StringComparison.OrdinalIgnoreCase))
        {
            if (parts.Length == 1)
            {
                if (!string.IsNullOrEmpty(queryString))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(queryString, @"(?:eq|startswith\([^,]+,)\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return await FindGroupsAsync(match.Groups[1].Value, cancellationToken);
                    }
                }
                return await GetAllGroupsAsync(cancellationToken);
            }

            string groupId = parts[1];
            if (parts.Length == 2)
            {
                return await GetGroupDetailsAsync(groupId, cancellationToken);
            }

            if (parts.Length >= 3)
            {
                var details = await GetGroupDetailsAsync(groupId, cancellationToken);
                if (string.Equals(parts[2], "members", StringComparison.OrdinalIgnoreCase))
                    return details?.Members ?? [];
                if (string.Equals(parts[2], "owners", StringComparison.OrdinalIgnoreCase))
                    return details?.Owners ?? [];
            }
        }

        return null;
    }

    // Files & OneDrive Operations
    public Task<GraphDrive> GetDriveAsync(string userOrDriveId = "me", CancellationToken cancellationToken = default)
    {
        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = userOrDriveId.Equals("me", StringComparison.OrdinalIgnoreCase)
                ? "https://graph.contoso.com/v1.0/me/drive/root/children?$select=id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName"
                : $"https://graph.contoso.com/v1.0/drives('{Uri.EscapeDataString(userOrDriveId)}')/root/children",
            StatusCode = 200,
            ResponseSummary = "OneDrive root children and quota",
            IsLive = false
        });
        return Task.FromResult(GetMockDrives().First());
    }

    public Task<List<GraphDriveItem>> SearchDriveItemsAsync(string query, CancellationToken cancellationToken = default)
    {
        string q = (query ?? "").Trim();
        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = $"https://graph.contoso.com/v1.0/drives('drive-miriam')/Microsoft.Graph.searchDriveItems(q='{Uri.EscapeDataString(q)}')?$select=id,name,size,webUrl,fileMimeType,lastModifiedDateTime,lastModifiedByDisplayName",
            StatusCode = 200,
            ResponseSummary = $"Bound Function searchDriveItems(q='{q}')",
            IsLive = false
        });
        var items = GetMockDriveItems()
            .Where(f => string.IsNullOrWhiteSpace(q) ||
                        f.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (f.LastModifiedByDisplayName != null && f.LastModifiedByDisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        (f.FileMimeType != null && f.FileMimeType.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult(items);
    }

    public Task<GraphDriveItem?> GetDriveItemWithModifierAsync(string fileIdOrName, CancellationToken cancellationToken = default)
    {
        string target = (fileIdOrName ?? "").Trim();
        var item = GetMockDriveItems().FirstOrDefault(f =>
            string.Equals(f.Id, target, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(f.Name, target, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains(target, StringComparison.OrdinalIgnoreCase)) ?? GetMockDriveItems()[0];

        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = $"https://graph.contoso.com/v1.0/driveItems('{Uri.EscapeDataString(item.Id)}')/lastModifiedByUser?$select=id,displayName,userPrincipalName,mail,jobTitle,department,officeLocation",
            StatusCode = 200,
            ResponseSummary = $"Resolved lastModifiedByUser={item.LastModifiedByDisplayName}",
            IsLive = false
        });
        return Task.FromResult<GraphDriveItem?>(item);
    }

    // Teams & Channels Operations
    public Task<List<GraphTeam>> GetTeamsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        string q = (filter ?? "").Trim();
        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = "https://graph.contoso.com/v1.0/me/joinedTeams?$select=id,displayName,description,visibility,webUrl&$expand=channels",
            StatusCode = 200,
            ResponseSummary = "Joined Microsoft Teams with channels",
            IsLive = false
        });
        var teams = GetMockTeams()
            .Where(t => string.IsNullOrWhiteSpace(q) ||
                        q.Equals("me", StringComparison.OrdinalIgnoreCase) ||
                        q.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                        t.Id.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                        t.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        (t.Description != null && t.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult(teams);
    }

    public Task<GraphChannel?> GetChannelMessagesAsync(string channelIdOrName, CancellationToken cancellationToken = default)
    {
        string q = (channelIdOrName ?? "").Trim();
        var allChannels = GetMockTeams().SelectMany(t => t.Channels).ToList();
        var channel = allChannels.FirstOrDefault(c =>
            string.Equals(c.Id, q, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.DisplayName, q, StringComparison.OrdinalIgnoreCase) ||
            c.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? allChannels[1];

        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = $"https://graph.contoso.com/v1.0/channels('{Uri.EscapeDataString(channel.Id)}')/Microsoft.Graph.deltaChannelMessages()?$top=20",
            StatusCode = 200,
            ResponseSummary = $"Channel '{channel.DisplayName}' messages ({channel.Messages.Count})",
            IsLive = false
        });
        return Task.FromResult<GraphChannel?>(channel);
    }

    public Task<GraphChatMessage> SendChannelMessageAsync(string channelIdOrName, string subject, string bodyContent, CancellationToken cancellationToken = default)
    {
        var allChannels = GetMockTeams().SelectMany(t => t.Channels).ToList();
        var channel = allChannels.FirstOrDefault(c =>
            string.Equals(c.Id, channelIdOrName, StringComparison.OrdinalIgnoreCase) ||
            c.DisplayName.Contains(channelIdOrName ?? "", StringComparison.OrdinalIgnoreCase)) ?? allChannels[1];

        var msg = new GraphChatMessage
        {
            Id = $"msg-posted-{ PostedChannelMessages.Count + 100 }",
            Subject = string.IsNullOrWhiteSpace(subject) ? "MCP 2.0 Status Update" : subject,
            Summary = bodyContent.Length > 80 ? bodyContent[..80] + "..." : bodyContent,
            BodyContent = bodyContent,
            FromDisplayName = "Miriam Graham",
            FromUpn = "miriamg@contoso.com",
            Importance = "high",
            CreatedDateTime = DateTimeOffset.UtcNow,
            WebUrl = $"https://teams.contoso.com/l/message/{channel.Id}/msg-posted"
        };
        lock (PostedChannelMessages)
        {
            PostedChannelMessages.Add(msg);
        }
        AddTrace(new GraphTraceRecord
        {
            Method = "POST",
            Url = $"https://graph.contoso.com/v1.0/channels('{Uri.EscapeDataString(channel.Id)}')/Microsoft.Graph.sendChannelMessage",
            StatusCode = 201,
            ResponseSummary = $"Posted message '{msg.Subject}' to {channel.DisplayName}",
            IsLive = false
        });
        return Task.FromResult(msg);
    }

    // Chats Operations
    public Task<List<GraphChat>> GetChatsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        string q = (filter ?? "").Trim();
        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = "https://graph.contoso.com/v1.0/me/chats?$select=id,topic,chatType,lastUpdatedDateTime,webUrl&$expand=members,messages($top=5)",
            StatusCode = 200,
            ResponseSummary = "Recent 1:1 and group chats",
            IsLive = false
        });
        var chats = GetMockChats()
            .Where(c => string.IsNullOrWhiteSpace(q) ||
                        q.Equals("all", StringComparison.OrdinalIgnoreCase) ||
                        c.Id.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                        c.ChatType.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                        (c.Topic != null && c.Topic.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                        c.Members.Any(m => m.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return Task.FromResult(chats);
    }

    // To Do Tasks Operations
    public Task<List<GraphTodoTaskList>> GetTodoTaskListsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        string q = (filter ?? "").Trim();
        AddTrace(new GraphTraceRecord
        {
            Method = "GET",
            Url = "https://graph.contoso.com/v1.0/me/todoLists?$select=id,displayName,isOwner,isShared,wellknownListName&$expand=tasks",
            StatusCode = 200,
            ResponseSummary = "Microsoft To Do task lists and tasks",
            IsLive = false
        });
        var lists = GetMockTodoLists();
        if (!string.IsNullOrWhiteSpace(q) && !q.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var list in lists)
            {
                list.Tasks = list.Tasks.Where(t =>
                    t.Importance.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Status.Equals(q, StringComparison.OrdinalIgnoreCase) ||
                    t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (t.BodyPreview != null && t.BodyPreview.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (t.AssignedToUpn != null && t.AssignedToUpn.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            }
        }
        return Task.FromResult(lists);
    }

    public Task<GraphTodoTask> CompleteTodoTaskAsync(string taskIdOrTitle, CancellationToken cancellationToken = default)
    {
        string q = (taskIdOrTitle ?? "").Trim();
        var task = GetMockTodoLists().SelectMany(l => l.Tasks).FirstOrDefault(t =>
            string.Equals(t.Id, q, StringComparison.OrdinalIgnoreCase) ||
            t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? GetMockTodoLists()[0].Tasks[0];

        lock (CompletedTaskIds)
        {
            CompletedTaskIds.Add(task.Id);
        }
        task.Status = "completed";
        task.CompletedDateTime = DateTimeOffset.UtcNow;

        AddTrace(new GraphTraceRecord
        {
            Method = "POST",
            Url = $"https://graph.contoso.com/v1.0/todoTasks('{Uri.EscapeDataString(task.Id)}')/Microsoft.Graph.completeTodoTask",
            StatusCode = 200,
            ResponseSummary = $"Completed To Do task '{task.Title}'",
            IsLive = false
        });
        return Task.FromResult(task);
    }

    #region Mock High-Fidelity M365 Dataset
    public static List<GraphUser> GetMockUsers()
    {
        return [
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000001",
                DisplayName = "Miriam Graham",
                UserPrincipalName = "miriamg@contoso.com",
                Mail = "miriamg@contoso.com",
                JobTitle = "PRINCIPAL API ARCHITECT",
                Department = "Architecture & Standards",
                OfficeLocation = "Home Office",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000002",
                DisplayName = "Alex Wilbur",
                UserPrincipalName = "alexw@contoso.com",
                Mail = "alexw@contoso.com",
                JobTitle = "Partner Director Engineering",
                Department = "Engineering Leadership",
                OfficeLocation = "Building 1 / Remote",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000003",
                DisplayName = "Megan Bowen",
                UserPrincipalName = "meganb@contoso.com",
                Mail = "meganb@contoso.com",
                JobTitle = "PRINCIPAL SOFTWARE ARCHITECT",
                Department = "Architecture & Standards",
                OfficeLocation = "Building 2 / 1072",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000004",
                DisplayName = "Adele Vance",
                UserPrincipalName = "adelev@contoso.com",
                Mail = "adelev@contoso.com",
                JobTitle = "PRINCIPAL PRODUCT MANAGER",
                Department = "Product Operations",
                OfficeLocation = "Building 2 / 1084",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000005",
                DisplayName = "Diego Siciliani",
                UserPrincipalName = "diegos@contoso.com",
                Mail = "diegos@contoso.com",
                JobTitle = "PRINCIPAL GROUP ENG MANAGER",
                Department = "Architecture & Standards",
                OfficeLocation = "Building 2 / 1086",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000006",
                DisplayName = "Patti Fernandez",
                UserPrincipalName = "pattif@contoso.com",
                Mail = "pattif@contoso.com",
                JobTitle = "BUSINESS ADMINISTRATOR",
                Department = "Engineering Leadership",
                OfficeLocation = "Building 1 / Remote",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000007",
                DisplayName = "Lynne Robbins",
                UserPrincipalName = "lynner@contoso.com",
                Mail = "lynner@contoso.com",
                JobTitle = "GROUP PM MANAGER",
                Department = "Directory Operations",
                OfficeLocation = "Building 2 / 1072",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000008",
                DisplayName = "Joni Sherman",
                UserPrincipalName = "jonis@contoso.com",
                Mail = "jonis@contoso.com",
                JobTitle = "PRINCIPAL GROUP ENG MGR",
                Department = "Architecture & Standards",
                OfficeLocation = "Building 1",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000009",
                DisplayName = "Henrietta Mueller",
                UserPrincipalName = "henriettam@contoso.com",
                Mail = "henriettam@contoso.com",
                JobTitle = "PARTNER ARCHITECT (OData Co-Chair)",
                Department = "Data & AI",
                OfficeLocation = "Building 2",
                PreferredLanguage = "en-US"
            }
        ];
    }

    public static UserProfileDetail? GetMockUserProfile(string identifier)
    {
        var users = GetMockUsers();
        var user = users.FirstOrDefault(u =>
            string.Equals(identifier, "me", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.Id, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(u.UserPrincipalName, identifier, StringComparison.OrdinalIgnoreCase) ||
            u.DisplayName.Contains(identifier, StringComparison.OrdinalIgnoreCase)) ?? users[0];

        var manager = users.FirstOrDefault(u => u.DisplayName.Contains("Alex", StringComparison.OrdinalIgnoreCase));
        var peers = users.Where(u => u.Id != user.Id && u.Id != manager?.Id).ToList();

        return new UserProfileDetail
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            UserPrincipalName = user.UserPrincipalName,
            Mail = user.Mail,
            JobTitle = user.JobTitle,
            Department = user.Department,
            OfficeLocation = user.OfficeLocation,
            PreferredLanguage = user.PreferredLanguage,
            Manager = (user.Id == manager?.Id) ? null : manager,
            Peers = (user.Id == manager?.Id) ? [] : peers,
            DirectReports = (user.Id == manager?.Id) ? users.Where(u => u.Id != manager.Id).ToList() : [],
            Source = "Microsoft Graph v1.0 (Mock Fallback)"
        };
    }

    public static List<GraphGroup> GetMockGroups()
    {
        return [
            new GraphGroup
            {
                Id = "11111111-2222-3333-4444-555555555555",
                DisplayName = "Identity Architecture",
                Description = "Microsoft Entra ID & Microsoft Graph core architecture, design, and standards",
                Mail = "identityarch@contoso.com",
                MailNickname = "identityarch",
                GroupTypes = ["Unified"],
                MailEnabled = true,
                SecurityEnabled = true,
                Visibility = "Public",
                CreatedDateTime = DateTimeOffset.Parse("2023-01-15T08:00:00Z")
            },
            new GraphGroup
            {
                Id = "22222222-3333-4444-5555-666666666666",
                DisplayName = "Security API Reviewers",
                Description = "Review board for Security and Identity division Graph APIs",
                Mail = "scimreviewers@contoso.com",
                MailNickname = "scimreviewers",
                GroupTypes = [],
                MailEnabled = false,
                SecurityEnabled = true,
                Visibility = "Private",
                CreatedDateTime = DateTimeOffset.Parse("2023-04-10T10:00:00Z")
            },
            new GraphGroup
            {
                Id = "33333333-4444-5555-6666-777777777777",
                DisplayName = "Microsoft Graph Partners",
                Description = "Partner onboarding, documentation, and API gateway guidance",
                Mail = "graphpartners@contoso.com",
                MailNickname = "graphpartners",
                GroupTypes = ["Unified"],
                MailEnabled = true,
                SecurityEnabled = false,
                Visibility = "Public",
                CreatedDateTime = DateTimeOffset.Parse("2023-06-20T14:30:00Z")
            },
            new GraphGroup
            {
                Id = "44444444-5555-6666-7777-888888888888",
                DisplayName = "OData TC Working Group",
                Description = "OASIS OData Technical Committee standards and Agent-Ready extensions",
                Mail = "odatetc@contoso.com",
                MailNickname = "odatetc",
                GroupTypes = ["Unified"],
                MailEnabled = true,
                SecurityEnabled = true,
                Visibility = "Public",
                CreatedDateTime = DateTimeOffset.Parse("2024-02-01T09:00:00Z")
            }
        ];
    }

    public static GroupDetail? GetMockGroupDetails(string identifier)
    {
        var groups = GetMockGroups();
        var users = GetMockUsers();

        var group = groups.FirstOrDefault(g =>
            string.Equals(g.Id, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(g.DisplayName, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(g.MailNickname, identifier, StringComparison.OrdinalIgnoreCase) ||
            g.DisplayName.Contains(identifier, StringComparison.OrdinalIgnoreCase)) ?? groups[0];

        var owners = users.Where(u => u.DisplayName.Contains("Alex") || u.DisplayName.Contains("Michael")).ToList();
        var members = users.Where(u => u.DisplayName.Contains("Miriam") || u.DisplayName.Contains("Megan") || u.DisplayName.Contains("Adele") || u.DisplayName.Contains("Diego Siciliani")).ToList();

        if (group.DisplayName.Contains("Partners"))
        {
            owners = users.Where(u => u.DisplayName.Contains("Miriam")).ToList();
            members = users.Where(u => u.DisplayName.Contains("Lynne") || u.DisplayName.Contains("Patti") || u.DisplayName.Contains("Joni")).ToList();
        }

        return new GroupDetail
        {
            Id = group.Id,
            DisplayName = group.DisplayName,
            Description = group.Description,
            Mail = group.Mail,
            MailNickname = group.MailNickname,
            GroupTypes = group.GroupTypes,
            MailEnabled = group.MailEnabled,
            SecurityEnabled = group.SecurityEnabled,
            Visibility = group.Visibility,
            CreatedDateTime = group.CreatedDateTime,
            Owners = owners,
            Members = members,
            Source = "Microsoft Graph v1.0 (Mock Fallback)"
        };
    }

    public static List<GraphDriveItem> GetMockDriveItems()
    {
        var users = GetMockUsers();
        var miriam = users[0];
        var megan = users[2];
        var henrietta = users[8];
        var adele = users[3];

        return [
            new GraphDriveItem
            {
                Id = "file-mcp-poc-docx",
                Name = "Script_MCP_POC.docx",
                Size = 482910,
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents/Script_MCP_POC.docx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-18T14:20:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-03T18:45:00Z"),
                LastModifiedByDisplayName = miriam.DisplayName,
                LastModifiedByUpn = miriam.UserPrincipalName,
                LastModifiedByUser = miriam
            },
            new GraphDriveItem
            {
                Id = "file-odata-agent-prompts-pptx",
                Name = "OData-v4.01-Agent-Prompts-Proposal.pptx",
                Size = 2841920,
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents/OData-v4.01-Agent-Prompts-Proposal.pptx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-20T09:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-04T09:15:00Z"),
                LastModifiedByDisplayName = henrietta.DisplayName,
                LastModifiedByUpn = henrietta.UserPrincipalName,
                LastModifiedByUser = henrietta
            },
            new GraphDriveItem
            {
                Id = "file-csdl-consistency-xlsx",
                Name = "Graph-CSDL-Consistency-Benchmark.xlsx",
                Size = 918240,
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents/Graph-CSDL-Consistency-Benchmark.xlsx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-24T11:30:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-02T16:10:00Z"),
                LastModifiedByDisplayName = megan.DisplayName,
                LastModifiedByUpn = megan.UserPrincipalName,
                LastModifiedByUser = megan
            },
            new GraphDriveItem
            {
                Id = "file-mcp-20-spec-pdf",
                Name = "MCP-2.0-Specification-2026-07-28-Notes.pdf",
                Size = 1540320,
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents/MCP-2.0-Specification-2026-07-28-Notes.pdf",
                FileMimeType = "application/pdf",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-08-12T10:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-10-29T13:25:00Z"),
                LastModifiedByDisplayName = adele.DisplayName,
                LastModifiedByUpn = adele.UserPrincipalName,
                LastModifiedByUser = adele
            },
            new GraphDriveItem
            {
                Id = "folder-architecture-specs",
                Name = "Architecture-Specs",
                Size = 0,
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents/Architecture-Specs",
                FileMimeType = "inode/directory",
                IsFolder = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-01-10T08:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-01T12:00:00Z"),
                LastModifiedByDisplayName = miriam.DisplayName,
                LastModifiedByUpn = miriam.UserPrincipalName,
                LastModifiedByUser = miriam
            }
        ];
    }

    public static List<GraphDrive> GetMockDrives()
    {
        return [
            new GraphDrive
            {
                Id = "drive-miriam",
                Name = "OneDrive - Miriam Graham",
                DriveType = "business",
                WebUrl = "https://files.contoso.com/personal/miriamg/Documents",
                OwnerDisplayName = "Miriam Graham",
                QuotaTotal = 1099511627776,
                QuotaUsed = 4829103920,
                Items = GetMockDriveItems()
            }
        ];
    }

    public static List<GraphTeam> GetMockTeams()
    {
        var chGeneral = new GraphChannel
        {
            Id = "ch-idarch-general",
            TeamId = "11111111-2222-3333-4444-555555555555",
            DisplayName = "General",
            Description = "General architecture announcements and weekly sync updates",
            MembershipType = "standard",
            Email = "identityarch-general@contoso.com",
            WebUrl = "https://teams.contoso.com/l/channel/ch-idarch-general",
            CreatedDateTime = DateTimeOffset.Parse("2023-01-15T08:05:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-101",
                    Subject = "Weekly Architecture Review Agenda",
                    Summary = "Reviewing OData v4.01 $metadata alignment and CSDL consistency gates.",
                    BodyContent = "Team — on Thursday we will review the OData v4.01 $metadata endpoint conventions and the new @Agent.Prompts / @Agent.Examples annotations.",
                    FromDisplayName = "Alex Wilbur",
                    FromUpn = "alexw@contoso.com",
                    Importance = "normal",
                    CreatedDateTime = DateTimeOffset.Parse("2026-11-03T15:00:00Z")
                }
            ]
        };

        var chCsdl = new GraphChannel
        {
            Id = "ch-csdl-typespec",
            TeamId = "11111111-2222-3333-4444-555555555555",
            DisplayName = "CSDL & TypeSpec",
            Description = "CSDL schema projection, TypeSpec migration, and @Agent.Examples annotations",
            MembershipType = "standard",
            Email = "csdl-typespec@contoso.com",
            WebUrl = "https://teams.contoso.com/l/channel/ch-csdl-typespec",
            CreatedDateTime = DateTimeOffset.Parse("2024-03-10T10:00:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-201",
                    Subject = "Phase 1 vs Phase 2 CSDL Annotations",
                    Summary = "@Agent.Prompts on EntityContainer (+36.1 pp routing gain) + @Agent.Examples on Entities & Operations",
                    BodyContent = "Phase 1 (@Agent.Prompts on GraphService) raised verified routing accuracy from 55.7% to 91.8%. For LargeServer we added @Agent.Examples on EntityTypes, EntitySets, Functions, and Actions covering Files, Teams, Chats, and To Do.",
                    FromDisplayName = "Miriam Graham",
                    FromUpn = "miriamg@contoso.com",
                    Importance = "high",
                    CreatedDateTime = DateTimeOffset.Parse("2026-11-04T10:30:00Z")
                },
                new GraphChatMessage
                {
                    Id = "msg-202",
                    Subject = "Bound OData Functions & Actions in CSDL",
                    Summary = "Added searchDriveItems, recentDriveItems, deltaChannelMessages, sendChannelMessage, completeTodoTask",
                    BodyContent = "Confirmed that bound Functions and Actions in Microsoft.Graph validate cleanly with Microsoft.OData.Edm 9.0.0 and project deterministically to MCP tools.",
                    FromDisplayName = "Megan Bowen",
                    FromUpn = "meganb@contoso.com",
                    Importance = "high",
                    CreatedDateTime = DateTimeOffset.Parse("2026-11-04T11:45:00Z")
                }
            ]
        };

        var chMcp = new GraphChannel
        {
            Id = "ch-mcp-20",
            TeamId = "11111111-2222-3333-4444-555555555555",
            DisplayName = "MCP 2.0 Integration",
            Description = "Model Context Protocol 2026-07-28 stateless HTTP transport and discovery",
            MembershipType = "standard",
            Email = "mcp20@contoso.com",
            WebUrl = "https://teams.contoso.com/l/channel/ch-mcp-20",
            CreatedDateTime = DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-301",
                    Subject = "Stateless server/discover & Multi-Workload Projection",
                    Summary = "Removed deprecated initialize; added M365 workloads (Files, Teams, Chats, To Do) on port 3003.",
                    BodyContent = "LargeServer (:3003) exposes 14 tools, 13 resources/templates, 22 @Agent.Prompts, and 28 @Agent.Examples directly projected from Server_Large.csdl.",
                    FromDisplayName = "Adele Vance",
                    FromUpn = "adelev@contoso.com",
                    Importance = "high",
                    CreatedDateTime = DateTimeOffset.Parse("2026-11-04T14:15:00Z")
                }
            ]
        };

        lock (PostedChannelMessages)
        {
            chMcp.Messages.AddRange(PostedChannelMessages);
        }

        return [
            new GraphTeam
            {
                Id = "11111111-2222-3333-4444-555555555555",
                DisplayName = "Identity Architecture",
                Description = "Microsoft Entra ID & Microsoft Graph core architecture, design, and standards",
                Visibility = "Public",
                IsArchived = false,
                WebUrl = "https://teams.contoso.com/l/team/11111111-2222-3333-4444-555555555555",
                CreatedDateTime = DateTimeOffset.Parse("2023-01-15T08:00:00Z"),
                Channels = [chGeneral, chCsdl, chMcp]
            },
            new GraphTeam
            {
                Id = "44444444-5555-6666-7777-888888888888",
                DisplayName = "OData TC Working Group",
                Description = "OASIS OData Technical Committee standards and Agent-Ready extensions",
                Visibility = "Public",
                IsArchived = false,
                WebUrl = "https://teams.contoso.com/l/team/44444444-5555-6666-7777-888888888888",
                CreatedDateTime = DateTimeOffset.Parse("2024-02-01T09:00:00Z"),
                Channels = [
                    new GraphChannel
                    {
                        Id = "ch-odata-vocab",
                        TeamId = "44444444-5555-6666-7777-888888888888",
                        DisplayName = "Agent-Ready Vocabularies",
                        Description = "OASIS OData TC proposal for @Agent.Prompts and @Agent.Examples",
                        MembershipType = "standard",
                        Email = "odata-agent-vocab@contoso.com",
                        WebUrl = "https://teams.contoso.com/l/channel/ch-odata-vocab",
                        CreatedDateTime = DateTimeOffset.Parse("2024-02-05T10:00:00Z"),
                        Messages = [
                            new GraphChatMessage
                            {
                                Id = "msg-401",
                                Subject = "OData v4.01 $metadata + Agent Vocabulary Alignment",
                                Summary = "Keeping CSDL pure: project tools/resources from native EntitySets & Operations; use @Agent.Prompts and @Agent.Examples.",
                                BodyContent = "Updated the proposal deck in OneDrive (OData-v4.01-Agent-Prompts-Proposal.pptx) to show both EntityContainer @Agent.Prompts and entity/operation @Agent.Examples.",
                                FromDisplayName = "Henrietta Mueller",
                                FromUpn = "henriettam@contoso.com",
                                Importance = "high",
                                CreatedDateTime = DateTimeOffset.Parse("2026-11-04T09:20:00Z")
                            }
                        ]
                    }
                ]
            }
        ];
    }

    public static List<GraphChat> GetMockChats()
    {
        var users = GetMockUsers();
        var miriam = users[0];
        var alex = users[1];
        var megan = users[2];
        var henrietta = users[8];

        return [
            new GraphChat
            {
                Id = "chat-miriam-megan",
                Topic = "CSDL LargeServer Schema & TypeSpec Sync",
                ChatType = "oneOnOne",
                CreatedDateTime = DateTimeOffset.Parse("2026-10-15T09:00:00Z"),
                LastUpdatedDateTime = DateTimeOffset.Parse("2026-11-04T13:10:00Z"),
                WebUrl = "https://teams.contoso.com/l/chat/chat-miriam-megan",
                Members = [miriam, megan],
                Messages = [
                    new GraphChatMessage
                    {
                        Id = "cmsg-1",
                        Subject = "DriveItem & TodoTask CSDL entities",
                        BodyContent = "Hey Miriam, I just updated Graph-CSDL-Consistency-Benchmark.xlsx in OneDrive. All 10 EntityTypes and 5 bound operations in Server_Large.csdl validate cleanly.",
                        FromDisplayName = megan.DisplayName,
                        FromUpn = megan.UserPrincipalName,
                        Importance = "normal",
                        CreatedDateTime = DateTimeOffset.Parse("2026-11-04T13:05:00Z")
                    },
                    new GraphChatMessage
                    {
                        Id = "cmsg-2",
                        Subject = "Re: DriveItem & TodoTask CSDL entities",
                        BodyContent = "Awesome, thanks Megan! Wiring up the Large MCP Explorer tab on :3000 now so we can demo Users, Groups, Files, Teams, Chats, and To Do end-to-end.",
                        FromDisplayName = miriam.DisplayName,
                        FromUpn = miriam.UserPrincipalName,
                        Importance = "normal",
                        CreatedDateTime = DateTimeOffset.Parse("2026-11-04T13:10:00Z")
                    }
                ]
            },
            new GraphChat
            {
                Id = "chat-odata-leads",
                Topic = "OData TC & MCP 2.0 Demo Dry Run",
                ChatType = "group",
                CreatedDateTime = DateTimeOffset.Parse("2026-10-28T16:00:00Z"),
                LastUpdatedDateTime = DateTimeOffset.Parse("2026-11-04T15:20:00Z"),
                WebUrl = "https://teams.contoso.com/l/chat/chat-odata-leads",
                Members = [miriam, alex, henrietta],
                Messages = [
                    new GraphChatMessage
                    {
                        Id = "cmsg-3",
                        Subject = "Demo Readiness Check",
                        BodyContent = "Make sure we highlight how @Agent.Prompts on the container and @Agent.Examples on entities/operations work together without polluting CSDL with tool definitions.",
                        FromDisplayName = henrietta.DisplayName,
                        FromUpn = henrietta.UserPrincipalName,
                        Importance = "high",
                        CreatedDateTime = DateTimeOffset.Parse("2026-11-04T15:20:00Z")
                    }
                ]
            }
        ];
    }

    public static List<GraphTodoTaskList> GetMockTodoLists()
    {
        var tasks1 = new List<GraphTodoTask>
        {
            new GraphTodoTask
            {
                Id = "task-101",
                ListId = "list-mcp-poc",
                ListDisplayName = "Microsoft Graph & MCP 2.0 POC",
                Title = "Author Server_Large.csdl with Files, Teams Channels, Chats, and To Do Tasks",
                BodyPreview = "Include 10 EntityTypes, bound Functions/Actions, 22 @Agent.Prompts, and 28 @Agent.Examples.",
                Status = CompletedTaskIds.Contains("task-101") ? "completed" : "inProgress",
                Importance = "high",
                IsReminderOn = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-11-01T09:00:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-05T17:00:00Z"),
                AssignedToUpn = "miriamg@contoso.com"
            },
            new GraphTodoTask
            {
                Id = "task-102",
                ListId = "list-mcp-poc",
                ListDisplayName = "Microsoft Graph & MCP 2.0 POC",
                Title = "Review Graph-CSDL-Consistency-Benchmark.xlsx with Megan Bowen",
                BodyPreview = "Verify multi-workload routing metrics and bound function parameter bindings.",
                Status = CompletedTaskIds.Contains("task-102") ? "completed" : "notStarted",
                Importance = "high",
                IsReminderOn = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-11-02T11:00:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-06T17:00:00Z"),
                AssignedToUpn = "meganb@contoso.com"
            },
            new GraphTodoTask
            {
                Id = "task-103",
                ListId = "list-mcp-poc",
                ListDisplayName = "Microsoft Graph & MCP 2.0 POC",
                Title = "Publish OData-v4.01-Agent-Prompts-Proposal.pptx to OData TC channel",
                BodyPreview = "Share final slide deck with Henrietta Mueller and Alex Wilbur.",
                Status = "completed",
                Importance = "normal",
                IsReminderOn = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-29T10:00:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-03T17:00:00Z"),
                CompletedDateTime = DateTimeOffset.Parse("2026-11-03T16:30:00Z"),
                AssignedToUpn = "miriamg@contoso.com"
            }
        };

        var tasks2 = new List<GraphTodoTask>
        {
            new GraphTodoTask
            {
                Id = "task-201",
                ListId = "list-api-reviews",
                ListDisplayName = "Graph API Council Action Items",
                Title = "Validate @Agent.Examples annotations on bound Functions and Actions",
                BodyPreview = "Ensure searchDriveItems, recentDriveItems, deltaChannelMessages, sendChannelMessage, and completeTodoTask expose canonical OData v4.01 examples.",
                Status = CompletedTaskIds.Contains("task-201") ? "completed" : "inProgress",
                Importance = "high",
                IsReminderOn = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-11-03T08:30:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-07T17:00:00Z"),
                AssignedToUpn = "miriamg@contoso.com"
            }
        };

        return [
            new GraphTodoTaskList
            {
                Id = "list-mcp-poc",
                DisplayName = "Microsoft Graph & MCP 2.0 POC",
                IsOwner = true,
                IsShared = true,
                WellknownListName = "defaultList",
                Tasks = tasks1
            },
            new GraphTodoTaskList
            {
                Id = "list-api-reviews",
                DisplayName = "Graph API Council Action Items",
                IsOwner = true,
                IsShared = true,
                WellknownListName = "none",
                Tasks = tasks2
            }
        ];
    }
    #endregion
}


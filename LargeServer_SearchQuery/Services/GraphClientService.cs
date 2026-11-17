using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using GraphMcpServer.Models;

namespace GraphMcpServer.Services;

public class GraphClientService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GraphClientService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresOn = DateTimeOffset.MinValue;
    private readonly List<GraphTraceRecord> _traces = [];
    private readonly object _traceLock = new();

    private static readonly List<GraphChatMessage> PostedChannelMessages = [];
    private static readonly HashSet<string> CompletedTaskIds = new(StringComparer.OrdinalIgnoreCase);

    public GraphClientService(
        HttpClient httpClient,
        ILogger<GraphClientService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public List<GraphTraceRecord> GetRecentTraces()
    {
        lock (_traceLock)
        {
            return _traces.TakeLast(100).ToList();
        }
    }

    private void AddTrace(GraphTraceRecord trace)
    {
        trace.CorrelationId ??= _httpContextAccessor.HttpContext?.Request.Headers[
            "X-Experiment-Correlation-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(trace.CorrelationId))
        {
            trace.ClientRequestId ??= GetClientRequestId(trace.CorrelationId).ToString();
            trace.RequestHeaders = string.Join(
                "\n",
                new[]
                {
                    trace.RequestHeaders,
                    $"X-Experiment-Correlation-Id: {trace.CorrelationId}",
                    $"client-request-id: {trace.ClientRequestId}",
                    $"user-agent: GraphMcpExperiment/1.0 correlation-id/{trace.CorrelationId}",
                    "return-client-request-id: true"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        lock (_traceLock)
        {
            _traces.Add(trace);
            if (_traces.Count > 100) _traces.RemoveAt(0);
        }
    }

    private async Task<(bool Success, JsonElement? Payload)> SendGraphAsync(
        HttpMethod method,
        string requestUrl,
        string token,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, requestUrl);
        ApplyGraphRequestHeaders(request, token);
        if (body != null)
            request.Content = JsonContent.Create(body);

        var trace = new GraphTraceRecord
        {
            Method = method.Method,
            Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
            RequestHeaders = "Authorization: Bearer [REDACTED]\nAccept: application/json",
            IsLive = true
        };

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            trace.StatusCode = (int)response.StatusCode;
            CaptureResponseHeaders(response, trace);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            trace.ResponseSummary = content.Length > 200 ? content[..200] + "..." : content;
            AddTrace(trace);
            if (!response.IsSuccessStatusCode)
                return (false, null);
            if (string.IsNullOrWhiteSpace(content))
                return (true, null);
            using var document = JsonDocument.Parse(content);
            return (true, document.RootElement.Clone());
        }

        catch (Exception exception)
        {
            trace.StatusCode = 0;
            trace.ResponseSummary = exception.Message;
            AddTrace(trace);
            _logger.LogWarning(exception, "Microsoft Graph request failed: {Method} {RequestUrl}", method, requestUrl);
            return (false, null);
        }
    }

    private void ApplyGraphRequestHeaders(HttpRequestMessage request, string token)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };
        request.Headers.Pragma.ParseAdd("no-cache");
        var correlationId = _httpContextAccessor.HttpContext?.Request.Headers[
            "X-Experiment-Correlation-Id"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Experiment-Correlation-Id", correlationId);
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                $"GraphMcpExperiment/1.0 correlation-id/{correlationId}");
        }
        var clientRequestId = GetClientRequestId(correlationId);
        request.Headers.TryAddWithoutValidation("client-request-id", clientRequestId.ToString());
        request.Headers.TryAddWithoutValidation("return-client-request-id", "true");
    }

    private static Guid GetClientRequestId(string? correlationId)
    {
        if (Guid.TryParse(correlationId, out var parsed))
            return parsed;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(correlationId ?? Guid.NewGuid().ToString()));
        return new Guid(hash[..16]);
    }

    private static void CaptureResponseHeaders(HttpResponseMessage response, GraphTraceRecord trace)
    {
        trace.GraphRequestId = response.Headers.TryGetValues("request-id", out var requestIds)
            ? requestIds.FirstOrDefault()
            : null;
        var clientRequestId = response.Headers.TryGetValues("client-request-id", out var clientRequestIds)
            ? clientRequestIds.FirstOrDefault()
            : null;
        trace.ResponseHeaders = string.Join(
            "\n",
            new[]
            {
                trace.GraphRequestId == null ? null : $"request-id: {trace.GraphRequestId}",
                clientRequestId == null ? null : $"client-request-id: {clientRequestId}",
                response.Headers.Date.HasValue ? $"date: {response.Headers.Date.Value:R}" : null,
                response.Headers.CacheControl == null ? null : $"cache-control: {response.Headers.CacheControl}"
            }.Where(value => !string.IsNullOrWhiteSpace(value)));
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
                new TokenRequestContext(["https://graph.microsoft.com/.default"]),
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
                ApplyGraphRequestHeaders(req, token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: Bearer [REDACTED]\nAccept: application/json",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);

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
                    Url = $"https://graph.microsoft.com/v1.0/users?$filter=... [FALLBACK]",
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
                ApplyGraphRequestHeaders(req, token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: Bearer [REDACTED]\nAccept: application/json",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);

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
            ApplyGraphRequestHeaders(req, token);

            var trace = new GraphTraceRecord
            {
                Method = "GET",
                Url = $"https://graph.microsoft.com/v1.0/{url}",
                RequestHeaders = "Authorization: Bearer [REDACTED]",
                IsLive = true
            };

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            trace.StatusCode = (int)res.StatusCode;
            CaptureResponseHeaders(res, trace);
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
            ApplyGraphRequestHeaders(req, token);

            var trace = new GraphTraceRecord
            {
                Method = "GET",
                Url = $"https://graph.microsoft.com/v1.0/{url}",
                RequestHeaders = "Authorization: Bearer [REDACTED]",
                IsLive = true
            };

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            trace.StatusCode = (int)res.StatusCode;
            CaptureResponseHeaders(res, trace);
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
                ApplyGraphRequestHeaders(req, token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);

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
                    Url = $"https://graph.microsoft.com/v1.0/groups?$filter=... [FALLBACK]",
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
                ApplyGraphRequestHeaders(req, token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);
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
                ApplyGraphRequestHeaders(req, token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
                    RequestHeaders = "Authorization: ******",
                    IsLive = true
                };

                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);
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
            ApplyGraphRequestHeaders(req, token);
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
        if (cleanPath.StartsWith("https://graph.microsoft.com/v1.0/", StringComparison.OrdinalIgnoreCase))
            cleanPath = cleanPath["https://graph.microsoft.com/v1.0/".Length..];
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
                ApplyGraphRequestHeaders(req, token);
                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{cleanPath}",
                    RequestHeaders = "Authorization: ****** application/json",
                    IsLive = true
                };
                using var res = await _httpClient.SendAsync(req, cancellationToken);
                trace.StatusCode = (int)res.StatusCode;
                CaptureResponseHeaders(res, trace);
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
    public async Task<GraphDrive> GetDriveAsync(string userOrDriveId = "me", CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var root = userOrDriveId.Equals("me", StringComparison.OrdinalIgnoreCase)
                ? "me/drive"
                : $"drives/{Uri.EscapeDataString(userOrDriveId)}";
            var driveResult = await SendGraphAsync(
                HttpMethod.Get, $"{root}?$select=id,name,driveType,webUrl,owner,quota",
                token, null, cancellationToken);
            var itemsResult = await SendGraphAsync(
                HttpMethod.Get,
                $"{root}/root/children?$select=id,name,size,webUrl,file,folder,createdDateTime,lastModifiedDateTime,lastModifiedBy",
                token, null, cancellationToken);
            if (driveResult.Success && driveResult.Payload is JsonElement driveJson &&
                itemsResult.Success && itemsResult.Payload is JsonElement itemsJson)
            {
                var drive = new GraphDrive
                {
                    Id = GetString(driveJson, "id"),
                    Name = GetString(driveJson, "name"),
                    DriveType = GetString(driveJson, "driveType", "business"),
                    WebUrl = GetNullableString(driveJson, "webUrl"),
                    OwnerDisplayName = GetNestedString(driveJson, "owner", "user", "displayName"),
                    QuotaTotal = GetNestedInt64(driveJson, "quota", "total"),
                    QuotaUsed = GetNestedInt64(driveJson, "quota", "used"),
                    Items = ParseDriveItems(itemsJson)
                };
                return drive;
            }
        }

        throw new HttpRequestException("Live Microsoft Graph OneDrive request failed. Check Files.Read permissions and OneDrive provisioning.");
    }

    public async Task<List<GraphDriveItem>> SearchDriveItemsAsync(string query, CancellationToken cancellationToken = default)
    {
        var q = (query ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var escaped = Uri.EscapeDataString(q.Replace("'", "''", StringComparison.Ordinal));
            var result = await SendGraphAsync(
                HttpMethod.Get,
                $"me/drive/root/search(q='{escaped}')?$select=id,name,size,webUrl,file,folder,createdDateTime,lastModifiedDateTime,lastModifiedBy",
                token, null, cancellationToken);
            if (result.Success && result.Payload is JsonElement json)
                return ParseDriveItems(json);
        }

        throw new HttpRequestException("Live Microsoft Graph file search failed. Check Files.Read permissions and OneDrive provisioning.");
    }

    public async Task<GraphDriveItem?> GetDriveItemWithModifierAsync(string fileIdOrName, CancellationToken cancellationToken = default)
    {
        var target = (fileIdOrName ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            GraphDriveItem? item = null;
            var direct = await SendGraphAsync(
                HttpMethod.Get,
                $"me/drive/items/{Uri.EscapeDataString(target)}?$select=id,name,size,webUrl,file,folder,createdDateTime,lastModifiedDateTime,lastModifiedBy",
                token, null, cancellationToken);
            if (direct.Success && direct.Payload is JsonElement directJson)
                item = ParseDriveItem(directJson);
            if (item == null)
                item = (await SearchDriveItemsAsync(target, cancellationToken)).FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, target, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Name.Contains(target, StringComparison.OrdinalIgnoreCase));
            if (item != null && !string.IsNullOrWhiteSpace(item.LastModifiedByUser?.Id))
                item.LastModifiedByUser = await GetUserProfileAsync(item.LastModifiedByUser.Id, cancellationToken);
            if (item != null)
                return item;
        }

        throw new HttpRequestException("Live Microsoft Graph drive item lookup failed. Check the item identifier and Files.Read permissions.");
    }

    // Teams & Channels Operations
    public async Task<List<GraphTeam>> GetTeamsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var q = (filter ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var result = await SendGraphAsync(
                HttpMethod.Get,
                "me/joinedTeams?$select=id,displayName,description,visibility,isArchived,webUrl,createdDateTime",
                token, null, cancellationToken);
            if (result.Success && result.Payload is JsonElement json)
            {
                var teams = new List<GraphTeam>();
                foreach (var teamJson in GetValues(json))
                {
                    teams.Add(new GraphTeam
                    {
                        Id = GetString(teamJson, "id"),
                        DisplayName = GetString(teamJson, "displayName"),
                        Description = GetNullableString(teamJson, "description"),
                        Visibility = GetString(teamJson, "visibility", "Public"),
                        IsArchived = GetBoolean(teamJson, "isArchived"),
                        WebUrl = GetNullableString(teamJson, "webUrl"),
                        CreatedDateTime = GetDateTimeOffset(teamJson, "createdDateTime")
                    });
                }
                var filtered = FilterTeams(teams, q);
                var teamsToHydrate = filtered.Take(10).ToList();
                await Task.WhenAll(teamsToHydrate.Select(async team =>
                {
                    var channelsResult = await SendGraphAsync(
                        HttpMethod.Get,
                        $"teams/{Uri.EscapeDataString(team.Id)}/channels?$select=id,displayName,description,membershipType,email,webUrl,createdDateTime",
                        token, null, cancellationToken);
                    if (channelsResult.Success && channelsResult.Payload is JsonElement channelsJson)
                    {
                        team.Channels = GetValues(channelsJson).Select(channel => ParseChannel(channel, team.Id)).ToList();
                    }
                }));
                return filtered;
            }
        }

        throw new HttpRequestException("Live Microsoft Graph joined Teams request failed. Check Team.ReadBasic.All permissions.");
    }

    public async Task<GraphChannel?> GetChannelMessagesAsync(string channelIdOrName, CancellationToken cancellationToken = default)
    {
        var q = (channelIdOrName ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var channel = (await GetTeamsAsync("all", cancellationToken))
                .SelectMany(team => team.Channels)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Id, q, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.DisplayName, q, StringComparison.OrdinalIgnoreCase) ||
                    candidate.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (channel != null)
            {
                var result = await SendGraphAsync(
                    HttpMethod.Get,
                    $"teams/{Uri.EscapeDataString(channel.TeamId)}/channels/{Uri.EscapeDataString(channel.Id)}/messages?$top=20",
                    token, null, cancellationToken);
                if (result.Success && result.Payload is JsonElement json)
                {
                    channel.Messages = GetValues(json).Select(ParseChatMessage).ToList();
                    return channel;
                }
            }
        }

        throw new HttpRequestException("Live Microsoft Graph channel lookup failed. Check Channel.ReadBasic.All and ChannelMessage.Read.All permissions.");
    }

    public async Task<GraphChatMessage> SendChannelMessageAsync(
        string channelIdOrName,
        string subject,
        string bodyContent,
        CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph authentication is required to send a channel message.");
        var channel = (await GetTeamsAsync("all", cancellationToken))
            .SelectMany(team => team.Channels)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, channelIdOrName, StringComparison.OrdinalIgnoreCase) ||
                candidate.DisplayName.Contains(channelIdOrName ?? "", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Channel '{channelIdOrName}' was not found.");
        var result = await SendGraphAsync(
            HttpMethod.Post,
            $"teams/{Uri.EscapeDataString(channel.TeamId)}/channels/{Uri.EscapeDataString(channel.Id)}/messages",
            token,
            new
            {
                subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
                body = new { contentType = "html", content = bodyContent }
            },
            cancellationToken);
        if (!result.Success || result.Payload is not JsonElement json)
            throw new HttpRequestException("Microsoft Graph did not accept the channel message.");
        return ParseChatMessage(json);
    }

    // Chats Operations
    public async Task<List<GraphChat>> GetChatsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var q = (filter ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var result = await SendGraphAsync(
                HttpMethod.Get,
                "me/chats?$select=id,topic,chatType,createdDateTime,lastUpdatedDateTime,webUrl&$expand=members",
                token, null, cancellationToken);
            if (result.Success && result.Payload is JsonElement json)
            {
                var chats = new List<GraphChat>();
                foreach (var chatJson in GetValues(json))
                {
                    chats.Add(new GraphChat
                    {
                        Id = GetString(chatJson, "id"),
                        Topic = GetNullableString(chatJson, "topic"),
                        ChatType = GetString(chatJson, "chatType", "oneOnOne"),
                        CreatedDateTime = GetDateTimeOffset(chatJson, "createdDateTime"),
                        LastUpdatedDateTime = GetDateTimeOffset(chatJson, "lastUpdatedDateTime"),
                        WebUrl = GetNullableString(chatJson, "webUrl"),
                        Members = ParseChatMembers(chatJson)
                    });
                }
                var filtered = FilterChats(chats, q).Take(20).ToList();
                await Task.WhenAll(filtered.Take(10).Select(async chat =>
                {
                    var messages = await SendGraphAsync(
                        HttpMethod.Get,
                        $"chats/{Uri.EscapeDataString(chat.Id)}/messages?$top=5",
                        token, null, cancellationToken);
                    if (messages.Success && messages.Payload is JsonElement messagesJson)
                        chat.Messages = GetValues(messagesJson).Select(ParseChatMessage).ToList();
                }));
                return filtered;
            }
        }

        throw new HttpRequestException("Live Microsoft Graph chats request failed. The signed-in token requires Chat.ReadBasic or Chat.Read.");
    }

    // To Do Tasks Operations
    public async Task<List<GraphTodoTaskList>> GetTodoTaskListsAsync(string? filter = null, CancellationToken cancellationToken = default)
    {
        var q = (filter ?? "").Trim();
        var token = await GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(token))
        {
            var result = await SendGraphAsync(
                HttpMethod.Get,
                "me/todo/lists?$select=id,displayName,isOwner,isShared,wellknownListName",
                token, null, cancellationToken);
            if (result.Success && result.Payload is JsonElement json)
            {
                var lists = new List<GraphTodoTaskList>();
                foreach (var listJson in GetValues(json))
                {
                    var list = new GraphTodoTaskList
                    {
                        Id = GetString(listJson, "id"),
                        DisplayName = GetString(listJson, "displayName"),
                        IsOwner = GetBoolean(listJson, "isOwner", true),
                        IsShared = GetBoolean(listJson, "isShared"),
                        WellknownListName = GetString(listJson, "wellknownListName", "none")
                    };
                    var tasks = await SendGraphAsync(
                        HttpMethod.Get,
                        $"me/todo/lists/{Uri.EscapeDataString(list.Id)}/tasks",
                        token, null, cancellationToken);
                    if (tasks.Success && tasks.Payload is JsonElement tasksJson)
                    {
                        list.Tasks = GetValues(tasksJson)
                            .Select(task => ParseTodoTask(task, list.Id, list.DisplayName))
                            .ToList();
                    }
                    lists.Add(list);
                }
                FilterTodoTasks(lists, q);
                return lists;
            }
        }

        throw new HttpRequestException("Live Microsoft Graph To Do request failed. Check Tasks.Read permissions and service availability.");
    }

    public async Task<GraphTodoTask> CompleteTodoTaskAsync(string taskIdOrTitle, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken)
            ?? throw new InvalidOperationException("Microsoft Graph authentication is required to complete a task.");
        var q = (taskIdOrTitle ?? "").Trim();
        var task = (await GetTodoTaskListsAsync("all", cancellationToken))
            .SelectMany(list => list.Tasks)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Id, q, StringComparison.OrdinalIgnoreCase) ||
                candidate.Title.Contains(q, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"To Do task '{taskIdOrTitle}' was not found.");
        var result = await SendGraphAsync(
            HttpMethod.Patch,
            $"me/todo/lists/{Uri.EscapeDataString(task.ListId)}/tasks/{Uri.EscapeDataString(task.Id)}",
            token, new { status = "completed" }, cancellationToken);
        if (!result.Success || result.Payload is not JsonElement json)
            throw new HttpRequestException("Microsoft Graph did not complete the To Do task.");
        return ParseTodoTask(json, task.ListId, task.ListDisplayName);
    }

    private static IReadOnlyList<JsonElement> GetValues(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("value", out var value) &&
            value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray().Select(item => item.Clone()).ToList();
        return [];
    }

    private static string GetString(JsonElement element, string propertyName, string fallback = "")
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback
            : fallback;

    private static string? GetNullableString(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool GetBoolean(JsonElement element, string propertyName, bool fallback = false)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(propertyName, out var value) &&
           value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement element, string propertyName)
        => DateTimeOffset.TryParse(GetString(element, propertyName), out var value) ? value : null;

    private static string? GetNestedString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static long GetNestedInt64(JsonElement element, string parent, string propertyName)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(parent, out var parentValue) &&
           parentValue.ValueKind == JsonValueKind.Object &&
           parentValue.TryGetProperty(propertyName, out var value) &&
           value.TryGetInt64(out var number)
            ? number
            : 0;

    private static List<GraphDriveItem> ParseDriveItems(JsonElement root)
        => GetValues(root).Select(ParseDriveItem).ToList();

    private static GraphDriveItem ParseDriveItem(JsonElement item)
    {
        var modifierId = GetNestedString(item, "lastModifiedBy", "user", "id");
        var modifierName = GetNestedString(item, "lastModifiedBy", "user", "displayName");
        return new GraphDriveItem
        {
            Id = GetString(item, "id"),
            Name = GetString(item, "name"),
            Size = item.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
            WebUrl = GetNullableString(item, "webUrl"),
            FileMimeType = GetNestedString(item, "file", "mimeType"),
            IsFolder = item.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.Object,
            CreatedDateTime = GetDateTimeOffset(item, "createdDateTime"),
            LastModifiedDateTime = GetDateTimeOffset(item, "lastModifiedDateTime"),
            LastModifiedByDisplayName = modifierName,
            LastModifiedByUser = string.IsNullOrWhiteSpace(modifierId) && string.IsNullOrWhiteSpace(modifierName)
                ? null
                : new GraphUser { Id = modifierId ?? "", DisplayName = modifierName ?? "" }
        };
    }

    private static GraphChannel ParseChannel(JsonElement channel, string teamId) => new()
    {
        Id = GetString(channel, "id"),
        TeamId = teamId,
        DisplayName = GetString(channel, "displayName"),
        Description = GetNullableString(channel, "description"),
        MembershipType = GetString(channel, "membershipType", "standard"),
        Email = GetNullableString(channel, "email"),
        WebUrl = GetNullableString(channel, "webUrl"),
        CreatedDateTime = GetDateTimeOffset(channel, "createdDateTime")
    };

    private static GraphChatMessage ParseChatMessage(JsonElement message) => new()
    {
        Id = GetString(message, "id"),
        Subject = GetNullableString(message, "subject"),
        Summary = GetNullableString(message, "summary"),
        BodyContent = GetNestedString(message, "body", "content") ?? "",
        FromDisplayName = GetNestedString(message, "from", "user", "displayName") ?? "",
        FromUpn = GetNestedString(message, "from", "user", "userIdentityType") ?? "",
        Importance = GetString(message, "importance", "normal"),
        CreatedDateTime = GetDateTimeOffset(message, "createdDateTime"),
        WebUrl = GetNullableString(message, "webUrl")
    };

    private static List<GraphUser> ParseChatMembers(JsonElement chat)
    {
        if (!chat.TryGetProperty("members", out var members) || members.ValueKind != JsonValueKind.Array)
            return [];
        return members.EnumerateArray().Select(member => new GraphUser
        {
            Id = GetString(member, "userId", GetString(member, "id")),
            DisplayName = GetString(member, "displayName"),
            UserPrincipalName = GetString(member, "email"),
            Mail = GetNullableString(member, "email")
        }).ToList();
    }

    private static GraphTodoTask ParseTodoTask(JsonElement task, string listId, string listName) => new()
    {
        Id = GetString(task, "id"),
        ListId = listId,
        ListDisplayName = listName,
        Title = GetString(task, "title"),
        BodyPreview = GetNestedString(task, "body", "content"),
        Status = GetString(task, "status", "notStarted"),
        Importance = GetString(task, "importance", "normal"),
        IsReminderOn = GetBoolean(task, "isReminderOn"),
        CreatedDateTime = GetDateTimeOffset(task, "createdDateTime"),
        DueDateTime = DateTimeOffset.TryParse(GetNestedString(task, "dueDateTime", "dateTime"), out var due) ? due : null,
        CompletedDateTime = GetDateTimeOffset(task, "completedDateTime")
    };

    private static List<GraphTeam> FilterTeams(List<GraphTeam> teams, string query)
        => teams.Where(team =>
            string.IsNullOrWhiteSpace(query) ||
            query.Equals("me", StringComparison.OrdinalIgnoreCase) ||
            query.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            team.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            team.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            (team.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

    private static List<GraphChat> FilterChats(List<GraphChat> chats, string query)
        => chats.Where(chat =>
            string.IsNullOrWhiteSpace(query) ||
            query.Equals("all", StringComparison.OrdinalIgnoreCase) ||
            chat.Id.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            chat.ChatType.Equals(query, StringComparison.OrdinalIgnoreCase) ||
            (chat.Topic?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            chat.Members.Any(member => member.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();

    private static void FilterTodoTasks(List<GraphTodoTaskList> lists, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Equals("all", StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var list in lists)
        {
            list.Tasks = list.Tasks.Where(task =>
                task.Importance.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                task.Status.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                task.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (task.BodyPreview?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (task.AssignedToUpn?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
    }

    #region Mock High-Fidelity M365 Dataset
    public static List<GraphUser> GetMockUsers()
    {
        return [
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000001",
                DisplayName = "Olga Podolyako",
                UserPrincipalName = "opodolyako@example.com",
                Mail = "opodolyako@example.com",
                JobTitle = "PRINCIPAL API ARCHITECT",
                Department = "IDM Services ENG",
                OfficeLocation = "Home Office",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000002",
                DisplayName = "Vojtech Vondra",
                UserPrincipalName = "vvondra@example.com",
                Mail = "vvondra@example.com",
                JobTitle = "Partner Director Engineering",
                Department = "IDM MAP COGS 1908 Prague",
                OfficeLocation = "PRAGUE-BBC/Mobile",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000003",
                DisplayName = "Joao Paiva",
                UserPrincipalName = "jpaiva@example.com",
                Mail = "jpaiva@example.com",
                JobTitle = "PRINCIPAL SOFTWARE ARCHITECT",
                Department = "IDM Services ENG",
                OfficeLocation = "REDW-B/1072",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000004",
                DisplayName = "Taylor Morgan",
                UserPrincipalName = "taylor.morgan@example.com",
                Mail = "taylor.morgan@example.com",
                JobTitle = "PRINCIPAL PRODUCT MANAGER",
                Department = "IDNA AI Innovations",
                OfficeLocation = "REDW-B/1084",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000005",
                DisplayName = "Casey Chen",
                UserPrincipalName = "casey.chen@example.com",
                Mail = "casey.chen@example.com",
                JobTitle = "PRINCIPAL GROUP ENG MANAGER",
                Department = "IDM Services ENG",
                OfficeLocation = "REDW-B/1086",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000006",
                DisplayName = "Riley Patel",
                UserPrincipalName = "riley.patel@example.com",
                Mail = "riley.patel@example.com",
                JobTitle = "BUSINESS ADMINISTRATOR",
                Department = "IDM MAP COGS 1908 Prague",
                OfficeLocation = "PRAGUE-BBC/Mobile",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000007",
                DisplayName = "Jamie Kim",
                UserPrincipalName = "jamie.kim@example.com",
                Mail = "jamie.kim@example.com",
                JobTitle = "GROUP PM MANAGER",
                Department = "IDM AD Ops COGS",
                OfficeLocation = "REDW-B/1072",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000008",
                DisplayName = "Sam Rivera",
                UserPrincipalName = "sam.rivera@example.com",
                Mail = "sam.rivera@example.com",
                JobTitle = "PRINCIPAL GROUP ENG MGR",
                Department = "IDM Services ENG",
                OfficeLocation = "PRAGUE-BBC",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "10000000-0000-4000-8000-000000000009",
                DisplayName = "Drew Parker",
                UserPrincipalName = "drew.parker@example.com",
                Mail = "drew.parker@example.com",
                JobTitle = "PARTNER ARCHITECT (OData Co-Chair)",
                Department = "Data & AI",
                OfficeLocation = "REDW-B",
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

        var manager = users.FirstOrDefault(u => u.DisplayName.Contains("Vojtech", StringComparison.OrdinalIgnoreCase));
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
                Mail = "identityarch@example.com",
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
                Mail = "scimreviewers@example.com",
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
                Mail = "graphpartners@example.com",
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
                Mail = "odatetc@example.com",
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

        var owners = users.Where(u => u.DisplayName.Contains("Vojtech") || u.DisplayName.Contains("Drew")).ToList();
        var members = users.Where(u => u.DisplayName.Contains("Olga") || u.DisplayName.Contains("Joao") || u.DisplayName.Contains("Taylor") || u.DisplayName.Contains("Casey Chen")).ToList();

        if (group.DisplayName.Contains("Partners"))
        {
            owners = users.Where(u => u.DisplayName.Contains("Olga")).ToList();
            members = users.Where(u => u.DisplayName.Contains("Jamie") || u.DisplayName.Contains("Riley") || u.DisplayName.Contains("Cameron")).ToList();
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
        var olga = users[0];
        var joao = users[2];
        var mike = users[8];
        var taylor = users[3];

        return [
            new GraphDriveItem
            {
                Id = "file-mcp-poc-docx",
                Name = "Script_MCP_POC.docx",
                Size = 482910,
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents/Script_MCP_POC.docx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-18T14:20:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-03T18:45:00Z"),
                LastModifiedByDisplayName = olga.DisplayName,
                LastModifiedByUpn = olga.UserPrincipalName,
                LastModifiedByUser = olga
            },
            new GraphDriveItem
            {
                Id = "file-odata-agent-prompts-pptx",
                Name = "OData-v4.01-Agent-Prompts-Proposal.pptx",
                Size = 2841920,
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents/OData-v4.01-Agent-Prompts-Proposal.pptx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-20T09:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-04T09:15:00Z"),
                LastModifiedByDisplayName = mike.DisplayName,
                LastModifiedByUpn = mike.UserPrincipalName,
                LastModifiedByUser = mike
            },
            new GraphDriveItem
            {
                Id = "file-csdl-consistency-xlsx",
                Name = "Graph-CSDL-Consistency-Benchmark.xlsx",
                Size = 918240,
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents/Graph-CSDL-Consistency-Benchmark.xlsx",
                FileMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-24T11:30:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-02T16:10:00Z"),
                LastModifiedByDisplayName = joao.DisplayName,
                LastModifiedByUpn = joao.UserPrincipalName,
                LastModifiedByUser = joao
            },
            new GraphDriveItem
            {
                Id = "file-mcp-20-spec-pdf",
                Name = "MCP-2.0-Specification-2026-07-28-Notes.pdf",
                Size = 1540320,
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents/MCP-2.0-Specification-2026-07-28-Notes.pdf",
                FileMimeType = "application/pdf",
                IsFolder = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-08-12T10:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-10-29T13:25:00Z"),
                LastModifiedByDisplayName = taylor.DisplayName,
                LastModifiedByUpn = taylor.UserPrincipalName,
                LastModifiedByUser = taylor
            },
            new GraphDriveItem
            {
                Id = "folder-architecture-specs",
                Name = "Architecture-Specs",
                Size = 0,
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents/Architecture-Specs",
                FileMimeType = "inode/directory",
                IsFolder = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-01-10T08:00:00Z"),
                LastModifiedDateTime = DateTimeOffset.Parse("2026-11-01T12:00:00Z"),
                LastModifiedByDisplayName = olga.DisplayName,
                LastModifiedByUpn = olga.UserPrincipalName,
                LastModifiedByUser = olga
            }
        ];
    }

    public static List<GraphDrive> GetMockDrives()
    {
        return [
            new GraphDrive
            {
                Id = "drive-olga",
                Name = "OneDrive - Olga Podolyako",
                DriveType = "business",
                WebUrl = "https://contoso-my.sharepoint.com/personal/opodolyako/Documents",
                OwnerDisplayName = "Olga Podolyako",
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
            Email = "identityarch-general@example.com",
            WebUrl = "https://teams.microsoft.com/l/channel/ch-idarch-general",
            CreatedDateTime = DateTimeOffset.Parse("2023-01-15T08:05:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-101",
                    Subject = "Weekly Architecture Review Agenda",
                    Summary = "Reviewing OData v4.01 $metadata alignment and CSDL consistency gates.",
                    BodyContent = "Team — on Thursday we will review the OData v4.01 $metadata endpoint conventions and the new @Agent.Prompts / @Agent.Examples annotations.",
                    FromDisplayName = "Vojtech Vondra",
                    FromUpn = "vvondra@example.com",
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
            Email = "csdl-typespec@example.com",
            WebUrl = "https://teams.microsoft.com/l/channel/ch-csdl-typespec",
            CreatedDateTime = DateTimeOffset.Parse("2024-03-10T10:00:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-201",
                    Subject = "Phase 1 vs Phase 2 CSDL Annotations",
                    Summary = "@Agent.Prompts on EntityContainer (+36.1 pp routing gain) + @Agent.Examples on Entities & Operations",
                    BodyContent = "Phase 1 (@Agent.Prompts on GraphService) raised verified routing accuracy from 55.7% to 91.8%. For LargeServer we added @Agent.Examples on EntityTypes, EntitySets, Functions, and Actions covering Files, Teams, Chats, and To Do.",
                    FromDisplayName = "Olga Podolyako",
                    FromUpn = "opodolyako@example.com",
                    Importance = "high",
                    CreatedDateTime = DateTimeOffset.Parse("2026-11-04T10:30:00Z")
                },
                new GraphChatMessage
                {
                    Id = "msg-202",
                    Subject = "Bound OData Functions & Actions in CSDL",
                    Summary = "Added searchDriveItems, recentDriveItems, deltaChannelMessages, sendChannelMessage, completeTodoTask",
                    BodyContent = "Confirmed that bound Functions and Actions in Microsoft.Graph validate cleanly with Microsoft.OData.Edm 9.0.0 and project deterministically to MCP tools.",
                    FromDisplayName = "Joao Paiva",
                    FromUpn = "jpaiva@example.com",
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
            Email = "mcp20@example.com",
            WebUrl = "https://teams.microsoft.com/l/channel/ch-mcp-20",
            CreatedDateTime = DateTimeOffset.Parse("2026-08-01T09:00:00Z"),
            Messages = [
                new GraphChatMessage
                {
                    Id = "msg-301",
                    Subject = "Stateless server/discover & Multi-Workload Projection",
                    Summary = "Removed deprecated initialize; added M365 workloads (Files, Teams, Chats, To Do) on port 3003.",
                    BodyContent = "LargeServer (:3003) exposes 14 tools, 13 resources/templates, 22 @Agent.Prompts, and 28 @Agent.Examples directly projected from Server_Large.csdl.",
                    FromDisplayName = "Taylor Morgan",
                    FromUpn = "taylor.morgan@example.com",
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
                WebUrl = "https://teams.microsoft.com/l/team/11111111-2222-3333-4444-555555555555",
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
                WebUrl = "https://teams.microsoft.com/l/team/44444444-5555-6666-7777-888888888888",
                CreatedDateTime = DateTimeOffset.Parse("2024-02-01T09:00:00Z"),
                Channels = [
                    new GraphChannel
                    {
                        Id = "ch-odata-vocab",
                        TeamId = "44444444-5555-6666-7777-888888888888",
                        DisplayName = "Agent-Ready Vocabularies",
                        Description = "OASIS OData TC proposal for @Agent.Prompts and @Agent.Examples",
                        MembershipType = "standard",
                        Email = "odata-agent-vocab@example.com",
                        WebUrl = "https://teams.microsoft.com/l/channel/ch-odata-vocab",
                        CreatedDateTime = DateTimeOffset.Parse("2024-02-05T10:00:00Z"),
                        Messages = [
                            new GraphChatMessage
                            {
                                Id = "msg-401",
                                Subject = "OData v4.01 $metadata + Agent Vocabulary Alignment",
                                Summary = "Keeping CSDL pure: project tools/resources from native EntitySets & Operations; use @Agent.Prompts and @Agent.Examples.",
                                BodyContent = "Updated the proposal deck in OneDrive (OData-v4.01-Agent-Prompts-Proposal.pptx) to show both EntityContainer @Agent.Prompts and entity/operation @Agent.Examples.",
                                FromDisplayName = "Drew Parker",
                                FromUpn = "drew.parker@example.com",
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
        var olga = users[0];
        var vojtech = users[1];
        var joao = users[2];
        var mike = users[8];

        return [
            new GraphChat
            {
                Id = "chat-olga-joao",
                Topic = "CSDL LargeServer Schema & TypeSpec Sync",
                ChatType = "oneOnOne",
                CreatedDateTime = DateTimeOffset.Parse("2026-10-15T09:00:00Z"),
                LastUpdatedDateTime = DateTimeOffset.Parse("2026-11-04T13:10:00Z"),
                WebUrl = "https://teams.microsoft.com/l/chat/chat-olga-joao",
                Members = [olga, joao],
                Messages = [
                    new GraphChatMessage
                    {
                        Id = "cmsg-1",
                        Subject = "DriveItem & TodoTask CSDL entities",
                        BodyContent = "Hey Olga, I just updated Graph-CSDL-Consistency-Benchmark.xlsx in OneDrive. All 10 EntityTypes and 5 bound operations in Server_Large.csdl validate cleanly.",
                        FromDisplayName = joao.DisplayName,
                        FromUpn = joao.UserPrincipalName,
                        Importance = "normal",
                        CreatedDateTime = DateTimeOffset.Parse("2026-11-04T13:05:00Z")
                    },
                    new GraphChatMessage
                    {
                        Id = "cmsg-2",
                        Subject = "Re: DriveItem & TodoTask CSDL entities",
                        BodyContent = "Awesome, thanks Joao! Wiring up the Large MCP Explorer tab on :3000 now so we can demo Users, Groups, Files, Teams, Chats, and To Do end-to-end.",
                        FromDisplayName = olga.DisplayName,
                        FromUpn = olga.UserPrincipalName,
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
                WebUrl = "https://teams.microsoft.com/l/chat/chat-odata-leads",
                Members = [olga, vojtech, mike],
                Messages = [
                    new GraphChatMessage
                    {
                        Id = "cmsg-3",
                        Subject = "Demo Readiness Check",
                        BodyContent = "Make sure we highlight how @Agent.Prompts on the container and @Agent.Examples on entities/operations work together without polluting CSDL with tool definitions.",
                        FromDisplayName = mike.DisplayName,
                        FromUpn = mike.UserPrincipalName,
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
                AssignedToUpn = "opodolyako@example.com"
            },
            new GraphTodoTask
            {
                Id = "task-102",
                ListId = "list-mcp-poc",
                ListDisplayName = "Microsoft Graph & MCP 2.0 POC",
                Title = "Review Graph-CSDL-Consistency-Benchmark.xlsx with Joao Paiva",
                BodyPreview = "Verify multi-workload routing metrics and bound function parameter bindings.",
                Status = CompletedTaskIds.Contains("task-102") ? "completed" : "notStarted",
                Importance = "high",
                IsReminderOn = true,
                CreatedDateTime = DateTimeOffset.Parse("2026-11-02T11:00:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-06T17:00:00Z"),
                AssignedToUpn = "jpaiva@example.com"
            },
            new GraphTodoTask
            {
                Id = "task-103",
                ListId = "list-mcp-poc",
                ListDisplayName = "Microsoft Graph & MCP 2.0 POC",
                Title = "Publish OData-v4.01-Agent-Prompts-Proposal.pptx to OData TC channel",
                BodyPreview = "Share final slide deck with Drew Parker and Vojtech Vondra.",
                Status = "completed",
                Importance = "normal",
                IsReminderOn = false,
                CreatedDateTime = DateTimeOffset.Parse("2026-10-29T10:00:00Z"),
                DueDateTime = DateTimeOffset.Parse("2026-11-03T17:00:00Z"),
                CompletedDateTime = DateTimeOffset.Parse("2026-11-03T16:30:00Z"),
                AssignedToUpn = "opodolyako@example.com"
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
                AssignedToUpn = "opodolyako@example.com"
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

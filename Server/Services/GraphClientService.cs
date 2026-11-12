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

    public GraphClientService(HttpClient httpClient, ILogger<GraphClientService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public List<GraphTraceRecord> GetRecentTraces()
    {
        lock (_traceLock)
        {
            return _traces.TakeLast(20).ToList();
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
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
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
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
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
                Url = $"https://graph.microsoft.com/v1.0/{url}",
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
                Url = $"https://graph.microsoft.com/v1.0/{url}",
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

    #region Mock High-Fidelity Dataset (Microsoft Tenant Fallback)
    public static List<GraphUser> GetMockUsers()
    {
        return [
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000001",
                DisplayName = "Avery Adams",
                UserPrincipalName = "avery.adams@example.com",
                Mail = "avery.adams@example.com",
                JobTitle = "PRINCIPAL API ARCHITECT",
                Department = "Platform Engineering",
                OfficeLocation = "Home Office",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000002",
                DisplayName = "Morgan Reed",
                UserPrincipalName = "morgan.reed@example.com",
                Mail = "morgan.reed@example.com",
                JobTitle = "Partner Director Engineering",
                Department = "Cloud Operations",
                OfficeLocation = "Remote",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000003",
                DisplayName = "Jordan Lee",
                UserPrincipalName = "jordan.lee@example.com",
                Mail = "jordan.lee@example.com",
                JobTitle = "PRINCIPAL SOFTWARE ARCHITECT",
                Department = "Platform Engineering",
                OfficeLocation = "Building A",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000004",
                DisplayName = "Casey Kim",
                UserPrincipalName = "casey.kim@example.com",
                Mail = "casey.kim@example.com",
                JobTitle = "PRINCIPAL PRODUCT MANAGER",
                Department = "AI Products",
                OfficeLocation = "Building A",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000005",
                DisplayName = "Taylor Morgan",
                UserPrincipalName = "taylor.morgan@example.com",
                Mail = "taylor.morgan@example.com",
                JobTitle = "PRINCIPAL GROUP ENG MANAGER",
                Department = "Platform Engineering",
                OfficeLocation = "Building A",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000006",
                DisplayName = "Riley Chen",
                UserPrincipalName = "riley.chen@example.com",
                Mail = "riley.chen@example.com",
                JobTitle = "BUSINESS ADMINISTRATOR",
                Department = "Cloud Operations",
                OfficeLocation = "Remote",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000007",
                DisplayName = "Jamie Patel",
                UserPrincipalName = "jamie.patel@example.com",
                Mail = "jamie.patel@example.com",
                JobTitle = "GROUP PM MANAGER",
                Department = "Directory Operations",
                OfficeLocation = "Building A",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000008",
                DisplayName = "Cameron Brooks",
                UserPrincipalName = "cameron.brooks@example.com",
                Mail = "cameron.brooks@example.com",
                JobTitle = "PRINCIPAL GROUP ENG MGR",
                Department = "Platform Engineering",
                OfficeLocation = "Remote",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000009",
                DisplayName = "Drew Parker",
                UserPrincipalName = "drew.parker@example.com",
                Mail = "drew.parker@example.com",
                JobTitle = "PARTNER ARCHITECT (OData Co-Chair)",
                Department = "Data & AI",
                OfficeLocation = "Building A",
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
            u.DisplayName.Contains(identifier, StringComparison.OrdinalIgnoreCase));

        if (user == null) user = users[0]; // default to Avery

        var manager = users.FirstOrDefault(u => u.DisplayName.Contains("Morgan", StringComparison.OrdinalIgnoreCase));
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
                DisplayName = "Platform Engineering",
                Description = "Microsoft Entra ID & Microsoft Graph core architecture, design, and standards",
                Mail = "platform-engineering@example.com",
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
                Mail = "schema-reviewers@example.com",
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
                DisplayName = "API Partners",
                Description = "Partner onboarding, documentation, and API gateway guidance",
                Mail = "api-partners@example.com",
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
                DisplayName = "OData Working Group",
                Description = "OASIS OData Technical Committee standards and Agent-Ready extensions",
                Mail = "odata-working-group@example.com",
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

        var owners = users.Where(u => u.DisplayName.Contains("Morgan") || u.DisplayName.Contains("Drew")).ToList();
        var members = users.Where(u => u.DisplayName.Contains("Avery") || u.DisplayName.Contains("Jordan") || u.DisplayName.Contains("Casey") || u.DisplayName.Contains("Taylor Morgan")).ToList();

        if (group.DisplayName.Contains("Partners"))
        {
            owners = users.Where(u => u.DisplayName.Contains("Avery")).ToList();
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
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
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
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
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
                    Url = $"https://graph.microsoft.com/v1.0/{requestUrl}",
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
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var trace = new GraphTraceRecord
                {
                    Method = "GET",
                    Url = $"https://graph.microsoft.com/v1.0/{cleanPath}",
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
    #endregion
}

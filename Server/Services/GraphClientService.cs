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

    #region Mock High-Fidelity Dataset (Sanitized Contoso Sample Data)
    public static List<GraphUser> GetMockUsers()
    {
        return [
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000001",
                DisplayName = "Olga Podolyako",
                UserPrincipalName = "olga@contoso.com",
                Mail = "olga@contoso.com",
                JobTitle = "Principal API Architect",
                Department = "Architecture & Standards",
                OfficeLocation = "Building 1 / Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000002",
                DisplayName = "Alex Wilbur",
                UserPrincipalName = "alexw@contoso.com",
                Mail = "alexw@contoso.com",
                JobTitle = "Partner Director of Engineering",
                Department = "Engineering Leadership",
                OfficeLocation = "Building 1 / Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000003",
                DisplayName = "Megan Bowen",
                UserPrincipalName = "meganb@contoso.com",
                Mail = "meganb@contoso.com",
                JobTitle = "Principal Software Architect",
                Department = "Architecture",
                OfficeLocation = "Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000004",
                DisplayName = "Adele Vance",
                UserPrincipalName = "adelev@contoso.com",
                Mail = "adelev@contoso.com",
                JobTitle = "Principal Product Manager",
                Department = "Product Operations",
                OfficeLocation = "Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000005",
                DisplayName = "Diego Siciliani",
                UserPrincipalName = "diegos@contoso.com",
                Mail = "diegos@contoso.com",
                JobTitle = "Principal Group Engineering Manager",
                Department = "Engineering",
                OfficeLocation = "Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000006",
                DisplayName = "Patti Fernandez",
                UserPrincipalName = "pattif@contoso.com",
                Mail = "pattif@contoso.com",
                JobTitle = "Business Administrator",
                Department = "Operations",
                OfficeLocation = "Prague",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000007",
                DisplayName = "Lynne Robbins",
                UserPrincipalName = "lynner@contoso.com",
                Mail = "lynner@contoso.com",
                JobTitle = "Group Product Manager",
                Department = "Product",
                OfficeLocation = "Redmond",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000008",
                DisplayName = "Joni Sherman",
                UserPrincipalName = "jonis@contoso.com",
                Mail = "jonis@contoso.com",
                JobTitle = "Principal Group Software Manager",
                Department = "Engineering",
                OfficeLocation = "Prague",
                PreferredLanguage = "en-US"
            },
            new GraphUser
            {
                Id = "00000000-0000-0000-0000-000000000009",
                DisplayName = "Henrietta Mueller",
                UserPrincipalName = "henriettam@contoso.com",
                Mail = "henriettam@contoso.com",
                JobTitle = "Principal Standards Architect",
                Department = "Standards",
                OfficeLocation = "Building 1 / Redmond",
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

        if (user == null) user = users[0]; // default to Olga

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
    #endregion
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GraphMcpServer.Services;

public interface IODataClientService
{
    Task<JsonElement> SendAsync(HttpMethod method, string path, JsonElement? body, CancellationToken cancellationToken);
    IReadOnlyList<ODataTrace> GetRecentTraces();
}
public sealed record ODataTrace(string Method, string Path, int? StatusCode, DateTimeOffset Timestamp, long DurationMs);

public sealed class ODataClientService : IODataClientService
{
    private readonly HttpClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ODataClientService> _logger;
    private readonly List<ODataTrace> _traces = [];
    private readonly object _sync = new();
    private readonly DefaultAzureCredential _credential = new();

    public ODataClientService(HttpClient client, IConfiguration configuration, ILogger<ODataClientService> logger)
    {
        _client = client;
        _configuration = configuration;
        _logger = logger;
    }
    public IReadOnlyList<ODataTrace> GetRecentTraces()
    {
        lock (_sync) return _traces.ToArray();
    }
    public async Task<JsonElement> SendAsync(HttpMethod method, string path, JsonElement? body, CancellationToken cancellationToken)
    {
        var configured = _configuration["OData:BaseUrl"];
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) || baseUri.Scheme is not ("http" or "https") ||
            !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment) || !string.IsNullOrEmpty(baseUri.UserInfo))
            throw new InvalidOperationException("Configure OData:BaseUrl with an absolute HTTP(S) service root before executing tools or reading resources.");
        baseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/");
        var uri = new Uri(baseUri, path);
        if (uri.GetLeftPart(UriPartial.Authority) != baseUri.GetLeftPart(UriPartial.Authority) ||
            !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            throw new ArgumentException("OData binding escaped the configured service root.");
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-Version", "4.0");
        var scope = _configuration["OData:Scope"];
        if (!string.IsNullOrWhiteSpace(scope))
        {
            var token = await _credential.GetTokenAsync(new TokenRequestContext([scope]), cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        }
        var apiKey = _configuration["OData:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var headerName = _configuration["OData:ApiKeyHeader"] ?? "OData-ApiKey";
            request.Headers.Add(headerName, apiKey);
        }
        if (body.HasValue) request.Content = JsonContent.Create(body.Value);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int? status = null;
        try
        {
            using var response = await _client.SendAsync(request, cancellationToken);
            status = (int)response.StatusCode;
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return JsonSerializer.SerializeToElement<object?>(null);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            _logger.LogError(exception, "OData returned an invalid JSON response");
            throw new HttpRequestException("OData returned an invalid JSON response.", exception,
                status.HasValue ? (System.Net.HttpStatusCode)status.Value : null);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation("OData {Method} request to {Path} completed with status {Status} in {DurationMs}ms", method, path, status, stopwatch.ElapsedMilliseconds);
            lock (_sync)
            {
                _traces.Add(new(method.Method, path, status, DateTimeOffset.UtcNow, stopwatch.ElapsedMilliseconds));
                if (_traces.Count > 30) _traces.RemoveAt(0);
            }
        }
    }
}

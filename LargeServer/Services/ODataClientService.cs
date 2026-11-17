using System.Text.Json;
using GraphMcpServer.Csdl;
using Microsoft.Extensions.Logging;

namespace GraphMcpServer.Services;

/// <summary>Defines runtime execution and trace access for generated OData request bindings.</summary>
public interface IODataClientService
{
    Task<JsonElement> SendAsync(
        ODataRequestBinding binding,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpMethod method,
        string path,
        JsonElement? body,
        CancellationToken cancellationToken);
    IReadOnlyList<ODataTrace> GetRecentTraces();
}

/// <summary>Records the method, path, status, timestamp, and duration of an in-process OData execution.</summary>
public sealed record ODataTrace(
    string Method,
    string Path,
    int? StatusCode,
    DateTimeOffset Timestamp,
    long DurationMs);

/// <summary>Bridges OData requests to the Graph executor, normalizes results to JSON, and records execution timing.</summary>
public sealed class ODataClientService(
    GeneratedGraphExecutor executor,
    ILogger<ODataClientService> logger) : IODataClientService
{
    private readonly List<ODataTrace> _traces = [];
    private readonly object _sync = new();

    public IReadOnlyList<ODataTrace> GetRecentTraces()
    {
        lock (_sync)
            return _traces.ToArray();
    }

    public async Task<JsonElement> SendAsync(
        ODataRequestBinding binding,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpMethod method,
        string path,
        JsonElement? body,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        int? status = null;
        try
        {
            var result = await executor.ExecuteAsync(
                binding,
                arguments,
                method,
                path,
                body,
                cancellationToken);
            if (result == null)
                throw new HttpRequestException($"No Microsoft Graph execution mapping matched '{method} {path}'.");

            var envelope = JsonSerializer.SerializeToElement(result);
            status = 200;
            return envelope.ValueKind == JsonValueKind.Object &&
                   envelope.TryGetProperty("data", out var data)
                ? data.Clone()
                : envelope;
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "In-process Graph {Method} execution for {Path} completed with status {Status} in {DurationMs}ms",
                method,
                path,
                status,
                stopwatch.ElapsedMilliseconds);
            lock (_sync)
            {
                _traces.Add(new(
                    method.Method,
                    path,
                    status,
                    DateTimeOffset.UtcNow,
                    stopwatch.ElapsedMilliseconds));
                if (_traces.Count > 30)
                    _traces.RemoveAt(0);
            }
        }
    }
}

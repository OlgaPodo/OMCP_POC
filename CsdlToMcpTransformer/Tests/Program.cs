using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GraphMcpServer.Csdl;
using GraphMcpServer.Services;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;

await VerifyRequestBindingsAsync();

var sourceRoot = args.Length > 0 ? args[0] : @"C:\MCP\Csdl";
var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Inventory.csdl");
var generated = Path.Combine(Path.GetTempPath(), "CsdlToMcpStaticTests-" + Guid.NewGuid().ToString("N"));

try
{
    var exitCode = CsdlToMcpTransformer.Program.Main([fixture, generated, "3444"]);
    Check(exitCode == 0, "Static transformer exits successfully");
    Check(File.Exists(Path.Combine(generated, "Csdl", "GeneratedModel.cs")), "Generated static model source");
    Check(!Directory.EnumerateFiles(generated, "*.csdl", SearchOption.AllDirectories).Any(),
        "Generated project contains no runtime CSDL files");

    var allSource = string.Join("\n", Directory.EnumerateFiles(generated, "*.cs", SearchOption.AllDirectories)
        .Select(File.ReadAllText));
    var project = File.ReadAllText(Path.Combine(generated, "GraphMcpServer.csproj"));
    Check(!allSource.Contains("CsdlReader", StringComparison.Ordinal) &&
          !allSource.Contains("File.ReadAllText", StringComparison.Ordinal) &&
          !allSource.Contains("CsdlProvider.Initialize", StringComparison.Ordinal),
        "Generated source performs no runtime CSDL parsing");
    Check(allSource.Contains("IReadOnlyList<ODataRequestBinding> RequestBindings", StringComparison.Ordinal) &&
          allSource.Contains("ODataRequestBinding RequestBinding", StringComparison.Ordinal) &&
          allSource.Contains("IReadOnlyList<ResourceBinding> Resources", StringComparison.Ordinal),
        "Generated tools and resources share OData request bindings");
    Check(!allSource.Contains("ExtractODataQueries", StringComparison.Ordinal) &&
          !allSource.Contains("Regex.Escape", StringComparison.Ordinal) &&
          !allSource.Contains("RequestBindingsByName", StringComparison.Ordinal) &&
          !allSource.Contains("record ResourceDefinition", StringComparison.Ordinal),
        "Generated runtime contains no catalog inference or resource binding construction");
    Check(allSource.Contains("List<Tool> McpTools", StringComparison.Ordinal) &&
          allSource.Contains("List<Prompt> McpPrompts", StringComparison.Ordinal) &&
          allSource.Contains("JsonElement Catalog", StringComparison.Ordinal),
        "Transformer emits ready-to-serve discovery and diagnostic metadata");
    Check(!project.Contains("Microsoft.OData.Edm", StringComparison.Ordinal) &&
          !project.Contains("metadata.csdl", StringComparison.Ordinal),
        "Generated project has no runtime EDM or CSDL dependency");
    Check(File.Exists(Path.Combine(generated, "Services", "GeneratedGraphExecutor.cs")) &&
          File.Exists(Path.Combine(generated, "Services", "GraphClientService.cs")) &&
          File.Exists(Path.Combine(generated, "Models", "GraphModels.cs")),
        "Generated project contains in-process Graph execution");
    var odataClient = File.ReadAllText(Path.Combine(generated, "Services", "ODataClientService.cs"));
    Check(!odataClient.Contains("localhost:", StringComparison.Ordinal) &&
          odataClient.Contains("GeneratedGraphExecutor", StringComparison.Ordinal),
        "Generated MCP execution does not use a local HTTP adapter");

    Run("dotnet", ["build", Path.Combine(generated, "GraphMcpServer.csproj"), "-c", "Release"],
        generated, "Static generated project builds");

    foreach (var (name, prompts, examples) in new[]
             {
                 ("Server_Baseline.csdl", 0, 0),
                 ("Server_WithPrompts.csdl", 12, 0),
                 ("Server_Large.csdl", 22, 0),
                 ("Server_Large_WithExamples.csdl", 22, 28)
             })
    {
        var output = Path.Combine(Path.GetTempPath(), "CsdlStatic-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(CsdlToMcpTransformer.Program.Main([Path.Combine(sourceRoot, name), output, "3445"]) == 0,
                $"{name}: static generation");
            using var manifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(output, "transformation-manifest.json")));
            var root = manifest.RootElement;
            Check(root.GetProperty("projectionMode").GetString() == "static-generated-csharp",
                $"{name}: static projection mode");
            Check(root.GetProperty("promptsCount").GetInt32() == prompts &&
                  root.GetProperty("examplesCount").GetInt32() == examples,
                $"{name}: annotation counts");
            Check(!Directory.EnumerateFiles(output, "*.csdl", SearchOption.AllDirectories).Any(),
                $"{name}: no runtime CSDL");
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    using var process = Start(
        "dotnet",
        [Path.Combine(generated, "bin", "Release", "net10.0", "win-x64", "GraphMcpServer.dll")],
        generated);
    try
    {
        using var client = new HttpClient();
        string? health = null;
        for (var attempt = 0; attempt < 30 && health == null; attempt++)
        {
            try { health = await client.GetStringAsync("http://localhost:3444/health"); }
            catch (HttpRequestException) { await Task.Delay(200); }
        }
        Check(health != null && health.Contains("Static generated C#", StringComparison.Ordinal),
            "Static server starts without source CSDL");
    }
    finally
    {
        if (!process.HasExited) process.Kill(true);
        process.WaitForExit();
    }

    Console.WriteLine("All static transformer tests passed.");
}
finally
{
    if (Directory.Exists(generated)) Directory.Delete(generated, true);
}

static Process Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
{
    var startInfo = new ProcessStartInfo(fileName)
    {
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    return Process.Start(startInfo) ?? throw new InvalidOperationException($"Failed to start {fileName}.");
}

static void Run(
    string fileName,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    string description)
{
    using var process = Start(fileName, arguments, workingDirectory);
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"FAILED: {description}\n{output}\n{error}");
    Check(true, description);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {description}");
    Console.WriteLine($"PASS: {description}");
}

static async Task VerifyRequestBindingsAsync()
{
    var bindings = CsdlProvider.GetRequestBindings();
    var tools = CsdlMcpProjection.ListTools().Tools;
    Check(ReferenceEquals(tools, GeneratedModel.McpTools) &&
          tools.Count == bindings.Count && bindings.All(binding =>
    {
        var tool = tools.Single(tool => tool.Name == binding.Name);
        return tool.Title == binding.Title && tool.Description == binding.Description &&
               tool.InputSchema.GetRawText() == binding.InputSchemaJson;
    }), "MCP tool discovery preserves binding names, descriptions and schemas");

    var resources = CsdlProvider.GetProjectedResources();
    Check(ReferenceEquals(resources, GeneratedModel.Resources) && resources.All(resource =>
        ReferenceEquals(resource.RequestBinding,
            bindings.Single(binding => binding.Name == resource.RequestBinding.Name))),
        "Resources use prelinked OData request bindings");
    Check(bindings.All(binding => binding.Examples.SequenceEqual(GeneratedModel.Examples
              .Where(example => example.TargetName == binding.AnnotationTarget ||
                  binding.EntitySet != null && example.EntitySet == binding.EntitySet ||
                  binding.EntityType != null && example.TargetName == binding.EntityType)
              .Distinct()) &&
          ReferenceEquals(binding.Examples, CsdlProvider.GetExamplesFor(binding))),
        "Transformer precomputes the same examples for every request binding");
    Check(CsdlMcpProjection.ListResources().Resources.Select(resource => resource.Uri)
              .SequenceEqual(resources.Where(resource => !resource.UriTemplate.Contains('{'))
                  .Select(resource => resource.UriTemplate)) &&
          CsdlMcpProjection.ListResourceTemplates().ResourceTemplates.Select(resource => resource.UriTemplate)
              .SequenceEqual(resources.Where(resource => resource.UriTemplate.Contains('{'))
                  .Select(resource => resource.UriTemplate)),
        "Resource discovery preserves fixed URIs and keyed templates");

    var client = new RecordingODataClient();
    var projection = new CsdlMcpProjection(client, NullLogger<CsdlMcpProjection>.Instance);
    foreach (var (name, expectedPath) in new[]
             {
                 ("users", "users"),
                 ("get_me", "me"),
                 ("get_users", "users(%27test-user%27)")
             })
    {
        var resource = resources.Single(resource => resource.Name == name);
        var arguments = resource.RequestBinding.Arguments.Where(argument => argument.IsKey)
            .ToDictionary(argument => argument.Name, _ => JsonSerializer.SerializeToElement("test-user"));
        var toolResult = await projection.CallToolAsync(new CallToolRequestParams
        {
            Name = resource.RequestBinding.Name,
            Arguments = arguments
        }, CancellationToken.None);
        Check(toolResult.IsError != true, $"{name}: MCP tool call succeeds");
        var toolText = toolResult.Content.OfType<TextContentBlock>().Single().Text;
        var callsBeforeRead = client.Calls.Count;
        var resourceResult = await projection.ReadResourceAsync(new ReadResourceRequestParams
        {
            Uri = resource.UriTemplate.Replace("{key_id}", "test-user")
        }, CancellationToken.None);
        var content = resourceResult.Contents.OfType<TextResourceContents>().Single();
        Check(client.Calls.Count == callsBeforeRead + 1 &&
              client.Calls[^1].Method == HttpMethod.Get &&
              client.Calls[^1].Path == expectedPath &&
              ReferenceEquals(client.Calls[^1].Binding, resource.RequestBinding),
            $"{name}: resource read directly executes its OData request once");
        Check(content.MimeType == "application/json" && content.Text == toolText,
            $"{name}: resource and tool response content remains identical");

        using var response = JsonDocument.Parse(content.Text);
        var root = response.RootElement;
        Check(root.EnumerateObject().Select(property => property.Name)
                  .SequenceEqual(new[] { "method", "odataQuery", "entitySet", "data", "examples" }) &&
              root.GetProperty("data").GetProperty("id").GetString() == "test-user" &&
              root.GetProperty("examples").EnumerateArray()
                  .Select(example => example.GetProperty("request").GetString())
                  .SequenceEqual(CsdlProvider.GetExamplesFor(resource.RequestBinding)
                      .Select(example => example.Request)) &&
              root.GetProperty("examples").GetArrayLength() > 0,
            $"{name}: response shape, data and associated examples are preserved");
    }

    var callsBeforeError = client.Calls.Count;
    var invalid = await projection.CallToolAsync(new CallToolRequestParams
    {
        Name = "list_users",
        Arguments = new Dictionary<string, JsonElement>
        {
            ["unknown"] = JsonSerializer.SerializeToElement(true)
        }
    }, CancellationToken.None);
    Check(invalid.IsError == true && client.Calls.Count == callsBeforeError &&
          invalid.Content.OfType<TextContentBlock>().Single().Text == "Unknown OData request argument.",
        "Unknown arguments report OData request terminology before execution");

    var missing = await projection.CallToolAsync(new CallToolRequestParams
    {
        Name = "get_users"
    }, CancellationToken.None);
    Check(missing.IsError == true && client.Calls.Count == callsBeforeError &&
          missing.Content.OfType<TextContentBlock>().Single().Text ==
              "Missing required OData request argument: key_id",
        "Missing arguments report OData request terminology before execution");

    var diagnostic = JsonSerializer.SerializeToElement(CsdlProvider.GetProjection());
    Check(diagnostic.GetProperty("tools").GetArrayLength() == bindings.Count &&
          diagnostic.GetProperty("resources").GetArrayLength() == resources.Count &&
          diagnostic.GetProperty("exampleQueries").GetArrayLength() == GeneratedModel.Examples.Count,
        "Diagnostic projection preserves external JSON field names and counts");
    var catalog = JsonSerializer.SerializeToElement(CsdlProvider.GetCatalog());
    Check(ReferenceEquals(CsdlProvider.GetProjection(), GeneratedModel.Projection) &&
          ReferenceEquals(CsdlProvider.GetPromptArchitecturalAnalysis(), GeneratedModel.Analysis) &&
          ReferenceEquals(CsdlProvider.ListPrompts().Prompts, GeneratedModel.McpPrompts) &&
          catalog.GetArrayLength() == GeneratedModel.Prompts.Count &&
          catalog.EnumerateArray().All(prompt =>
              prompt.GetProperty("toolBindings").EnumerateArray()
                  .All(name => bindings.Any(binding => binding.Name == name.GetString())) &&
              prompt.GetProperty("resourceBindings").EnumerateArray()
                  .All(uri => resources.Any(resource => resource.UriTemplate == uri.GetString()))),
        "Runtime serves generated catalogs, discovery and diagnostic metadata directly");
}

/// <summary>Records generated OData executions and returns deterministic JSON without authentication or network calls.</summary>
sealed class RecordingODataClient : IODataClientService
{
    public List<(ODataRequestBinding Binding, HttpMethod Method, string Path)> Calls { get; } = [];

    public Task<JsonElement> SendAsync(
        ODataRequestBinding binding,
        IReadOnlyDictionary<string, JsonElement> arguments,
        HttpMethod method,
        string path,
        JsonElement? body,
        CancellationToken cancellationToken)
    {
        Calls.Add((binding, method, path));
        return Task.FromResult(JsonSerializer.SerializeToElement(new { id = "test-user" }));
    }

    public IReadOnlyList<ODataTrace> GetRecentTraces() => [];
}

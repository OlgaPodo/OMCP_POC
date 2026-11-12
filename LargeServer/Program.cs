using GraphMcpServer.Csdl;
using GraphMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://localhost:3003");

// Register CORS for web app
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddHttpClient<ODataClientService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IODataClientService>(services => services.GetRequiredService<ODataClientService>());
builder.Services.AddSingleton<CsdlMcpProjection>();

// Register MCP server with HTTP transport, Tools, Resources, and Prompts
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithListToolsHandler((_, _) => ValueTask.FromResult(CsdlMcpProjection.ListTools()))
    .WithCallToolHandler((request, cancellationToken) => request.Services!.GetRequiredService<CsdlMcpProjection>()
        .CallToolAsync(request.Params ?? throw new ArgumentException("Tool parameters are required."), cancellationToken))
    .WithListResourcesHandler((_, _) => ValueTask.FromResult(CsdlMcpProjection.ListResources()))
    .WithListResourceTemplatesHandler((_, _) => ValueTask.FromResult(CsdlMcpProjection.ListResourceTemplates()))
    .WithReadResourceHandler((request, cancellationToken) => request.Services!.GetRequiredService<CsdlMcpProjection>()
        .ReadResourceAsync(request.Params ?? throw new ArgumentException("Resource parameters are required."), cancellationToken))
    .WithListPromptsHandler((_, _) => ValueTask.FromResult(CsdlProvider.ListPrompts()))
    .WithGetPromptHandler((request, _) => ValueTask.FromResult(CsdlProvider.GetPrompt(
        request.Params ?? throw new ArgumentException("Prompt parameters are required."))));

var app = builder.Build();

app.UseCors();

// Map MCP endpoints (handles POST / with MCP protocol)
app.MapMcp();

// Diagnostic and exploration API endpoints
app.MapGet("/health", (IODataClientService odataService) =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "CSDL MCP Server (Server_Large_WithExamples.csdl)",
        mcpSpecification = "MCP 2.0 (2026-07-28)",
        protocolVersion = "2026-07-28",
        supportedVersions = new[] { "2026-07-28" },
        projectionMode = "Static generated C# (no runtime CSDL parsing)",
        port = 3003,
        mcpEndpoint = "http://localhost:3003/",
        csdlPath = CsdlProvider.CsdlFilePath,
        hasCsdlPrompts = CsdlProvider.ListPrompts().Prompts.Count > 0,
        hasEntitySetExamples = CsdlProvider.GetExampleQueries().Count > 0,
        entityTypesCount = GeneratedModel.EntityTypesCount,
        operationsCount = GeneratedModel.OperationsCount,
        examplesCount = CsdlProvider.GetExampleQueries().Count,
        capabilities = new
        {
            discovery = "server/discover",
            transport = "Streamable HTTP (Stateless, Single Endpoint)",
            polymorphicResults = true,
            perRequestMeta = true,
            tools = CsdlMcpProjection.ListTools().Tools.Select(tool => tool.Name),
            resources = CsdlProvider.GetProjectedResources().Select(resource => resource.UriTemplate),
            prompts = CsdlProvider.ListPrompts().Prompts.Select(prompt => prompt.Name)
        },
        recentTraces = odataService.GetRecentTraces().Count
    });
});

app.MapGet("/api/csdl", () =>
{
    return Results.Ok(new
    {
        csdlPath = CsdlProvider.CsdlFilePath,
        csdl = CsdlProvider.GetCsdl(),
        csdlJson = CsdlProvider.GetCsdlJson(),
        csdlXml = CsdlProvider.GetCsdlXml(),
        edmElementsCount = GeneratedModel.SchemaElementsCount
    });
});

app.MapGet("/api/examples", () => Results.Ok(CsdlProvider.GetExampleQueries()));
app.MapGet("/api/analysis", () => Results.Ok(CsdlProvider.GetPromptArchitecturalAnalysis()));
app.MapGet("/api/catalog", () => Results.Ok(CsdlProvider.GetCatalog()));
app.MapGet("/api/projection", () => Results.Ok(CsdlProvider.GetProjection()));
app.MapGet("/api/traces", (IODataClientService odataService) => Results.Ok(odataService.GetRecentTraces()));

app.Run();

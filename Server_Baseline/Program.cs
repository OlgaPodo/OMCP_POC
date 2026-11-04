using GraphMcpServer.Csdl;
using GraphMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure port 3002
builder.WebHost.UseUrls("http://localhost:3002");

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

// Register Graph Client Service
builder.Services.AddHttpClient<GraphClientService>();
builder.Services.AddSingleton<GraphClientService>();
builder.Services.AddSingleton<CsdlMcpProjection>();
CsdlMcpProjection.Validate();

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
    .WithListPromptsHandler((_, _) => ValueTask.FromResult(UserCsdlProvider.ListPrompts()))
    .WithGetPromptHandler((request, _) => ValueTask.FromResult(UserCsdlProvider.GetPrompt(
        request.Params ?? throw new ArgumentException("Prompt parameters are required."))));

var app = builder.Build();

app.UseCors();

// Map MCP endpoints (handles POST / with MCP protocol)
app.MapMcp();

// Diagnostic and exploration API endpoints for Baseline Server
app.MapGet("/health", (GraphClientService graphService) =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "Baseline Microsoft Graph HTTP MCP Server (Without Entity-Set Examples)",
        mcpSpecification = "MCP 2.0 (2026-07-28)",
        protocolVersion = "2026-07-28",
        supportedVersions = new[] { "2026-07-28" },
        odataLibrary = "Microsoft.OData.Edm 9.0.0",
        port = 3002,
        mcpEndpoint = "http://localhost:3002/",
        hasCsdlPrompts = UserCsdlProvider.ListPrompts().Prompts.Count > 0,
        hasEntitySetExamples = UserCsdlProvider.GetExampleQueries().Count > 0,
        capabilities = new
        {
            discovery = "server/discover",
            transport = "Streamable HTTP (Stateless, Single Endpoint)",
            polymorphicResults = true,
            perRequestMeta = true,
            tools = CsdlMcpProjection.ListTools().Tools.Select(tool => tool.Name),
            resources = UserCsdlProvider.GetProjectedResources().Select(resource => resource["UriTemplate"]!.GetValue<string>()),
            prompts = UserCsdlProvider.ListPrompts().Prompts.Select(prompt => prompt.Name)
        },
        recentTraces = graphService.GetRecentTraces().Count
    });
});

app.MapGet("/api/csdl", () =>
{
    return Results.Ok(new
    {
        csdl = UserCsdlProvider.GetCsdl(),
        csdlJson = UserCsdlProvider.GetCsdlJson(),
        csdlXml = UserCsdlProvider.GetCsdlXml(),
        edmElementsCount = UserCsdlProvider.EdmModel.SchemaElements.Count()
    });
});

app.MapGet("/api/analysis", () =>
{
    return Results.Ok(UserCsdlProvider.GetPromptArchitecturalAnalysis());
});

app.MapGet("/api/catalog", () => Results.Ok(UserCsdlProvider.GetCatalog()));
app.MapGet("/api/projection", () => Results.Ok(UserCsdlProvider.GetProjection()));

app.MapGet("/api/traces", (GraphClientService graphService) =>
{
    return Results.Ok(graphService.GetRecentTraces());
});

app.Run();

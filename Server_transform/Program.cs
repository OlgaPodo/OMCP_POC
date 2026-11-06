using GraphMcpServer.Csdl;
using GraphMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Initialize CSDL Provider from configuration (defaults to C:\MCP\Csdl\Server_Large.csdl)
UserCsdlProvider.Initialize(builder.Configuration);

// Configure port 3003 for LargeServer
builder.WebHost.UseUrls("http://localhost:3011");

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

// Diagnostic and exploration API endpoints for Large MCP Server
app.MapGet("/health", (GraphClientService graphService) =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "Transformed Microsoft Graph HTTP MCP Server (Server_WithPrompts.csdl)",
        mcpSpecification = "MCP 2.0 (2026-07-28)",
        protocolVersion = "2026-07-28",
        supportedVersions = new[] { "2026-07-28" },
        odataLibrary = "Microsoft.OData.Edm 9.0.0",
        port = 3011,
        mcpEndpoint = "http://localhost:3011/",
        csdlPath = UserCsdlProvider.CsdlFilePath,
        hasCsdlPrompts = UserCsdlProvider.ListPrompts().Prompts.Count > 0,
        hasEntitySetExamples = UserCsdlProvider.GetExampleQueries().Count > 0,
        entityTypesCount = UserCsdlProvider.EdmModel.SchemaElements.OfType<Microsoft.OData.Edm.IEdmEntityType>().Count(),
        operationsCount = UserCsdlProvider.EdmModel.SchemaElements.OfType<Microsoft.OData.Edm.IEdmOperation>().Count(),
        examplesCount = UserCsdlProvider.GetExampleQueries().Count,
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
        csdlPath = UserCsdlProvider.CsdlFilePath,
        csdl = UserCsdlProvider.GetCsdl(),
        csdlJson = UserCsdlProvider.GetCsdlJson(),
        csdlXml = UserCsdlProvider.GetCsdlXml(),
        edmElementsCount = UserCsdlProvider.EdmModel.SchemaElements.Count()
    });
});

app.MapGet("/api/examples", () => Results.Ok(UserCsdlProvider.GetExampleQueries()));
app.MapGet("/api/analysis", () => Results.Ok(UserCsdlProvider.GetPromptArchitecturalAnalysis()));
app.MapGet("/api/catalog", () => Results.Ok(UserCsdlProvider.GetCatalog()));
app.MapGet("/api/projection", () => Results.Ok(UserCsdlProvider.GetProjection()));
app.MapGet("/api/traces", (GraphClientService graphService) => Results.Ok(graphService.GetRecentTraces()));

app.Run();

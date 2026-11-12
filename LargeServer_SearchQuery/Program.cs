using GraphMcpServer.Csdl;
using GraphMcpServer.Services;

var builder = WebApplication.CreateBuilder(args);

// Initialize CSDL Provider from configuration (defaults to C:\MCP\Csdl\Server_Large_WithExamples.csdl)
UserCsdlProvider.Initialize(builder.Configuration);

// Configure port 3005 for LargeServer_SearchQuery (Intent Identification + OData Query Construction)
builder.WebHost.UseUrls("http://localhost:3005");

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
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<GraphClientService>();
builder.Services.AddSingleton<GraphClientService>();
builder.Services.AddSingleton<CsdlMcpProjection>();
CsdlMcpProjection.Validate();

// Register 2-Tool MCP server (search_intent + execute_odata_query)
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
    .WithListPromptsHandler((_, _) => ValueTask.FromResult(CsdlMcpProjection.ListPrompts()));

var app = builder.Build();

app.UseCors();

// Map MCP endpoints (handles POST / with MCP protocol)
app.MapMcp();

// Diagnostic and exploration API endpoints
app.MapGet("/health", (GraphClientService graphService) =>
{
    return Results.Ok(new
    {
        status = "Healthy",
        server = "Microsoft Graph Large M365 Intent-Search + OData Query Construction MCP Server (Server_Large_WithExamples.csdl)",
        architecture = "search_intent + execute_odata_query",
        mcpSpecification = "MCP 2.0 (2026-07-28)",
        protocolVersion = "2026-07-28",
        supportedVersions = new[] { "2026-07-28" },
        odataLibrary = "Microsoft.OData.Edm 9.0.0",
        port = 3005,
        mcpEndpoint = "http://localhost:3005/",
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
            resources = Array.Empty<string>(),
            prompts = Array.Empty<string>()
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
app.MapGet("/api/projection", () => Results.Ok(new
{
    csdlPath = UserCsdlProvider.CsdlFilePath,
    architecture = "search_intent + execute_odata_query",
    tools = CsdlMcpProjection.ListTools().Tools,
    resources = Array.Empty<object>(),
    prompts = UserCsdlProvider.GetCatalog(),
    exampleQueries = UserCsdlProvider.GetExampleQueries(),
    mcpTools = CsdlMcpProjection.ListTools().Tools,
    mcpResources = Array.Empty<object>(),
    mcpResourceTemplates = Array.Empty<object>()
}));
app.MapGet("/api/traces", (GraphClientService graphService) => Results.Ok(graphService.GetRecentTraces()));

// OData v4.0/v4.01 upstream endpoint for CsdlToMcpTransformer-generated servers (:3003 & :3004)
app.MapMethods("/odata/{**odataPath}", ["GET", "POST"], async (HttpContext ctx, string odataPath, CsdlMcpProjection proj, CancellationToken ct) =>
{
    var fullQuery = $"{ctx.Request.Method} {odataPath}{ctx.Request.QueryString}";
    var result = await proj.ExecuteODataQueryAsync(fullQuery, ct);
    if (result == null)
    {
        return Results.NotFound(new { error = new { code = "ResourceNotFound", message = $"No OData resource matched '{fullQuery}'." } });
    }
    return Results.Ok(result);
});

app.Run();

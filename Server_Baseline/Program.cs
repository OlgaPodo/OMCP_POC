using GraphMcpServer.Csdl;
using GraphMcpServer.Prompts;
using GraphMcpServer.Resources;
using GraphMcpServer.Services;
using GraphMcpServer.Tools;

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

// Register MCP server with HTTP transport, Tools, Resources, and Prompts
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<GraphTools>()
    .WithResources<GraphResources>()
    .WithPrompts<GraphPrompts>();

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
        server = "Baseline Microsoft Graph HTTP MCP Server (Without Examples in Prompts)",
        mcpSpecification = "MCP 2.0 (2026-07-28)",
        protocolVersion = "2026-07-28",
        supportedVersions = new[] { "2026-07-28" },
        port = 3002,
        mcpEndpoint = "http://localhost:3002/",
        hasPromptExamples = false,
        capabilities = new
        {
            discovery = "server/discover",
            transport = "Streamable HTTP (Stateless, Single Endpoint)",
            polymorphicResults = true,
            perRequestMeta = true,
            tools = new[] { "find_user", "user_profile" },
            resources = new[] { "users://me", "users://{userId}" },
            prompts = new[]
            {
                "whom_olga_reports_to",
                "who_are_olgas_peers",
                "direct_reports_of_manager",
                "search_users_by_dept_title",
                "user_quick_lookup"
            }
        },
        recentTraces = graphService.GetRecentTraces().Count
    });
});

app.MapGet("/api/csdl", () =>
{
    return Results.Ok(new
    {
        csdlJson = UserCsdlProvider.GetCsdlJson(),
        csdlXml = UserCsdlProvider.GetCsdlXml()
    });
});

app.MapGet("/api/analysis", () =>
{
    return Results.Ok(UserCsdlProvider.GetPromptArchitecturalAnalysis());
});

app.MapGet("/api/traces", (GraphClientService graphService) =>
{
    return Results.Ok(graphService.GetRecentTraces());
});

app.Run();

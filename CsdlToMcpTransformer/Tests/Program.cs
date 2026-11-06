using System.Net;
using System.Text.Json;
using System.Xml;
using GraphMcpServer.Csdl;
using GraphMcpServer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Vocabularies;
using ModelContextProtocol.Protocol;

var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Inventory.csdl");
Check(CsdlToMcpTransformer.Program.Main(["--help"]) == 0, "CLI --help exits with 0");
Check(CsdlToMcpTransformer.Program.Main([]) == 1, "CLI with no args displays usage and exits with 1");

var generated = Path.Combine(Path.GetTempPath(), "CsdlToMcpTests-" + Guid.NewGuid().ToString("N"));
var directGenerated = Path.Combine(Path.GetTempPath(), "CsdlToMcpTestsDirect-" + Guid.NewGuid().ToString("N"));
try
{
    CsdlToMcpTransformer.Program.Main(["--single", fixture, generated, "3444"]);
    Check(File.Exists(Path.Combine(generated, "metadata.csdl")) &&
        File.Exists(Path.Combine(generated, "Csdl", "EdmJsonSchema.cs")) &&
        File.Exists(Path.Combine(generated, "Csdl", "CsdlProvider.cs")) &&
        !File.Exists(Path.Combine(generated, "Csdl", "UserCsdlProvider.cs")) &&
        !File.Exists(Path.Combine(generated, "Services", "GraphClientService.cs")), "Fresh standalone generation via --single");
    using (var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(generated, "appsettings.json"))))
        Check(settings.RootElement.GetProperty("Csdl").GetProperty("FileName").GetString() == "metadata.csdl", "Portable generated CSDL configuration");
    Directory.CreateDirectory(Path.Combine(generated, "Models"));
    File.WriteAllText(Path.Combine(generated, "Models", "GraphModels.cs"), "legacy");
    File.WriteAllText(Path.Combine(generated, "Services", "GraphClientService.cs"), "legacy");
    File.Copy(Path.Combine(generated, "Csdl", "CsdlProvider.cs"),
        Path.Combine(generated, "Csdl", "UserCsdlProvider.cs"));
    CsdlToMcpTransformer.Program.Main(["--single", fixture, generated, "3444"]);
    Check(!File.Exists(Path.Combine(generated, "Models", "GraphModels.cs")) &&
        !File.Exists(Path.Combine(generated, "Services", "GraphClientService.cs")) &&
        !File.Exists(Path.Combine(generated, "Csdl", "UserCsdlProvider.cs")), "Regeneration removes obsolete generated code");

    // Test direct CLI invocation without --single flag
    Check(CsdlToMcpTransformer.Program.Main([fixture, directGenerated, "3445"]) == 0, "Direct single-server generation without --single");
    Check(File.Exists(Path.Combine(directGenerated, "metadata.csdl")) &&
        File.Exists(Path.Combine(directGenerated, "GraphMcpServer.csproj")) &&
        File.Exists(Path.Combine(directGenerated, "Program.cs")), "Direct single-server project structure verified");
}
finally
{
    if (Directory.Exists(generated)) Directory.Delete(generated, true);
    if (Directory.Exists(directGenerated)) Directory.Delete(directGenerated, true);
}
CsdlProvider.Initialize(fixture);
var tools = CsdlProvider.GetProjectedTools();
Check(CsdlProvider.EdmModel.EntityContainer.FullName() == "Inventory.Warehouse", "Arbitrary namespace/container");
Check(tools.Any(t => t.Name == "list_Products") && tools.Any(t => t.Name == "get_Featured"), "Entity sets and singletons");
Check(tools.Any(t => t.Name == "get_Products_Supplier"), "Navigation properties");
Check(tools.Any(t => t.Name == "Specials_Inventory_Price"), "Inherited operation binding and keys");
Check(tools.Count(t => t.Name.StartsWith("Products_Inventory_Search_")) == 2, "Overload names");
Check(tools.Select(t => t.Name).Distinct().Count() == tools.Count, "Unique tool names");
var actionSchema = CsdlMcpProjection.ListTools().Tools.Single(t => t.Name == "Products_Inventory_Restock").InputSchema;
Check(actionSchema.GetProperty("properties").GetProperty("quantity").GetProperty("type").GetString() == "integer" &&
    actionSchema.GetProperty("properties").GetProperty("quantity").GetProperty("maximum").GetInt64() == int.MaxValue &&
    actionSchema.GetProperty("required").EnumerateArray().Any(a => a.GetString() == "quantity"), "Exact integer schema and required arguments");
Check(actionSchema.GetProperty("properties").GetProperty("quantity").TryGetProperty("description", out var qDesc) &&
    !string.IsNullOrWhiteSpace(qDesc.GetString()), "Parameter descriptions in inputSchema");
var listSchema = CsdlMcpProjection.ListTools().Tools.Single(t => t.Name == "list_Products").InputSchema;
Check(listSchema.GetProperty("properties").GetProperty("filter").TryGetProperty("description", out var filterDesc) &&
    !string.IsNullOrWhiteSpace(filterDesc.GetString()), "Query option descriptions in inputSchema");
Check(actionSchema.GetProperty("properties").GetProperty("address").GetProperty("$ref").GetString() == "#/$defs/Inventory.Address" &&
    actionSchema.GetProperty("$defs").GetProperty("Inventory.Address").GetProperty("required")[0].GetString() == "City" &&
    actionSchema.GetProperty("properties").GetProperty("tags").GetProperty("type").GetString() == "array", "Structured and collection input schemas");
Check(CsdlProvider.ListPrompts().Prompts.Count == 0 && CsdlProvider.GetExampleQueries().Count == 0, "No Agent vocabulary required");
using (var reader = JsonDocument.Parse(CsdlProvider.GetCsdlJson()))
    Check(reader.RootElement.GetProperty("$EntityContainer").GetString() == "Inventory.Warehouse", "Library JSON export");

var handler = new RecordingHandler();
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OData:BaseUrl"] = "http://localhost/odata/" }).Build();
var client = new ODataClientService(new HttpClient(handler), config, NullLogger<ODataClientService>.Instance);
var projection = new CsdlMcpProjection(client, NullLogger<CsdlMcpProjection>.Instance);
await Call("list_Products", """{"filter":"Name eq 'a/b'","top":3,"count":true}""");
Check(handler.Method == "GET" && handler.Path.Contains("$filter=Name%20eq%20%27a%2Fb%27") &&
    handler.Path.Contains("$top=3") && handler.Path.Contains("$count=true"), "Typed query options");
await Call("get_Suppliers", """{"key_Region":"O'Brien/West","key_Code":12}""");
Check(Uri.UnescapeDataString(handler.Path).Contains("Suppliers(Region='O''Brien/West',Code=12)"), "Composite and escaped keys");
await projection.ReadResourceAsync(new() { Uri = "odata://Inventory.Warehouse/Suppliers/O%27Brien%2FWest/12" }, default);
Check(Uri.UnescapeDataString(handler.Path).Contains("Suppliers(Region='O''Brien/West',Code=12)"), "Composite resource routing");
await projection.ReadResourceAsync(new() { Uri = "odata://Inventory.Warehouse/Products/7" }, default);
Check(handler.Path.Contains("Products(7)"), "Numeric resource keys");
await Call("get_Products_Supplier", """{"key_Id":7}""");
Check(handler.Path.Contains("Products(7)/Supplier"), "Navigation execution");
await Call("Products_Inventory_Discount", """{"key_Id":7}""");
Check(handler.Path.Contains("Products(7)/Inventory.SpecialItem/Inventory.Discount"), "Derived operation type cast");
await Call("Specials_Inventory_Price", """{"key_Id":7,"tax":0.2}""");
Check(Uri.UnescapeDataString(handler.Path).Contains("Specials(7)/Inventory.Price(tax=@tax)?@tax=0.2"), "Bound function execution");
var search = tools.Single(t => t.EntitySet == "Products" && t.Operation?.Name == "Search" && t.Arguments.Any(a => a.Name == "q"));
await Call(search.Name, """{"q":"O'Brien"}""");
Check(Uri.UnescapeDataString(handler.Path).Contains("Products/Inventory.Search(q=@q)?@q='O''Brien'"), "Collection function and string literal");
await Call("AvailableColors", """{"color":"Red"}""");
Check(Uri.UnescapeDataString(handler.Path).Contains("AvailableColors(color=@color)?@color=Inventory.Color'Red'"), "Imported function and enum parameter");
await Call("Products_Inventory_Restock", """{"key_Id":7,"quantity":3,"address":{"City":"Paris"},"tags":["new"]}""");
Check(handler.Method == "POST" && handler.Path.Contains("Products(7)/Inventory.Restock"), "Bound action path");
using (var body = JsonDocument.Parse(handler.Body!))
    Check(!body.RootElement.TryGetProperty("key_Id", out _) && body.RootElement.GetProperty("quantity").GetInt32() == 3 &&
        body.RootElement.GetProperty("address").GetProperty("City").GetString() == "Paris", "Typed action body");
await Call("ResetInventory", """{"confirm":true}""");
Check(handler.Method == "POST" && handler.Path.EndsWith("ResetInventory"), "Imported action");
var count = handler.Calls;
await Rejected("get_Products", """{"key_Id":"7"}""");
await Rejected("get_Products", """{"key_Id":2147483648}""");
await Rejected("get_Products", """{}""");
await Rejected("list_Products", """{"unexpected":1}""");
await Rejected("list_Products", """{"top":-1}""");
await Rejected("AvailableColors", """{"color":"Green"}""");
await Rejected("Products_Inventory_Restock", """{"key_Id":1,"quantity":1,"address":{},"tags":[]}""");
Check(handler.Calls == count, "Invalid arguments do not reach upstream");
handler.Status = HttpStatusCode.NotFound;
var failed = await projection.CallToolAsync(new() { Name = "get_Products", Arguments = Arguments("""{"key_Id":42}""") }, default);
Check(failed.IsError == true, "Upstream failures are MCP errors");
handler.Status = HttpStatusCode.OK;
handler.ResponseBody = "not JSON";
var malformed = await projection.CallToolAsync(new() { Name = "list_Products" }, default);
Check(malformed.IsError == true, "Malformed upstream JSON is an explicit tool error");
handler.ResponseBody = """{"value":[{"Id":7}]}""";
handler.Status = HttpStatusCode.NoContent;
var empty = await projection.CallToolAsync(new() { Name = "ResetInventory", Arguments = Arguments("""{"confirm":true}""") }, default);
Check(empty.IsError != true && ((TextContentBlock)empty.Content.Single()).Text == "null", "Void action response");
var noConfigClient = new ODataClientService(new HttpClient(handler), new ConfigurationBuilder().Build(), NullLogger<ODataClientService>.Instance);
var unconfigured = await new CsdlMcpProjection(noConfigClient, NullLogger<CsdlMcpProjection>.Instance)
    .CallToolAsync(new() { Name = "list_Products" }, default);
Check(unconfigured.IsError == true, "Missing endpoint is an explicit failure");
Check(client.GetRecentTraces().Count > 0, "Request traces");
var trace = client.GetRecentTraces().First();
Check(trace.DurationMs >= 0 && trace.Timestamp <= DateTimeOffset.UtcNow, "Trace timestamp and duration metrics");
CsdlProvider.Initialize(fixture);
Check(CsdlProvider.GetProjectedTools().Select(t => t.Name).SequenceEqual(tools.Select(t => t.Name)), "Stable names across reloads");

foreach (var (name, prompts, examples) in new[] { ("Server_Baseline.csdl", 0, 0), ("Server_WithPrompts.csdl", 12, 0), ("Server_Large.csdl", 22, 28) })
{
    var path = Path.Combine(args.Length > 0 ? args[0] : @"C:\MCP\Csdl", name);
    CsdlProvider.Initialize(path);
    Check(CsdlProvider.ListPrompts().Prompts.Count == prompts && CsdlProvider.GetExampleQueries().Count == examples, $"{name}: annotation counts");
    Check(CsdlProvider.GetProjectedTools().Any(t => t.Name == "list_users"), $"{name}: generic discovery");
    foreach (var annotation in CsdlProvider.EdmModel.VocabularyAnnotations.Where(a => a.Term.FullName() == "Agent.Prompts"))
    {
        foreach (var expression in ((IEdmCollectionExpression)annotation.Value).Elements)
        {
            var record = (IEdmRecordExpression)expression;
            var promptName = ((IEdmStringConstantExpression)record.Properties.Single(p => p.Name == "Name").Value).Value;
            var result = CsdlProvider.GetPrompt(new() { Name = promptName });
            var messages = ((IEdmCollectionExpression)record.Properties.Single(p => p.Name == "Messages").Value).Elements.Cast<IEdmRecordExpression>().ToList();
            Check(result.Messages.Count == messages.Count, $"{promptName}: all messages");
            var defaults = ((IEdmCollectionExpression)record.Properties.Single(p => p.Name == "Arguments").Value).Elements.Cast<IEdmRecordExpression>()
                .ToDictionary(a => ((IEdmStringConstantExpression)a.Properties.Single(p => p.Name == "Name").Value).Value,
                    a => ((IEdmStringConstantExpression)a.Properties.Single(p => p.Name == "Default").Value).Value);
            for (var i = 0; i < messages.Count; i++)
            {
                var content = (IEdmRecordExpression)messages[i].Properties.Single(p => p.Name == "Content").Value;
                var text = ((IEdmStringConstantExpression)content.Properties.Single(p => p.Name == "Text").Value).Value;
                var expected = System.Text.RegularExpressions.Regex.Replace(text, @"\{([A-Za-z0-9_]+)\}", m =>
                    defaults.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Value);
                Check(((TextContentBlock)result.Messages[i].Content).Text == expected, $"{promptName}: rendering parity");
            }
        }
    }
    using var json = JsonDocument.Parse(CsdlProvider.GetCsdlJson());
    using var xml = XmlReader.Create(new StringReader(CsdlProvider.GetCsdlXml()));
    Check(CsdlReader.TryParse(xml, out var model, out _) &&
        model.SchemaElements.Count() == CsdlProvider.EdmModel.SchemaElements.Count(), $"{name}: original EDM retained");
}
Console.WriteLine("All generic pipeline tests passed.");

static Dictionary<string, JsonElement> Arguments(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
async Task Call(string name, string json)
{
    var result = await projection.CallToolAsync(new() { Name = name, Arguments = Arguments(json) }, default);
    Check(result.IsError != true, $"Successful tool: {name}");
}
async Task Rejected(string name, string json)
{
    var result = await projection.CallToolAsync(new() { Name = name, Arguments = Arguments(json) }, default);
    Check(result.IsError == true, $"Rejected arguments: {name}");
}
static void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException($"FAILED: {description}");
    Console.WriteLine($"PASS: {description}");
}

sealed class RecordingHandler : HttpMessageHandler
{
    public string Method { get; private set; } = "";
    public string Path { get; private set; } = "";
    public string? Body { get; private set; }
    public int Calls { get; private set; }
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = """{"value":[{"Id":7}]}""";
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        Method = request.Method.Method;
        Path = request.RequestUri!.AbsoluteUri;
        Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new(Status) { Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json") };
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;

namespace CsdlToMcpTransformer;

public static class Program
{
    public record TransformTarget(string CsdlFileName, string OutputDir, int Port, string ServerTitle);

    public static int Main(string[] args)
    {
        if (args.Length == 0 || (args.Length > 0 && args[0] is "--help" or "-h" or "help"))
        {
            Console.WriteLine("CsdlToMcpTransformer - Generic CSDL OData to MCP 2.0 Server Generator");
            Console.WriteLine();
            Console.WriteLine("Generates a single standalone MCP 2.0 HTTP server project for a given CSDL schema.");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  dotnet run --project CsdlToMcpTransformer.csproj -- <csdl-path> <output-directory> [port]");
            Console.WriteLine("  dotnet run --project CsdlToMcpTransformer.csproj -- --single <csdl-path> <output-directory> [port]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  <csdl-path>         Full path or filename (relative to Csdl/) of the OData CSDL file");
            Console.WriteLine("  <output-directory>  Destination directory where the MCP server project will be written");
            Console.WriteLine("  [port]              HTTP port for the generated MCP server (default: 3001)");
            return args.Length == 0 ? 1 : 0;
        }

        var csdlDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Csdl"));
        if (!Directory.Exists(csdlDir))
        {
            var fallback = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Csdl"));
            csdlDir = Directory.Exists(fallback) ? fallback : @"C:\MCP\Csdl";
        }

        string csdlInput;
        string outputDir;
        int port = 3001;

        if (args[0] == "--single")
        {
            if (args.Length < 3 || args.Length > 4)
                throw new ArgumentException("Usage: --single <CSDL filename or full path> <output directory> [port 1-65535]");
            csdlInput = args[1];
            outputDir = args[2];
            if (args.Length == 4 && (!int.TryParse(args[3], out port) || port is < 1 or > 65535))
                throw new ArgumentException($"Invalid port number: '{args[3]}'. Port must be between 1 and 65535.");
        }
        else
        {
            if (args.Length < 2 || args.Length > 3)
                throw new ArgumentException("Usage: <CSDL filename or full path> <output directory> [port 1-65535]");
            csdlInput = args[0];
            outputDir = args[1];
            if (args.Length == 3 && (!int.TryParse(args[2], out port) || port is < 1 or > 65535))
                throw new ArgumentException($"Invalid port number: '{args[2]}'. Port must be between 1 and 65535.");
        }

        var csdlPath = Path.IsPathRooted(csdlInput) ? csdlInput : Path.Combine(csdlDir, csdlInput);
        var target = new TransformTarget(Path.GetFileName(csdlPath), outputDir, port, $"CSDL MCP Server ({Path.GetFileName(csdlPath)})");
        TransformCsdlToMcpServer(csdlPath, target);

        return 0;
    }

    private static string ReadTemplate(string relativePath)
    {
        var resourceName = $"CsdlToMcpTransformer.Templates.{relativePath.Replace('\\', '.')}";
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded server source template: {resourceName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void TransformCsdlToMcpServer(string csdlPath, TransformTarget target)
    {
        if (!File.Exists(csdlPath))
            throw new FileNotFoundException($"CSDL file not found: {csdlPath}");

        var csdlText = File.ReadAllText(csdlPath);
        using var xmlReader = XmlReader.Create(new StringReader(csdlText));
        if (!CsdlReader.TryParse(xmlReader, out var edmModel, out var parseErrors))
        {
            var msg = string.Join("; ", parseErrors.Select(e => $"{e.ErrorCode}: {e.ErrorMessage}"));
            throw new InvalidOperationException($"CSDL parse failed for {csdlPath}: {msg}");
        }

        if (!edmModel.Validate(out var validationErrors))
        {
            var msg = string.Join("; ", validationErrors.Select(e => $"{e.ErrorCode}: {e.ErrorMessage}"));
            throw new InvalidOperationException($"OData EDM validation failed for {csdlPath}: {msg}");
        }

        var entityTypes = edmModel.SchemaElements.OfType<IEdmEntityType>().Select(e => e.FullName()).ToList();
        var operations = edmModel.SchemaElements.OfType<IEdmOperation>().Select(o => $"{o.SchemaElementKind}:{o.FullName()}").ToList();
        var container = edmModel.EntityContainer ?? throw new InvalidOperationException("Missing EntityContainer in CSDL.");
        var entitySets = container.EntitySets().Select(es => es.Name).ToList();
        var singletons = container.Singletons().Select(s => s.Name).ToList();

        var promptsCount = edmModel.VocabularyAnnotations.Where(a => a.Term.FullName() == "Agent.Prompts")
            .Sum(a => a.Value is Microsoft.OData.Edm.Vocabularies.IEdmCollectionExpression c ? c.Elements.Count() : 0);
        var examplesCount = edmModel.VocabularyAnnotations.Where(a => a.Term.FullName() is "Agent.Examples" or "Agent.ExampleQueries")
            .Sum(a => a.Value is Microsoft.OData.Edm.Vocabularies.IEdmCollectionExpression c ? c.Elements.Count() : 0);

        // Create output directory structure
        Directory.CreateDirectory(target.OutputDir);
        Directory.CreateDirectory(Path.Combine(target.OutputDir, "Csdl"));
        Directory.CreateDirectory(Path.Combine(target.OutputDir, "Services"));

        // Emit the project-owned CSDL-driven engine source.
        File.WriteAllText(Path.Combine(target.OutputDir, "GraphMcpServer.csproj"), ReadTemplate("GraphMcpServer.csproj"));
        var providerCs = ReadTemplate(@"Csdl\CsdlProvider.cs");
        File.WriteAllText(Path.Combine(target.OutputDir, "Csdl", "CsdlProvider.cs"), providerCs);
        File.WriteAllText(Path.Combine(target.OutputDir, "Csdl", "CsdlMcpProjection.cs"), ReadTemplate(@"Csdl\CsdlMcpProjection.cs"));
        File.WriteAllText(Path.Combine(target.OutputDir, "Csdl", "EdmJsonSchema.cs"), ReadTemplate(@"Csdl\EdmJsonSchema.cs"));
        File.WriteAllText(Path.Combine(target.OutputDir, "Services", "ODataClientService.cs"), ReadTemplate(@"Services\ODataClientService.cs"));
        File.WriteAllText(Path.Combine(target.OutputDir, "metadata.csdl"), csdlText);
        foreach (var legacyFile in new[] { @"Models\GraphModels.cs", @"Services\GraphClientService.cs", @"Csdl\UserCsdlProvider.cs" })
        {
            var legacyPath = Path.Combine(target.OutputDir, legacyFile);
            if (File.Exists(legacyPath)) File.Delete(legacyPath);
        }

        // 2. Write appsettings.json pointing to the target CSDL
        var appSettings = new JsonObject
        {
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["Microsoft.AspNetCore"] = "Warning"
                }
            },
            ["Csdl"] = new JsonObject
            {
                ["FileName"] = "metadata.csdl"
            },
            ["OData"] = new JsonObject { ["BaseUrl"] = "", ["Scope"] = "" }
        };
        File.WriteAllText(
            Path.Combine(target.OutputDir, "appsettings.json"),
            appSettings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        // 3. Generate Program.cs configured for target.Port and target.CsdlFileName
        var programCs = ReadTemplate("Program.cs")
            .Replace("__SERVER_PORT__", target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("__SERVER_TITLE__", JsonSerializer.Serialize(target.ServerTitle));
        File.WriteAllText(Path.Combine(target.OutputDir, "Program.cs"), programCs);

        // 4. Write transformation-manifest.json
        var manifest = new JsonObject
        {
            ["generator"] = "CsdlToMcpTransformer",
            ["projectionMode"] = "generic-edm",
            ["sourceCsdl"] = csdlPath,
            ["outputProject"] = target.OutputDir,
            ["port"] = target.Port,
            ["endpoint"] = $"http://localhost:{target.Port}/",
            ["entityContainer"] = container.FullName(),
            ["entityTypesCount"] = entityTypes.Count,
            ["entityTypes"] = new JsonArray(entityTypes.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["entitySetsCount"] = entitySets.Count,
            ["entitySets"] = new JsonArray(entitySets.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["singletonsCount"] = singletons.Count,
            ["singletons"] = new JsonArray(singletons.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["operationsCount"] = operations.Count,
            ["operations"] = new JsonArray(operations.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["promptsCount"] = promptsCount,
            ["examplesCount"] = examplesCount
        };
        File.WriteAllText(
            Path.Combine(target.OutputDir, "transformation-manifest.json"),
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"[Transformed] {target.CsdlFileName} -> {target.OutputDir} (http://localhost:{target.Port}/)");
        Console.WriteLine($"   EntityTypes: {entityTypes.Count} | EntitySets: {entitySets.Count} | Singletons: {singletons.Count} | Operations: {operations.Count} | Prompts: {promptsCount} | Examples: {examplesCount}");
    }
}

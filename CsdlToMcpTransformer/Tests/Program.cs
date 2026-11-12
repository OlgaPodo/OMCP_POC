using System.Diagnostics;
using System.Net;
using System.Text.Json;

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
    Check(!project.Contains("Microsoft.OData.Edm", StringComparison.Ordinal) &&
          !project.Contains("metadata.csdl", StringComparison.Ordinal),
        "Generated project has no runtime EDM or CSDL dependency");

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

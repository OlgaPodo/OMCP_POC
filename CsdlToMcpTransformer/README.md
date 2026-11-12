# Generic CSDL-to-MCP Transformer

The generation pipeline is **CSDL XML -> validated OData EDM model -> generated
static C# MCP bindings -> JSON at the protocol boundary**. `IEdmModel` is used
only by the transformer process. Generated servers do not load, parse, copy, or
require CSDL at runtime.

## Generate a single MCP server

The transformer generates **a single standalone MCP server project for a given CSDL schema at a time**.

```powershell
# Standard usage with full path from C:\MCP\Csdl:
dotnet run --project C:\MCP\CsdlToMcpTransformer\CsdlToMcpTransformer.csproj -- C:\MCP\Csdl\Server_Large.csdl C:\MCP\LargeServer_transform 3013

# Or using filename relative to C:\MCP\Csdl:
dotnet run --project C:\MCP\CsdlToMcpTransformer\CsdlToMcpTransformer.csproj -- Server_WithPrompts.csdl C:\MCP\Server_transform 3011

# Or with the optional --single flag:
dotnet run --project C:\MCP\CsdlToMcpTransformer\CsdlToMcpTransformer.csproj -- --single Server_Baseline.csdl C:\MCP\Server_Baseline_transform 3012
```

- `<csdl-path>` accepts a full file path (e.g. `C:\MCP\Csdl\Server_Large.csdl`), or a filename relative to `C:\MCP\Csdl`.
- `<output-directory>` is the target destination where the standalone project will be generated (e.g. `C:\MCP\LargeServer_transform`).
- `[port]` is optional (defaults to `3001` if omitted; range: 1–65535).
- Running without arguments or with `--help` displays command-line usage.

Each run generates a single self-contained server project containing:
- `GraphMcpServer.csproj` - The ASP.NET Core web project targeting `net10.0`
- `Program.cs` - Application bootstrap configured for the target port and title
- `appsettings.json` - Runtime OData backend settings
- `transformation-manifest.json` - Generation summary with schema statistics
- `Csdl/GeneratedModel.cs` - Static generated tools, resources, prompts, examples, argument types, and schemas
- `Csdl/CsdlProvider.cs` - Runtime access to the generated static model; no CSDL parser
- `Csdl/CsdlMcpProjection.cs` - MCP protocol handler (tools, resources, templates, prompts)
- `Csdl/EdmJsonSchema.cs` - Static argument validation and OData literal formatting
- `Services/ODataClientService.cs` - Upstream HTTP client with `DefaultAzureCredential` and metrics

Source templates under `Templates` are embedded directly in the transformer assembly.
Existing server projects are not consulted during generation. Regeneration
overwrites generated files and removes obsolete legacy code.

## Generic projection

Using schemas in `C:\MCP\Csdl` (such as `Server_Large.csdl` and `Server_WithPrompts.csdl`), OData EDM elements map to MCP primitives:

| EDM element (e.g. from `C:\MCP\Csdl\Server_Large.csdl`) | MCP projection |
|---|---|
| Entity set `users`, `groups`, `drives` | `list_users`, `get_users`, `list_groups`, `get_groups`; collection resources and keyed templates |
| Singleton `me` | `get_me` tool and fixed resource |
| Navigation property `users/manager`, `users/directReports` | `get_users_manager`, `get_users_directReports` |
| Bound function `Microsoft.Graph.searchDriveItems` on `drives` | `drives_Microsoft_Graph_searchDriveItems` |
| Bound action `Microsoft.Graph.sendChannelMessage` on `channels` | `channels_Microsoft_Graph_sendChannelMessage` |
| Bound action `Microsoft.Graph.completeTodoTask` on `todoTasks` | `todoTasks_Microsoft_Graph_completeTodoTask` |
| `@Agent.Prompts` (e.g. `whom_olga_reports_to`) | Typed prompt definitions, arguments/defaults, and text messages |
| `@Agent.Examples` / `@Agent.ExampleQueries` | Examples attached to their EDM annotation targets (e.g. `User`, `DriveItem`) |

For example, for `Server_Large.csdl` in `C:\MCP\Csdl`, resources use `odata://Microsoft.Graph.GraphService/users` and
`odata://Microsoft.Graph.GraphService/users/{key_id}`, while singletons map to `odata://Microsoft.Graph.GraphService/me`.
Composite keys produce one template segment per key. Resource key values are URI-encoded, unquoted values.
Read-tool keys are named `key_<property>` (e.g. `key_id`), and use the property's EDM type.

Read tools accept `filter`, `select`, `expand`, `orderby`, `top`, `skip`, `count`,
and `search`. Function arguments use OData parameter aliases; action arguments
become a typed JSON request body. Actions are the only generated write tools:
CRUD writes are not inferred from the mere existence of an entity set.

Input schemas and validation support Boolean, bounded integer, decimal/floating
point, string, GUID, date/time, duration, binary, enum, type-definition,
collection, and structured types. Recursive structured input schemas use `$defs`.
Unsupported input types (such as spatial types) fail explicitly at initialization.
Unbound operations require an operation import to be callable. Bound operations
must bind to an entity or entity collection. Prompt content currently supports
text with `user` or `assistant` roles. Agent annotations are optional.

### Configure the backend

Set `OData:BaseUrl` in generated `appsettings.json`, or through an environment
variable. For Microsoft Graph CSDL schemas in `C:\MCP\Csdl`, configure the Graph endpoint and optional scope for automatic Azure CLI or Managed Identity token acquisition:

```powershell
$env:OData__BaseUrl = "https://graph.microsoft.com/v1.0/"
$env:OData__Scope = "https://graph.microsoft.com/.default"
dotnet run --project C:\MCP\LargeServer_transform\GraphMcpServer.csproj
```

The backend must implement the paths/operations declared by the CSDL; the transformer does not map fictional
schema operations to unrelated service APIs.

Discovery and prompts work without a backend URL. Execution requires a configured
endpoint and sends real HTTP requests; it never falls back to mock data.
Invalid inputs and upstream HTTP failures return MCP tool errors and are logged.
HTTP redirects are disabled so backend credentials cannot follow a redirect.

## Diagnostics and compatibility

The existing `/health`, `/api/csdl`, `/api/examples`, `/api/analysis`,
`/api/catalog`, `/api/projection`, and `/api/traces` endpoints remain.
`csdlJson` is an **on-demand export using the OData library's `CsdlWriter`**,
not an intermediate representation used by the pipeline. The original XML and
the full EDM model, including overloads, are retained.

This is an intentional behavior change: Graph-specific tool names, resource
URIs, inferred prompt bindings, expanded response wrappers, and mock execution
are replaced with generic bindings and upstream OData responses. Existing
non-transformed servers remain unchanged. `verify_transform_parity.py` is a
legacy verifier for the previous Graph-specific projection, not this pipeline.

## Regression tests

```powershell
dotnet run --project C:\MCP\CsdlToMcpTransformer\Tests\PipelineTests.csproj
```

The dependency-light console runner compiles the templates and tests all three schemas
from `C:\MCP\Csdl` (`Server_Large.csdl`, `Server_WithPrompts.csdl`, `Server_Baseline.csdl`),
verifying all 34 prompt renderings and example counts. It also exercises edge-case OData
semantics (composite keys, derived types, enum parameters, structured payloads) using a test
fixture. HTTP execution uses an in-memory recording handler; tests do not authenticate or call a live service.

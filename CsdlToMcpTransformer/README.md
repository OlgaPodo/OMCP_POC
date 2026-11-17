# Generic CSDL-to-MCP Transformer

The generation pipeline is **CSDL XML -> validated OData EDM model -> generated
static C# MCP bindings -> JSON at the protocol boundary**. `IEdmModel` is used
only by the transformer process. Generated servers do not load, parse, copy, or
require CSDL at runtime.

## Generate a single MCP server

The transformer generates **a single standalone MCP server project for a given CSDL schema at a time**.

```powershell
# Standard usage with full path from C:\MCP\Csdl:
dotnet run --project C:\MCP\CsdlToMcpTransformer\CsdlToMcpTransformer.csproj -- C:\MCP\Csdl\Server_Large.csdl C:\MCP\LargeServer_transform 3010

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
- `Csdl/GeneratedModel.cs` - Static request bindings, prelinked resources and examples, MCP discovery metadata, prompt catalog, diagnostics, argument types, and schemas
- `Csdl/CsdlProvider.cs` - Runtime access to the generated static model; no CSDL parser
- `Csdl/CsdlMcpProjection.cs` - MCP protocol handler (tools, resources, templates, prompts)
- `Csdl/EdmJsonSchema.cs` - Static argument validation and OData literal formatting
- `Services/GeneratedGraphExecutor.cs` - executes Graph plans selected by OData request bindings emitted by `StaticModelGenerator`
- `Services/GraphClientService.cs` and `Models/GraphModels.cs` - direct authenticated Microsoft Graph execution and correlated traces
- `Services/ODataClientService.cs` - in-process bridge from generated MCP bindings to the Graph dispatcher; no local HTTP adapter

Source templates under `Templates` are embedded directly in the transformer assembly.
Existing server projects are not consulted during generation. Regeneration
overwrites generated files and removes obsolete legacy code.

All schema-derived generation runs in this project: CSDL parsing and validation,
request/schema generation, resource linking and URI-pattern construction,
example association, prompt query extraction and catalog inference, and MCP
discovery/diagnostic projection. The generated server only serves this metadata,
renders prompts with request arguments, validates inputs, matches resource URIs,
and executes requests. It has no reference to the transformer or ODL.

Generated `ODataRequestBinding` records describe OData requests shared by MCP tools
and resources. `GeneratedModel.RequestBindings` and `CsdlProvider.GetRequestBindings()`
expose this metadata. Each `ResourceBinding.RequestBinding` directly references
its generated request binding; matching examples are already attached to that
binding. A resource read executes it directly, not through an MCP tool.
Tool names, resource URIs, and external JSON fields remain unchanged.

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

### Execute against Microsoft Graph

Generated servers translate MCP calls to Graph operations inside the same process.
They acquire a delegated Microsoft Graph token through Azure CLI credentials and
call `https://graph.microsoft.com/v1.0/` directly. No `OData:BaseUrl`, localhost
proxy, search server, or HTTP adapter is required.

Discovery and prompts work without authentication. Tool and resource execution
requires `az login` with the Graph permissions required by the requested workload.
Graph failures and missing permissions return MCP errors and correlated traces.

## Diagnostics and compatibility

The existing `/health`, `/api/csdl`, `/api/examples`, `/api/analysis`,
`/api/catalog`, `/api/projection`, and `/api/traces` endpoints remain.
`csdlJson` is exported by the transformer's OData `CsdlWriter` at generation time
and embedded alongside the original XML. At runtime the endpoint only decodes
the embedded metadata; no ODL export or schema inference occurs. JSON is not an
intermediate representation used by the binding-generation pipeline.

This is an intentional behavior change: Graph-specific tool names, resource
URIs, inferred prompt bindings, expanded response wrappers, and mock execution
are replaced with generic bindings and in-process Microsoft Graph responses. Existing
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
The runner also references `LargeServer` to verify request-binding resolution,
unchanged MCP discovery, and equivalent tool/resource responses (including examples)
using a recording OData client without authentication.
It checks that resource and example links and protocol discovery are precomputed,
and that generation-only inference is absent from emitted runtime source.

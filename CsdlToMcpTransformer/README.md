# Generic CSDL-to-MCP Transformer

The pipeline is **CSDL XML -> validated OData EDM model -> typed MCP bindings ->
JSON at the protocol boundary**. There is no custom JSON metadata model or
Microsoft Graph entity-name dispatcher. Namespaces, containers, keys, navigation
properties, parameter types, and operation signatures come from `IEdmModel`.

## Generate a server

```powershell
dotnet run --project C:\MCP\CsdlToMcpTransformer\CsdlToMcpTransformer.csproj -- --single C:\schemas\Inventory.csdl C:\MCP\InventoryServer 3020
```

`--single` accepts a full CSDL path, or a filename relative to `C:\MCP\Csdl`.
With no arguments, the transformer generates the three existing demo schemas.
The generated project includes its input as `metadata.csdl`, copied to build and
publish outputs. Existing server projects are not consulted during generation.

Source templates under `Templates` are embedded in the transformer assembly.
Edit these templates and rebuild to change generated behavior. Regeneration
overwrites generated files and removes the obsolete generated Graph models/client
and legacy `UserCsdlProvider.cs`. The generic provider is `Csdl/CsdlProvider.cs`.

## Generic projection

| EDM element | MCP projection |
|---|---|
| Entity set `Products` | `list_Products`, `get_Products`; collection resource and keyed resource template |
| Singleton `Featured` | `get_Featured` and a fixed resource |
| Navigation property `Products/Supplier` | `get_Products_Supplier` |
| Bound function/action | One tool per compatible navigation source, including inherited types and derived-type casts |
| Function/action import | Tool using the import's service-root path |
| `Agent.Prompts` | Typed prompt definitions, arguments/defaults, and all text messages |
| `Agent.Examples` / `Agent.ExampleQueries` | Examples attached to their EDM annotation targets |

For example, resources use `odata://Inventory.Warehouse/Products` and
`odata://Inventory.Warehouse/Products/{key_Id}`. Composite keys produce one
template segment per key. Resource key values are URI-encoded, unquoted values.
Read-tool keys are named `key_<property>`, and use the property's EDM type.

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
variable. Optional `OData:Scope` enables Azure CLI authentication for that scope:

```powershell
$env:OData__BaseUrl = "https://example.com/odata/"
dotnet run --project C:\MCP\InventoryServer\GraphMcpServer.csproj
```

For Microsoft Graph, use `https://graph.microsoft.com/v1.0/` and, when needed,
scope `https://graph.microsoft.com/.default`. The backend must implement the
paths/operations declared by the CSDL; the transformer does not map fictional
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

The dependency-light console runner compiles the actual templates and tests a
non-Graph inventory schema: composite/inherited keys, navigation, operation
overloads, functions, actions, derived-type casts, enums, structured/collection
inputs, HTTP routing/bodies, validation, errors, and traces. It also verifies
all existing demo prompt renderings and example counts. HTTP execution uses a
recording handler; tests do not authenticate or call a live service.

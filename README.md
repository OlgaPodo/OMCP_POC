# Microsoft Graph HTTP MCP 2.0 Server & Demo Site POC

Proof-of-Concept updated to **MCP 2.0 (`2026-07-28` Specification)** ([specification reference](https://modelcontextprotocol.io/specification/2026-07-28)) and the OASIS OData TC proposal *"Making OData Agent-Ready: Prompts, Events and Tasks, and a Modernized Domain Language"*. The `Agent` vocabulary is experimental; prompt schemas and responses contain no examples.

---

## 1. Quick Start

### Start MCP 2.0 Server (`http://localhost:3001`)
```powershell
cd C:\MCP\Server
dotnet run
```

### Start Demo Site (`http://localhost:3000`)
```powershell
cd C:\MCP\Demo_Site
npm start  # or: node server.js
```
Open **[http://localhost:3000](http://localhost:3000)** in your browser.

---

## 2. MCP 2.0 (`2026-07-28`) Specification Upgrades

The implementation adheres to the **Model Context Protocol 2.0 (2026-07-28)** standard:

1. **Protocol Versioning:**
   - Protocol version identifier: `"2026-07-28"`.
   - Mandatory HTTP header: `MCP-Protocol-Version: 2026-07-28`.
   - Per-request metadata `_meta`:
     ```json
     "_meta": {
       "io.modelcontextprotocol/protocolVersion": "2026-07-28",
       "io.modelcontextprotocol/clientInfo": { "name": "GraphMcpExplorer", "version": "2.0.0" },
       "io.modelcontextprotocol/clientCapabilities": {}
     }
     ```
   - Per-response server identity in `_meta`:
     ```json
     "_meta": {
       "io.modelcontextprotocol/serverInfo": { "name": "GraphMcpServer", "version": "2.0.0" }
     }
     ```

2. **Streamable HTTP Transport (Stateless, Single Endpoint):**
   - Single POST endpoint at `http://localhost:3001/`.
   - Dedicated standard HTTP metadata headers:
     - `Mcp-Method`: mirrors JSON-RPC `method` (e.g. `server/discover`, `tools/call`, `prompts/get`, `resources/read`).
     - `Mcp-Name`: mirrors targeted name/URI (e.g. `find_user`, `whom_olga_reports_to`, `users://me`).
   - Accept header supports `application/json` and `text/event-stream`.

3. **Mandatory `server/discover` RPC:**
   - Supports up-front server capability discovery, supported version negotiation (`["2026-07-28"]`), and identity introspection without needing prior handshake sessions.

4. **Polymorphic Responses (`resultType`):**
   - Standardized `result.resultType = "complete"`.

---

## 3. Server Primitives (`C:\MCP\Server`)

- **Tech Stack:** .NET 10 / C# with `ModelContextProtocol.AspNetCore` 2.1.0 and `Azure.Identity`.
- **Live Graph Auth:** Azure CLI access token (`az account get-access-token --resource-type ms-graph`) with seamless automatic high-fidelity fallback dataset (Olga Podolyako & team) for offline reliability.

| Type | Name / URI | Description | Underlying Graph API |
|---|---|---|---|
| **Discovery** | `server/discover` | MCP 2.0 capability & version negotiation | Protocol Core |
| **Tool** | `find_user` | Search/filter users by name, email, or UPN prefix | `GET /v1.0/users?$filter=startswith(...)` |
| **Tool** | `user_profile` | Detailed profile with manager, peers, and direct reports | `GET /v1.0/users/{id}`, `/manager`, `/directReports` |
| **Tool** | `find_group`, `group_details` | Group search, membership and ownership | `GET /v1.0/groups`, `/members`, `/owners` |
| **Tool** | `read_resource` | CLI adapter using the same projected resource dispatcher as `resources/read` | Determined by the declared resource URI binding |
| **Resource** | `users://me` | Authenticated user profile representation | `GET /v1.0/me` |
| **Resource** | `users://{userId}` | Individual user profile representation | `GET /v1.0/users/{id}` |
| **Resource** | `groups://all`, `groups://{groupId}` | Group catalog and group membership/ownership data | `GET /v1.0/groups`, `/groups/{id}` |
| **Prompt** | `whom_olga_reports_to` | "Whom Olga reports to?" | Resolves manager via `user_profile` |
| **Prompt** | `who_are_olgas_peers` | "Who are Olga's peers?" | Resolves manager's direct reports via `user_profile` |
| **Prompt** | `direct_reports_of_manager` | "Direct reports of a manager" | Queries direct reports for any manager |
| **Prompt** | `search_users_by_dept_title` | "Search users by dept & title" | Queries users filtered by department & title |
| **Prompt** | `user_quick_lookup` | "User quick lookup" | Resource reader template (`users://{id}`) |
| **Prompt** | `find_group_by_name`, `list_group_members`, `who_owns_group`, `group_quick_lookup` | Group workflows | Projected group tools and resources |
| **Composed prompt** | `user_peers_in_group` | Intersect reporting peers with group members | Requires both user and group resources |
| **Composed prompt** | `group_members_reporting_to_manager` | Filter group members by reporting manager | Requires membership plus user reporting profiles |
| **Composed prompt** | `user_and_group_owners_common_manager` | Compare user and owner managers | Requires ownership plus separate user reporting profiles |

---

## 4. Demo Web Application (`C:\MCP\Demo_Site`)

Modeled directly after **Microsoft Graph Explorer (`aka.ms/ge`)**:

1. **Interactive MCP 2.0 Explorer**:
   - Inspects exact **HTTP Request Headers** (`MCP-Protocol-Version: 2026-07-28`, `Mcp-Method`, `Mcp-Name`), **Request Payload (JSON-RPC)**, **Response Headers**, and **Response Payload** (`resultType: complete`).
   - Interactive Org Profile Card rendering (Avatar, Details, Manager Card, Peers Grid, Direct Reports).
   - Live Microsoft Graph REST trace inspection (`https://graph.microsoft.com/v1.0/...`).
2. **CSDL and annotations**:
   - Full CSDL JSON ($Version 4.01) and CSDL XML (EDMX 4.0) viewers.
   - Canonical XML declares `Agent.Prompts` on `GraphService` and `Agent.ExampleQueries` only on entity sets (`users`, `groups`).
   - JSON, tool schemas, resource URIs/dispatch and MCP prompts are deterministic projections of the same XML, with explicit example-to-prompt references.
   - POC `Agent.Tools` and `Agent.Resources` bindings select supported read-only Graph operations. Container prompt `Tools`/`Resources` collections reference these generated primitives, including cross-resource workflows.
3. **Addressing Concern 1 (Prompts as Search Index vs. Reusable Templates)**:
   - Architectural analysis separates core MCP from POC/client behavior:
     - *MCP role:* Formal parameterized templates (`Messages`, `Arguments`) for execution.
     - *Retrieval role:* Entity-set example queries remain independent CSDL metadata, never fields on prompt schemas or responses.
   - The sandbox is explicitly illustrative word-overlap search, not embeddings or LLM execution.
4. **Addressing Concern 2 (The End-to-End Mapping Pipeline)**:
   - Six expanded, unclipped tiles show canonical CSDL bindings, generated tools/resources, prompt invocation, rendered messages, resource reads and joins. The mapping contains no examples.
   - A separate **Runtime Sequence with Discovery** subtab captures read-only `server/discover`, tool/resource/template/prompt listings from the selected MCP. Later prompt, tool, resource and synthesis steps are explicitly illustrative; refresh does not execute Graph operations.
   - The default joins a user's reporting peers with group members; selecting another Explorer prompt changes the mapping.
   - CSDL selects operations from an explicit C# Graph operation registry; no unrestricted REST compiler is claimed.
5. **Interactive A/B Experiment View**:
   - Inspect live status of Server A (:3001) vs Server B (:3002).
   - Filter benchmark entries and inspect actual recorded outcomes; absent results are labeled Not run.
6. **POC Summary**:
   - Inventories and counts are loaded from the selected CSDL projection, including all container prompts.
   - Tiles distinguish deterministic annotated-schema conversion from the supported C# operation registry, probabilistic client orchestration, changing Graph data and mock fallback.
   - Protected PDF requirements and production / full standards conformance are not claimed as verified.

---

## 5. A/B Benchmark Experiment (`C:\MCP\Experiment`)

Compare actual Copilot CLI routing with and without entity-set example queries:

* **Server A (Variant):** `http://localhost:3001` (Independent entity-set example annotations)
* **Server B (Control):** `http://localhost:3002` in `Server_Baseline/` (No entity-set example annotations)
* Both expose identical, standard MCP prompt messages without examples. The harness supplies entity-set query guidance separately.
* **Dataset:** 61 user/group, cross-resource and unsupported-request queries (`benchmark_queries.json`).

### Run with any model:
```powershell
# Use your authenticated Copilot CLI; baseline completes first
python C:\MCP\Experiment\run_experiment.py
python C:\MCP\Experiment\run_experiment.py --limit 3
```

Every query gets a unique CLI home/work directory and only the assigned MCP.
Option 1 is baseline Server B; Option 2 is examples Server A.
New reports verify actual MCP calls, parameters and errors, and distinguish mock
fallback from live provenance. Generated OData strings are not executed or syntax
validated. Historical heuristic reports are retained and labeled unverified.
See `Experiment\README.md` for methodology and commands.

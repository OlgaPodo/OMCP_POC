# Microsoft Graph HTTP MCP 2.0 Server & Demo Site POC

Proof-of-Concept updated to **MCP 2.0 (`2026-07-28` Specification)** ([specification reference](https://modelcontextprotocol.io/specification/2026-07-28)) and the OASIS OData TC proposal *"Making OData Agent-Ready: Prompts, Events and Tasks, and a Modernized Domain Language"* (Option 1).

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
| **Resource** | `users://me` | Authenticated user profile representation | `GET /v1.0/me` |
| **Resource** | `users://{userId}` | Individual user profile representation | `GET /v1.0/users/{id}` |
| **Prompt** | `whom_olga_reports_to` | "Whom Olga reports to?" | Resolves manager via `user_profile` |
| **Prompt** | `who_are_olgas_peers` | "Who are Olga's peers?" | Resolves manager's direct reports via `user_profile` |
| **Prompt** | `direct_reports_of_manager` | "Direct reports of a manager" | Queries direct reports for any manager |
| **Prompt** | `search_users_by_dept_title` | "Search users by dept & title" | Queries users filtered by department & title |
| **Prompt** | `user_quick_lookup` | "User quick lookup" | Resource reader template (`users://{id}`) |

---

## 4. Demo Web Application (`C:\MCP\Demo_Site`)

Modeled directly after **Microsoft Graph Explorer (`aka.ms/ge`)**:

1. **Interactive MCP 2.0 Explorer**:
   - Inspects exact **HTTP Request Headers** (`MCP-Protocol-Version: 2026-07-28`, `Mcp-Method`, `Mcp-Name`), **Request Payload (JSON-RPC)**, **Response Headers**, and **Response Payload** (`resultType: complete`).
   - Interactive Org Profile Card rendering (Avatar, Details, Manager Card, Peers Grid, Direct Reports).
   - Live Microsoft Graph REST trace inspection (`https://graph.microsoft.com/v1.0/...`).
2. **Limited User CSDL with `@Agent.Prompts` (Option 1)**:
   - Full CSDL JSON ($Version 4.01) and CSDL XML (EDMX 4.0) viewers.
   - Shows `@Agent.Prompts` embedded with `Examples` (utterance & OData request pairs) attached to the `GraphService` container.
3. **Addressing Concern 1 (Prompts as Search Index vs. Reusable Templates)**:
   - Architectural analysis of how Option 1 bridges the duality:
     - *MCP role:* Formal parameterized templates (`Messages`, `Arguments`) for execution.
     - *Search Index role:* Utterance-query pairs in `Examples` used as semantic embeddings for zero-shot prompt discovery.
   - Live interactive semantic matcher sandbox.
4. **Addressing Concern 2 (The End-to-End Mapping Pipeline)**:
   - Visual 4-stage interactive pipeline:
     `[OData CSDL Annotation]` ➔ `[MCP 2.0 Protocol (prompts/get)]` ➔ `[Agent Tool Call]` ➔ `[Microsoft Graph REST Request]`
   - Step-through walk-through for all 4 script queries.

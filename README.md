# Microsoft Graph HTTP MCP 2.0 Server & Demo Site POC

Proof-of-Concept aligned with the **MCP 2.0 (`2026-07-28` Specification)** ([specification reference](https://modelcontextprotocol.io/specification/2026-07-28)), **OASIS OData v4.01 OS** ([specification reference](https://www.oasis-open.org/standard/odata-v4-01-os/)), and the OASIS OData TC proposal *"Making OData Agent-Ready: Prompts, Events and Tasks, and a Modernized Domain Language"*.

---

## 1. Prerequisites

### A. Microsoft Graph API Access & Authentication (Any Tenant)
To execute live queries against `https://graph.microsoft.com/v1.0`:
1. **Microsoft Entra ID / M365 Tenant Access**: Access to Microsoft Graph APIs on any tenant (for example, a [Microsoft 365 Developer Program](https://developer.microsoft.com/microsoft-365/dev-program) sandbox tenant or test directory) containing users, reporting hierarchies (`manager`, `directReports`), and groups (`members`, `owners`).
2. **Graph Permissions**: Directory read permissions on the signed-in account:
   - `User.Read.All` (or `Directory.Read.All`) for `/v1.0/me`, `/v1.0/users`, `/v1.0/users('{id}')/manager`, and `/v1.0/users('{id}')/directReports`
   - `Group.Read.All` (or `Directory.Read.All`) for `/v1.0/groups`, `/v1.0/groups('{id}')/members`, and `/v1.0/groups('{id}')/owners`
3. **Ability to Sign In (Azure CLI or Bearer Token)**:
   - Sign in interactively with **Azure CLI**:
     ```powershell
     az login
     az account get-access-token --resource-type ms-graph
     ```
   - Or supply a bearer token directly via environment variable before starting the servers:
     ```powershell
     $env:GRAPH_ACCESS_TOKEN = "<your-ms-graph-bearer-token>"
     ```
4. **Offline / Unauthenticated Fallback**: If Azure CLI is not signed in and `GRAPH_ACCESS_TOKEN` is not set, the MCP servers automatically fall back to a deterministic `@contoso.com` sample directory dataset so the Demo Site and Explorer remain runnable offline.

### B. Required SDKs & CLI Tools
- **.NET 10.0 SDK** (`dotnet --version`) — required to build and run the C# ASP.NET Core MCP 2.0 servers (`Server` and `Server_Baseline`).
- **Node.js 18+ & npm** (`node --version`) — required to run the presentation site (`Demo_Site`).
- **Azure CLI** (`az --version`) — required for interactive Microsoft Graph token acquisition.
- **Python 3.10+** & **GitHub Copilot CLI** (`copilot` signed in to GitHub) — required only if running the live A/B routing experiment (`Experiment/run_experiment.py`).

---

## 2. Quick Start

### Start Server A — CSDL with `@Agent.Prompts` (`http://localhost:3001`)
```powershell
cd C:\MCP\Server
dotnet run
```

### Start Server B — Baseline CSDL without Prompts (`http://localhost:3002`)
```powershell
cd C:\MCP\Server_Baseline
dotnet run
```

### Start Demo Site (`http://localhost:3000`)
```powershell
cd C:\MCP\Demo_Site
npm install
npm start  # or: node server.js
```
Open **[http://localhost:3000](http://localhost:3000)** in your browser.

---

## 3. MCP 2.0 (`2026-07-28`) & OData v4.01 Architecture

1. **MCP 2.0 Protocol (`2026-07-28`)**:
   - Mandatory HTTP header: `MCP-Protocol-Version: 2026-07-28`.
   - Streamable HTTP single endpoint (`POST /`) with `Mcp-Method` and `Mcp-Name` metadata headers.
   - Stateless `server/discover` capability and version negotiation (`["2026-07-28"]`) without deprecated stateful `initialize` handshakes.
   - Standardized `_meta` envelopes and `result.resultType = "complete"`.
2. **Standard OData v4.01 Metadata (`/$metadata`) & Pure CSDL**:
   - Exposes `GET /$metadata` with `OData-Version: 4.01` (`application/xml` and `application/json`).
   - Standard OData `EntitySet` (`users`, `groups`) and `Singleton` (`me`) declarations deterministically project into MCP tools and resources — no custom `Agent.Tools` or `Agent.Resources` annotations are added to CSDL.
   - **Server A (`Csdl/Server_WithPrompts.csdl`)** adds `<Annotation Term="Agent.Prompts">` on `GraphService`, expressing single-resource and cross-resource workflows via canonical OData v4.01 queries.
   - **Server B (`Csdl/Server_Baseline.csdl`)** contains pure OData CSDL without `@Agent.Prompts`.

---

## 4. Server Primitives (`C:\MCP\Server`)

- **Tech Stack:** .NET 10 / C# with `ModelContextProtocol.AspNetCore` and `Azure.Identity`.
- **Live Graph Auth:** `GRAPH_ACCESS_TOKEN` or Azure CLI (`az account get-access-token --resource-type ms-graph`) with automatic `@contoso.com` fallback dataset when offline.

| Type | Name / URI | Description | Underlying Graph API (OData v4.01) |
|---|---|---|---|
| **Discovery** | `server/discover` | MCP 2.0 capability & version negotiation | Protocol Core |
| **Tool** | `find_user` | Search/filter users by name, email, or UPN prefix | `GET /v1.0/users?$filter=startswith(...)` |
| **Tool** | `user_profile` | Detailed profile with manager, peers, and direct reports | `GET /v1.0/users('{id}')`, `/manager`, `/directReports` |
| **Tool** | `find_group`, `group_details` | Group search, membership and ownership | `GET /v1.0/groups`, `/groups('{id}')/members`, `/owners` |
| **Tool** | `read_resource` | CLI adapter using the same projected resource dispatcher as `resources/read` | Determined by the declared resource URI binding |
| **Resource** | `users://me` | Authenticated user profile representation | `GET /v1.0/me` |
| **Resource** | `users://{userId}` | Individual user profile representation | `GET /v1.0/users('{id}')` |
| **Resource** | `groups://all`, `groups://{groupId}` | Group catalog and group membership/ownership data | `GET /v1.0/groups`, `/groups('{id}')` |
| **Prompts (Server A)** | 12 `@Agent.Prompts` templates | User, group, and cross-resource (`users` + `groups`) workflows | Expressed with canonical OData v4.01 queries |

---

## 5. Demo Web Application (`C:\MCP\Demo_Site`)

Modeled after **Microsoft Graph Explorer (`aka.ms/ge`)**:

1. **Interactive MCP 2.0 Explorer**:
   - Inspects HTTP Request Headers (`MCP-Protocol-Version: 2026-07-28`, `Mcp-Method`, `Mcp-Name`), JSON-RPC payloads, Response Headers, and Response Payloads (`resultType: complete`).
   - Interactive Org Profile Card rendering and Microsoft Graph REST trace inspection (`https://graph.microsoft.com/v1.0/...`).
2. **CSDL & Annotations (`GET /$metadata`)**:
   - Side-by-side CSDL JSON (`$Version: "4.01"`) and CSDL XML (`Version="4.0"`) viewers with toggle between Server A (with `@Agent.Prompts`) and Server B (without prompts).
3. **Architectural Comparison**:
   - Contrasts prompts as a retrieval index vs. parameterized MCP workflow templates.
4. **End-to-End Mapping & Runtime Sequence with Discovery**:
   - Collapsible request/response sequence tiles with color-coded legend (**Blue = MCP Standard**, **Purple = Suggested `@Agent.Prompts` Annotations Standard**).
5. **A/B Experiment (CSDL with Prompts vs. CSDL without Prompts)**:
   - Compares Server A (`:3001`) against Baseline Server B (`:3002`), highlighting category success deltas and the 5 primary failure modes observed when `@Agent.Prompts` are absent.
6. **POC Summary**:
   - Highlights the phased CSDL roadmap: **Phase 1 (`@Agent.Prompts`)** delivered in this POC, followed by **Phase 2 (Additional Example Annotations)** to further simplify CSDL consumption by AI agents.


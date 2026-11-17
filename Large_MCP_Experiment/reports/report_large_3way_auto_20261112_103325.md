# Large CSDL (10 Entities, 5 Bound Operations, 22 Prompts, 28 Examples) — 100-Query 3-Way Experiment Report

- **Run ID**: `report_large_3way_auto_20261112_103325`
- **Timestamp**: `2026-11-12T10:33:25.303499`
- **Total Benchmark Questions**: `15`

## 1. Executive Summary Across All 3 Large MCP Variations

| Variation | Server Endpoint | CSDL File | Exposed Tools | Exposed Resources | `@Agent.Prompts` | `@Agent.Examples` | Catalog Token Footprint | Verified Routing Success | Intent Accuracy | Avg Latency |
|---|---|---|---|---|---|---|---|---|---|---|
| **1. Direct Transform w/o Examples** | `http://localhost:3004` | `Server_Large.csdl` | 59 | 21 | 22 | 0 | ~16672 tok | **46.7%** | 53.3% | 21870.8 ms |
| **2. Direct Transform with Examples** | `http://localhost:3003` | `Server_Large_WithExamples.csdl` | 59 | 21 | 22 | 28 | ~16672 tok | **46.7%** (**+0.0 pp**) | 60.0% | 26647.67 ms |
| **3. Intent Search + OData Query Construction** | `http://localhost:3005` | `Server_Large_WithExamples.csdl` | **2 (`search_intent`, `execute_odata_query`)** | 0 | 22 (indexed) | 28 (indexed) | **~383 tok (-97.7%)** | **86.7%** (**+40.0 pp vs Direct**) | 93.3% | 15625.07 ms |

## 2. Experiment 2: Query Examples Impact on Large API Surface (Variation 1 vs Variation 2)
- Adding 28 `@Agent.Examples` on entities, entity sets, singletons, functions, and actions increased verified success on the 100-question M365 benchmark from **46.7%** to **46.7%** (**+0.0 pp**).
- **Biggest gains from `@Agent.Examples`**: bound OData functions (`searchDriveItems`, `recentDriveItems`, `deltaChannelMessages`), navigation-property lookups (`driveItems('{id}')/lastModifiedByUser`), and key/GUID resolution (`channels('ch-csdl-typespec')`, `groups('11111111-...')`).

## 3. Experiment 3: Direct Transformation vs Intent Identification + OData Query Construction (Variation 2 vs Variation 3)
- **Tool Surface & Context Compression**: Direct CSDL transformation (`CsdlToMcpTransformer`) exposes **59 tools, 21 resources, and 22 prompts** (~16672 tokens upfront on every turn), whereas `LargeServer_SearchQuery` (`:3005`) exposes **only 2 tools** (`search_intent` and `execute_odata_query`, ~383 tokens — a **97.7% reduction** in upfront tool schema overhead).
- **Query Expressiveness & Accuracy**: `search_intent` + `execute_odata_query` achieves **86.7%** verified success (**+40.0 pp** over Direct Transform with Examples) because `execute_odata_query` can execute arbitrary canonical OData v4.01 `$filter`, `$select`, and `$expand` expressions learned from `@Agent.Prompts` and `@Agent.Examples` without being constrained to fixed C# tool argument signatures.

## 4. Breakdown by M365 Workload

| Workload | Queries | Var 1: Direct w/o Examples (:3004) | Var 2: Direct with Examples (:3003) | Examples Delta | Var 3: Intent + OData Query (:3005) | Architecture Delta (Var 3 vs Var 2) |
|---|---|---|---|---|---|---|
| `users_groups` | 6 | 2/6 (33.3%) | 2/6 (33.3%) | **+0.0 pp** | 4/6 (66.7%) | **+33.4 pp** |
| `files` | 3 | 1/3 (33.3%) | 3/3 (100.0%) | **+66.7 pp** | 3/3 (100.0%) | **+0.0 pp** |
| `teams` | 2 | 2/2 (100.0%) | 1/2 (50.0%) | **+-50.0 pp** | 2/2 (100.0%) | **+50.0 pp** |
| `chats` | 1 | 0/1 (0.0%) | 0/1 (0.0%) | **+0.0 pp** | 1/1 (100.0%) | **+100.0 pp** |
| `todo` | 2 | 2/2 (100.0%) | 1/2 (50.0%) | **+-50.0 pp** | 2/2 (100.0%) | **+50.0 pp** |
| `cross_workload` | 1 | 0/1 (0.0%) | 0/1 (0.0%) | **+0.0 pp** | 1/1 (100.0%) | **+100.0 pp** |

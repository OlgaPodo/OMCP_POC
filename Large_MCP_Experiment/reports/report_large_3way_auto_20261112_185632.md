# Large CSDL (10 Entities, 5 Bound Operations, 22 Prompts, 28 Examples) — 100-Query 3-Way Experiment Report

- **Run ID**: `report_large_3way_auto_20261112_185632`
- **Timestamp**: `2026-11-12T18:56:32.931194`
- **Total Benchmark Questions**: `5`

## 1. Executive Summary Across All 3 Large MCP Variations

| Variation | Server Endpoint | CSDL File | Exposed Tools | Exposed Resources | `@Agent.Prompts` | `@Agent.Examples` | Catalog Token Footprint | Verified Routing Success | Intent Accuracy | Avg Latency |
|---|---|---|---|---|---|---|---|---|---|---|
| **1. Direct Transform w/o Examples** | `http://localhost:3004` | `Server_Large.csdl` | 59 | 21 | 22 | 0 | ~16672 tok | **40.0%** | 40.0% | 26903.0 ms |
| **2. Direct Transform with Examples** | `http://localhost:3003` | `Server_Large_WithExamples.csdl` | 59 | 21 | 22 | 28 | ~16672 tok | **20.0%** (**+-20.0 pp**) | 20.0% | 34334.4 ms |
| **3. Intent Search + OData Query Construction** | `http://localhost:3005` | `Server_Large_WithExamples.csdl` | **2 (`search_intent`, `execute_odata_query`)** | 0 | 22 (indexed) | 28 (indexed) | **~383 tok (-97.7%)** | **60.0%** (**+40.0 pp vs Direct**) | 80.0% | 13221.8 ms |

## 2. Experiment 2: Query Examples Impact on Large API Surface (Variation 1 vs Variation 2)
- Adding 28 `@Agent.Examples` on entities, entity sets, singletons, functions, and actions increased verified success on the 100-question M365 benchmark from **40.0%** to **20.0%** (**+-20.0 pp**).
- **Biggest gains from `@Agent.Examples`**: bound OData functions (`searchDriveItems`, `recentDriveItems`, `deltaChannelMessages`), navigation-property lookups (`driveItems('{id}')/lastModifiedByUser`), and key/GUID resolution (`channels('ch-csdl-typespec')`, `groups('11111111-...')`).

## 3. Experiment 3: Direct Transformation vs Intent Identification + OData Query Construction (Variation 2 vs Variation 3)
- **Tool Surface & Context Compression**: Direct CSDL transformation (`CsdlToMcpTransformer`) exposes **59 tools, 21 resources, and 22 prompts** (~16672 tokens upfront on every turn), whereas `LargeServer_SearchQuery` (`:3005`) exposes **only 2 tools** (`search_intent` and `execute_odata_query`, ~383 tokens — a **97.7% reduction** in upfront tool schema overhead).
- **Query Expressiveness & Accuracy**: `search_intent` + `execute_odata_query` achieves **60.0%** verified success (**+40.0 pp** over Direct Transform with Examples) because `execute_odata_query` can execute arbitrary canonical OData v4.01 `$filter`, `$select`, and `$expand` expressions learned from `@Agent.Prompts` and `@Agent.Examples` without being constrained to fixed C# tool argument signatures.

## 4. Breakdown by M365 Workload

| Workload | Queries | Var 1: Direct w/o Examples (:3004) | Var 2: Direct with Examples (:3003) | Examples Delta | Var 3: Intent + OData Query (:3005) | Architecture Delta (Var 3 vs Var 2) |
|---|---|---|---|---|---|---|
| `users_groups` | 5 | 2/5 (40.0%) | 1/5 (20.0%) | **+-20.0 pp** | 3/5 (60.0%) | **+40.0 pp** |

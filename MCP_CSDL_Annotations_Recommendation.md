# OASIS OData TC Recommendation: CSDL Annotations & MCP 2.0 Projection Architecture for Agent-Ready OData APIs

**Date:** November 10, 2026  
**Author:** Miriam Graham  
**Reference Proposal:** *Making OData Agent-Ready: Prompts, Events and Tasks, and a Modernized Domain Language*  
**Reference Implementation & Benchmark Suite:** `C:\MCP` (`http://localhost:3000` Demo Site; MCP 2.0 Servers `:3001`–`:3005`)  
**Standards Baseline:** [Model Context Protocol 2.0 (`2026-07-28`)](https://modelcontextprotocol.io/specification/2026-07-28) &bull; [OASIS OData v4.01 OS (`$metadata`)](https://www.oasis-open.org/standard/odata-v4-01-os/)

---

## 1. Executive Summary & Core Recommendations

To make OData services natively consumable by AI agents via the **Model Context Protocol (MCP 2.0)** with **deterministic code generation, high routing accuracy, and minimal token cost**, we recommend a two-part **OASIS OData `Agent` Vocabulary** paired with two size-appropriate MCP server projection patterns:

1. **Keep Domain CSDL Pure — Do Not Introduce `Agent.Tools` or `Agent.Resources` Annotations:**
   Native OData `EntitySet`, `Singleton`, `EntityType`, `NavigationProperty`, `Function`, and `Action` declarations in `GET <serviceRoot>/$metadata` already carry complete structural and type metadata to **deterministically project** MCP tools (`tools/list`, `tools/call`) and MCP resources (`resources/list`, `resources/templates/list`, `resources/read`) without any AI involvement at build time.
2. **Standardize Two Orthogonal CSDL Annotations (`@Agent.Prompts` and `@Agent.Examples`):**
   - **Phase 1 — `@Agent.Prompts` (`AppliesTo="EntityContainer"`):** Parameterized, multi-step workflow templates authored using canonical OData v4.01 queries (`GET`/`POST`). Container-level scope is essential because real user intents frequently span multiple entity sets (e.g., intersecting `users` reporting hierarchies with `groups` memberships, or correlating `teams` channels, `driveItems`, and `todoTasks`).
   - **Phase 2 — `@Agent.Examples` (`AppliesTo="EntityType EntitySet Singleton Function Action"`):** Independent utterance-to-OData-request pairs attached directly to the schema element they illustrate, **without prompt cross-references** and **never injected into MCP `GetPromptResult` responses**.
3. **Adopt Two Complementary MCP Projection Architectures Based on Schema Scale:**
   - **Direct CSDL-to-MCP Transformation (`CsdlToMcpTransformer`):** Best for small-to-medium OData services ($\le 15$ entity sets), projecting 1:1 tools, resources, and prompts directly from CSDL.
   - **Two-Step Intent Identification + OData Query Construction (`search_intent` + `execute_odata_query`):** Best for large enterprise schemas (e.g., Microsoft Graph), reducing upfront MCP tool-catalog token overhead by **90.1%** while achieving **96.0%** verified query execution accuracy.

---

## 2. Resolution of OData TC Review Questions (October 28, 2026)

| OData TC Question / Concern | Architectural Resolution & Empirical Finding |
|---|---|
| **1. Are MCP Prompts a search index or reusable templates?**  | In strict MCP 2.0 (`2026-07-28`), `prompts/list` returns capability signatures (`name`, `title`, `description`, `arguments`) and `prompts/get` returns parameterized `messages`. Prompts act as **grounded workflow templates** that teach the LLM how to decompose free-text user utterances into valid OData v4.01 sequences and default parameters. They must **not** contain non-standard `examples` fields in `prompts/get` responses. |
| **2. Is the CSDL $\rightarrow$ MCP conversion deterministic or AI-generated?**  | **100% deterministic.** Authoring CSDL annotations may be human-curated or AI-assisted at design time, but once published in `$metadata`, `CsdlToMcpTransformer` deterministically compiles `IEdmModel` into MCP tools, resources, and prompts with **zero runtime or build-time LLM rewriting** (verified via 1:1 behavioral parity across `Server_transform`, `Server_Baseline_transform`, and `LargeServer_transform`). |
| **3. Should `@Agent.Examples` be inside `@Agent.Prompts` on the container (Option 1) or attached to individual schema targets (Option 2)?**  | **Option 2 (Targeted on Entities & Operations, separate from Prompts).** Attaching `@Agent.Examples` directly to `EntityType`, `EntitySet`, `Singleton`, `Function`, and `Action` makes examples modular, schema-scoped, and reusable across prompts. Removing `Prompt` references from `@Agent.Examples` cleanly decouples entity/operation syntax grounding from container workflow templates. |
| **4. Why put annotations in CSDL (`$metadata`) instead of an external agent skill (`SKILL.md` / `AGENTS.md`)?**  | External skills are proprietary to a specific client harness. Embedding `@Agent.Prompts` and `@Agent.Examples` in standard OData `$metadata` makes **any OData service self-describing to any MCP server generator or enterprise MCP gateway** out of the box. |
| **5. Why do LLM-generated OData queries fail without CSDL annotations?**  | LLMs know generic OData syntax but do **not** know service-specific constraints (e.g., Microsoft Graph rejecting `$filter=contains(...)` on `users` in favor of `startswith(...)`, bound function parameter conventions like `Microsoft.Graph.searchDriveItems(q='...')`, or multi-entity join steps). |

---

## 3. Empirical Benchmark Results Across 5 MCP Servers (`:3001`–`:3005`)

We evaluated five live .NET 10 MCP 2.0 servers across two benchmark suites: **Experiment 1** (61 queries on a 2-entity `Users` & `Groups` CSDL) and **Experiments 2 & 3** (100 queries on a 10-entity, 5-bound-operation M365 CSDL spanning `Users`, `Groups`, `Drives`, `DriveItems`, `Teams`, `Channels`, `Chats`, `ChatMessages`, `TodoLists`, and `TodoTasks`).

| Experiment & MCP Server Variation | Port & Source CSDL | Exposed MCP Surface | Upfront Catalog Footprint | Intent Accuracy | Verified Execution Success |
|---|---|---|---|---|---|
| **Exp 1 &bull; Option 1: Baseline (w/o Prompts)** | `:3002` (`Server_Baseline.csdl`) | 5 Tools, 4 Resources, **0 Prompts**, 0 Examples | ~820 tok | 13.1% | **55.7%** (`34/61`) |
| **Exp 1 &bull; Option 2: Variant (with `@Agent.Prompts`)** | `:3001` (`Server_WithPrompts.csdl`) | 5 Tools, 4 Resources, **12 `@Agent.Prompts`**, 0 Examples | ~1,490 tok | 95.1% | **91.8%** (`56/61`, **`+36.1 pp`**) |
| **Exp 2 &bull; Var 1: Large Direct Transform w/o Examples** | `:3004` (`Server_Large.csdl`) | 59 Tools, 21 Resources, 22 Prompts, **0 Examples** | ~17,509 tok | 55.0% | **55.0%** (`55/100`) |
| **Exp 2 &amp; 3 &bull; Var 2: Large Direct Transform with Examples** | `:3003` (`Server_Large_WithExamples.csdl`) | 59 Tools, 21 Resources, 22 Prompts, **28 `@Agent.Examples`** | ~17,509 tok | 100.0% | **91.0%** (`91/100`, **`+36.0 pp`**) |
| **Exp 3 &bull; Var 3: Intent Search + OData Query Construction** | `:3005` (`Server_Large_WithExamples.csdl`) | **2 Tools only** (`search_intent`, `execute_odata_query`), 22 Prompts &amp; 28 Examples indexed | **~383 tok (`-97.8%`)** | 100.0% | **96.0%** (`96/100`, **`+5.0 pp` vs Var 2 / `+41.0 pp` vs Var 1**) |

### Key Empirical Takeaways by Experiment

1. **Experiment 1 — Prompt Impact (`+36.1 pp` on 61 Queries):**
   Without `@Agent.Prompts`, Baseline Server B (`:3002`) failed **44.3% (27/61)** of queries due to five recurring failure modes: (a) first-name disambiguation failures returning 20 tenant matches, (b) missing default parameters on canonical commands, (c) false `out_of_scope` rejections on `$filter=department eq '...'` queries, (d) `groups` vs `users` entity-set misrouting, and (e) `0/3` completion on cross-resource (`users` + `groups`) joins. Adding `@Agent.Prompts` on `EntityContainer` resolved `3/3` cross-resource joins and raised success to **91.8%**.
2. **Experiment 2 — Query Examples Impact on Large API Surface (`+36.0 pp` on 100 Queries):**
   When scaling to 10 entities and 5 bound operations (`59` generated tools via `CsdlToMcpTransformer`), prompts alone (`:3004`) achieved only **55.0%** because non-default queries lacked concrete syntax examples for bound functions (`searchDriveItems`, `recentDriveItems`, `deltaChannelMessages`), navigation paths (`driveItems('{id}')/lastModifiedByUser`), and workload-specific `$filter` predicates. Adding 28 `@Agent.Examples` (`:3003`) lifted Files accuracy from **36.4% to 90.9% (`+54.5 pp`)**, Chats from **33.3% to 83.3% (`+50.0 pp`)**, and overall accuracy to **91.0%**.
3. **Experiment 3 — Direct Transform vs. Intent Search + OData Query Construction (`-97.8%` Catalog Tokens & `+5.0 pp` Accuracy):**
   Direct CSDL transformation via `CsdlToMcpTransformer` scales tool count linearly ($O(N)$) with schema size, generating **59 tools and 21 resources** (~17,509 upfront catalog tokens) for 10 entities (and thousands of tools for full Microsoft Graph) while restricting queries to fixed C# tool parameters. By exposing **only 2 MCP tools** (`search_intent` to retrieve matching CSDL `@Agent.Prompts`, `@Agent.Examples`, and `IEdmModel` properties, followed by `execute_odata_query`), `LargeServer_SearchQuery` (`:3005`) cut upfront catalog tokens by **97.8%** (~383 tokens) and solved multi-attribute `$filter`/`$expand` queries that static tool signatures could not express (**96.0%** overall).

---

## 4. Recommended Normative `Agent` Vocabulary Schema for OASIS OData

```xml
<Schema Namespace="Agent" xmlns="http://docs.oasis-open.org/odata/ns/edm">
  <!-- 1. EntityContainer Workflow Prompts (Maps 1:1 to MCP prompts/list & prompts/get) -->
  <Term Name="Prompts" Type="Collection(Agent.PromptType)" AppliesTo="EntityContainer" />
  <ComplexType Name="PromptType">
    <Property Name="Name" Type="Edm.String" Nullable="false" />
    <Property Name="Title" Type="Edm.String" Nullable="false" />
    <Property Name="Description" Type="Edm.String" Nullable="false" />
    <Property Name="Arguments" Type="Collection(Agent.PromptArgument)" Nullable="false" />
    <Property Name="Messages" Type="Collection(Agent.PromptMessage)" Nullable="false" />
  </ComplexType>
  <ComplexType Name="PromptArgument">
    <Property Name="Name" Type="Edm.String" Nullable="false" />
    <Property Name="Description" Type="Edm.String" Nullable="false" />
    <Property Name="Required" Type="Edm.Boolean" Nullable="false" />
    <Property Name="Default" Type="Edm.String" Nullable="false" />
  </ComplexType>
  <ComplexType Name="PromptMessage">
    <Property Name="Role" Type="Edm.String" Nullable="false" />
    <Property Name="Content" Type="Agent.TextContent" Nullable="false" />
  </ComplexType>
  <ComplexType Name="TextContent">
    <Property Name="Type" Type="Edm.String" Nullable="false" />
    <Property Name="Text" Type="Edm.String" Nullable="false" />
  </ComplexType>

  <!-- 2. Entity & Operation Query Examples (Independent of Prompts; powers examples://catalog & search_intent) -->
  <Term Name="Examples" Type="Collection(Agent.ExampleQuery)"
        AppliesTo="EntityType EntitySet Singleton Function Action" />
  <ComplexType Name="ExampleQuery">
    <Property Name="Utterance" Type="Edm.String" Nullable="false" />
    <Property Name="Request" Type="Edm.String" Nullable="false" />
    <Property Name="Description" Type="Edm.String" Nullable="true" />
  </ComplexType>
</Schema>
```

### Summary of Best-Practice Guidelines for OData Service Authors

1. **Author `@Agent.Prompts` on `EntityContainer` using canonical OData v4.01 queries** (`GET users('{userId}')/manager`, `GET groups('{groupId}')/members`) rather than coupling CSDL to implementation-specific MCP tool names.
2. **Attach `@Agent.Examples` directly to `EntityType`, `EntitySet`, `Singleton`, `Function`, and `Action` targets** without `Prompt` cross-references, focusing on service-specific filter constraints (e.g., `startswith` vs `contains`), navigation paths, and bound operation syntax.
3. **Select the MCP Server Projection Mode by Schema Scale:**
   - **Small/Focused OData Schemas ($\le 15$ EntitySets):** Use **Direct CSDL-to-MCP Transformation** with `@Agent.Prompts` and `examples://catalog`.
   - **Large Enterprise OData Schemas ($> 15$ EntitySets):** Use **Two-Step Intent Identification + OData Query Construction** (`search_intent` + `execute_odata_query`) backed by `@Agent.Prompts` and `@Agent.Examples` to maintain an $O(1)$ 2-tool context footprint and full OData v4.01 query expressiveness.

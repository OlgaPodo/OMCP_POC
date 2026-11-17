# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts

Report ID: `report_copilot_cli-default_20261104_105928_336470`

Model: `gpt-6.1-sol` | Queries: 1

## Architectural Comparison

- **Option 1: Baseline Server B (CSDL without Prompts)**: Exposes tools and resources projected deterministically from standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`), with **0 `@Agent.Prompts`** in CSDL.
- **Option 2: Variant Server A (CSDL with `@Agent.Prompts`)**: Exposes the exact same tools and resources, plus container-level `@Agent.Prompts` (authored with standard OData v4.01 query steps, with no examples in CSDL).
- **POC Reference: Server 3 (Search-then-Query)**: Third MCP server running on port 3003, implementing the two-tool (`search` + `query`) pattern via Copilot API semantic retrieval.

## OASIS OData TC Proposal Scope & CSDL Model Principles

**Proposal Focus:** `Agent.Prompts` annotations only.

- **No inlined `<Schema Namespace="Agent">`**: Vocabulary terms should be defined in external vocabulary documents, never inlined in the domain service CSDL.
- **No annotations for tools (`Agent.Tools`)**: OData already defines first-class operations (Actions and Functions) and EntitySets. Agent tools (e.g. MCP tools) are client/agent projections over those native operations, not CSDL schema metadata.
- **Prompts-First**: Agent capabilities, instructions, and workflows are modeled exclusively through `Agent.Prompts` annotations on the `EntityContainer`.

## Methodology

Option 1 (Baseline Server B) uses CSDL without @Agent.Prompts annotations (exposing tools and resources projected from standard OData EntitySets and Singletons). Option 2 (Variant Server A) uses CSDL with container @Agent.Prompts (exposing the exact same tools and resources, plus 12 container prompts). POC Reference Server 3 (port 3003) retains the experimental Search-then-Query 2-tool architecture (search and query). Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored.

| Metric | Option 1: Baseline (No Prompts) | Option 2: Variant (With @Agent.Prompts) |
|---|---|---|
| Verified MCP routing success | 0.0% | 100.0% |
| Intent accuracy | 0.0% | 100.0% |

Difference: **+100.0 percentage points**.

## Categories

| Category | Queries | Baseline (No Prompts) | Variant (With Prompts) |
|---|---|---|---|
| canonical | 1 | 0.0% | 100.0% |

## Observed CLI usage

Baseline (No Prompts): 34522 input tokens (including cached input), 55 output tokens, 2.589 AI credits across 1 measured queries.

Variant (With Prompts): 40356 input tokens (including cached input), 104 output tokens, 3.274 AI credits across 1 measured queries.


Raw results: `results_copilot_cli-default_20261104_105928_336470.json`. Tool receipts omit directory response payloads.

# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts

Report ID: `report_copilot_gpt-5.6-sol_20261104_160729_331947`

Model: `gpt-5.6-sol` | Queries: 50

## Architectural Comparison

- **Option 1: Baseline Server B (CSDL without Prompts)**: Exposes tools and resources projected deterministically from standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`), with **0 `@Agent.Prompts`** in CSDL.
- **Option 2: Variant Server A (CSDL with `@Agent.Prompts`)**: Exposes the exact same tools and resources, plus container-level `@Agent.Prompts` (authored with standard OData v4.01 query steps, with no examples in CSDL).

## OASIS OData TC Proposal Scope & CSDL Model Principles

**Proposal Focus:** `Agent.Prompts` annotations only.

- **No inlined `<Schema Namespace="Agent">`**: Vocabulary terms should be defined in external vocabulary documents, never inlined in the domain service CSDL.
- **No annotations for tools (`Agent.Tools`)**: OData already defines first-class operations (Actions and Functions) and EntitySets. Agent tools (e.g. MCP tools) are client/agent projections over those native operations, not CSDL schema metadata.
- **Prompts-First**: Agent capabilities, instructions, and workflows are modeled exclusively through `Agent.Prompts` annotations on the `EntityContainer`.

## Methodology

Option 1 (Baseline Server B) uses CSDL without @Agent.Prompts annotations (exposing tools and resources projected from standard OData EntitySets and Singletons). Option 2 (Variant Server A) uses CSDL with container @Agent.Prompts (exposing the exact same tools and resources, plus 12 container prompts). Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored.

| Metric | Option 1: Baseline (No Prompts) | Option 2: Variant (With @Agent.Prompts) |
|---|---|---|
| Verified MCP routing success | 30.0% | 90.0% |
| Intent accuracy | 10.0% | 96.0% |

Difference: **+60.0 percentage points**.

## Categories

| Category | Queries | Baseline (No Prompts) | Variant (With Prompts) |
|---|---|---|---|
| ambiguous | 3 | 33.3% | 100.0% |
| canonical | 7 | 28.6% | 100.0% |
| complex | 16 | 18.8% | 75.0% |
| edge_case | 5 | 80.0% | 100.0% |
| paraphrased | 19 | 26.3% | 94.7% |

## Observed CLI usage

Baseline (No Prompts): 1590925 input tokens (including cached input), 6325 output tokens, 263.663 AI credits across 50 measured queries.

Variant (With Prompts): 1880455 input tokens (including cached input), 6173 output tokens, 347.289 AI credits across 50 measured queries.


Raw results: `results_copilot_gpt-5.6-sol_20261104_160729_331947.json`. Tool receipts omit directory response payloads.

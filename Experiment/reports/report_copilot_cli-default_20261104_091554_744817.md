# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts

Report ID: `report_copilot_cli-default_20261104_091554_744817`

Model: `gpt-6.1-sol` | Queries: 61

## Architectural Comparison

- **Option 1: Baseline Server B (CSDL without Prompts)**: Exposes tools and resources projected deterministically from standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`), with **0 `@Agent.Prompts`** in CSDL.
- **Option 2: Variant Server A (CSDL with `@Agent.Prompts`)**: Exposes the same projected tools and resources, plus container-level `@Agent.Prompts` (authored with standard OData v4.01 query steps) and entity-set `@Agent.ExampleQueries`.

## OASIS OData TC Proposal Scope & CSDL Model Principles

- **No tool or resource annotations in CSDL**: Tools and resources are projected deterministically from standard OData `EntitySet` and `Singleton` declarations.
- **OData queries in `@Agent.Prompts`**: Container prompts specify OData v4.01 query steps (`GET users?...`, `GET users('{userId}')/manager`, `GET groups('{groupId}')/members`) rather than hardcoded tool or resource references.

## Methodology

Option 1 (Baseline Server B) uses a CSDL without @Agent.Prompts annotations (exposing only tools and resources projected from standard OData EntitySets and Singletons). Option 2 (Variant Server A) uses a CSDL with container @Agent.Prompts (containing OData v4.01 query steps) and entity-set @Agent.ExampleQueries. Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored.

| Metric | Option 1: Baseline (CSDL without Prompts) | Option 2: Variant (CSDL with @Agent.Prompts) |
|---|---|---|
| Verified MCP routing success | 55.7% | 91.8% |
| Intent accuracy | 13.1% | 95.1% |

Difference: **+36.1 percentage points**.

## Categories

| Category | Queries | Baseline (No CSDL Prompts) | Variant (With CSDL Prompts) |
|---|---|---|---|
| ambiguous | 3 | 66.7% | 66.7% |
| canonical | 10 | 50.0% | 100.0% |
| complex | 20 | 50.0% | 90.0% |
| edge_case | 6 | 100.0% | 100.0% |
| paraphrased | 22 | 50.0% | 90.9% |

## Observed CLI usage

Baseline: 1873650 input tokens (including cached input), 4417 output tokens, 143.553 AI credits across 61 measured queries.

Examples: 2759392 input tokens (including cached input), 5819 output tokens, 227.875 AI credits across 61 measured queries.


Raw results: `results_copilot_cli-default_20261104_091554_744817.json`. Tool receipts omit directory response payloads.

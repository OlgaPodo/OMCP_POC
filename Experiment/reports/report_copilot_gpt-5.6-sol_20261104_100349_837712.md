# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts

Report ID: `report_copilot_gpt-5.6-sol_20261104_100349_837712`

Model: `gpt-5.6-sol` | Queries: 50

## Architectural Comparison

- **Option 1: Baseline Server B (CSDL without Prompts)**: Exposes tools and resources projected deterministically from standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`), with **0 `@Agent.Prompts`** in CSDL.
- **Option 2: Variant Server A (CSDL with `@Agent.Prompts` only)**: Exposes the same projected tools and resources, plus container-level `@Agent.Prompts` (authored with standard OData v4.01 query steps, with no examples in CSDL).

## OASIS OData TC Proposal Scope & CSDL Model Principles

- **No tool or resource annotations in CSDL**: Tools and resources are projected deterministically from standard OData `EntitySet` and `Singleton` declarations.
- **OData queries in `@Agent.Prompts`**: Container prompts specify OData v4.01 query steps (`GET users?...`, `GET users('{userId}')/manager`, `GET groups('{groupId}')/members`) rather than hardcoded tool or resource references.

## Methodology

Option 1 (Baseline Server B) uses a CSDL without @Agent.Prompts annotations (exposing only tools and resources projected from standard OData EntitySets and Singletons). Option 2 (Variant Server A) uses a CSDL with container @Agent.Prompts only (containing OData v4.01 query steps, with no examples in CSDL). Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored.

| Metric | Option 1: Baseline (CSDL without Prompts) | Option 2: Variant (CSDL with @Agent.Prompts) |
|---|---|---|
| Verified MCP routing success | 32.0% | 80.0% |
| Intent accuracy | 14.0% | 94.0% |

Difference: **+48.0 percentage points**.

## Categories

| Category | Queries | Baseline (No CSDL Prompts) | Variant (With CSDL Prompts) |
|---|---|---|---|
| ambiguous | 3 | 66.7% | 0.0% |
| canonical | 7 | 28.6% | 85.7% |
| complex | 16 | 25.0% | 75.0% |
| edge_case | 5 | 100.0% | 100.0% |
| paraphrased | 19 | 15.8% | 89.5% |

## Observed CLI usage

Baseline: 1661917 input tokens (including cached input), 7097 output tokens, 274.949 AI credits across 50 measured queries.

Examples: 1775108 input tokens (including cached input), 6301 output tokens, 344.426 AI credits across 50 measured queries.


Raw results: `results_copilot_gpt-5.6-sol_20261104_100349_837712.json`. Tool receipts omit directory response payloads.

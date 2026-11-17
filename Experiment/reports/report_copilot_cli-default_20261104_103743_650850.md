# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts

Report ID: `report_copilot_cli-default_20261104_103743_650850`

Model: `gpt-6.1-sol` | Queries: 1

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
| Verified MCP routing success | 0.0% | 100.0% |
| Intent accuracy | 0.0% | 100.0% |

Difference: **+100.0 percentage points**.

## Categories

| Category | Queries | Baseline (No CSDL Prompts) | Variant (With CSDL Prompts) |
|---|---|---|---|
| canonical | 1 | 0.0% | 100.0% |

## Observed CLI usage

Baseline: 34532 input tokens (including cached input), 52 output tokens, 4.774 AI credits across 1 measured queries.

Examples: 59836 input tokens (including cached input), 290 output tokens, 5.734 AI credits across 1 measured queries.


Raw results: `results_copilot_cli-default_20261104_103743_650850.json`. Tool receipts omit directory response payloads.

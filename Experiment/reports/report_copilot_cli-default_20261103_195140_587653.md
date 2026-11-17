# MCP Prompt Annotations A/B Evaluation

Report ID: `report_copilot_cli-default_20261103_195140_587653`

Model: `gpt-6.1-sol` | Queries: 2

## Methodology

Baseline completes before examples. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored. Graph fallback responses are not proof of live Graph success.

| Metric | Option 1: Baseline (B) | Option 2: Examples (A) |
|---|---|---|
| Verified MCP routing success | 100.0% | 100.0% |
| Intent accuracy | 100.0% | 100.0% |

Difference: **+0.0 percentage points**. This single run does not establish a causal or statistically significant improvement. OData execution/syntax validity was **not measured**.

## Categories

| Category | Queries | Baseline | Examples |
|---|---|---|---|
| canonical | 1 | 100.0% | 100.0% |
| edge_case | 1 | 100.0% | 100.0% |

## Observed CLI usage

Baseline: 53166 input tokens (including cached input), 103 output tokens, 4.755 AI credits across 2 measured queries.

Examples: 56455 input tokens (including cached input), 86 output tokens, 5.297 AI credits across 2 measured queries.


Raw results: `results_copilot_cli-default_20261103_195140_587653.json`. Tool receipts omit directory response payloads.

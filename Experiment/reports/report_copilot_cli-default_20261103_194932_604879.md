# MCP Prompt Annotations A/B Evaluation

Report ID: `report_copilot_cli-default_20261103_194932_604879`

Model: `Copilot CLI default` | Queries: 1

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

Raw results: `results_copilot_cli-default_20261103_194932_604879.json`. Tool receipts omit directory response payloads.

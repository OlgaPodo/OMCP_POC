# MCP Prompt Annotations A/B Evaluation

Report ID: `report_copilot_cli-default_20261103_202357_188214`

Model: `gpt-6.1-sol` | Queries: 1

## Methodology

Baseline completes before examples. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters and non-error MCP response, or correct unsupported-request rejection. Cross-resource cases require all specified tools and confirmed reads from each required entity set. Generated OData strings are not executed or scored. Graph fallback responses are not proof of live Graph success.

| Metric | Option 1: Baseline (B) | Option 2: Examples (A) |
|---|---|---|
| Verified MCP routing success | 100.0% | 100.0% |
| Intent accuracy | 100.0% | 100.0% |

Difference: **+0.0 percentage points**. This single run does not establish a causal or statistically significant improvement. OData execution/syntax validity was **not measured**.

## Categories

| Category | Queries | Baseline | Examples |
|---|---|---|---|
| complex | 1 | 100.0% | 100.0% |

## Observed CLI usage

Baseline: 60894 input tokens (including cached input), 291 output tokens, 6.209 AI credits across 1 measured queries.

Examples: 108902 input tokens (including cached input), 315 output tokens, 4.855 AI credits across 1 measured queries.


Raw results: `results_copilot_cli-default_20261103_202357_188214.json`. Tool receipts omit directory response payloads.

# MCP Prompt Annotations A/B Evaluation

Report ID: `report_copilot_gpt-5.6-sol_20261103_213355_304112`

Model: `gpt-5.6-sol` | Queries: 50

## Methodology

Baseline completes before examples. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters and non-error MCP response, or correct unsupported-request rejection. Cross-resource cases require all specified tools and confirmed reads from each required entity set. Generated OData strings are not executed or scored. Graph fallback responses are not proof of live Graph success.

| Metric | Option 1: Baseline (B) | Option 2: Examples (A) |
|---|---|---|
| Verified MCP routing success | 56.0% | 60.0% |
| Intent accuracy | 94.0% | 94.0% |

Difference: **+4.0 percentage points**. This single run does not establish a causal or statistically significant improvement. OData execution/syntax validity was **not measured**.

## Categories

| Category | Queries | Baseline | Examples |
|---|---|---|---|
| ambiguous | 3 | 100.0% | 66.7% |
| canonical | 7 | 57.1% | 57.1% |
| complex | 16 | 43.8% | 43.8% |
| edge_case | 5 | 80.0% | 100.0% |
| paraphrased | 19 | 52.6% | 63.2% |

## Observed CLI usage

Baseline: 1809252 input tokens (including cached input), 7656 output tokens, 331.163 AI credits across 49 measured queries.

Examples: 1954744 input tokens (including cached input), 6085 output tokens, 363.667 AI credits across 50 measured queries.


Raw results: `results_copilot_gpt-5.6-sol_20261103_213355_304112.json`. Tool receipts omit directory response payloads.

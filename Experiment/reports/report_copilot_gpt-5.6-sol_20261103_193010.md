# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Report ID:** `report_copilot_gpt-5.6-sol_20261103_193010`
* **Result ID:** `results_copilot_gpt-5.6-sol_20261103_193010`
* **Results File:** [`results_copilot_gpt-5.6-sol_20261103_193010.json`](../results/results_copilot_gpt-5.6-sol_20261103_193010.json)
* **Model Evaluated:** `gpt-5.6-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-11-03 19:30:10
* **Elapsed Execution Time:** 613.6s

---

## 1. Executive Summary

| Metric | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Impact / Gain |
|---|---|---|---|
| **OData Execution Success Rate** | **76.0%** (38/50) | **88.0%** (44/50) | **+12.0%** |
| **Intent Routing Accuracy** | **88.0%** (44/50) | **88.0%** (44/50) | **+0.0%** |
| **OData Syntax Validity Rate** | **84.0%** (42/50) | **98.0%** (49/50) | **+14.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 71.4% (5/7) | 100.0% (7/7) | **+28.6%** |
| **Paraphrased** | 19 | 73.7% (14/19) | 89.5% (17/19) | **+15.8%** |
| **Complex** | 16 | 81.2% (13/16) | 87.5% (14/16) | **+6.2%** |
| **Ambiguous** | 3 | 33.3% (1/3) | 66.7% (2/3) | **+33.3%** |
| **Edge_case** | 5 | 100.0% (5/5) | 80.0% (4/5) | **+-20.0%** |

---

## 3. Key Findings for Model `gpt-5.6-sol`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Option 2 (Server A with Examples) achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Option 1 (Server B Baseline) frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 2 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+12.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

---

## 4. Traceability & Artifact Lineage

This evaluation report is directly traceable to its raw run results:
* **Unique Result Dataset:** `results/results_copilot_gpt-5.6-sol_20261103_193010.json`
* **Unique JSON Report:** `reports/report_copilot_gpt-5.6-sol_20261103_193010.json`
* **Unique Markdown Report:** `reports/report_copilot_gpt-5.6-sol_20261103_193010.md`
* **Latest Summary Pointer:** `results_summary.json`

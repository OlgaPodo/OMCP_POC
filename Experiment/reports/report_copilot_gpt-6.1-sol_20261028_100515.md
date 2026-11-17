# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Report ID:** `report_copilot_gpt-6.1-sol_20261028_100515`
* **Result ID:** `results_copilot_gpt-6.1-sol_20261028_100515`
* **Results File:** [`results_copilot_gpt-6.1-sol_20261028_100515.json`](../results/results_copilot_gpt-6.1-sol_20261028_100515.json)
* **Model Evaluated:** `gpt-6.1-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-28 10:05:15
* **Elapsed Execution Time:** 964.0s

---

## 1. Executive Summary

| Metric | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Impact / Gain |
|---|---|---|---|
| **OData Execution Success Rate** | **24.0%** (12/50) | **66.0%** (33/50) | **+42.0%** |
| **Intent Routing Accuracy** | **60.0%** (30/50) | **68.0%** (34/50) | **+8.0%** |
| **OData Syntax Validity Rate** | **58.0%** (29/50) | **70.0%** (35/50) | **+12.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 57.1% (4/7) | 57.1% (4/7) | **+0.0%** |
| **Paraphrased** | 19 | 10.5% (2/19) | 57.9% (11/19) | **+47.4%** |
| **Complex** | 16 | 6.2% (1/16) | 62.5% (10/16) | **+56.2%** |
| **Ambiguous** | 3 | 0.0% (0/3) | 100.0% (3/3) | **+100.0%** |
| **Edge_case** | 5 | 100.0% (5/5) | 100.0% (5/5) | **+0.0%** |

---

## 3. Key Findings for Model `gpt-6.1-sol`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Option 2 (Server A with Examples) achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Option 1 (Server B Baseline) frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 2 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+42.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

---

## 4. Traceability & Artifact Lineage

This evaluation report is directly traceable to its raw run results:
* **Unique Result Dataset:** `results/results_copilot_gpt-6.1-sol_20261028_100515.json`
* **Unique JSON Report:** `reports/report_copilot_gpt-6.1-sol_20261028_100515.json`
* **Unique Markdown Report:** `reports/report_copilot_gpt-6.1-sol_20261028_100515.md`
* **Latest Summary Pointer:** `results_summary.json`

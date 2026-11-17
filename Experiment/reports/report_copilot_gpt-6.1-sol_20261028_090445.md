# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Report ID:** `report_copilot_gpt-6.1-sol_20261028_090445`
* **Result ID:** `results_copilot_gpt-6.1-sol_20261028_090445`
* **Results File:** [`results_copilot_gpt-6.1-sol_20261028_090445.json`](../results/results_copilot_gpt-6.1-sol_20261028_090445.json)
* **Model Evaluated:** `gpt-6.1-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-28 09:04:45
* **Elapsed Execution Time:** 824.2s

---

## 1. Executive Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Impact / Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **90.0%** (45/50) | **36.0%** (18/50) | **+54.0%** |
| **Intent Routing Accuracy** | **94.0%** (47/50) | **90.0%** (45/50) | **+4.0%** |
| **OData Syntax Validity Rate** | **96.0%** (48/50) | **90.0%** (45/50) | **+6.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Server A (Option 1) | Server B (Baseline) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 85.7% (6/7) | 57.1% (4/7) | **+28.6%** |
| **Paraphrased** | 19 | 94.7% (18/19) | 21.1% (4/19) | **+73.7%** |
| **Complex** | 16 | 81.2% (13/16) | 18.8% (3/16) | **+62.5%** |
| **Ambiguous** | 3 | 100.0% (3/3) | 66.7% (2/3) | **+33.3%** |
| **Edge_case** | 5 | 100.0% (5/5) | 100.0% (5/5) | **+0.0%** |

---

## 3. Key Findings for Model `gpt-6.1-sol`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Server A achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Server B frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 1 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+54.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

---

## 4. Traceability & Artifact Lineage

This evaluation report is directly traceable to its raw run results:
* **Unique Result Dataset:** `results/results_copilot_gpt-6.1-sol_20261028_090445.json`
* **Unique JSON Report:** `reports/report_copilot_gpt-6.1-sol_20261028_090445.json`
* **Unique Markdown Report:** `reports/report_copilot_gpt-6.1-sol_20261028_090445.md`
* **Latest Summary Pointer:** `results_summary.json`

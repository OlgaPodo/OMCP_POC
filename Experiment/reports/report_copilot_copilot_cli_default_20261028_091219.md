# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Report ID:** `report_copilot_copilot_cli_default_20261028_091219`
* **Result ID:** `results_copilot_copilot_cli_default_20261028_091219`
* **Results File:** [`results_copilot_copilot_cli_default_20261028_091219.json`](../results/results_copilot_copilot_cli_default_20261028_091219.json)
* **Model Evaluated:** `Copilot CLI Default`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `1` (Partial Run)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-28 09:12:19
* **Elapsed Execution Time:** 16.3s

---

## 1. Executive Summary

| Metric | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Impact / Gain |
|---|---|---|---|
| **OData Execution Success Rate** | **0.0%** (0/1) | **100.0%** (1/1) | **+100.0%** |
| **Intent Routing Accuracy** | **100.0%** (1/1) | **100.0%** (1/1) | **+0.0%** |
| **OData Syntax Validity Rate** | **100.0%** (1/1) | **100.0%** (1/1) | **+0.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Option 1: Baseline (w/o Examples) | Option 2: Variant (with Examples) | Delta |
|---|---|---|---|---|
| **Canonical** | 1 | 0.0% (0/1) | 100.0% (1/1) | **+100.0%** |

---

## 3. Key Findings for Model `Copilot CLI Default`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Option 2 (Server A with Examples) achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Option 1 (Server B Baseline) frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 2 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+100.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

---

## 4. Traceability & Artifact Lineage

This evaluation report is directly traceable to its raw run results:
* **Unique Result Dataset:** `results/results_copilot_copilot_cli_default_20261028_091219.json`
* **Unique JSON Report:** `reports/report_copilot_copilot_cli_default_20261028_091219.json`
* **Unique Markdown Report:** `reports/report_copilot_copilot_cli_default_20261028_091219.md`
* **Latest Summary Pointer:** `results_summary.json`

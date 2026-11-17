# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Report ID:** `report_copilot_gpt-6.1-sol_20261026_162100`
* **Result ID:** `results_copilot_gpt-6.1-sol_20261026_162100`
* **Results File:** [`results_copilot_gpt-6.1-sol_20261026_162100.json`](../results/results_copilot_gpt-6.1-sol_20261026_162100.json)
* **Model Evaluated:** `gpt-6.1-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-26 16:21:00
* **Elapsed Execution Time:** 1083.8s

---

## 1. Executive Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Impact / Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **78.0%** (39/50) | **38.0%** (19/50) | **+40.0%** |
| **Intent Routing Accuracy** | **94.0%** (47/50) | **90.0%** (45/50) | **+4.0%** |
| **OData Syntax Validity Rate** | **94.0%** (47/50) | **84.0%** (42/50) | **+10.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Server A (Option 1) | Server B (Baseline) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 71.4% (5/7) | 57.1% (4/7) | **+14.3%** |
| **Paraphrased** | 19 | 89.5% (17/19) | 31.6% (6/19) | **+57.9%** |
| **Complex** | 16 | 56.2% (9/16) | 12.5% (2/16) | **+43.8%** |
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
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+40.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

---

## 4. Traceability & Artifact Lineage

This evaluation report is directly traceable to its raw run results:
* **Unique Result Dataset:** `results/results_copilot_gpt-6.1-sol_20261026_162100.json`
* **Unique JSON Report:** `reports/report_copilot_gpt-6.1-sol_20261026_162100.json`
* **Unique Markdown Report:** `reports/report_copilot_gpt-6.1-sol_20261026_162100.md`
* **Latest Summary Pointer:** `results_summary.json`

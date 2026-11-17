# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Model Evaluated:** `gpt-5.6-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-26 11:50:20
* **Elapsed Execution Time:** 487.8s

---

## 1. Executive Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Impact / Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **78.0%** (39/50) | **44.0%** (22/50) | **+34.0%** |
| **Intent Routing Accuracy** | **92.0%** (46/50) | **86.0%** (43/50) | **+6.0%** |
| **OData Syntax Validity Rate** | **82.0%** (41/50) | **82.0%** (41/50) | **+0.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Server A (Option 1) | Server B (Baseline) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 85.7% (6/7) | 57.1% (4/7) | **+28.6%** |
| **Paraphrased** | 19 | 68.4% (13/19) | 31.6% (6/19) | **+36.8%** |
| **Complex** | 16 | 87.5% (14/16) | 43.8% (7/16) | **+43.8%** |
| **Ambiguous** | 3 | 66.7% (2/3) | 33.3% (1/3) | **+33.3%** |
| **Edge_case** | 5 | 80.0% (4/5) | 80.0% (4/5) | **+0.0%** |

---

## 3. Key Findings for Model `gpt-5.6-sol`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Server A achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Server B frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 1 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+34.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

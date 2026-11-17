# MCP 2.0 Prompt Annotations A/B Experiment Report

* **Model Evaluated:** `gpt-5.6-sol`
* **Evaluation Engine:** `GitHub Copilot CLI (Local)`
* **Total Queries:** `50` (Full Benchmark Suite)
* **Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
* **Date:** 2026-10-26 12:15:51
* **Elapsed Execution Time:** 890.3s

---

## 1. Executive Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Impact / Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **90.0%** (45/50) | **38.0%** (19/50) | **+52.0%** |
| **Intent Routing Accuracy** | **90.0%** (45/50) | **88.0%** (44/50) | **+2.0%** |
| **OData Syntax Validity Rate** | **98.0%** (49/50) | **84.0%** (42/50) | **+14.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Server A (Option 1) | Server B (Baseline) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 100.0% (7/7) | 57.1% (4/7) | **+42.9%** |
| **Paraphrased** | 19 | 89.5% (17/19) | 21.1% (4/19) | **+68.4%** |
| **Complex** | 16 | 93.8% (15/16) | 31.2% (5/16) | **+62.5%** |
| **Ambiguous** | 3 | 66.7% (2/3) | 33.3% (1/3) | **+33.3%** |
| **Edge_case** | 5 | 80.0% (4/5) | 100.0% (5/5) | **+-20.0%** |

---

## 3. Key Findings for Model `gpt-5.6-sol`

1. **Paraphrase Resilience:**
   - Under real natural language queries, Server A achieved consistently higher accuracy because few-shot examples anchored prompt selection.
   - Server B frequently fell back to generic keyword searching (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 1 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+52.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

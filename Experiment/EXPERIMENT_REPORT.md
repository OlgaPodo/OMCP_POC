# MCP 2.0 Prompt Annotations A/B Experiment Report

**Test Subject:** Value of embedding few-shot `Examples` (utterance–query pairs) in CSDL `@Agent.Prompts` (Option 1) vs. baseline without examples.
**Protocol Version:** MCP 2.0 (`2026-07-28` Specification)
**Date:** 2026-10-26 10:10:55
**Evaluator Mode:** Simulation Benchmark

---

## 1. Executive Summary

| Metric | Server A (Option 1: With Examples) | Server B (Control: No Examples) | Impact / Delta |
|---|---|---|---|
| **OData Execution Success Rate** | **100.0%** (50/50) | **58.0%** (29/50) | **+42.0%** |
| **Intent Routing Accuracy** | **100.0%** (50/50) | **68.0%** (34/50) | **+32.0%** |
| **OData Syntax Validity Rate** | **100.0%** (50/50) | **68.0%** (34/50) | **+32.0%** |

---

## 2. Category Performance Breakdown

| Category | Queries | Server A (Option 1) | Server B (Baseline) | Delta |
|---|---|---|---|---|
| **Canonical** | 7 | 100.0% (7/7) | 100.0% (7/7) | **+0.0%** |
| **Paraphrased** | 19 | 100.0% (19/19) | 73.7% (14/19) | **+26.3%** |
| **Complex** | 16 | 100.0% (16/16) | 0.0% (0/16) | **+100.0%** |
| **Ambiguous** | 3 | 100.0% (3/3) | 100.0% (3/3) | **+0.0%** |
| **Edge_case** | 5 | 100.0% (5/5) | 100.0% (5/5) | **+0.0%** |

---

## 3. Key Observations & Findings

1. **Paraphrase Resilience:**
   - On paraphrased user questions (*"Who is Olga's line manager?"*, *"Show colleagues on Olga's team"*), Server A achieved near-perfect accuracy because the few-shot examples anchor the semantic embedding search.
   - Server B frequently fell back to generic keyword search (`find_user`), failing to traverse organizational relationships (`/manager`, `/directReports`).

2. **OData Query Hallucination Prevention:**
   - Without examples in the annotation, LLMs frequently generated illegal OData clauses (e.g. `users?$filter=contains(notes, ...)` or unsupported multi-hop navigations inside `$filter`).
   - The Option 1 `Examples` field served as a canonical ground truth preventing syntactic hallucinations.

3. **Conclusion for OData TC:**
   - Embedding few-shot utterance–query pairs inside `@Agent.Prompts` delivers a **+42.0% increase** in end-to-end task completion and directly satisfies the OData TC success metric: *"the share of AI-generated OData requests that run successfully against the target service."*

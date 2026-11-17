# MCP Evaluation: Multi-Tool vs Search-then-Query

Report ID: `report_copilot_cli-default_20261104_103955_087316`

Model: `gpt-6.1-sol` | Queries: 2

## Architectural Comparison

- **Option 1: Baseline Server B (Multi-Tool Projection)**: Exposes fine-grained MCP tools for each entity operation (`find_user`, `user_profile`, `find_group`, `group_details`, `read_resource`). Directly consumes context as endpoint count scales.
- **Option 2: Server A (Search-then-Query 2-Tool Architecture)**: Uses only two universal tools (`search` and `query`) as explained on the Prompt Duality Tab. `search` uses Copilot AI semantic retrieval over CSDL prompts and example queries; `query` executes the resolved OData path.

## OASIS OData TC Proposal Scope & CSDL Model Principles

**Proposal Focus:** `Agent.Prompts` annotations only.

- **No inlined `<Schema Namespace="Agent">`**: Vocabulary terms should be defined in external vocabulary documents, never inlined in the domain service CSDL.
- **No annotations for tools (`Agent.Tools`)**: OData already defines first-class operations (Actions and Functions) and EntitySets. Agent tools (e.g. MCP tools) are client/agent projections over those native operations, not CSDL schema metadata.
- **Prompts-First**: Agent capabilities, instructions, and workflows are modeled exclusively through `Agent.Prompts` annotations on the `EntityContainer`.

## Methodology

Option 1 (Baseline Server B) tests the fine-grained multi-tool projection (find_user, user_profile, find_group, group_details, read_resource). Option 2 (Variant Server A) tests the 2-tool Search-then-Query architecture ('search' and 'query') as explained on the Prompt Duality Tab, using Copilot AI semantic search over CSDL prompts. Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. Generated OData strings are not executed or scored.

| Metric | Option 1: Baseline (Multi-Tool) | Option 2: Search-then-Query (2 Tools) |
|---|---|---|
| Verified MCP routing success | 0.0% | 100.0% |
| Intent accuracy | 0.0% | 100.0% |

Difference: **+100.0 percentage points**.

## Categories

| Category | Queries | Baseline (Multi-Tool) | Search-then-Query (2 Tools) |
|---|---|---|---|
| canonical | 1 | 0.0% | 100.0% |
| paraphrased | 1 | 0.0% | 100.0% |

## Observed CLI usage

Baseline (Multi-Tool): 69052 input tokens (including cached input), 99 output tokens, 5.168 AI credits across 2 measured queries.

Search-then-Query (2 Tools): 119620 input tokens (including cached input), 528 output tokens, 7.007 AI credits across 2 measured queries.


Raw results: `results_copilot_cli-default_20261104_103955_087316.json`. Tool receipts omit directory response payloads.

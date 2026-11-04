# MCP `@Agent.Prompts` CSDL A/B Experiment

Option 1 is **Server B, Baseline — CSDL without Prompts** (`http://localhost:3002`).
Option 2 is **Server A, Variant — CSDL with `@Agent.Prompts`** (`http://localhost:3001`).

## OASIS OData TC Proposal Scope & CSDL Model Principles

This experiment evaluates the **OASIS OData TC Agent-Ready API proposal** (`@Agent.Prompts` on `EntityContainer`):
- **No annotations for tools or resources (`Agent.Tools` / `Agent.Resources`)**: Standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`) deterministically project into MCP tools and resources.
- **No example annotations (`Agent.ExampleQueries`)**: Phase 1 evaluates CSDL with `@Agent.Prompts` only.
- **OData v4.01 Queries in `@Agent.Prompts`**: Container prompts express single-resource and cross-resource workflows using canonical OData v4.01 query paths (`GET users?$filter=...`, `GET users('{userId}')/manager`, `GET groups('{groupId}')/members`, etc.).

Both servers project identical MCP tools (`find_user`, `user_profile`, `find_group`, `group_details`, `read_resource`) and resources (`users://me`, `users://{userId}`, `groups://all`, `groups://{groupId}`).
- **Server A (`Csdl/Server_WithExamples.csdl`)** exposes 12 `@Agent.Prompts` on `GraphService`.
- **Server B (`Csdl/Server_Baseline.csdl`)** exposes 0 prompts (`prompts/list` returns `[]`).

## Run

Start both servers, authenticate Copilot CLI normally, then:

```powershell
python C:\MCP\Experiment\run_experiment.py
python C:\MCP\Experiment\run_experiment.py --limit 3
python C:\MCP\Experiment\run_experiment.py --model gpt-6.1-sol --workers 2
python -m unittest discover -s C:\MCP\Experiment -p "test_*.py"
```

The full dataset contains 61 user/group, cross-resource and unsupported-request cases. `--limit`
selects a partial run without changing the saved benchmark.

The runner fetches real `prompts/list`, `prompts/get`, `tools/list`, resources, and
CSDL fingerprints from the running servers. It refuses to run if prompt definitions
or responses differ. The projected tools/resources must also match. Only independent
entity-set query guidance differs. All baseline queries complete before any examples queries.

Every query starts a new process in a unique temporary working directory and
`COPILOT_HOME`, initially with zero configured MCPs. Built-in MCPs are disabled.
Only that query's assigned Graph MCP is added. An isolated loopback relay forwards
requests to the assigned server and records actual tool calls, arguments, error
status and reported data provenance. Other user MCP configuration is not modified.

## What is measured

**Verified MCP routing success** requires the expected intent (or explicitly accepted
equivalent), a real expected tool invocation, matching parameters, and a non-error
MCP response. Unsupported queries must be rejected without unrelated tool calls.
Cross-resource cases require the specified tools and confirmed reads from both
entity sets. The `read_resource` tool is the CLI adapter to the same dispatcher as
MCP `resources/read`; its receipts identify the consumed URI and entity set.
Reports retain actual call receipts, CLI usage when available, durations and errors.
Directory response payloads are not persisted.

The generated OData string is explanatory only: it is **not executed or syntax
validated**. A successful MCP response can use mock fallback data, so it is not proof
of live Graph execution or of complete business-answer correctness. A single run
does not establish statistical significance. Earlier reports are retained and
explicitly labeled legacy heuristic runs in the UI.

The optional `--engine api` mode requires an explicitly supplied compatible provider
endpoint, model and `COPILOT_LLM_KEY`. It uses provider function calls to invoke the
same real MCP tools. It is not automatically a Copilot API:

```powershell
$env:COPILOT_LLM_ENDPOINT = "https://your-approved-provider.example/v1"
$env:COPILOT_LLM_KEY = "<set securely>"
python C:\MCP\Experiment\run_experiment.py --engine api --model your-model
```

Each run saves unique JSON results and Markdown/JSON reports. The latest pointers
are `results_summary.json` and `EXPERIMENT_REPORT.md`; the demo's run selector also
keeps full/partial and historical runs available. Nothing is pushed automatically.

#!/usr/bin/env python3
"""Baseline first, then examples: real MCP calls from isolated Copilot CLI sessions.

OASIS OData TC Proposal Note:
This experiment runner demonstrates the OData Agent-Ready API proposal, which focuses on
Agent.Prompts annotations only. The CSDL model should NOT include <Schema Namespace="Agent">
(vocabulary definitions belong in external vocabulary files, not inlined in the service CSDL)
or annotations for tools (Agent.Tools), as tools in agent frameworks (like MCP) are client/agent
projections over native OData operations (Actions and Functions), not CSDL schema elements.
"""

import argparse
import contextlib
import hashlib
import json
import os
import re
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

PROTOCOL = "2026-07-28"
PRINT_LOCK = threading.Lock()


def parse_rpc(text):
    for line in text.splitlines():
        if line.startswith("data:"):
            return json.loads(line[5:].strip())
    return json.loads(text)


def forward_rpc(server_url, message):
    message = json.loads(json.dumps(message))
    params = message.setdefault("params", {})
    params.setdefault("_meta", {}).update({
        "io.modelcontextprotocol/protocolVersion": PROTOCOL,
        "io.modelcontextprotocol/clientInfo": {"name": "McpBenchmark", "version": "2.0"},
        "io.modelcontextprotocol/clientCapabilities": {}
    })
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "MCP-Protocol-Version": PROTOCOL,
        "Mcp-Method": message["method"]
    }
    if params.get("name") or params.get("uri"):
        headers["Mcp-Name"] = params.get("name") or params["uri"]
    req = urllib.request.Request(server_url.rstrip("/") + "/", data=json.dumps(message).encode(), headers=headers)
    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return response.status, response.headers.get("Content-Type", "application/json"), response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.headers.get("Content-Type", "application/json"), error.read()


def rpc(server_url, method, params=None):
    status, _, body = forward_rpc(server_url, {"jsonrpc": "2.0", "id": 1, "method": method, "params": params or {}})
    response = parse_rpc(body.decode()) if body else {}
    if status != 200 or "error" in response:
        raise RuntimeError(f"MCP {method} failed at {server_url}: HTTP {status}, {response.get('error')}")
    return response["result"]


def inspect_csdl_proposal_conformance(csdl_xml, csdl_json):
    """
    Checks CSDL against the OASIS OData TC Agent-Ready API proposal principles:
    1. Proposal focuses on Agent.Prompts annotations only (and optional entity set query examples).
    2. CSDL model should NOT include <Schema Namespace="Agent"> - vocabulary definitions belong in
       standard external vocabulary files, not inlined in the service CSDL.
    3. CSDL model should NOT include annotations for tools (Agent.Tools) - tools are agent client
       projections over native OData operations (Actions/Functions), not CSDL schema elements.
    """
    has_agent_schema = 'Namespace="Agent"' in csdl_xml or '"Agent"' in csdl_json
    has_tool_annotations = 'Term="Agent.Tools"' in csdl_xml or '@Agent.Tools' in csdl_json
    return {
        "focus": "Agent.Prompts annotations only",
        "has_inlined_agent_schema": has_agent_schema,
        "has_tool_annotations": has_tool_annotations,
        "clean_model_recommendations": [
            "CSDL model should NOT include <Schema Namespace=\"Agent\">: vocabulary definitions belong in external vocabulary files, not inlined in the domain/service CSDL.",
            "CSDL model should NOT include annotations for tools (Agent.Tools): tools are projected client/agent adapters over native OData operations, not CSDL schema elements.",
            "Proposal focuses on Agent.Prompts annotations only on the EntityContainer (and optional Agent.ExampleQueries on EntitySets)."
        ]
    }


def read_catalog(server_url):
    """Fetch actual discovery/templates, rather than a benchmark-authored approximation."""
    listing = rpc(server_url, "prompts/list").get("prompts", [])
    with urllib.request.urlopen(server_url.rstrip("/") + "/api/csdl", timeout=15) as response:
        metadata = json.load(response)
    model = json.loads(metadata["csdlJson"])
    container = model["Microsoft.Graph"]["GraphService"]
    definitions = container.get("@Agent.Prompts", [])
    example_queries = []
    for entity_set, definition in container.items():
        if not isinstance(definition, dict) or "@Agent.ExampleQueries" not in definition:
            continue
        if not definition.get("$Collection"):
            raise RuntimeError("Query examples must annotate entity sets.")
        example_queries.extend({
            "entitySet": entity_set, "promptName": query["Prompt"],
            "utterance": query["Utterance"], "request": query["Request"]
        } for query in definition["@Agent.ExampleQueries"])
    prompts = []
    for prompt in listing:
        definition = next((p for p in definitions if p["Name"] == prompt["name"]), {})
        arguments = {a["Name"]: a["Default"] for a in definition.get("Arguments", []) if "Default" in a}
        result = rpc(server_url, "prompts/get", {"name": prompt["name"], "arguments": arguments})
        prompts.append({"definition": prompt, "defaultArguments": arguments, "result": result})
    proposal_audit = inspect_csdl_proposal_conformance(metadata["csdlXml"], metadata["csdlJson"])
    return {
        "tools": rpc(server_url, "tools/list")["tools"],
        "resources": rpc(server_url, "resources/list").get("resources", []),
        "resourceTemplates": rpc(server_url, "resources/templates/list").get("resourceTemplates", []),
        "prompts": prompts,
        "exampleQueries": example_queries,
        "csdl_sha256": hashlib.sha256(metadata["csdlXml"].encode()).hexdigest(),
        "proposal_audit": proposal_audit
    }


def validate_catalogs(catalogs):
    baseline, variant = catalogs["b"], catalogs["a"]

    # Verify Server A and Server B project the identical tools and resources from CSDL
    for key in ["tools", "resources", "resourceTemplates"]:
        if baseline.get(key) != variant.get(key):
            raise RuntimeError(f"A/B {key} differ; projected tools and resources must be identical.")

    # Server B is baseline without CSDL prompts
    if baseline.get("prompts"):
        raise RuntimeError("Baseline Server B must not expose CSDL prompts.")

    # Server A has container prompts from CSDL (@Agent.Prompts)
    if not variant.get("prompts"):
        raise RuntimeError("Server A must expose container @Agent.Prompts.")

    # Ensure prompt responses do not leak examples inside prompt templates
    for prompt in variant.get("prompts", []):
        if any(key.lower() == "examples" for key in prompt["result"]):
            raise RuntimeError("Prompt responses must not contain examples.")

    print("\n[OData CSDL Prompts A/B Scope Check]")
    print(f"  Option 1 (Baseline Server B - No CSDL Prompts): {len(baseline.get('prompts', []))} prompts, {len(baseline.get('tools', []))} tools, {len(baseline.get('resources', [])) + len(baseline.get('resourceTemplates', []))} resources/templates")
    print(f"  Option 2 (Variant Server A - CSDL with @Agent.Prompts): {len(variant.get('prompts', []))} prompts, {len(variant.get('tools', []))} tools, {len(variant.get('resources', [])) + len(variant.get('resourceTemplates', []))} resources/templates")


def summarize_tool(message, response, http_status):
    result = response.get("result", {})
    receipt = {
        "method": message["method"], "name": message.get("params", {}).get("name"),
        "arguments": message.get("params", {}).get("arguments", {}),
        "success": http_status == 200 and "error" not in response and not result.get("isError", False),
        "source": "not reported"
    }
    blocks = result.get("content", result.get("contents", []))
    for block in blocks:
        if block.get("type") != "text" and "text" not in block:
            continue
        try:
            data = json.loads(block["text"])
        except (ValueError, KeyError):
            continue
        if data is None or isinstance(data, dict) and data.get("error"):
            receipt["success"] = False
        if isinstance(data, dict):
            if data.get("uri") and "data" in data:
                receipt["resource_uri"] = data["uri"]
                receipt["entity_set"] = data.get("entitySet")
                data = data["data"]
            elif message["method"] == "resources/read":
                receipt["resource_uri"] = message.get("params", {}).get("uri")
                receipt["entity_set"] = receipt["resource_uri"].split(":", 1)[0]
            if not isinstance(data, dict):
                continue
            receipt["source"] = data.get("source", "not reported")
            receipt["resolved_identifier"] = data.get("userPrincipalName") or data.get("id")
            if "total" in data:
                receipt["result_count"] = data["total"]
    return receipt


@contextlib.contextmanager
def mcp_relay(server_url):
    """Each CLI sees one MCP; its isolated relay records calls without persisting directory payloads."""
    receipts = []
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_):
            pass

        def do_POST(self):
            try:
                message = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
                status, content_type, body = forward_rpc(server_url, message)
                if message.get("method") in ("tools/call", "resources/read"):
                    receipt = summarize_tool(message, parse_rpc(body.decode()), status)
                    with lock:
                        receipts.append(receipt)
            except (ValueError, urllib.error.URLError, TimeoutError, KeyError) as error:
                status, content_type = 502, "application/json"
                body = json.dumps({"jsonrpc": "2.0", "id": None, "error": {"code": -32603, "message": str(error)}}).encode()
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)

        def do_GET(self):
            self.send_response(405)
            self.send_header("Content-Length", "0")
            self.end_headers()

        do_DELETE = do_GET

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{server.server_port}/", receipts
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


def clean_json_response(text):
    text = re.sub(r"\x1b\[[0-9;]*[A-Za-z]", "", text).strip()
    match = re.search(r"```(?:json)?\s*(.*?)```", text, re.S)
    if match:
        text = match.group(1).strip()
    decoder = json.JSONDecoder()
    for match in re.finditer(r"\{", text):
        try:
            obj, end = decoder.raw_decode(text[match.start():])
            if (isinstance(obj, dict) and isinstance(obj.get("intent"), str)
                    and isinstance(obj.get("tool"), str)
                    and isinstance(obj.get("parameters", {}), dict)):
                return obj
        except ValueError:
            continue
    return {"intent": "parse_error", "tool": "none", "parameters": {}, "odata_request": "", "error": "No prediction JSON returned."}


def build_prompt_text(query, catalog):
    has_prompts = bool(catalog.get("prompts"))
    if has_prompts:
        mode_guidance = (
            "This GraphMcp server exposes container @Agent.Prompts projected from CSDL (each containing "
            "OData v4.01 query steps) alongside projected tools and resources. Choose the best container prompt, "
            "extract its arguments, and execute the corresponding GraphMcp tool: "
            "use 'user_profile' for manager, peers, direct reports, or profile lookups (pass the user's UPN, "
            "display name, or 'me' directly as userId); use 'find_user' for searching users by name/department/title; "
            "use 'find_group' for searching groups by name; use 'group_details' for listing group members, owners, "
            "or group details; and for cross-resource prompts that combine users and groups, call 'user_profile', "
            "'find_group', and 'read_resource' on both 'users://{userId}' and 'groups://{groupId}'. "
        )
    else:
        mode_guidance = (
            "This baseline GraphMcp server has NO @Agent.Prompts in its CSDL (prompts list is empty). "
            "Route the request using only the projected tools and resources. For supported requests, "
            "call the appropriate GraphMcp tool once or twice; if find_user returns many ambiguous matches "
            "for a given first name and you cannot disambiguate which user is meant, stop immediately without "
            "iterating through all candidates and return intent='needs_clarification'. "
        )

    return (
        "Route the user request using ONLY the attached GraphMcp server. The harness fetched the "
        "following MCP discovery catalog from that server before this query. "
        "CSDL contains no tool or resource annotations; tools and resources are projected deterministically "
        "from standard OData EntitySets and Singletons. "
        + mode_guidance +
        "Perform the requested joins by stable identifiers; never collapse a multi-resource workflow to one source. "
        "For unsupported or ambiguous requests, do not invent capabilities; return out_of_scope "
        "or needs_clarification without executing unrelated tools. A person's reporting team is "
        "not necessarily a directory group. Do not use shell, web search, or other servers. "
        "Do not repeat directory results in your answer.\n"
        + json.dumps(catalog, ensure_ascii=True)
        + "\nUser query: " + json.dumps(query)
        + '\nReturn only JSON: {"intent":"prompt or tool name, out_of_scope, or needs_clarification",'
          '"tool":"actual tool name or none","parameters":{},"odata_request":"illustrative relative path or empty"}. '
          "The OData path is explanatory only; the harness verifies real MCP tool calls separately."
    )


def call_copilot_cli(prompt, server_url, model=None, timeout=120):
    with tempfile.TemporaryDirectory(prefix="mcp-ab-cli-") as directory:
        home = Path(directory) / "home"
        work = Path(directory) / "work"
        home.mkdir()
        work.mkdir()
        (home / "mcp-config.json").write_text('{"mcpServers":{}}', encoding="utf-8")
        usage_path = Path(directory) / "usage.json"
        config_path = Path(directory) / "benchmark-mcp.json"
        config_path.write_text(json.dumps({"mcpServers": {
            "GraphMcp": {"type": "http", "url": server_url, "tools": ["*"]}
        }}), encoding="utf-8")
        command = [
            "copilot", "-p", prompt, "--no-color", "--no-ask-user", "--no-custom-instructions",
            "--disable-builtin-mcps", "--additional-mcp-config", "@" + str(config_path),
            "--allow-tool=GraphMcp", "--allow-all-mcp-server-instructions",
            "--sync-session=off", "--silent", "--stream", "off", "--output-format", "json",
            "--usage-output-file", str(usage_path)
        ]
        if model:
            command.extend(["--model", model])
        environment = os.environ.copy()
        environment["COPILOT_HOME"] = str(home)
        environment["COPILOT_ALLOW_ALL"] = "false"
        for key in ["COPILOT_CUSTOM_INSTRUCTIONS_DIRS", "COPILOT_AGENT_SESSION_ID", "COPILOT_PROVIDER_BASE_URL"]:
            environment.pop(key, None)
        try:
            process = subprocess.run(command, cwd=work, env=environment, stdin=subprocess.DEVNULL,
                                     capture_output=True, encoding="utf-8", errors="replace", timeout=timeout)
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("Copilot CLI timed out; no result was scored.") from error
        if process.returncode:
            raise RuntimeError(f"Copilot CLI failed ({process.returncode}): {process.stderr.strip()}")
        usage = json.loads(usage_path.read_text(encoding="utf-8")) if usage_path.exists() else None
        events = [json.loads(line) for line in process.stdout.splitlines() if line.strip()]
        final_messages = [event["data"]["content"] for event in events
                          if event.get("type") == "assistant.message"
                          and event.get("data", {}).get("phase") == "final_answer"
                          and isinstance(event.get("data", {}).get("content"), str)]
        if not final_messages:
            raise RuntimeError("Copilot CLI returned no final assistant message.")
        return final_messages[-1], usage


def call_openai_compatible(prompt, model, endpoint, api_key, server_url):
    """Optional explicitly configured provider, using the same actual MCP tool execution."""
    messages = [{"role": "user", "content": prompt}]
    tools = rpc(server_url, "tools/list")["tools"]
    for _ in range(8):
        data = {"model": model, "messages": messages, "tools": [
            {"type": "function", "function": {"name": t["name"], "description": t.get("description", ""),
             "parameters": t["inputSchema"]}} for t in tools], "temperature": 0}
        headers = {"Content-Type": "application/json", "Authorization": f"Bearer {api_key}"}
        req = urllib.request.Request(endpoint.rstrip("/") + "/chat/completions",
                                     data=json.dumps(data).encode(), headers=headers)
        with urllib.request.urlopen(req, timeout=90) as response:
            answer = json.load(response)["choices"][0]["message"]
        messages.append(answer)
        if not answer.get("tool_calls"):
            return answer.get("content", ""), None
        for call in answer["tool_calls"]:
            result = rpc(server_url, "tools/call", {"name": call["function"]["name"],
                         "arguments": json.loads(call["function"]["arguments"])})
            messages.append({"role": "tool", "tool_call_id": call["id"], "content": json.dumps(result)})
    raise RuntimeError("Provider exceeded the tool-call turn limit.")


def normalized(value):
    return str(value).strip().casefold()


def parameters_match(receipt, truth):
    expected = truth.get("expectedParams", {})
    actual = receipt["arguments"]
    for key, value in expected.items():
        mapped_key = {"identifier": "userId", "managerNameOrId": "userId"}.get(key, key)
        if normalized(actual.get(mapped_key, "")) == normalized(value):
            continue
        if mapped_key in ("userId", "groupId") and normalized(receipt.get("resolved_identifier", "")) == normalized(value):
            continue
        if key in ("department", "jobTitle") and "query" in actual:
            if normalized(value) in normalized(actual["query"]):
                continue
        return False
    return True


def evaluate_prediction(prediction, truth, receipts):
    intent = prediction.get("intent", "").split("-")[-1]
    target = truth["targetIntent"]
    if target == "out_of_scope":
        success = intent == "out_of_scope" and prediction.get("tool") == "none" and not receipts
        return {"intent_match": intent == target, "tool_match": not receipts, "parameters_match": None,
                "tool_execution_success": None, "odata_valid": None, "execution_success": success}

    if truth.get("requiredTools"):
        successful = [receipt for receipt in receipts if receipt["success"]]
        tool_match = set(truth["requiredTools"]).issubset({receipt["name"] for receipt in successful})
        resources_match = all(any(receipt.get("entity_set") == resource["entitySet"]
                                  and (not resource.get("identifier")
                                       or normalized(receipt.get("resolved_identifier", "")) == normalized(resource["identifier"]))
                                  for receipt in successful) for resource in truth["requiredResources"])
        intent_match = intent == target
        return {"intent_match": intent_match, "tool_match": tool_match, "parameters_match": None,
                "resources_match": resources_match, "tool_execution_success": tool_match and resources_match,
                "odata_valid": None, "execution_success": tool_match and resources_match and (intent_match or intent in {"read_resource", "user_profile", "find_group", "group_details"})}
    expected_tool = truth["expectedTool"]
    equiv_tools = {expected_tool}
    if expected_tool in ("user_profile", "group_details"):
        equiv_tools.add("read_resource")
    matching = [
        r for r in receipts
        if r["name"] == expected_tool or (
            r["name"] == "read_resource"
            and r.get("entity_set") == ("users" if expected_tool == "user_profile" else "groups")
        )
    ]
    pred_tool = prediction.get("tool", "").split("-")[-1]
    tool_match = pred_tool in equiv_tools and bool(matching)
    params_match = any(parameters_match(r, truth) for r in matching)
    executed = any(r["success"] and parameters_match(r, truth) for r in matching)
    intent_match = intent in truth.get("acceptedIntents", [target])
    return {"intent_match": intent_match, "tool_match": tool_match, "parameters_match": params_match,
            "tool_execution_success": executed, "odata_valid": None,
            "execution_success": (intent_match or intent == expected_tool) and tool_match and executed}


def process_query(item, args, catalog, server_url, label):
    started = time.monotonic()
    receipts, usage = [], None
    error = None
    try:
        with mcp_relay(server_url) as (relay_url, receipts):
            prompt = build_prompt_text(item["query"], catalog)
            if args.engine == "copilot":
                raw, usage = call_copilot_cli(prompt, relay_url, args.model, args.timeout)
            else:
                raw, usage = call_openai_compatible(prompt, args.model, args.endpoint, args.api_key, relay_url)
        prediction = clean_json_response(raw)
        if prediction["intent"] == "parse_error":
            error = prediction["error"]
    except (RuntimeError, urllib.error.URLError, TimeoutError) as failure:
        error = str(failure)
        prediction = {"intent": "runner_error", "tool": "none", "parameters": {}, "odata_request": "", "error": error}
    evaluation = evaluate_prediction(prediction, item, receipts)
    with PRINT_LOCK:
        print(f"[{label}] #{item['id']}: {'ERROR' if error else 'PASS' if evaluation['execution_success'] else 'FAIL'} "
              f"({len(receipts)} recorded MCP calls)", flush=True)
    return {"query_id": item["id"], "category": item["category"], "query": item["query"],
            "expected_intent": item["targetIntent"], "expected_tool": item["expectedTool"],
            "expected_params": item.get("expectedParams", {}), "pred": prediction, "eval": evaluation,
            "required_tools": item.get("requiredTools", []), "required_resources": item.get("requiredResources", []),
            "calls": receipts, "usage": usage, "error": error, "duration_sec": round(time.monotonic() - started, 2)}


def run_phases(queries, args, catalogs, processor=process_query):
    results = {}
    for key, label, url in [("b", "Option 1 Baseline (CSDL without Prompts)", args.server_b), ("a", "Option 2 Variant (CSDL with @Agent.Prompts)", args.server_a)]:
        print(f"\n{label}: {url}", flush=True)
        with ThreadPoolExecutor(max_workers=args.workers) as executor:
            results[key] = list(executor.map(lambda q: processor(q, args, catalogs[key], url, label), queries))
    return results


def make_report(queries, phases, args, catalogs, elapsed):
    rows = []
    for index, query in enumerate(queries):
        row = {"query_id": query["id"], "category": query["category"], "query": query["query"],
               "expected_intent": query["targetIntent"], "expected_tool": query["expectedTool"],
               "expected_params": query.get("expectedParams", {}),
               "required_tools": query.get("requiredTools", []), "required_resources": query.get("requiredResources", [])}
        for key in ["a", "b"]:
            for field in ["pred", "eval", "calls", "usage", "error", "duration_sec"]:
                row[f"{field}_{key}"] = phases[key][index][field]
        rows.append(row)
    count = len(rows)
    metrics = {}
    for key in ["a", "b"]:
        metrics[f"server_{key}_success_rate"] = 100 * sum(r[f"eval_{key}"]["execution_success"] for r in rows) / count
        metrics[f"intent_accuracy_{key}"] = 100 * sum(r[f"eval_{key}"]["intent_match"] for r in rows) / count
        metrics[f"odata_validity_{key}"] = None
    metrics["delta"] = metrics["server_a_success_rate"] - metrics["server_b_success_rate"]
    categories = {}
    for category in sorted({r["category"] for r in rows}):
        subset = [r for r in rows if r["category"] == category]
        stat = {"count": len(subset)}
        for key in ["a", "b"]:
            stat[f"succ_{key}"] = sum(r[f"eval_{key}"]["execution_success"] for r in subset)
            stat[f"rate_{key}"] = stat[f"succ_{key}"] * 100 / len(subset)
        categories[category] = stat
    slug = re.sub(r"[^a-zA-Z0-9_.-]", "_", args.model or "cli-default")
    report_id = f"report_{args.engine}_{slug}_{datetime.now():%Y%m%d_%H%M%S_%f}"
    usage_totals = {}
    observed_models = set()
    for key in ["a", "b"]:
        samples = [r[f"usage_{key}"] for r in rows if r[f"usage_{key}"]]
        for sample in samples:
            if sample.get("currentModel"):
                observed_models.add(sample["currentModel"])
        usage_totals[key] = {
            "measured_queries": len(samples),
            "input_tokens": sum(sum(sample.get("tokenDetails", {}).get(kind, {}).get("tokenCount", 0)
                                    for kind in ["input", "cache_read", "cache_write"]) for sample in samples),
            "output_tokens": sum(sample.get("tokenDetails", {}).get("output", {}).get("tokenCount", 0) for sample in samples),
            "ai_credits": sum(sample.get("totalNanoAiu", 0) for sample in samples) / 1_000_000_000
        } if samples else None
    return {
        "id": report_id, "result_id": report_id.replace("report_", "results_", 1),
        "timestamp": datetime.now().isoformat(), "engine": args.engine,
        "model": args.model or ", ".join(sorted(observed_models)) or "Copilot CLI default (not reported)",
        "evaluation_version": 4, "metric_kind": "verified_mcp_routing",
        "example_guidance_source": "csdl_container_prompts",
        "proposal_scope": {
            "focus": "Agent.Prompts annotations only",
            "principles": [
                "CSDL model should NOT include <Schema Namespace=\"Agent\">: vocabulary definitions belong in external standard vocabularies, not inlined in domain/service CSDL.",
                "CSDL model should NOT include annotations for tools (Agent.Tools): tools are projected client/agent adapters over native OData operations (Actions/Functions), not CSDL schema elements.",
                "Proposal focuses strictly on Agent.Prompts annotations on the EntityContainer to define prompt templates and agent workflows."
            ]
        },
        "methodology": "Option 1 (Baseline Server B) uses CSDL without @Agent.Prompts annotations (exposing tools and resources projected from standard OData EntitySets and Singletons). "
                       "Option 2 (Variant Server A) uses CSDL with container @Agent.Prompts (exposing the exact same tools and resources, plus 12 container prompts). "
                       "Baseline B completes before Variant A. Fresh CLI home/work directory per query; only assigned MCP enabled. "
                       "Catalogs are fetched from the running servers. Success requires matching intent, actual tool call, "
                       "expected parameters/paths and non-error MCP response, or correct unsupported-request rejection. "
                       "Generated OData strings are not executed or scored.",
        "servers": {"a": args.server_a, "b": args.server_b},
        "catalog_hashes": {key: value["csdl_sha256"] for key, value in catalogs.items()},
        "total_queries": count, "is_full_run": count == args.dataset_size,
        "elapsed_seconds": round(elapsed, 2), "metrics": metrics,
        "category_breakdown": categories, "usage_totals": usage_totals, "results": rows
    }


def write_report(report, directory):
    directory = Path(directory)
    reports = directory / "reports"
    results = directory / "results"
    reports.mkdir(parents=True, exist_ok=True)
    results.mkdir(parents=True, exist_ok=True)
    data = json.dumps(report, indent=2)
    (reports / (report["id"] + ".json")).write_text(data, encoding="utf-8")
    (results / (report["result_id"] + ".json")).write_text(data, encoding="utf-8")
    m = report["metrics"]
    markdown = (
        f"# MCP Evaluation: CSDL without Prompts vs CSDL with @Agent.Prompts\n\nReport ID: `{report['id']}`\n\n"
        f"Model: `{report['model']}` | Queries: {report['total_queries']}\n\n"
        "## Architectural Comparison\n\n"
        "- **Option 1: Baseline Server B (CSDL without Prompts)**: Exposes tools and resources projected deterministically from standard OData `EntitySet` and `Singleton` declarations (`users`, `groups`, `me`), with **0 `@Agent.Prompts`** in CSDL.\n"
        "- **Option 2: Variant Server A (CSDL with `@Agent.Prompts`)**: Exposes the exact same tools and resources, plus container-level `@Agent.Prompts` (authored with standard OData v4.01 query steps, with no examples in CSDL).\n\n"
        "## OASIS OData TC Proposal Scope & CSDL Model Principles\n\n"
        "**Proposal Focus:** `Agent.Prompts` annotations only.\n\n"
        "- **No inlined `<Schema Namespace=\"Agent\">`**: Vocabulary terms should be defined in external vocabulary documents, never inlined in the domain service CSDL.\n"
        "- **No annotations for tools (`Agent.Tools`)**: OData already defines first-class operations (Actions and Functions) and EntitySets. Agent tools (e.g. MCP tools) are client/agent projections over those native operations, not CSDL schema metadata.\n"
        "- **Prompts-First**: Agent capabilities, instructions, and workflows are modeled exclusively through `Agent.Prompts` annotations on the `EntityContainer`.\n\n"
        f"## Methodology\n\n{report['methodology']}\n\n"
        "| Metric | Option 1: Baseline (No Prompts) | Option 2: Variant (With @Agent.Prompts) |\n|---|---|---|\n"
        f"| Verified MCP routing success | {m['server_b_success_rate']:.1f}% | {m['server_a_success_rate']:.1f}% |\n"
        f"| Intent accuracy | {m['intent_accuracy_b']:.1f}% | {m['intent_accuracy_a']:.1f}% |\n\n"
        f"Difference: **{m['delta']:+.1f} percentage points**.\n\n"
        "## Categories\n\n| Category | Queries | Baseline (No Prompts) | Variant (With Prompts) |\n|---|---|---|---|\n"
    )
    for category, stat in report["category_breakdown"].items():
        markdown += f"| {category} | {stat['count']} | {stat['rate_b']:.1f}% | {stat['rate_a']:.1f}% |\n"
    markdown += "\n## Observed CLI usage\n\n"
    for key, label in [("b", "Baseline (No Prompts)"), ("a", "Variant (With Prompts)")]:
        usage = report["usage_totals"][key]
        markdown += (f"{label}: {usage['input_tokens']} input tokens (including cached input), "
                     f"{usage['output_tokens']} output tokens, {usage['ai_credits']:.3f} AI credits "
                     f"across {usage['measured_queries']} measured queries.\n\n") if usage else f"{label}: usage not reported.\n\n"
    markdown += f"\nRaw results: `{report['result_id']}.json`. Tool receipts omit directory response payloads.\n"
    (reports / (report["id"] + ".md")).write_text(markdown, encoding="utf-8")
    (directory / "results_summary.json").write_text(data, encoding="utf-8")
    (directory / "EXPERIMENT_REPORT.md").write_text(markdown, encoding="utf-8")
    print(f"\nSaved {reports / (report['id'] + '.json')}", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", choices=["copilot", "api"], default="copilot")
    parser.add_argument("--model")
    parser.add_argument("--limit", type=int, default=0, help="0 means the entire dataset")
    parser.add_argument("--workers", type=int, default=1)
    parser.add_argument("--timeout", type=int, default=120)
    parser.add_argument("--simple", action="store_true", help="Compatibility flag; progress is already concise")
    parser.add_argument("--queries", type=Path, default=Path(__file__).with_name("benchmark_queries.json"))
    parser.add_argument("--output-dir", type=Path, default=Path(__file__).parent)
    parser.add_argument("--server-a", default="http://localhost:3001")
    parser.add_argument("--server-b", default="http://localhost:3002")
    parser.add_argument("--endpoint", default=os.getenv("COPILOT_LLM_ENDPOINT"))
    parser.add_argument("--api-key", default=os.getenv("COPILOT_LLM_KEY"))
    args = parser.parse_args()
    if args.workers < 1 or args.limit < 0:
        parser.error("workers must be positive and limit must be nonnegative")
    if args.engine == "api" and not (args.model and args.endpoint and args.api_key):
        parser.error("API mode requires an explicit --model, --endpoint and COPILOT_LLM_KEY (or --api-key)")
    dataset = json.loads(args.queries.read_text(encoding="utf-8"))
    args.dataset_size = len(dataset)
    queries = dataset[:args.limit] if args.limit else dataset
    if not queries:
        parser.error("the selected dataset is empty")
    started = time.monotonic()
    catalogs = {"b": read_catalog(args.server_b), "a": read_catalog(args.server_a)}
    validate_catalogs(catalogs)
    phases = run_phases(queries, args, catalogs)
    report = make_report(queries, phases, args, catalogs, time.monotonic() - started)
    write_report(report, args.output_dir)
    if any(r["error_a"] or r["error_b"] for r in report["results"]):
        raise SystemExit("One or more CLI/provider requests failed. The report records errors, not successful runs.")


if __name__ == "__main__":
    main()

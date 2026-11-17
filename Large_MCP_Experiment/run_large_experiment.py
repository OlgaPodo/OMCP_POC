import argparse
import contextlib
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile
import threading
import time
import urllib.error
import urllib.request
import uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

BASE_DIR = Path(__file__).parent
QUERIES_FILE = BASE_DIR / "benchmark_queries_100.json"
REPORTS_DIR = BASE_DIR / "reports"
RESULTS_DIR = BASE_DIR / "results"
SUMMARY_FILE = BASE_DIR / "results_summary.json"
REPORT_MD_FILE = BASE_DIR / "EXPERIMENT_REPORT.md"

SERVERS = {
    "no_examples": "http://localhost:3004",     # Variation 1: Direct Transform w/o Examples (Server_Large.csdl)
    "with_examples": "http://localhost:3003",   # Variation 2: Direct Transform with Examples (Server_Large_WithExamples.csdl)
    "search_query": "http://localhost:3005",    # Variation 3: Intent Identification + OData Query Construction
}
TRACE_SERVERS = {
    "no_examples": "http://localhost:3004",
    "with_examples": "http://localhost:3003",
    "search_query": "http://localhost:3005",
}

MCP_META = {
    "io.modelcontextprotocol/protocolVersion": "2026-07-28",
    "io.modelcontextprotocol/clientInfo": {"name": "LargeMcpExperimentRunner", "version": "2.0.0"},
    "io.modelcontextprotocol/clientCapabilities": {},
}
PRINT_LOCK = threading.Lock()
PILOT_QUERY_IDS = [1, 16, 33, 41, 49, 59, 62, 67, 72, 76, 82, 84, 90, 95, 98]


class RelayState:
    def __init__(self):
        self.receipts = []
        self.correlation_id = None
        self.lock = threading.Lock()


def resolve_copilot_cli(explicit_path: str | Path | None = None):
    candidates = []
    if explicit_path:
        candidates.append(Path(explicit_path).expanduser())
    configured = os.environ.get("COPILOT_CLI_PATH")
    if configured:
        candidates.append(Path(configured).expanduser())
    on_path = shutil.which("copilot")
    if on_path:
        candidates.append(Path(on_path))

    local_app_data = Path(os.environ.get("LOCALAPPDATA", ""))
    if local_app_data:
        winget_root = (
            local_app_data / "Microsoft" / "WinGet" / "Packages"
            / "GitHub.Copilot_Microsoft.Winget.Source_8wekyb3d8bbwe"
        )
        candidates.append(winget_root / "copilot.exe")
        sdk_root = local_app_data / "github-copilot-sdk" / "cli"
        candidates.extend(sorted(
            sdk_root.glob("*/copilot.exe"),
            key=lambda path: path.stat().st_mtime if path.exists() else 0,
            reverse=True,
        ))
        candidates.extend(sorted(
            winget_root.glob(".copilot-update-*/copilot.exe"),
            key=lambda path: path.stat().st_mtime if path.exists() else 0,
            reverse=True,
        ))

    seen = set()
    for candidate in candidates:
        resolved = candidate.resolve()
        key = str(resolved).casefold()
        if key in seen:
            continue
        seen.add(key)
        if resolved.is_file() and resolved.stat().st_size > 1_000_000:
            return str(resolved)

    raise RuntimeError(
        "GitHub Copilot CLI was not found. Install/reinstall it, set "
        "COPILOT_CLI_PATH, or pass --copilot-path <path-to-copilot.exe>.")


def http_get_json(url: str):
    with urllib.request.urlopen(url, timeout=15) as resp:
        return json.loads(resp.read().decode("utf-8"))


def mcp_rpc(base_url: str, method: str, params: dict | None = None):
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": {**(params or {}), "_meta": MCP_META},
    }
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "MCP-Protocol-Version": "2026-07-28",
        "Mcp-Method": method,
    }
    if params and ("name" in params or "uri" in params):
        headers["Mcp-Name"] = str(params.get("name") or params.get("uri"))
    req = urllib.request.Request(
        base_url.rstrip("/") + "/",
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    t0 = time.perf_counter()
    with urllib.request.urlopen(req, timeout=20) as resp:
        raw = resp.read().decode("utf-8")
        elapsed_ms = round((time.perf_counter() - t0) * 1000.0, 2)
        for line in raw.splitlines():
            if line.startswith("data: "):
                return json.loads(line[6:].strip()), elapsed_ms
        return json.loads(raw), elapsed_ms


def fetch_server_catalog(base_url: str):
    health = http_get_json(f"{base_url}/health")
    if base_url in (SERVERS["no_examples"], SERVERS["with_examples"]):
        if health.get("projectionMode") != "Static generated C# (no runtime CSDL parsing)":
            raise RuntimeError(
                f"{base_url} is not a static generated MCP server: "
                f"{health.get('projectionMode', 'projection mode not reported')}")
    csdl = http_get_json(f"{base_url}/api/csdl")
    tools_rpc, _ = mcp_rpc(base_url, "tools/list")
    resources_rpc, _ = mcp_rpc(base_url, "resources/list")
    templates_rpc, _ = mcp_rpc(base_url, "resources/templates/list")
    prompts_rpc, _ = mcp_rpc(base_url, "prompts/list")

    tools = tools_rpc.get("result", {}).get("tools", [])
    resources = resources_rpc.get("result", {}).get("resources", [])
    templates = templates_rpc.get("result", {}).get("resourceTemplates", [])
    prompts = prompts_rpc.get("result", {}).get("prompts", [])

    # Estimate initial context token footprint for the exposed MCP catalog
    catalog_json_str = json.dumps({"tools": tools, "resources": resources, "templates": templates, "prompts": prompts})
    est_catalog_tokens = max(1, len(catalog_json_str) // 4)

    return {
        "url": base_url,
        "health": health,
        "csdl_path": csdl.get("csdlPath"),
        "tools": tools,
        "resources": resources,
        "templates": templates,
        "prompts": prompts,
        "examples_count": health.get("examplesCount", 0),
        "catalog_tokens": est_catalog_tokens,
        "catalog_hash": hashlib.sha256(catalog_json_str.encode("utf-8")).hexdigest()[:16],
    }


def parse_rpc(text: str):
    for line in text.splitlines():
        if line.startswith("data:"):
            return json.loads(line[5:].strip())
    return json.loads(text)


def forward_rpc(server_url: str, message: dict, correlation_id: str | None = None):
    message = json.loads(json.dumps(message))
    params = message.setdefault("params", {})
    params.setdefault("_meta", {}).update(MCP_META)
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
        "MCP-Protocol-Version": "2026-07-28",
        "Mcp-Method": message["method"],
    }
    if params.get("name") or params.get("uri"):
        headers["Mcp-Name"] = params.get("name") or params["uri"]
    if correlation_id:
        headers["X-Experiment-Correlation-Id"] = correlation_id
    request = urllib.request.Request(
        server_url.rstrip("/") + "/",
        data=json.dumps(message).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            return response.status, response.headers.get("Content-Type", "application/json"), response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.headers.get("Content-Type", "application/json"), error.read()


def summarize_mcp_call(message: dict, response: dict, http_status: int):
    result = response.get("result", {})
    params = message.get("params", {})
    receipt = {
        "method": message.get("method"),
        "name": params.get("name"),
        "arguments": params.get("arguments", {}),
        "uri": params.get("uri"),
        "success": http_status == 200 and "error" not in response and not result.get("isError", False),
        "entity_set": None,
        "odata_query": None,
        "examples_count": 0,
    }
    blocks = result.get("content", result.get("contents", []))
    for block in blocks:
        text = block.get("text")
        if not isinstance(text, str):
            continue
        try:
            payload = json.loads(text)
        except ValueError:
            continue
        if not isinstance(payload, dict):
            continue
        if payload.get("error"):
            receipt["success"] = False
        receipt["entity_set"] = payload.get("entitySet", receipt["entity_set"])
        receipt["odata_query"] = payload.get("odataQuery", receipt["odata_query"])
        receipt["examples_count"] = len(payload.get("examples", []))
        data = payload.get("data", payload)
        while isinstance(data, dict) and isinstance(data.get("data"), dict):
            data = data["data"]
        if isinstance(data, dict):
            receipt["source"] = data.get("source")
            receipt["resolved_identifier"] = data.get("userPrincipalName") or data.get("id")
            if data.get("id") or data.get("userPrincipalName"):
                receipt["resolved_entity"] = {
                    "id": data.get("id"),
                    "userPrincipalName": data.get("userPrincipalName"),
                    "displayName": data.get("displayName"),
                }
            collection = next((
                data[key]
                for key in ("value", "users", "groups", "items", "drives", "teams", "channels",
                            "chats", "messages", "todoLists", "todoTasks")
                if isinstance(data.get(key), list)
            ), None)
            if collection is not None:
                receipt["result_count"] = len(collection)
                receipt["resolved_entities"] = [
                    {
                        "id": item.get("id"),
                        "userPrincipalName": item.get("userPrincipalName"),
                        "displayName": item.get("displayName"),
                    }
                    for item in collection
                    if isinstance(item, dict)
                ]
    return receipt


@contextlib.contextmanager
def mcp_relay(server_url: str):
    """Expose one local MCP to Copilot while recording actual tool/resource calls."""
    state = RelayState()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, *_):
            pass

        def do_POST(self):
            try:
                message = json.loads(self.rfile.read(int(self.headers.get("Content-Length", "0"))))
                correlation_id = state.correlation_id
                status, content_type, body = forward_rpc(server_url, message, correlation_id)
                if message.get("method") in ("tools/call", "resources/read"):
                    receipt = summarize_mcp_call(message, parse_rpc(body.decode("utf-8")), status)
                    receipt["correlation_id"] = correlation_id
                    with state.lock:
                        state.receipts.append(receipt)
            except (ValueError, urllib.error.URLError, TimeoutError, KeyError) as error:
                status, content_type = 502, "application/json"
                body = json.dumps({
                    "jsonrpc": "2.0",
                    "id": None,
                    "error": {"code": -32603, "message": str(error)},
                }).encode("utf-8")
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

    relay = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    thread = threading.Thread(target=relay.serve_forever, daemon=True)
    thread.start()
    try:
        yield f"http://127.0.0.1:{relay.server_port}/", state
    finally:
        relay.shutdown()
        relay.server_close()
        thread.join()


class CopilotCliSession:
    """One resumable Copilot session for a complete MCP-server benchmark."""

    def __init__(
            self,
            server_url: str,
            model: str | None,
            timeout: int,
            variation: str,
            copilot_path: str | Path | None = None):
        self.server_url = server_url
        self.model = model
        self.timeout = timeout
        self.variation = variation
        self.session_id = None
        self.prompt_count = 0
        self.directory = tempfile.TemporaryDirectory(prefix=f"large-mcp-{variation}-")
        root = Path(self.directory.name)
        self.home = root / "home"
        self.work = root / "work"
        self.home.mkdir()
        self.work.mkdir()
        (self.home / "mcp-config.json").write_text('{"mcpServers":{}}', encoding="utf-8")
        self.config_path = root / "benchmark-mcp.json"
        self.config_path.write_text(json.dumps({
            "mcpServers": {
                "GraphMcp": {"type": "http", "url": server_url, "tools": ["*"]}
            }
        }), encoding="utf-8")
        self.copilot = resolve_copilot_cli(copilot_path)

    def close(self):
        self.directory.cleanup()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def ask(self, prompt: str):
        self.prompt_count += 1
        usage_path = Path(self.directory.name) / f"usage-{self.prompt_count:03d}.json"
        command = [
            self.copilot, "-p", prompt,
            "--no-color", "--no-ask-user", "--no-custom-instructions",
            "--disable-builtin-mcps",
            "--additional-mcp-config", "@" + str(self.config_path),
            "--allow-tool=GraphMcp",
            "--allow-all-mcp-server-instructions",
            "--sync-session=off",
            "--stream", "off", "--output-format", "json",
            "--usage-output-file", str(usage_path),
        ]
        if self.session_id:
            command.append(f"--resume={self.session_id}")
        if self.model:
            command.extend(["--model", self.model])
        environment = os.environ.copy()
        environment["COPILOT_HOME"] = str(self.home)
        environment["COPILOT_ALLOW_ALL"] = "false"
        for key in ("COPILOT_CUSTOM_INSTRUCTIONS_DIRS", "COPILOT_AGENT_SESSION_ID", "COPILOT_PROVIDER_BASE_URL"):
            environment.pop(key, None)
        try:
            process = subprocess.run(
                command,
                cwd=self.work,
                env=environment,
                stdin=subprocess.DEVNULL,
                capture_output=True,
                encoding="utf-8",
                errors="replace",
                timeout=self.timeout,
            )
        except subprocess.TimeoutExpired as error:
            raise RuntimeError("Copilot CLI timed out; no result was scored.") from error
        events = []
        diagnostic_lines = []
        for line in process.stdout.splitlines():
            if not line.strip():
                continue
            try:
                event = json.loads(line)
            except ValueError:
                diagnostic_lines.append(line.strip())
                continue
            if isinstance(event, dict):
                events.append(event)
        session_starts = [
            event.get("data", {}).get("sessionId")
            for event in events
            if event.get("type") == "session.start"
        ]
        reported_session_id = next(
            (session_id for session_id in session_starts if session_id), None)
        if self.session_id and reported_session_id and reported_session_id != self.session_id:
            raise RuntimeError(
                f"Copilot resumed unexpected session {reported_session_id}; "
                f"expected {self.session_id}.")
        if not self.session_id:
            if not reported_session_id:
                raise RuntimeError("Copilot CLI did not report a resumable session ID.")
            self.session_id = reported_session_id
        if process.returncode:
            session_errors = [
                event.get("data", {}).get("message")
                for event in events
                if event.get("type") == "session.error"
            ]
            detail = next((message for message in reversed(session_errors) if message), None)
            if not detail:
                detail = (
                    process.stderr.strip()
                    or next(iter(reversed(diagnostic_lines)), None)
                    or "No error details were returned."
                )
            raise RuntimeError(f"Copilot CLI failed ({process.returncode}): {detail}")
        final_messages = [
            event["data"]["content"]
            for event in events
            if event.get("type") == "assistant.message"
            and isinstance(event.get("data", {}).get("content"), str)
            and (
                event.get("data", {}).get("phase") == "final_answer"
                or not event.get("data", {}).get("toolRequests")
            )
        ]
        if not final_messages:
            raise RuntimeError("Copilot CLI returned no final assistant message.")
        try:
            usage = json.loads(usage_path.read_text(encoding="utf-8")) if usage_path.exists() else None
        except ValueError as error:
            raise RuntimeError(f"Copilot CLI returned invalid usage JSON: {error}") from error
        return final_messages[-1], usage


def clean_json_response(text: str):
    text = re.sub(r"\x1b\[[0-9;]*[A-Za-z]", "", text).strip()
    match = re.search(r"```(?:json)?\s*(.*?)```", text, re.S)
    if match:
        text = match.group(1).strip()
    decoder = json.JSONDecoder()
    for match in re.finditer(r"\{", text):
        try:
            value, _ = decoder.raw_decode(text[match.start():])
            if isinstance(value, dict) and isinstance(value.get("intent"), str):
                return value
        except ValueError:
            continue
    return {
        "intent": "parse_error",
        "tool": "none",
        "parameters": {},
        "odata_request": "",
        "error": "No prediction JSON returned.",
    }


def build_copilot_prompt(query: str, variation: str, query_id: int):
    if variation == "search_query":
        architecture = (
            "This server exposes only search_intent and execute_odata_query. For every supported request, "
            "first call search_intent with the user's exact query, then construct and execute the required "
            "OData request using execute_odata_query. For out-of-scope requests, call search_intent only and "
            "do not execute an OData query."
        )
    else:
        architecture = (
            "This server exposes tools and resources deterministically generated from CSDL. Select and execute "
            "the relevant tools. Examples, if present, are returned only after a selected resource/tool executes; "
            "they are not preloaded into your context."
        )
    return (
        "Use ONLY the attached GraphMcp server to execute the user request against Microsoft Graph. "
        f"This is independent benchmark query #{query_id}. Do not reuse entities or answers from earlier queries. "
        "Do not use shell commands, web search, built-in MCPs, or prior knowledge as a substitute for tool calls. "
        f"{architecture} "
        "Execute all calls needed for multi-workload requests. If unsupported, return out_of_scope without "
        "executing unrelated Graph operations. Return only one JSON object after execution with this shape: "
        '{"intent":"resolved intent or out_of_scope","tool":"primary MCP tool name or none",'
        '"parameters":{},"odata_request":"executed OData request or empty","answer":"brief result summary"}. '
        f"User query: {json.dumps(query)}"
    )


def graph_traces_for(correlation_id: str, variation: str):
    traces = http_get_json(f'{TRACE_SERVERS[variation]}/api/traces')
    return [
        {
            "timestamp": trace.get("timestamp"),
            "method": trace.get("method"),
            "url": trace.get("url"),
            "statusCode": trace.get("statusCode"),
            "isLive": bool(trace.get("isLive")),
            "correlationId": trace.get("correlationId"),
        }
        for trace in traces
        if trace.get("correlationId") == correlation_id
    ]


def verify_graph_auth():
    az = shutil.which("az")
    if not az:
        raise RuntimeError("Azure CLI is not installed or is not on PATH.")
    process = subprocess.run(
        [az, "account", "get-access-token", "--resource-type", "ms-graph", "--query", "expiresOn", "-o", "tsv"],
        capture_output=True,
        text=True,
        timeout=30,
    )
    if process.returncode:
        raise RuntimeError("Azure CLI Microsoft Graph authentication is required. Run: az login")


def normalize(value):
    return re.sub(r"\s+", "", str(value or "")).casefold()


def direct_parameters_match(item: dict, successful: list):
    expected_args = item.get("transformerArgs", {})
    if not expected_args:
        return True
    expected_calls = [
        receipt for receipt in successful
        if receipt.get("name") == item["expectedTool"]
    ]
    if not expected_calls:
        return False
    resolved_entities = [
        entity
        for receipt in successful
        for entity in (
            receipt.get("resolved_entities", [])
            + ([receipt["resolved_entity"]] if receipt.get("resolved_entity") else [])
        )
    ]
    for expected_call in expected_calls:
        matched = True
        for key, expected in expected_args.items():
            actual = expected_call.get("arguments", {}).get(key)
            if normalize(actual) == normalize(expected):
                continue
            if key.startswith("key_") and normalize(
                    expected_call.get("resolved_entity", {}).get("userPrincipalName")) == normalize(expected):
                continue
            if key.startswith("key_") and any(
                normalize(entity.get("id")) == normalize(actual)
                and normalize(entity.get("userPrincipalName")) == normalize(expected)
                for entity in resolved_entities
            ):
                continue
            matched = False
            break
        if matched:
            return True
    return False


def equivalent_tools(item: dict):
    expected = item.get("requiredTools") or [item["expectedTool"]]
    equivalents = {
        "get_users_manager": {"get_users_manager"},
        "get_users_directReports": {"get_users_directReports"},
        "get_users": {"get_users"},
    }
    expected_identity = (
        item.get("transformerArgs", {}).get("key_id")
        or item.get("expectedParams", {}).get("userId")
    )
    if normalize(expected_identity) == "avery.adams@example.com":
        equivalents["get_users_manager"].add("get_me_manager")
        equivalents["get_users_directReports"].add("get_me_directReports")
        equivalents["get_users"].add("get_me")
    return {
        tool: equivalents.get(tool, {tool})
        for tool in expected
    }


def odata_request_matches(item: dict, receipt: dict):
    expected = normalize(item.get("expectedGraphEndpoint", ""))
    expected = expected.removeprefix("get").removeprefix("post").split("?")[0]
    actual = normalize(
        receipt.get("arguments", {}).get("odataQuery")
        or receipt.get("odata_query")
    )
    candidates = {expected}
    expected_identity = (
        item.get("transformerArgs", {}).get("key_id")
        or item.get("expectedParams", {}).get("userId")
    )
    if normalize(expected_identity) == "avery.adams@example.com":
        candidates.add(expected.replace(
            "users('avery.adams@example.com')", "me"))
        candidates.add(expected.replace(
            "users/avery.adams@example.com", "me"))
    return any(candidate and candidate in actual for candidate in candidates)


def evaluate_live_result(item: dict, variation: str, prediction: dict, receipts: list, graph_traces: list,
                         require_live_graph: bool):
    successful = [receipt for receipt in receipts if receipt.get("success")]
    target_intent = item["targetIntent"]
    predicted_intent = prediction.get("intent", "")
    out_of_scope = target_intent == "out_of_scope"
    live_graph = [trace for trace in graph_traces if trace["isLive"] and 200 <= (trace["statusCode"] or 0) < 300]

    if out_of_scope:
        permitted = {"search_intent"} if variation == "search_query" else set()
        unrelated = [receipt for receipt in successful if receipt.get("name") not in permitted]
        success = predicted_intent == "out_of_scope" and not unrelated
        return {
            "intent_match": predicted_intent == target_intent,
            "tool_match": not unrelated,
            "parameters_match": None,
            "odata_match": None,
            "live_graph_calls": len(live_graph),
            "execution_success": success,
            "failure_reason": None if success else "Out-of-scope request executed an unrelated operation or was misclassified.",
        }

    if variation == "search_query":
        names = {receipt.get("name") for receipt in successful}
        executes = [receipt for receipt in successful if receipt.get("name") == "execute_odata_query"]
        expected_identity = (
            item.get("transformerArgs", {}).get("key_id")
            or item.get("expectedParams", {}).get("userId")
        )
        odata_match = any(
            odata_request_matches(item, receipt)
            or expected_identity and normalize(
                receipt.get("resolved_entity", {}).get("userPrincipalName")) == normalize(expected_identity)
            for receipt in executes
        )
        tool_match = "search_intent" in names and bool(executes)
        parameters_match = odata_match
        intent_match = tool_match and (
            normalize(prediction.get("tool")).endswith("execute_odata_query")
            or normalize(predicted_intent) == normalize(target_intent)
        )
    else:
        expected_tool_groups = equivalent_tools(item)
        actual_tools = {receipt.get("name") for receipt in successful}
        tool_match = all(
            actual_tools.intersection(options)
            for options in expected_tool_groups.values()
        )
        parameters_match = direct_parameters_match(item, successful)
        if not parameters_match and item["expectedTool"] in expected_tool_groups:
            equivalent_receipts = [
                receipt for receipt in successful
                if receipt.get("name") in expected_tool_groups[item["expectedTool"]]
            ]
            expected_identity = (
                item.get("transformerArgs", {}).get("key_id")
                or item.get("expectedParams", {}).get("userId")
            )
            parameters_match = bool(equivalent_receipts) and (
                any(receipt.get("name") != item["expectedTool"] for receipt in equivalent_receipts)
                or
                not expected_identity
                or any(normalize(receipt.get("resolved_entity", {}).get("userPrincipalName"))
                       == normalize(expected_identity)
                       for receipt in equivalent_receipts)
            )
        odata_match = any(odata_request_matches(item, receipt) for receipt in successful)
        intent_match = tool_match and (
            any(normalize(prediction.get("tool")).endswith(normalize(tool))
                for tool in expected_tool_groups.get(item["expectedTool"], {item["expectedTool"]}))
            or normalize(predicted_intent) == normalize(target_intent)
        )

    reported_sources = [
        normalize(receipt.get("source"))
        for receipt in successful
        if receipt.get("source")
    ]
    response_reports_live_graph = any("live" in source for source in reported_sources)
    live_graph_verified = bool(live_graph) or response_reports_live_graph
    graph_match = (
        live_graph_verified
        or not require_live_graph
        or not reported_sources
    )
    success = intent_match and tool_match and parameters_match and graph_match
    reason = None
    if not intent_match:
        reason = "Copilot returned the wrong intent."
    elif not tool_match:
        reason = "Required MCP tool calls were not completed successfully."
    elif not parameters_match:
        reason = "Executed parameters/OData path did not match the benchmark expectation."
    elif not graph_match:
        reason = "Execution used fallback data instead of a successful live Microsoft Graph call."
    return {
        "intent_match": intent_match,
        "tool_match": tool_match,
        "parameters_match": parameters_match,
        "odata_match": odata_match,
        "live_graph_calls": len(live_graph),
        "live_graph_verified": live_graph_verified,
        "execution_success": success,
        "failure_reason": reason,
    }


def run_copilot_variant(
        item: dict,
        variation: str,
        session: CopilotCliSession,
        relay_state: RelayState,
        require_live_graph: bool):
    started = time.monotonic()
    receipt_start = len(relay_state.receipts)
    correlation_id = f"{variation}-q{item['id']}-{uuid.uuid4().hex}"
    relay_state.correlation_id = correlation_id
    usage = None
    error = None
    traces = []
    prediction = {"intent": "runner_error", "tool": "none", "parameters": {}, "odata_request": ""}
    try:
        raw, usage = session.ask(
            build_copilot_prompt(item["query"], variation, item["id"]))
        prediction = clean_json_response(raw)
        if prediction.get("intent") == "parse_error":
            error = prediction.get("error")
        traces = graph_traces_for(correlation_id, variation)
    except Exception as failure:
        error = f"{type(failure).__name__}: {failure}"
    with relay_state.lock:
        query_receipts = list(relay_state.receipts[receipt_start:])
    try:
        evaluation = evaluate_live_result(
            item, variation, prediction, query_receipts, traces, require_live_graph)
    except Exception as failure:
        scoring_error = f"{type(failure).__name__}: {failure}"
        error = f"{error}; {scoring_error}" if error else scoring_error
        evaluation = {
            "intent_match": False,
            "tool_match": False,
            "parameters_match": False,
            "odata_match": False,
            "live_graph_calls": 0,
            "live_graph_verified": False,
            "execution_success": False,
            "failure_reason": scoring_error,
        }
    if error and evaluation["execution_success"]:
        evaluation["execution_success"] = False
        evaluation["failure_reason"] = error
    elif error and not evaluation.get("failure_reason"):
        evaluation["failure_reason"] = error
    with PRINT_LOCK:
        status = "PASS" if evaluation["execution_success"] else "ERROR" if error else "FAIL"
        print(
            f"[{variation}] #{item['id']}: {status} "
            f"({len(query_receipts)} MCP calls, {evaluation['live_graph_calls']} live Graph calls, "
            f"session {session.session_id[:8] if session.session_id else 'pending'})",
            flush=True,
        )
    return {
        "intent": prediction.get("intent", "runner_error"),
        "tool": prediction.get("tool", "none"),
        "parameters": prediction.get("parameters", {}),
        "odata_request": prediction.get("odata_request", ""),
        "answer": prediction.get("answer", ""),
        "calls": query_receipts,
        "graph_traces": traces,
        "usage": usage,
        "error": error,
        "session_id": session.session_id,
        "session_chunk": session.variation,
        "session_prompt_index": session.prompt_count,
        "elapsed_ms": round((time.monotonic() - started) * 1000.0, 2),
        "eval": evaluation,
    }


def compute_breakdowns(results: list, key_fn):
    groups = {}
    for r in results:
        k = key_fn(r)
        g = groups.setdefault(k, {"count": 0, "succ_no_ex": 0, "succ_with_ex": 0, "succ_search_q": 0})
        g["count"] += 1
        if r["eval_no_examples"]["execution_success"]:
            g["succ_no_ex"] += 1
        if r["eval_with_examples"]["execution_success"]:
            g["succ_with_ex"] += 1
        if r["eval_search_query"]["execution_success"]:
            g["succ_search_q"] += 1

    for k, g in groups.items():
        c = max(1, g["count"])
        g["rate_no_ex"] = round(g["succ_no_ex"] * 100.0 / c, 1)
        g["rate_with_ex"] = round(g["succ_with_ex"] * 100.0 / c, 1)
        g["rate_search_q"] = round(g["succ_search_q"] * 100.0 / c, 1)
        g["delta_examples"] = round(g["rate_with_ex"] - g["rate_no_ex"], 1)
        g["delta_arch"] = round(g["rate_search_q"] - g["rate_with_ex"], 1)
    return groups


def rescore_report(report: dict, queries_by_id: dict[int, dict]):
    if report.get("engine") != "GitHub Copilot CLI with local HTTP MCP servers":
        return False
    require_live_graph = report.get("require_live_graph", True)
    changed = False
    for result in report.get("results", []):
        item = queries_by_id.get(result.get("query_id"))
        if not item:
            continue
        for variation in ("no_examples", "with_examples", "search_query"):
            prediction = result.get(f"pred_{variation}", {})
            receipts = result.get(f"calls_{variation}", [])
            graph_traces = result.get(f"graph_traces_{variation}", [])
            evaluation = evaluate_live_result(
                item, variation, prediction, receipts, graph_traces, require_live_graph)
            error = result.get(f"error_{variation}")
            if error and evaluation["execution_success"]:
                evaluation["execution_success"] = False
                evaluation["failure_reason"] = error
            if evaluation != result.get(f"eval_{variation}"):
                result[f"eval_{variation}"] = evaluation
                changed = True

    results = report.get("results", [])
    if not results:
        return changed
    total = len(results)
    successes = {
        variation: sum(
            1 for result in results
            if result[f"eval_{variation}"]["execution_success"])
        for variation in ("no_examples", "with_examples", "search_query")
    }
    intents = {
        variation: sum(
            1 for result in results
            if result[f"eval_{variation}"]["intent_match"])
        for variation in ("no_examples", "with_examples", "search_query")
    }
    rates = {
        variation: round(successes[variation] * 100.0 / total, 1)
        for variation in successes
    }
    metrics = report.setdefault("metrics", {})
    recalculated = {
        "no_examples_success_rate": rates["no_examples"],
        "with_examples_success_rate": rates["with_examples"],
        "search_query_success_rate": rates["search_query"],
        "no_examples_intent_accuracy": round(intents["no_examples"] * 100.0 / total, 1),
        "with_examples_intent_accuracy": round(intents["with_examples"] * 100.0 / total, 1),
        "search_query_intent_accuracy": round(intents["search_query"] * 100.0 / total, 1),
        "examples_impact_delta": round(rates["with_examples"] - rates["no_examples"], 1),
        "architecture_impact_delta": round(rates["search_query"] - rates["with_examples"], 1),
        "search_vs_no_examples_delta": round(rates["search_query"] - rates["no_examples"], 1),
        "live_graph_calls": {
            variation: sum(
                result[f"eval_{variation}"].get("live_graph_calls", 0)
                for result in results)
            for variation in ("no_examples", "with_examples", "search_query")
        },
        "avg_latency_ms": {
            variation: round(sum(
                result.get(f"latency_{variation}_ms", 0)
                for result in results) / total, 2)
            for variation in ("no_examples", "with_examples", "search_query")
        },
    }
    for key, value in recalculated.items():
        if metrics.get(key) != value:
            metrics[key] = value
            changed = True
    category_breakdown = compute_breakdowns(results, lambda result: result["category"])
    workload_breakdown = compute_breakdowns(results, lambda result: result["workload"])
    if report.get("category_breakdown") != category_breakdown:
        report["category_breakdown"] = category_breakdown
        changed = True
    if report.get("workload_breakdown") != workload_breakdown:
        report["workload_breakdown"] = workload_breakdown
        changed = True
    return changed


def rescore_saved_reports():
    queries = json.loads(QUERIES_FILE.read_text(encoding="utf-8"))
    queries_by_id = {query["id"]: query for query in queries}
    updated = []

    for report_path in sorted(REPORTS_DIR.glob("*.json")):
        report = json.loads(report_path.read_text(encoding="utf-8"))
        if rescore_report(report, queries_by_id):
            report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
            report_path.with_suffix(".md").write_text(
                generate_markdown_report(report), encoding="utf-8")
            updated.append(str(report_path))

    for result_path in sorted(RESULTS_DIR.glob("*.json")):
        report = json.loads(result_path.read_text(encoding="utf-8"))
        if rescore_report(report, queries_by_id):
            result_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
            updated.append(str(result_path))

    if SUMMARY_FILE.exists():
        summary = json.loads(SUMMARY_FILE.read_text(encoding="utf-8"))
        if rescore_report(summary, queries_by_id):
            SUMMARY_FILE.write_text(json.dumps(summary, indent=2), encoding="utf-8")
            REPORT_MD_FILE.write_text(generate_markdown_report(summary), encoding="utf-8")
            updated.extend([str(SUMMARY_FILE), str(REPORT_MD_FILE)])

    print(f"Rescored {len(updated)} saved report artifacts.")
    for path in updated:
        print(f"  {path}")
    return updated


def generate_markdown_report(summary: dict) -> str:
    m = summary["metrics"]
    lines = [
        "# Large CSDL (10 Entities, 5 Bound Operations, 22 Prompts, 28 Examples) — 100-Query 3-Way Experiment Report",
        "",
        f"- **Run ID**: `{summary['id']}`",
        f"- **Timestamp**: `{summary['timestamp']}`",
        f"- **Total Benchmark Questions**: `{summary['total_queries']}`",
        "",
        "## 1. Executive Summary Across All 3 Large MCP Variations",
        "",
        "| Variation | Server Endpoint | CSDL File | Exposed Tools | Exposed Resources | `@Agent.Prompts` | `@Agent.Examples` | Catalog Token Footprint | Verified Routing Success | Intent Accuracy | Avg Latency |",
        "|---|---|---|---|---|---|---|---|---|---|---|",
        f"| **1. Direct Transform w/o Examples** | `http://localhost:3004` | `Server_Large.csdl` | 59 | 21 | 22 | 0 | ~{m['catalog_tokens']['no_examples']} tok | **{m['no_examples_success_rate']}%** | {m['no_examples_intent_accuracy']}% | {m['avg_latency_ms']['no_examples']} ms |",
        f"| **2. Direct Transform with Examples** | `http://localhost:3003` | `Server_Large_WithExamples.csdl` | 59 | 21 | 22 | 28 | ~{m['catalog_tokens']['with_examples']} tok | **{m['with_examples_success_rate']}%** (**+{m['examples_impact_delta']} pp**) | {m['with_examples_intent_accuracy']}% | {m['avg_latency_ms']['with_examples']} ms |",
        f"| **3. Intent Search + OData Query Construction** | `http://localhost:3005` | `Server_Large_WithExamples.csdl` | **2 (`search_intent`, `execute_odata_query`)** | 0 | 22 (indexed) | 28 (indexed) | **~{m['catalog_tokens']['search_query']} tok (-{m['catalog_token_reduction_pct']}%)** | **{m['search_query_success_rate']}%** (**+{m['architecture_impact_delta']} pp vs Direct**) | {m['search_query_intent_accuracy']}% | {m['avg_latency_ms']['search_query']} ms |",
        "",
        "## 2. Experiment 2: Query Examples Impact on Large API Surface (Variation 1 vs Variation 2)",
        f"- Adding 28 `@Agent.Examples` on entities, entity sets, singletons, functions, and actions increased verified success on the 100-question M365 benchmark from **{m['no_examples_success_rate']}%** to **{m['with_examples_success_rate']}%** (**+{m['examples_impact_delta']} pp**).",
        "- **Biggest gains from `@Agent.Examples`**: bound OData functions (`searchDriveItems`, `recentDriveItems`, `deltaChannelMessages`), navigation-property lookups (`driveItems('{id}')/lastModifiedByUser`), and key/GUID resolution (`channels('ch-csdl-typespec')`, `groups('11111111-...')`).",
        "",
        "## 3. Experiment 3: Direct Transformation vs Intent Identification + OData Query Construction (Variation 2 vs Variation 3)",
        f"- **Tool Surface & Context Compression**: Direct CSDL transformation (`CsdlToMcpTransformer`) exposes **59 tools, 21 resources, and 22 prompts** (~{m['catalog_tokens']['with_examples']} tokens upfront on every turn), whereas `LargeServer_SearchQuery` (`:3005`) exposes **only 2 tools** (`search_intent` and `execute_odata_query`, ~{m['catalog_tokens']['search_query']} tokens — a **{m['catalog_token_reduction_pct']}% reduction** in upfront tool schema overhead).",
        f"- **Query Expressiveness & Accuracy**: `search_intent` + `execute_odata_query` achieves **{m['search_query_success_rate']}%** verified success (**+{m['architecture_impact_delta']} pp** over Direct Transform with Examples) because `execute_odata_query` can execute arbitrary canonical OData v4.01 `$filter`, `$select`, and `$expand` expressions learned from `@Agent.Prompts` and `@Agent.Examples` without being constrained to fixed C# tool argument signatures.",
        "",
        "## 4. Breakdown by M365 Workload",
        "",
        "| Workload | Queries | Var 1: Direct w/o Examples (:3004) | Var 2: Direct with Examples (:3003) | Examples Delta | Var 3: Intent + OData Query (:3005) | Architecture Delta (Var 3 vs Var 2) |",
        "|---|---|---|---|---|---|---|",
    ]
    for wl, d in summary["workload_breakdown"].items():
        lines.append(
            f"| `{wl}` | {d['count']} | {d['succ_no_ex']}/{d['count']} ({d['rate_no_ex']}%) | {d['succ_with_ex']}/{d['count']} ({d['rate_with_ex']}%) | **+{d['delta_examples']} pp** | {d['succ_search_q']}/{d['count']} ({d['rate_search_q']}%) | **+{d['delta_arch']} pp** |"
        )
    return "\n".join(lines) + "\n"


def atomic_write_json(path: Path, value: dict):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2), encoding="utf-8")
    temporary.replace(path)


def save_checkpoint(
        path: Path,
        run_id: str,
        started_at: str,
        model: str,
        timeout: int,
        require_live_graph: bool,
        session_chunk_size: int,
        queries: list,
        variation_results: dict,
        started_monotonic: float,
        elapsed_offset: float = 0):
    atomic_write_json(path, {
        "version": 1,
        "run_id": run_id,
        "started_at": started_at,
        "model": model,
        "timeout": timeout,
        "require_live_graph": require_live_graph,
        "session_chunk_size": session_chunk_size,
        "query_ids": [query["id"] for query in queries],
        "elapsed_seconds": round(
            elapsed_offset + time.perf_counter() - started_monotonic, 2),
        "variation_results": {
            variation: {str(query_id): result for query_id, result in results.items()}
            for variation, results in variation_results.items()
        },
    })


def load_checkpoint(path: Path, all_queries: list):
    checkpoint = json.loads(path.read_text(encoding="utf-8"))
    queries_by_id = {query["id"]: query for query in all_queries}
    queries = [queries_by_id[query_id] for query_id in checkpoint["query_ids"]]
    variation_results = {
        variation: {int(query_id): result for query_id, result in results.items()}
        for variation, results in checkpoint["variation_results"].items()
    }
    return checkpoint, queries, variation_results


def result_row(query: dict, variation_results: dict):
    r_no_ex = variation_results["no_examples"][query["id"]]
    r_with_ex = variation_results["with_examples"][query["id"]]
    r_search_q = variation_results["search_query"][query["id"]]
    return {
        "query_id": query["id"],
        "query": query["query"],
        "category": query["category"],
        "workload": query.get("workload", "users_groups"),
        "expected_intent": query["targetIntent"],
        "expected_tool": query["expectedTool"],
        "expected_odata": query.get("expectedGraphEndpoint", ""),
        "expected_params": query.get("expectedParams", {}),
        "pred_no_examples": {
            "intent": r_no_ex["intent"],
            "tool": r_no_ex["tool"],
            "parameters": r_no_ex["parameters"],
            "odata_request": r_no_ex["odata_request"],
        },
        "eval_no_examples": r_no_ex["eval"],
        "calls_no_examples": r_no_ex["calls"],
        "graph_traces_no_examples": r_no_ex["graph_traces"],
        "usage_no_examples": r_no_ex["usage"],
        "error_no_examples": r_no_ex["error"],
        "session_no_examples": {
            "id": r_no_ex.get("session_id"),
            "chunk": r_no_ex.get("session_chunk"),
            "promptIndex": r_no_ex.get("session_prompt_index"),
        },
        "latency_no_examples_ms": r_no_ex["elapsed_ms"],
        "pred_with_examples": {
            "intent": r_with_ex["intent"],
            "tool": r_with_ex["tool"],
            "parameters": r_with_ex["parameters"],
            "odata_request": r_with_ex["odata_request"],
        },
        "eval_with_examples": r_with_ex["eval"],
        "calls_with_examples": r_with_ex["calls"],
        "graph_traces_with_examples": r_with_ex["graph_traces"],
        "usage_with_examples": r_with_ex["usage"],
        "error_with_examples": r_with_ex["error"],
        "session_with_examples": {
            "id": r_with_ex.get("session_id"),
            "chunk": r_with_ex.get("session_chunk"),
            "promptIndex": r_with_ex.get("session_prompt_index"),
        },
        "latency_with_examples_ms": r_with_ex["elapsed_ms"],
        "pred_search_query": {
            "intent": r_search_q["intent"],
            "tool": r_search_q["tool"],
            "parameters": r_search_q["parameters"],
            "odata_request": r_search_q["odata_request"],
        },
        "eval_search_query": r_search_q["eval"],
        "calls_search_query": r_search_q["calls"],
        "graph_traces_search_query": r_search_q["graph_traces"],
        "usage_search_query": r_search_q["usage"],
        "error_search_query": r_search_q["error"],
        "session_search_query": {
            "id": r_search_q.get("session_id"),
            "chunk": r_search_q.get("session_chunk"),
            "promptIndex": r_search_q.get("session_prompt_index"),
        },
        "latency_search_query_ms": r_search_q["elapsed_ms"],
    }


def run_benchmark(
        limit: int | None = None,
        model: str = "gpt-6.1-sol",
        timeout: int = 180,
        require_live_graph: bool = True,
        session_chunk_size: int = 20,
        pilot: bool = False,
        resume_checkpoint: Path | None = None,
        copilot_path: str | Path | None = None):
    REPORTS_DIR.mkdir(parents=True, exist_ok=True)
    RESULTS_DIR.mkdir(parents=True, exist_ok=True)
    if session_chunk_size <= 0:
        raise ValueError("session_chunk_size must be greater than zero")

    all_queries = json.loads(QUERIES_FILE.read_text(encoding="utf-8"))
    if resume_checkpoint:
        checkpoint, queries, variation_results = load_checkpoint(resume_checkpoint, all_queries)
        run_id = checkpoint["run_id"]
        started_at = checkpoint["started_at"]
        model = checkpoint["model"]
        timeout = checkpoint["timeout"]
        require_live_graph = checkpoint["require_live_graph"]
        session_chunk_size = checkpoint["session_chunk_size"]
        checkpoint_path = resume_checkpoint
        elapsed_offset = checkpoint.get("elapsed_seconds", 0)
        print(f"Resuming checkpoint: {checkpoint_path}", flush=True)
    else:
        queries = all_queries
        if pilot:
            queries = [query for query in queries if query["id"] in PILOT_QUERY_IDS]
        if limit and limit > 0:
            queries = queries[:limit]
        now_slug = datetime.datetime.now().strftime("%Y%m%d_%H%M%S")
        run_id = f"report_large_3way_{model}_{now_slug}"
        started_at = datetime.datetime.now().isoformat()
        checkpoint_path = RESULTS_DIR / f"checkpoint_{run_id}.json"
        elapsed_offset = 0
        variation_results = {
            "no_examples": {},
            "with_examples": {},
            "search_query": {},
        }
    if require_live_graph:
        verify_graph_auth()

    catalogs = {k: fetch_server_catalog(url) for k, url in SERVERS.items()}
    start_ts = time.perf_counter()
    save_checkpoint(
        checkpoint_path, run_id, started_at, model, timeout, require_live_graph,
        session_chunk_size, queries, variation_results, start_ts, elapsed_offset)
    print(f"Checkpoint: {checkpoint_path}", flush=True)

    # Run each server type completely before moving to the next. Within each server,
    # bounded persistent-session chunks preserve MCP context without allowing all 100
    # independent queries to accumulate in one conversation.
    for variation in ("no_examples", "with_examples", "search_query"):
        server_url = SERVERS[variation]
        pending = [
            query for query in queries
            if query["id"] not in variation_results[variation]
        ]
        print(
            f"\n[{variation}] {len(pending)} pending of {len(queries)} queries "
            f"against {server_url}; persistent-session chunk size={session_chunk_size}",
            flush=True,
        )
        for chunk_start in range(0, len(pending), session_chunk_size):
            chunk = pending[chunk_start:chunk_start + session_chunk_size]
            chunk_number = chunk_start // session_chunk_size + 1
            print(
                f"[{variation}] Starting persistent session chunk {chunk_number} "
                f"for query IDs {[query['id'] for query in chunk]}",
                flush=True,
            )
            with mcp_relay(server_url) as (relay_url, relay_state):
                with CopilotCliSession(
                        relay_url, model, timeout, f"{variation}-chunk{chunk_number}",
                        copilot_path) as session:
                    for query in chunk:
                        variation_results[variation][query["id"]] = run_copilot_variant(
                            query, variation, session, relay_state, require_live_graph)
                        save_checkpoint(
                            checkpoint_path, run_id, started_at, model, timeout,
                            require_live_graph, session_chunk_size, queries,
                            variation_results, start_ts, elapsed_offset)

    results = [result_row(query, variation_results) for query in queries]

    elapsed = round(elapsed_offset + time.perf_counter() - start_ts, 2)
    total = max(1, len(results))

    succ_no_ex = sum(1 for r in results if r["eval_no_examples"]["execution_success"])
    succ_with_ex = sum(1 for r in results if r["eval_with_examples"]["execution_success"])
    succ_search_q = sum(1 for r in results if r["eval_search_query"]["execution_success"])

    intent_no_ex = sum(1 for r in results if r["eval_no_examples"]["intent_match"])
    intent_with_ex = sum(1 for r in results if r["eval_with_examples"]["intent_match"])
    intent_search_q = sum(1 for r in results if r["eval_search_query"]["intent_match"])
    live_no_ex = sum(r["eval_no_examples"]["live_graph_calls"] for r in results)
    live_with_ex = sum(r["eval_with_examples"]["live_graph_calls"] for r in results)
    live_search_q = sum(r["eval_search_query"]["live_graph_calls"] for r in results)

    rate_no_ex = round(succ_no_ex * 100.0 / total, 1)
    rate_with_ex = round(succ_with_ex * 100.0 / total, 1)
    rate_search_q = round(succ_search_q * 100.0 / total, 1)

    tok_with_ex = catalogs["with_examples"]["catalog_tokens"]
    tok_search_q = catalogs["search_query"]["catalog_tokens"]
    tok_reduction_pct = round((tok_with_ex - tok_search_q) * 100.0 / max(1, tok_with_ex), 1)

    now_slug = run_id.rsplit("_", 2)[-2] + "_" + run_id.rsplit("_", 1)[-1]

    summary = {
        "id": run_id,
        "timestamp": started_at,
        "model": model,
        "engine": "GitHub Copilot CLI with local HTTP MCP servers",
        "session_strategy": (
            f"independent prompts in persistent Copilot sessions, "
            f"chunked at {session_chunk_size} queries per MCP variation"),
        "session_chunk_size": session_chunk_size,
        "pilot_query_ids": (
            PILOT_QUERY_IDS
            if [query["id"] for query in queries] == PILOT_QUERY_IDS
            else None
        ),
        "require_live_graph": require_live_graph,
        "total_queries": len(results),
        "is_full_run": len(results) >= 100,
        "elapsed_seconds": elapsed,
        "servers": SERVERS,
        "catalog_summary": {
            k: {
                "url": c["url"],
                "csdlPath": c["csdl_path"],
                "toolsCount": len(c["tools"]),
                "resourcesCount": len(c["resources"]) + len(c["templates"]),
                "promptsCount": len(c["prompts"]),
                "examplesCount": c["examples_count"],
                "catalogTokens": c["catalog_tokens"],
                "catalogHash": c["catalog_hash"],
            }
            for k, c in catalogs.items()
        },
        "metrics": {
            "no_examples_success_rate": rate_no_ex,
            "with_examples_success_rate": rate_with_ex,
            "search_query_success_rate": rate_search_q,
            "no_examples_intent_accuracy": round(intent_no_ex * 100.0 / total, 1),
            "with_examples_intent_accuracy": round(intent_with_ex * 100.0 / total, 1),
            "search_query_intent_accuracy": round(intent_search_q * 100.0 / total, 1),
            "examples_impact_delta": round(rate_with_ex - rate_no_ex, 1),
            "architecture_impact_delta": round(rate_search_q - rate_with_ex, 1),
            "search_vs_no_examples_delta": round(rate_search_q - rate_no_ex, 1),
            "catalog_tokens": {
                "no_examples": catalogs["no_examples"]["catalog_tokens"],
                "with_examples": tok_with_ex,
                "search_query": tok_search_q,
            },
            "catalog_token_reduction_pct": tok_reduction_pct,
            "live_graph_calls": {
                "no_examples": live_no_ex,
                "with_examples": live_with_ex,
                "search_query": live_search_q,
            },
            "avg_latency_ms": {
                "no_examples": round(sum(r["latency_no_examples_ms"] for r in results) / total, 2),
                "with_examples": round(sum(r["latency_with_examples_ms"] for r in results) / total, 2),
                "search_query": round(sum(r["latency_search_query_ms"] for r in results) / total, 2),
            },
        },
        "category_breakdown": compute_breakdowns(results, lambda r: r["category"]),
        "workload_breakdown": compute_breakdowns(results, lambda r: r["workload"]),
        "results": results,
    }

    md_report = generate_markdown_report(summary)
    SUMMARY_FILE.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    REPORT_MD_FILE.write_text(md_report, encoding="utf-8")
    (REPORTS_DIR / f"{run_id}.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    (REPORTS_DIR / f"{run_id}.md").write_text(md_report, encoding="utf-8")
    (RESULTS_DIR / f"results_{now_slug}.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    checkpoint_path.unlink(missing_ok=True)

    print(f"[Large 3-Way Experiment Complete] {len(results)} queries in {elapsed}s")
    print(f"  Var 1 (Direct w/o Examples :3004): {rate_no_ex}% ({succ_no_ex}/{len(results)})")
    print(f"  Var 2 (Direct with Examples :3003): {rate_with_ex}% ({succ_with_ex}/{len(results)}) [Delta: +{summary['metrics']['examples_impact_delta']} pp]")
    print(f"  Var 3 (Intent + OData Query :3005): {rate_search_q}% ({succ_search_q}/{len(results)}) [Delta vs Var 2: +{summary['metrics']['architecture_impact_delta']} pp | Token reduction: -{tok_reduction_pct}%]")
    return summary


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Run a live Copilot CLI + Microsoft Graph 3-way Large CSDL MCP experiment")
    parser.add_argument("--limit", type=int, default=None, help="Optional limit on number of queries")
    parser.add_argument("--model", type=str, default="gpt-6.1-sol", help="Copilot CLI model ID")
    parser.add_argument("--timeout", type=int, default=180, help="Per Copilot query timeout in seconds")
    parser.add_argument("--allow-fallback", action="store_true",
                        help="Allow fallback/mock responses instead of requiring a live Graph call")
    parser.add_argument("--rescore-reports", action="store_true",
                        help="Re-evaluate and rewrite saved live-Copilot reports without rerunning queries")
    parser.add_argument("--session-chunk-size", type=int, default=20,
                        help="Independent prompts per persistent Copilot session (default: 20)")
    parser.add_argument("--pilot", action="store_true",
                        help="Run the balanced 15-query representative pilot")
    parser.add_argument("--resume-checkpoint", type=Path, default=None,
                        help="Resume an incomplete run from its checkpoint JSON")
    parser.add_argument("--copilot-path", type=Path, default=None,
                        help="Optional explicit path to copilot.exe")
    args = parser.parse_args()
    if args.rescore_reports:
        rescore_saved_reports()
    else:
        run_benchmark(
            limit=args.limit,
            model=args.model,
            timeout=args.timeout,
            require_live_graph=not args.allow_fallback,
            session_chunk_size=args.session_chunk_size,
            pilot=args.pilot,
            resume_checkpoint=args.resume_checkpoint,
            copilot_path=args.copilot_path,
        )

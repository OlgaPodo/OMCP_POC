import json
import urllib.request
import unittest

META = {
    "io.modelcontextprotocol/protocolVersion": "2026-07-28",
    "io.modelcontextprotocol/clientInfo": {"name": "TransformParityVerifier", "version": "2.0.0"},
    "io.modelcontextprotocol/clientCapabilities": {},
}

PAIRS = [
    ("Server_WithPrompts.csdl", "http://localhost:3001", "http://localhost:3011"),
    ("Server_Baseline.csdl", "http://localhost:3002", "http://localhost:3012"),
    ("Server_Large.csdl", "http://localhost:3003", "http://localhost:3013"),
]


def http_get_json(url: str):
    with urllib.request.urlopen(url, timeout=15) as resp:
        return json.loads(resp.read().decode("utf-8"))


def mcp_rpc(base_url: str, method: str, params: dict | None = None):
    payload = {
        "jsonrpc": "2.0",
        "id": 1,
        "method": method,
        "params": {**(params or {}), "_meta": META},
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
        base_url + "/",
        data=json.dumps(payload).encode("utf-8"),
        headers=headers,
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=20) as resp:
        raw = resp.read().decode("utf-8")
        for line in raw.splitlines():
            if line.startswith("data: "):
                return json.loads(line[6:].strip())
        return json.loads(raw)


class TransformParityTests(unittest.TestCase):
    maxDiff = None

    def test_all_three_transformed_servers_match_existing_servers(self):
        for csdl_name, orig_url, trans_url in PAIRS:
            with self.subTest(csdl=csdl_name):
                # 1. CSDL & EDM Model parity
                orig_csdl = http_get_json(f"{orig_url}/api/csdl")
                trans_csdl = http_get_json(f"{trans_url}/api/csdl")
                self.assertEqual(orig_csdl["csdlXml"], trans_csdl["csdlXml"])
                self.assertEqual(json.loads(orig_csdl["csdlJson"]), json.loads(trans_csdl["csdlJson"]))
                self.assertEqual(orig_csdl["edmElementsCount"], trans_csdl["edmElementsCount"])

                # 2. Projection parity
                orig_proj = http_get_json(f"{orig_url}/api/projection")
                trans_proj = http_get_json(f"{trans_url}/api/projection")
                for key in ("tools", "resources", "prompts", "exampleQueries", "mcpTools", "mcpResources", "mcpResourceTemplates"):
                    self.assertEqual(orig_proj[key], trans_proj[key], f"Projection mismatch on {key} for {csdl_name}")

                # 3. MCP Discovery RPC parity (tools/list, resources/list, resources/templates/list, prompts/list)
                for method in ("tools/list", "resources/list", "resources/templates/list", "prompts/list"):
                    r_orig = mcp_rpc(orig_url, method)["result"]
                    r_trans = mcp_rpc(trans_url, method)["result"]
                    self.assertEqual(r_orig, r_trans, f"RPC {method} mismatch for {csdl_name}")

                # 4. Every prompt in prompts/list renders identically via prompts/get
                prompts = mcp_rpc(orig_url, "prompts/list")["result"]["prompts"]
                for p in prompts:
                    p_orig = mcp_rpc(orig_url, "prompts/get", {"name": p["name"], "arguments": {}})["result"]
                    p_trans = mcp_rpc(trans_url, "prompts/get", {"name": p["name"], "arguments": {}})["result"]
                    self.assertEqual(p_orig, p_trans, f"prompts/get({p['name']}) mismatch for {csdl_name}")

                # 5. Resources read parity
                uris = ["users://me", "users://miriamg@contoso.com", "groups://all", "groups://11111111-2222-3333-4444-555555555555"]
                if csdl_name == "Server_Large.csdl":
                    uris.extend(["drives://me", "files://recent", "files://file-mcp-poc-docx", "teams://all", "channels://ch-csdl-typespec/messages", "chats://all", "todo://lists", "todo://tasks"])
                for uri in uris:
                    res_orig = mcp_rpc(orig_url, "resources/read", {"uri": uri})["result"]
                    res_trans = mcp_rpc(trans_url, "resources/read", {"uri": uri})["result"]
                    self.assertEqual(res_orig, res_trans, f"resources/read({uri}) mismatch for {csdl_name}")

                # 6. Tools call parity
                tool_calls = [
                    ("find_user", {"query": "Miriam"}),
                    ("user_profile", {"userId": "miriamg@contoso.com"}),
                    ("find_group", {"query": "Identity"}),
                    ("group_details", {"groupId": "Identity Architecture"}),
                    ("read_resource", {"uri": "users://miriamg@contoso.com"}),
                ]
                if csdl_name == "Server_Large.csdl":
                    tool_calls.extend([
                        ("list_my_files", {"userId": "me"}),
                        ("search_files", {"query": "CSDL"}),
                        ("file_last_modified_by", {"fileName": "Script_MCP_POC.docx"}),
                        ("list_teams", {"filter": "all"}),
                        ("channel_messages", {"channelId": "CSDL & TypeSpec"}),
                        ("list_chats", {"filter": "all"}),
                        ("list_todo_tasks", {"filter": "high"}),
                    ])
                for t_name, t_args in tool_calls:
                    tc_orig = mcp_rpc(orig_url, "tools/call", {"name": t_name, "arguments": t_args})["result"]
                    tc_trans = mcp_rpc(trans_url, "tools/call", {"name": t_name, "arguments": t_args})["result"]
                    self.assertEqual(tc_orig, tc_trans, f"tools/call({t_name}) mismatch for {csdl_name}")

                print(
                    f"[PARITY VERIFIED] {csdl_name}: {orig_url} == {trans_url} | "
                    f"tools={len(orig_proj['mcpTools'])}, "
                    f"resources={len(orig_proj['mcpResources']) + len(orig_proj['mcpResourceTemplates'])}, "
                    f"prompts={len(prompts)}, "
                    f"examples={len(orig_proj['exampleQueries'])}"
                )


if __name__ == "__main__":
    unittest.main(verbosity=2)

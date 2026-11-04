import argparse
import copy
import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest.mock import patch

import run_experiment as runner


class ExperimentTests(unittest.TestCase):
    def setUp(self):
        self.truth = {"targetIntent": "user_quick_lookup", "expectedTool": "user_profile",
                      "expectedParams": {"userId": "me"}}
        self.prediction = {"intent": "user_quick_lookup", "tool": "user_profile",
                           "parameters": {"userId": "me"}, "odata_request": "not-a-valid-api"}

    def test_scoring_requires_actual_successful_call_and_parameters(self):
        self.assertFalse(runner.evaluate_prediction(self.prediction, self.truth, [])["execution_success"])
        receipt = {"name": "user_profile", "arguments": {"userId": "someone-else"}, "success": True}
        self.assertFalse(runner.evaluate_prediction(self.prediction, self.truth, [receipt])["execution_success"])
        receipt["arguments"]["userId"] = "me"
        receipt["success"] = False
        self.assertFalse(runner.evaluate_prediction(self.prediction, self.truth, [receipt])["execution_success"])
        receipt["success"] = True
        result = runner.evaluate_prediction(self.prediction, self.truth, [receipt])
        self.assertTrue(result["execution_success"])
        self.assertIsNone(result["odata_valid"])

    def test_out_of_scope_must_not_execute_unrelated_tools(self):
        truth = {"targetIntent": "out_of_scope"}
        pred = {"intent": "out_of_scope", "tool": "none"}
        self.assertTrue(runner.evaluate_prediction(pred, truth, [])["execution_success"])
        self.assertFalse(runner.evaluate_prediction(pred, truth, [{"name": "find_user"}])["execution_success"])

    def test_composed_prompts_require_both_resource_reads(self):
        truth = {"targetIntent": "user_peers_in_group", "expectedTool": "read_resource",
                 "requiredTools": ["user_profile", "find_group"],
                 "requiredResources": [{"entitySet": "users"}, {"entitySet": "groups"}]}
        prediction = {"intent": "user_peers_in_group", "tool": "read_resource"}
        receipts = [{"name": name, "success": True} for name in truth["requiredTools"]]
        receipts.append({"name": "read_resource", "entity_set": "users", "success": True})
        self.assertFalse(runner.evaluate_prediction(prediction, truth, receipts)["execution_success"])
        receipts.append({"name": "read_resource", "entity_set": "groups", "success": True})
        self.assertTrue(runner.evaluate_prediction(prediction, truth, receipts)["execution_success"])

    def test_cli_sessions_are_unique_and_have_only_assigned_mcp(self):
        homes = []

        def fake_run(command, **kwargs):
            home = Path(kwargs["env"]["COPILOT_HOME"])
            homes.append(str(home))
            self.assertEqual(json.loads((home / "mcp-config.json").read_text()), {"mcpServers": {}})
            config = Path(command[command.index("--additional-mcp-config") + 1][1:])
            servers = json.loads(config.read_text())["mcpServers"]
            self.assertEqual(list(servers), ["GraphMcp"])
            self.assertEqual(servers["GraphMcp"]["url"], "http://127.0.0.1:1234/")
            self.assertIn("--disable-builtin-mcps", command)
            self.assertFalse(Path(kwargs["cwd"]).joinpath(".git").exists())
            self.assertNotIn("--resume", command)
            event = {"type": "assistant.message", "data": {"phase": "final_answer",
                     "content": '{"intent":"x","tool":"none"}'}}
            return argparse.Namespace(returncode=0, stdout=json.dumps(event), stderr="")

        with patch.object(runner.subprocess, "run", side_effect=fake_run):
            for _ in range(2):
                runner.call_copilot_cli("test", "http://127.0.0.1:1234/")
        self.assertNotEqual(homes[0], homes[1])
        self.assertTrue(all(not Path(home).exists() for home in homes))

    def test_baseline_finishes_before_examples_with_multiple_workers(self):
        order = []
        args = argparse.Namespace(server_a="a", server_b="b", workers=2)
        queries = [{"id": i} for i in range(3)]

        def processor(query, args, catalog, url, label):
            order.append(url)
            return query

        runner.run_phases(queries, args, {"a": {}, "b": {}}, processor)
        self.assertEqual(order, ["b", "b", "b", "a", "a", "a"])

    def test_examples_are_separate_from_identical_prompt_responses(self):
        baseline = {"tools": [{"name": "find_user"}, {"name": "user_profile"}], "resources": [], "resourceTemplates": [],
                    "prompts": [], "exampleQueries": []}
        variant = {"tools": [{"name": "find_user"}, {"name": "user_profile"}], "resources": [], "resourceTemplates": [],
                   "prompts": [{"definition": {"name": "lookup"}, "result": {"messages": []}}],
                   "exampleQueries": []}
        runner.validate_catalogs({"b": baseline, "a": variant})
        variant["prompts"][0]["result"]["examples"] = []
        with self.assertRaisesRegex(RuntimeError, "must not contain examples"):
            runner.validate_catalogs({"b": baseline, "a": variant})

    def test_mcp_error_payload_is_not_success(self):
        message = {"method": "tools/call", "params": {"name": "user_profile", "arguments": {}}}
        response = {"result": {"content": [{"type": "text", "text": '{"error":"Not found"}'}]}}
        self.assertFalse(runner.summarize_tool(message, response, 200)["success"])

    def test_api_uses_supplied_bearer_credential(self):
        class Response:
            def __enter__(self):
                return self

            def __exit__(self, *_):
                pass

            def read(self):
                return b'{"choices":[{"message":{"content":"{}"}}]}'

        with patch.object(runner, "rpc", return_value={"tools": []}):
            with patch.object(runner.urllib.request, "urlopen", return_value=Response()) as request:
                runner.call_openai_compatible("test", "model", "http://localhost:1", "test-token", "http://localhost:2")
        self.assertEqual(request.call_args.args[0].get_header("Authorization"), "Bearer test-token")

    def test_parameters_match_handles_manager_name_and_query_filters(self):
        # Case 1: managerNameOrId mapping to userId
        truth_manager = {"expectedParams": {"managerNameOrId": "olga"}}
        receipt_profile = {"arguments": {"userId": "olga"}}
        self.assertTrue(runner.parameters_match(receipt_profile, truth_manager))

        # Case 2: department / jobTitle matching in find_user query
        truth_dept = {"expectedParams": {"department": "Engineering", "jobTitle": "Director"}}
        receipt_search = {"arguments": {"query": "Engineering Director"}}
        self.assertTrue(runner.parameters_match(receipt_search, truth_dept))

    def test_csdl_proposal_inspection(self):
        xml_with_legacy = '<Schema Namespace="Agent"><Annotation Term="Agent.Tools"/></Schema>'
        json_with_legacy = '{"Agent": {}, "@Agent.Tools": []}'
        audit = runner.inspect_csdl_proposal_conformance(xml_with_legacy, json_with_legacy)
        self.assertTrue(audit["has_inlined_agent_schema"])
        self.assertTrue(audit["has_tool_annotations"])
        self.assertEqual(audit["focus"], "Agent.Prompts annotations only")

        clean_xml = '<Schema Namespace="Microsoft.Graph"><Annotation Term="Agent.Prompts"/></Schema>'
        clean_json = '{"Microsoft.Graph": {}, "@Agent.Prompts": []}'
        clean_audit = runner.inspect_csdl_proposal_conformance(clean_xml, clean_json)
        self.assertFalse(clean_audit["has_inlined_agent_schema"])
        self.assertFalse(clean_audit["has_tool_annotations"])

    def test_example_annotations_only_target_entity_sets(self):
        ns = {"e": "http://docs.oasis-open.org/odata/ns/edm"}
        directory = Path(__file__).parent.parent / "Csdl"
        for stem in ["Server_WithExamples", "Server_Baseline"]:
            root = ET.parse(directory / (stem + ".csdl")).getroot()
            container = root.find(".//e:EntityContainer", ns)
            self.assertEqual(container.find("e:Singleton", ns).attrib["Type"], "Microsoft.Graph.User")
            has_prompts = stem == "Server_WithExamples"
            self.assertEqual(len(container.findall("e:Annotation[@Term='Agent.Prompts']", ns)), 1 if has_prompts else 0)
            self.assertEqual(len(root.findall(".//e:Annotation[@Term='Agent.Tools']", ns)), 0)
            self.assertEqual(len(root.findall(".//e:Annotation[@Term='Agent.Resources']", ns)), 0)
            self.assertEqual(len(root.findall(".//e:Annotation[@Term='Agent.ExampleQueries']", ns)), 0)
            self.assertIsNone(root.find(".//e:ComplexType[@Name='ExampleQuery']", ns))
            prompt_schema = root.find(".//e:ComplexType[@Name='PromptType']", ns)
            if has_prompts:
                self.assertIsNotNone(prompt_schema)
                self.assertTrue(all(property.attrib["Name"].lower() not in ("examples", "tools", "resources") for property in prompt_schema))
            else:
                self.assertIsNone(prompt_schema)
            for prompt in container.findall("e:Annotation[@Term='Agent.Prompts']/e:Collection/e:Record", ns):
                self.assertTrue(all(property.attrib["Property"].lower() not in ("examples", "tools", "resources") for property in prompt))


if __name__ == "__main__":
    unittest.main()

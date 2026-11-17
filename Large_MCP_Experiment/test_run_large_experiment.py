import json
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import run_large_experiment as runner


class LargeExperimentTests(unittest.TestCase):
    def test_benchmark_has_100_questions_across_all_workloads(self):
        queries = json.loads(runner.QUERIES_FILE.read_text(encoding="utf-8"))
        self.assertEqual(len(queries), 100)
        ids = [q["id"] for q in queries]
        self.assertEqual(ids, list(range(1, 101)))
        workloads = {q.get("workload") for q in queries}
        self.assertEqual(workloads, {"users_groups", "files", "teams", "chats", "todo", "cross_workload"})

    def test_pilot_is_balanced_across_all_workloads(self):
        queries = json.loads(runner.QUERIES_FILE.read_text(encoding="utf-8"))
        selected = [q for q in queries if q["id"] in runner.PILOT_QUERY_IDS]
        self.assertEqual([q["id"] for q in selected], runner.PILOT_QUERY_IDS)
        self.assertEqual(len(selected), 15)
        self.assertEqual(
            {q.get("workload") for q in selected},
            {"users_groups", "files", "teams", "chats", "todo", "cross_workload"},
        )

    def test_large_csdl_with_and_without_examples_conformance(self):
        ns = {"e": "http://docs.oasis-open.org/odata/ns/edm"}
        csdl_dir = Path(__file__).parent.parent / "Csdl"

        with_ex = ET.parse(csdl_dir / "Server_Large_WithExamples.csdl").getroot()
        no_ex = ET.parse(csdl_dir / "Server_Large.csdl").getroot()

        self.assertEqual(len(with_ex.findall(".//e:Annotation[@Term='Agent.Prompts']/e:Collection/e:Record", ns)), 22)
        self.assertEqual(len(no_ex.findall(".//e:Annotation[@Term='Agent.Prompts']/e:Collection/e:Record", ns)), 22)

        ex_records_with = with_ex.findall(".//e:Annotation[@Term='Agent.Examples']/e:Collection/e:Record", ns)
        ex_records_no = no_ex.findall(".//e:Annotation[@Term='Agent.Examples']/e:Collection/e:Record", ns)
        self.assertEqual(len(ex_records_with), 28)
        self.assertEqual(len(ex_records_no), 0)

        for rec in ex_records_with:
            self.assertTrue(all(p.attrib.get("Property") != "Prompt" for p in rec))

    def test_scoring_accepts_later_correct_tool_call(self):
        item = {
            "expectedTool": "get_users_manager",
            "transformerArgs": {"key_id": "avery.adams@example.com"},
        }
        successful = [
            {
                "name": "get_users_manager",
                "arguments": {"key_id": "wrong-id"},
                "resolved_entity": {
                    "id": "wrong-id",
                    "userPrincipalName": "example.directory.077@example.com",
                },
            },
            {
                "name": "get_users_manager",
                "arguments": {"key_id": "correct-id"},
                "resolved_entity": {
                    "id": "correct-id",
                    "userPrincipalName": "avery.adams@example.com",
                },
            },
        ]
        self.assertTrue(runner.direct_parameters_match(item, successful))
        self.assertTrue(runner.direct_parameters_match(item, [{
            "name": "get_users_manager",
            "arguments": {"key_id": "Avery"},
            "resolved_entity": {
                "id": "correct-id",
                "userPrincipalName": "avery.adams@example.com",
            },
        }]))

    def test_scoring_accepts_current_user_tool_equivalent(self):
        item = {
            "targetIntent": "whom_avery_reports_to",
            "expectedTool": "get_users_manager",
            "expectedGraphEndpoint": "users('avery.adams@example.com')/manager",
            "transformerArgs": {"key_id": "avery.adams@example.com"},
        }
        evaluation = runner.evaluate_live_result(
            item,
            "no_examples",
            {"intent": "whom_avery_reports_to", "tool": "get_me_manager"},
            [{
                "name": "get_me_manager",
                "success": True,
                "arguments": {},
                "odata_query": "me/manager",
            }],
            [{"isLive": True, "statusCode": 200}],
            True,
        )
        self.assertTrue(evaluation["execution_success"])

    def test_scoring_accepts_current_user_odata_path_equivalent(self):
        item = {
            "targetIntent": "whom_avery_reports_to",
            "expectedTool": "get_users_manager",
            "expectedGraphEndpoint": "users('avery.adams@example.com')/manager",
            "transformerArgs": {"key_id": "avery.adams@example.com"},
        }
        evaluation = runner.evaluate_live_result(
            item,
            "search_query",
            {"intent": "whom_avery_reports_to", "tool": "execute_odata_query"},
            [
                {"name": "search_intent", "success": True, "arguments": {}},
                {
                    "name": "execute_odata_query",
                    "success": True,
                    "arguments": {"odataQuery": "GET me/manager"},
                },
            ],
            [{"isLive": True, "statusCode": 200}],
            True,
        )
        self.assertTrue(evaluation["execution_success"])

    def test_checkpoint_round_trip_preserves_completed_queries(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "checkpoint.json"
            variation_results = {
                "no_examples": {1: {"tool": "get_users"}},
                "with_examples": {},
                "search_query": {},
            }
            runner.save_checkpoint(
                path, "run-1", "2026-01-01T00:00:00", "test-model", 180,
                False, 20, [{"id": 1}], variation_results, 0, 12.5)
            checkpoint, queries, restored = runner.load_checkpoint(
                path, [{"id": 1, "query": "test"}])
        self.assertEqual(checkpoint["run_id"], "run-1")
        self.assertEqual(queries[0]["id"], 1)
        self.assertEqual(restored["no_examples"][1]["tool"], "get_users")

    @patch("run_large_experiment.http_get_json")
    def test_graph_trace_filter_uses_exact_correlation(self, http_get):
        http_get.return_value = [
            {"correlationId": "query-a", "isLive": True, "statusCode": 200},
            {"correlationId": "query-b", "isLive": True, "statusCode": 200},
        ]
        traces = runner.graph_traces_for("query-b", "search_query")
        self.assertEqual(len(traces), 1)
        self.assertEqual(traces[0]["correlationId"], "query-b")

    @patch("run_large_experiment.shutil.which", return_value="copilot.exe")
    @patch("run_large_experiment.subprocess.run")
    def test_copilot_session_ignores_non_json_diagnostics(self, run_process, _):
        run_process.return_value = SimpleNamespace(
            returncode=0,
            stderr="",
            stdout="\n".join([
                "diagnostic: loading cached model context",
                json.dumps({"type": "session.start", "data": {"sessionId": "session-1"}}),
                json.dumps({"type": "assistant.message", "data": {
                    "content": '{"intent":"test","tool":"test","parameters":{}}',
                }}),
            ]),
        )
        session = runner.CopilotCliSession(
            "http://127.0.0.1:1234/", "test-model", 30, "test")
        try:
            answer, usage = session.ask("test")
        finally:
            session.close()
        self.assertIn('"intent":"test"', answer)
        self.assertIsNone(usage)
        self.assertEqual(session.session_id, "session-1")

    def test_query_exception_becomes_checkpointable_result(self):
        class FailingSession:
            session_id = None
            variation = "no_examples-chunk1"
            prompt_count = 1

            def ask(self, _):
                raise ValueError("non-JSON Copilot output")

        result = runner.run_copilot_variant(
            {
                "id": 1,
                "query": "Who is my manager?",
                "targetIntent": "manager",
                "expectedTool": "get_users_manager",
                "transformerArgs": {"key_id": "avery.adams@example.com"},
            },
            "no_examples",
            FailingSession(),
            runner.RelayState(),
            True,
        )
        self.assertFalse(result["eval"]["execution_success"])
        self.assertIn("ValueError", result["error"])

    def test_rescore_report_updates_incorrect_failure(self):
        query = {
            "id": 1,
            "targetIntent": "whom_avery_reports_to",
            "expectedTool": "get_users_manager",
            "expectedGraphEndpoint": "users('avery.adams@example.com')/manager",
            "transformerArgs": {"key_id": "avery.adams@example.com"},
            "category": "canonical",
            "workload": "users_groups",
        }
        correct_call = {
            "name": "get_users_manager",
            "arguments": {"key_id": "correct-id"},
            "success": True,
            "odata_query": "users('correct-id')/manager",
            "resolved_entity": {
                "id": "correct-id",
                "userPrincipalName": "avery.adams@example.com",
            },
        }
        report = {
            "engine": "GitHub Copilot CLI with local HTTP MCP servers",
            "require_live_graph": True,
            "results": [{
                "query_id": 1,
                "category": "canonical",
                "workload": "users_groups",
                "pred_no_examples": {
                    "intent": "manager",
                    "tool": "get_users_manager",
                },
                "calls_no_examples": [correct_call],
                "graph_traces_no_examples": [{
                    "isLive": True, "statusCode": 200,
                }],
                "eval_no_examples": {"execution_success": False},
                "latency_no_examples_ms": 10,
                "pred_with_examples": {
                    "intent": "manager",
                    "tool": "get_users_manager",
                },
                "calls_with_examples": [correct_call],
                "graph_traces_with_examples": [{
                    "isLive": True, "statusCode": 200,
                }],
                "eval_with_examples": {"execution_success": False},
                "latency_with_examples_ms": 10,
                "pred_search_query": {
                    "intent": "whom_avery_reports_to",
                    "tool": "execute_odata_query",
                },
                "calls_search_query": [
                    {"name": "search_intent", "success": True},
                    {
                        "name": "execute_odata_query",
                        "success": True,
                        "arguments": {
                            "odataQuery":
                                "GET users('avery.adams@example.com')/manager",
                        },
                    },
                ],
                "graph_traces_search_query": [{
                    "isLive": True, "statusCode": 200,
                }],
                "eval_search_query": {"execution_success": False},
                "latency_search_query_ms": 10,
            }],
            "metrics": {},
        }
        self.assertTrue(runner.rescore_report(report, {1: query}))
        self.assertTrue(report["results"][0]["eval_no_examples"]["execution_success"])
        self.assertEqual(report["metrics"]["no_examples_success_rate"], 100.0)

    @patch("run_large_experiment.run_copilot_variant")
    def test_three_servers_catalog_and_partial_run(self, run_variant):
        run_variant.return_value = {
            "intent": "test",
            "tool": "test",
            "parameters": {},
            "odata_request": "",
            "answer": "",
            "calls": [],
            "graph_traces": [],
            "usage": None,
            "error": None,
            "elapsed_ms": 1.0,
            "eval": {
                "intent_match": True,
                "tool_match": True,
                "parameters_match": True,
                "odata_match": True,
                "live_graph_calls": 1,
                "execution_success": True,
                "failure_reason": None,
            },
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch.object(runner, "REPORTS_DIR", root / "reports"), \
                    patch.object(runner, "RESULTS_DIR", root / "results"), \
                    patch.object(runner, "SUMMARY_FILE", root / "results_summary.json"), \
                    patch.object(runner, "REPORT_MD_FILE", root / "EXPERIMENT_REPORT.md"), \
                    patch("run_large_experiment.shutil.which", return_value="copilot.exe"):
                summary = runner.run_benchmark(
                    limit=5, model="test-runner", require_live_graph=False,
                    session_chunk_size=2)
                self.assertEqual(list((root / "results").glob("checkpoint_*.json")), [])
        self.assertEqual(summary["total_queries"], 5)
        self.assertEqual(summary["catalog_summary"]["no_examples"]["toolsCount"], 59)
        self.assertEqual(summary["catalog_summary"]["no_examples"]["resourcesCount"], 21)
        self.assertEqual(summary["catalog_summary"]["no_examples"]["examplesCount"], 0)
        self.assertEqual(summary["catalog_summary"]["with_examples"]["toolsCount"], 59)
        self.assertEqual(summary["catalog_summary"]["with_examples"]["resourcesCount"], 21)
        self.assertEqual(summary["catalog_summary"]["with_examples"]["examplesCount"], 28)
        self.assertEqual(summary["catalog_summary"]["search_query"]["toolsCount"], 2)
        self.assertGreater(summary["metrics"]["catalog_token_reduction_pct"], 50.0)
        for result in summary["results"]:
            self.assertFalse(any(call["method"] == "GET /api/examples"
                                 for call in result["calls_with_examples"]))
        self.assertEqual(run_variant.call_count, 15)
        sessions_by_variation = {}
        for call in run_variant.call_args_list:
            variation = call.args[1]
            sessions_by_variation.setdefault(variation, set()).add(id(call.args[2]))
        self.assertEqual(
            {variation: len(sessions) for variation, sessions in sessions_by_variation.items()},
            {"no_examples": 3, "with_examples": 3, "search_query": 3},
        )


if __name__ == "__main__":
    unittest.main()

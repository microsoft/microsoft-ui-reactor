from contextlib import redirect_stdout
import hashlib
import io
import json
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
import zipfile

import review_eval
import score_eval


class ReviewEvalTests(unittest.TestCase):
    def review(self, status="reviewed"):
        return {"findings": [], "coverage": [
            {"dimension": name, "status": status, "notes": "Fixture coverage"}
            for name in review_eval.DIMENSIONS
        ]}

    def experiment(self, root, coverage="reviewed", **result_changes):
        config = self.config()
        config.update(
            name="Fixture", source_pr="https://github.com/example/repo/pull/1",
            base="0" * 40, reasoning_effort="high", models=["model-a"],
            cases=[{"id": "c1"}], repetitions=1, max_runs=1,
        )
        config["schedule"] = review_eval.schedule(config)
        reference = {
            "labels": [{"id": "R1", "category": "runtime", "present_in": ["c1"],
                        "acceptable_severities": ["high"]}],
            "scoring": {"limitations": []},
        }
        review_eval.write_json(root / "experiment.json", config)
        review_eval.write_json(root / "reference.json", reference)
        review_eval.write_json(root / "protocol-lock.json", {
            "cli_version": "fixture",
            **{f"{name}_sha256": hashlib.sha256((root / f"{name}.json").read_bytes()).hexdigest()
               for name in ("experiment", "reference")},
        })
        result = {
            **config["schedule"][0], "status": "completed", "exit_code": 0,
            "review_valid": True, "review_complete": True, "budget_exhausted": False,
            "model_verified": True, "input_unchanged": True, "unexpected_tools": [],
            "outside_read_attempts": [], "ai_credits": 10, "elapsed_seconds": 1,
            **result_changes,
        }
        folder = root / "runs" / "r01"
        review_eval.write_json(folder / "result.json", result)
        review_eval.write_json(folder / "review.json", self.review(coverage))
        return config, result

    def config(self):
        return {
            "models": ["model-a", "model-b"],
            "cases": [{"id": "c1"}, {"id": "c2"}],
            "seed": 7,
            "repetitions": 2,
            "max_runs": 8,
            "credits_per_run": 30,
            "total_credits": 240,
            "timeout_seconds": 900,
        }

    def test_schedule_is_balanced_and_reproducible(self):
        config = self.config()
        actual = review_eval.schedule(config)
        self.assertEqual(actual, review_eval.schedule(config))
        self.assertEqual(8, len(actual))
        self.assertEqual(8, len({
            (job["case"], job["model"], job["repetition"]) for job in actual
        }))

    def test_schedule_cannot_exceed_approved_calls(self):
        config = self.config()
        config["max_runs"] = 7
        with self.assertRaisesRegex(ValueError, "approved"):
            review_eval.validate_config(config)

    def test_budget_and_model_inputs_cannot_disable_the_controls(self):
        for field in ("credits_per_run", "total_credits", "timeout_seconds"):
            for value in (float("nan"), float("inf"), True, -1):
                with self.subTest(field=field, value=value):
                    config = self.config()
                    config[field] = value
                    with self.assertRaises(ValueError):
                        review_eval.validate_config(config)
        config = self.config()
        config["models"] = ["auto"]
        with self.assertRaisesRegex(ValueError, "explicit"):
            review_eval.validate_config(config)

    def test_explicit_uncapped_configuration_omits_the_cli_limit(self):
        config = self.config()
        config.update(credits_per_run=None, total_credits=None, timeout_seconds=None,
                      reasoning_effort="high", prompt="Review the frozen source.")
        review_eval.validate_config(config)
        command = review_eval.cli_command("copilot", config, {"model": "model-a"}, Path("run"))
        self.assertNotIn("--max-ai-credits", command)
        self.assertIn("--deny-tool=shell", command)
        self.assertIn("--deny-tool=write", command)
        self.assertIn("--disallow-temp-dir", command)
        self.assertNotIn("--session-id", command)
        self.assertTrue(review_eval.can_start_run(config, used=1000000, running=19))
        config["total_credits"] = 1200
        with self.assertRaisesRegex(ValueError, "per-run cap"):
            review_eval.validate_config(config)

    def test_capped_configuration_still_reserves_in_flight_cost(self):
        config = self.config()
        config.update(reasoning_effort="high", prompt="Review the frozen source.")
        command = review_eval.cli_command("copilot", config, {"model": "model-a"}, Path("run"))
        self.assertEqual("30", command[command.index("--max-ai-credits") + 1])
        self.assertTrue(review_eval.can_start_run(config, used=150, running=2))
        self.assertFalse(review_eval.can_start_run(config, used=151, running=2))

    def test_unsafe_case_directory_rejected(self):
        config = self.config()
        config["cases"][0]["id"] = "..\\private"
        with self.assertRaisesRegex(ValueError, "Case IDs"):
            review_eval.validate_config(config)

    def test_experiment_cannot_inherit_the_source_checkouts_git_context(self):
        root = Path.cwd()
        with self.assertRaisesRegex(ValueError, "outside"):
            review_eval.prepare(root, root / "unused.json", root / "eval-output")

    def test_unknown_usage_is_not_zero_cost(self):
        for value in (None, True, -1, float("nan"), float("inf")):
            with self.subTest(value=value), self.assertRaises(ValueError):
                review_eval.credits_from_usage({"totalNanoAiu": value, "aiCreditsStatus": "complete"})
        with self.assertRaises(ValueError):
            review_eval.credits_from_usage({"totalNanoAiu": 0, "aiCreditsStatus": "pending"})
        self.assertEqual(26.04881, review_eval.credits_from_usage({
            "totalNanoAiu": 26048810000, "aiCreditsStatus": "complete",
        }))

    def test_final_report_requires_actual_dimension_coverage(self):
        valid = {"findings": [], "coverage": [
            {"dimension": name, "status": "not_applicable", "notes": "No relevant surface"}
            for name in review_eval.DIMENSIONS
        ]}
        self.assertEqual(valid, review_eval.parse_review(json.dumps(valid)))
        fenced = review_eval.parse_review("```json\n" + json.dumps(valid) + "\n```")
        self.assertEqual(["Removed JSON display fence"], fenced.pop("_schema_normalizations"))
        self.assertEqual(valid, fenced)
        valid["coverage"][0]["dimension"] = "Security"
        normalized = review_eval.parse_review(json.dumps(valid))
        self.assertEqual("security", normalized["coverage"][0]["dimension"])
        self.assertTrue(normalized["_schema_normalizations"])
        valid["coverage"][0]["dimension"] = "invented-dimension"
        with self.assertRaises(ValueError):
            review_eval.parse_review(json.dumps(valid))
        valid["coverage"][0]["dimension"] = "security"
        valid["coverage"].pop()
        with self.assertRaises(ValueError):
            review_eval.parse_review(json.dumps(valid))
        with self.assertRaises(ValueError):
            review_eval.parse_review("I found nothing wrong.")

    def test_prose_normalization_preserves_report_and_rejects_ambiguous_objects(self):
        valid = {"findings": [], "coverage": [
            {"dimension": name, "status": "reviewed", "notes": "Inspected the relevant source."}
            for name in review_eval.DIMENSIONS
        ]}
        payload = json.dumps(valid)
        normalized = review_eval.parse_review("Summary of the review.\n\n" + payload)
        self.assertEqual(["Removed prose before JSON report"], normalized.pop("_schema_normalizations"))
        self.assertEqual(valid, normalized)
        with self.assertRaises(ValueError):
            review_eval.parse_review("Summary.\n" + payload + "\n" + payload)
        with self.assertRaises(ValueError):
            review_eval.parse_review("Summary.\n" + payload + "\nMore findings follow.")

    def test_snapshot_extracts_text_but_not_binary(self):
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            archive.writestr("src/example.cs", "original")
            archive.writestr("docs/example.png", b"\x00\x01")
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.assertEqual(1, review_eval.extract_text_snapshot(stream.getvalue(), root))
            self.assertEqual("original", (root / "src" / "example.cs").read_text())
            self.assertFalse((root / "docs" / "example.png").exists())
            before = review_eval.tree_digest(root)
            (root / "src" / "example.cs").write_text("changed")
            self.assertNotEqual(before, review_eval.tree_digest(root))

    def test_snapshot_rejects_traversal_and_symlinks(self):
        for name, symlink in (("../outside.cs", False), ("..\\outside.cs", False),
                              ("/outside.cs", False), ("C:/outside.cs", False),
                              ("link.cs", True)):
            with self.subTest(name=name), tempfile.TemporaryDirectory() as temporary:
                stream = io.BytesIO()
                with zipfile.ZipFile(stream, "w") as archive:
                    entry = zipfile.ZipInfo(name)
                    if symlink:
                        entry.external_attr = 0o120777 << 16
                    archive.writestr(entry, "content")
                with self.assertRaises(ValueError):
                    review_eval.extract_text_snapshot(stream.getvalue(), Path(temporary))

    def test_scoring_preserves_unknowns_and_deduplicates_label_credit(self):
        review = {"findings": [{"severity": "high"}] * 3}
        labels = {"R1": {"present_in": ["before"], "acceptable_severities": ["high"]}}
        decisions = [
            {"finding": 0, "verdict": "supported", "labels": ["R1"], "reason": "Verified causal path"},
            {"finding": 1, "verdict": "duplicate", "labels": [], "reason": "Same root cause as finding 0"},
            {"finding": 2, "verdict": "unresolved", "labels": [], "reason": "Native timing not established"},
        ]
        result = score_eval.score_findings(review, decisions, labels, "before")
        self.assertEqual(["R1"], result["known_labels_found"])
        self.assertEqual(1 / 2, result["precision_lower_bound"])
        self.assertEqual(1, result["precision_upper_bound"])
        self.assertEqual(1, result["duplicate"])
        with self.assertRaises(ValueError):
            score_eval.score_findings(review, decisions, labels, "after")
        decisions[1] = {
            "finding": 1, "verdict": "supported", "labels": ["R1"], "reason": "Duplicate reference",
        }
        with self.assertRaises(ValueError):
            score_eval.score_findings(review, decisions, labels, "before")
        decisions[0]["reason"] = ""
        with self.assertRaises(ValueError):
            score_eval.score_findings(review, decisions, labels, "before")

    def test_empty_review_does_not_imply_perfect_precision(self):
        result = score_eval.score_findings({"findings": []}, [], {}, "clean")
        self.assertIsNone(result["precision_judged"])
        self.assertIsNone(result["precision_lower_bound"])

    def test_scoring_excludes_wrong_models_and_outside_reads(self):
        valid = dict(review_valid=True, model_verified=True, input_unchanged=True,
                     unexpected_tools=[], outside_read_attempts=[], status="completed",
                     exit_code=0, review_complete=True, budget_exhausted=False)
        self.assertTrue(score_eval.eligible_for_scoring(valid))
        for field in ("review_valid", "model_verified", "input_unchanged"):
            self.assertFalse(score_eval.eligible_for_scoring({**valid, field: False}))
        self.assertFalse(score_eval.eligible_for_scoring({**valid, "outside_read_attempts": ["..\\reference.json"]}))
        self.assertFalse(score_eval.eligible_for_scoring({**valid, "unexpected_tools": ["powershell"]}))
        for changes in ({"status": "timeout"}, {"status": "cli_error"}, {"exit_code": 1},
                        {"review_complete": False}, {"budget_exhausted": True},
                        {"outside_read_attempts": None}):
            self.assertFalse(score_eval.eligible_for_scoring({**valid, **changes}))
        self.assertTrue(score_eval.eligible_for_scoring({**valid, "soft_cap_exceeded": True}))

    def test_collection_keeps_incomplete_output_but_marks_coverage(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            review_eval.write_json(folder / "usage.json", {
                "aiCreditsStatus": "complete", "totalNanoAiu": 10**9, "currentModel": "model-a",
            })
            events = [
                {"type": "model.call_start", "data": {"model": "model-a"}},
                {"type": "assistant.message", "data": {"content": json.dumps(self.review("incomplete"))}},
            ]
            (folder / "events.jsonl").write_text(
                "\n".join(json.dumps(event) for event in events), encoding="utf-8"
            )
            result = review_eval.collect_run(folder, folder, {"model": "model-a"}, None)
            self.assertTrue(result["review_valid"])
            self.assertFalse(result["review_complete"])
            self.assertEqual(list(review_eval.DIMENSIONS), result["incomplete_dimensions"])
            self.assertEqual(self.review("incomplete"), score_eval.load(folder / "review.json"))

    def test_incomplete_or_failed_reports_remain_unscored_not_zero(self):
        scenarios = [
            ("reviewed", {}, 0),
            ("incomplete", {}, None),
            ("reviewed", {"status": "timeout"}, None),
            ("reviewed", {"exit_code": 1, "status": "cli_error"}, None),
            ("reviewed", {"budget_exhausted": True}, None),
        ]
        for coverage, changes, expected in scenarios:
            with self.subTest(coverage=coverage, changes=changes), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.experiment(root, coverage, **changes)
                with redirect_stdout(io.StringIO()):
                    score_eval.export(root, root / "adjudication")
                    score_eval.score(root, root / "adjudication")
                report = score_eval.load(root / "scored-results.json")
                self.assertEqual(expected, report["runs"][0]["runtime"]["found"])
                self.assertEqual(int(expected is not None), report["repeatability"][0]["scored_runs"])
                self.assertEqual(expected, report["repeatability"][0]["runtime_distribution"]["mean"])

    def test_postprocessing_rejects_changed_protocol_before_reading_runs(self):
        for name in ("reference", "experiment"):
            for operation in ("recover", "export", "score"):
                with self.subTest(name=name, operation=operation), tempfile.TemporaryDirectory() as temporary:
                    root = Path(temporary)
                    self.experiment(root)
                    self.assertEqual("Fixture", review_eval.verify_protocol(root)["name"])
                    path = root / f"{name}.json"
                    changed = score_eval.load(path)
                    changed["changed"] = True
                    review_eval.write_json(path, changed)
                    with self.assertRaisesRegex(ValueError, f"Frozen {name} changed"):
                        if operation == "recover":
                            review_eval.recover(root, refresh=True)
                        else:
                            getattr(score_eval, operation)(root, root / "adjudication")

    def test_prepared_schedule_cannot_exceed_or_relabel_the_configuration(self):
        config = self.config()
        config["schedule"] = review_eval.schedule(config)
        review_eval.validate_config(config)
        config["schedule"].append({**config["schedule"][0], "id": "r99"})
        with self.assertRaisesRegex(ValueError, "schedule"):
            review_eval.validate_config(config)

    def test_export_rejects_mismatched_recorded_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self.experiment(root, model="different-model")
            with self.assertRaisesRegex(ValueError, "identity"):
                score_eval.export(root, root / "adjudication")

    def test_scoring_rejects_dropped_remapped_or_modified_export(self):
        for mutation in ("drop", "remap", "packet", "result"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self.experiment(root)
                destination = root / "adjudication"
                with redirect_stdout(io.StringIO()):
                    score_eval.export(root, destination)
                    score_eval.score(root, destination)
                if mutation == "drop":
                    review_eval.write_json(destination / "blind-map.json", {})
                    review_eval.write_json(destination / "adjudications.json", {})
                elif mutation == "remap":
                    key = score_eval.load(destination / "blind-map.json")
                    key["P1"]["case"] = "different-case"
                    review_eval.write_json(destination / "blind-map.json", key)
                elif mutation == "packet":
                    review_eval.write_json(destination / "P1.json", {"case": "c1", **self.review("incomplete")})
                else:
                    result_path = root / "runs" / "r01" / "result.json"
                    result = score_eval.load(result_path)
                    result["ai_credits"] = 0
                    review_eval.write_json(result_path, result)
                with self.assertRaises(ValueError):
                    score_eval.score(root, destination)

    def test_recovery_does_not_upgrade_a_timed_out_process(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            _, result = self.experiment(root, status="timeout")
            with patch.object(review_eval, "collect_run", return_value=result), redirect_stdout(io.StringIO()):
                review_eval.recover(root, refresh=True)
            self.assertEqual("timeout", score_eval.load(root / "runs" / "r01" / "result.json")["status"])
            self.assertEqual(result, score_eval.load(root / "runs" / "r01" / "result.before-recovery.json"))

    def test_published_studies_replay_without_model_calls(self):
        for filename, count in (("2026-11-05.json", 4), ("2026-11-05-repeats.json", 20)):
            with self.subTest(filename=filename):
                data = score_eval.load(Path(__file__).parent / "results" / filename)
                labels = {label["id"]: label for label in data["reference"]["labels"]}
                records = data["reviews_and_adjudications"]
                self.assertEqual(count, len(records))
                complete = 0
                for record in records:
                    review = record.get("review", record.get("final_review"))
                    coverage_complete = all(item["status"] != "incomplete" for item in review["coverage"])
                    complete += coverage_complete
                    if "result" in record:
                        self.assertTrue(score_eval.eligible_for_scoring({
                            **record["result"], "review_complete": coverage_complete,
                        }))
                    actual = score_eval.score_findings(review, record["adjudications"], labels, record["case"])
                    matching = [
                        row for row in data["results"]["runs"]
                        if (row["id"] == record["id"] if "id" in record
                            else row["model"] == record["model"] and row["case"] == record["case"])
                    ]
                    self.assertEqual(1, len(matching))
                    self.assertEqual(matching[0]["adjudication"], actual)
                self.assertEqual(3 if count == 4 else 20, complete)
                rows = data["results"]["runs"]
                self.assertAlmostEqual(data["results"]["known_ai_credits"],
                                       sum(row["ai_credits"] for row in rows), places=5)
                if "repeatability" in data["results"]:
                    replayed = score_eval.summarize_repeats(rows, labels)
                    self.assertEqual(len(data["results"]["repeatability"]), len(replayed))
                    for expected, actual in zip(data["results"]["repeatability"], replayed):
                        expected = dict(expected)
                        self.assertAlmostEqual(expected.pop("known_ai_credits"),
                                               actual.pop("known_ai_credits"), places=8)
                        self.assertEqual(expected, actual)

    def test_repeatability_retains_failures_and_per_bug_denominators(self):
        labels = {
            label: {"category": "runtime", "present_in": ["before"]}
            for label in ("R1", "R2", "R3")
        }
        rows = []
        for index, hits in enumerate((["R1", "R2"], ["R1"], None), 1):
            decision = None if hits is None else {
                "known_labels_found": hits, "supported": len(hits), "unsupported": 0,
                "out_of_scope": 0, "duplicate": 0, "unresolved": 0, "novel_supported": 0,
            }
            rows.append({
                "id": f"r{index}", "repetition": index, "model": "model-a", "case": "before",
                "adjudication": decision, "runtime": {"found": len(hits) if hits is not None else None},
                "ai_credits": index * 10 if hits is not None else None,
                "elapsed_seconds": 100 if hits is not None else 1,
            })
        summary = score_eval.summarize_repeats(rows, labels)[0]
        self.assertEqual([2, 1, None], summary["runtime_found_per_run"])
        self.assertEqual(1.5, summary["runtime_distribution"]["mean"])
        self.assertEqual({"found": 2, "scored_runs": 2, "scheduled_runs": 3},
                         summary["per_runtime_bug"]["R1"])
        self.assertEqual(["R1", "R2"], summary["runtime_union"])
        self.assertIsNone(summary["runtime_detected_every_run"])
        self.assertEqual(30, summary["known_ai_credits"])
        self.assertEqual(["r3"], summary["unmetered_runs"])
        self.assertEqual(100, summary["latency_seconds_scored_reviews"]["mean"])
        self.assertIsNone(score_eval.distribution([None])["mean"])

    def test_complete_repetitions_do_not_mix_snapshots_or_reward_empty_reviews(self):
        labels = {"R1": {"category": "runtime", "present_in": ["before"]}}
        rows = []
        for case in ("before", "after"):
            for repetition in range(1, 6):
                hits = ["R1"] if case == "before" and repetition % 2 else []
                decision = score_eval.score_findings({"findings": []}, [], {}, case)
                decision["known_labels_found"] = hits
                rows.append({
                    "id": f"{case}{repetition}", "repetition": repetition, "model": "model-a", "case": case,
                    "adjudication": decision, "runtime": {"found": len(hits)},
                    "ai_credits": 10, "elapsed_seconds": 100,
                })
        before, after = score_eval.summarize_repeats(rows, labels)
        self.assertEqual([1, 0, 1, 0, 1], before["runtime_found_per_run"])
        self.assertEqual({"0": 2, "1": 3}, before["runtime_histogram"])
        self.assertEqual(0.6, before["runtime_distribution"]["mean"])
        self.assertEqual(1, before["runtime_distribution"]["median"])
        self.assertEqual([], before["runtime_detected_every_run"])
        self.assertEqual([], after["runtime_reference_labels"])
        self.assertEqual([0] * 5, after["runtime_found_per_run"])


if __name__ == "__main__":
    unittest.main()

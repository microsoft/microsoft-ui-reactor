"""Export masked final reviews and score explicit, evidence-backed adjudications."""

import argparse
import hashlib
import json
from pathlib import Path
import random
import statistics

from review_eval import completed_review, parse_review, validate_run_identity, verify_protocol, write_json


def load(path):
    return json.loads(path.read_text(encoding="utf-8"))


def eligible_for_scoring(result):
    return (
        completed_review(result)
        and result.get("model_verified") is True
        and result.get("input_unchanged") is True
        and result.get("unexpected_tools") == []
        and result.get("outside_read_attempts") == []
    )


def load_runs(experiment, config):
    records = {}
    hashes = {}
    for job in config["schedule"]:
        folder = experiment / "runs" / job["id"]
        result_path = folder / "result.json"
        if not result_path.is_file():
            raise ValueError("Wait for all scheduled runs before postprocessing.")
        result = load(result_path)
        validate_run_identity(result, job)
        review = None
        review_hash = None
        if result.get("review_valid") is True:
            review_path = folder / "review.json"
            review = parse_review(review_path.read_text(encoding="utf-8"))
            review_hash = hashlib.sha256(review_path.read_bytes()).hexdigest()
            result["incomplete_dimensions"] = [
                item["dimension"] for item in review["coverage"] if item["status"] == "incomplete"
            ]
            result["review_complete"] = not result["incomplete_dimensions"]
        else:
            result["review_complete"] = False
        records[job["id"]] = (result, review)
        hashes[job["id"]] = {
            "result_sha256": hashlib.sha256(result_path.read_bytes()).hexdigest(),
            "review_sha256": review_hash,
        }
    return records, hashes


def masked_map(config, records, hashes):
    jobs = list(config["schedule"])
    random.Random(config["seed"] + 97).shuffle(jobs)
    key = {}
    for job in jobs:
        result, _ = records[job["id"]]
        if not eligible_for_scoring(result):
            continue
        packet_id = f"P{len(key) + 1}"
        key[packet_id] = {
            "run": job["id"], "model": job["model"], "case": job["case"],
            "review_sha256": hashes[job["id"]]["review_sha256"],
        }
    return key


def export_state(experiment, hashes):
    return {
        "protocol_sha256": hashlib.sha256((experiment / "protocol-lock.json").read_bytes()).hexdigest(),
        "runs": hashes,
    }


def export(experiment, destination):
    config = verify_protocol(experiment)
    if destination.exists():
        raise ValueError("Refusing to overwrite an existing adjudication.")
    records, hashes = load_runs(experiment, config)
    key = masked_map(config, records, hashes)
    destination.mkdir(parents=True)
    decisions = {}
    for packet_id, entry in key.items():
        _, review = records[entry["run"]]
        write_json(destination / f"{packet_id}.json", {"case": entry["case"], **review})
        decisions[packet_id] = [
            {"finding": index, "verdict": "unresolved", "labels": [], "reason": ""}
            for index in range(len(review["findings"]))
        ]
    write_json(destination / "blind-map.json", key)
    write_json(destination / "adjudications.json", decisions)
    write_json(destination / "export-lock.json", export_state(experiment, hashes))
    print(f"Exported {len(key)} masked reviews. Fill adjudications.json before scoring.")


def score_findings(review, decisions, labels, case):
    if len(decisions) != len(review["findings"]):
        raise ValueError("Every finding requires an explicit adjudication.")
    if {decision["finding"] for decision in decisions} != set(range(len(decisions))):
        raise ValueError("Finding indices must appear exactly once.")
    counts = dict(supported=0, unsupported=0, out_of_scope=0, duplicate=0, unresolved=0)
    hits = set()
    severity_acceptable = 0
    severity_judged = 0
    novel = 0
    for decision in decisions:
        verdict = decision["verdict"]
        if verdict not in counts or not decision.get("reason", "").strip():
            raise ValueError("Every disposition needs a valid verdict and evidence/reason.")
        matched = decision.get("labels", [])
        if len(set(matched)) != len(matched):
            raise ValueError("A label cannot be matched twice by one finding.")
        if matched and verdict != "supported":
            raise ValueError("Only supported findings can receive reference-label credit.")
        for label_id in matched:
            if label_id not in labels or case not in labels[label_id]["present_in"]:
                raise ValueError(f"{label_id} is not a positive reference in {case}.")
            if label_id in hits:
                raise ValueError("Reference already credited: mark duplicate findings explicitly.")
        counts[verdict] += 1
        if verdict == "supported":
            hits.update(matched)
            if not matched:
                novel += 1
            else:
                severity_judged += 1
                severity = review["findings"][decision["finding"]]["severity"]
                severity_acceptable += all(
                    severity in labels[label_id]["acceptable_severities"] for label_id in matched
                )
    judged = counts["supported"] + counts["unsupported"] + counts["out_of_scope"]
    total = judged + counts["unresolved"]
    return {
        **counts,
        "known_labels_found": sorted(hits),
        "novel_supported": novel,
        "precision_judged": counts["supported"] / judged if judged else None,
        "precision_lower_bound": counts["supported"] / total if total else None,
        "precision_upper_bound": (counts["supported"] + counts["unresolved"]) / total if total else None,
        "severity_acceptable": severity_acceptable,
        "severity_judged": severity_judged,
    }


def distribution(values):
    observed = [value for value in values if value is not None]
    return {
        "count": len(observed),
        "mean": statistics.mean(observed) if observed else None,
        "median": statistics.median(observed) if observed else None,
        "min": min(observed) if observed else None,
        "max": max(observed) if observed else None,
    }


def summarize_repeats(rows, labels):
    groups = {}
    for row in rows:
        groups.setdefault((row["model"], row["case"]), []).append(row)
    summaries = []
    for (model, case), group in groups.items():
        group.sort(key=lambda row: (row["repetition"], row["id"]))
        scored = [row for row in group if row["adjudication"] is not None]
        runtime_labels = {
            label_id for label_id, label in labels.items()
            if label["category"] == "runtime" and case in label["present_in"]
        }
        hit_sets = [set(row["adjudication"]["known_labels_found"]) & runtime_labels for row in scored]
        counts = [row["runtime"]["found"] for row in group]
        per_bug = {
            label_id: {
                "found": sum(label_id in hits for hits in hit_sets) if scored else None,
                "scored_runs": len(scored),
                "scheduled_runs": len(group),
            }
            for label_id in sorted(runtime_labels)
        }
        credits = [row["ai_credits"] for row in group]
        summaries.append({
            "model": model,
            "case": case,
            "scheduled_runs": len(group),
            "scored_runs": len(scored),
            "runs": [row["id"] for row in group],
            "runtime_reference_labels": sorted(runtime_labels),
            "runtime_found_per_run": counts,
            "runtime_distribution": distribution(counts),
            "runtime_histogram": {
                str(count): sum(value == count for value in counts)
                for count in range(len(runtime_labels) + 1)
            },
            "per_runtime_bug": per_bug,
            "runtime_union": sorted(set().union(*hit_sets)),
            "runtime_detected_every_run": (
                sorted(set.intersection(*hit_sets)) if len(scored) == len(group) and scored else None
            ),
            "finding_outcomes": {
                name: sum(row["adjudication"][name] for row in scored)
                for name in ("supported", "unsupported", "out_of_scope", "duplicate", "unresolved", "novel_supported")
            },
            "credits_distribution": distribution(credits),
            "known_ai_credits": sum(value for value in credits if value is not None),
            "unmetered_runs": [row["id"] for row in group if row["ai_credits"] is None],
            "latency_seconds_scored_reviews": distribution([row["elapsed_seconds"] for row in scored]),
        })
    return summaries


def score(experiment, adjudication):
    config = verify_protocol(experiment)
    reference = load(experiment / "reference.json")
    labels = {label["id"]: label for label in reference["labels"]}
    records, hashes = load_runs(experiment, config)
    if load(adjudication / "export-lock.json") != export_state(experiment, hashes):
        raise ValueError("Run artifacts changed after export; create a fresh masked export.")
    key = load(adjudication / "blind-map.json")
    decisions = load(adjudication / "adjudications.json")
    if key != masked_map(config, records, hashes) or set(key) != set(decisions):
        raise ValueError("Adjudication packets do not match the frozen export.")
    by_run = {}
    for packet_id, entry in key.items():
        _, review = records[entry["run"]]
        if load(adjudication / f"{packet_id}.json") != {"case": entry["case"], **review}:
            raise ValueError(f"Masked review changed after export: {packet_id}")
        by_run[entry["run"]] = score_findings(
            review, decisions[packet_id], labels, entry["case"]
        )
    rows = []
    for job in config["schedule"]:
        result, _ = records[job["id"]]
        available = [label for label in labels.values() if job["case"] in label["present_in"]]
        assessed = by_run.get(job["id"])
        row = {
            **job, "status": result["status"], "review_valid": result.get("review_valid", False),
            "review_complete": result["review_complete"],
            "incomplete_dimensions": result.get("incomplete_dimensions"),
            "eligible_for_scoring": eligible_for_scoring(result),
            "ai_credits": result.get("ai_credits"),
            "elapsed_seconds": result["elapsed_seconds"],
            "soft_cap_exceeded": result.get("soft_cap_exceeded"),
            "model_verified": result.get("model_verified"),
            "input_unchanged": result.get("input_unchanged"),
            "review_schema_exact": result.get("review_schema_exact"),
            "schema_normalizations": result.get("schema_normalizations", []),
            "adjudication": assessed,
        }
        for category in ("runtime", "oracle", "documentation"):
            expected = {label["id"] for label in available if label["category"] == category}
            found = set(assessed["known_labels_found"]) if assessed else set()
            row[category] = {
                "found": len(expected & found) if assessed else None,
                "reference_count": len(expected),
            }
        rows.append(row)
    report = {
        "name": config["name"], "source_pr": config["source_pr"],
        "base": config["base"], "cases": config["cases"],
        "reasoning_effort": config["reasoning_effort"],
        "repetitions": config["repetitions"],
        "credits_per_run": config["credits_per_run"],
        "total_credit_cap": config["total_credits"],
        "timeout_seconds": config["timeout_seconds"],
        "reference_sha256": hashlib.sha256((experiment / "reference.json").read_bytes()).hexdigest(),
        "known_ai_credits": sum(row["ai_credits"] for row in rows if row["ai_credits"] is not None),
        "unmetered_runs": [row["id"] for row in rows if row["ai_credits"] is None],
        "runs": rows,
        "repeatability": summarize_repeats(rows, labels),
        "analysis_plan": config.get("analysis_plan"),
        "limitations": reference["scoring"]["limitations"],
    }
    write_json(experiment / "scored-results.json", report)
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command", choices=("export", "score"))
    parser.add_argument("--experiment", type=Path, required=True)
    parser.add_argument("--adjudication", type=Path, required=True)
    args = parser.parse_args()
    if args.command == "export":
        export(args.experiment.resolve(), args.adjudication.resolve())
    else:
        score(args.experiment.resolve(), args.adjudication.resolve())

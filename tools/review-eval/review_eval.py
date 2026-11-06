"""Prepare and run isolated, read-only historical PR review experiments."""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import io
import json
import math
import os
from pathlib import Path, PurePosixPath, PureWindowsPath
import random
import re
import shutil
import subprocess
import time
import zipfile


DIMENSIONS = (
    "security", "correctness", "api-ergonomics", "alternative-solution",
    "test-coverage", "docs-and-samples", "packaging",
)
READ_TOOLS = ("view", "glob", "grep")
DIMENSION_TITLES = {
    "correctness & edge-case": "correctness",
    "api & dsl ergonomics": "api-ergonomics",
    "alternative-solution": "alternative-solution",
    "test coverage": "test-coverage",
    "docs & samples sync": "docs-and-samples",
    "packaging & agent-kit impact": "packaging",
}
TEXT_SUFFIXES = {
    ".cs", ".csproj", ".props", ".targets", ".json", ".md", ".dt", ".txt",
    ".slnx", ".config", ".yml", ".yaml", ".ps1", ".py", ".ts", ".tsx",
    ".js", ".mjs", ".cjs", ".xml", ".xaml", ".resx", ".editorconfig",
}


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def git(repo: Path, *args: str) -> bytes:
    return subprocess.run(
        ["git", "-C", str(repo), "--no-pager", *args],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    ).stdout


def tree_digest(root: Path) -> str:
    digest = hashlib.sha256()
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        digest.update(path.relative_to(root).as_posix().encode("utf-8") + b"\0")
        digest.update(hashlib.sha256(path.read_bytes()).digest())
    return digest.hexdigest()


def extract_text_snapshot(archive: bytes, destination: Path) -> int:
    count = 0
    with zipfile.ZipFile(io.BytesIO(archive)) as bundle:
        for entry in bundle.infolist():
            path = PurePosixPath(entry.filename)
            if (path.is_absolute() or PureWindowsPath(entry.filename).drive
                    or ".." in path.parts or "\\" in entry.filename):
                raise ValueError(f"Unsafe archive path: {entry.filename}")
            if (entry.external_attr >> 16) & 0o170000 == 0o120000:
                raise ValueError(f"Symlink is not allowed in an eval: {entry.filename}")
            if entry.is_dir() or path.suffix.lower() not in TEXT_SUFFIXES:
                continue
            target = destination.joinpath(*path.parts)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(bundle.read(entry))
            count += 1
    if not count:
        raise ValueError("The snapshot contained no text files.")
    return count


def schedule(config: dict) -> list[dict]:
    randomizer = random.Random(config["seed"])
    jobs = []
    for repetition in range(1, config["repetitions"] + 1):
        cases = list(config["cases"])
        randomizer.shuffle(cases)
        for case in cases:
            models = list(config["models"])
            randomizer.shuffle(models)
            for model in models:
                jobs.append({
                    "id": f"r{len(jobs) + 1:02}",
                    "case": case["id"],
                    "model": model,
                    "repetition": repetition,
                })
    return jobs


def validate_config(config: dict) -> None:
    for field in ("credits_per_run", "total_credits", "timeout_seconds"):
        value = config[field]
        if value is None:
            continue
        if (not isinstance(value, (int, float)) or isinstance(value, bool)
                or not math.isfinite(value) or value <= 0):
            raise ValueError(f"{field} must be a positive finite number or explicit null.")
    for field in ("repetitions", "max_runs"):
        if type(config[field]) is not int or config[field] < 1:
            raise ValueError(f"{field} must be a positive integer.")
    cap, total = config["credits_per_run"], config["total_credits"]
    if cap is not None and cap < 30:
        raise ValueError("Copilot CLI requires at least 30 credits per run.")
    if total is not None and cap is None:
        raise ValueError("A finite total requires a per-run cap for in-flight reservations.")
    if total is not None and total < cap:
        raise ValueError("Total credits must accommodate at least one run.")
    if not config["models"] or len(set(config["models"])) != len(config["models"]):
        raise ValueError("Models must be nonempty and unique.")
    if any(not isinstance(model, str) or not model.strip() or model == "auto"
           for model in config["models"]):
        raise ValueError("Use explicit model IDs, not automatic routing.")
    ids = [case["id"] for case in config["cases"]]
    if not ids or len(set(ids)) != len(ids):
        raise ValueError("Case IDs must be nonempty and unique.")
    for value in ids:
        if not value.isalnum():
            raise ValueError("Case IDs must contain only letters and digits.")
    if config["repetitions"] * len(config["cases"]) * len(config["models"]) > config["max_runs"]:
        raise ValueError("The schedule exceeds the approved number of runs.")
    if "schedule" in config and config["schedule"] != schedule(config):
        raise ValueError("Prepared schedule does not match the approved configuration.")


def verify_protocol(output: Path) -> dict:
    lock_path = output / "protocol-lock.json"
    if not lock_path.is_file():
        raise ValueError("Missing protocol lock; cannot verify the frozen experiment.")
    locked = json.loads(lock_path.read_text(encoding="utf-8"))
    for name in ("experiment", "reference"):
        content = (output / f"{name}.json").read_bytes()
        if hashlib.sha256(content).hexdigest() != locked.get(f"{name}_sha256"):
            raise ValueError(f"Frozen {name} changed after the protocol was locked.")
    config = json.loads((output / "experiment.json").read_text(encoding="utf-8"))
    validate_config(config)
    if "schedule" not in config:
        raise ValueError("Frozen experiment has no prepared schedule.")
    return config


def validate_run_identity(result: dict, job: dict) -> None:
    if any(result.get(key) != job[key] for key in ("id", "model", "case", "repetition")):
        raise ValueError(f"Recorded run identity does not match the schedule: {job['id']}")


def completed_review(result: dict) -> bool:
    return (
        result.get("status") == "completed"
        and result.get("exit_code") == 0
        and result.get("review_valid") is True
        and result.get("review_complete") is True
        and result.get("budget_exhausted") is False
    )


def prepare(repo: Path, config_path: Path, output: Path) -> None:
    if output.resolve().is_relative_to(repo.resolve()):
        raise ValueError("Keep experiment outputs outside the source checkout.")
    config = json.loads(config_path.read_text(encoding="utf-8"))
    validate_config(config)
    config["base"] = git(repo, "rev-parse", f"{config['base']}^{{commit}}").decode().strip()
    if output.exists():
        raise ValueError(f"Refusing to overwrite an existing experiment: {output}")
    output.mkdir(parents=True)
    dimensions = repo / ".github" / "skills" / "pr-review" / "dimensions"
    shared = (dimensions / "_shared-contract.md").read_text(encoding="utf-8")
    shared = shared[shared.index("## The Team Lead Test"):]
    rubric = shared + "\n\n" + "\n\n".join(
        (dimensions / f"{name}.md").read_text(encoding="utf-8")
        for name in DIMENSIONS
    )
    rubric += """

## Evaluation output adapter

This is one primary review, not a multi-model cross-check. Consider every
dimension above. Do not delegate, run builds/tests, or use external sources.
The Markdown output instructions in the dimension fragments are replaced by
this JSON contract for this run. Return ONLY a JSON object, without fences:
{
  "findings": [
    {
      "file": "repository-relative path",
      "line_start": 1,
      "line_end": 1,
      "dimension": "one of the seven dimension names",
      "severity": "critical|high|medium|low",
      "confidence": "high|medium|low",
      "title": "specific defect",
      "evidence": "code citations and a concrete cause/effect or counterexample",
      "recommendation": "specific correction"
    }
  ],
  "coverage": [
    {
      "dimension": "one of the seven dimension names",
      "status": "reviewed|not_applicable|incomplete",
      "notes": "what was inspected or why it was not"
    }
  ]
}
Use post-change line numbers. Paths are relative to source, without a source/
prefix. Report defects introduced or left inadequately covered by this diff,
not unrelated pre-existing defects. Missing evidence is a limitation, not a
finding. There is no target finding count. Do not claim to have executed tests.
"""
    prompt = (
        "Review the proposed Reactor change in change.diff. The corresponding "
        "post-change source snapshot is under source. Read "
        "source\\AGENTS.md for repository context and review-rubric.txt for the "
        "seven review dimensions and required final JSON format. Source coverage "
        "is described in source-scope.json; files outside that export are not "
        "evidence of missing repository files. Inspect supporting code as needed. "
        "Do not inspect parent directories, other cases, Git history, reviews, "
        "or evaluation metadata. Stay within this working directory. "
        "Do not edit files. Return the final review, not a plan."
    )
    records = []
    for case in config["cases"]:
        root = output / "cases" / case["id"]
        root.mkdir(parents=True)
        revision = git(repo, "rev-parse", f"{case['revision']}^{{commit}}").decode().strip()
        archive = git(repo, "archive", "--format=zip", revision, "--", *config["source_paths"])
        count = extract_text_snapshot(archive, root / "source")
        (root / "change.diff").write_bytes(
            git(repo, "diff", "--no-ext-diff", "--no-textconv", "--no-renames", config["base"], revision, "--")
        )
        (root / "review-rubric.txt").write_text(rubric, encoding="utf-8")
        write_json(root / "source-scope.json", {
            "paths": config["source_paths"],
            "file_suffixes": sorted(TEXT_SUFFIXES),
            "note": "Historical source text only; no Git history or repository automation.",
        })
        records.append({
            **case, "revision": revision, "text_files": count,
            "sha256": tree_digest(root),
        })
    write_json(output / "experiment.json", {
        **config, "cases": records, "schedule": schedule(config), "prompt": prompt,
        "rubric_sha256": hashlib.sha256(rubric.encode("utf-8")).hexdigest(),
        "prepared_from": git(repo, "rev-parse", "HEAD").decode().strip(),
    })
    print(json.dumps({"prepared": str(output), "cases": records}, indent=2))


def credits_from_usage(usage: dict) -> float:
    # Copilot SDK usage-and-billing.md defines 1 AI credit as 1e9 nano-AIU.
    if usage.get("aiCreditsStatus") != "complete":
        raise ValueError("CLI AI-credit accounting is not complete.")
    value = usage.get("totalNanoAiu")
    if (not isinstance(value, (int, float)) or isinstance(value, bool)
            or not math.isfinite(value) or value < 0):
        raise ValueError("Missing or invalid totalNanoAiu in CLI usage export.")
    return value / 1e9


def parse_review(content: str) -> dict:
    fenced = re.fullmatch(r"\s*```(?:json)?\s*\n(.*)\n```\s*", content, re.DOTALL)
    payload = (fenced.group(1) if fenced else content).strip()
    normalizations = ["Removed JSON display fence"] if fenced else []
    if not payload.startswith(("{", "[")) and (start := payload.find("{")) > 0:
        payload = payload[start:]
        normalizations.append("Removed prose before JSON report")
    review = json.loads(payload)
    if not isinstance(review, dict) or not isinstance(review.get("findings"), list):
        raise ValueError("Final answer does not contain a findings array.")
    coverage = review.get("coverage")
    if not isinstance(coverage, list):
        raise ValueError("Final answer must contain a coverage array.")
    for item in [*coverage, *review["findings"]]:
        if not isinstance(item, dict) or not isinstance(item.get("dimension"), str):
            raise ValueError("Each item must name a review dimension.")
        original = item["dimension"]
        lower = original.lower()
        canonical = lower if lower in DIMENSIONS else DIMENSION_TITLES.get(lower)
        if canonical is None:
            raise ValueError(f"Unknown dimension: {original}")
        if canonical != original:
            normalizations.append(f"Dimension '{original}' -> '{canonical}'")
            item["dimension"] = canonical
    if (not isinstance(coverage, list) or len(coverage) != len(DIMENSIONS)
            or {item.get("dimension") for item in coverage if isinstance(item, dict)} != set(DIMENSIONS)):
        raise ValueError("Final answer must cover all seven dimensions exactly once.")
    for item in coverage:
        if item.get("status") not in {"reviewed", "not_applicable", "incomplete"}:
            raise ValueError("Invalid coverage status.")
    for finding in review["findings"]:
        if not isinstance(finding, dict):
            raise ValueError("Each finding must be an object.")
        if finding.get("dimension") not in DIMENSIONS:
            raise ValueError("Invalid finding dimension.")
        if finding.get("severity") not in {"critical", "high", "medium", "low"}:
            raise ValueError("Invalid finding severity.")
        if finding.get("confidence") not in {"high", "medium", "low"}:
            raise ValueError("Invalid finding confidence.")
        for field in ("file", "title", "evidence", "recommendation"):
            if not isinstance(finding.get(field), str) or not finding[field].strip():
                raise ValueError(f"Missing finding field: {field}")
        start, end = finding.get("line_start"), finding.get("line_end")
        if type(start) is not int or type(end) is not int or start < 1 or end < start:
            raise ValueError("Invalid finding line range.")
    if normalizations:
        review["_schema_normalizations"] = normalizations
    return review


def collect_run(folder: Path, cwd: Path, job: dict, cap: float | None) -> dict:
    usage = json.loads((folder / "usage.json").read_text(encoding="utf-8"))
    credits = credits_from_usage(usage)
    events = [json.loads(line) for line in (folder / "events.jsonl").read_text(
        encoding="utf-8").splitlines() if line.strip()]
    models = sorted({
        event["data"]["model"] for event in events
        if event.get("type") == "model.call_start" and event.get("data", {}).get("model")
    })
    tools = [event["data"] for event in events if event.get("type") == "tool.execution_start"]
    tool_names = sorted({tool["toolName"] for tool in tools})
    outside_reads = []
    for tool in tools:
        args = tool.get("arguments", {})
        paths = args.get("paths", args.get("path", []))
        if isinstance(paths, str):
            paths = [paths]
        for path in paths or []:
            resolved = (cwd / path).resolve()
            if not resolved.is_relative_to(cwd.resolve()):
                outside_reads.append(path)
    record = {
        "ai_credits": credits,
        "effective_models": models,
        "model_verified": models == [job["model"]] and usage.get("currentModel") == job["model"],
        "tool_names": tool_names,
        "tool_calls": len(tools),
        "unexpected_tools": sorted(set(tool_names) - {*READ_TOOLS, "rg"}),
        "outside_read_attempts": outside_reads,
        "soft_cap_exceeded": credits > cap if cap is not None else None,
        "api_duration_ms": usage.get("totalApiDurationMs"),
        "token_details": usage.get("tokenDetails"),
        "limit_messages": [
            event["data"]["message"] for event in events
            if event.get("type") == "session.info"
            and event.get("data", {}).get("infoType") == "session_limits"
        ],
    }
    record["budget_exhausted"] = any(
        "Session limit reached" in message for message in record["limit_messages"]
    )
    messages = [event["data"].get("content", "") for event in events
                if event.get("type") == "assistant.message"]
    try:
        review = parse_review(messages[-1] if messages else "")
    except ValueError as error:
        record.update(review_valid=False, review_complete=False, review_error=str(error))
    else:
        normalizations = review.pop("_schema_normalizations", [])
        record["review_schema_exact"] = not normalizations
        record["schema_normalizations"] = normalizations
        write_json(folder / "review.json", review)
        record["review_valid"] = True
        record["incomplete_dimensions"] = [
            item["dimension"] for item in review["coverage"] if item["status"] == "incomplete"
        ]
        record["review_complete"] = not record["incomplete_dimensions"]
    return record


def recover(output: Path, refresh: bool = False) -> None:
    config = verify_protocol(output)
    for job in config["schedule"]:
        folder = output / "runs" / job["id"]
        result_path = folder / "result.json"
        if not result_path.exists():
            continue
        result = json.loads(result_path.read_text(encoding="utf-8"))
        validate_run_identity(result, job)
        if "metering_error" not in result and not refresh:
            continue
        data = collect_run(folder, output / "cases" / job["case"], job, config["credits_per_run"])
        backup = folder / "result.before-recovery.json"
        if not backup.exists():
            write_json(backup, result)
        result.pop("metering_error", None)
        result.pop("review_error", None)
        result.update(data)
        if result["exit_code"] == 0 and result["status"] not in {"timeout", "cli_error"}:
            result["status"] = ("budget_exhausted" if result["budget_exhausted"] else
                                "completed" if result["review_complete"] else "incomplete_review")
        write_json(result_path, result)
        print(f"Recovered {job['id']}: credits={data['ai_credits']}, "
              f"model_verified={data['model_verified']}, review_valid={data['review_valid']}")


def stop_process_tree(pid: int) -> None:
    script = """
function Stop-OwnedTree([int] $TargetId) {
    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $TargetId")
    foreach ($child in $children) { Stop-OwnedTree $child.ProcessId }
    if (Get-Process -Id $TargetId -ErrorAction Ignore) {
        Stop-Process -Id $TargetId -Force -ErrorAction Stop
    }
}
Stop-OwnedTree """ + str(pid)
    subprocess.run(["pwsh", "-NoProfile", "-Command", script], check=True,
                   stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def cli_command(executable: str, config: dict, job: dict, folder: Path) -> list[str]:
    cap = config["credits_per_run"]
    limit_args = ["--max-ai-credits", str(cap)] if cap is not None else []
    return [
        executable, "--no-auto-update", "--model", job["model"],
        "--reasoning-effort", config["reasoning_effort"], "--context", "default",
        "--mode", "interactive", "--no-custom-instructions", "--no-ask-user",
        "--disable-builtin-mcps", "--disable-client-telemetry", "--sync-session=off",
        "--available-tools", *READ_TOOLS, "--allow-all-tools",
        "--deny-tool=shell", "--deny-tool=write", "--deny-tool=url",
        "--disallow-temp-dir", "--trust-folder", "--no-color", "--stream=off",
        *limit_args,
        "--output-format=json", "--usage-output-file", str(folder / "usage.json"),
        "--log-dir", str(folder / "logs"), "--log-level=info",
        "-p", config["prompt"],
    ]


def can_start_run(config: dict, used: float, running: int) -> bool:
    total = config["total_credits"]
    return total is None or used + (running + 1) * config["credits_per_run"] <= total


def run_one(output: Path, config: dict, job: dict, token: str, executable: str) -> dict:
    folder = output / "runs" / job["id"]
    folder.mkdir(parents=True, exist_ok=False)
    case = next(case for case in config["cases"] if case["id"] == job["case"])
    cwd = output / "cases" / case["id"]
    if tree_digest(cwd) != case["sha256"]:
        raise ValueError(f"Input snapshot changed: {job['case']}")
    env = {
        key: value for key, value in os.environ.items()
        if not key.upper().startswith(("COPILOT_", "OTEL_"))
        and key.upper() not in {"GH_TOKEN", "GITHUB_TOKEN"}
    }
    env.update({
        "COPILOT_HOME": str(folder / "home"),
        "COPILOT_GITHUB_TOKEN": token,
        "COPILOT_OTEL_ENABLED": "false",
        "USE_TGREP": "false",
    })
    args = cli_command(executable, config, job, folder)
    invocation = {
        **job, "args": args, "cwd": str(cwd), "input_sha256": case["sha256"],
        "started_at": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
    }
    write_json(folder / "invocation.json", invocation)
    started = time.monotonic()
    status = "completed"
    with (folder / "events.jsonl").open("wb") as stdout, (folder / "stderr.txt").open("wb") as stderr:
        process = subprocess.Popen(args, cwd=cwd, env=env, stdin=subprocess.DEVNULL,
                                   stdout=stdout, stderr=stderr)
        invocation["pid"] = process.pid
        write_json(folder / "invocation.json", invocation)
        try:
            exit_code = process.wait(timeout=config["timeout_seconds"])
        except subprocess.TimeoutExpired:
            stop_process_tree(process.pid)
            exit_code = process.wait(timeout=30)
            status = "timeout"
        except BaseException:
            stop_process_tree(process.pid)
            process.wait(timeout=30)
            raise
    if status == "completed" and exit_code != 0:
        status = "cli_error"
    result = {
        **job, "status": status, "exit_code": exit_code,
        "elapsed_seconds": round(time.monotonic() - started, 3),
        "input_unchanged": tree_digest(cwd) == case["sha256"],
    }
    try:
        result.update(collect_run(folder, cwd, job, config["credits_per_run"]))
    except (OSError, ValueError) as error:
        result["metering_error"] = str(error)
    if result["status"] == "completed" and result.get("budget_exhausted"):
        result["status"] = "budget_exhausted"
    elif result["status"] == "completed" and not result.get("review_complete", False):
        result["status"] = "incomplete_review"
    write_json(folder / "result.json", result)
    return result


def run(output: Path, max_starts: int, workers: int) -> None:
    experiment_path = output / "experiment.json"
    config = json.loads(experiment_path.read_text(encoding="utf-8"))
    validate_config(config)
    reference = output / "reference.json"
    if not reference.is_file():
        raise ValueError("Freeze the held-out reference labels before starting model runs.")
    executable = shutil.which("copilot")
    if not executable:
        raise RuntimeError("Copilot CLI is not installed.")
    version = subprocess.run([executable, "--no-auto-update", "--version"], check=True,
                             stdout=subprocess.PIPE, text=True).stdout.strip()
    lock_path = output / "protocol-lock.json"
    protocol = {
        "cli_version": version,
        "reference_sha256": hashlib.sha256(reference.read_bytes()).hexdigest(),
        "experiment_sha256": hashlib.sha256(experiment_path.read_bytes()).hexdigest(),
    }
    if lock_path.exists():
        if json.loads(lock_path.read_text(encoding="utf-8")) != protocol:
            raise ValueError("CLI version, reference labels or experiment changed during the run.")
    else:
        runs = output / "runs"
        if runs.exists() and any(runs.iterdir()):
            raise ValueError("Missing protocol lock for existing runs; refusing to replace it.")
        write_json(lock_path, protocol)
    verify_protocol(output)
    token_result = subprocess.run(["gh", "auth", "token"], check=True,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    token = token_result.stdout.strip()
    if not token:
        raise RuntimeError("GitHub CLI supplied no authentication token.")
    used = 0.0
    pending = []
    for job in config["schedule"]:
        folder = output / "runs" / job["id"]
        if folder.exists():
            result_path = folder / "result.json"
            if not result_path.exists():
                raise ValueError(f"Unfinished run needs recovery; refusing to rerun: {job['id']}")
            result = json.loads(result_path.read_text(encoding="utf-8"))
            validate_run_identity(result, job)
            if "ai_credits" not in result:
                raise ValueError(f"Unmetered run needs recovery: {job['id']}")
            if (not result.get("model_verified") or result.get("unexpected_tools")
                    or result.get("outside_read_attempts") or not result["input_unchanged"]):
                raise ValueError(f"Run failed isolation or model verification: {job['id']}")
            if not completed_review(result):
                raise ValueError(f"Incomplete or failed run needs inspection/recovery: {job['id']}")
            used += result["ai_credits"]
        else:
            pending.append(job)
    started = 0
    halted = False
    with concurrent.futures.ThreadPoolExecutor(max_workers=workers) as pool:
        running = {}
        while pending or running:
            if (output / "pause.flag").exists():
                halted = True
            while pending and not halted and len(running) < workers and started < max_starts:
                if not can_start_run(config, used, len(running)):
                    break
                job = pending.pop(0)
                future = pool.submit(run_one, output, config, job, token, executable)
                running[future] = job
                started += 1
                print(f"START {job['id']} {job['case']} {job['model']} repeat={job['repetition']}", flush=True)
            if not running:
                break
            done, _ = concurrent.futures.wait(running, return_when=concurrent.futures.FIRST_COMPLETED)
            for future in done:
                running.pop(future)
                result = future.result()
                print("FINISH " + json.dumps({
                    key: result.get(key) for key in
                    ("id", "case", "model", "status", "elapsed_seconds", "ai_credits",
                     "model_verified", "review_valid", "soft_cap_exceeded", "metering_error")
                }), flush=True)
                if ("ai_credits" not in result or not result["input_unchanged"]
                        or not result.get("model_verified") or result.get("unexpected_tools")
                        or result.get("outside_read_attempts") or not completed_review(result)):
                    halted = True
                if "ai_credits" in result:
                    used += result["ai_credits"]
    print(json.dumps({"started": started, "known_ai_credits": used,
                      "remaining": len(pending), "halted": halted}), flush=True)
    if halted:
        raise RuntimeError("Evaluation paused: inspect incomplete reports, accounting, integrity, or pause.flag before resuming.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare")
    prep.add_argument("--repo", type=Path, default=Path.cwd())
    prep.add_argument("--config", type=Path, required=True)
    prep.add_argument("--output", type=Path, required=True)
    execute = commands.add_parser("run")
    execute.add_argument("--output", type=Path, required=True)
    execute.add_argument("--max-starts", type=int, default=1)
    execute.add_argument("--workers", type=int, default=1)
    recovery = commands.add_parser("recover")
    recovery.add_argument("--output", type=Path, required=True)
    recovery.add_argument("--refresh", action="store_true",
                          help="Re-extract completed-run metadata without repeating inference.")
    args = parser.parse_args()
    if args.command == "prepare":
        prepare(args.repo.resolve(), args.config.resolve(), args.output.resolve())
    elif args.command == "recover":
        recover(args.output.resolve(), args.refresh)
    else:
        if args.max_starts < 1 or args.workers < 1:
            parser.error("--max-starts and --workers must be positive.")
        run(args.output.resolve(), args.max_starts, args.workers)


if __name__ == "__main__":
    main()

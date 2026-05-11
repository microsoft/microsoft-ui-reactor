#!/usr/bin/env python3
"""Compute per-build CS-prefixed diagnostic count distribution from a
ranker-labels.jsonl file. Validates whether the spec 038 §14 #8 gate
default (3) sits sensibly against the empirical typical-failure shape.

A "build" is one (run_id, turn) pair: each turn the agent emits one fix,
the harness re-builds, and labels every surfaced diagnostic with whether
that fix addressed it. The diagnostic-count gate counts unique CS codes
in a single `mur check` invocation — same shape as a single build.
"""
from __future__ import annotations
import json, sys, statistics
from collections import Counter, defaultdict

path = sys.argv[1]

# (run_id, turn) -> set of unique (file, line, col, code)
builds = defaultdict(set)
total_rows = 0
non_cs = 0
unique_codes = Counter()

with open(path, "r", encoding="utf-8") as fh:
    for line in fh:
        line = line.strip()
        if not line:
            continue
        total_rows += 1
        row = json.loads(line)
        diag = row["diag"]
        code = diag["code"]
        unique_codes[code] += 1
        if not code.startswith("CS"):
            non_cs += 1
            continue
        key = (row["run_id"], row["turn"])
        builds[key].add((diag.get("file", ""), diag.get("line", 0), diag.get("col", 0), code))

counts = sorted(len(v) for v in builds.values())
n = len(counts)
print(f"ranker rows total           : {total_rows}")
print(f"  non-CS-prefixed (e.g. REACTOR_*) : {non_cs}")
print(f"distinct (run_id, turn) builds with >= 1 CS diag : {n}")
print()
print(f"CS-diagnostics-per-build distribution (n={n}):")
print(f"  min      : {counts[0]}")
print(f"  p25      : {counts[n // 4]}")
print(f"  median   : {statistics.median(counts):.1f}")
print(f"  p75      : {counts[3 * n // 4]}")
print(f"  p90      : {counts[int(0.90 * n)]}")
print(f"  p95      : {counts[int(0.95 * n)]}")
print(f"  max      : {counts[-1]}")
print(f"  mean     : {statistics.mean(counts):.2f}")
print()
# Bucket frequencies
buckets = Counter()
for c in counts:
    buckets[c] += 1
print("histogram (count -> builds):")
for k in sorted(buckets.keys()):
    bar = "#" * min(60, buckets[k])
    pct = 100.0 * buckets[k] / n
    print(f"  {k:>3} : {buckets[k]:>4}  ({pct:5.1f}%)  {bar}")
print()

# Gate evaluation at common thresholds
print("Gate evaluation — share of builds that would EMIT (count >= T):")
for T in [1, 2, 3, 4, 5]:
    emit = sum(1 for c in counts if c >= T)
    print(f"  T = {T}: {emit:>4} / {n} builds emit ({100.0 * emit / n:.1f}%)")
print()

# Top non-CS codes
print("top 15 codes by row count (informational):")
for code, c in unique_codes.most_common(15):
    print(f"  {code:<10} {c}")

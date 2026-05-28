"""Spec 047 §14 Phase 2 (Q1 spike) — aggregate launch-N.jsonl into a means
+ 95% CI table per (bench, variant), and emit the Q1 decision-matrix deltas
(ReactorDescriptors vs ReactorV2, ReactorDescriptors vs ReactorToday).

Usage:  python aggregate.py    # reads launch-*.jsonl in CWD
"""
import glob
import json
import math
import statistics
from collections import defaultdict


def main():
    rows = []
    for path in sorted(glob.glob("launch-*.jsonl")):
        with open(path, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line:
                    continue
                row = json.loads(line)
                if row.get("status") != "ok":
                    continue
                rows.append(row)

    # Group by (benchId, variant).
    buckets = defaultdict(list)
    for r in rows:
        buckets[(r["benchId"], r["variant"])].append(r)

    benches = sorted({b for (b, _) in buckets}, key=_bench_key)
    variants = ["ReactorToday", "ReactorV2", "ReactorDescriptors"]

    def summarize(rs, key):
        vals = [r[key] for r in rs]
        if not vals:
            return (math.nan, math.nan, 0)
        mean = statistics.mean(vals)
        if len(vals) > 1:
            stdev = statistics.stdev(vals)
            # 95% CI half-width for a t-distribution. For n=15 dof=14, t ≈ 2.145.
            # Approximate with 1.96 for simplicity — close enough at n≥10.
            ci_half = 1.96 * stdev / math.sqrt(len(vals))
        else:
            ci_half = math.nan
        return mean, ci_half, len(vals)

    # ── Per-(bench, variant) summary table. ──
    print("# Per-(bench, variant) means")
    print()
    print(f"| Bench | Variant | n | Mean ns | 95% CI ±ns | Mean alloc B | 95% CI ±B |")
    print(f"|---|---|---:|---:|---:|---:|---:|")
    for b in benches:
        for v in variants:
            rs = buckets.get((b, v), [])
            mean_ns, ci_ns, n = summarize(rs, "meanNs")
            mean_b, ci_b, _ = summarize(rs, "allocBytes")
            if n == 0:
                print(f"| {b} | {v} | 0 | — | — | — | — |")
            else:
                print(
                    f"| {b} | {v} | {n} | {mean_ns:,.0f} | {ci_ns:,.0f} "
                    f"| {mean_b:,.0f} | {ci_b:,.0f} |"
                )
        print(f"| | | | | | | |")

    # ── Q1 decision-matrix deltas. ──
    print()
    print("# Q1 head-to-head — ReactorDescriptors deltas")
    print()
    print(
        "| Bench | vs ReactorV2 ns | vs ReactorV2 alloc | vs ReactorToday ns | vs ReactorToday alloc | Q1 band |"
    )
    print("|---|---:|---:|---:|---:|---|")
    for b in benches:
        ds = buckets.get((b, "ReactorDescriptors"), [])
        v2 = buckets.get((b, "ReactorV2"), [])
        today = buckets.get((b, "ReactorToday"), [])
        d_ns, _, _ = summarize(ds, "meanNs")
        d_b, _, _ = summarize(ds, "allocBytes")
        v_ns, _, _ = summarize(v2, "meanNs")
        v_b, _, _ = summarize(v2, "allocBytes")
        t_ns, _, _ = summarize(today, "meanNs")
        t_b, _, _ = summarize(today, "allocBytes")

        def pct(a, base):
            if base and not math.isnan(base) and not math.isnan(a):
                return (a - base) / base * 100.0
            return math.nan

        vs_v2_ns = pct(d_ns, v_ns)
        vs_v2_b = pct(d_b, v_b)
        vs_t_ns = pct(d_ns, t_ns)
        vs_t_b = pct(d_b, t_b)

        # §13 Q1 matrix bands keyed off the worst of ns vs V2.
        worst = vs_v2_ns
        if math.isnan(worst):
            band = "-"
        elif abs(worst) <= 5:
            band = "<=5%: ship descriptors"
        elif abs(worst) <= 15:
            band = "5-15%: judgment call"
        else:
            band = ">15%: ship hand-coded"

        print(
            f"| {b} | {vs_v2_ns:+.1f}% | {vs_v2_b:+.1f}% | {vs_t_ns:+.1f}% | {vs_t_b:+.1f}% | {band} |"
        )


def _bench_key(s):
    # M1, M2, ..., M13 — sort numerically.
    try:
        return int(s.lstrip("M"))
    except ValueError:
        return 999


if __name__ == "__main__":
    main()

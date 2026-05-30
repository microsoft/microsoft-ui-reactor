#!/usr/bin/env python3
"""Spec-047 §15.6 comparison: post-Phase-4 capture vs 2026-05-25 ARM64 baseline.

Computes per-render mean (allocBytes/iterations) and per-op meanNs for each
(benchId, variant), averaged across reps. Maps baseline 'ReactorV2' and current
'Reactor' to a common 'Reactor' label (same code lineage — the post-047 V1 path).
"""
import json, glob, sys, statistics, os

BASE = "docs/specs/047/baseline-results/LAPTOP-4MEP83VI/2026-05-25-arm64"
NEW  = "perf-capture-2026-05-29-arm64"

def load(files):
    rows = []
    for f in files:
        with open(f) as fh:
            for line in fh:
                line = line.strip()
                if line:
                    rows.append(json.loads(line))
    return rows

def norm_variant(v):
    return "Reactor" if v in ("Reactor", "ReactorV2") else v

def agg(rows):
    # key (bench, variant) -> dict with per-render alloc list, ns list
    g = {}
    for r in rows:
        if r.get("status") != "ok":
            continue
        b = r["benchId"]; v = norm_variant(r["variant"]); it = r["iterations"]
        key = (b, v)
        d = g.setdefault(key, {"alloc": [], "ns": [], "iters": it})
        d["alloc"].append(r["allocBytes"] / it)   # per-render bytes
        d["ns"].append(r["meanNs"])               # per-op ns
    out = {}
    for key, d in g.items():
        out[key] = {
            "alloc": statistics.mean(d["alloc"]),
            "ns": statistics.mean(d["ns"]),
            "iters": d["iters"],
            "n": len(d["alloc"]),
        }
    return out

base = agg(load(sorted(glob.glob(f"{BASE}/perfbench-controlmodel*.jsonl"))))
new  = agg(load(sorted(glob.glob(f"{NEW}/perfbench-controlmodel*.jsonl"))))

benches = ["M1","M2","M3","M4","M5","M6","M7","M8","M9","M10","M11","M12","M13"]

def pct(new_v, old_v):
    if old_v == 0: return "n/a"
    return f"{(new_v-old_v)/old_v*100:+.1f}%"

def g(tbl, b, v, field):
    r = tbl.get((b, v))
    return r[field] if r else None

print("# Spec-047 §15.6 — Post-Phase-4 capture vs 2026-05-25 ARM64 baseline (LAPTOP-4MEP83VI)\n")
print("Per-render allocated bytes (allocBytes / iterations), mean across 5 reps.\n")

# ---- ALLOC table ----
print("## Per-render ALLOC bytes\n")
print("| Bench | Direct (new) | Direct (base) | Reactor (new) | Reactor=V2 (base) | new vs base-V2 | Today (base) | new-Reactor vs base-Today |")
print("|---|---:|---:|---:|---:|---:|---:|---:|")
for b in benches:
    dn = g(new,b,"Direct","alloc"); db = g(base,b,"Direct","alloc")
    rn = g(new,b,"Reactor","alloc"); rb = g(base,b,"Reactor","alloc")
    tb = g(base,b,"ReactorToday","alloc")
    def f(x): return f"{x:,.0f}" if x is not None else "-"
    vV2 = pct(rn, rb) if (rn is not None and rb) else "-"
    vTd = pct(rn, tb) if (rn is not None and tb) else "-"
    print(f"| {b} | {f(dn)} | {f(db)} | {f(rn)} | {f(rb)} | {vV2} | {f(tb)} | {vTd} |")

# ---- NS table ----
print("\n## Per-op mean NS\n")
print("| Bench | Direct (new) | Reactor (new) | Reactor=V2 (base) | new vs base-V2 | Today (base) | new-Reactor vs base-Today |")
print("|---|---:|---:|---:|---:|---:|---:|")
for b in benches:
    dn = g(new,b,"Direct","ns")
    rn = g(new,b,"Reactor","ns"); rb = g(base,b,"Reactor","ns")
    tb = g(base,b,"ReactorToday","ns")
    def f(x): return f"{x:,.0f}" if x is not None else "-"
    vV2 = pct(rn, rb) if (rn is not None and rb) else "-"
    vTd = pct(rn, tb) if (rn is not None and tb) else "-"
    print(f"| {b} | {f(dn)} | {f(rn)} | {f(rb)} | {vV2} | {f(tb)} | {vTd} |")

# ---- Byte gate ----
print("\n## §11.6 byte-gate (per-render Reactor-new vs target)\n")
gates = {"M1":407,"M2":1520,"M3":19200}
print("| Bench | Target | Reactor (new) per-render | Pass? |")
print("|---|---:|---:|:---:|")
for b,t in gates.items():
    rn = g(new,b,"Reactor","alloc")
    ok = "✅" if (rn is not None and rn <= t) else "❌"
    print(f"| {b} | ≤{t:,} | {rn:,.0f} | {ok} |" if rn is not None else f"| {b} | ≤{t:,} | - | - |")

# ---- env / sanity ----
print("\n## Capture coverage\n")
for b in benches:
    have = [v for v in ("Direct","ReactorToday","Reactor") if (b,v) in new]
    print(f"- {b}: {', '.join(have) if have else 'MISSING'}  (iters={g(new,b,'Reactor','iters')})")
